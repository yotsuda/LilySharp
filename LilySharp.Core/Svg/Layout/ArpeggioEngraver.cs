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
/// Layout for a single arpeggio marking.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct ArpeggioLayout(
    double X,
    double TopY,
    double BottomY,
    int SourcePosition);

/// <summary>
/// Calculates arpeggio layouts from detected arpeggio items.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
/// Parameters: padding=0.5, direction=LEFT, protrusion=0.4
/// The arpeggio is a wavy vertical line placed to the left of a chord.
/// </remarks>
public static class ArpeggioEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:209 (padding . 0.5)
    private const double Padding = 0.5;

    /// <summary>
    /// Calculates layout positions for all arpeggio items.
    /// </summary>
    public static ImmutableArray<ArpeggioLayout> Calculate(
        ImmutableArray<ArpeggioItem> arpeggios,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        double staffHeight,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, double>? staffYByIndex = null)
    {
        if (arpeggios.IsDefaultOrEmpty || arpeggios.Length == 0)
            return ImmutableArray<ArpeggioLayout>.Empty;

        var measureMap = new Dictionary<int, (SystemLayout system, MeasureLayout measure)>();
        foreach (var system in systems)
        {
            foreach (var ml in system.Measures)
            {
                measureMap[ml.MeasureIndex] = (system, ml);
            }
        }

        var layouts = new List<ArpeggioLayout>();

        foreach (var arp in arpeggios)
        {
            if (!measureMap.TryGetValue(arp.MeasureIndex, out var info))
                continue;

            var (system, measure) = info;

            // Resolve this arpeggio's OWN staff (multi-staff): its measures (for
            // the item X) and the staff's vertical offset within the system.
            var arpMeasures = measuresByStaff != null
                && measuresByStaff.TryGetValue(arp.StaffIndex, out var mm) ? mm : measures;
            double staffOffset = staffYByIndex != null
                && staffYByIndex.TryGetValue(arp.StaffIndex, out var so) ? so : 0;

            // Get X position of the chord item, then place arpeggio to the left
            // LILYPOND-REF: scm/define-grobs.scm:206 (direction . ,LEFT)
            double itemX = measure.X + LayoutUtilities.GetItemXOffset(
                arpMeasures, arp.MeasureIndex, arp.ItemIndex, measure);

            double arpeggioX = itemX - Padding;

            // Calculate Y positions from staff positions. The arpeggio's Y is
            // absolute (system.Y based), so add the staff's within-system offset
            // so it lands over its OWN staff, not the first.
            double staffMiddleY = system.Y + staffOffset + staffHeight / 2;
            double topY = StaffFrame.PositionToDevice(arp.MaxStaffPosition, staffMiddleY);
            double bottomY = StaffFrame.PositionToDevice(arp.MinStaffPosition, staffMiddleY);

            // LILYPOND-REF: scm/define-grobs.scm:211 (protrusion . 0.4)
            topY -= 0.4;
            bottomY += 0.4;

            layouts.Add(new ArpeggioLayout(
                X: arpeggioX,
                TopY: topY,
                BottomY: bottomY,
                SourcePosition: arp.SourcePosition));
        }

        return layouts.ToImmutableArray();
    }
}
