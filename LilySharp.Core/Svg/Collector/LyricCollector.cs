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
    /// <returns>List of LyricItem objects.</returns>
    public ImmutableArray<LyricItem> Collect(
        LyricsBlockSyntax lyricsBlock,
        IReadOnlyList<(int MeasureIndex, int ItemIndex)> noteItemIndices,
        int voiceId = 0,
        int verseNumber = 1)
    {
        var lyrics = ImmutableArray.CreateBuilder<LyricItem>();
        var syllables = ParseSyllables(lyricsBlock);

        int noteIndex = 0;
        for (int i = 0; i < syllables.Count && noteIndex < noteItemIndices.Count; i++)
        {
            var (text, connectorType) = syllables[i];

            // Skip extender markers - they indicate previous syllable continues
            if (text == "__")
            {
                // Don't consume a note for pure extenders
                continue;
            }

            // Handle empty text with extender (melisma continuation)
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var (measureIndex, itemIndex) = noteItemIndices[noteIndex];

            lyrics.Add(new LyricItem(
                Text: text,
                MeasureIndex: measureIndex,
                ItemIndex: itemIndex,
                ConnectorType: connectorType,
                VoiceId: voiceId,
                VerseNumber: verseNumber
            ));

            noteIndex++;
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
    private List<(string Text, LyricConnectorType Connector)> ParseSyllables(LyricsBlockSyntax lyricsBlock)
    {
        var result = new List<(string, LyricConnectorType)>();

        // Collect all syllable tokens from all measures
        var allTokens = new List<string>();
        foreach (var measureNode in lyricsBlock.Syllables)
        {
            // Each child of lyrics block is a LyricMeasure (Kind = LyricMeasure)
            // LyricMeasure contains LyricSyllable nodes and a barline
            for (int i = 0; i < measureNode.SlotCount; i++)
            {
                var syllableNode = measureNode.GetChild(i);
                if (syllableNode == null) continue;

                var text = GetTokenText(syllableNode);
                if (!string.IsNullOrEmpty(text) && text != "|")
                {
                    allTokens.Add(text);
                }
            }
        }

        // Process tokens with connector detection
        for (int i = 0; i < allTokens.Count; i++)
        {
            var text = allTokens[i];

            // Check if next token is a connector
            LyricConnectorType connector = LyricConnectorType.None;
            if (i + 1 < allTokens.Count)
            {
                var nextText = allTokens[i + 1];
                if (nextText == "--")
                {
                    connector = LyricConnectorType.Hyphen;
                    i++; // Skip the connector token
                }
                else if (nextText == "__" || nextText == "_")
                {
                    connector = LyricConnectorType.Extender;
                    i++; // Skip the connector token
                }
            }

            // Skip pure connector tokens and standalone hyphens
            if (text == "--" || text == "__" || text == "_" || text == "~" || text == "-")
            {
                continue;
            }

            result.Add((text, connector));
        }

        return result;
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
