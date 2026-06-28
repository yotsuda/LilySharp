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
    double StartY,
    double EndX,
    double EndY,
    GlissandoStyle Style,
    int SourcePosition);

/// <summary>
/// Calculates glissando layouts from detected glissando items.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm, scm/define-grobs.scm:1557-1577
/// Parameters: style=line, gap=0.5, padding=0.5, zigzag-width=0.75
/// </remarks>
public static class GlissandoEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:1570 (gap . 0.5)
    private const double Gap = 0.5;

    // LILYPOND-REF: scm/define-grobs.scm:1562,1565 (padding . 0.5)
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
        double staffHeight,
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
            // LILYPOND-REF: scm/define-grobs.scm:1560 left attach-dir = RIGHT
            // LILYPOND-REF: scm/define-grobs.scm:1563 right attach-dir = LEFT
            double realStartX = startMeasure.X + LayoutUtilities.GetItemXOffset(
                measures, gliss.StartMeasureIndex, gliss.StartItemIndex, startMeasure);
            double realEndX = endMeasure.X + LayoutUtilities.GetItemXOffset(
                measures, gliss.EndMeasureIndex, gliss.EndItemIndex, endMeasure);
            realStartX += Padding;
            realEndX -= Padding;

            var segments = SpannerBreakSubstitution.Split(
                gliss.StartMeasureIndex, gliss.EndMeasureIndex, systems, measureToSystemIdx);
            if (segments.IsEmpty)
                continue;

            foreach (var segment in segments)
            {
                var system = systems[segment.SystemIndex];
                double staffMiddleY = LayoutUtilities.ResolveStaffMiddleY(system, staffIndex, staffHeight);
                double startStaffYAbs = StaffFrame.PositionToDevice(gliss.StartStaffPosition, staffMiddleY);
                double endStaffYAbs = StaffFrame.PositionToDevice(gliss.EndStaffPosition, staffMiddleY);

                // Resolve segment-local X bounds against the system's measure layouts.
                // LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges.
                double segStartX, segEndX;
                if (segment.IsFirst)
                {
                    segStartX = realStartX;
                }
                else
                {
                    segStartX = system.Measures[0].X;
                }

                if (segment.IsLast)
                {
                    segEndX = realEndX;
                }
                else
                {
                    var lastMeasure = system.Measures[^1];
                    segEndX = lastMeasure.X + lastMeasure.Width;
                }

                // Y at the broken edge "freezes" at the destination pitch — visual cue
                // that the slide continues on the adjacent system.
                double segStartY = segment.IsFirst ? startStaffYAbs : endStaffYAbs;
                double segEndY = segment.IsLast ? endStaffYAbs : startStaffYAbs;
                if (segment.IsMiddle)
                {
                    double mid = (startStaffYAbs + endStaffYAbs) / 2.0;
                    segStartY = mid;
                    segEndY = mid;
                }

                // Apply gap: shorten along the line direction.
                // LILYPOND-REF: lily/line-spanner.cc:457 span_points[d] += -d * gaps[d] * magstep * dz.direction()
                // LILYPOND-REF: scm/define-grobs.scm:1570 (gap . 0.5)
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

                layouts.Add(new GlissandoLayout(
                    StartX: segStartX,
                    StartY: segStartY,
                    EndX: segEndX,
                    EndY: segEndY,
                    Style: gliss.Style,
                    SourcePosition: gliss.SourcePosition));
            }
        }

        return layouts.ToImmutableArray();
    }
}
