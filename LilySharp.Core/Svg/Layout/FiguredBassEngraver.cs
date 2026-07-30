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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a figured bass group.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc:197-269 center_continuations,
/// center_repeated_continuations, clear_spanners, process_music
/// LILYPOND-REF: scm/define-grobs.scm:352-364 BassFigure (bass-figure-interface at :359)
/// <para>
/// <c>RowOffsets</c> is the alignment's answer, not this column's: each entry is a ROW's
/// baseline as a distance below the top row's, and every column of the same staff and system
/// carries the same array because they are the same <c>BassFigureAlignment</c>
/// (scm/define-grobs.scm:366-385, stacked by <see cref="BassFigureAlignment.RowOffsets"/>). A
/// column with fewer figures than the deepest one simply uses the first entries.
/// </para>
/// </remarks>
public readonly record struct FiguredBassLayout(
    int MeasureIndex,
    double X,                                         // X position (staff spaces)
    double YUp,                                       // Y-up (frame B): staff-spaces above
                                                      // the staff middle, up+ (topmost figure)
    ImmutableArray<string> FigureTexts,               // Text for each figure, top to bottom
    int SourcePosition,
    int SourceIndex = -1,                             // F3/B: index into score.FiguredBasses
    int StaffIndex = -1,                              // owning staff, so the draw resolves its middle
    ImmutableArray<double> RowOffsets = default       // each row's baseline below the top one's
);

/// <summary>
/// Calculates positions for figured bass figures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - Figured_bass_engraver
/// LILYPOND-REF: lily/figured-bass-position-engraver.cc - positioning
/// LILYPOND-REF: scm/define-grobs.scm:352-364 BassFigure (bass-figure-interface at :359)
///
/// Figured bass appears below the staff, with figures stacked vertically by
/// <see cref="BassFigureAlignment"/> — the one home for the row step, which is where the
/// hand-picked 1.6 that used to live here went (HANDOFF §5.2.1②: the renderer spelled the
/// same quantity 1.5).
/// </remarks>
internal static class FiguredBassEngraver
{
    /// <summary>
    /// The floor <c>aligned_side</c> puts under the row — the staff's own ink plus
    /// <c>BassFigureAlignmentPositioning</c>'s staff-padding, as a distance below the staff
    /// middle.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:433-453 aligned_side's staff_padding floor:
    /// <c>diff = dir * staff_extent[dir] + staff_padding - dir * total_off;
    /// total_off += dir * max (diff, 0.0)</c>, the same block
    /// <c>DynamicEngraver.BaselineY</c> transcribes; the staff extent comes from
    /// <see cref="DynamicEngraver.StaffExtent"/>, the one home for it.
    /// LILYPOND-REF: scm/define-grobs.scm:395 staff-padding 1.0 of BassFigureAlignmentPositioning (side-position-interface at :407).
    /// <para>
    /// ⚠️ MEASURED INERT, and computed anyway because LilyPond computes it (HANDOFF §5.2):
    /// the support placement is staff ink + padding 0.5 + the top digit's cap, and a cap
    /// beats staff-padding − padding = 0.5 for every figure the font draws, so this floor
    /// can never bind for this grob. Ledger <c>figbass.quiet.staff-to-baseline</c> is the
    /// observer — LilyPond reads 3.674795235605315 there, the support placement.
    /// </para>
    /// <para>
    /// ⚠️ IT WAS <c>BelowStaffY 5.0 + StaffPadding 1.0</c> = 4.0 below the middle line, a
    /// Lily#-own fixed offset with no LilyPond counterpart (neither device has one: the
    /// FiguredBass context declares no basic-distance and side-position has no fixed
    /// offset). It was invisible while the cap was the invented 1.5 — 2.05 + 0.5 + 1.5 =
    /// 4.05 cleared it by 0.05 — and the moment the cap became the real one it started
    /// BINDING, holding the quiet regime at exactly 4.000000. An inert invention is one
    /// texture away from being load-bearing.
    /// </para>
    /// </remarks>
    private static double AlignedSideFloorBelowStaff
        => DynamicEngraver.StaffExtent + EngravingDefaults.BassFigureStaffPadding;

    /// <summary>
    /// The figure's own ink above its baseline — <c>BassFigure</c>'s Y-extent.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:356 BassFigure's Y-extent (bass-figure-interface at :359):
    /// <c>(Y-extent . grob::always-Y-extent-from-stencil)</c> —
    /// the extent is the drawn stencil's, per figure, so it is asked of the glyphs that
    /// figure actually draws (<see cref="FiguredBassGlyphRun"/>) rather than of a constant.
    /// <para>
    /// ⚠️ IT WAS A CONSTANT 1.5, described as "the digit CAP height at the 3.0 ss figure
    /// font". Both halves of that were Lily#'s own: the em is 4 ss × magstep(−5) = 2.244924
    /// (<see cref="EngravingDefaults.FiguredBassFontSize"/>) and the face is Emmentaler's
    /// number cut, whose digit is 0.5 em — so the reservation asked for 1.5 where LilyPond
    /// reads 1.124795235605315, and the DRAWING (a serif face whose real digit ink was
    /// 2.112000) agreed with neither. That single term stood under every figured-bass ledger
    /// point as +0.375204764 and printed the digits 0.112 through the stem above them.
    /// </para>
    /// </remarks>
    internal static double FigureInkTop(string topFigureText)
        => FiguredBassGlyphRun.InkTop(topFigureText);

    // LILYSHARP-OWN: the WIDTH of the box a figure offers the skyline. LilyPond has no such
    // number — a BassFigure's X-extent is its stencil's, i.e. the same run
    // FiguredBassGlyphRun.Width now measures (1.6 design-ss per digit at this em = 0.898 ss,
    // where this box is 0.8). ⚠️ THE VERTICAL HALF OF THIS BOX WENT LITERAL on 2026-07-30 and
    // the horizontal half did not, deliberately: the width moves the page's system spacing
    // through LayoutEngine's inter-system seed, and no figured-bass point observes X at all.
    // It closes with the pair that opens X — the same shape as every other box-vs-ink debt.
    // ⚠️ IT GAINED A CONSUMER with the stacking port: BassFigureAlignment.RowOffsets takes a
    // SKYLINE distance between rows, so this width decides which columns of one row see which
    // columns of the next. It is inert for the ledger's texture (the minimum-distance branch
    // wins for digits whatever the overlap) and it is not inert in general — a row whose only
    // tall figure is far to the left of the next row's would step by the minimum here and by
    // the ink in LilyPond, or the reverse.
    internal const double MinFigureBoxWidth = 0.8;
    // The grob's own padding and the distance→drop arithmetic live in SkylineDrop; the padding
    // is passed IN (EngravingDefaults.BassFigurePadding), so the figure row spends its own
    // declaration and not the lyric line's.

    /// <summary>
    /// The up-skyline one figure column offers the placement, about its own baseline.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, because the same box is asked for twice: by the placement
    /// (<see cref="ApplySkylineDrop"/>) and by the reservation
    /// (<see cref="RowInkBelowStaff"/>). ⚠️ AND THERE IS A THIRD SPELLING that this does NOT
    /// yet cover: <c>LayoutEngine</c>'s inter-SYSTEM seed uses <see cref="MinFigureBoxWidth"/>
    /// as a HALF-width where these two use it as a full one. Left alone deliberately —
    /// changing it moves the page's system spacing, which no figured-bass point observes yet.
    /// </remarks>
    internal static VerticalSkyline ColumnUpSkyline(double x, string topFigureText)
        => VerticalSkyline.FromBox(
            x - MinFigureBoxWidth / 2.0, x + MinFigureBoxWidth / 2.0,
            0, FigureInkTop(topFigureText), VerticalDirection.Up);

    /// <summary>
    /// The down-skyline one figure offers the row BELOW it, about its own baseline — the
    /// other edge of the box <see cref="ColumnUpSkyline"/> offers upward.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:451 <c>ly:axis-group-interface::combine-skylines</c>,
    /// which is BassFigureLine's <c>vertical-skylines</c> — a line's profile is its figures'
    /// stencils, both ways up, which is what <see cref="BassFigureAlignment.RowOffsets"/>
    /// stacks against. Zero-deep for a digit and not for an accidental (see
    /// <see cref="FiguredBassGlyphRun.InkBottom"/>).
    /// </remarks>
    internal static VerticalSkyline ColumnDownSkyline(double x, string figureText)
        => VerticalSkyline.FromBox(
            x - MinFigureBoxWidth / 2.0, x + MinFigureBoxWidth / 2.0,
            FiguredBassGlyphRun.InkBottom(figureText), 0, VerticalDirection.Down);

    /// <summary>
    /// The ink a staff's figure row occupies, as a DOWN skyline about that staff's MIDDLE
    /// line — what the staff below has to clear.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:387-411 <c>side-position-interface</c>,
    /// <c>outside-staff-priority</c>, <c>add-stem-support</c> —
    /// <c>BassFigureAlignmentPositioning</c> is an outside-staff grob of its own staff's axis
    /// group, so once it is placed its stencil is part of that group's vertical skyline and
    /// <c>Align_interface</c> spaces the next staff against it.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-950 <c>skyline_spacing</c> — each
    /// priority's grobs are placed against the accumulated profile and then merged INTO it.
    /// <para>
    /// ⚠️ THE PLACEMENT IS RE-RUN HERE rather than read off the layouts, for the same reason
    /// <c>AddDynamicsToSkyline</c> re-runs a dynamic's: this profile is an INPUT to the layout
    /// pass that produces those layouts. The arithmetic is not duplicated — it is
    /// <see cref="SkylineDrop.Compute"/> and <see cref="ColumnUpSkyline"/>, the same two the
    /// placement calls.
    /// </para>
    /// <para>
    /// ⚠️ WITHOUT THIS THE ROW IS PLACED CORRECTLY AND RESERVED NOWHERE. MEASURED (ledger
    /// <c>figbass.upper-staff.staff-gap</c>): LilyPond leaves 12.174795235605316 between two
    /// staves when the row lives between them and Lily# left 9.550000 — the gap it leaves
    /// with nothing there — so giving the drop a staff without this would have moved the
    /// figures out of the system's basement and into the staff below.
    /// </para>
    /// </remarks>
    /// <param name="downSoFar">The staff's accumulated inside-staff DOWN profile — what the
    /// row is placed against. Not mutated.</param>
    internal static VerticalSkyline RowInkBelowStaff(
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MeasureLayout> measureLayouts,
        int staffIndex,
        ImmutableArray<Measure> staffMeasures,
        VerticalSkyline downSoFar)
    {
        var ink = new VerticalSkyline(VerticalDirection.Down);
        if (figuredBasses.IsDefaultOrEmpty) return ink;

        // The row's own basic position, in this staff's frame: Y-up above the middle line.
        double baselineYUp = -AlignedSideFloorBelowStaff;

        var columns = new List<BassFigureAlignment.Column>();
        var rowUp = new VerticalSkyline(VerticalDirection.Up);
        foreach (var fb in figuredBasses)
        {
            if (fb.StaffIndex != staffIndex) continue;
            int layoutIdx = -1;
            for (int i = 0; i < measureLayouts.Length; i++)
                if (measureLayouts[i].MeasureIndex == fb.MeasureIndex) { layoutIdx = i; break; }
            if (layoutIdx < 0) continue;
            var measureLayout = measureLayouts[layoutIdx];
            // The SAME X the engraver computes (Calculate), so seed and draw agree.
            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                staffMeasures, fb.MeasureIndex, fb.ItemIndex, measureLayout);
            var texts = fb.Figures.Select(f => f.DisplayText).ToImmutableArray();
            string topText = texts.Length > 0 ? texts[0] : string.Empty;
            columns.Add(new BassFigureAlignment.Column(x, texts));
            rowUp.Merge(ColumnUpSkyline(x, topText));
        }
        if (columns.Count == 0) return ink;

        // The rows of THIS staff's alignment, stacked once for all of its columns — the same
        // house Calculate hands to the drawing, asked again here because this profile is an
        // INPUT to the pass that produces those layouts (see the remark above).
        // ⚠️ THE TWO CALLS SEE THE SAME COLUMNS, checked rather than assumed: `measureLayouts`
        // is ONE system's (LayoutEngine builds it per system from LayoutMeasures and hands it
        // to MultiStaffLayouter.BuildStaffSkylines with that system's index), so the
        // membership test above is a per-system filter and matches StackRows' (system, staff)
        // grouping. If it ever became the whole score's, this would stack a different
        // alignment from the one that is drawn, and no digit texture could tell.
        var rowOffsets = BassFigureAlignment.RowOffsets(columns);

        // The drop, by the same two steps the placement takes. The frame here is the staff's
        // MIDDLE line, so the basic floor is that Y-up read downward.
        var drops = SkylineDrop.Compute(
            new Dictionary<int, VerticalSkyline> { [staffIndex] = rowUp },
            _ => -baselineYUp,
            _ => downSoFar,
            EngravingDefaults.BassFigurePadding, SkylineDrop.HorizonPadding);
        double placedYUp = baselineYUp - (drops.TryGetValue(staffIndex, out var d) ? d : 0);

        foreach (var col in columns)
        {
            string topText = col.Texts.Length > 0 ? col.Texts[0] : string.Empty;
            ink.Merge(VerticalSkyline.FromBox(
                col.X - MinFigureBoxWidth / 2.0, col.X + MinFigureBoxWidth / 2.0,
                placedYUp - BassFigureAlignment.ColumnDepth(rowOffsets, col.Texts),
                placedYUp + FigureInkTop(topText),
                VerticalDirection.Down));
        }
        return ink;
    }

    public static ImmutableArray<FiguredBassLayout> Calculate(
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null,
        Func<int, int, VerticalSkyline?>? staffDownSkyline = null)
    {
        if (figuredBasses.IsDefaultOrEmpty)
            return ImmutableArray<FiguredBassLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<FiguredBassLayout>(figuredBasses.Length);

        for (int fbi = 0; fbi < figuredBasses.Length; fbi++)
        {
            var fb = figuredBasses[fbi];
            if (fb.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[fb.MeasureIndex];

            if (measureLayout.Columns.IsDefaultOrEmpty
                && fb.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this figure's OWN staff (multi-staff) measures for the item X.
            // The staff offset is no longer baked: the Y is stored relative to the
            // staff middle (frame B) and resolved to the right staff at each consumer.
            var fbMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, fb.StaffIndex, measures);

            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                fbMeasures, fb.MeasureIndex, fb.ItemIndex, measureLayout);
            // Y-up (frame B): the topmost figure starts on aligned_side's floor below the
            // staff middle, and ApplySkylineDrop then lowers it until it clears its own
            // staff's ink (the support half of the same aligned_side).
            double yUp = -AlignedSideFloorBelowStaff;

            var figureTexts = fb.Figures
                .Select(f => f.DisplayText)
                .ToImmutableArray();

            layouts.Add(new FiguredBassLayout(
                fb.MeasureIndex,
                x,
                yUp,
                figureTexts,
                fb.SourcePosition,
                fbi,
                StaffIndex: fb.StaffIndex));
        }

        var result = StackRows(layouts.ToImmutable(), systems);
        if (systemSkylines != null && !systems.IsDefaultOrEmpty)
            result = ApplySkylineDrop(result, systems, systemSkylines, staffDownSkyline);
        return result;
    }

    /// <summary>
    /// Stacks each alignment's rows once and writes the result onto every column of it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:370 <c>ly:align-interface::align-to-minimum-distances</c>,
    /// BassFigureAlignment's <c>positioning-done</c> — it positions its lines ONCE and every figure on a
    /// line then rides that translation, which is why the offsets are stored on the layouts
    /// rather than recomputed by each consumer (the drawing, the per-measure extent, the
    /// inter-system silhouette).
    /// <para>
    /// ⚠️ ONE ALIGNMENT IS ONE STAFF OF ONE SYSTEM. The grob is a Spanner, so line-breaking
    /// gives each system its own broken piece with its own <c>positioning-done</c>, and
    /// <c>BassFigureAlignmentPositioning</c> is per staff by construction. Grouping any wider
    /// would let a figure in one system decide a step in another.
    /// </para>
    /// </remarks>
    private static ImmutableArray<FiguredBassLayout> StackRows(
        ImmutableArray<FiguredBassLayout> layouts, ImmutableArray<SystemLayout> systems)
    {
        if (layouts.IsDefaultOrEmpty) return layouts;

        var measureToSystem = systems.IsDefaultOrEmpty
            ? null
            : SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var columnsByAlignment = new Dictionary<(int Sys, int Staff), List<BassFigureAlignment.Column>>();
        foreach (var lay in layouts)
        {
            var key = (SystemOf(lay), lay.StaffIndex);
            if (!columnsByAlignment.TryGetValue(key, out var cols))
                columnsByAlignment[key] = cols = new List<BassFigureAlignment.Column>();
            cols.Add(new BassFigureAlignment.Column(lay.X, lay.FigureTexts));
        }

        var offsetsByAlignment = new Dictionary<(int Sys, int Staff), ImmutableArray<double>>();
        foreach (var (key, cols) in columnsByAlignment)
            offsetsByAlignment[key] = BassFigureAlignment.RowOffsets(cols);

        return layouts
            .Select(lay => lay with { RowOffsets = offsetsByAlignment[(SystemOf(lay), lay.StaffIndex)] })
            .ToImmutableArray();

        int SystemOf(FiguredBassLayout lay)
            => measureToSystem != null && measureToSystem.TryGetValue(lay.MeasureIndex, out int s)
                ? s : 0;
    }

    /// <summary>
    /// Lowers each staff's figure row until it clears THAT STAFF's down-skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-position-engraver.cc:90-100 <c>acknowledge_note_column</c>,
    /// <c>acknowledge_stem</c> — the supports <c>BassFigureAlignmentPositioning</c> is
    /// side-positioned against are the ones its OWN engraver acknowledged, i.e. its own
    /// staff's; the loose-line device reaches the same staff from the other end
    /// (ly/engraver-init.ly:1108-1123 <c>\consists Figured_bass_engraver</c>,
    /// <c>VerticalAxisGroup.staff-affinity</c>, <c>nonstaff-relatedstaff-spacing</c> — affinity
    /// UP hangs the line from the staff above it).
    /// <para>
    /// ⚠️ THE KEY IS (system, staff), AND UNTIL 2026-07-30 IT WAS THE SYSTEM. Every figure of
    /// a system was merged into one skyline, measured against the SYSTEM's down-skyline and
    /// lowered by one number, so a row on a non-bottom staff was thrown below all of the
    /// system's ink — the same shape as the lower-staff fermata that flew over the top staff
    /// before the above pass went per-(system, staff). MEASURED, one score in three
    /// arrangements: LilyPond reads 8.124795235605315 whichever staff owns the figures, and
    /// Lily# read 8.500000 / 18.050000 / 8.500000 (ledger <c>figbass.{alone,upper-staff,
    /// lower-staff}.staff-to-baseline</c>). No committed fixture had the middle arrangement.
    /// </para>
    /// </remarks>
    private static ImmutableArray<FiguredBassLayout> ApplySkylineDrop(
        ImmutableArray<FiguredBassLayout> layouts, ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)> systemSkylines,
        Func<int, int, VerticalSkyline?>? staffDownSkyline)
    {
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        var fbUp = new Dictionary<(int Sys, int Staff), VerticalSkyline>();
        var basicY = new Dictionary<(int Sys, int Staff), double>();
        foreach (var lay in layouts)
        {
            if (!measureToSystem.TryGetValue(lay.MeasureIndex, out int s)) continue;
            var key = (s, lay.StaffIndex);
            var box = ColumnUpSkyline(
                lay.X, lay.FigureTexts.Length > 0 ? lay.FigureTexts[0] : string.Empty);
            if (fbUp.TryGetValue(key, out var sky)) sky.Merge(box);
            else fbUp[key] = box;
            // basicY is the system-relative device floor (the old lay.Y). Reconstruct
            // it from Y-up against this figure's own staff offset.
            double staffOffset = LayoutUtilities.StaffOffsetInSystemDown(systems[s], lay.StaffIndex);
            double layY = staffOffset + (2.0 - lay.YUp);
            basicY[key] = basicY.TryGetValue(key, out var b) ? System.Math.Min(b, layY) : layY;
        }

        // Figured bass uses each staff's own lowest figure as the basic-distance floor.
        // ⚠️ The floor is INERT and is computed anyway, because LilyPond computes its own:
        // aligned_side takes the larger of the support placement and the staff-padding floor
        // (lily/side-position-interface.cc:401-453), and MEASURED (ledger
        // figbass.quiet.staff-to-baseline) the support placement wins in the quietest regime a
        // five-line staff has — the staff's own ink 2.05 + padding 0.5 + the top digit's cap
        // already exceeds staff ink + staff-padding 1.0, for any figure whose cap beats
        // staff-padding − padding = 0.5.
        // LILYPOND-REF: lily/side-position-interface.cc:370 aligned_side —
        // total_off += dir * ss * padding, the grob's OWN declared padding
        // (scm/define-grobs.scm:393 padding of BassFigureAlignmentPositioning). It is passed
        // in rather than taken from SkylineDrop's lyric constant: the two devices declare the
        // same 0.5 today and that agreement is not an invariant (§5.2.1②).
        var drop = SkylineDrop.Compute(fbUp, k => basicY[k], k => StaffDown(k.Sys, k.Staff),
            EngravingDefaults.BassFigurePadding, SkylineDrop.HorizonPadding);

        if (drop.Count == 0)
            return layouts;

        // The drop d is a downward (device) shift; in Y-up that is a decrease.
        return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(layouts, lay =>
            measureToSystem.TryGetValue(lay.MeasureIndex, out int s)
            && drop.TryGetValue((s, lay.StaffIndex), out var d) && d > 0
                ? lay with { YUp = lay.YUp - d }
                : lay)).ToImmutableArray();

        // The profile this staff's row has to clear, in the system's Y-up frame. The caller
        // supplies it per (system, staff); the system silhouette is the degenerate support for
        // a harness that builds no staff, where it is the same profile because there is only
        // one staff to be the silhouette of.
        VerticalSkyline? StaffDown(int sys, int staff)
        {
            if (staffDownSkyline?.Invoke(sys, staff) is { } own)
                return own;
            return sys >= 0 && sys < systemSkylines.Count ? systemSkylines[sys].down : null;
        }
    }
}
