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
internal sealed class MeasureLayouter
{
    /// <summary>
    /// Predicate returning whether a music item is part of a beam group (and so
    /// carries NO flag). Set by <see cref="LayoutEngine"/> before a layout pass so
    /// note-to-note spacing does not reserve flag width for beamed notes. Null =>
    /// treat every note as unbeamed (flag reserved), the pre-beam-aware behaviour.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/note-spacing.cc — a beamed Stem has no Flag grob.</remarks>
    public Func<MusicItem, bool>? IsItemBeamed { get; set; }

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
        double startLeadGrace = 0;
        if (timingToItems.TryGetValue(timings[0], out var firstItems))
        {
            foreach (var item in firstItems)
            {
                double skyDist = SpacingRules.CalculateSkylineDistance(null, item, staffY: 0);
                firstNoteMin = Math.Max(firstNoteMin, skyDist);
            }

            // A zero-duration clef/key/time change at the MEASURE START shares
            // the first note's column and is drawn hanging LEFT of it. The
            // inter-column springs below (see ~line 290) reserve that hung
            // width via ChangeItemPrefixWidth, but the barline→first-column
            // spring did not — so a measure-opening change had nothing pushing
            // its column right and the hung glyph jammed against the barline.
            // Reserve it here too (glyph + padding on both sides).
            // LILYPOND-REF: lily/paper-column.cc — the non-musical (breakable)
            // column precedes the musical column of the same moment.
            double startPrefix = ChangeItemPrefixWidth(firstItems);
            firstNoteMin = Math.Max(firstNoteMin, startPrefix);

            // Leading grace notes on the first note hang left of its column, after
            // the barline (LilyPond gives the grace its own column between the
            // barline and the main note).
            startLeadGrace = LeadingGracePrefixWidth(firstItems);
        }

        Spring firstSpring;
        if (startLeadGrace > 0)
        {
            // The grace is now the FIRST musical column after the barline, so the
            // barline→grace gap uses tight GRACE spacing (spacing-increment), NOT
            // the wider regular barline→first-note space. The grace group span +
            // grace→main rod (startLeadGrace) then pushes the main note's column
            // right. The whole front block is rigid (grace columns don't stretch).
            // LILYPOND-REF: scm/define-grobs.scm:1592 GraceSpacing
            //   (spacing-increment . 0.8) — grace columns space tighter than notes.
            // LILYPOND-REF: lily/grace-spacing-engraver.cc — barline → first grace
            //   column → … → main column.
            double graceApproach = GraceSpacingParameters.Default.SpacingIncrement;
            double front = Math.Max(firstNoteMin, graceApproach + startLeadGrace);
            firstSpring = new Spring(front, front, inverseStretchStrength: 0);
        }
        else
        {
            firstSpring = new Spring(
                Math.Max(firstNoteSpace, firstNoteMin),
                firstNoteMin,
                inverseStretchStrength: 0);
        }
        springs.Add(firstSpring);

        // Springs between adjacent timing columns (duration-proportional + skyline rods)
        // LILYPOND-REF: lily/spacing-basic.cc:107-162 — note_spacing uses left column's shortest-playing-duration.
        for (int i = 1; i < timings.Count; i++)
        {
            // This spring connects timings[i-1] → timings[i]; its duration is
            // THAT segment. (A previous off-by-one used the FOLLOWING
            // segment's duration here, which clamped a half-note gap down to
            // the next quarter's length — half and quarter came out equal.)
            Fraction segmentDuration = timings[i] - timings[i - 1];
            // LILYPOND-REF: lily/spacing-engraver.cc:200-253 — shortest_playing aggregated at the LEFT column.
            var shortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[i - 1], measuresToScan);
            var spring = SpacingRules.CreateTimingSpringMultiVoice(
                segmentDuration, shortestPlaying, baseShortestDuration);

            // LILYPOND-REF: lily/spacing-spanner.cc — apply rod from skyline collision
            // between items at adjacent timing points across ALL voices.
            // Take the maximum skyline distance across all voice pairs.
            timingToItems.TryGetValue(timings[i - 1], out var prevItems);
            timingToItems.TryGetValue(timings[i], out var nextItems);

            // Refine the duration-based ideal to the LEFT column's actual head
            // width (LilyPond's note-spacing.cc:77), BEFORE the stem correction
            // that LilyPond applies afterward. Without this every note gap is
            // ~0.1 ss tighter than LilyPond (the head-width vs generic-increment
            // difference).
            if (prevItems != null)
                spring = SpacingRules.ApplyLeftHeadWidth(spring, prevItems);

            // Stem-direction optical correction ([Wanske]): up-stem→down-stem
            // gets extra space, down→up less. LilyPond computes it per voice
            // inside each Note_spacing wish, then merges the simultaneous
            // voices' wishes via merge_springs. For monophonic music this is
            // the single voice's wish (base + its correction); for polyphony
            // the per-voice corrections are averaged via Spring.Merge instead
            // of being dropped.
            // LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction
            // LILYPOND-REF: lily/spacing-spanner.cc:322-393 musical_column_spacing
            //   + lily/spring.cc:104 merge_springs
            spring = SpacingRules.MergeVoiceStemWishes(
                spring, measuresToScan, timings[i - 1], timings[i],
                NoteSpacingParameters.Default);
            if (prevItems != null && nextItems != null)
            {
                double maxSkyDist = 0;
                foreach (var prev in prevItems)
                {
                    bool prevBeamed = IsItemBeamed?.Invoke(prev) ?? false;
                    foreach (var next in nextItems)
                    {
                        double skyDist = SpacingRules.CalculateSkylineDistance(
                            prev, next, staffY: 0, prevBeamed: prevBeamed);
                        maxSkyDist = Math.Max(maxSkyDist, skyDist);
                    }
                }

                if (maxSkyDist > spring.MinDistance)
                {
                    // Rods are MINIMA, never ideals: the spring's natural
                    // length stays duration-based and the rod only blocks
                    // compression below the collision distance (Spring's
                    // blocking force handles min > ideal). Inflating the
                    // ideal here used to swallow optical corrections and
                    // over-stretch dense columns in justified lines.
                    // LILYPOND-REF: lily/spacing-spanner.cc — set_min_distance.
                    spring = new Spring(
                        spring.IdealDistance,
                        maxSkyDist,
                        spring.InverseStretchStrength);
                }
            }

            // Mid-measure clef/key-signature changes have zero duration and
            // share the next note's timing. LilyPond puts them in their own
            // non-musical column BEFORE the musical column of that moment;
            // approximate by reserving the change's width in this spring —
            // the renderer hangs the glyph left of the column to match.
            // LILYPOND-REF: lily/paper-column.cc — breakable (non-musical)
            // columns precede the musical column of the same moment.
            double prefixWidth = ChangeItemPrefixWidth(nextItems);
            // Leading grace on the NEXT note hangs left of its column, between the
            // two notes (same reservation as a hung clef change, see ~line 204).
            prefixWidth += LeadingGracePrefixWidth(nextItems);
            if (prefixWidth > 0)
                spring = new Spring(
                    spring.IdealDistance + prefixWidth,
                    spring.MinDistance + prefixWidth,
                    spring.InverseStretchStrength);

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
            // LilyPond's note-spacing.cc:77 head-width refinement applies to every
            // Note_spacing spring, including the last note → barline one, so the
            // gap after the final note matches an interior gap of the same value.
            endSpring = SpacingRules.ApplyLeftHeadWidth(endSpring, lastItems);

            double maxSkyDist = 0;
            foreach (var item in lastItems)
            {
                double skyDist = SpacingRules.CalculateSkylineDistance(item, null, staffY: 0);
                maxSkyDist = Math.Max(maxSkyDist, skyDist);
            }

            if (maxSkyDist > endSpring.MinDistance)
            {
                // Rod = minimum only; see the loop above.
                endSpring = new Spring(
                    endSpring.IdealDistance,
                    maxSkyDist,
                    endSpring.InverseStretchStrength);
            }
        }
        springs.Add(endSpring);

        return springs.ToImmutableArray();
    }

    /// <summary>
    /// Width a zero-duration clef/key-signature change at a timing column
    /// needs in FRONT of that column (glyph + padding on both sides). When
    /// several staves change at the same moment the glyphs align vertically,
    /// so the MAX (not the sum) is reserved.
    /// </summary>
    private static double ChangeItemPrefixWidth(IEnumerable<MusicItem>? items)
    {
        if (items == null) return 0;
        double w = 0;
        foreach (var item in items)
        {
            double itemW = item switch
            {
                ClefChangeItem cc =>
                    SpacingRules.GetClefChangeWidth(cc.NewClef) + 2 * GlyphMetrics.ClefChangePadding,
                KeySignatureChangeItem kc =>
                    SpacingRules.GetKeySignatureChangeWidth(kc) + 2 * GlyphMetrics.ClefChangePadding,
                TimeSignatureChangeItem tc =>
                    SpacingRules.GetTimeSignatureChangeWidth(tc) + 2 * GlyphMetrics.ClefChangePadding,
                _ => 0
            };
            w = Math.Max(w, itemW);
        }
        return w;
    }

    /// <summary>
    /// Width that leading grace notes need in FRONT of their main note's column.
    /// Grace notes hang to the left of the note (like a mid-measure clef change),
    /// so the spring into the column reserves their group width. When several
    /// voices have grace at the same moment the groups align, so the MAX is taken.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 — grace columns precede
    ///   the main note's musical column; their span is reserved before it.
    /// The width equals SpacingRules.CalculateGraceGroupSpringWidth (grace springs
    /// plus the grace→main rod), the same measure GraceNoteEngraver uses to PLACE
    /// the group, so reserved space and drawn space agree.
    /// </remarks>
    private static double LeadingGracePrefixWidth(IEnumerable<MusicItem>? items)
    {
        if (items == null) return 0;
        double w = 0;
        foreach (var item in items)
        {
            var grace = item switch
            {
                NoteItem n => n.LeadingGrace,
                ChordItem c => c.LeadingGrace,
                _ => ImmutableArray<GraceNoteInfo>.Empty
            };
            if (!grace.IsDefaultOrEmpty)
                w = Math.Max(w, SpacingRules.CalculateGraceGroupSpringWidth(grace));
        }
        return w;
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
