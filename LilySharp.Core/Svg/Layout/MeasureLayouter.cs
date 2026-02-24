// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    ///
    /// Creates springs between each timing point (column) in the measure.
    /// Springs include skyline-based minimum distances (rods) from the primary
    /// voice's items to prevent accidental/notehead collisions.
    /// </remarks>
    public ImmutableArray<ColumnLayout> LayoutColumns(Measure measure, double totalWidth, List<Fraction> timings,
                                                      double? baseShortestDuration = null)
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
        // Build a map from timing → item for skyline-based rod calculation.
        // Each column's minimum distance must account for collisions between
        // items at adjacent timing points (e.g., accidentals, noteheads).
        var timingToItem = new Dictionary<Fraction, MusicItem>();
        {
            var t = Fraction.Zero;
            foreach (var item in measure.Items)
            {
                if (!timingToItem.ContainsKey(t))
                    timingToItem[t] = item;
                t += item.Duration;
            }
        }

        // LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
        // Spring chain: [barline] → [col₀] → [col₁] → ... → [colₙ] → [end barline]
        // All springs participate in the solver uniformly, just like LayoutItems.
        var springs = new List<Spring>();

        // Spring 0: barline → first column
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist (first-note . (fixed-space . 1.3))
        // Uses duration-based ideal but enforces BarLineToFirstNoteSpace as minimum.
        var firstDuration = timings.Count > 1 ? timings[1] - timings[0] : totalDuration;
        var firstSpring = SpacingRules.CreateTimingSpring(firstDuration, baseShortestDuration);
        double firstNoteMin = EngravingDefaults.BarLineToFirstNoteSpace;

        // Apply skyline rod: barline → first item
        if (timingToItem.TryGetValue(timings[0], out var firstItem))
        {
            double skyDist = SpacingRules.CalculateSkylineDistance(null, firstItem, staffY: 0);
            firstNoteMin = Math.Max(firstNoteMin, skyDist);
        }

        springs.Add(new Spring(
            Math.Max(firstSpring.IdealDistance, firstNoteMin),
            firstNoteMin,
            firstSpring.InverseStretchStrength));

        // Springs between adjacent timing columns (duration-proportional + skyline rods)
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
            var spring = SpacingRules.CreateTimingSpring(segmentDuration, baseShortestDuration);

            // LILYPOND-REF: lily/spacing-spanner.cc — apply rod from skyline collision
            // between items at adjacent timing points
            timingToItem.TryGetValue(timings[i - 1], out var prevItem);
            timingToItem.TryGetValue(timings[i], out var nextItem);
            if (prevItem != null && nextItem != null)
            {
                double skyDist = SpacingRules.CalculateSkylineDistance(prevItem, nextItem, staffY: 0);
                if (skyDist > spring.MinDistance)
                {
                    spring = new Spring(
                        Math.Max(spring.IdealDistance, skyDist),
                        skyDist,
                        spring.InverseStretchStrength);
                }
            }

            springs.Add(spring);
        }

        // End spring: last column → barline (remaining duration)
        var endDuration = totalDuration - timings[^1];
        var endSpring = SpacingRules.CreateTimingSpring(endDuration, baseShortestDuration);

        // Apply skyline rod: last item → barline
        if (timingToItem.TryGetValue(timings[^1], out var lastItem))
        {
            double skyDist = SpacingRules.CalculateSkylineDistance(lastItem, null, staffY: 0);
            if (skyDist > endSpring.MinDistance)
            {
                endSpring = new Spring(
                    Math.Max(endSpring.IdealDistance, skyDist),
                    skyDist,
                    endSpring.InverseStretchStrength);
            }
        }
        springs.Add(endSpring);

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
