// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Collects lyrics from syntax tree and associates them with notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-engraver.cc:60-88 Lyric_engraver::process_music
/// LILYPOND-REF: lily/lyric-combine-music-iterator.cc:1-200
///
/// Lyrics are associated with notes by position. Each syllable takes one note the voice
/// is not in a melisma at (a slur or tie holds the previous syllable — see
/// LyricsCollector.BuildNoteIndices). Hyphens (--) indicate word continuation, extenders
/// (__) draw the melisma's line and take no note, `_` / detached `~` take one note.
/// </remarks>
/// <summary>
/// A lyric line's overflow: how many syllables ran past the notes, and where
/// the FIRST of them sits — its text, source offset, and 1-based bar number
/// within the lyric line — plus how many notes of that bar a slur and a tie held
/// (they take no syllable), which is usually WHY the bar ran out.
/// </summary>
public readonly record struct LyricOverflow(
    int Count, string FirstText, int FirstPosition, int FirstBar,
    int SlurHeldInFirstBar = 0, int TieHeldInFirstBar = 0);

internal sealed class LyricCollector
{
    /// <summary>One note a syllable can take, and the last note of the melisma that runs on
    /// from it (a slur or tie — the busy notes after it), or null when none does.</summary>
    private sealed class Slot(int measureIndex, int itemIndex, LilySharp.Core.Semantics.Fraction timing)
    {
        public int MeasureIndex { get; } = measureIndex;
        public int ItemIndex { get; } = itemIndex;
        public LilySharp.Core.Semantics.Fraction Timing { get; } = timing;
        public (int MeasureIndex, LilySharp.Core.Semantics.Fraction Timing)? HeldEnd { get; set; }

        /// <summary>The last note a syllable on this slot holds: the melisma's end, else
        /// the slot's own note.</summary>
        public (int MeasureIndex, LilySharp.Core.Semantics.Fraction Timing) End
            => HeldEnd ?? (MeasureIndex, Timing);
    }

    /// <summary>
    /// Collects lyrics from a LyricsBlockSyntax.
    /// </summary>
    /// <param name="lyricsBlock">The lyrics block syntax node.</param>
    /// <param name="noteItemIndices">
    /// List of (measureIndex, itemIndex) tuples for notes in the associated voice.
    /// Each syllable is matched to a note in order.
    /// </param>
    /// <param name="voiceId">Voice ID to associate with these lyrics.</param>
    /// <param name="verseNumber">Verse number (1-based) for multiple lyric lines.</param>
    /// <param name="overflow">
    /// Non-null when real syllables were left over after the notes ran out: the
    /// count, plus the FIRST dropped syllable's text, source offset and 1-based
    /// bar number within the lyric line — so the warning can point at the exact
    /// word where the miscount starts. Melisma/extender markers do not count
    /// (they consume no note), so a line shorter than the melody never reports
    /// overflow.
    /// </param>
    /// <returns>List of LyricItem objects.</returns>
    public ImmutableArray<LyricItem> Collect(
        LyricsBlockSyntax lyricsBlock,
        IReadOnlyList<(int MeasureIndex, int ItemIndex, LilySharp.Core.Semantics.Fraction Timing, bool Busy, bool TieHeld)> noteItemIndices,
        out LyricOverflow? overflow,
        int voiceId = 0,
        int verseNumber = 1,
        bool hideStanza = false)
        => Collect(lyricsBlock.Syllables, noteItemIndices, out overflow, voiceId, verseNumber, hideStanza);

    /// <summary>Collects lyrics from an explicit set of lyric-measure nodes (the
    /// whole block, or one part-major inner section's measures).</summary>
    public ImmutableArray<LyricItem> Collect(
        IEnumerable<SyntaxNode> syllableMeasures,
        IReadOnlyList<(int MeasureIndex, int ItemIndex, LilySharp.Core.Semantics.Fraction Timing, bool Busy, bool TieHeld)> noteItemIndices,
        out LyricOverflow? overflow,
        int voiceId = 0,
        int verseNumber = 1,
        bool hideStanza = false,
        int baseMeasureIndex = 0)
    {
        var lyrics = ImmutableArray.CreateBuilder<LyricItem>();
        var syllables = ParseSyllablesFrom(syllableMeasures);
        overflow = null;
        int unplacedSyllableCount = 0;
        string firstDroppedText = "";
        int firstDroppedPosition = 0, firstDroppedBar = 0;
        int firstDroppedSlurHeld = 0, firstDroppedTieHeld = 0;

        // Group the verse's notes by measure INDEX (relative to the run's first bar). A
        // written "|" advances by exactly ONE bar, so a measure with NO notes — a whole-bar
        // rest, or an empty "| |" lyric bar — still occupies a slot. Indexing by the note's
        // measure (not merely opening a new group on each change) keeps a rest-only bar in
        // the count, so a leading "| " lines a verse up right after an r1 pickup; grouping
        // by change used to COLLAPSE that bar and shift the whole verse over. Syllables that
        // run PAST the last bar WRAP into the next stacked verse (1番, 2番, … in one block).
        // ⚠️ A BUSY note (inside a slur or at the end of a tie — LyricsCollector.
        // BuildNoteIndices) is no slot: it joins the melisma of the slot before it, whose
        // HeldEnd it becomes — across a bar line too, where a slur or tie crosses one.
        var measures = new List<List<Slot>>();
        // How many notes of each bar a slur / a tie held (they are no slot) — reported with an
        // overflow, since a long slur, or a tied note still given a syllable by a `~`, is the
        // usual reason the bar ran out.
        var slurHeldPerMeasure = new List<int>();
        var tieHeldPerMeasure = new List<int>();
        Slot? lastSlot = null;
        foreach (var n in noteItemIndices)
        {
            int local = n.MeasureIndex - baseMeasureIndex;
            if (local < 0)
                continue;
            while (measures.Count <= local)
            {
                measures.Add(new List<Slot>());
                slurHeldPerMeasure.Add(0);
                tieHeldPerMeasure.Add(0);
            }
            if (n.Busy)
            {
                if (n.TieHeld)
                    tieHeldPerMeasure[local]++;
                else
                    slurHeldPerMeasure[local]++;
                if (lastSlot is not null)
                    lastSlot.HeldEnd = (n.MeasureIndex, n.Timing);
                continue;
            }
            lastSlot = new Slot(n.MeasureIndex, n.ItemIndex, n.Timing);
            measures[local].Add(lastSlot);
        }
        int measureCount = measures.Count;
        if (measureCount == 0)
            return lyrics.ToImmutable();

        int lm = 0;     // local measure within the current verse (0 .. measureCount-1)
        int pos = 0;    // note position within the current measure
        int verse = verseNumber;
        int lastPlaced = -1;   // index in `lyrics` of the last placed syllable

        foreach (var (text, connectorType, position, isBarline, isMelisma) in syllables)
        {
            // A barline advances one measure; past the last bar it wraps to the
            // next stacked verse (and any unsung notes left in the bar are skipped).
            if (isBarline)
            {
                lm++;
                pos = 0;
                if (lm >= measureCount)
                {
                    lm = 0;
                    verse++;
                    lastPlaced = -1;   // a marker cannot hold across verses
                }
                continue;
            }

            // A melisma marker (~ / _) holds the previous syllable over one more note
            // in THIS bar — consume a note slot without placing a syllable. The held
            // syllable is LEFT-aligned on its column, and remembers the LAST note it holds
            // (where its extender, if any, ends): the consumed slot's own melisma end when
            // a slur or tie runs on from it, else the slot's note — see
            // LyricItem.MelismaAlignLeft / MelismaEndMeasureIndex.
            if (isMelisma)
            {
                if (lastPlaced >= 0)
                {
                    // The marker declares the melisma (→ LEFT alignment) even when it
                    // has no note left in this bar to consume; the held-end note is
                    // recorded only when there is one.
                    if (lm < measureCount && pos < measures[lm].Count)
                    {
                        var (heldMeasure, heldTiming) = measures[lm][pos].End;
                        lyrics[lastPlaced] = lyrics[lastPlaced] with
                        {
                            MelismaAlignLeft = true,
                            MelismaEndMeasureIndex = heldMeasure,
                            MelismaEndTiming = heldTiming,
                        };
                    }
                    else
                    {
                        lyrics[lastPlaced] = lyrics[lastPlaced] with { MelismaAlignLeft = true };
                    }
                }
                pos++;
                continue;
            }

            // Defensive: a blank syllable consumes nothing.
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (lm < measureCount && pos < measures[lm].Count)
            {
                var slot = measures[lm][pos];
                lastPlaced = lyrics.Count;
                var item = new LyricItem(
                    Text: text,
                    MeasureIndex: slot.MeasureIndex,
                    ItemIndex: slot.ItemIndex,
                    ConnectorType: connectorType,
                    VoiceId: voiceId,
                    VerseNumber: verse,
                    Timing: slot.Timing,
                    SourcePosition: position,
                    HideStanza: hideStanza
                );
                // A slur or tie runs on from this note: the syllable is sung over the
                // whole melisma — LEFT-aligned (lily/lyric-engraver.cc:180-183, a
                // melisma-busy voice takes lyricMelismaAlignment) and held to its last note.
                if (slot.HeldEnd is { } held)
                    item = item with
                    {
                        MelismaAlignLeft = true,
                        MelismaEndMeasureIndex = held.MeasureIndex,
                        MelismaEndTiming = held.Timing,
                    };
                lyrics.Add(item);
            }
            else
            {
                lastPlaced = -1;   // a marker after a DROPPED syllable holds nothing
                // More syllables than notes in this bar — the word would vanish.
                if (unplacedSyllableCount == 0)
                {
                    firstDroppedText = text;
                    firstDroppedPosition = position;
                    firstDroppedBar = lm + 1;
                    firstDroppedSlurHeld = lm < slurHeldPerMeasure.Count ? slurHeldPerMeasure[lm] : 0;
                    firstDroppedTieHeld = lm < tieHeldPerMeasure.Count ? tieHeldPerMeasure[lm] : 0;
                }
                unplacedSyllableCount++;
            }
            pos++;
        }

        if (unplacedSyllableCount > 0)
            overflow = new LyricOverflow(
                unplacedSyllableCount, firstDroppedText, firstDroppedPosition, firstDroppedBar,
                firstDroppedSlurHeld, firstDroppedTieHeld);

        return lyrics.ToImmutable();
    }

    /// <summary>
    /// Parses syllables from a lyrics block, handling hyphens and extenders.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:40-60 syllable parsing
    ///
    /// Structure: LyricsBlock contains LyricMeasure nodes, each containing LyricSyllable nodes.
    /// </remarks>
    internal static List<(string Text, LyricConnectorType Connector, int Position, bool IsBarline, bool IsMelisma)> ParseSyllables(LyricsBlockSyntax lyricsBlock)
        => ParseSyllablesFrom(lyricsBlock.Syllables);

    /// <summary>Parses syllables from an explicit set of lyric-measure nodes
    /// (a block's own measures, or one inner section's).</summary>
    internal static List<(string Text, LyricConnectorType Connector, int Position, bool IsBarline, bool IsMelisma)> ParseSyllablesFrom(IEnumerable<SyntaxNode> measureNodes)
    {
        var result = new List<(string, LyricConnectorType, int, bool, bool)>();

        // Collect all syllable tokens from all measures, keeping each token's
        // source byte offset so the placed syllable can carry it (data-pos).
        // Barlines are KEPT (not stripped) so Collect can use them to skip to the
        // next measure's notes — a written bar is a real measure boundary.
        var allTokens = new List<(string Text, int Position)>();
        foreach (var measureNode in measureNodes)
        {
            // A part-major inner section is not a lyric measure — skip it (the
            // sectioned form is collected via its sections, not this flat path).
            if (measureNode is SectionDeclarationSyntax)
                continue;
            // ⚠️ A lone '|' OPENING the run is a BAR like any other (owner's decision,
            // 2026-08-28). It used to be dropped here as an "anchor", so a lyric row
            // could only line up against a rest-first melody by spelling the gap `| |`.
            // The music path's rule lives on MeasureBuilder._confirmableBoundary.
            // Each child of lyrics block is a LyricMeasure (Kind = LyricMeasure)
            // LyricMeasure contains LyricSyllable nodes and a trailing barline token.
            for (int i = 0; i < measureNode.SlotCount; i++)
            {
                var syllableNode = measureNode.GetChild(i);
                if (syllableNode == null) continue;

                var (text, position) = LyricSyllableReader.ReadToken(syllableNode);
                if (!string.IsNullOrEmpty(text))
                    allTokens.Add((text, position));
            }
        }

        // Build the marker stream. A barline (single `|` or compound `||`/`|.`) is a
        // measure boundary; "--"/"-" is a hyphen on the PREVIOUS syllable (no note);
        // "__"/"_" is an extender (a line over one more held note); "~" is a plain
        // melisma (one more held note, no line). Connectors attach by looking BACK at
        // the last real syllable so a melisma marker can also consume the held note.
        // Marker roles are classified by the shared reader the lyrics-row path uses.
        foreach (var (text, position) in allTokens)
        {
            switch (LyricSyllableReader.Classify(text))
            {
                case LyricSyllableReader.Marker.Barline:
                    result.Add((text, LyricConnectorType.None, position, true, false));
                    break;
                case LyricSyllableReader.Marker.Hyphen:
                    SetPreviousConnector(result, LyricConnectorType.Hyphen);
                    break;
                case LyricSyllableReader.Marker.Extender:
                    SetPreviousConnector(result, LyricConnectorType.Extender);
                    // `__` is LilyPond's extender: a LINE, and nothing else — it takes no
                    // note (the melisma it draws over is the slur's or tie's, or `_`'s).
                    // `_` is LilyPond's skip: it takes one note, holding the previous
                    // syllable over it (and Lily# draws the line with it). Owner's
                    // decision 2026-09-24 (session 569): `__` used to take a note too.
                    if (text != "__")
                        result.Add(("", LyricConnectorType.None, position, false, true)); // melisma note (with line)
                    break;
                case LyricSyllableReader.Marker.Melisma:
                    result.Add(("", LyricConnectorType.None, position, false, true)); // melisma note (no line)
                    break;
                case LyricSyllableReader.Marker.HyphenWord:
                    // A sung syllable ending in a hyphen (Mu-): render it WITHOUT the
                    // dash and draw a centered hyphen to the next syllable, same as a
                    // spaced `--` marker (and the lyrics-row path).
                    result.Add((LyricSyllableReader.DisplaySyllable(LyricSyllableReader.TrimHyphenWord(text)),
                        LyricConnectorType.Hyphen, position, false, false));
                    break;
                default: // Syllable
                    result.Add((LyricSyllableReader.DisplaySyllable(text),
                        LyricConnectorType.None, position, false, false));
                    break;
            }
        }

        return result;
    }

    /// <summary>Attaches a connector (hyphen/extender) to the most recent real
    /// syllable in the stream — connectors are written AFTER the syllable they
    /// belong to.</summary>
    private static void SetPreviousConnector(
        List<(string Text, LyricConnectorType Connector, int Position, bool IsBarline, bool IsMelisma)> result,
        LyricConnectorType connector)
    {
        for (int j = result.Count - 1; j >= 0; j--)
        {
            var e = result[j];
            if (!e.IsBarline && !e.IsMelisma && !string.IsNullOrEmpty(e.Text))
            {
                result[j] = (e.Text, connector, e.Position, e.IsBarline, e.IsMelisma);
                return;
            }
        }
    }

}
