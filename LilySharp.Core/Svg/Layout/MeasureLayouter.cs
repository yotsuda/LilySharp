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
    /// Creates timing-based springs for a measure, considering items from all voices.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    /// LILYPOND-REF: lily/paper-column.cc — paper columns aggregate grobs from all staves.
    ///
    /// Spring chain: [barline] → [col₀] → [col₁] → ... → [colₙ] → [end barline]
    /// Each spring's minimum distance (rod) accounts for skyline collisions from ALL voices.
    /// </remarks>
    public ImmutableArray<Spring> CreateTimingSprings(
        Measure measure, List<Fraction> timings,
        double? baseShortestDuration = null,
        IReadOnlyList<Measure>? allMeasures = null)
    {
        if (timings.Count == 0)
            return ImmutableArray<Spring>.Empty;

        // Calculate total duration of the measure
        var totalDuration = Fraction.Zero;
        foreach (var item in measure.Items)
        {
            totalDuration += item.Duration;
        }

        if (totalDuration == Fraction.Zero)
            return ImmutableArray<Spring>.Empty;

        // LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
        // Build a map from timing → items for skyline-based rod calculation.
        // Each column's minimum distance must account for collisions between
        // items at adjacent timing points across ALL voices (e.g., accidentals, noteheads).
        // LILYPOND-REF: lily/paper-column.cc — paper columns aggregate grobs from all staves
        var timingToItems = new Dictionary<Fraction, List<MusicItem>>();
        var measuresToScan = allMeasures ?? new[] { measure };
        foreach (var m in measuresToScan)
        {
            var t = Fraction.Zero;
            foreach (var item in m.Items)
            {
                if (!timingToItems.TryGetValue(t, out var items))
                {
                    items = new List<MusicItem>();
                    timingToItems[t] = items;
                }
                items.Add(item);
                t += item.Duration;
            }
        }

        // Full-measure rests are spaced by a compact rod, not proportionally
        // to the notated whole note — when EVERY voice is resting the whole
        // measure, the combined-timing path must compact exactly like the
        // single-voice path, or line breaking and layout disagree about the
        // measure's width and multi-measure-rest runs split or stretch.
        // LILYPOND-REF: lily/multi-measure-rest.cc:340-391 set_spacing_rods
        bool allFullMeasureRests = true;
        foreach (var m in measuresToScan)
        {
            if (!MultiMeasureRestEngraver.IsFullMeasureRest(m))
            {
                allFullMeasureRests = false;
                break;
            }
        }
        if (allFullMeasureRests && measure.Items.Length > 0)
        {
            var rest = measure.Items[0];
            double inc = EngravingDefaults.SpacingIncrement;
            double startMin = Math.Max(inc, SpacingRules.CalculateSkylineDistance(null, rest, staffY: 0));
            double endMin = Math.Max(inc, SpacingRules.CalculateSkylineDistance(rest, null, staffY: 0));
            return ImmutableArray.Create(
                new Spring(Math.Max(1.25 * inc, startMin), startMin, Math.Max(0.1, 0.25 * inc)),
                new Spring(Math.Max(2.0 * inc, endMin), endMin, Math.Max(0.1, inc)));
        }

        var springs = new List<Spring>();

        // Spring 0: barline → first column. This is BREAKABLE spacing, not
        // musical spacing: the gap after a barline is governed by the
        // BarLine space-alist, NOT by the first note's duration, and it must
        // never stretch under line justification (or the first note drifts
        // rightward in stretched lines).
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist —
        //   (first-note . (semi-shrink-space . 1.3))
        // LILYPOND-REF: lily/staff-spacing.cc Staff_spacing::get_spacing —
        //   semi-shrink-space: fixed = d/2, ideal = d, is_stretchable = false
        //   → inverse stretch strength 0; compressible only down to fixed
        //   (inverse compress = ideal − fixed = d/2).
        double firstNoteSpace = EngravingDefaults.BarLineToFirstNoteSpace;
        double firstNoteMin = firstNoteSpace / 2;

        // Apply skyline rod: barline → first item (max across all voices)
        if (timingToItems.TryGetValue(timings[0], out var firstItems))
        {
            foreach (var item in firstItems)
            {
                double skyDist = SpacingRules.CalculateSkylineDistance(null, item, staffY: 0);
                firstNoteMin = Math.Max(firstNoteMin, skyDist);
            }
        }

        springs.Add(new Spring(
            Math.Max(firstNoteSpace, firstNoteMin),
            firstNoteMin,
            inverseStretchStrength: 0));

        // Springs between adjacent timing columns (duration-proportional + skyline rods)
        // LILYPOND-REF: lily/spacing-basic.cc:107-162 — note_spacing uses left column's shortest-playing-duration.
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
            // LILYPOND-REF: lily/spacing-engraver.cc:200-253 — shortest_playing aggregated at the LEFT column.
            var shortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[i - 1], measuresToScan);
            var spring = SpacingRules.CreateTimingSpringMultiVoice(
                segmentDuration, shortestPlaying, baseShortestDuration);

            // LILYPOND-REF: lily/spacing-spanner.cc — apply rod from skyline collision
            // between items at adjacent timing points across ALL voices.
            // Take the maximum skyline distance across all voice pairs.
            timingToItems.TryGetValue(timings[i - 1], out var prevItems);
            timingToItems.TryGetValue(timings[i], out var nextItems);
            if (prevItems != null && nextItems != null)
            {
                double maxSkyDist = 0;
                foreach (var prev in prevItems)
                {
                    foreach (var next in nextItems)
                    {
                        double skyDist = SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0);
                        maxSkyDist = Math.Max(maxSkyDist, skyDist);
                    }
                }

                if (maxSkyDist > spring.MinDistance)
                {
                    spring = new Spring(
                        Math.Max(spring.IdealDistance, maxSkyDist),
                        maxSkyDist,
                        spring.InverseStretchStrength);
                }
            }

            springs.Add(spring);
        }

        // End spring: last column → barline (remaining duration)
        // LILYPOND-REF: lily/spacing-basic.cc:107-162 — note_spacing uses left column's shortest-playing-duration.
        var endDuration = totalDuration - timings[^1];
        var endShortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[^1], measuresToScan);
        var endSpring = SpacingRules.CreateTimingSpringMultiVoice(
            endDuration, endShortestPlaying, baseShortestDuration);

        // Apply skyline rod: last item → barline (max across all voices)
        if (timingToItems.TryGetValue(timings[^1], out var lastItems))
        {
            double maxSkyDist = 0;
            foreach (var item in lastItems)
            {
                double skyDist = SpacingRules.CalculateSkylineDistance(item, null, staffY: 0);
                maxSkyDist = Math.Max(maxSkyDist, skyDist);
            }

            if (maxSkyDist > endSpring.MinDistance)
            {
                endSpring = new Spring(
                    Math.Max(endSpring.IdealDistance, maxSkyDist),
                    maxSkyDist,
                    endSpring.InverseStretchStrength);
            }
        }
        springs.Add(endSpring);

        return springs.ToImmutableArray();
    }

    /// <summary>
    /// Calculates column layouts for a measure based on collected timings.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc - Each musical moment becomes a paper column
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    ///
    /// When precomputedSprings and precomputedForce are provided (from system-level solving),
    /// uses those directly. Otherwise creates springs and solves internally.
    /// </remarks>
    public ImmutableArray<ColumnLayout> LayoutColumns(Measure measure, double totalWidth, List<Fraction> timings,
                                                      double? baseShortestDuration = null,
                                                      IReadOnlyList<Measure>? allMeasures = null,
                                                      ImmutableArray<Spring>? precomputedSprings = null,
                                                      double? precomputedForce = null)
    {
        if (timings.Count == 0)
            return ImmutableArray<ColumnLayout>.Empty;

        // Calculate barline widths
        // LILYPOND-REF: lily/spacing-basic.cc:50-52 barline dimensions
        double startBarlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline);
        double endBarlineWidth = SpacingRules.GetBarlineWidth(measure.EndBarline);

        // Use precomputed springs or create them
        var springs = precomputedSprings ?? CreateTimingSprings(measure, timings, baseShortestDuration, allMeasures);
        if (springs.Length == 0)
            return ImmutableArray<ColumnLayout>.Empty;

        // Use precomputed force or solve internally
        double force;
        if (precomputedForce.HasValue)
        {
            force = precomputedForce.Value;
        }
        else
        {
            double targetWidth = totalWidth - startBarlineWidth - endBarlineWidth;
            var solver = new SpringSolver(springs);
            force = solver.SolveForWidth(targetWidth);
        }

        // Get positions from spring solver
        var positions = new SpringSolver(springs).GetPositions(force, startX: 0);

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
