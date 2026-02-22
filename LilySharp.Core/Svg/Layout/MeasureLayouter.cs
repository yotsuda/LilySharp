using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates item positions within a measure using Spring-Rod model.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-basic.cc:100-130 note_spacing()
/// LILYPOND-REF: lily/simple-spacer.cc (spring solver)
/// </remarks>
public sealed class MeasureLayouter
{
    /// <summary>
    /// Layouts items within a measure using the Spring-Rod model.
    /// </summary>
    /// <remarks>
    /// The Spring-Rod model:
    /// 1. Creates springs between adjacent items (and between barlines and items)
    /// 2. Each spring has an ideal distance (based on duration) and minimum distance (to avoid collision)
    /// 3. A solver finds the force that achieves the target width while respecting constraints
    /// </remarks>
    public ImmutableArray<ItemLayout> LayoutItems(
        Measure measure,
        double totalWidth,
        ImmutableArray<Spring>? precomputedSprings = null,
        double? precomputedForce = null)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<ItemLayout>.Empty;

        // Calculate barline widths
        double startBarlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline);
        double endBarlineWidth = SpacingRules.GetBarlineWidth(measure.EndBarline);

        // Use precomputed springs if available, otherwise calculate
        var springs = precomputedSprings ?? SpacingRules.CreateSpringsForMeasure(measure);

        // Use precomputed force if available, otherwise solve for it
        double force;
        if (precomputedForce.HasValue)
        {
            force = precomputedForce.Value;
        }
        else
        {
            // Calculate target width for the spring chain
            double targetWidth = totalWidth - startBarlineWidth - endBarlineWidth;
            var solver = new SpringSolver(springs);
            force = solver.SolveForWidth(targetWidth);
        }

        // Get positions (these are reference point positions relative to start barline)
        var positions = new SpringSolver(springs).GetPositions(force, startX: 0);

        // Convert to ItemLayout
        // positions[0] = first item position
        // positions[i + 1] = position of item i
        // positions[N] = end position (should equal targetWidth)
        var layouts = new List<ItemLayout>();

        for (int i = 0; i < measure.Items.Length; i++)
        {
            // X position relative to measure start (add startBarlineWidth)
            double x = startBarlineWidth + positions[i + 1];

            // Width is distance to next position
            double width = positions[i + 2] - positions[i + 1];

            layouts.Add(new ItemLayout(i, x, width));
        }

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Calculates column layouts for a measure based on collected timings.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc - Each musical moment becomes a paper column
    /// LILYPOND-REF: lily/spacing-spanner.cc - Springs connect adjacent columns
    ///
    /// This creates springs between each timing point (column) in the measure,
    /// using the same Spring-Rod model as single-staff layout.
    /// </remarks>
    public ImmutableArray<ColumnLayout> LayoutColumns(Measure measure, double totalWidth, List<Fraction> timings)
    {
        if (timings.Count == 0)
            return ImmutableArray<ColumnLayout>.Empty;

        // Calculate barline widths
        // LILYPOND-REF: lily/spacing-basic.cc:50-52 barline dimensions
        double startBarlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline);
        double endBarlineWidth = SpacingRules.GetBarlineWidth(measure.EndBarline);

        // Calculate total duration of the measure
        var totalDuration = Fraction.Zero;
        foreach (var item in measure.Items)
        {
            totalDuration += item.Duration;
        }

        if (totalDuration == Fraction.Zero)
            return ImmutableArray<ColumnLayout>.Empty;

        // LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
        // Spring chain: [barline] → [col₀] → [col₁] → ... → [colₙ] → [end barline]
        // All springs participate in the solver uniformly, just like LayoutItems.
        var springs = new List<Spring>();

        // Spring 0: barline → first column
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist (first-note . (fixed-space . 1.3))
        // Uses duration-based ideal but enforces BarLineToFirstNoteSpace as minimum.
        var firstDuration = timings.Count > 1 ? timings[1] - timings[0] : totalDuration;
        var firstSpring = SpacingRules.CreateTimingSpring(firstDuration);
        double firstNoteMin = EngravingDefaults.BarLineToFirstNoteSpace;
        springs.Add(new Spring(
            Math.Max(firstSpring.IdealDistance, firstNoteMin),
            firstNoteMin,
            firstSpring.InverseStretchStrength));

        // Springs between adjacent timing columns (duration-proportional)
        for (int i = 1; i < timings.Count; i++)
        {
            Fraction segmentDuration;
            if (i < timings.Count - 1)
            {
                segmentDuration = timings[i + 1] - timings[i];
            }
            else
            {
                segmentDuration = totalDuration - timings[i];
            }
            springs.Add(SpacingRules.CreateTimingSpring(segmentDuration));
        }

        // End spring: last column → barline (remaining duration)
        var endDuration = totalDuration - timings[^1];
        springs.Add(SpacingRules.CreateTimingSpring(endDuration));

        // Available width for the entire spring chain
        double targetWidth = totalWidth - startBarlineWidth - endBarlineWidth;

        // LILYPOND-REF: lily/simple-spacer.cc:175-205 solve for force
        var solver = new SpringSolver(springs.ToImmutableArray());
        double force = solver.SolveForWidth(targetWidth);

        // Get positions from spring solver
        var positions = solver.GetPositions(force, startX: 0);

        // Create columns with solved positions
        var columns = ImmutableArray.CreateBuilder<ColumnLayout>();

        for (int i = 0; i < timings.Count; i++)
        {
            var timing = timings[i];
            double x = startBarlineWidth + positions[i + 1];
            double width = positions[i + 2] - positions[i + 1];

            columns.Add(new ColumnLayout(timing, x, width));
        }

        return columns.ToImmutable();
    }
}
