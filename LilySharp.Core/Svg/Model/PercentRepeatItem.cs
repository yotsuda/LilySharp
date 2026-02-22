namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A percent repeat sign marking a measure as a repetition of the previous measure.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-engraver.cc - PercentRepeat grob
/// LILYPOND-REF: lily/percent-repeat-interface.cc - visual rendering (slash + dots)
/// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - PercentRepeat properties
///
/// A single percent sign (%) repeats the previous measure.
/// A double percent sign (%%) repeats the previous 2 measures.
///
/// In LilySharp, percent repeats are generated from:
/// - `repeat percent N { body }` syntax (automatic)
/// - Body is unfolded N times, iterations 2+ marked as percent repeats
/// </remarks>
public sealed record PercentRepeatItem(
    /// <summary>Measure index where the percent sign appears.</summary>
    int MeasureIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);
