namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of piano pedal.
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc - Pedal_type_info
/// </remarks>
public enum PedalType
{
    /// <summary>Sustain pedal (damper pedal)</summary>
    Sustain,
    /// <summary>Sostenuto pedal (center pedal)</summary>
    Sostenuto,
    /// <summary>Una corda pedal (soft pedal)</summary>
    UnaCorda,
}

/// <summary>
/// Represents a pedal bracket span (from pedal-on to pedal-off).
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc:256-400 Pedal event processing
/// LILYPOND-REF: define-grobs.scm:2586-2605 PianoPedalBracket grob
/// </remarks>
public readonly record struct PedalBracketItem(
    PedalType Type,
    int StartMeasureIndex,
    int EndMeasureIndex,
    int SourcePosition
);
