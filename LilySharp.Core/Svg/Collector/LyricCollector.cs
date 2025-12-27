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
    private List<(string Text, LyricConnectorType Connector)> ParseSyllables(LyricsBlockSyntax lyricsBlock)
    {
        var result = new List<(string, LyricConnectorType)>();
        var tokens = lyricsBlock.Syllables.ToList();
        
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var text = GetTokenText(token);
            
            // Check if next token is a connector
            LyricConnectorType connector = LyricConnectorType.None;
            if (i + 1 < tokens.Count)
            {
                var nextText = GetTokenText(tokens[i + 1]);
                if (nextText == "--")
                {
                    connector = LyricConnectorType.Hyphen;
                    i++; // Skip the connector token
                }
                else if (nextText == "__")
                {
                    connector = LyricConnectorType.Extender;
                    i++; // Skip the connector token
                }
            }
            
            // Skip pure connector tokens
            if (text == "--" || text == "__")
            {
                continue;
            }
            
            result.Add((text, connector));
        }
        
        return result;
    }
    
    private string GetTokenText(SyntaxNode node)
    {
        if (node is SyntaxTokenNode tokenNode)
        {
            var text = tokenNode.Text;
            // Remove quotes from string literals
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
            {
                return text.Substring(1, text.Length - 2);
            }
            return text;
        }
        return node.ToString();
    }
}
