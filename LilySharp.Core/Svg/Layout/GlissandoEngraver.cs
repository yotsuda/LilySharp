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
/// LILYPOND-REF: lily/glissando-engraver.cc, scm/define-grobs.scm:1557-1577
/// Parameters: style=line, gap=0.5, padding=0.5, zigzag-width=0.75
/// </remarks>
public static class GlissandoEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:1570 (gap . 0.5)
    private const double Gap = 0.5;

    // LILYPOND-REF: scm/define-grobs.scm:1562,1565 (padding . 0.5)
    private const double Padding = 0.5;

    /// <summary>
    /// Calculates layout positions for all glissando items.
    /// </summary>
    public static ImmutableArray<GlissandoLayout> Calculate(
        ImmutableArray<GlissandoItem> glissandos,
        ImmutableArray<SystemLayout> systems,
        double staffHeight,
        int staffIndex = -1)
    {
        if (glissandos.IsDefaultOrEmpty || glissandos.Length == 0)
            return ImmutableArray<GlissandoLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var layouts = new List<GlissandoLayout>();

        foreach (var gliss in glissandos)
        {
            if (!measureMap.TryGetValue(gliss.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(gliss.EndMeasureIndex, out var endInfo))
                continue;

            var (startSystem, startMeasure) = startInfo;
            var (endSystem, endMeasure) = endInfo;

            // Get X positions from item layouts
            double startX = startMeasure.X;
            double endX = endMeasure.X;

            if (gliss.StartItemIndex < startMeasure.Items.Length)
                startX += startMeasure.Items[gliss.StartItemIndex].X;
            if (gliss.EndItemIndex < endMeasure.Items.Length)
                endX += endMeasure.Items[gliss.EndItemIndex].X;

            // Apply padding: start from right side of start note, end at left side of end note
            // LILYPOND-REF: scm/define-grobs.scm:1560 left attach-dir = RIGHT
            // LILYPOND-REF: scm/define-grobs.scm:1563 right attach-dir = LEFT
            startX += Padding;
            endX -= Padding;

            // Apply gap: shorten both ends slightly
            // LILYPOND-REF: scm/define-grobs.scm:1570 (gap . 0.5)
            double dx = endX - startX;
            double dy = (gliss.StartStaffPosition - gliss.EndStaffPosition) / 2.0;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > Gap * 2)
            {
                double gapRatio = Gap / length;
                startX += dx * gapRatio;
                endX -= dx * gapRatio;
            }

            // Calculate Y positions from staff positions
            double staffY = LayoutUtilities.FindStaffYInSystem(startSystem, staffIndex);
            double staffMiddleY = staffY + staffHeight / 2;
            double startY = staffMiddleY - gliss.StartStaffPosition / 2.0;
            double endY = staffMiddleY - gliss.EndStaffPosition / 2.0;

            // For cross-system glissandos, use endSystem Y
            if (startSystem.SystemIndex != endSystem.SystemIndex)
            {
                double endStaffY = LayoutUtilities.FindStaffYInSystem(endSystem, staffIndex);
                double endStaffMiddleY = endStaffY + staffHeight / 2;
                endY = endStaffMiddleY - gliss.EndStaffPosition / 2.0;
            }

            layouts.Add(new GlissandoLayout(
                StartX: startX,
                StartY: startY,
                EndX: endX,
                EndY: endY,
                Style: gliss.Style,
                SourcePosition: gliss.SourcePosition));
        }

        return layouts.ToImmutableArray();
    }
}
