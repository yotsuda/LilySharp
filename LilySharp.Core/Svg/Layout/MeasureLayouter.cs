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
        var measuresToScan = allMeasures ?? new[] { measure };
        var timingToItems = BuildTimingToItemsMap(measuresToScan);

        // Full-measure rests get compact rods, not proportional whole-note
        // spacing (see TryCreateAllRestSprings for the LilyPond reasoning).
        if (TryCreateAllRestSprings(measure, measuresToScan) is { } restSprings)
            return restSprings;

        var springs = new List<Spring>();

        // Spring 0: barline → first column (see CreateBarlineToFirstSpring).
        springs.Add(CreateBarlineToFirstSpring(timings, timingToItems));

        // Springs between adjacent timing columns (see CreateInterColumnSpring).
        for (int i = 1; i < timings.Count; i++)
            springs.Add(CreateInterColumnSpring(i, timings, timingToItems, measuresToScan, baseShortestDuration));

        // End spring: last column → barline (see CreateLastToBarlineSpring).
        springs.Add(CreateLastToBarlineSpring(timings, timingToItems, measuresToScan, totalDuration, baseShortestDuration));

        return springs.ToImmutableArray();
    }

    /// <summary>
    /// Builds the timing → items map used for skyline-based rod calculation:
    /// each column's minimum distance must account for collisions between items
    /// at adjacent timing points across ALL voices (accidentals, noteheads).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    /// LILYPOND-REF: lily/paper-column.cc — paper columns aggregate grobs from all staves.
    /// </remarks>
    private static Dictionary<Fraction, List<MusicItem>> BuildTimingToItemsMap(
        IReadOnlyList<Measure> measuresToScan)
    {
        var timingToItems = new Dictionary<Fraction, List<MusicItem>>();
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
        return timingToItems;
    }

    /// <summary>
    /// When EVERY voice rests the whole measure, returns two compact rod springs
    /// (not proportional whole-note spacing); otherwise null. The combined-timing
    /// path must compact exactly like the single-voice path, or line breaking and
    /// layout disagree about the measure's width and multi-measure-rest runs split
    /// or stretch.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/multi-measure-rest.cc:340-391 set_spacing_rods</remarks>
    private static ImmutableArray<Spring>? TryCreateAllRestSprings(
        Measure measure, IReadOnlyList<Measure> measuresToScan)
    {
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
        return null;
    }

    /// <summary>
    /// Spring 0: barline → first column. BREAKABLE spacing, not musical: the gap
    /// after a barline is governed by the BarLine space-alist, NOT the first
    /// note's duration, and it must never stretch under justification.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarLine space-alist —
    ///   (first-note . (semi-shrink-space . 1.3))
    /// LILYPOND-REF: lily/staff-spacing.cc Staff_spacing::get_spacing —
    ///   semi-shrink-space: fixed = d/2, ideal = d, is_stretchable = false
    ///   → inverse stretch strength 0; compressible only down to fixed.
    /// </remarks>
    private static Spring CreateBarlineToFirstSpring(
        List<Fraction> timings, Dictionary<Fraction, List<MusicItem>> timingToItems)
    {
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
            // the first note's column and is drawn hanging LEFT of it. Reserve
            // that hung width so the change doesn't jam against the barline.
            // LILYPOND-REF: lily/paper-column.cc — the non-musical (breakable)
            // column precedes the musical column of the same moment.
            double startPrefix = ChangeItemPrefixWidth(firstItems);
            firstNoteMin = Math.Max(firstNoteMin, startPrefix);

            // Leading grace notes on the first note hang left of its column, after
            // the barline (LilyPond gives the grace its own column between the
            // barline and the main note).
            startLeadGrace = LeadingGracePrefixWidth(firstItems);
        }

        if (startLeadGrace > 0)
        {
            // The grace is now the FIRST musical column after the barline, so the
            // barline→grace gap uses tight GRACE spacing (spacing-increment). The
            // whole front block is rigid (grace columns don't stretch).
            // LILYPOND-REF: scm/define-grobs.scm:1592 GraceSpacing
            //   (spacing-increment . 0.8) — grace columns space tighter than notes.
            // LILYPOND-REF: lily/grace-spacing-engraver.cc — barline → first grace
            //   column → … → main column.
            double graceApproach = GraceSpacingParameters.Default.SpacingIncrement;
            double front = Math.Max(firstNoteMin, graceApproach + startLeadGrace);
            return new Spring(front, front, inverseStretchStrength: 0);
        }
        return new Spring(
            Math.Max(firstNoteSpace, firstNoteMin),
            firstNoteMin,
            inverseStretchStrength: 0);
    }

    /// <summary>
    /// Spring connecting timing column <paramref name="i"/>-1 → <paramref name="i"/>:
    /// duration-proportional ideal refined by left-head width, stem-direction
    /// optical correction merged across voices, then skyline rods and hung-glyph
    /// (clef/key change, leading grace) prefix reservation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:107-162; lily/note-spacing.cc:204-315
    ///   stem_dir_correction; lily/spacing-spanner.cc:322-393 musical_column_spacing
    ///   + lily/spring.cc:104 merge_springs.
    /// </remarks>
    private Spring CreateInterColumnSpring(
        int i, List<Fraction> timings,
        Dictionary<Fraction, List<MusicItem>> timingToItems,
        IReadOnlyList<Measure> measuresToScan, double? baseShortestDuration)
    {
        // This spring connects timings[i-1] → timings[i]; its duration is
        // THAT segment. (A previous off-by-one used the FOLLOWING segment's
        // duration, clamping a half-note gap down to the next quarter's length.)
        Fraction segmentDuration = timings[i] - timings[i - 1];
        // LILYPOND-REF: lily/spacing-engraver.cc:200-253 — shortest_playing aggregated at the LEFT column.
        var shortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[i - 1], measuresToScan);
        var spring = SpacingRules.CreateTimingSpringMultiVoice(
            segmentDuration, shortestPlaying, baseShortestDuration);

        timingToItems.TryGetValue(timings[i - 1], out var prevItems);
        timingToItems.TryGetValue(timings[i], out var nextItems);

        // Refine the duration-based ideal to the LEFT column's actual head width
        // (LilyPond's note-spacing.cc:77), BEFORE the stem correction.
        if (prevItems != null)
            spring = SpacingRules.ApplyLeftHeadWidth(spring, prevItems);

        // Stem-direction optical correction ([Wanske]), merged across simultaneous
        // voices' wishes (single voice = its own wish; polyphony = averaged).
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
                // Rods are MINIMA, never ideals: the natural length stays
                // duration-based and the rod only blocks compression below the
                // collision distance. LILYPOND-REF: lily/spacing-spanner.cc — set_min_distance.
                spring = new Spring(
                    spring.IdealDistance,
                    maxSkyDist,
                    spring.InverseStretchStrength);
            }
        }

        // Mid-measure clef/key change (zero duration, shares the next timing) and
        // leading grace on the next note hang left of that column; reserve their
        // width here so the renderer's hung glyph has room.
        // LILYPOND-REF: lily/paper-column.cc — breakable columns precede the musical column.
        double prefixWidth = ChangeItemPrefixWidth(nextItems);
        prefixWidth += LeadingGracePrefixWidth(nextItems);
        if (prefixWidth > 0)
            spring = new Spring(
                spring.IdealDistance + prefixWidth,
                spring.MinDistance + prefixWidth,
                spring.InverseStretchStrength);

        return spring;
    }

    /// <summary>
    /// End spring: last column → barline (remaining duration), with left-head-width
    /// refinement and the last-item → barline skyline rod.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spacing-basic.cc:107-162; lily/note-spacing.cc:77.</remarks>
    private static Spring CreateLastToBarlineSpring(
        List<Fraction> timings, Dictionary<Fraction, List<MusicItem>> timingToItems,
        IReadOnlyList<Measure> measuresToScan, Fraction totalDuration, double? baseShortestDuration)
    {
        var endDuration = totalDuration - timings[^1];
        var endShortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[^1], measuresToScan);
        var endSpring = SpacingRules.CreateTimingSpringMultiVoice(
            endDuration, endShortestPlaying, baseShortestDuration);

        if (timingToItems.TryGetValue(timings[^1], out var lastItems))
        {
            endSpring = SpacingRules.ApplyLeftHeadWidth(endSpring, lastItems);

            double maxSkyDist = 0;
            foreach (var item in lastItems)
            {
                double skyDist = SpacingRules.CalculateSkylineDistance(item, null, staffY: 0);
                maxSkyDist = Math.Max(maxSkyDist, skyDist);
            }

            if (maxSkyDist > endSpring.MinDistance)
            {
                endSpring = new Spring(
                    endSpring.IdealDistance,
                    maxSkyDist,
                    endSpring.InverseStretchStrength);
            }
        }
        return endSpring;
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
            // SUM, not max: clef/key/time changes sharing a column are drawn side by
            // side (the renderer sequences them), so they need their combined width.
            // A lone change (the common case) sums to its own width — unchanged.
            w += itemW;
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

        // Sentinel end-column at the measure's total duration, positioned at the
        // content's right edge (where the end barline sits). Without it, a moment
        // that falls WITHIN the last note — e.g. a chord name on a beat inside a
        // half note — has no column past the last onset, so GetXForTiming snaps it
        // onto the last column and it collides with the chord placed there. With the
        // sentinel, GetXForTiming interpolates across the last note's span instead.
        if (columns.Count > 0)
        {
            double endX = startBarlineWidth + positions[timings.Count + 1];
            if (measure.TotalDuration > columns[^1].Timing)
                columns.Add(new ColumnLayout(measure.TotalDuration, endX, 0));
        }

        return columns.ToImmutable();
    }
}
