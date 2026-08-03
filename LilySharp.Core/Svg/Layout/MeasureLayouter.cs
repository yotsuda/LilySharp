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
    // Beam membership is NOT threaded through this class. It is a property of the item —
    // NoteItem.BeamId / ChordItem.BeamId (and the IsBeamed it answers), baked by
    // MeasureCollector's ResolveBeamStemDirections before any spacing runs, which is also
    // how the knee correction asks whether two columns share ONE beam. So every consumer
    // reads the same
    // answer and none can be handed a wrong one. That mirrors LilyPond, where a beamed
    // stem's Flag grob has already SUICIDED by spacing time (lily/stem-engraver.cc:165-172)
    // and the column skyline simply walks the grobs that exist
    // (lily/separation-item.cc:130-164): nothing there asks whether a note is beamed.
    // ⚠️ It used to be a settable predicate here, and the line-break gate — which builds its
    // springs through this very class — never set it, so the gate priced every beamed note
    // WITH a flag: a beamed eighth's skyline minimum read 2.532200 instead of 1.704200 and
    // the merge_springs headroom (minimum + 0.3) then lifted its IDEAL from LilyPond's
    // 2.504200 to 2.832200, a spring the layout never uses. On probe JN that is +0.984029
    // per bar, enough that the gate could not see LilyPond's five-bar first system as a
    // natural fit and cut 4,4,4,4 where LilyPond sets 5,5,6 (ledger point
    // justified.first-system.heads, audit/lp-geometry/probes/jn-line-forces.ly).

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
    /// Derives item slots from the already-solved timing COLUMNS so each item's X
    /// equals the column-grid X the renderer draws its notehead at (see
    /// SharedRenderer.EnumerateStaffItems / MeasureLayout.GetXForTiming). This makes
    /// <c>MeasureLayout.Items[i].X == GetXForTiming(itemTiming)</c> by construction, so
    /// every consumer that reads the raw item slot (Hairpin / TextSpanner /
    /// TrillSpanner / TieVariant) stays on the notehead grid instead of drifting when a
    /// bar opens with a mid-piece meter/clef change — whose zero-duration grob would
    /// otherwise consume an item spring slot and shove the following notes right.
    /// </summary>
    /// <remarks>
    /// An item's onset timing is always one of the union timings the columns were
    /// built from (a zero-duration change item shares the next note's column), so the
    /// exact-match branch mirrors <see cref="LayoutUtilities.GetItemXOffset"/>. Width is
    /// the distance to the next item's X (last item → the measure's content right edge),
    /// keeping the pre-existing slot-width semantics its readers rely on. Returns Empty
    /// when there are no columns (degenerate all-zero-duration measure); the caller then
    /// falls back to the item-spring layout.
    /// </remarks>
    public static ImmutableArray<ItemLayout> LayoutItemsFromColumns(
        Measure measure, ImmutableArray<ColumnLayout> columns, double totalWidth)
    {
        if (measure.Items.Length == 0 || columns.IsDefaultOrEmpty || columns.Length == 0)
            return ImmutableArray<ItemLayout>.Empty;

        double endBarlineWidth = SpacingRules.GetBarlineWidth(measure.EndBarline);
        double contentRightX = totalWidth - endBarlineWidth;

        var xs = new double[measure.Items.Length];
        var timing = Fraction.Zero;
        for (int i = 0; i < measure.Items.Length; i++)
        {
            xs[i] = LayoutUtilities.NearestColumnX(columns, timing);
            timing += measure.Items[i].Duration;
        }

        var layouts = ImmutableArray.CreateBuilder<ItemLayout>(measure.Items.Length);
        for (int i = 0; i < measure.Items.Length; i++)
        {
            double width = (i + 1 < measure.Items.Length ? xs[i + 1] : contentRightX) - xs[i];
            layouts.Add(new ItemLayout(i, xs[i], Math.Max(0, width)));
        }
        return layouts.MoveToImmutable();
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
    /// <param name="nextMeasure">
    /// The measure FOLLOWING this one, when known. Needed because a clef change opening
    /// it is engraved before the shared bar line, so its width is charged to THIS
    /// measure's closing spring — see <see cref="SpacingRules.BoundaryClefAllowance"/>.
    /// </param>
    public ImmutableArray<Spring> CreateTimingSprings(
        Measure measure, List<Fraction> timings,
        double? baseShortestDuration = null,
        IReadOnlyList<Measure>? allMeasures = null,
        Measure? nextMeasure = null)
    {
        if (timings.Count == 0)
            return ImmutableArray<Spring>.Empty;

        // LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
        // Build a map from timing → items for skyline-based rod calculation.
        // Each column's minimum distance must account for collisions between
        // items at adjacent timing points across ALL voices (e.g., accidentals, noteheads).
        // LILYPOND-REF: lily/paper-column.cc — paper columns aggregate grobs from all staves
        var measuresToScan = allMeasures ?? new[] { measure };

        // Total duration measured across ALL staves at this column — the `timings`
        // are the UNION, so the spring count must match them. When the PRIMARY
        // measure is an empty placeholder (`| |`) but a sibling staff plays real
        // notes here, the primary's own duration is 0 while the union is not; a
        // duration read from the primary alone would return no springs (spring
        // count != timings.Count + 1) and LayoutColumns would index past the
        // solved positions. The measure is only truly empty — and collapses to
        // its rigid placeholder spring upstream — when EVERY staff is empty here.
        var totalDuration = Fraction.Zero;
        foreach (var m in measuresToScan)
        {
            var d = Fraction.Zero;
            foreach (var item in m.Items)
                d += item.Duration;
            if (d > totalDuration)
                totalDuration = d;
        }

        if (totalDuration == Fraction.Zero)
            return ImmutableArray<Spring>.Empty;
        var timingToItems = BuildTimingToItemsMap(measuresToScan);

        // NOTE: full-measure rests get ORDINARY springs, mirroring LilyPond — the
        // compaction of a multi-measure rest comes from the run-level rod applied
        // across the collapsed run (SpacingRules.MmrRodDistance), not from shrinking
        // each rested measure. See the note in SpacingRules.CreateSpringsForMeasure.

        var springs = new List<Spring>();

        // Spring 0: barline → first column (see CreateBarlineToFirstSpring).
        springs.Add(CreateBarlineToFirstSpring(timings, timingToItems));

        // Springs between adjacent timing columns (see CreateInterColumnSpring).
        for (int i = 1; i < timings.Count; i++)
            springs.Add(CreateInterColumnSpring(i, timings, timingToItems, measuresToScan, baseShortestDuration));

        // End spring: last column → barline (see CreateLastToBarlineSpring).
        springs.Add(CreateLastToBarlineSpring(timings, timingToItems, measuresToScan, totalDuration,
            baseShortestDuration, SpacingRules.BoundaryClefAllowance(measure.EndBarline, nextMeasure)));

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

    /// <summary>The MUSICAL item in <paramref name="m"/> (one voice's sequential items) that
    /// STARTS exactly at <paramref name="t"/>, or null when this voice has no notehead at that
    /// column — so a separation rod is only raised between two columns the SAME voice
    /// occupies.</summary>
    /// <remarks>
    /// A zero-duration clef/key/time change shares the following note's timing but belongs to
    /// the NON-musical column, so it is skipped: the rod this feeds is between two musical
    /// columns, and the change column's own rod is
    /// <see cref="SpacingRules.MidMeasureChangeGaps"/>'s (mid-measure) or
    /// <see cref="SpacingRules.BarlineToFirstColumnSpring"/>'s (at a bar line). Returning the
    /// change item here measured the gap from a glyph that is not in either column being
    /// spaced — and through the change-item branch of the extent helpers, which was still on
    /// the centre basis.
    /// </remarks>
    private static MusicItem? ItemStartingAt(Measure m, Fraction t)
    {
        var acc = Fraction.Zero;
        foreach (var item in m.Items)
        {
            if (acc == t && !SpacingRules.IsMidMeasureChangeColumn(item))
                return item;
            if (acc > t) break;
            acc += item.Duration;
        }
        return null;
    }

    /// <summary>
    /// Spring 0: barline → first column. BREAKABLE spacing, not musical — the shape
    /// lives in <see cref="SpacingRules.BarlineToFirstColumnSpring"/>, shared with the
    /// item spring system so the two cannot drift. This side only supplies the
    /// column's items and decides full-measure-extra-space.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:446-472 fills_measure — the single
    /// musical column after this barline is followed straight by the next breakable
    /// column (timings.Count == 1), spanning the measure.
    /// The column test is SpacingRules.IsMusicalColumn, LilyPond's
    /// Paper_column::is_musical, so a full-measure REST counts exactly as a whole note
    /// does — the same predicate SpacingRules.FillsMeasure applies on the line-breaking
    /// side, because the two spring gates must price identically.
    /// </remarks>
    private static Spring CreateBarlineToFirstSpring(
        List<Fraction> timings, Dictionary<Fraction, List<MusicItem>> timingToItems)
    {
        timingToItems.TryGetValue(timings[0], out var firstItems);
        bool fillsMeasure =
            timings.Count == 1
            && firstItems != null
            && firstItems.Any(SpacingRules.IsMusicalColumn);
        return SpacingRules.BarlineToFirstColumnSpring(firstItems, fillsMeasure);
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
        // LILYPOND-REF: lily/spacing-basic.cc:144 — measure length caps shortest_playing (mmrest guard).
        Fraction measureLength = Fraction.Zero;
        foreach (var vm in measuresToScan)
        {
            var total = Fraction.Zero;
            foreach (var item in vm.Items)
                total += item.Duration;
            if (total > measureLength)
                measureLength = total;
        }
        var spring = SpacingRules.CreateTimingSpringMultiVoice(
            segmentDuration, shortestPlaying, baseShortestDuration,
            measureLength: measureLength > Fraction.Zero ? measureLength : null);

        timingToItems.TryGetValue(timings[i - 1], out var prevItems);
        timingToItems.TryGetValue(timings[i], out var nextItems);

        // Refine the duration-based ideal to the LEFT column's actual head width
        // (LilyPond's note-spacing.cc:77), BEFORE the stem correction — unless the pair
        // straddles a VOICE boundary, where LilyPond's wish does not reach the right column
        // and the spring keeps its raw duration ideal (spacing-spanner.cc:352-358 / :380-391).
        // That is why nextItems is passed: see SpacingRules.CrossesVoiceBoundary.
        if (prevItems != null)
            spring = SpacingRules.ApplyLeftHeadWidth(spring, prevItems, nextItems);

        // A mid-measure clef/key/time change (zero duration, so it shares the NEXT
        // column's timing) gets its own non-musical column in LilyPond, and the gaps
        // around it are priced from the ideal as it stands HERE — before the stem
        // correction, which LilyPond applies afterwards (note-spacing.cc:87-109 then
        // :111) and which contributes nothing when the right column is non-musical:
        // stem_dir_correction only looks at grobs with the Note_column interface
        // (:235-238), and a change column has none. Taking the correction first put the
        // mid-measure clef of probe MC 0.188 too far right, because the low notes after
        // it earn a correction that LilyPond charges to a pair this one is not.
        var changeGaps = SpacingRules.MidMeasureChangeGaps(
            nextItems, prevItems, spring.IdealDistance);

        // Stem-direction optical correction ([Wanske]), merged across simultaneous
        // voices' wishes (single voice = its own wish; polyphony = averaged).
        if (changeGaps is null)
            spring = SpacingRules.MergeVoiceStemWishes(
                spring, measuresToScan, timings[i - 1], timings[i],
                NoteSpacingParameters.Default);
        // Collision rods are PER VOICE: two noteheads force a horizontal minimum only when the
        // SAME voice puts one at each of these adjacent columns. Pairing items across voices/staves
        // (as the aggregated prev/nextItems do, all at staffY 0) made a triplet note in ONE staff
        // and a straight eighth in ANOTHER clash as if stacked, so the triplet's off-beat columns
        // over-widened the other staff's eighths (they should stay evenly spaced, the triplet notes
        // tucking between them). Compute the rod per voice-measure and take the max. A rod is a
        // MINIMUM, never an ideal — the natural length stays duration-based.
        // LILYPOND-REF: lily/spacing-spanner.cc — separation rods come from each staff's own
        // Separation_item, not a cross-staff aggregate.
        // ⚠️ THE COLUMNS' INK ENTERS THIS SPRING TWICE, AS TWO DIFFERENT NUMBERS, and reading
        // one of them for both jobs is what made every floor-bound pair 0.100000 too wide
        // until 2026-08-02 (session 72). LilyPond raises TWO constraints over one column pair:
        //   the SPRING's minimum   = the padding-free skyline distance   (note-spacing.cc:78-83)
        //   the ROD                = that distance + the spanner's 0.1   (separation-item.cc:47-68)
        // and merge_springs' headroom is measured from the FIRST of them
        // (spring.cc:122 avg_distance = max (min_distance + 0.3, avg_distance)). This method
        // used to compute only the rod and hand it to EnsureMinDistance BEFORE the headroom,
        // so every gap the floor decided came out at skyline + 0.1 + 0.3. The rod is a floor
        // on the COMPRESSED length alone: being 0.2 under the headroom's answer, it cannot
        // bind at force >= 0, which is exactly what the note on ApplyMergeSpringsHeadroom
        // already said in writing.
        // MEASURED (scratch books beside audit/lp-geometry/probes/flagged-stem-reach.ly): a
        // plain `c''4 dis''4` — no flag anywhere in it — carried the identical +0.100000 that
        // the three flag points share, at every accidental width and both stem directions,
        // while every spring-bound book in the same set stayed EXACT.
        double maxSkyDist = 0;
        double maxRod = 0;
        foreach (var vm in measuresToScan)
        {
            var prev = ItemStartingAt(vm, timings[i - 1]);
            var next = ItemStartingAt(vm, timings[i]);
            if (prev == null || next == null)
                continue;
            // LILYPOND-REF: lily/note-spacing.cc:78-83 Note_spacing::get_spacing — the
            //   spring's own minimum, taken with the right column's skyline-vertical-padding
            //   and with NO spanner padding.
            maxSkyDist = Math.Max(maxSkyDist,
                SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0));
            // LILYPOND-REF: lily/spacing-spanner.cc:229-296 Spacing_spanner::set_column_rods
            //   raises a rod over every pair of columns that can reach each other, via
            //   lily/separation-item.cc:47-68 Separation_item::set_distance.
            maxRod = Math.Max(maxRod,
                SpacingRules.SeparationRodDistance(prev, next, staffY: 0));
        }
        // LILYPOND-REF: lily/spring.cc:155-159 Spring::ensure_min_distance — raising the
        // minimum leaves BOTH strengths where the duration spring put them, so the
        // compressibility stays fraction * (duration_space - increment) and does not become
        // ideal - skyline. Measured against LilyPond's own compressed line: 1.698045 for a
        // quarter-to-quarter spring (audit/lp-geometry/probes/compressed-line-force.ly).
        spring = spring.EnsureMinDistance(maxSkyDist);

        // The change column's two gaps, computed above, become this one spring — see
        // SpacingRules.MidMeasureChangeGaps for the derivation, the measurements, and what
        // a single spring cannot carry.
        // LILYPOND-REF: lily/note-spacing.cc:103-108 (left) + lily/staff-spacing.cc:166-215
        //   (right); lily/paper-column.cc — the non-musical column precedes the musical
        //   column of the same moment.
        if (changeGaps is { } gaps)
        {
            spring = new Spring(
                gaps.TotalIdeal,
                Math.Max(spring.MinDistance, gaps.MinDistance),
                spring.InverseStretchStrength);
        }

        // Leading grace on the next note hangs left of that column; reserve its width here
        // so the renderer's hung glyphs have room — and shrink the APPROACH by LilyPond's
        // 0.8 first, which is the half this used to skip (SpacingRules.SpringIntoGraceRun).
        spring = SpacingRules.SpringIntoGraceRun(
            spring,
            SpacingRules.LeadingGraceRunSpan(nextItems),
            SpacingRules.LeadingGracePrefixWidth(nextItems));

        // LilyPond merges every wish through merge_springs, which floors the ideal at
        // min + 0.3. A no-op for an ordinary note-to-note ideal (~3.0 vs a ~1.8 floor).
        // LILYPOND-REF: lily/spacing-spanner.cc:380-393 note_spacing — `merge_springs`
        //   is taken whenever the wish list is non-empty, i.e. also for a single wish.
        // LILYPOND-REF: lily/spring.cc:122 — avg_distance = max (min_distance + 0.3, …).
        spring = SpacingRules.ApplyMergeSpringsHeadroom(spring);

        // …and only NOW the rod, which is a floor on the COMPRESSED length and nothing else:
        // it stands 0.1 above the same skyline distance the headroom just put 0.3 above, so
        // it cannot reach the ideal and cannot move it.
        return spring.EnsureMinDistance(maxRod);
    }

    /// <summary>
    /// End spring: last column → barline (remaining duration), with left-head-width
    /// refinement and the last-item → barline skyline rod.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spacing-basic.cc:107-162; lily/note-spacing.cc:77.</remarks>
    private static Spring CreateLastToBarlineSpring(
        List<Fraction> timings, Dictionary<Fraction, List<MusicItem>> timingToItems,
        IReadOnlyList<Measure> measuresToScan, Fraction totalDuration, double? baseShortestDuration,
        double boundaryClefAllowance = 0)
    {
        var endDuration = totalDuration - timings[^1];
        var endShortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[^1], measuresToScan);
        var endSpring = SpacingRules.CreateTimingSpringMultiVoice(
            endDuration, endShortestPlaying, baseShortestDuration);

        if (timingToItems.TryGetValue(timings[^1], out var lastItems))
        {
            endSpring = SpacingRules.ApplyLeftHeadWidth(endSpring, lastItems);

            // Stem-direction optical correction, with the bar line standing in for the
            // right-hand stem. LilyPond runs stem_dir_correction on THIS spring too,
            // not only between musical columns; omitting it left a stemmed note ~0.24 ss
            // too close to the bar line.
            // Merged one wish per voice, exactly as the inter-column spring is —
            // LilyPond dispatches a musical → breakable pair to the same
            // musical_column_spacing / merge_springs path.
            // LILYPOND-REF: lily/note-spacing.cc:111 + :243-264;
            // lily/spacing-spanner.cc:183-199 + :322-393.
            endSpring = SpacingRules.MergeVoiceStemWishesToBarline(
                endSpring, measuresToScan, timings[^1], NoteSpacingParameters.Default);

            double maxSkyDist = 0;
            foreach (var item in lastItems)
            {
                double skyDist = SpacingRules.CalculateSkylineDistance(item, null, staffY: 0);
                maxSkyDist = Math.Max(maxSkyDist, skyDist);
            }

            // LILYPOND-REF: lily/spring.cc:155-159 Spring::ensure_min_distance.
            endSpring = endSpring.EnsureMinDistance(maxSkyDist);

            // NOTE: full-measure-extra-space is NOT applied here. LilyPond passes it
            // as `situational_space` to Staff_spacing::get_spacing, i.e. to the
            // barline → NEXT column spring, keyed on the measure that FOLLOWS the
            // barline — see CreateBarlineToFirstSpring. Adding it here charged it to
            // the wrong spring (and to the preceding measure), which mis-attributed
            // 1.0 ss when comparing measure-by-measure against LilyPond.
            // LILYPOND-REF: lily/spacing-spanner.cc:484-489.
        }

        // A clef change opening the NEXT measure is drawn before this bar line, so its
        // width belongs to this closing gap. It enters the MINIMUM: LilyPond keeps the
        // duration-based ideal measured to the bar line itself (note-spacing.cc:99-100
        // subtracts the bar line's column-internal offset), which is the frame this
        // spring is already in. LILYPOND-REF: SpacingRules.BoundaryClefAllowance.
        // LILYPOND-REF: lily/spring.cc:143-153 set_min_distance — the minimum moves, the
        // strengths do not.
        if (boundaryClefAllowance > 0)
            endSpring = endSpring.WithMinDistance(
                endSpring.MinDistance + boundaryClefAllowance);

        // ...and then merge_springs' headroom lifts the IDEAL off that minimum, which is
        // what actually places the bar line when a clef sits before it: the ideal above
        // discounts the clef's whole width, so without this floor the clef is drawn back
        // over the preceding note.
        //   note -> bar line = max (clef-less ideal, skyline + 0.3 + clef allowance)
        // Measured on 2.24.4 (`c'4 d' e' f' \clef bass g4 a b c'`):
        //   max (1.934752, 1.504212 + 0.3 + 2.84668) = 4.650892, and the dumped grobs
        //   give 22.357657 - 17.706765 = 4.650892.
        // LILYPOND-REF: lily/spacing-spanner.cc:380-393 note_spacing -> merge_springs;
        //   lily/spring.cc:122 avg_distance = max (min_distance + 0.3, avg_distance).
        // LILYPOND-REF: lily/note-spacing.cc:78-83 — the spring MINIMUM is the
        //   padding-free skyline distance, which is what the 0.3 is measured from.
        return SpacingRules.ApplyMergeSpringsHeadroom(endSpring);
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
