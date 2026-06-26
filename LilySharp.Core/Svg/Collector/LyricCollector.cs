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
/// Lyrics are associated with notes by position. Each syllable corresponds
/// to one note in the melody. Hyphens (--) indicate word continuation,
/// extenders (__) indicate melisma (single syllable over multiple notes).
/// </remarks>
public sealed class LyricCollector
{
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
    /// <param name="unplacedSyllableCount">
    /// Number of real syllables left over after the notes ran out (overflow). Each
    /// is a word that would silently vanish from the engraving — the count lets the
    /// caller warn about a miscounted lyric line. Melisma/extender markers do not
    /// count (they consume no note), so a line shorter than the melody never
    /// reports overflow.
    /// </param>
    /// <returns>List of LyricItem objects.</returns>
    public ImmutableArray<LyricItem> Collect(
        LyricsBlockSyntax lyricsBlock,
        IReadOnlyList<(int MeasureIndex, int ItemIndex, LilySharp.Core.Semantics.Fraction Timing)> noteItemIndices,
        out int unplacedSyllableCount,
        int voiceId = 0,
        int verseNumber = 1)
    {
        var lyrics = ImmutableArray.CreateBuilder<LyricItem>();
        var syllables = ParseSyllables(lyricsBlock);

        int noteIndex = 0;
        int i = 0;
        // Music measure the last syllable landed in. A lyric barline skips every
        // remaining note of this measure so the next syllable starts the next bar,
        // honouring the written "|" instead of running syllables on sequentially.
        int lastPlacedMeasure = -1;
        for (; i < syllables.Count && noteIndex < noteItemIndices.Count; i++)
        {
            var (text, connectorType, position, isBarline, isMelisma) = syllables[i];

            // A barline advances to the next measure's notes (drops any unsung
            // notes left in the current bar).
            if (isBarline)
            {
                while (noteIndex < noteItemIndices.Count
                       && noteItemIndices[noteIndex].MeasureIndex <= lastPlacedMeasure)
                    noteIndex++;
                continue;
            }

            // A melisma (~ / __ / _) holds the PREVIOUS syllable over one more note,
            // so consume that note without placing a syllable — the held note is no
            // longer left unsung. Stay within the bar (a barline, not a melisma,
            // crosses to the next measure).
            if (isMelisma)
            {
                if (noteIndex < noteItemIndices.Count
                    && noteItemIndices[noteIndex].MeasureIndex == lastPlacedMeasure)
                    noteIndex++;
                continue;
            }

            // Defensive: a blank syllable consumes nothing.
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var (measureIndex, itemIndex, timing) = noteItemIndices[noteIndex];

            lyrics.Add(new LyricItem(
                Text: text,
                MeasureIndex: measureIndex,
                ItemIndex: itemIndex,
                ConnectorType: connectorType,
                VoiceId: voiceId,
                VerseNumber: verseNumber,
                Timing: timing,
                SourcePosition: position
            ));

            lastPlacedMeasure = measureIndex;
            noteIndex++;
        }

        // Any real syllables remaining once the notes are exhausted are dropped on
        // the floor by the loop above. Count them (applying the same skip rules so
        // trailing extenders/blanks/barlines don't inflate the figure) so the
        // caller can warn.
        unplacedSyllableCount = 0;
        for (; i < syllables.Count; i++)
        {
            var (text, _, _, isBarline, isMelisma) = syllables[i];
            if (isBarline || isMelisma || string.IsNullOrWhiteSpace(text))
                continue;
            unplacedSyllableCount++;
        }

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
    private List<(string Text, LyricConnectorType Connector, int Position, bool IsBarline, bool IsMelisma)> ParseSyllables(LyricsBlockSyntax lyricsBlock)
    {
        var result = new List<(string, LyricConnectorType, int, bool, bool)>();

        // Collect all syllable tokens from all measures, keeping each token's
        // source byte offset so the placed syllable can carry it (data-pos).
        // Barlines are KEPT (not stripped) so Collect can use them to skip to the
        // next measure's notes — the written "|" is a real measure boundary.
        var allTokens = new List<(string Text, int Position)>();
        foreach (var measureNode in lyricsBlock.Syllables)
        {
            // Each child of lyrics block is a LyricMeasure (Kind = LyricMeasure)
            // LyricMeasure contains LyricSyllable nodes and a barline
            for (int i = 0; i < measureNode.SlotCount; i++)
            {
                var syllableNode = measureNode.GetChild(i);
                if (syllableNode == null) continue;

                var text = GetTokenText(syllableNode);
                if (!string.IsNullOrEmpty(text))
                {
                    allTokens.Add((text, GetTokenPosition(syllableNode)));
                }
            }
        }

        // Build the marker stream. A barline is a measure boundary; "--"/"-" is a
        // hyphen on the PREVIOUS syllable (no note); "__"/"_" is an extender (a line
        // over one more held note); "~" is a plain melisma (one more held note, no
        // line). Connectors attach by looking BACK at the last real syllable so a
        // melisma marker can also be emitted to consume the held note.
        foreach (var (text, position) in allTokens)
        {
            if (text == "|")
            {
                result.Add(("|", LyricConnectorType.None, position, true, false));
            }
            else if (text == "--" || text == "-")
            {
                SetPreviousConnector(result, LyricConnectorType.Hyphen);
            }
            else if (text == "__" || text == "_")
            {
                SetPreviousConnector(result, LyricConnectorType.Extender);
                result.Add(("", LyricConnectorType.None, position, false, true)); // melisma note (with line)
            }
            else if (text == "~")
            {
                result.Add(("", LyricConnectorType.None, position, false, true)); // melisma note (no line)
            }
            else
            {
                result.Add((text, LyricConnectorType.None, position, false, false));
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

    private string GetTokenText(SyntaxNode node)
    {
        // For LyricSyllable nodes, get the child token's text
        if (node.Kind == SyntaxKind.LyricSyllable)
        {
            var child = node.GetChild(0);
            if (child is SyntaxTokenNode tokenNode)
            {
                return GetCleanText(tokenNode.Text);
            }
        }

        // Direct token node
        if (node is SyntaxTokenNode directToken)
        {
            return GetCleanText(directToken.Text);
        }

        // Fallback: try to get text from first child token
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child is SyntaxTokenNode childToken)
            {
                return GetCleanText(childToken.Text);
            }
        }

        return "";
    }

    /// <summary>Source byte offset of a syllable's WORD (Span.Start excludes the
    /// leading trivia), so the emitted data-pos lands on the syllable itself and a
    /// preview click jumps the editor to the word, not the whitespace before it.
    /// Mirrors <see cref="GetTokenText"/>'s node walk.</summary>
    private int GetTokenPosition(SyntaxNode node)
    {
        if (node.Kind == SyntaxKind.LyricSyllable && node.GetChild(0) is SyntaxTokenNode tokenNode)
            return tokenNode.Span.Start;

        if (node is SyntaxTokenNode directToken)
            return directToken.Span.Start;

        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is SyntaxTokenNode childToken)
                return childToken.Span.Start;
        }

        return node.Span.Start;
    }

    private static string GetCleanText(string text)
    {
        // Remove quotes from string literals
        if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
        {
            return text.Substring(1, text.Length - 2);
        }
        return text;
    }
}
