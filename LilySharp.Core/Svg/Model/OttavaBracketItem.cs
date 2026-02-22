namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of ottava transposition.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-engraver.cc middleCOffset handling
/// </remarks>
public enum OttavaType
{
    /// <summary>8va - up one octave.</summary>
    Ottava8va,
    /// <summary>8vb - down one octave.</summary>
    Ottava8vb,
    /// <summary>15ma - up two octaves.</summary>
    Quindicesima15ma,
    /// <summary>15mb - down two octaves.</summary>
    Quindicesima15mb
}

/// <summary>
/// Represents an ottava bracket spanning multiple measures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc OttavaBracket grob
/// LILYPOND-REF: lily/ottava-engraver.cc Ottava_spanner_engraver
/// LILYPOND-REF: scm/define-grobs.scm:2445-2468 OttavaBracket grob defaults
/// </remarks>
public sealed record OttavaBracketItem(
    /// <summary>The type of ottava transposition.</summary>
    OttavaType Type,
    /// <summary>Measure index of the start.</summary>
    int StartMeasureIndex,
    /// <summary>Measure index of the end (where loco or next ottava appears).</summary>
    int EndMeasureIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);
