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
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (hairpins.IsDefaultOrEmpty)
            return ImmutableArray<HairpinLayout>.Empty;

        // LILYPOND-REF: lily/system.cc:143-192 — fixup_refpoints walks all systems once.
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = ImmutableArray.CreateBuilder<HairpinLayout>();
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);

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
            int staffIdx = hairpin.StaffIndex;

            // LILYPOND-REF: lily/spanner.cc:36-144 — broken once per system; bounds
            // reattached to the system edges. LilyPond breaks the DynamicLineSpanner with
            // it and side-positions EACH piece against the supports that fall inside it
            // (break-substitution.cc:67-153 substitute_grob / do_break_substitution
            // rewrites the support list per piece), so the level is resolved here and not
            // once for the whole span.
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
                    WedgeSkylines(segStartX, segEndX, startOpening, endOpening, 0.0));
                // spannerY is Y-up about the staff middle; the layout frame is Y-up from
                // the SYSTEM top, and staffOffset is a within-system downward offset.
                double hairpinYUp = spannerY - StaffMiddleBelowSystemTop - staffOffset;

                layouts.Add(new HairpinLayout(
                    segment.StartMeasureIndex, segStartX, segEndX, hairpinYUp,
                    startOpening, endOpening, hairpin.Direction, hairpin.SourcePosition,
                    hairpin.SourceIndex, hairpin.StaffIndex));
            }
        }

        return layouts.ToImmutable();
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
    internal static (VerticalSkyline Up, VerticalSkyline Down) WedgeSkylines(
        double startX, double endX, double startOpening, double endOpening, double centreYUp)
    {
        double half = EngravingDefaults.StaffLineThickness / 2.0;
        return (VerticalSkyline.FromSlope(
                    startX, centreYUp + startOpening + half,
                    endX, centreYUp + endOpening + half,
                    thickness: 0, VerticalDirection.Up),
                VerticalSkyline.FromSlope(
                    startX, centreYUp - startOpening - half,
                    endX, centreYUp - endOpening - half,
                    thickness: 0, VerticalDirection.Down));
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
