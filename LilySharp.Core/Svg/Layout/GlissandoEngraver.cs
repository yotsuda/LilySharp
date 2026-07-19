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
/// Layout for a single glissando line.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct GlissandoLayout(
    double StartX,
    // Y of the line's start/end in the LilyPond-native Y-up frame: staff-spaces
    // ABOVE this glissando's staff middle line, up-positive (frame B). The renderer
    // reflects each back to device via StaffFrame.ToDevice against the staff middle
    // it resolves from StaffIndex/MeasureIndex.
    double StartYUp,
    double EndX,
    double EndYUp,
    GlissandoStyle Style,
    int SourcePosition,
    // F3/B: locator of the START note this glissando hangs on, so a reused (cached)
    // layout re-derives its data-pos from the live score (SharedRenderer.ResolveDataPos).
    // -1 = unresolved (direct unit-test construction / no note resolution).
    int StaffIndex = -1,
    int MeasureIndex = -1,
    int ItemIndex = -1,
    // Voice the start note lives in (0 = primary), so the locator resolves against
    // the RIGHT voice on a polyphonic staff — a second voice's glissando must not
    // re-derive its data-pos from the primary voice's note at the same index.
    int VoiceIndex = 0);

/// <summary>
/// Calculates glissando layouts from detected glissando items.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm, scm/define-grobs.scm:1692-1719
/// Parameters: style=line, gap=0.5, padding=0.5, zigzag-width=0.75
/// </remarks>
internal static class GlissandoEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:1705 (gap . 0.5)
    private const double Gap = 0.5;

    // LILYPOND-REF: scm/define-grobs.scm:1697,1700 (padding . 0.5)
    private const double Padding = 0.5;

    /// <summary>
    /// Calculates layout positions for all glissando items, splitting cross-system
    /// glissandos into broken pieces (one per system).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
    /// LILYPOND-REF: scm/scheme-engravers.scm — Glissando_engraver
    /// </remarks>
    public static ImmutableArray<GlissandoLayout> Calculate(
        ImmutableArray<GlissandoItem> glissandos,
        ImmutableArray<SystemLayout> systems,
        int staffIndex = -1,
        ImmutableArray<Measure> measures = default)
    {
        if (glissandos.IsDefaultOrEmpty || glissandos.Length == 0)
            return ImmutableArray<GlissandoLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = new List<GlissandoLayout>();

        foreach (var gliss in glissandos)
        {
            if (!measureMap.TryGetValue(gliss.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(gliss.EndMeasureIndex, out var endInfo))
                continue;

            var (_, startMeasure) = startInfo;
            var (_, endMeasure) = endInfo;

            // Real start/end X derived from note attachment.
            // LILYPOND-REF: scm/define-grobs.scm:1699 left attach-dir = RIGHT
            // LILYPOND-REF: scm/define-grobs.scm:1695 right attach-dir = LEFT
            double realStartX = startMeasure.X + LayoutUtilities.GetItemXOffset(
                measures, gliss.StartMeasureIndex, gliss.StartItemIndex, startMeasure);
            double realEndX = endMeasure.X + LayoutUtilities.GetItemXOffset(
                measures, gliss.EndMeasureIndex, gliss.EndItemIndex, endMeasure);
            realStartX += Padding;
            realEndX -= Padding;

            foreach (var (segment, system) in SpannerBreakSubstitution.BrokenPieces(
                gliss.StartMeasureIndex, gliss.EndMeasureIndex, systems, measureToSystemIdx))
            {
                // Native Y-up (staff-spaces above this glissando's staff middle):
                // a staff position p sits p/2 spaces above the middle line. The former
                // ResolveStaffMiddleY round-trip (PositionToDevice, then ToUp at store)
                // cancelled exactly, so no staff-middle resolution is needed here.
                double startStaffY = gliss.StartStaffPosition * 0.5;
                double endStaffY = gliss.EndStaffPosition * 0.5;

                // Resolve segment-local X bounds against the system's measure layouts.
                // LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges.
                var (segStartX, segEndX) = SpannerBreakSubstitution.ReattachSpanX(
                    segment, system, realStartX, realEndX);

                // Y at the broken edge "freezes" at the destination pitch — visual cue
                // that the slide continues on the adjacent system.
                double segStartY = segment.IsFirst ? startStaffY : endStaffY;
                double segEndY = segment.IsLast ? endStaffY : startStaffY;
                if (segment.IsMiddle)
                {
                    double mid = (startStaffY + endStaffY) / 2.0;
                    segStartY = mid;
                    segEndY = mid;
                }

                // Apply gap: shorten along the line direction.
                // LILYPOND-REF: lily/line-spanner.cc:599 span_points[d] += -d * gaps[d] * magstep * dz.direction()
                // LILYPOND-REF: scm/define-grobs.scm:1705 (gap . 0.5)
                // Gap is applied only at real-note bounds (not at system-edge cuts).
                double dx = segEndX - segStartX;
                double dy = segEndY - segStartY;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > Gap * 2)
                {
                    double gapRatio = Gap / length;
                    if (segment.IsFirst)
                    {
                        segStartX += dx * gapRatio;
                        segStartY += dy * gapRatio;
                    }
                    if (segment.IsLast)
                    {
                        segEndX -= dx * gapRatio;
                        segEndY -= dy * gapRatio;
                    }
                }

                // The segment/gap geometry above ran directly in the native Y-up frame
                // (line math is frame-invariant — dy just carries its Y-up sign), so
                // the Y-up store is a direct assignment; no reflection.
                layouts.Add(new GlissandoLayout(
                    StartX: segStartX,
                    StartYUp: segStartY,
                    EndX: segEndX,
                    EndYUp: segEndY,
                    Style: gliss.Style,
                    SourcePosition: gliss.SourcePosition,
                    StaffIndex: staffIndex,
                    // MeasureIndex is the START-note data-pos locator, shared by ALL
                    // broken segments. The draw resolves the staff middle from it, so a
                    // glissando broken across a SYSTEM would resolve later segments
                    // against the first system's staff middle. No such case is in
                    // coverage today; a proper fix needs a separate per-segment system
                    // reference (MeasureIndex can't move — it drives click-to-source).
                    MeasureIndex: gliss.StartMeasureIndex,
                    ItemIndex: gliss.StartItemIndex,
                    VoiceIndex: gliss.VoiceIndex));
            }
        }

        return layouts.ToImmutableArray();
    }
}
