namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Direction of a hairpin (crescendo or decrescendo).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:140-142 grow_dir
/// </remarks>
public enum HairpinDirection
{
    /// <summary>Crescendo: opening wedge (grows louder).</summary>
    Crescendo,
    /// <summary>Decrescendo: closing wedge (grows softer).</summary>
    Decrescendo
}

/// <summary>
/// Represents a hairpin (crescendo/decrescendo wedge) spanning multiple notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358
/// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob
/// </remarks>
public sealed record HairpinItem(
    /// <summary>Crescendo or decrescendo.</summary>
    HairpinDirection Direction,
    /// <summary>Measure index of the start note.</summary>
    int StartMeasureIndex,
    /// <summary>Item index within the start measure.</summary>
    int StartItemIndex,
    /// <summary>Measure index of the end note.</summary>
    int EndMeasureIndex,
    /// <summary>Item index within the end measure.</summary>
    int EndItemIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);
