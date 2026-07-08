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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Collects lyrics into <see cref="LyricItem"/>s: note-bound lines (<c>lyrics melody
/// { … }</c>, one syllable per note, optionally auto-wrapping into verses) and
/// independent lyrics ROWS (<c>lyrics name { … }</c> placed as a score row, syllables
/// spread evenly across each bar). Pulled out of <see cref="MeasureCollector"/> so all
/// lyric logic lives together. The collection-time context it reads — the section→
/// measure map, the bound voices, the lyrics-row names, the time signature — is passed
/// in, as it is built by the main pass.
/// </summary>
internal sealed class LyricsCollector
{
    private readonly List<LyricItem> _lyrics = new();
    private readonly List<LyricSyllableWarning> _warnings = new();

    /// <summary>All collected lyric syllables (note-bound and row).</summary>
    public IReadOnlyList<LyricItem> Lyrics => _lyrics;

    /// <summary>Lyric lines with more syllables than notes (trailing words dropped).</summary>
    public IReadOnlyList<LyricSyllableWarning> Warnings => _warnings;

    /// <summary>
    /// Collects note-bound lyrics from every <c>lyrics { … }</c> block (skipping the
    /// independent ROW blocks, which <see cref="CollectRow"/> handles). A block aligns
    /// to the notes of the SECTION it is written in (offset to that section's first
    /// measure); blocks sharing a section start stack as successive verses.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:60-88 process_music
    /// LILYPOND-REF: lily/lyric-combine-music-iterator.cc:100-150 note association
    /// </remarks>
    public void CollectNoteBound(
        SyntaxNode root, List<Measure> measures,
        IReadOnlySet<string> lyricsRowNames,
        IReadOnlyDictionary<string, (int Index, List<Measure> Measures)> voiceMeasuresByName,
        IReadOnlyDictionary<string, int> sectionStartMeasure,
        IReadOnlyDictionary<string, List<int>>? sectionAllStarts = null)
    {
        var lyricsBlocks = root.DescendantNodes().OfType<LyricsBlockSyntax>().ToList();
        if (lyricsBlocks.Count == 0)
            return;

        var defaultIndices = BuildNoteIndices(measures);

        var lyricCollector = new LyricCollector();
        var nextVerseByStart = new Dictionary<int, int>();
        foreach (var lyricsBlock in lyricsBlocks)
        {
            // An independent lyrics row's block is collected by CollectRow (even-spread),
            // not here — skip it so it isn't also note-bound.
            if (lyricsBlock.VoiceName is { } rowName && lyricsRowNames.Contains(rowName))
                continue;

            // `lyrics sop { … }` aligns to the same-named voice's notes; an unnamed
            // block uses the default (first) voice. The note count AND columns then come
            // from the right voice (the voice index drives timing-based X), so a voice
            // with its own rhythm matches.
            var indices = defaultIndices;
            int voiceId = 0;
            if (lyricsBlock.VoiceName is { } vn
                && voiceMeasuresByName.TryGetValue(vn, out var bound))
            {
                indices = BuildNoteIndices(bound.Measures);
                voiceId = bound.Index;
            }

            // Collect one run of lyric measures aligned from an absolute start bar,
            // stacking verses per start and reporting any overflow.
            void ProcessRun(IEnumerable<SyntaxNode> syllableMeasures, int startMeasure)
            {
                int verseNumber = nextVerseByStart.TryGetValue(startMeasure, out var v) ? v : 1;

                IReadOnlyList<(int MeasureIndex, int ItemIndex, Fraction Timing)> aligned = startMeasure <= 0
                    ? indices
                    : indices.Where(n => n.MeasureIndex >= startMeasure).ToList();

                var lyrics = lyricCollector.Collect(syllableMeasures, aligned, out var overflow, voiceId: voiceId, verseNumber);
                _lyrics.AddRange(lyrics);

                // A single block may auto-wrap into several stacked verses; the next
                // block at this start begins after the highest verse this one produced.
                int maxVerse = verseNumber;
                foreach (var ly in lyrics)
                    if (ly.VerseNumber > maxVerse) maxVerse = ly.VerseNumber;
                nextVerseByStart[startMeasure] = maxVerse + 1;

                // More syllables than notes: the loop above ran out of notes and the
                // trailing syllables vanished. Flag the FIRST dropped syllable itself
                // (not just the lyrics keyword) so the author lands on the exact word
                // where the miscount starts instead of recounting the whole line.
                if (overflow is { } of)
                    _warnings.Add(new LyricSyllableWarning(
                        new LilySharp.Core.Syntax.TextSpan(of.FirstPosition, Math.Max(1, of.FirstText.Length)),
                        of.Count, of.FirstText, of.FirstBar));
            }

            // Part-major lyric track: each inner section's verse aligns under its own
            // named section's bars — at EVERY occurrence (a reprise gets it too). Flat
            // form: the enclosing section's occurrences (or 0 at top level).
            IEnumerable<int> StartsFor(string sectionName)
            {
                if (sectionAllStarts != null && sectionAllStarts.TryGetValue(sectionName, out var all) && all.Count > 0)
                    return all;
                return new[] { sectionStartMeasure.GetValueOrDefault(sectionName, 0) };
            }

            if (lyricsBlock.HasSections)
                foreach (var section in lyricsBlock.Sections)
                    foreach (int start in StartsFor(section.SectionName))
                        ProcessRun(SectionLyricMeasures(section), start);
            else
            {
                string? enclosing = EnclosingSectionName(lyricsBlock);
                if (enclosing != null)
                    foreach (int start in StartsFor(enclosing))
                        ProcessRun(lyricsBlock.Syllables, start);
                else
                    ProcessRun(lyricsBlock.Syllables, ResolveStartMeasure(lyricsBlock, sectionStartMeasure));
            }
        }
    }

    /// <summary>
    /// Collects an independent lyrics row (<c>lyrics name { … }</c> placed via
    /// <c>lyrics name</c> in a score) with NO bound melody: each bar's syllables are
    /// spread evenly across it, and an empty bar (bare <c>|</c>) is skipped. Multiple
    /// blocks of the same name stack as verses (1番, 2番, …). A SINGLE block whose bars
    /// exceed the section's bar count also auto-wraps: every wrap-bars measures start
    /// the next stacked verse, mapped back onto the same bars. Returns the row's measure
    /// skeleton (spacer rests for width); the syllables go to <see cref="Lyrics"/>
    /// tagged with this row's staff index.
    /// </summary>
    public ImmutableArray<Measure> CollectRow(
        SyntaxNode root, string partName, int staffIndex, int totalBars,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int timeBeats, int timeBeatType)
    {
        var blocks = root.DescendantNodes().OfType<LyricsBlockSyntax>()
            .Where(b => b.VoiceName == partName).ToList();
        if (blocks.Count == 0)
            return ImmutableArray<Measure>.Empty;

        var measureItems = new Dictionary<int, ImmutableArray<MusicItem>>();
        int maxIndex = -1;
        int verse = 1;
        int prevSectionStart = -1;
        var measureLen = new Fraction(timeBeats, timeBeatType);

        foreach (var block in blocks)
        {
            int startMeasure = 0;
            bool inSection = false;
            for (var n = block.Parent; n != null; n = n.Parent)
            {
                if (n is SectionDeclarationSyntax section
                    && sectionStartMeasure.TryGetValue(section.SectionName, out int s))
                {
                    startMeasure = s;
                    inSection = true;
                    break;
                }
            }

            // Wrap a block at its OWN section's bar count (verses written flat stack
            // every section). A standalone block (not inside a section) wraps at the
            // whole piece's length. Otherwise a short section's verse 2 would overrun
            // into the next section's bars.
            int wrapBars = totalBars;
            if (inSection)
            {
                int sectionEnd = totalBars;
                foreach (var st in sectionStartMeasure.Values)
                    if (st > startMeasure && st < sectionEnd)
                        sectionEnd = st;
                wrapBars = sectionEnd - startMeasure;
            }

            // Verses stack within ONE section; a block in a new section restarts at
            // verse 1, so its line sits directly under that section's staff rather than
            // carrying over the previous section's verse offset.
            if (startMeasure != prevSectionStart)
            {
                verse = 1;
                prevSectionStart = startMeasure;
            }

            int j = 0; // block-local bar index
            foreach (var measureNode in block.Syllables)
            {
                // Wrap a long block: bar j belongs to verse (j / wrapBars) and maps back
                // onto bar (j % wrapBars). wrapBars <= 0 → no wrap (one verse).
                int barInVerse = wrapBars > 0 ? j % wrapBars : j;
                int v = verse + (wrapBars > 0 ? j / wrapBars : 0);
                int mi = startMeasure + barInVerse;

                var sylls = RowSyllables(measureNode);
                if (sylls.Count > 0)
                {
                    var slotDur = measureLen * new Fraction(1, sylls.Count);
                    var spacers = ImmutableArray.CreateBuilder<MusicItem>(sylls.Count);
                    var timing = Fraction.Zero;
                    // ItemIndex = the syllable's slot (one spacer per syllable), so the
                    // bar gets widened for the lyric widths (SpacingRules.ApplyLyricSpacing).
                    // The engraver still takes X from Timing (IsLyricsRow path).
                    for (int k = 0; k < sylls.Count; k++)
                    {
                        var (text, conn, pos) = sylls[k];
                        _lyrics.Add(new LyricItem(
                            Text: text, MeasureIndex: mi, ItemIndex: k,
                            ConnectorType: conn, VoiceId: staffIndex, VerseNumber: v,
                            Timing: timing, SourcePosition: pos,
                            StaffIndex: staffIndex, IsLyricsRow: true));
                        spacers.Add(new RestItem(slotDur, 0, pos) { IsSpacer = true });
                        timing += slotDur;
                    }
                    // Keep the widest verse's slot count at this bar (verses usually match).
                    if (!measureItems.TryGetValue(mi, out var existing) || existing.Length < sylls.Count)
                        measureItems[mi] = spacers.MoveToImmutable();
                    maxIndex = Math.Max(maxIndex, mi);
                }
                j++;
            }
            // The block produced ceil(j / wrapBars) stacked verses.
            verse += wrapBars > 0 && j > 0 ? (j + wrapBars - 1) / wrapBars : 1;
        }

        if (maxIndex < 0)
            return ImmutableArray<Measure>.Empty;

        var emptyBar = ImmutableArray.Create<MusicItem>(
            new RestItem(measureLen, 0, 0) { IsSpacer = true });
        var measures = ImmutableArray.CreateBuilder<Measure>(maxIndex + 1);
        for (int i = 0; i <= maxIndex; i++)
        {
            var items = measureItems.TryGetValue(i, out var it) ? it : emptyBar;
            // A lyrics row separates measures with plain `|`; give each a drawn
            // boundary (final on the last) so a standalone lyrics-only lead sheet
            // shows a measure grid. The score-wide barline sync merges these
            // harmlessly with any music staff (which dominates via Stronger).
            var end = i == maxIndex ? BarlineType.Final : BarlineType.Single;
            measures.Add(new Measure(items, BarlineType.None, end, null, 0, 0));
        }
        return measures.MoveToImmutable();
    }

    /// <summary>
    /// Extracts one bar's lyric syllables (text + connector) from a lyric-measure node,
    /// applying the hyphen ("a-" / "-" / "--") and extender ("~" / "_") markers — the
    /// barline token and melisma-only markers are dropped.
    /// </summary>
    private static List<(string Text, LyricConnectorType Conn, int Pos)> RowSyllables(SyntaxNode measureNode)
    {
        var result = new List<(string, LyricConnectorType, int)>();
        void SetPrevConnector(LyricConnectorType conn)
        {
            if (result.Count > 0)
                result[^1] = (result[^1].Item1, conn, result[^1].Item3);
        }

        for (int i = 0; i < measureNode.SlotCount; i++)
        {
            var child = measureNode.GetChild(i);
            if (child == null) continue;
            var (text, pos) = LyricSyllableReader.ReadToken(child);
            if (string.IsNullOrEmpty(text)) continue;

            switch (LyricSyllableReader.Classify(text))
            {
                case LyricSyllableReader.Marker.Barline:
                    break; // a measure boundary, never a word
                case LyricSyllableReader.Marker.Hyphen:
                    SetPrevConnector(LyricConnectorType.Hyphen);
                    break;
                // A row holds no notes, so an extender/melisma marker just draws an
                // extender line off the previous syllable.
                case LyricSyllableReader.Marker.Extender:
                case LyricSyllableReader.Marker.Melisma:
                    SetPrevConnector(LyricConnectorType.Extender);
                    break;
                case LyricSyllableReader.Marker.HyphenWord:
                    result.Add((LyricSyllableReader.DisplaySyllable(LyricSyllableReader.TrimHyphenWord(text)),
                        LyricConnectorType.Hyphen, pos));
                    break;
                default: // Syllable
                    result.Add((LyricSyllableReader.DisplaySyllable(text),
                        LyricConnectorType.None, pos));
                    break;
            }
        }
        return result;
    }

    /// <summary>(measureIndex, itemIndex, timing) of every note/chord (not rests) in a
    /// voice's measures — the slots a lyric line's syllables map onto. The timing
    /// (musical moment in the measure) lets a bound voice's syllable land over its real
    /// column even when that voice's rhythm differs.</summary>
    private static List<(int MeasureIndex, int ItemIndex, Fraction Timing)> BuildNoteIndices(List<Measure> measures)
    {
        var noteIndices = new List<(int MeasureIndex, int ItemIndex, Fraction Timing)>();
        for (int m = 0; m < measures.Count; m++)
        {
            var timing = Fraction.Zero;
            var items = measures[m].Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is NoteItem or ChordItem)
                    noteIndices.Add((m, i, timing));
                timing += items[i].Duration;
            }
        }
        return noteIndices;
    }

    /// <summary>The first expanded-measure index a lyrics block aligns to: the start of
    /// the section it is written in (0 when it is top-level or its section was never
    /// reached by the structure).</summary>
    private static int ResolveStartMeasure(
        LyricsBlockSyntax lyricsBlock, IReadOnlyDictionary<string, int> sectionStartMeasure)
    {
        for (var n = lyricsBlock.Parent; n != null; n = n.Parent)
            if (n is SectionDeclarationSyntax section
                && sectionStartMeasure.TryGetValue(section.SectionName, out int start))
                return start;
        return 0;
    }

    /// <summary>The name of the section a flat lyrics block is written inside, or null
    /// at top level — used to repeat its verse under every occurrence of that section.</summary>
    private static string? EnclosingSectionName(LyricsBlockSyntax block)
    {
        for (var n = block.Parent; n != null; n = n.Parent)
            if (n is SectionDeclarationSyntax section)
                return section.SectionName;
        return null;
    }

    /// <summary>The lyric measures of a lyric-track inner section (the nodes between
    /// its name and closing brace).</summary>
    private static IEnumerable<SyntaxNode> SectionLyricMeasures(SectionDeclarationSyntax section)
    {
        // Slots: 0 keyword, 1 name, 2 '{', 3..n-2 items, n-1 '}'.
        for (int i = 3; i < section.SlotCount - 1; i++)
            if (section.GetChild(i) is SyntaxNode node and not SyntaxTokenNode)
                yield return node;
    }
}
