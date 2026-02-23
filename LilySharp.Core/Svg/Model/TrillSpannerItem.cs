namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a trill spanner (tr symbol with wavy line extension).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/trill-spanner-engraver.cc Trill_spanner_engraver class
/// LILYPOND-REF: scm/define-grobs.scm:2175-2230 TrillSpanner grob definition
///
/// Trill spanners display "tr" at the start point with a wavy line
/// extending to the end point. Used for sustained trills:
///   tr~~~~~~~~~~~~
/// </remarks>
public sealed record TrillSpannerItem(
    /// <summary>Measure index of the start point.</summary>
    int StartMeasureIndex,
    /// <summary>Item index within the start measure.</summary>
    int StartItemIndex,
    /// <summary>Measure index of the end point.</summary>
    int EndMeasureIndex,
    /// <summary>Item index within the end measure.</summary>
    int EndItemIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);
