// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/hairpin.cc
//     Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
//   scm/define-grobs.scm
//     Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>;
//     Jan Nieuwenhuizen <janneke@gnu.org>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a hairpin wedge.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358
/// </remarks>
public readonly record struct HairpinLayout(
    // Start measure index (for system Y lookup).
    int StartMeasureIndex,
    // Start X position (staff spaces from score start).
    double StartX,
    // End X position.
    double EndX,
    // Y of the wedge centre line in the Y-up frame: staff-spaces ABOVE the system
    // top, up-positive (frame B). The renderer reflects it to device against the
    // segment's system top (sy + old-Y == sy − YUp).
    double YUp,
    // Opening at the start (left) end (half-height, in staff spaces).
    // LILYPOND-REF: lily/hairpin.cc:300-313 — continued/continuing height fractions
    // For crescendo: 0 (point). For decrescendo: full or fractional opening.
    double StartOpening,
    // Opening at the end (right) end (half-height, in staff spaces).
    // For crescendo: full or fractional opening. For decrescendo: 0 (point).
    double EndOpening,
    // Crescendo or decrescendo.
    HairpinDirection Direction,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating cresc/decresc mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // Which staff this wedge hangs under (per-staff stacking).
    int StaffIndex = 0,
    // X of the CENTRE of the circle drawn at a niente tip (al / dal niente), or NaN for none.
    // StartX / EndX are then the ARMS' ends, already short of the circle.
    // LILYPOND-REF: lily/hairpin.cc:323-355 Hairpin::print — circled_tip
    double CircleX = double.NaN
);

/// <summary>
/// Calculates positions for hairpin (crescendo/decrescendo) wedges.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hairpin.cc:110-358 print()
/// LILYPOND-REF: scm/define-grobs.scm:1777-1803 Hairpin grob
///
/// Hairpin parameters from LilyPond:
/// - height: 0.6666 staff spaces (maximum opening)
/// - bound-padding: 1.0
/// - minimum-length: 2.0 — a SPACING rod (springs-and-rods =
///   ly:spanner::set-spacing-rods), never a draw-time stretch; unported (springs regime)
/// - thickness: 1.0 (staff line widths)
/// </remarks>
internal static class HairpinEngraver
{

    /// <summary>
    /// Maximum opening of the wedge (half-height).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1785 (height . 0.6666)</remarks>
    private const double Height = 0.6666;

    /// <summary>
    /// Horizontal padding from note/dynamic to hairpin endpoint.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1780 (bound-padding . 1.0)</remarks>
    private const double BoundPadding = 1.0;

    // Height IS the wedge's half-opening (LP's `height` property): the two
    // arms sit at ±fullOpening, so the open end's full mouth is 2·Height,
    // matching LilyPond. (A stray /2 here made every hairpin half-height,
    // so the flat wedge read as "not closing" at a broken continuation.)
    // LILYPOND-REF: lily/hairpin.cc — Line(x, ±starth) … (width, ±endh).
    private const double fullOpening = Height;
    // LILYPOND-REF: lily/hairpin.cc:300-313 — broken hairpin height fractions
    // (crescendo: first piece 0→2·height/3, continuation height/3→height).
    private const double continuedOpening = fullOpening * ContinuedFraction;
    private const double continuingOpening = fullOpening * ContinuingFraction;

    /// <summary>
    /// The staff middle's own place in the frame a <see cref="HairpinLayout"/> stores:
    /// Y-up from the SYSTEM TOP, and the staff's top line is the system top, so its middle
    /// is half a staff below. The side-position port works in the staff-middle frame that
    /// <see cref="DynamicEngraver"/> shares; this is the only conversion between them.
    /// </summary>
    private const double StaffMiddleBelowSystemTop = EngravingDefaults.StaffMiddle;

    /// <summary>
    /// Height fraction for the broken end of a continued hairpin (right edge at line break).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/hairpin.cc:300-313 — broken hairpin height fractions</remarks>
    private const double ContinuedFraction = 2.0 / 3.0;

    /// <summary>
    /// Height fraction for the broken end of a continuing hairpin (left edge at line start).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/hairpin.cc:300-313 — broken hairpin height fractions</remarks>
    private const double ContinuingFraction = 1.0 / 3.0;

    /// <summary>
    /// Calculates layout for all hairpins in a score.
    /// Handles broken hairpins across system breaks with correct height fractions.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:300-313 — broken hairpin height calculation
    /// When a hairpin crosses a system break:
    /// - continued (end of first system): opening = height * 2/3
    /// - continuing (start of next system): opening = height * 1/3
    /// </remarks>
    public static ImmutableArray<HairpinLayout> Calculate(
        ImmutableArray<HairpinItem> hairpins,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, int, double>? staffYAt = null,
        ImmutableArray<Voice> voices = default,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        ImmutableArray<BeamLayout> beamLayouts = default,
        ImmutableArray<DynamicLayout> dynamicLayouts = default)
    {
        if (hairpins.IsDefaultOrEmpty)
            return ImmutableArray<HairpinLayout>.Empty;

        // LILYPOND-REF: lily/system.cc:143-192 — fixup_refpoints walks all systems once.
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = ImmutableArray.CreateBuilder<HairpinLayout>();
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);
        // Indexed ONCE: the text-bound lookup runs twice per hairpin, and a linear
        // scan made a 1000-bar hairpin+dynamic page measurably slower (A/B min
        // 3.8s → 4.5s). First entry per key wins, like the scan it replaces.
        var dynamicAt = BuildDynamicIndex(dynamicLayouts);

        foreach (var hairpin in hairpins)
        {
            // An end of (lastMeasure+1, 0) is the moment PAST the music — the FINAL
            // barline. Under to-barline that is a drawable bound (the bar at the last
            // measure's end), so only truly unplaceable spans are skipped. A trailing
            // "c1\> <>\pp" used to vanish whole here.
            // LILYPOND-REF: lily/bar-engraver.cc:548-558 process_acknowledged —
            //   set_bound (RIGHT, bar_) rewrites the bound to the bar item standing
            //   at the end timestep, and the final bar stands at the final timestep.
            bool endsAtFinalBar = hairpin.EndItemIndex == 0
                && hairpin.EndMeasureIndex == measureLayouts.Length;
            if (hairpin.StartMeasureIndex >= measureLayouts.Length ||
                (hairpin.EndMeasureIndex >= measureLayouts.Length && !endsAtFinalBar))
                continue;

            // The wedge hangs below ITS staff; add the staff's within-system offset so a
            // hairpin on staff 2 sits under staff 2, not staff 1. Staff 0 (or a single
            // staff) has offset 0 -> unchanged. The per-staff stacker then keeps it clear
            // of that staff's dynamics only.
            double staffOffset = staffYAt?.Invoke(hairpin.StartMeasureIndex, hairpin.StaffIndex) ?? 0;

            // This hairpin's own staff: its voices (to support off the right heads and
            // stems) and its measures (to place the columns in X).
            var hpVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(hairpin.StaffIndex, out var vv) ? vv : voices;
            var hpMeasures = LayoutUtilities.ResolveStaffMeasures(
                measuresByStaff, hairpin.StaffIndex,
                hpVoices.IsDefaultOrEmpty ? ImmutableArray<Measure>.Empty : hpVoices[0].Measures);

            // The RIGHT bound under to-barline: a terminator on a measure START binds
            // the hairpin to the BAR LINE before it — Hairpin has to-barline = #t, so
            // the Bar_engraver rewrites the right bound to the BarLine item standing
            // at the end timestep, and the non-musical bound then pays the full
            // bound-padding off its right edge. Mid-line that bar ends exactly at the
            // measure's X (the renderer draws it at ml.X − width); when the terminator
            // measure OPENS a system, the bar it binds to is the PREVIOUS system's
            // end bar (drawn inside that measure's width), and the spanner never
            // enters the terminator's system — the piece list must stop a measure
            // early, or a phantom stub piece appears at the new line's head
            // and stacks against the next hairpin. Measured: 51.025−1.0 and
            // 50.598−1.0 (dynamics-broken-hairpin), 21.021−1.0 (probe-hairpin-bounds).
            // LILYPOND-REF: lily/bar-engraver.cc:579-587 acknowledge_end_spanner and
            //   :548-558 process_acknowledged — set_bound (RIGHT, bar_)
            // LILYPOND-REF: lily/hairpin.cc:283-284 — Item::is_non_musical → −padding
            // LILYPOND-REF: scm/define-grobs.scm Hairpin — (to-barline . #t) (bound-padding . 1.0)
            int endMeasureIdx = hairpin.EndMeasureIndex;
            if (hairpin.EndItemIndex == 0 && hairpin.EndMeasureIndex > 0)
            {
                bool opensSystem =
                    !endsAtFinalBar
                    && measureToSystemIdx.TryGetValue(hairpin.EndMeasureIndex, out int endSys)
                    && measureToSystemIdx.TryGetValue(hairpin.EndMeasureIndex - 1, out int prevSys)
                    && endSys != prevSys;
                if (opensSystem || endsAtFinalBar)
                    endMeasureIdx = hairpin.EndMeasureIndex - 1;
            }

            // LILYPOND-REF: lily/spanner.cc:36-144 — broken once per system; bounds
            // reattached to the system edges. LilyPond breaks the DynamicLineSpanner with
            // it and side-positions EACH piece against the supports that fall inside it
            // (break-substitution.cc:67-153 substitute_grob / do_break_substitution
            // rewrites the support list per piece), so the level is resolved here and not
            // once for the whole span.
            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                hairpin.StartMeasureIndex, endMeasureIdx, systems, measureToSystemIdx))
                layouts.Add(LayoutPiece(hairpin, segment, system, endMeasureIdx, measureLayouts,
                    staffOffset, hpVoices, hpMeasures, beamMembers, dynamicAt));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// The pieces of these hairpins that fall on ONE system, laid out on that system alone.
    /// For the room (<c>MultiStaffLayouter.BuildAllStaffSkylines</c>), which reserves each
    /// staff's wedges in its silhouette before the systems are placed: it hands over a
    /// trivial one-staff system holding this system's measures, an array keyed by MEASURE
    /// INDEX (empty where this system has no such measure), and the score's measure count.
    /// A hairpin that reaches into this system from an earlier one, or out of it into a later
    /// one, gets the same continuing / continued piece <see cref="Calculate"/> gives it.
    /// </summary>
    /// <remarks>
    /// The one system-shaped question <see cref="Calculate"/> asks of the real systems — does
    /// the terminator's measure OPEN a system, so that the to-barline bound is the previous
    /// line's end bar — is answered here from this system's own bounds: the terminator opens
    /// THIS system (the wedge never enters it) or the NEXT (it ends at this line's end bar).
    /// LILYPOND-REF: lily/bar-engraver.cc:548-558 process_acknowledged — set_bound (RIGHT,
    ///   bar_), the same rewrite the whole-score path reads.
    /// ⚠️ Two things the trivial system cannot say, both disclosed: it has no delimiter, so
    ///   a piece broken at the line end does not back off the span bar's half padding
    ///   (<see cref="SpanBarBelowOnSystem"/>) — 0.5 of length, never of height; and the
    ///   DynamicLineSpanner GROUPING (a text seated on its wedge's line,
    ///   <see cref="DynamicAlignEngraver"/>) is not replayed, so the room reserves each
    ///   text where its own column puts it. Both under-reserve by construction and neither
    ///   moves a wedge's height.
    /// </remarks>
    internal static ImmutableArray<HairpinLayout> CalculateOnSystem(
        ImmutableArray<HairpinItem> hairpins,
        SystemLayout system,
        int scoreMeasureCount,
        ImmutableArray<MeasureLayout> byMeasureIndex,
        ImmutableArray<Voice> staffVoices,
        ImmutableArray<Measure> staffMeasures,
        ImmutableArray<BeamLayout> beamLayouts,
        ImmutableArray<DynamicLayout> dynamicLayouts)
    {
        if (hairpins.IsDefaultOrEmpty || system.Measures.IsDefaultOrEmpty)
            return ImmutableArray<HairpinLayout>.Empty;

        int first = system.Measures[0].MeasureIndex;
        int last = system.Measures[^1].MeasureIndex;
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);
        var dynamicAt = BuildDynamicIndex(dynamicLayouts);
        var layouts = ImmutableArray.CreateBuilder<HairpinLayout>();

        foreach (var hairpin in hairpins)
        {
            bool endsAtFinalBar = hairpin.EndItemIndex == 0
                && hairpin.EndMeasureIndex == scoreMeasureCount;
            if (hairpin.StartMeasureIndex >= scoreMeasureCount ||
                (hairpin.EndMeasureIndex >= scoreMeasureCount && !endsAtFinalBar))
                continue;

            int endMeasureIdx = hairpin.EndMeasureIndex;
            if (hairpin.EndItemIndex == 0 && hairpin.EndMeasureIndex > 0
                && (endsAtFinalBar
                    || hairpin.EndMeasureIndex == first
                    || hairpin.EndMeasureIndex == last + 1))
                endMeasureIdx = hairpin.EndMeasureIndex - 1;

            if (hairpin.StartMeasureIndex > last || endMeasureIdx < first)
                continue;

            bool isFirst = hairpin.StartMeasureIndex >= first;
            bool isLast = endMeasureIdx <= last;
            var segment = new SpannerBreakSegment(
                SystemIndex: 0,
                StartMeasureIndex: Math.Max(hairpin.StartMeasureIndex, first),
                EndMeasureIndex: Math.Min(endMeasureIdx, last),
                IsFirst: isFirst, IsLast: isLast, IsMiddle: !isFirst && !isLast);
            layouts.Add(LayoutPiece(hairpin, segment, system, endMeasureIdx, byMeasureIndex,
                staffOffset: 0.0, staffVoices, staffMeasures, beamMembers, dynamicAt));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// One broken piece of one hairpin: its bounds on <paramref name="system"/>, its openings
    /// and its level. <paramref name="endMeasureIdx"/> is the measure the RIGHT bound stands
    /// in — the terminator's own measure, or the one before it when the bound was rewritten
    /// to a bar (the caller's to-barline answer). <paramref name="measureLayouts"/> is keyed
    /// by measure index; only the measures of the piece's own segment, and the bound
    /// measures of a FIRST / LAST piece, are read from it.
    /// </summary>
    private static HairpinLayout LayoutPiece(
        HairpinItem hairpin, SpannerBreakSegment segment, SystemLayout system,
        int endMeasureIdx, ImmutableArray<MeasureLayout> measureLayouts, double staffOffset,
        ImmutableArray<Voice> hpVoices, ImmutableArray<Measure> hpMeasures,
        Dictionary<(int Staff, int Voice, int Measure, int Item),
            (BeamLayout Beam, double StemX, bool StemUp)> beamMembers,
        Dictionary<(int Measure, int Item, int Staff), DynamicLayout> dynamicAt)
    {
        int staffIdx = hairpin.StaffIndex;

        // The RIGHT bound, read only for the piece that carries it (a cut right edge is the
        // system's, ReattachSpanX): under to-barline the bound is the bar the terminator's
        // measure start rewrote to — drawn INSIDE the previous measure's width when that
        // measure opens a system or is the final bar (endMeasureIdx is then one less), and
        // at the measure's own X otherwise; at a mid-measure terminator with a dynamic text
        // the bound is the TEXT, and the wedge stops a full bound-padding left of its ink.
        // LILYPOND-REF: lily/hairpin.cc:214-218 — Text_interface bound,
        //   x_points[d] = e[-d] − d·padding. Measured: probe-hairpin-bounds
        //   line 3, end = f-left − 1.0 = 9.132.
        double ownEndX = 0.0;
        if (segment.IsLast)
        {
            if (hairpin.EndItemIndex == 0 && hairpin.EndMeasureIndex > 0)
            {
                if (endMeasureIdx != hairpin.EndMeasureIndex)
                {
                    var prevM = measureLayouts[endMeasureIdx];
                    ownEndX = prevM.X + prevM.Width - BoundPadding;
                }
                else
                {
                    ownEndX = measureLayouts[hairpin.EndMeasureIndex].X - BoundPadding;
                }
            }
            else if (hairpin.NienteAtEnd != null
                     && ColumnExtent(hairpin.EndMeasureIndex, hairpin.EndItemIndex, hpMeasures,
                         measureLayouts) is { } endColumn)
            {
                // A niente is no DynamicText — the twin ends the hairpin with \! — so the bound
                // is the note column: its right edge (the default endpoint alignment), a rest's
                // left edge, or, where a crescendo dal niente starts on the same note, just
                // right of the column's centre, so that the two circles fall on one place.
                // LILYPOND-REF: lily/dynamic-engraver.cc:246-249 acknowledge_note_column —
                //   finished_spanner_->set_bound (RIGHT, info.grob ())
                // LILYPOND-REF: lily/hairpin.cc:243-281 Hairpin::print — x_points: the adjacent circled
                //   tip e.center () + d * (rad - thick / 2.0); a rest's e[-d]; else e[d] for
                //   endpoint_alignments[RIGHT] == RIGHT
                ownEndX = hairpin.TipAdjacent && hairpin.Direction == HairpinDirection.Decrescendo
                    ? (endColumn.Left + endColumn.Right) / 2 + (CircleRadius - CircleThickness / 2.0)
                    : endColumn.IsRest ? endColumn.Left : endColumn.Right;
            }
            else if (dynamicAt.TryGetValue(
                         (hairpin.EndMeasureIndex, hairpin.EndItemIndex, hairpin.StaffIndex),
                         out var endDyn)
                     && (endDyn.GlyphRunWidth ?? DynamicOutline.AdvanceWidth(endDyn.Text)) is { } endW)
            {
                ownEndX = endDyn.X - endW / 2.0 - BoundPadding;
            }
            else
            {
                ownEndX = CalculateEndX(hairpin, measureLayouts);
            }
        }

        // The LEFT bound at a concurrent dynamic text: the wedge opens a full
        // bound-padding right of the text's ink — LilyPond's dynamic engraver
        // hands the hairpin the DynamicText item as its start bound. Without a
        // text the bound is the note column itself (CalculateStartX). Read only for
        // the FIRST piece, for the same reason as the right bound above.
        // LILYPOND-REF: lily/hairpin.cc:214-218 — Text_interface bound.
        //   Measured: probe-hairpin-bounds line 2, start = p-right + 1.0 = 8.186.
        double ownStartX = 0.0;
        if (segment.IsFirst && hairpin.TipAdjacent
            && hairpin.Direction == HairpinDirection.Crescendo
            && ColumnExtent(hairpin.StartMeasureIndex, hairpin.StartItemIndex, hpMeasures,
                measureLayouts) is { } startColumn)
            // A crescendo dal niente from the note a decrescendo al niente ends on: just left of
            // the column's centre, the mirror of that decrescendo's end.
            // LILYPOND-REF: lily/hairpin.cc:243-252 Hairpin::print — x_points[LEFT] =
            //   e.center () - (rad - thick / 2.0)
            ownStartX = (startColumn.Left + startColumn.Right) / 2 - (CircleRadius - CircleThickness / 2.0);
        else if (segment.IsFirst)
            ownStartX =
                dynamicAt.TryGetValue(
                    (hairpin.StartMeasureIndex, hairpin.StartItemIndex, hairpin.StaffIndex),
                    out var startDyn)
                && (startDyn.GlyphRunWidth ?? DynamicOutline.AdvanceWidth(startDyn.Text)) is { } startW
                ? startDyn.X + startW / 2.0 + BoundPadding
                : CalculateStartX(hairpin, measureLayouts);

        {
            {
                var (segStartX, segEndX) = SpannerBreakSubstitution.ReattachSpanX(
                    segment, system, ownStartX, ownEndX);

                // A broken LEFT bound pays the bound-padding off the break column's
                // right edge (= the line's content start, which ReattachSpanX already
                // returns). Measured: continuation pieces start 1.0 right of the first
                // measure's X (dynamics-broken-hairpin 4.365 = 3.365 + 1.0).
                // LILYPOND-REF: lily/hairpin.cc:191-194 — x_points[LEFT] = e[-d] + padding
                if (!segment.IsFirst)
                    segStartX += BoundPadding;

                // A broken RIGHT bound (line end) backs off HALF the bound-padding to
                // clear the SPAN BAR between its staff and the next staff below — paid
                // only when that neighbor is VISIBLE on this system (a hara-kiri'd
                // neighbor takes the span bar with it, and the wedge runs to the line
                // end — that contrast is regression hairpin-span-bar.ly's claim).
                // Lily#'s hairpin hangs below its staff, so the neighbor side is
                // always DOWN (LilyPond reads the DynamicLineSpanner's direction; UP
                // is unreachable here — the parser rejects '@cresc.up'). Measured:
                // hairpin-span-bar twin, line-end 101.93 = 102.43 − 0.5 with the span
                // bar, 102.43 without.
                // ⚠️ UNPORTED (disclosed): LilyPond takes the MAX of this padding
                //   across all CONCURRENT hairpins broken at the same line end, so
                //   simultaneously broken wedges keep their right ends ALIGNED even
                //   when only one of them stands over a span bar.
                //   departs from: lily/hairpin.cc:199-208 Hairpin::print — max over
                //     the concurrent-hairpins set, which concurrent-hairpin-engraver.cc
                //     populates SCORE-wide (ly/engraver-init.ly:776), i.e. across staves.
                //   observed by: NOTHING — no fixture or corpus book breaks two
                //     hairpins at one line end with differing span-bar answers; when
                //     one does, wire a per-system max over this padding first.
                // ⚠️ The line-end bar is assumed to PRINT (LilyPond reads has-span-bar
                //   off the actual line-end BarLine grob); a barline-less line end is
                //   not consulted here.
                // LILYPOND-REF: lily/hairpin.cc:53-109 Hairpin::broken_bound_padding — bound-padding / 2.0 when both staves' line-end bars share one span bar
                // LILYPOND-REF: lily/hairpin.cc:195-210 Hairpin::print — x_points[RIGHT] -= broken_bound_padding at the broken RIGHT bound
                // LILYPOND-REF: scm/define-grobs.scm:1780-1781 Hairpin — bound-padding 1.0 and the broken-bound-padding callback
                if (!segment.IsLast && SpanBarBelowOnSystem(system, staffIdx))
                    segEndX -= BoundPadding / 2.0;

                // Crossed bounds draw as a point — LilyPond warns "(de)crescendo too
                // small" and clamps the WIDTH to zero; it never stretches the drawn
                // wedge to minimum-length (that property is a SPACING rod, spent
                // between the bound columns — unported, the spring side's ticket).
                // Measured: dynamics-line.ly's to-barline end 21.985 − start 20.474 =
                // 1.511 < 2.0, drawn as-is.
                // LILYPOND-REF: lily/hairpin.cc:292-299 Hairpin::print — width = x_points[RIGHT]
                //   − x_points[LEFT]; if (width < 0) width = 0 (with the "too small" warning)
                // LILYPOND-REF: scm/define-grobs.scm:1786-1788 Hairpin minimum-length 2.0 rides springs-and-rods
                //   (ly:spanner::set-spacing-rods), the spacing side, not the stencil
                if (segEndX < segStartX)
                    segEndX = segStartX;

                // The circle at a niente tip, on the piece that holds the tip: the arms give it
                // room — a crescendo's start 2·rad right, a decrescendo's width 2·rad shorter —
                // and it is set at their edge, beyond the arms' half thickness. The width is not
                // clamped again, as LilyPond does not.
                // LILYPOND-REF: lily/hairpin.cc:323-355 Hairpin::print, add_at_edge — "Compensate for size of
                //   circle": if (circled_tip && !broken[tip_dir]) x = rad * 2.0 (BIGGER) /
                //   width -= rad * 2.0 (SMALLER); then mol.add_at_edge (X_AXIS, tip_dir, circle, 0)
                // LILYPOND-REF: lily/line-interface.cc:177-191 Line_interface::make_line — the
                //   arm's box is widened by th / 2, the edge add_at_edge sets the circle against
                double circleX = double.NaN;
                bool tipBroken = hairpin.Direction == HairpinDirection.Crescendo
                    ? !segment.IsFirst : !segment.IsLast;
                if (hairpin.CircledTip && !tipBroken)
                {
                    if (hairpin.Direction == HairpinDirection.Crescendo)
                    {
                        segStartX += CircleRadius * 2.0;
                        circleX = segStartX - CircleThickness / 2.0 - CircleRadius;
                    }
                    else
                    {
                        segEndX -= CircleRadius * 2.0;
                        circleX = segEndX + CircleThickness / 2.0 + CircleRadius;
                    }
                }

                double startOpening, endOpening;
                if (hairpin.Direction == HairpinDirection.Crescendo)
                {
                    startOpening = segment.IsFirst ? 0 : continuingOpening;
                    endOpening = segment.IsLast ? fullOpening : continuedOpening;
                }
                else
                {
                    // Decrescendo: LP hairpin.cc:305-309 — starth = continuing ? 2h/3 : h,
                    // endh = continued ? h/3 : 0. The interior fractions are the MIRROR of
                    // the crescendo case: a non-first left mouth is 2h/3 (continuedOpening),
                    // a non-last right mouth is h/3 (continuingOpening).
                    startOpening = segment.IsFirst ? fullOpening : continuedOpening;
                    endOpening = segment.IsLast ? 0 : continuingOpening;
                }

                // THE LEVEL IS THE DynamicLineSpanner'S OWN OFFSET, not a constant: the
                // same aligned_side DynamicEngraver runs for the text on the same spanner,
                // called with the WEDGE's outline and spending no child offset (the
                // Hairpin is self-alignment-Y CENTER on the spanner, where DynamicText
                // hangs 0.6 below it). MEASURED in LilyPond 2.26.0 on the ledger's own
                // texture: the spanner refpoint sits 3.366600 below the staff refpoint
                // = staff ink 2.05 + padding 0.6 + the wedge's own half height 0.7166.
                // LILYPOND-REF: scm/define-grobs.scm DynamicLineSpanner
                //   (Y-offset . side-position-interface::y-aligned-side) and its own
                //   description, "a vertical baseline to align successive dynamic grobs
                //   (DynamicText, DynamicTextSpanner, and Hairpin)".
                var support = DynamicEngraver.SpanSupportSkylines(
                    hpVoices, VoiceIndex,
                    SpanColumns(hairpin, segment, hpMeasures, measureLayouts),
                    (vi, mi, ii) => beamMembers.TryGetValue((staffIdx, vi, mi, ii), out var b)
                        ? b : null);
                double spannerY = DynamicEngraver.SpannerOffsetY(dir: -1.0, support,
                    WedgeSkylines(segStartX, segEndX, startOpening, endOpening, 0.0, circleX));
                // spannerY is Y-up about the staff middle; the layout frame is Y-up from
                // the SYSTEM top, and staffOffset is a within-system downward offset.
                double hairpinYUp = spannerY - StaffMiddleBelowSystemTop - staffOffset;

                return new HairpinLayout(
                    segment.StartMeasureIndex, segStartX, segEndX, hairpinYUp,
                    startOpening, endOpening, hairpin.Direction, hairpin.SourcePosition,
                    hairpin.SourceIndex, hairpin.StaffIndex, circleX);
            }
        }
    }

    /// <summary>
    /// ⚠️ LILYSHARP-OWN, DECLARED: the voice a hairpin supports off. LilyPond's
    /// Dynamic_align_engraver is consisted into the <c>Voice</c> context
    /// (ly/engraver-init.ly:359,410), so a hairpin in the lower voice sides off the LOWER
    /// voice's heads and stems. Lily# cannot ask that question here — a hairpin comes from
    /// a <see cref="MusicMarkItem"/>, which carries a staff but no voice — so it takes the
    /// staff's first voice. The other voices' ink still reaches the wedge, through the
    /// outside-staff collision pass over the whole staff profile
    /// (<see cref="OutsideStaffStacker"/>), which is the route LilyPond uses for them too;
    /// what is lost is only the 0.6-padding side-position support of a hairpin authored in
    /// a non-first voice. It goes when MusicMarkItem carries its voice.
    /// ⚠️ NOTHING OBSERVES IT: the corpus has no point on a hairpin in a lower voice, and
    /// the pair that would see it is a two-voice staff whose lower voice descends further
    /// than 0.14 below where the collision pass would put the wedge anyway (the gap
    /// between this padding, 0.6, and outside-staff-padding, 0.46).
    /// </summary>
    private const int VoiceIndex = 0;

    /// <summary>
    /// The wedge's OWN skyline pair (<c>my_dim</c>) about the spanner origin: the two
    /// drawn arms, each a straight edge from its start opening to its end opening, widened
    /// by half the rule's thickness — the same two lines
    /// <c>SharedRenderer.DrawHairpins</c> puts on the page.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm Hairpin
    ///   <c>(vertical-skylines . grob::unpure-vertical-skylines-from-stencil)</c> — the
    ///   profile is the STENCIL, so it narrows to the apex rather than being the max
    ///   half-height in a box; and <c>(self-alignment-Y . CENTER)</c> with
    ///   <c>(Y-offset . self-alignment-interface::y-aligned-on-self)</c> centres the wedge
    ///   on the spanner, which is why <paramref name="centreYUp"/> is the spanner's own
    ///   origin and no child offset is spent.
    /// LILYPOND-REF: lily/hairpin.cc:110-358 <c>Hairpin::print</c> (:124 <c>grow_dir</c>,
    ///   :304-309 <c>starth</c> / <c>endh</c>) — the arms are straight lines from ±starth to
    ///   ±endh, and the rule is centred on them at <c>thickness</c>.
    /// </remarks>
    /// <param name="circleX">The centre of the niente circle (<see cref="HairpinLayout.CircleX"/>),
    /// or NaN: the circle's box joins both sides of the profile.</param>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:342-355 Hairpin::print — circled_tip: the circle stencil's extent is
    ///   <c>Box extent (Interval (-rad, rad), Interval (-rad, rad))</c>, added to the wedge's.
    /// </remarks>
    internal static (VerticalSkyline Up, VerticalSkyline Down) WedgeSkylines(
        double startX, double endX, double startOpening, double endOpening, double centreYUp,
        double circleX = double.NaN)
    {
        double half = EngravingDefaults.StaffLineThickness / 2.0;
        var up = VerticalSkyline.FromSlope(
            startX, centreYUp + startOpening + half,
            endX, centreYUp + endOpening + half,
            thickness: 0, VerticalDirection.Up);
        var down = VerticalSkyline.FromSlope(
            startX, centreYUp - startOpening - half,
            endX, centreYUp - endOpening - half,
            thickness: 0, VerticalDirection.Down);
        if (!double.IsNaN(circleX))
        {
            up.MergeBox(circleX - CircleRadius, circleX + CircleRadius,
                centreYUp - CircleRadius, centreYUp + CircleRadius);
            down.MergeBox(circleX - CircleRadius, circleX + CircleRadius,
                centreYUp - CircleRadius, centreYUp + CircleRadius);
        }
        return (up, down);
    }

    /// <summary>
    /// The radius of the circle at a niente tip: the hairpin's <c>height</c> × 0.525, which
    /// LilyPond itself calls a guess.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:150-160 Hairpin::print — circled_tip; Real rad = height * 0.525
    ///   ("FIXME: 0.525 is still just a guess... same method is used in `circle-radius' of
    ///   scm/output-lib.scm"), height = the grob's height × staff_space
    /// </remarks>
    internal const double CircleRadius = Height * 0.525;

    /// <summary>
    /// The circle's line, and the arms' (whose half widens the edge the circle is set against):
    /// the hairpin's <c>thickness</c> 1.0 × the staff's line thickness.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:161-164 Hairpin::print — thick = thickness × line_thickness
    ///   when circled_tip
    /// LILYPOND-REF: scm/define-grobs.scm Hairpin (thickness . 1.0)
    /// </remarks>
    internal static readonly double CircleThickness = 1.0 * EngravingDefaults.StaffLineThickness;

    /// <summary>
    /// The X extent of the note column at (<paramref name="measure"/>, <paramref name="item"/>)
    /// on the hairpin's own staff, as a hairpin bound sees it — its heads united with its stem;
    /// a rest's own glyph box — or null when no such column is there.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:111-135 Axis_group_interface::generic_bound_extent
    ///   — the elements with a <c>bound-alignment-interfaces</c> interface
    /// LILYPOND-REF: scm/define-grobs.scm:2574 NoteColumn (bound-alignment-interfaces .
    ///   (rhythmic-head-interface stem-interface))
    /// ⚠️ A chord's second-clash head on the far side of the stem is not in this extent (the
    ///   column's head box is the normal-side one) — the bracket bound's
    ///   (TupletBracketEngraver.BoundEdgeOffset) reading, and no pair measures it.
    /// </remarks>
    private static (double Left, double Right, bool IsRest)? ColumnExtent(
        int measure, int item, ImmutableArray<Measure> staffMeasures,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (staffMeasures.IsDefaultOrEmpty || measure >= staffMeasures.Length
            || measure >= measureLayouts.Length || measureLayouts[measure] is not { } layout
            || item < 0 || item >= staffMeasures[measure].Items.Length)
            return null;
        double x = layout.X + LayoutUtilities.GetItemXOffset(staffMeasures, measure, item, layout);
        double halfStem = EngravingDefaults.StemThickness / 2;
        switch (staffMeasures[measure].Items[item])
        {
            case RestItem { IsSpacer: false } rest:
            {
                var box = GlyphMetrics.GetRestBBox(GlyphMetrics.NoteValueOf(rest.BaseDuration));
                return (x + box.Left, x + box.Right, true);
            }
            case NoteItem or ChordItem:
            {
                var music = staffMeasures[measure].Items[item];
                int value = GlyphMetrics.NoteValueOf(music);
                var head = GlyphMetrics.GetNoteheadBBox(value);
                double left = head.Left, right = head.Right;
                // LILYPOND-REF: lily/stem.cc Stem::is_normal_stem — a whole or a breve has no stem.
                if (value >= 2)
                {
                    bool up = music is NoteItem n ? n.StemUp : ((ChordItem)music).StemUp;
                    double stemX = LayoutUtilities.StemAttachX(up, value, LayoutUtilities.NoteheadStyleOf(music));
                    left = Math.Min(left, stemX - halfStem);
                    right = Math.Max(right, stemX + halfStem);
                }
                return (x + left, x + right, false);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether a span bar stands between the given staff and the next staff below it
    /// on this system: the staff's group must be delimited (the renderer's span-bar
    /// gate, <c>DrawSpanBars</c>) and a staff of the same group must be VISIBLE below
    /// it there — hara-kiri hides a staff together with its share of the span bar.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:53-109 Hairpin::broken_bound_padding — get_neighboring_staff (DOWN) and the shared has-span-bar grob
    /// </remarks>
    private static bool SpanBarBelowOnSystem(SystemLayout system, int staffIdx)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return false;
        foreach (var group in system.StaffGroups)
        {
            if (!group.HasDelimiter)
                continue;
            var staves = group.Staves;
            for (int i = 0; i < staves.Length; i++)
            {
                if (staves[i].StaffIndex != staffIdx || staves[i].IsHidden)
                    continue;
                for (int j = i + 1; j < staves.Length; j++)
                    if (!staves[j].IsHidden)
                        return true;
                break;
            }
        }
        // An outer group's span bar runs below a staff that has a visible staff after it
        // anywhere in the group's run, not only in its own leaf group — when that group, or any
        // group around it, draws bar lines through (SharedRenderer.DrawStaffConnectors).
        foreach (var run in MultiStaffLayouter.OuterRuns(system))
        {
            if (!run.Outer.SelfAndOuters().Any(o => o.Type != StaffGroupType.ChoirStaff))
                continue;
            var runStaves = run.Leaves.SelectMany(l => l.Staves).ToList();
            int at = runStaves.FindIndex(s => s.StaffIndex == staffIdx && !s.IsHidden);
            if (at >= 0 && runStaves.Skip(at + 1).Any(s => !s.IsHidden))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The (measure, item, column X) of every note column this broken piece runs over —
    /// the timesteps whose heads and stems LilyPond's Dynamic_align_engraver adds as
    /// support while the line spanner is alive.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dynamic-align-engraver.cc:222-223 <c>add_support</c> per
    ///   <c>stop_translation_timestep</c>, from the timestep that created the spanner to
    ///   the one that ends it — so the range is the hairpin's own span, clipped to this
    ///   piece's measures.
    /// </remarks>
    private static IEnumerable<(int Measure, int Item, double X)> SpanColumns(
        HairpinItem hairpin, SpannerBreakSegment segment,
        ImmutableArray<Measure> staffMeasures, ImmutableArray<MeasureLayout> measureLayouts)
    {
        // No measures means no caller supplied a staff — the harness case (a unit test that
        // builds layouts directly). The support is then the staff's own extent alone, which
        // is aligned_side's include_staff minimum and NOT a silent empty: HairpinTests'
        // Calculate_Y_IsAlignedSideOffTheStaff_NotAConstant is that state, asserted.
        if (staffMeasures.IsDefaultOrEmpty)
            yield break;
        int first = Math.Max(segment.StartMeasureIndex, hairpin.StartMeasureIndex);
        int last = Math.Min(segment.EndMeasureIndex, hairpin.EndMeasureIndex);
        for (int m = first; m <= last && m < measureLayouts.Length; m++)
        {
            if (m >= staffMeasures.Length)
                break;
            var layout = measureLayouts[m];
            int itemCount = staffMeasures[m].Items.Length;
            int from = m == hairpin.StartMeasureIndex ? hairpin.StartItemIndex : 0;
            int to = m == hairpin.EndMeasureIndex ? hairpin.EndItemIndex : itemCount - 1;
            for (int i = Math.Max(0, from); i <= to && i < itemCount; i++)
                yield return (m, i,
                    layout.X + LayoutUtilities.GetItemXOffset(staffMeasures, m, i, layout));
        }
    }

    /// <summary>
    /// The dynamic texts a hairpin bound can stand against, indexed by their moment —
    /// same staff, below the staff, a real level (expressive text rides the dynamics
    /// table but is a different grob). First entry per key wins.
    /// </summary>
    private static Dictionary<(int Measure, int Item, int Staff), DynamicLayout> BuildDynamicIndex(
        ImmutableArray<DynamicLayout> dynamicLayouts)
    {
        var index = new Dictionary<(int, int, int), DynamicLayout>();
        if (dynamicLayouts.IsDefaultOrEmpty)
            return index;
        foreach (var d in dynamicLayouts)
            if (!d.IsAbove && !d.IsExpressiveText)
                index.TryAdd((d.MeasureIndex, d.ItemIndex, d.StaffIndex), d);
        return index;
    }

    private static double CalculateStartX(HairpinItem hairpin, ImmutableArray<MeasureLayout> measureLayouts)
    {
        var startMeasure = measureLayouts[hairpin.StartMeasureIndex];
        if (hairpin.StartItemIndex < startMeasure.Items.Length)
        {
            // The LEFT bound on a note is the note column's LEFT edge, unpadded:
            // the default endpoint-alignments pick e[LEFT] for a musical bound, and
            // bound-padding is spent only on non-musical/text bounds. Measured: the
            // wedge opens exactly at the notehead's X (probe-hairpin-bounds 8.585 =
            // note X; dynamics-broken-hairpin m1/m3 the same). The old law took the
            // start item's ALLOCATED right edge, which pinned a justified whole-note
            // measure's hairpin to the line end, squeezed to MinimumLength.
            // LILYPOND-REF: lily/hairpin.cc:184-290 print — x_points[d] = e[d] for
            //   endpoint_alignments[LEFT] == LEFT
            // LILYPOND-REF: scm/define-grobs.scm Hairpin — endpoint-alignments (LEFT . RIGHT)
            // ⚠️ item.X is the COLUMN's X (the normal-side head), not the column
            //   extent: a down-stem chord with a second flips a head a full head-width
            //   LEFT of it, and LilyPond's generic_bound_extent would start the wedge
            //   there. No pair measures it (needs a hairpin opening on such a chord).
            var startItem = startMeasure.Items[hairpin.StartItemIndex];
            return startMeasure.X + startItem.X;
        }
        return startMeasure.X + BoundPadding;
    }

    private static double CalculateEndX(HairpinItem hairpin, ImmutableArray<MeasureLayout> measureLayouts)
    {
        var endMeasure = measureLayouts[hairpin.EndMeasureIndex];
        if (hairpin.EndItemIndex < endMeasure.Items.Length)
        {
            var endItem = endMeasure.Items[hairpin.EndItemIndex];
            return endMeasure.X + endItem.X - BoundPadding * 0.5;
        }
        return endMeasure.X + endMeasure.Width - BoundPadding;
    }

    /// <summary>
    /// Detects hairpin spans from music marks and dynamics.
    /// A hairpin starts at a cresc/decresc mark and ends at the next absolute dynamic.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dynamic-engraver.cc start/end event handling
    /// </remarks>
    /// <param name="plays">The score's section plays (<c>Collector.SectionPlays.For</c>),
    /// or null for a score with none: a hairpin open when its section ends is carried into the
    /// section played next and must end there (<see cref="Collector.SectionPlayCursor"/>'s rule).
    /// One that does not — or is carried over a repeat, volta or jump — is cut at the end of its
    /// own section, and reported to <paramref name="carrySink"/>.</param>
    /// <param name="carrySink">Where the refused carries go; null to report none.</param>
    public static ImmutableArray<HairpinItem> DetectHairpins(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<DynamicItem> dynamics,
        Collector.SectionPlays? plays = null,
        List<Collector.SectionCarryWarning>? carrySink = null)
    {
        // Sort all events by position (measure, item). F3/B: keep each mark's ORIGINAL
        // index in musicMarks (== score.MusicMarks) so the hairpin can re-derive its
        // data-pos from the live score on reuse.
        var crescMarks = musicMarks
            .Select((m, i) => (Mark: m, Index: i))
            .Where(x => x.Mark.Type == MusicMarkType.Cresc ||
                        x.Mark.Type == MusicMarkType.Decresc ||
                        x.Mark.Type == MusicMarkType.Dim)
            .OrderBy(x => x.Mark.MeasureIndex)
            .ToList();

        if (crescMarks.Count == 0)
            return ImmutableArray<HairpinItem>.Empty;

        // ⚠️ BUILT AFTER THE GUARD ABOVE, not before it. `ImmutableArray.CreateBuilder<T>()`
        // lays out its first block — 88 B for a reference element — before a single Add, and
        // this method takes the `crescMarks.Count == 0` exit in all 4,010 of its calls over
        // the reader's corpus, so the builder used to be laid out and thrown away every time
        // (session 448).
        var hairpins = ImmutableArray.CreateBuilder<HairpinItem>();

        // Sort dynamics by position. Free expressive text (@text) rides the
        // dynamics table but is NOT a dynamic level — a hairpin must run
        // through "dolce" to the real closing dynamic.
        var sortedDynamics = dynamics
            .Where(d => !d.IsExpressiveText)
            .OrderBy(d => d.MeasureIndex)
            .ThenBy(d => d.ItemIndex)
            .ToList();

        foreach (var (mark, srcIndex) in crescMarks)
        {
            var direction = mark.Type == MusicMarkType.Cresc
                ? HairpinDirection.Crescendo
                : HairpinDirection.Decrescendo;

            // The wedge starts at the mark's OWN moment (\< is a post-event of its
            // note); a collector that didn't stamp the anchor leaves -1 and keeps
            // the old measure-head start.
            int startItem = Math.Max(0, mark.AnchorItemIndex);

            // A hairpin ends at a dynamic / next hairpin ON THE SAME STAFF, and
            // STRICTLY AFTER the start moment: a dynamic AT the start moment is the
            // hairpin's opening text and becomes its LEFT bound, never the
            // terminator — and a text at a LATER moment ends it as the RIGHT bound.
            // Until 2026-08-07 "c\f\> ..." ended its own wedge on that f. Without
            // the staff filter a cresc on staff 2 terminated against staff 1's cresc
            // in the same measure, collapsing both spans to nothing (they share the
            // single score.MusicMarks / Dynamics tables).
            // LILYPOND-REF: lily/dynamic-engraver.cc:170-176 process_music — the
            //   same-timestep DynamicText item is wired
            //   current_spanner_->set_bound (LEFT, script_) and the ending one
            //   finished_spanner_->set_bound (RIGHT, script_).
            var nextDynamic = sortedDynamics
                .FirstOrDefault(d =>
                    d.StaffIndex == mark.StaffIndex &&
                    (d.MeasureIndex > mark.MeasureIndex ||
                     (d.MeasureIndex == mark.MeasureIndex && d.ItemIndex > startItem)));

            // Find the next cresc/decresc mark on this staff (another hairpin starts
            // there). A same-measure mark only counts if it is at a LATER item, so a
            // second cresc on the same beat can't be mistaken for this one's end.
            // ...and NOT ANOTHER PLAYING OF THIS SAME WRITTEN MARK. A form that lists a
            // section twice draws it twice, so one written @cresc becomes one MusicMarkItem
            // per playing — same SourcePosition, different measures — and the object test
            // `!=` cannot tell them apart. MEASURED 2026-08-29 on scratch/p289/c7 against
            // c8, the same section played once and twice: the wedge is 7.58 wide when the
            // section is played once and 17.18 when it is played twice, because the first
            // copy's wedge ran to the second copy of itself. This is the twin of the rit.
            // defect the same day (TextSpannerEngraver.DetectTextSpanners' remark has the
            // report and the reasoning); the two searches are one shape and now carry one
            // guard.
            var nextMark = crescMarks
                .Select(x => x.Mark)
                .FirstOrDefault(m =>
                    m != mark && m.SourcePosition != mark.SourcePosition &&
                    m.StaffIndex == mark.StaffIndex &&
                    (m.MeasureIndex > mark.MeasureIndex ||
                     (m.MeasureIndex == mark.MeasureIndex &&
                      m.AnchorItemIndex > mark.AnchorItemIndex)));

            // End at whichever comes first: next dynamic or next cresc/decresc
            int endMeasure;
            int endItem;
            DynamicItem? endDynamic = null;

            // The dynamic ends it when it stands at or before the next mark. ⚠️ Until session 847
            // only a dynamic at the bar's head beat a mark in the same bar, so `c@p@cresc d@f
            // e@decresc` ended the crescendo at the mark — whose end is its bar's head, before
            // the start — and drew neither hairpin (LilyPond draws both, the f between).
            if (nextDynamic != null && (nextMark == null ||
                nextDynamic.MeasureIndex < nextMark.MeasureIndex ||
                (nextDynamic.MeasureIndex == nextMark.MeasureIndex
                 && nextDynamic.ItemIndex <= Math.Max(0, nextMark.AnchorItemIndex))))
            {
                endMeasure = nextDynamic.MeasureIndex;
                endItem = nextDynamic.ItemIndex;
                endDynamic = nextDynamic;
            }
            else if (nextMark != null)
            {
                endMeasure = nextMark.MeasureIndex;
                endItem = 0;
            }
            else
            {
                // No end found — extend to end of the mark's measure + 1
                endMeasure = mark.MeasureIndex + 1;
                endItem = 0;
            }

            // The carry rule at section boundaries. Only an end the pairing FOUND is judged;
            // the default end above never reaches past the next bar line.
            if (plays != null && (nextDynamic != null || nextMark != null)
                && plays.Judge(mark.MeasureIndex, endMeasure) is { } fault)
            {
                int ownPlay = plays.PlayAt(mark.MeasureIndex);
                carrySink?.Add(new Collector.SectionCarryWarning(mark.SourcePosition,
                    Collector.SectionSpanKind.Hairpin, fault, plays.NameOf(ownPlay),
                    plays.NameOf(ownPlay + 1), Play: ownPlay));
                endMeasure = plays.StartOf(ownPlay + 1);
                endItem = 0;
                endDynamic = null;
            }

            // Only add if there's actually a span
            if (endMeasure > mark.MeasureIndex ||
                (endMeasure == mark.MeasureIndex && endItem > startItem))
            {
                hairpins.Add(new HairpinItem(
                    Direction: direction,
                    StartMeasureIndex: mark.MeasureIndex,
                    StartItemIndex: startItem,
                    EndMeasureIndex: endMeasure,
                    EndItemIndex: endItem,
                    SourcePosition: mark.SourcePosition,
                    SourceIndex: srcIndex,
                    StaffIndex: mark.StaffIndex
                )
                {
                    // The dynamic at the start moment is this hairpin's opening text (the
                    // terminator search above passes over it); a niente there is where a
                    // crescendo grows from.
                    NienteAtStart = sortedDynamics.FirstOrDefault(d =>
                        d.Level == DynamicLevel.Niente && d.StaffIndex == mark.StaffIndex
                        && d.MeasureIndex == mark.MeasureIndex && d.ItemIndex == startItem),
                    NienteAtEnd = endDynamic is { Level: DynamicLevel.Niente } ? endDynamic : null,
                });
            }
        }

        return MarkAdjacentTips(hairpins);
    }

    /// <summary>
    /// Marks the circled tips two hairpins share (<see cref="HairpinItem.TipAdjacent"/>): a
    /// decrescendo to a niente from which a crescendo starts. A book without a niente returns
    /// the list as it is.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dynamic-engraver.cc:155-165 process_music — the hairpin ending and
    ///   the one starting at the same timestep are each other's adjacent-spanners.
    /// </remarks>
    private static ImmutableArray<HairpinItem> MarkAdjacentTips(ImmutableArray<HairpinItem>.Builder hairpins)
    {
        // ⚠️ Not at a bar's head: there the decrescendo's right bound is rewritten to the bar
        // line (to-barline), which is not the column the crescendo starts on, so LilyPond
        // finds no adjacent hairpin and draws two circles.
        // LILYPOND-REF: lily/hairpin.cc:232-235 — adjacent->get_bound (-d)->get_column () ==
        //   b->get_column (); lily/bar-engraver.cc:579-587 acknowledge_end_spanner
        HashSet<DynamicItem>? growsFrom = null;
        foreach (var h in hairpins)
            if (h.Direction == HairpinDirection.Crescendo && h.NienteAtStart is { ItemIndex: > 0 } n)
                (growsFrom ??= new HashSet<DynamicItem>()).Add(n);
        if (growsFrom == null)
            return hairpins.ToImmutable();
        var fadesTo = new HashSet<DynamicItem>();
        for (int i = 0; i < hairpins.Count; i++)
            if (hairpins[i].Direction == HairpinDirection.Decrescendo
                && hairpins[i].NienteAtEnd is { } n && growsFrom.Contains(n))
            {
                fadesTo.Add(n);
                hairpins[i] = hairpins[i] with { TipAdjacent = true };
            }
        for (int i = 0; i < hairpins.Count; i++)
            if (hairpins[i].Direction == HairpinDirection.Crescendo
                && hairpins[i].NienteAtStart is { } n && fadesTo.Contains(n))
                hairpins[i] = hairpins[i] with { TipAdjacent = true };
        return hairpins.ToImmutable();
    }

    /// <summary>
    /// The niente dynamics a circled hairpin tip stands for (<see cref="HairpinItem.CircledNiente"/>):
    /// they are not printed as words. Empty when the dynamics hold no niente, without pairing.
    /// </summary>
    /// <remarks>
    /// Keyed by where the niente stands (<see cref="NienteKey"/>), not by the item: a layout
    /// pass may hold the score's dynamics re-addressed into other items (a multi-staff score's
    /// tables), and the question is the same one.
    /// </remarks>
    internal static HashSet<(int Staff, int Measure, int Item, int Source)> CircledNientes(
        ImmutableArray<MusicMarkItem> musicMarks, ImmutableArray<DynamicItem> dynamics,
        Collector.SectionPlays? plays = null)
    {
        var circled = new HashSet<(int, int, int, int)>();
        if (!HasNiente(dynamics))
            return circled;
        foreach (var h in DetectHairpins(musicMarks, dynamics, plays))
            if (h.CircledNiente is { } n)
                circled.Add(NienteKey(n));
        return circled;
    }

    /// <summary>The key <see cref="CircledNientes"/> answers by.</summary>
    internal static (int Staff, int Measure, int Item, int Source) NienteKey(DynamicItem d)
        => (d.StaffIndex, d.MeasureIndex, d.ItemIndex, d.SourcePosition);

    /// <summary>Whether any of <paramref name="dynamics"/> is a niente — the cheap gate in front
    /// of <see cref="CircledNientes"/>, so a book without one pairs nothing more.</summary>
    internal static bool HasNiente(ImmutableArray<DynamicItem> dynamics)
    {
        if (dynamics.IsDefaultOrEmpty)
            return false;
        foreach (var d in dynamics)
            if (d.Level == DynamicLevel.Niente)
                return true;
        return false;
    }
}
