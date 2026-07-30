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
    int StaffIndex = 0
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
/// - minimum-length: 2.0
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

    /// <summary>
    /// Minimum hairpin length.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1786 (minimum-length . 2.0)</remarks>
    private const double MinimumLength = 2.0;

    /// <summary>
    /// ⚠️ LILYSHARP-OWN, AND MEASURED WRONG BY 0.166600: the hairpin's resting level below
    /// the staff, as a constant, where LilyPond computes it — and where Lily# ALREADY
    /// computes it for the other grob on the same spanner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sentence this remark used to carry is FALSIFIED, and both of its halves were
    /// wrong. It read "staff-padding 0.2 + padding 0.6 + ascent" and cited
    /// DynamicLineSpanner's staff-padding as 0.2; LilyPond's is 0.1
    /// (scm/define-grobs.scm:1411), and staff-padding is not a SUMMAND at all — the
    /// :433-453 block of aligned_side is a FLOOR on the grob's refpoint, reached only when
    /// the supports put it nearer than that (the same trap DynamicEngraver.BaselineY's
    /// remark records for the text). MEASURED IN LILYPOND 2.26.0, dumping the
    /// DynamicLineSpanner's own offset on this exact texture (a quiet d with a hairpin
    /// under every bar): its refpoint sits 3.366600 below the staff refpoint with an ink
    /// extent of ±0.7166, so the ink bottom is 4.083200 — which is the number
    /// audit/lp-geometry hairpin.page.quiet.last-staff-to-foot reads through the page's
    /// foot. PERTURBED to find each owner, rather than fitted:
    /// <list type="bullet">
    /// <item>padding 0.6 → 1.6 moves it by exactly 1.0 ⇒ the 0.6 is side-position's
    /// padding, spent after the skyline distance.</item>
    /// <item>staff-padding 0.1 → 1.1 does not move it at all, and → 2.1 lands it on
    /// 4.150000 = 2.05 + 2.1 ⇒ a dominated floor, proved from both sides.</item>
    /// <item>outside-staff-padding 0.46 → 1.46 moves it ⇒ that pass is live but dominated
    /// at the default (2.05 + 0.46 + 0.7166 = 3.2266 &lt; 3.3666).</item>
    /// <item>a note below the staff drags it down ⇒ the support is the notes UNION the
    /// staff's own extent, which is aligned_side's include_staff minimum.</item>
    /// </list>
    /// ⇒ 3.366600 = staff ink 2.05 + padding 0.6 + the wedge's own half height 0.7166
    /// (HairpinEngraver.Height 0.6666 + half the rule's thickness). ⚠️ The ledger's earlier
    /// arithmetic fit 2.05 + 0.1 + 0.6 + 0.6666 reached the right INK BOTTOM by two
    /// compensating errors of 0.05 — HANDOFF §5.0's "a fit is not a decomposition", caught
    /// here by the dump.
    /// </para>
    /// <para>
    /// THE PORT IS NAMED AND IT IS NOT A NEW NUMBER: DynamicEngraver.BaselineY is already
    /// the full aligned_side transcription, and LilyPond hangs BOTH grobs off ONE
    /// DynamicLineSpanner (scm/define-grobs.scm:1428-1431 says so in its own description).
    /// Calling it with the wedge's own dim — and without the text's TextOffsetInSpanner,
    /// which is DynamicText's Y-offset inside the spanner and not the spanner's — returns
    /// −3.366600 by construction. PREDICTION, written before the move: this constant goes,
    /// the level becomes −5.366600 in this frame, and
    /// hairpin.page.quiet.last-staff-to-foot lands at 0 from −0.166600181.
    /// </para>
    /// </remarks>
    private const double BaseYUp = -5.2; // Y-up: 5.2 below the system top

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
        Func<int, int, double>? staffYAt = null)
    {
        if (hairpins.IsDefaultOrEmpty)
            return ImmutableArray<HairpinLayout>.Empty;

        // LILYPOND-REF: lily/system.cc:143-192 — fixup_refpoints walks all systems once.
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = ImmutableArray.CreateBuilder<HairpinLayout>();

        // Height IS the wedge's half-opening (LP's `height` property): the two
        // arms sit at ±fullOpening, so the open end's full mouth is 2·Height,
        // matching LilyPond. (A stray /2 here made every hairpin half-height,
        // so the flat wedge read as "not closing" at a broken continuation.)
        // LILYPOND-REF: lily/hairpin.cc — Line(x, ±starth) … (width, ±endh).
        double fullOpening = Height;
        // LILYPOND-REF: lily/hairpin.cc:300-313 — broken hairpin height fractions
        // (crescendo: first piece 0→2·height/3, continuation height/3→height).
        double continuedOpening = fullOpening * ContinuedFraction;
        double continuingOpening = fullOpening * ContinuingFraction;

        foreach (var hairpin in hairpins)
        {
            if (hairpin.StartMeasureIndex >= measureLayouts.Length ||
                hairpin.EndMeasureIndex >= measureLayouts.Length)
                continue;

            // The wedge hangs a fixed distance below ITS staff; add the staff's
            // within-system offset so a hairpin on staff 2 sits under staff 2, not
            // staff 1. Staff 0 (or a single staff) has offset 0 -> unchanged. The
            // per-staff stacker then keeps it clear of that staff's dynamics only.
            double staffOffset = staffYAt?.Invoke(hairpin.StartMeasureIndex, hairpin.StaffIndex) ?? 0;
            // Y-up from the system top; staffOffset is a within-system downward offset,
            // so it SUBTRACTS.
            double hairpinYUp = BaseYUp - staffOffset;

            // LILYPOND-REF: lily/spanner.cc:36-144 — broken once per system; bounds
            // reattached to the system edges.
            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                hairpin.StartMeasureIndex, hairpin.EndMeasureIndex, systems, measureToSystemIdx))
            {
                var (segStartX, segEndX) = SpannerBreakSubstitution.ReattachSpanX(
                    segment, system,
                    CalculateStartX(hairpin, measureLayouts),
                    CalculateEndX(hairpin, measureLayouts));

                if (segEndX - segStartX < MinimumLength)
                    segEndX = segStartX + MinimumLength;

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

                layouts.Add(new HairpinLayout(
                    segment.StartMeasureIndex, segStartX, segEndX, hairpinYUp,
                    startOpening, endOpening, hairpin.Direction, hairpin.SourcePosition,
                    hairpin.SourceIndex, hairpin.StaffIndex));
            }
        }

        return layouts.ToImmutable();
    }

    private static double CalculateStartX(HairpinItem hairpin, ImmutableArray<MeasureLayout> measureLayouts)
    {
        var startMeasure = measureLayouts[hairpin.StartMeasureIndex];
        if (hairpin.StartItemIndex < startMeasure.Items.Length)
        {
            var startItem = startMeasure.Items[hairpin.StartItemIndex];
            return startMeasure.X + startItem.X + startItem.Width + BoundPadding * 0.5;
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
    public static ImmutableArray<HairpinItem> DetectHairpins(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<DynamicItem> dynamics)
    {
        var hairpins = ImmutableArray.CreateBuilder<HairpinItem>();

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

            // A hairpin ends at a dynamic / next hairpin ON THE SAME STAFF. Without
            // the staff filter a cresc on staff 2 terminated against staff 1's cresc
            // in the same measure, collapsing both spans to nothing (they share the
            // single score.MusicMarks / Dynamics tables).
            var nextDynamic = sortedDynamics
                .FirstOrDefault(d =>
                    d.StaffIndex == mark.StaffIndex &&
                    (d.MeasureIndex > mark.MeasureIndex ||
                     (d.MeasureIndex == mark.MeasureIndex && d.ItemIndex > 0)));

            // Find the next cresc/decresc mark on this staff (another hairpin starts
            // there). A same-measure mark only counts if it is at a LATER item, so a
            // second cresc on the same beat can't be mistaken for this one's end.
            var nextMark = crescMarks
                .Select(x => x.Mark)
                .FirstOrDefault(m =>
                    m != mark && m.StaffIndex == mark.StaffIndex &&
                    (m.MeasureIndex > mark.MeasureIndex ||
                     (m.MeasureIndex == mark.MeasureIndex &&
                      m.AnchorItemIndex > mark.AnchorItemIndex)));

            // End at whichever comes first: next dynamic or next cresc/decresc
            int endMeasure;
            int endItem;

            if (nextDynamic != null && (nextMark == null ||
                nextDynamic.MeasureIndex < nextMark.MeasureIndex ||
                (nextDynamic.MeasureIndex == nextMark.MeasureIndex && nextDynamic.ItemIndex <= 0)))
            {
                endMeasure = nextDynamic.MeasureIndex;
                endItem = nextDynamic.ItemIndex;
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

            // Only add if there's actually a span
            if (endMeasure > mark.MeasureIndex ||
                (endMeasure == mark.MeasureIndex && endItem > 0))
            {
                hairpins.Add(new HairpinItem(
                    Direction: direction,
                    StartMeasureIndex: mark.MeasureIndex,
                    StartItemIndex: 0,
                    EndMeasureIndex: endMeasure,
                    EndItemIndex: endItem,
                    SourcePosition: mark.SourcePosition,
                    SourceIndex: srcIndex,
                    StaffIndex: mark.StaffIndex
                ));
            }
        }

        return hairpins.ToImmutable();
    }
}
