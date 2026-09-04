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
/// The EMPTY BAR — a bar in which no column holds a grob: a bar of nothing but skips
/// (<c>s1</c>), the <c>| |</c> placeholder (which holds the same skip since 2026-08-28),
/// and a bar a percent repeat covers (its music is a spacer run since session 332).
/// </summary>
/// <remarks>
/// <para>
/// LilyPond never spaces such a bar's own column. A skip engraves no grob, so the musical
/// column at its moment has no <c>elements</c>; a column with no elements, no spanner
/// bound and no <c>used</c> flag is NOT USED, and unused columns leave the spacing problem
/// before a single spring is made. The two bar-line columns then stand NEXT TO EACH OTHER
/// and are spaced as a breakable pair by the one-size-fits-all formula, whose only inputs
/// are the columns' skyline distance and the measure's length over the piece's common
/// shortest duration.
/// LILYPOND-REF: lily/paper-column.cc:115-136 Paper_column::is_used — elements,
///   bounded-by-me, is_breakable, "used", labels; a skip's column has none.
/// LILYPOND-REF: lily/system.cc:751-777 System::used_columns_in_range — the columns the
///   spacer sees are filtered through is_used.
/// LILYPOND-REF: lily/spacing-spanner.cc:42-58 Spacing_spanner::get_columns — the spacing
///   spanner takes exactly that filtered list.
/// LILYPOND-REF: lily/spacing-spanner.cc:478-517 Spacing_spanner::breakable_column_spacing
///   — a pair with <c>dt != 0</c> collects no Staff_spacing wish, so <c>springs.empty ()</c>
///   and the pair falls to standard_breakable_column_spacing.
/// </para>
/// <para>
/// MEASURED (2026-09-05, LilyPond 2.26.0, scratch/p332/t7, bar line to bar line, ragged):
/// <c>c4 d e f | s1 | s1 | s1 | d4 e f g |</c> gives 5.51 for each skip bar, and
/// <c>c4 d e f | repeat percent 4 { g,4 a, b, c } | d4 e f g |</c> gives the same 5.51 for
/// each covered bar — the pair that says a covered bar IS a skip bar. The formula below
/// lands on the digit for four textures: quarters 5.51, eighths 8.07 (gs 1/8), a whole
/// note 5.51, sixteenths 15.75 (gs 1/16). Lily# priced every one of them 6.39 — a spacer
/// column of its own with the bar-line spring in front and a whole note's LOGARITHMIC
/// duration space behind, blind to the piece's shortest note; LilyPond's is LINEAR in
/// <c>mlen / gs</c>.
/// </para>
/// <para>
/// ⚠️ THE COLUMN IS NOT DELETED HERE, IT IS SHRUNK TO NOTHING. Lily# keys the renderer,
/// the lyric and chord grids and the incremental memo on one column per union onset
/// (<c>springs.Length == timings.Count + 1</c>, MultiStaffLayouter.CollectAllTimingsForMeasure),
/// so an empty bar keeps its onsets and carries LilyPond's ONE spring on the last leg with
/// rigid zero-length springs before it — springs in series add, so the chain is that one
/// spring. The pruning of a SINGLE unused column inside a bar that still has music
/// (<c>c4 s2.</c>) is the same mechanism and is NOT done yet: there LilyPond's note spring
/// runs from the note straight to the bar line over the deleted column's moment, and Lily#
/// still prices the skip's leg with the delta_t fallback
/// (<see cref="CreateTimingSpringMultiVoice"/>). That is the next island, not this one.
/// </para>
/// </remarks>
internal static partial class SpacingRules
{
    /// <summary>
    /// <c>Spacing_spanner::standard_breakable_column_spacing</c> for a pair of NON-MUSICAL
    /// columns with nothing kept between them — two bar lines — in LilyPond's frame: column
    /// origin to column origin. Two branches, as LilyPond has: when BOTH bar lines can take a
    /// line break the bar is priced LINEARLY in the meter's length over the common shortest
    /// (an empty bar of a piece in eighths is wider than one of a piece in quarters, by the
    /// ratio of the two); when either cannot, it is priced by the ordinary LOGARITHMIC
    /// duration space of the time between the two columns.
    /// </summary>
    /// <param name="minimumDistance"><c>Paper_column::minimum_distance (l, r)</c> — the
    /// skyline distance between the two bounding columns, <see cref="MmrRodMinimumDistance"/>.</param>
    /// <param name="bothBreakable"><c>Paper_column::is_breakable (l) &amp;&amp; is_breakable (r)</c>
    /// — a column is breakable when its <c>line-break-permission</c> is a symbol, which the
    /// column engraver clears where <c>forbidBreak</c> was raised in that timestep.
    /// LILYPOND-REF: lily/paper-column.cc:138-142 Paper_column::is_breakable;
    /// LILYPOND-REF: lily/paper-column-engraver.cc:264-271 Paper_column_engraver::stop_translation_timestep
    /// — <c>!break_allowed (context ())</c> unsets the three permissions.</param>
    /// <param name="measureLength">The <c>measure-length</c> the left column carries — the
    /// meter's bar length, which the column engraver stamps on the first command column of
    /// every bar.
    /// LILYPOND-REF: lily/paper-column-engraver.cc:185-194 Paper_column_engraver::stop_translation_timestep
    /// — <c>set_property (column, "measure-length", to_scm (mlen))</c>.</param>
    /// <param name="dt">The time between the two columns, <c>when_mom (r) − when_mom (l)</c>
    /// — the bar's own duration, which is the meter's length unless the bar is a pickup.</param>
    /// <param name="globalShortest">The piece's common shortest duration in whole notes —
    /// <c>Spacing_options::global_shortest_</c>, <see cref="CalculateCommonShortestDuration(MultiStaffScore)"/>.</param>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/spacing-basic.cc:40-83 Spacing_spanner::standard_breakable_column_spacing
    /// — transcribed line for line below, both branches. The <c>0.8</c> is LilyPond's own
    /// literal on line 55; the <c>1.2</c> is <c>spacing-increment</c>. Each branch builds its
    /// spring with the two-argument constructor, whose default strengths are
    /// <c>stretch = ideal</c> and <c>compress = ideal − min</c>
    /// (LILYPOND-REF: lily/spring.cc:49-60 Spring::Spring (dist, min_dist) → set_default_strength;
    /// LILYPOND-REF: lily/spring.cc:204-216 Spring::set_default_compress_strength / set_default_stretch_strength);
    /// the breakable branch then sets the stretch strength to <c>space</c> explicitly, so that
    /// an empty bar opening a line — whose min_dist is large because of the clef — does not
    /// stretch more than an empty bar later in the line (LilyPond's comment on lines 58-63).
    /// The other branch keeps the default.
    /// </para>
    /// <para>
    /// MEASURED, 2.26.0 (scratch/p333/fx, ALLCOL dumps with <c>line-break-permission</c>):
    /// the blank bars of a THREE-bar slash body sit between bar-line columns whose permission
    /// is unset — LilyPond forbids a line break while a rhythmic grob is still sounding, and
    /// the RepeatSlash grob sounds for the whole body — and measure 6.39 at gs 1/8:
    /// 0.39 + (2 + log2 8) × 1.2, the second branch to the digit. The bar between the two
    /// bars of a DOUBLE percent is unset the same way (its engraver says so in as many words)
    /// and both bars of the pair measure 0.39 + 5.30 column to column at gs 3/16. A SINGLE
    /// percent's bars, and a bar of <c>s1</c>, keep <c>allow</c> on both sides and take the
    /// first branch (5.51 at gs 3/16, 8.07 at gs 1/8).
    /// LILYPOND-REF: lily/forbid-break-engraver.cc:41-62 Forbid_line_break_engraver::pre_process_music
    ///   — busyGrobs still sounding with rhythmic-grob-interface raise forbidBreak (a Staff
    ///   engraver, ly/engraver-init.ly:378).
    /// LILYPOND-REF: lily/double-percent-repeat-engraver.cc:63-71 Double_percent_repeat_engraver::pre_process_music
    ///   — "Prevent breaks over percent sign."
    /// </para>
    /// </remarks>
    internal static Spring StandardBreakableColumnSpacing(
        double minimumDistance, bool bothBreakable, Fraction measureLength, Fraction dt,
        double globalShortest)
    {
        // LILYPOND-REF: lily/spacing-basic.cc:44 standard_breakable_column_spacing —
        //   min_dist = std::max (0.0, Paper_column::minimum_distance (l, r)).
        double minDist = Math.Max(0.0, minimumDistance);

        if (bothBreakable)
        {
            // LILYPOND-REF: lily/spacing-basic.cc:46-55 standard_breakable_column_spacing —
            //   is_breakable (l) && is_breakable (r): mlen = measure-length of l (default 1);
            //   incr = spacing-increment; space = incr * (mlen.main_part_ / global_shortest_) * 0.8.
            double space = EngravingDefaults.SpacingIncrement
                           * (measureLength.ToDouble() / globalShortest) * 0.8;

            // LILYPOND-REF: lily/spacing-basic.cc:56 — Spring spring = Spring (min_dist + space, min_dist);
            // LILYPOND-REF: lily/spacing-basic.cc:64 — spring.set_inverse_stretch_strength (space).
            // The three-argument constructor leaves the compress strength at ideal − min =
            // space, which is what Spring (dist, min_dist)'s set_default_strength put there.
            return new Spring(minDist + space, minDist, space);
        }

        // LILYPOND-REF: lily/spacing-basic.cc:68-82 standard_breakable_column_spacing —
        //   dt = when_mom (r) - when_mom (l); dt == 0 → ideal = min_dist + 0.5 (the
        //   Staff_spacing case, ApplyRowCommandColumnSprings' term); else
        //   ideal = min_dist + options->get_duration_space (dt.main_part_);
        //   return Spring (ideal, min_dist).
        double ideal = dt == Fraction.Zero
            ? minDist + 0.5
            : minDist + CalculateDurationSpace(dt, globalShortest);
        // LILYPOND-REF: lily/spring.cc:212-216 Spring::set_default_stretch_strength —
        //   inverse_stretch_strength_ = ideal_distance_; the compress strength is the
        //   three-argument constructor's ideal − min, as set_default_compress_strength has it.
        return new Spring(ideal, minDist, ideal);
    }

    /// <summary>
    /// The spring chain of an EMPTY BAR in Lily#'s content frame — <paramref name="columnCount"/>
    /// rigid zero-length legs (the unused onsets LilyPond deletes) and then LilyPond's one
    /// breakable-pair spring, re-framed from column origin → column origin to bar-line ink →
    /// bar-line ink.
    /// </summary>
    /// <param name="columnCount">The bar's union onsets — every one of them unused.</param>
    /// <param name="leftBound">The bar line the bar opens after (its own start bar line, or
    /// the previous bar's end when it declares none — <see cref="RunLeftBoundBarline"/>).</param>
    /// <param name="leadingItems">The bar's items, scanned for the break-aligned key / time
    /// change that rides the left bounding column and widens its skyline reach.</param>
    /// <param name="bothBreakable">Whether a line may break at BOTH bounding bar lines — the
    /// left bar line's permission is the previous measure's <see cref="Measure.LineBreakPermission"/>,
    /// the right one's this measure's; see <see cref="StandardBreakableColumnSpacing"/>.</param>
    /// <param name="measureLength">The meter's bar length (see
    /// <see cref="StandardBreakableColumnSpacing"/>).</param>
    /// <param name="dt">The bar's own duration, the time between its two bar lines.</param>
    /// <param name="globalShortest">The piece's common shortest duration in whole notes.</param>
    /// <remarks>
    /// <para>
    /// FRAME: LilyPond's <c>minimum_distance</c> runs from the LEFT column's origin — the left
    /// bar line's left edge — to the right bar line's left edge, and
    /// <see cref="MmrRodMinimumDistance"/> answers in exactly that frame (bar line to bar line,
    /// 0.390 for two single lines: 0.19 of ink + 0.1 + 0.1 of extra-spacing-width). Lily#'s
    /// chain starts at the left bar line's ink RIGHT edge, because the layout adds each bar
    /// line's drawn width beside the content it prices (MultiStaffLayouter and
    /// SystemBreaker both add <c>GetBarlineWidth (start) + GetBarlineWidth (end)</c>), so the
    /// ideal and the minimum are both shortened by the left bar line's drawn width and the two
    /// strengths are untouched: 0.390 + 5.120 − 0.190 = 5.320 of content, 5.510 bar line to bar
    /// line, which is the LilyPond figure. <see cref="MmrRodDistance"/> makes the same
    /// correction for the multi-measure-rest rod, spelled as the run bar's own start + end
    /// widths; for a bar with no start bar line of its own the two spellings are the same
    /// number, and when the run/empty bar opens with a repeat bar the one here follows the
    /// column the distance was measured from. ⚠️ Two spellings of one frame correction
    /// (RULES §5.2.1②); they should become one when the MMR rod is next measured against a
    /// repeat-bar-opened run.
    /// </para>
    /// <para>
    /// ⚠️ A LINE-START EMPTY BAR IS NOT MEASURED. LilyPond's left column there is the
    /// line's prefix column (clef, key, time), whose reach is the whole of <c>min_dist</c>;
    /// Lily# replaces spring 0 — here a rigid zero — with its own prefix→first-column spring
    /// (MultiStaffLayouter.LineStartSpringForLine) and keeps this spring after it. Nothing
    /// observes the pair yet; the ragged probes of scratch/p332/t7 all open with a bar of
    /// music.
    /// </para>
    /// </remarks>
    internal static ImmutableArray<Spring> EmptyBarSprings(
        int columnCount, BarlineType leftBound, IEnumerable<MusicItem>? leadingItems,
        bool bothBreakable, Fraction measureLength, Fraction dt, double globalShortest,
        double leftDoublePercentHalfWidth = 0, double rightDoublePercentHalfWidth = 0)
    {
        // LILYPOND-REF: lily/paper-column.cc:144-164 Paper_column::minimum_distance — the
        //   skyline distance between the two bounding columns, the same quantity the
        //   multi-measure-rest rod reads through MmrRodMinimumDistance. A DOUBLE percent
        //   sign straddling either bounding bar line is in it: LilyPond's DoublePercentRepeat
        //   is break-aligned on the bar-line column between the pair (staff-bar), so its ink
        //   enters that column's skyline on both sides and both bars of the pair read wider
        //   by the sign's reach (MEASURED, pc4r: the pair is 5.69 / 9.26 column to column,
        //   of which 0.39 / 3.96 is min_dist — 3.96 = the sign's 3.7576 + 0.2 of
        //   extra-spacing-width). In Lily#'s bar-line frame the left half of the sign lands
        //   in the FIRST bar and the right half in the SECOND: 7.57 / 7.38 bar line to bar
        //   line, LilyPond's own PROBEBAR figures.
        double minimumDistance = MmrRodMinimumDistance(
            leftBound, leadingItems, leftDoublePercentHalfWidth, rightDoublePercentHalfWidth);
        var pair = StandardBreakableColumnSpacing(
            minimumDistance, bothBreakable, measureLength, dt, globalShortest);

        // Re-frame: the left bar line's drawn width is the layout's, not this chain's.
        double leftBarlineWidth = GetBarlineWidth(leftBound);

        var springs = ImmutableArray.CreateBuilder<Spring>(columnCount + 1);
        // The deleted columns: rigid, zero-length. Springs in series add ideal, minimum and
        // both inverse strengths, so a zero leg changes the chain by nothing.
        // LILYSHARP-OWN: LilyPond has no such spring because it has no such column — the
        //   unused column leaves the list (lily/system.cc used_columns_in_range) and the two
        //   bar lines become adjacent.
        //   departs from: nothing in the spring; the column's EXISTENCE. Lily# keys the
        //     renderer, the grids and the incremental memo on one column per union onset
        //     (springs.Length == timings.Count + 1), so the onset is kept at zero length.
        //   goes away when: unused onsets are pruned from CollectAllTimingsForMeasure the
        //     way loose notation spacers already are — the same step that would let a
        //     PARTLY empty bar (`c4 s2.`) price its note → bar line leg as LilyPond does.
        //   observed by: EmptyBarSpacingTests (every leg before the last asserted zero in
        //     all four quantities; the chain's sums against 2.26.0).
        for (int i = 0; i < columnCount; i++)
            springs.Add(new Spring(0, 0, 0, 0));
        springs.Add(new Spring(
            pair.IdealDistance - leftBarlineWidth,
            pair.MinDistance - leftBarlineWidth,
            pair.InverseStretchStrength,
            pair.InverseCompressStrength));
        return springs.MoveToImmutable();
    }

    /// <summary>
    /// Spring 0 of a bar that OPENS WITH A SKIP (<c>s4 c4 d e</c>): the skip's column is
    /// unused and gone, so the bar line's neighbour is the first note column, at a moment
    /// <paramref name="dt"/> after the bar — a pair with <c>dt != 0</c>, which collects no
    /// Staff_spacing wish and takes standard_breakable_column_spacing's duration-space
    /// branch: <c>min_dist + duration_space (dt)</c>, default strengths. In Lily#'s frame
    /// (bar-line ink right edge → the note column), the left bar line's drawn width is the
    /// layout's, as for <see cref="EmptyBarSprings"/>.
    /// </summary>
    /// <param name="leftBound">The bar line drawn at the bar's left bounding column.</param>
    /// <param name="measureItems">The bar's items, scanned for the break-aligned change riding
    /// the bounding column (the scan stops at the skip, the first sounding item).</param>
    /// <param name="firstItems">Everything at the first KEPT onset, across voices.</param>
    /// <param name="dt">The first kept onset — the time from the bar line to the column.</param>
    /// <param name="globalShortest">The piece's common shortest duration in whole notes.</param>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:478-515 Spacing_spanner::breakable_column_spacing
    ///   — <c>if (dt == Moment (0, 0))</c> is the only branch that reads spacing-wishes, so
    ///   a bar line whose neighbour column is later than its own moment falls to
    ///   standard_breakable_column_spacing; and full-measure-extra-space is handed only to
    ///   those wishes, so it never reaches this spring.
    /// LILYPOND-REF: lily/paper-column.cc:144-164 Paper_column::minimum_distance — the
    ///   left column's right skyline (bar line and any key / time change) against the note
    ///   column's left skyline (its leftmost ink plus extra-spacing-width).
    /// MEASURED, 2.26.0 (scratch/p333/ps/ps3, ALLCOL): <c>s4 c4 d e</c> opens 3.288 =
    /// 0.39 + 2.898 (a quarter at gs 3/16), <c>s2 c4 d</c> 4.488 = 0.39 + 4.098 (a half) —
    /// three digits each.
    /// </remarks>
    internal static Spring SkipOpenedBarFirstSpring(
        BarlineType leftBound, ImmutableArray<MusicItem> measureItems,
        IReadOnlyList<MusicItem>? firstItems, Fraction dt, double globalShortest)
    {
        var leftColumnRight = BoundaryColumn.Build(leftBound, measureItems).RightSkylineFromBarLine();
        // The note column's left reach: its leftmost ink plus its extra-spacing-width, the
        // right-hand term of minimum_distance (MusicalColumnLeftReach), over every voice.
        double reach = 0;
        if (firstItems != null)
            foreach (var item in firstItems)
                if (!IsChangeItem(item) && item is not RestItem { IsSpacer: true })
                    reach = Math.Max(reach, MusicalColumnLeftReach(item));
        var noteColumnLeft = HorizontalSkyline.FromBox(
            BoundaryColumn.StaffYBottom, BoundaryColumn.StaffYTop,
            xLeft: -reach, xRight: 0.1, HorizontalDirection.Left);
        // RightSkylineFromBarLine's origin is the bar line's LEFT edge — the column origin
        // when no clef precedes the bar — so this distance is LilyPond's min_dist as it
        // stands, column origin to column origin, the bar line's drawn width inside it
        // (0.39 for a plain bar line and a plain note: 0.19 + 0.1 + 0.1).
        double minimumDistance = Math.Max(0.0, leftColumnRight.Distance(noteColumnLeft));

        // Lily#'s spring 0 starts at the bar line's ink right edge: the drawn width is the
        // layout's, exactly as EmptyBarSprings re-frames its pair.
        double leftBarlineWidth = GetBarlineWidth(leftBound);
        var pair = StandardBreakableColumnSpacing(
            minimumDistance, bothBreakable: false, measureLength: dt, dt: dt, globalShortest);
        return new Spring(
            pair.IdealDistance - leftBarlineWidth,
            pair.MinDistance - leftBarlineWidth,
            pair.InverseStretchStrength,
            pair.InverseCompressStrength);
    }

    /// <summary>
    /// Whether a voice's bar holds no grob of its own: nothing but skips, behind the
    /// break-aligned key / time / clef change that rides the bar's opening column rather
    /// than a column of the bar.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc:115-136 Paper_column::is_used — a column is used
    ///   by its <c>elements</c>; a skip engraves none.
    /// LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders — clef, staff-bar,
    ///   key-signature, time-signature are placed on the BREAKABLE column, which is used
    ///   by being breakable and is one of the pair being spaced, not a column between them.
    /// A change written after a skip (<c>s2 clef bass s2</c>) is a non-musical column of its
    /// own in LilyPond, used by its clef; it is read as content here, so such a bar keeps its
    /// ordinary chain — unmeasured, and rarer than the leading change.
    /// </remarks>
    internal static bool BarHoldsOnlySkips(ImmutableArray<MusicItem> items)
    {
        int i = 0;
        while (i < items.Length && MultiMeasureRestEngraver.IsBreakAlignedChange(items[i]))
            i++;
        for (; i < items.Length; i++)
            if (items[i] is not RestItem { IsSpacer: true })
                return false;
        return true;
    }
}
