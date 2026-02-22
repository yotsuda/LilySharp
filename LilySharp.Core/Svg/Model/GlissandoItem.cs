namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Style of glissando line.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1575 (style . line)
/// </remarks>
public enum GlissandoStyle
{
    /// <summary>Straight line (LilyPond default).</summary>
    Line,
    /// <summary>Zigzag/wavy line.</summary>
    Zigzag
}

/// <summary>
/// Represents a glissando connecting two notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/glissando-engraver.cc - Glissando_engraver
/// A glissando is a sliding line between two notes of different pitch.
/// </remarks>
public readonly record struct GlissandoItem(
    int StartMeasureIndex,
    int StartItemIndex,
    int StartStaffPosition,
    int EndMeasureIndex,
    int EndItemIndex,
    int EndStaffPosition,
    GlissandoStyle Style,
    int SourcePosition);
