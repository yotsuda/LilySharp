using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout result for a percent repeat symbol.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - PercentRepeat grob
/// </remarks>
public readonly record struct PercentRepeatLayout(
    int MeasureIndex,
    double X,                // X center of the percent symbol (staff spaces)
    double Y,                // Y center of the percent symbol (staff spaces from system top)
    double Width,            // Measure width for proportional sizing
    int SourcePosition
);

/// <summary>
/// Calculates layout positions for percent repeat symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-interface.cc - x_percent() rendering
/// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - self-alignment-X = CENTER
///
/// The percent symbol is centered horizontally and vertically within the measure.
/// </remarks>
public static class PercentRepeatEngraver
{
    /// <summary>
    /// Calculates percent repeat layouts from collected items.
    /// </summary>
    public static ImmutableArray<PercentRepeatLayout> Calculate(
        ImmutableArray<PercentRepeatItem> percentRepeats,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (percentRepeats.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<PercentRepeatLayout>.Empty;

        var results = ImmutableArray.CreateBuilder<PercentRepeatLayout>(percentRepeats.Length);

        foreach (var item in percentRepeats)
        {
            if (item.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[item.MeasureIndex];

            // Center of the measure
            double x = ml.X + ml.Width / 2;

            // Y = center of staff (2 staff spaces from top = middle of 4-line staff)
            double y = 2.0;

            results.Add(new PercentRepeatLayout(
                item.MeasureIndex, x, y, ml.Width, item.SourcePosition));
        }

        return results.ToImmutable();
    }
}
