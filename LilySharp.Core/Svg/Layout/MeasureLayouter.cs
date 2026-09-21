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
/// LILYPOND-REF: lily/spacing-basic.cc:108-111 Spacing_spanner::note_spacing
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
        Rendering.ScoreTextMetrics fonts,
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
        var springs = precomputedSprings ?? SpacingRules.CreateSpringsForMeasure(fonts, measure);

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
    /// <param name="leftBound">The bar line drawn at the bar's LEFT bounding column — its own
    /// start bar line, or the previous bar's end when it declares none
    /// (<see cref="SpacingRules.RunLeftBoundBarline"/>). Null reads the measure's own start
    /// line, a single line standing in where it declares none — the single-measure callers.</param>
    /// <param name="fonts">The score's text metrics — a mid-measure meter change's column
    /// reads the plan for a compound numerator's <c>+</c> (SpacingRules.GetTimeSignatureChangeWidth).</param>
    public ImmutableArray<Spring> CreateTimingSprings(
        Rendering.ScoreTextMetrics fonts,
        Measure measure, List<Fraction> timings,
        SpacingOptions? spacing = null,
        IReadOnlyList<Measure>? allMeasures = null,
        Measure? nextMeasure = null,
        BarlineType? leftBound = null,
        IReadOnlyList<Staff>? stavesOfMeasures = null)
    {
        if (timings.Count == 0)
            return ImmutableArray<Spring>.Empty;
        var so = spacing ?? SpacingOptions.Default;

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
        // Indexed, not foreach, at every walk of measuresToScan in this file: the `??` above
        // leaves it an interface (a List in one arm, an array in the other), so its type
        // cannot be narrowed — and foreach over an interface boxes an enumerator (RULES §5.3).
        for (int mi = 0; mi < measuresToScan.Count; mi++)
        {
            var m = measuresToScan[mi];
            var d = Fraction.Zero;
            foreach (var item in m.Items)
                d += item.Duration;
            if (d > totalDuration)
                totalDuration = d;
        }

        if (totalDuration == Fraction.Zero)
            return ImmutableArray<Spring>.Empty;
        var columns = BuildTimingColumns(measuresToScan, timings);

        // NOTE: full-measure rests get ORDINARY springs, mirroring LilyPond — the
        // compaction of a multi-measure rest comes from the run-level rod applied
        // across the collapsed run (SpacingRules.MmrRodDistance), not from shrinking
        // each rested measure. See the note in SpacingRules.CreateSpringsForMeasure.

        var springs = new List<Spring>();

        // Rods raised over the neighbors of PRUNED loose change columns — they span two
        // or more springs, so they go through the blocking-force machinery, not a
        // single spring's minimum. LILYPOND-REF: lily/spacing-determine-loose-columns.cc:180-184
        //   set_distances_for_loose_col — r.item_drul_ = next_door; r.add_to_cols ().
        var looseRods = new List<(int Left, int Right, double Distance)>();

        // Whether a column AFTER the first kept one was dropped as unused: the union of every
        // voice's moments against the kept list. A skip's onset is one such column, and so is
        // the moment any event ENDS where nothing starts — a skip that opens the bar beside a
        // rest still leaves a column where it stops. MEASURED, 2.26.0, scratch/p388/fm/end.ly:
        // `<< { r1 } \\ { s2 } >>` and `<< { r1 } \\ { s4 } >>` read bar line → rest 1.09 and
        // a 6.688 bar, a lone r1 2.09 and 7.688; a combined part's `<< r1 s2 s4 >>` is the
        // same bar (audit/lpreg/pcsm.log, and scratch/p388/fm/span-fm0.ly zeroes
        // full-measure-extra-space to show the 1.0 is that quantity).
        // LILYPOND-REF: lily/simultaneous-music-iterator.cc:136-146 Simultaneous_music_iterator::pending_moment — the next timestep is the EARLIEST child's pending moment, a skip's end among them.
        // LILYPOND-REF: lily/spacing-spanner.cc:446-472 Spacing_spanner::fills_measure — !is_used (next) on that column.
        bool droppedOnsetFollows = false;
        for (int mi = 0; mi < measuresToScan.Count; mi++)
        {
            var m = measuresToScan[mi];
            var t = Fraction.Zero;
            foreach (var item in m.Items)
            {
                var end = t + item.Duration;
                if ((item is RestItem { IsSpacer: true } && t > timings[0] && !timings.Contains(t))
                    || (item.Duration > Fraction.Zero && end > timings[0] && end < totalDuration
                        && !timings.Contains(end)))
                {
                    droppedOnsetFollows = true;
                    break;
                }
                t += item.Duration;
            }
            if (droppedOnsetFollows) break;
        }

        // Spring 0: barline → first column (see CreateBarlineToFirstSpring), one Staff_spacing
        // wish per staff when the caller says which staff each measure belongs to.
        springs.Add(CreateBarlineToFirstSpring(
            fonts, timings, columns, measure,
            leftBound ?? (measure.StartBarline == BarlineType.None ? BarlineType.Single : measure.StartBarline),
            droppedOnsetFollows, so, StaffItemsAt(measuresToScan, stavesOfMeasures, timings[0])));

        // Springs between adjacent timing columns (see CreateInterColumnSpring).
        for (int i = 1; i < timings.Count; i++)
            springs.Add(CreateInterColumnSpring(fonts, i, timings, columns, measuresToScan,
                so, looseRods));

        // End spring: last column → barline (see CreateLastToBarlineSpring).
        springs.Add(CreateLastToBarlineSpring(fonts, timings, columns, measuresToScan, totalDuration,
            so, SpacingRules.BoundaryClefAllowance(fonts, measure.EndBarline, nextMeasure),
            SpacingRules.LeadingMusicalItems(nextMeasure)));

        return looseRods.Count > 0
            ? SpringSolver.ApplyRods(springs.ToImmutableArray(), looseRods)
            : springs.ToImmutableArray();
    }

    /// <summary>
    /// The items each SPRING COLUMN holds, index-aligned with <paramref name="timings"/>:
    /// each column's minimum distance must account for collisions between items at
    /// adjacent timing points across ALL voices (accidentals, noteheads).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️⚠️ THERE IS NO MAP. This was a <c>Dictionary&lt;Fraction, List&lt;MusicItem&gt;&gt;</c>
    /// until session 455, and every one of its four reads was <c>TryGetValue(timings[k])</c>
    /// — at <c>timings[0]</c>, <c>timings[i-1]</c>, <c>timings[i]</c> and
    /// <c>timings[^1]</c>. A map keyed by a value nobody holds except as an INDEX is an
    /// array; <paramref name="timings"/> arrives sorted ascending
    /// (<c>MultiStaffLayouter.CollectAllTimingsForMeasure</c> ends on <c>Sort()</c>), so a
    /// cursor assigns each item its column with no hash at all.
    /// </para>
    /// <para>
    /// MEASURED (2026-09-21, session 455; the reader's corpus, 231 books x 8 forward
    /// keystrokes, Release): the dictionary cost 3,900 + 562 and its value lists
    /// 2,511 + 1,435 = 8,408 B/keystroke of array and object together.
    /// </para>
    /// <para>
    /// ⚠️ AN ONSET THAT IS NOT A COLUMN IS DROPPED, and it was already unread. The map
    /// bucketed EVERY onset, including the unused columns
    /// <c>MultiStaffLayouter.PruneSpacerOnlyOnsets</c> had already taken out of
    /// <paramref name="timings"/> — built, filled, and never asked for.
    /// </para>
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    /// LILYPOND-REF: lily/paper-column.cc — paper columns aggregate grobs from all staves.
    /// </remarks>
    private static ItemColumn[] BuildTimingColumns(
        IReadOnlyList<Measure> measuresToScan, List<Fraction> timings)
    {
        var columns = new ItemColumn[timings.Count];
        for (int mi = 0; mi < measuresToScan.Count; mi++)
        {
            var m = measuresToScan[mi];
            var t = Fraction.Zero;
            int k = 0;
            foreach (var item in m.Items)
            {
                // Grace time is spaced by its own machine (SpacingRules.Grace), which
                // reserves the group's approach in front of the MAIN note. Letting a grace
                // column into this bucket would raise the main column's rod off a grace
                // notehead — LilyPond's paper column at that moment holds the main note's
                // grobs, and the grace's live in the grace part of the moment
                // (LILYPOND-REF: lily/spacing-basic.cc:163-180 Spacing_spanner::note_spacing).
                if (item.GraceTime)
                    continue;
                // t never decreases inside a measure, so one cursor serves the whole walk;
                // a zero-duration change item shares the note's t and does not move it.
                while (k < timings.Count && timings[k] < t)
                    k++;
                if (k < timings.Count && timings[k] == t)
                    columns[k] = columns[k].Append(item);
                t += item.Duration;
            }
        }
        return columns;
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
    /// <para>
    /// ⚠️ A SPACER IS NOT AN ENDPOINT EITHER. A skip engraves no grob, so LilyPond's
    /// Note_spacing_engraver — which files a wish from every rhythmic grob it acknowledges —
    /// files nothing for it: a voice that reads <c>s16 d''4</c> has no wish spanning the
    /// column its skip stands on and the column its note stands on. Returning the spacer here
    /// made that pair a "wish" (anyWish), so it took the wish pipeline — the skyline minimum,
    /// merge_springs' +0.3 headroom, the left-head refinement — where LilyPond gives the pair
    /// the wishless spring (min 0, the bare duration ideal) and a rod. MEASURED (2.26.0,
    /// scratch/p361/lp/bos.lys = test/beam-over-stem bar 2, the NoteSpacing left-/right-items
    /// dumped): voice 1's wish at the b8 names ONLY its own next column, and the d''4's names
    /// only the bar lines; the b8→d''4 gap is 1.6042 = the rod (1.3042 + 0.1 + 0.1 + 0.1), not
    /// the 1.8042 the headroom gave, and d''4→b8 is the bare 1.2, not the refined 1.3042.
    /// LILYPOND-REF: lily/note-spacing-engraver.cc:81-91 acknowledge_note_column /
    ///   acknowledge_rhythmic_grob — a wish's items are the grobs the voice engraved.
    /// </para>
    /// <para>
    /// ⚠️ EXCEPT THE BEAT SLASH'S SPACER, which IS a grob: the RepeatSlash / DoubleRepeatSlash
    /// item is rhythmic, the engraver acknowledges it, and the voice's wish spans from it to
    /// its next column exactly as from a note (RestItem.RepeatSlashCount). MEASURED (2.26.0,
    /// audit/lp-geometry/probes/beat-slash-spacing.ly): the slash column's quarter to the
    /// next note is 3.600000 — the wish's ideal with no head, 4.8 − 1.2 — where the wishless
    /// branch keeps 4.800000.
    /// </para>
    /// </remarks>
    private static MusicItem? ItemStartingAt(Measure m, Fraction t)
    {
        var acc = Fraction.Zero;
        foreach (var item in m.Items)
        {
            // A grace column shares the FOLLOWING note's timing and takes no measure time of
            // its own, so at the main note's moment it stands FIRST in the item list — and
            // returning it here measured the separation rod from a grace notehead instead of
            // the main one. Skipped for the same reason a mid-measure change is: neither is
            // the musical column being spaced.
            if (acc == t && !item.GraceTime && !SpacingRules.IsMidMeasureChangeColumn(item))
                return item is RestItem { IsSpacer: true, RepeatSlashCount: null } ? null : item;
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
    /// <param name="timings">The kept onsets — a skip's unused column is not among them.</param>
    /// <param name="columns">Items by column, index-aligned with the timings.</param>
    /// <param name="measure">The primary measure (its leading change items ride the bar).</param>
    /// <param name="leftBound">The bar line drawn at the bar's left bounding column.</param>
    /// <param name="droppedOnsetFollows">Whether an unused (skip-only) onset was dropped AFTER
    /// the first kept one. LilyPond's fills_measure looks at the column of the NEXT RANK,
    /// which for <c>c4 s2.</c> is the skip's unused column: <c>!is_used (next)</c> and no
    /// full-measure-extra-space — MEASURED, ps1's bar line → c4 is 1.23, not 2.23.
    /// LILYPOND-REF: lily/spacing-spanner.cc:446-472 Spacing_spanner::fills_measure.</param>
    /// <param name="spacing">The score's spacing options, for a skip-opened bar's
    /// duration-space spring.</param>
    private static Spring CreateBarlineToFirstSpring(
        Rendering.ScoreTextMetrics fonts,
        List<Fraction> timings, ItemColumn[] columns,
        Measure measure, BarlineType leftBound, bool droppedOnsetFollows,
        SpacingOptions spacing, IReadOnlyList<IReadOnlyList<MusicItem>>? staffFirstItems)
    {
        var firstItems = columns[0];
        // A bar that opens with a skip: the bar line's neighbour is a column at a later
        // moment — the duration-space branch, not Staff_spacing.
        if (timings[0] > Fraction.Zero)
            return SpacingRules.SkipOpenedBarFirstSpring(fonts,
                leftBound, measure.Items, firstItems, timings[0], spacing);
        // A grace run on ANY staff at this moment puts a grace column between the bar line and
        // the first musical column, and fills_measure then sees a musical `next`
        // (SpacingRules.HasLeadingGraceColumn).
        bool anyMusical = false, anyLeadingGrace = false;
        for (int q = 0; q < firstItems.Count; q++)
        {
            anyMusical |= SpacingRules.IsMusicalColumn(firstItems[q]);
            anyLeadingGrace |= SpacingRules.HasLeadingGraceColumn(firstItems[q]);
        }
        bool fillsMeasure =
            timings.Count == 1
            && !droppedOnsetFollows
            && anyMusical
            && !anyLeadingGrace
            && !(staffFirstItems?.Any(items => items.Any(SpacingRules.HasLeadingGraceColumn)) ?? false);
        return SpacingRules.BarlineToFirstColumnSpring(fonts, firstItems, fillsMeasure, staffFirstItems, leftBound);
    }

    /// <summary>
    /// The items starting at <paramref name="t"/>, one list per STAFF (every voice of the staff
    /// together), for the staves that carry a Staff_spacing wish — or null when there is no
    /// staff grouping or only one such staff, where the column's one list is that staff's.
    /// </summary>
    /// <remarks>
    /// A lyric / chord row makes no Staff_spacing grob and is skipped, as the line-start merge
    /// skips it (LineStartColumn.LineStartSpring). A staff with nothing starting at
    /// <paramref name="t"/> keeps an EMPTY list: it still wishes off its own bar line.
    /// MEASURED (2.26.0, scratch/p390/ks kse.ly): a lower staff holding only s1 pulls the upper
    /// staff's key-change bar to the same 26.11 as one holding r1.
    /// LILYPOND-REF: lily/spacing-spanner.cc:478-536 Spacing_spanner::breakable_column_spacing — the left column's spacing-wishes
    /// </remarks>
    private static List<IReadOnlyList<MusicItem>>? StaffItemsAt(
        IReadOnlyList<Measure> measures, IReadOnlyList<Staff>? staves, Fraction t)
    {
        if (staves == null || staves.Count != measures.Count)
            return null;
        var owners = new List<Staff>();
        var lists = new List<IReadOnlyList<MusicItem>>();
        for (int i = 0; i < measures.Count; i++)
        {
            var staff = staves[i];
            if (staff.IsTextRow)
                continue;
            int k = owners.Count - 1;
            while (k >= 0 && !ReferenceEquals(owners[k], staff))
                k--;
            if (k < 0)
            {
                owners.Add(staff);
                lists.Add(new List<MusicItem>());
                k = owners.Count - 1;
            }
            var items = (List<MusicItem>)lists[k];
            var onset = Fraction.Zero;
            foreach (var item in measures[i].Items)
            {
                if (onset > t)
                    break;
                // The same column membership BuildTimingToItemsMap uses: a grace item's column
                // is its own.
                if (onset == t && !item.GraceTime)
                    items.Add(item);
                onset += item.Duration;
            }
        }
        return lists.Count > 1 ? lists : null;
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
        Rendering.ScoreTextMetrics fonts,
        int i, List<Fraction> timings,
        ItemColumn[] columns,
        IReadOnlyList<Measure> measuresToScan, SpacingOptions spacing,
        List<(int Left, int Right, double Distance)> looseRods)
    {
        // This spring connects timings[i-1] → timings[i]; its duration is
        // THAT segment. (A previous off-by-one used the FOLLOWING segment's
        // duration, clamping a half-note gap down to the next quarter's length.)
        Fraction segmentDuration = timings[i] - timings[i - 1];
        // LILYPOND-REF: lily/spacing-engraver.cc:200-253 — shortest_playing aggregated at the LEFT column.
        var shortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[i - 1], measuresToScan);
        // LILYPOND-REF: lily/spacing-basic.cc:144 — measure length caps shortest_playing (mmrest guard).
        Fraction measureLength = Fraction.Zero;
        for (int vi = 0; vi < measuresToScan.Count; vi++)
        {
            var total = Fraction.Zero;
            foreach (var item in measuresToScan[vi].Items)
                total += item.Duration;
            if (total > measureLength)
                measureLength = total;
        }
        var spring = SpacingRules.CreateTimingSpringMultiVoice(
            segmentDuration, shortestPlaying, spacing,
            measureLength: measureLength > Fraction.Zero ? measureLength : null);

        var prevItems = columns[i - 1];
        var nextItems = columns[i];

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
        bool anyWish = false;
        // The left items of the PAIRS that actually carry a wish — one per voice
        // whose notes occupy BOTH columns. The left-head refinement below prices
        // THESE heads (each wish reads its own voice's first_head,
        // note-spacing.cc:46-70), not the widest head any voice parks on the left
        // column: a half held under a quarter has no wish into the quarter's next
        // column (its own wish spans to ITS next note), and LilyPond's gap is blind
        // to it — MEASURED, probe multi-voice-head-spacing.ly (MVH's three gaps
        // equal to the digit; charging the widest head instead was the whole of
        // multi-voice.natural.wide-head-gap's +0.073200 = the half-vs-quarter
        // head-width difference).
        // ⚠️ NOT A LIST. Session 455's census: 37.80 builds a keystroke, mean 1.74 items,
        // MAX 2 — so the whole 3,327 B/keystroke this container cost was two slots wide.
        // ItemColumn carries both in the struct; the third wish (three voices occupying
        // both columns — expressible, and absent from 231 books) spills to a real list.
        MusicItem? wish0 = null, wish1 = null;
        List<MusicItem>? wishMany = null;
        for (int vi = 0; vi < measuresToScan.Count; vi++)
        {
            var vm = measuresToScan[vi];
            var prev = ItemStartingAt(vm, timings[i - 1]);
            var next = ItemStartingAt(vm, timings[i]);
            if (prev == null || next == null)
                continue;
            anyWish = true;
            if (wishMany is not null) wishMany.Add(prev);
            else if (wish0 is null) wish0 = prev;
            else if (wish1 is null) wish1 = prev;
            else wishMany = [wish0, wish1, prev];
            // LILYPOND-REF: lily/note-spacing.cc:78-83 Note_spacing::get_spacing — the
            //   spring's own minimum, taken with the right column's skyline-vertical-padding
            //   and with NO spanner padding.
            maxSkyDist = Math.Max(maxSkyDist,
                SpacingRules.CalculateSkylineDistance(fonts, prev, next, staffY: 0));
            // LILYPOND-REF: lily/spacing-spanner.cc:229-296 Spacing_spanner::set_column_rods
            //   raises a rod over every pair of columns that can reach each other, via
            //   lily/separation-item.cc:47-68 Separation_item::set_distance.
            maxRod = Math.Max(maxRod,
                SpacingRules.SeparationRodDistance(fonts, prev, next, staffY: 0));
            // A whole-display tremolo pair with accidentals on its right half spans
            // the Beam's minimum-length as a rod (6.0) — the spacing side of the
            // gapped floating beam. Same house as the measure-estimate system's.
            // LILYPOND-REF: lily/beam.cc:429-449 tremolo_springs_and_rods.
            maxRod = Math.Max(maxRod, SpacingRules.TremoloPairRod(prev, next));
        }

        // ⚠️ THE WISH CHAIN IS PER VOICE, NOT PER STAFF. Until 2026-09-10 a branch here made
        // any pair whose two columns were occupied by two voices of ONE staff a "wish",
        // reasoning that Note_spacing_engraver keys its last-spacing map by the voice's parent
        // Staff. The map is a member of each Voice's own engraver instance (keyed by parent
        // only so a \change Staff can find its way back), so voice 1's wish never names a
        // column only voice 2 stands on. MEASURED (2.26.0, scratch/p361/lp/bos.lys =
        // test/beam-over-stem bar 2, NoteSpacing left-/right-items dumped): the b8's wish
        // names its own b8 at 9/8 and nothing else, and the pair into voice 2's d''4 at 17/16
        // is priced wishless — the bare 1.2 ideal, min 0, and the column rod. Such a pair is
        // floored by its ROD alone, in ApplyCrossVoiceColumnSpacing.
        // LILYPOND-REF: lily/note-spacing-engraver.cc:31-37 — last_spacings_ and
        //   last_spacing_ are per-engraver members; :109-128 stop_translation_timestep.

        // Refine the duration-based ideal to the LEFT column's actual head width
        // (LilyPond's note-spacing.cc:77), BEFORE the stem correction — but ONLY when the
        // pair has a wish at all: the refinement is a line of Note_spacing::get_spacing,
        // which runs once per wish, so a pair no single voice occupies at both ends (the
        // springs.empty () hemiola branch below) keeps its raw duration ideal. Running it
        // anyway held the two cross-staff gaps of spacing-loose-polyphony.ly at 1.20/1.70
        // where LilyPond's bare ideals are 0.80/1.60. The cue check stays on top of this:
        // see SpacingRules.CrossesVoiceBoundary (spacing-spanner.cc:352-358).
        if (wish0 != null)
            spring = SpacingRules.ApplyLeftHeadWidth(
                spring,
                // One left item per WISH — per voice occupying both columns with a
                // rhythmic grob at each (ItemStartingAt); anyWish and wish0 are the
                // same fact, so a pair no voice spans keeps its raw duration ideal.
                wishMany is not null ? new ItemColumn(wishMany) : new ItemColumn(wish0, wish1),
                spacing.Increment, nextItems,
                // Several wishes merge as LilyPond merges them — by AVERAGING the
                // ideals (merge_springs) — not by taking the widest head.
                mergeWishAverage: true);

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
            fonts, nextItems, prevItems, spring.IdealDistance);

        // A LOOSE change column — another staff's column stands between it and its own
        // staff's previous note — is PRUNED from the spring chain: this pair is priced
        // as if the change were not there, the renderer drapes the glyphs back from the
        // next column (SpacingRules.LooseChangeColumnHangDistance, attached in
        // MultiStaffLayouter), and the room the pruned column still needs under
        // compression becomes a rod spanning its own-staff neighbors.
        // LILYPOND-REF: lily/spacing-determine-loose-columns.cc:192-278 prune_loose_columns
        //   — loose columns leave the cols vector and get between-cols instead.
        if (changeGaps is { } pruned && nextItems.Count > 0)
        {
            var ownLeft = SpacingRules.LooseChangeLeftNeighborTiming(measuresToScan, nextItems);
            if (SpacingRules.IsLooseChangeColumn(fonts, timings, ownLeft, timings[i], nextItems))
            {
                // The rod's two arms are the same wish minimums the change gaps carry:
                // Note_spacing's skyline minimum on the left, Staff_spacing's
                // Paper_column::minimum_distance on the right — summed over next_door,
                // the loose column's OWN-STAFF neighbours. The left arm is therefore
                // re-priced from the own-staff previous ITEM: `pruned` read the UNION
                // previous column, which mid-clique is another staff's intervening
                // note (sploose's A4 half) — the wrong column, masked while item M
                // priced every scaled head as black (see LooseChangeOwnPrevItem).
                // LILYPOND-REF: lily/spacing-determine-loose-columns.cc:135-185
                //   set_distances_for_loose_col — r.item_drul_ = next_door.
                int leftIndex = timings.IndexOf(ownLeft!.Value);
                if (leftIndex >= 0)
                {
                    var ownPrev = SpacingRules.LooseChangeOwnPrevItem(measuresToScan, nextItems);
                    var ownArms = SpacingRules.MidMeasureChangeGaps(
                        fonts, nextItems, ownPrev != null ? new ItemColumn(ownPrev) : default,
                        spring.IdealDistance);
                    looseRods.Add((leftIndex + 1, i + 1, (ownArms ?? pruned).MinDistance));
                }
                changeGaps = null;
            }
        }

        // The wish REPLACES the base spring's increment minimum with the skyline
        // distance — set_min_distance, not ensure — so a pair whose columns never meet
        // in Y carries min 0 and merge_springs' +0.3 headroom is measured from THERE,
        // not from the increment. Maxing with the increment instead held every such
        // floor at 1.2 + 0.3 = 1.5: the down→up KNEE pair of
        // spacing-correction-accidentals.ly has ideal 1.330 (base − 1.2 + 1.3042 −
        // 1.1742 knee) and LilyPond draws exactly that; the old ensure shipped 1.500.
        // A pair with NO wish takes LilyPond's springs.empty () branch ("polyphonic
        // spacing of hemiolas"): minimum 0.0 outright, the raw duration ideal, no
        // left-head refinement and no merge headroom — the whole wish pipeline is per
        // wish. The zero is gated on the RIGHT column being musical (:382); a wishless
        // pair into a change column keeps its base minimum, which the changeGaps
        // override below replaces anyway, so the gate has no separate reader here.
        // MEASURED: spacing-loose-polyphony.ly is the LP-oracle book this branch
        // waited for (the previous NAMED keep of the increment minimum said "zero it
        // when a book with an LP oracle measures this branch") — its two cross-staff
        // pairs price bare at 0.80/1.60 and the loose-column rod's blocking force
        // stretches them to LilyPond's exact 1.25/2.50.
        // ⚠️ Lily#'s no-wish set is still wider than LilyPond's where no staff frame
        // exists at all (a staffless chords/lyrics row). A SAME-staff cross-voice pair
        // is no-wish on BOTH sides — LilyPond's wish chain is per voice (see the note
        // above the left-head refinement) — and its floor is the column rod alone,
        // raised in ApplyCrossVoiceColumnSpacing.
        // Both strengths stay where the duration spring put them (the compressibility
        // stays fraction * (duration_space - increment) and does not become
        // ideal - skyline). Measured against LilyPond's own compressed line: 1.698045 for
        // a quarter-to-quarter spring (audit/lp-geometry/probes/compressed-line-force.ly).
        // LILYPOND-REF: lily/note-spacing.cc:78-83 Note_spacing::get_spacing —
        //   min_dist = max (0.0, distance); base.set_min_distance (min_dist);
        // LILYPOND-REF: lily/spacing-spanner.cc:380-393 musical_column_spacing —
        //   springs.empty () ? spring.set_min_distance (0.0) : merge_springs.
        spring = spring.WithMinDistance(anyWish ? Math.Max(0.0, maxSkyDist) : 0.0);

        // Stem-direction optical correction ([Wanske]), merged across simultaneous
        // voices' wishes (single voice = its own wish; polyphony = averaged). Runs
        // AFTER the min replacement above because every wish inside carries that same
        // skyline minimum — get_spacing sets it on each wish BEFORE merge_springs, so
        // the merge's +0.3 floor stands on the skyline, not on the increment.
        if (changeGaps is null)
        {
            spring = SpacingRules.MergeVoiceStemWishes(
                spring, measuresToScan, timings[i - 1], timings[i],
                NoteSpacingParameters.Default, spacing.Increment);
            // LILYPOND-REF: lily/note-spacing.cc:113 Note_spacing::get_spacing — set_ideal_distance (std::max (0.0, ideal)),
            // for every wish. MergeVoiceStemWishes clamps the NOTE wishes it sees; a wish
            // whose left column is a rest never reaches it, and ApplyLeftHeadWidth (:77)
            // no longer clamps, so the pair's wish is clamped here too.
            if (wish0 != null)
                spring = spring.WithIdealDistance(Math.Max(0.0, spring.IdealDistance));
        }

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
        // min + 0.3. A no-op for an ordinary note-to-note ideal (~3.0 vs a ~1.8 floor)
        // — and NOT taken at all on a wishless pair, whose hemiola branch above never
        // calls merge_springs (a change column always has its Staff_spacing wish, so a
        // change pair keeps the headroom even when this scan saw no note wish).
        // LILYPOND-REF: lily/spacing-spanner.cc:380-393 note_spacing — `merge_springs`
        //   is taken whenever the wish list is non-empty, i.e. also for a single wish.
        // LILYPOND-REF: lily/spring.cc:122 — avg_distance = max (min_distance + 0.3, …).
        if (anyWish || changeGaps != null)
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
    /// <param name="rightNeighbours">What opens the next measure, when known — the bar
    /// line's other neighbours (SpacingRules.NoteColumnToBarlineFloorPair).</param>
    private static Spring CreateLastToBarlineSpring(
        Rendering.ScoreTextMetrics fonts,
        List<Fraction> timings, ItemColumn[] columns,
        IReadOnlyList<Measure> measuresToScan, Fraction totalDuration, SpacingOptions spacing,
        double boundaryClefAllowance = 0, IReadOnlyList<MusicItem>? rightNeighbours = null)
    {
        var endDuration = totalDuration - timings[^1];
        var endShortestPlaying = SpacingRules.ComputeShortestPlayingAt(timings[^1], measuresToScan);
        var endSpring = SpacingRules.CreateTimingSpringMultiVoice(
            endDuration, endShortestPlaying, spacing);

        // The column ROD toward the bar line, applied last (below) — a floor on the
        // compressed length alone, as on every inter-column spring.
        double maxRod = 0;
        var lastItems = columns[^1];
        if (lastItems.Count > 0)
        {
            endSpring = SpacingRules.ApplyLeftHeadWidth(endSpring, lastItems, spacing.Increment);

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
            // LILYPOND-REF: lily/note-spacing.cc:113 Note_spacing::get_spacing — set_ideal_distance (std::max (0.0, ideal)).
            // The merge clamps the NOTE wishes; a rest's wish never reaches it, and
            // ApplyLeftHeadWidth (:77) does not clamp, so the spring is clamped here too.
            endSpring = endSpring.WithIdealDistance(Math.Max(0.0, endSpring.IdealDistance));

            // The column's whole skyline — flag included — against the bar line's box:
            // the spring minimum now, the rod after the headroom.
            // LILYPOND-REF: lily/note-spacing.cc:78-83 get_spacing (the minimum);
            // LILYPOND-REF: lily/spacing-spanner.cc:228-297 set_column_rods (the rod).
            double maxSkyDist = 0;
            for (int q = 0; q < lastItems.Count; q++)
            {
                var item = lastItems[q];
                var (skyDist, rod) = SpacingRules.NoteColumnToBarlineFloorPair(
                    fonts, item, new ItemColumn(rightNeighbours));
                maxSkyDist = Math.Max(maxSkyDist, skyDist);
                maxRod = Math.Max(maxRod, rod);
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
        endSpring = SpacingRules.ApplyMergeSpringsHeadroom(endSpring);

        // …and only now the rod — the same order as CreateInterColumnSpring, and for the
        // same reason: it stands 0.1 above the skyline distance the headroom put 0.3
        // above, so it binds only under compression. A clef before the bar line widens it
        // as it widened the minimum. Until 2026-09-03 this pair had no rod at all, so a
        // compressed line closed on its last note 0.1 tighter than LilyPond's, on top of
        // the flag it did not see (NoteColumnToBarlineFloorPair).
        // LILYPOND-REF: lily/spacing-spanner.cc:228-297 set_column_rods — every adjacent
        //   pair, the breakable columns included.
        return endSpring.EnsureMinDistance(maxRod + boundaryClefAllowance);
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
    public ImmutableArray<ColumnLayout> LayoutColumns(Rendering.ScoreTextMetrics fonts,
                                                      Measure measure, double totalWidth, List<Fraction> timings,
                                                      SpacingOptions? spacing = null,
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
        var springs = precomputedSprings ?? CreateTimingSprings(fonts, measure, timings, spacing, allMeasures);
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
        var columns = RentColumnBuilder();

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

        // ToImmutable COPIES (see the drawer's remark), so the builder is finished with here.
        var laid = columns.ToImmutable();
        GiveColumnBuilder(columns);
        return laid;
    }

    /// <summary>
    /// The builder <see cref="LayoutColumns"/> gathers a measure's solved columns into, lent
    /// from one builder the thread keeps between measures.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 3.99 measures a keystroke at 7.35 columns each (max 16), and all 7,368
    /// builders built were unreachable by the time the render that built them returned. The
    /// builders and their growth ladders were 1,687 B a keystroke — the smallest of the six
    /// this session parks, and the most-CALLED of them.
    /// <para>
    /// ⚠️ THE SENTINEL IS WHY THE SIZE IS NOT <c>timings.Count</c>: the closing column is
    /// added only when the measure's total duration stands past the last onset, so the count
    /// is <c>timings.Count</c> or one more. An exact-sized builder would have to decide that
    /// before the loop, and the loop's own reading (<c>columns[^1].Timing</c>) is what decides
    /// it — this is the shape session 451 named, read the other way round.
    /// </para>
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a builder parked dirty would open the next measure's columns with the
    /// previous measure's, and <c>GetXForTiming</c> would read them. There is no early return
    /// and no throw between the rent and the give (the empty-timings guard is at the top of
    /// the method, before the rent). <c>ToImmutable</c> copies, measured (session 459) — see
    /// <see cref="LedgerLineSpannerEngraver"/>'s drawer for the probe.
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one builder a thread at that thread's busiest measure — 16 columns,
    /// emptied, so it pins no timing.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static ImmutableArray<ColumnLayout>.Builder? t_columnBuilder;

    /// <summary>Takes the thread's column builder, or makes the thread's first.</summary>
    private static ImmutableArray<ColumnLayout>.Builder RentColumnBuilder()
    {
        var builder = t_columnBuilder;
        if (builder is null)
            return ImmutableArray.CreateBuilder<ColumnLayout>();
        t_columnBuilder = null;
        return builder;
    }

    /// <summary>Puts a finished measure's builder back, emptied, with its capacity.</summary>
    private static void GiveColumnBuilder(ImmutableArray<ColumnLayout>.Builder builder)
    {
        builder.Clear();
        t_columnBuilder = builder;
    }
}
