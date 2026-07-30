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
/// Layout information for a trill spanner ("tr" glyph + wavy line extension).
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:4048-4094 TrillSpanner grob
/// </remarks>
public readonly record struct TrillSpannerLayout(
    // Start measure index (for system Y lookup).
    int StartMeasureIndex,
    // X position of the "tr" glyph.
    double GlyphX,
    // X position where the wavy line starts (after "tr" glyph).
    double LineStartX,
    // X position where the wavy line ends.
    double LineEndX,
    // Y in the LilyPond-native Y-up frame: staff-spaces ABOVE the system top,
    // up-positive (frame B). The renderer reflects it to device against the
    // segment's system top (sy + old-Y == sy − YUp).
    double YUp,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // Global staff index this spanner belongs to (multi-staff). The
    // above-staff stacker only de-collides staff 0; lower staves keep their
    // engraver Y so they stay over their own staff.
    int StaffIndex = 0,
    int SourceIndex = -1   // F3/B: index into score.TrillSpanners (shared by all broken pieces)
);

/// <summary>
/// Calculates positions for trill spanners (tr symbol + wavy line extension).
/// Handles cross-system spanners by extending to system edge.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver class
/// LILYPOND-REF: scm/define-grobs.scm:4048-4094 TrillSpanner grob defaults
///
/// TrillSpanner parameters from LilyPond:
/// - direction: UP (always above staff)
/// - style: trill (wavy line)
/// - padding: 0.5
/// - staff-padding: 1.0
/// - outside-staff-priority: 50
/// </remarks>
internal static class TrillSpannerEngraver
{
    /// <summary>
    /// Horizontal padding from bound objects.
    /// </summary>
    /// <remarks>LILYSHARP-OWN: an X-axis bound gap. LilyPond's <c>(padding . 0.5)</c>
    /// at define-grobs.scm:4079 is the VERTICAL side-position padding (now
    /// <c>EngravingDefaults.TrillSpannerPadding</c>); its bound-details declare no
    /// horizontal padding, so this stays a Lily# device with the same value.</remarks>
    private const double BoundPadding = 0.5;

    /// <summary>
    /// Width of the "tr" glyph in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: feta-scripts.mf scripts.trill glyph metrics
    /// Measured from Emmentaler font: ~1.6 staff spaces
    /// </remarks>
    private const double TrillGlyphWidth = 1.6;

    /// <summary>
    /// Gap between "tr" glyph and wavy line start.
    /// </summary>
    private const double GlyphLinePadding = 0.3;

    /// <summary>
    /// Calculates layout for all trill spanners.
    /// Handles cross-system spanners by extending the wavy line to the system edge.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:1798-1868 positioning
    /// LILYPOND-REF: lily/line-spanner.cc:526-648 cross-system spanner handling
    /// </remarks>
    public static ImmutableArray<TrillSpannerLayout> Calculate(
        ImmutableArray<TrillSpannerItem> trillSpanners,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, int, double>? staffYAt = null,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (trillSpanners.IsDefaultOrEmpty)
            return ImmutableArray<TrillSpannerLayout>.Empty;

        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        // Beam membership per (staff, voice, measure, item) — a beamed support
        // column's stem ends at the quanted beam face, not the unbeamed formula
        // (ledger trill.beam-face.staff-to-line). The same map the dynamics' support
        // uses, so the two consumers cannot disagree about who is beamed.
        // LILYPOND-REF: lily/stem.cc:490-497 internal_calc_stem_end_position — a
        //   beamed stem's end fires `quantized-positions` and reads the beam, so the
        //   support extent the trill clears IS the quanted face.
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);
        var layouts = ImmutableArray.CreateBuilder<TrillSpannerLayout>();

        for (int ti = 0; ti < trillSpanners.Length; ti++)
        {
            var spanner = trillSpanners[ti];
            if (spanner.StartMeasureIndex >= measureLayouts.Length)
                continue;

            // Y-up from the system top (staff top = 0 in this frame): the trill's
            // resting height, transcribed from aligned_side. The SUPPORT is the
            // spanned note columns' UP edges (DynamicEngraver.ColumnUpEdge — the
            // DRAWN stem end: shortened / beam-quanted face, ledger
            // trill.{shortened-stem,beam-face}.staff-to-line), floored by the
            // STAFF EXTENT (declaring staff-padding turns include_staff on and the
            // staff's ink edge enters the support as a minimum); the grob pays its own
            // padding 0.5 over that with its facing edge — the "tr" glyph's
            // stencil-offset reach 1.0 below the line. The :433-453 refpoint floor
            // (ink + staff-padding) is written too, though the padding term subsumes
            // it for the trill's reach (ledger trill.quiet.staff-to-line = 2.05 + 0.5
            // + 1.0 = 3.550000, the staff-extent case; trill.support.staff-to-line =
            // column box top + 0.5 + 1.0, the column case. The old resting height
            // StaffPadding + TrillGlyphHeight = 2.2 was an invention, +0.65 high —
            // and it left a lower-staff trill with NO column support at all, so a
            // stem ran through the lowered glyph).
            // LILYPOND-REF: lily/side-position-interface.cc:219-222 include_staff,
            //   :323-330 set_minimum_height, :361-370 padding, :433-453 staff_padding
            double staffOffset = staffYAt?.Invoke(spanner.StartMeasureIndex, spanner.StaffIndex) ?? 0;
            double staffInkUp = EngravingDefaults.StaffLineThickness / 2.0;
            double support = staffInkUp;
            if (voicesByStaff != null
                && voicesByStaff.TryGetValue(spanner.StaffIndex, out var trillVoices)
                && !trillVoices.IsDefaultOrEmpty)
            {
                for (int mi = spanner.StartMeasureIndex; mi <= spanner.EndMeasureIndex; mi++)
                {
                    int count = trillVoices.Max(
                        v => mi < v.Measures.Length ? v.Measures[mi].Items.Length : 0);
                    int first = mi == spanner.StartMeasureIndex ? spanner.StartItemIndex : 0;
                    int last = mi == spanner.EndMeasureIndex
                        ? Math.Min(spanner.EndItemIndex, count - 1)
                        : count - 1;
                    for (int ii = first; ii <= last; ii++)
                    {
                        // ColumnUpEdge answers in the staff-middle frame; this frame's
                        // origin is the staff TOP line, 2 above it.
                        int cmi = mi, cii = ii;
                        support = Math.Max(support,
                            DynamicEngraver.ColumnUpEdge(trillVoices, mi, ii,
                                vi => beamMembers.TryGetValue(
                                    (spanner.StaffIndex, vi, cmi, cii), out var b)
                                    ? b : null) - 2.0);
                    }
                }
            }
            double supported = support + EngravingDefaults.TrillSpannerPadding
                + EngravingDefaults.TrillSpannerTextOffsetDown;
            double floored = staffInkUp + EngravingDefaults.TrillSpannerStaffPadding;
            double yUp = Math.Max(supported, floored) - staffOffset;

            var startMeasure = measureLayouts[spanner.StartMeasureIndex];
            if (spanner.StartItemIndex >= startMeasure.Items.Length)
                continue;

            var startItem = startMeasure.Items[spanner.StartItemIndex];
            double startX = startMeasure.X + startItem.X;

            // LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                spanner.StartMeasureIndex, spanner.EndMeasureIndex, systems, measureToSystem))
            {
                // First segment carries the "tr" glyph; continuation segments draw line only.
                double glyphX, lineStartX;
                if (segment.IsFirst)
                {
                    glyphX = startX;
                    lineStartX = startX + TrillGlyphWidth + GlyphLinePadding;
                }
                else
                {
                    // No glyph on continuation — set GlyphX == LineStartX so the renderer suppresses it.
                    glyphX = system.PrefixWidth + BoundPadding;
                    lineStartX = glyphX;
                }

                double endX;
                if (segment.IsLast)
                {
                    endX = GetEndX(spanner, measureLayouts);
                }
                else
                {
                    endX = system.Width - BoundPadding;
                }

                if (endX <= glyphX)
                    continue;

                layouts.Add(new TrillSpannerLayout(
                    segment.StartMeasureIndex, glyphX, lineStartX, endX, yUp,
                    spanner.SourcePosition, spanner.StaffIndex, ti));
            }
        }

        return layouts.ToImmutable();
    }

    private static double GetEndX(TrillSpannerItem spanner, ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (spanner.EndMeasureIndex < measureLayouts.Length)
        {
            var endMeasure = measureLayouts[spanner.EndMeasureIndex];
            if (spanner.EndItemIndex < endMeasure.Items.Length)
            {
                var endItem = endMeasure.Items[spanner.EndItemIndex];
                return endMeasure.X + endItem.X - BoundPadding;
            }
            return endMeasure.X + endMeasure.Width - BoundPadding;
        }
        var lastMeasure = measureLayouts[^1];
        return lastMeasure.X + lastMeasure.Width - BoundPadding;
    }
}
