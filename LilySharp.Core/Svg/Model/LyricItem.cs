namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of connector between lyric syllables.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-hyphen.cc:1-150
/// </remarks>
public enum LyricConnectorType
{
    /// <summary>No connector.</summary>
    None,

    /// <summary>Hyphen between syllables of the same word.</summary>
    Hyphen,

    /// <summary>Extender line for melisma (single syllable over multiple notes).</summary>
    Extender
}

/// <summary>
/// Represents a single lyric syllable in a score.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-engraver.cc:32-52
/// LILYPOND-REF: scm/define-grobs.scm:3020-3060 LyricText grob
///
/// A syllable is a unit of lyric text that corresponds to one or more notes.
/// Multiple syllables form words, connected by hyphens.
/// </remarks>
public sealed record LyricItem(
    /// <summary>The text content of this syllable.</summary>
    string Text,

    /// <summary>Index of the measure containing this syllable.</summary>
    int MeasureIndex,

    /// <summary>Index of the note this syllable is attached to.</summary>
    int ItemIndex,

    /// <summary>Type of connector after this syllable.</summary>
    LyricConnectorType ConnectorType = LyricConnectorType.None,

    /// <summary>Voice ID this lyric belongs to (for multi-voice lyrics).</summary>
    int VoiceId = 0,

    /// <summary>Verse number (1-based) for multiple lyric lines.</summary>
    int VerseNumber = 1
);
