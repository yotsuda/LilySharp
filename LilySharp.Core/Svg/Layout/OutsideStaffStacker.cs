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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Adjusts Y positions of below-staff elements using LP-conformant
/// priority-based stacking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside_staff_axis_group
/// LILYPOND-REF: scm/define-grobs.scm outside-staff-priority values
///
/// Elements with lower outside-staff-priority are placed closer to the staff.
/// Higher priority elements are stacked further away, avoiding collisions with
/// already-placed lower-priority elements.
///
/// Below-staff priority order (ascending = closer to staff):
///   DynamicLineSpanner: 250 (includes both DynamicText and Hairpin)
///   TextSpanner: 350
///   SustainPedalLineSpanner: 1050
///
/// The stacker post-processes layouts from individual engravers, adjusting Y values
/// so that elements don't overlap. Each priority group is processed in order,
/// with lower-priority elements occupying space first.
/// </remarks>
public static class OutsideStaffStacker
{
    // Staff geometry
    private const double StaffBottom = 4.0;

    // LILYPOND-REF: scm/define-grobs.scm outside-staff-padding = 0.46
    private const double OutsideStaffPadding = 0.46;

    // Element height estimates (staff spaces)
    // LILYPOND-REF: define-grobs.scm:1317 DynamicText Y-offset = (scale-by-font-size -0.6)
    private const double DynamicTextAscent = 1.2;
    private const double DynamicTextDescent = 0.3;

    // LILYPOND-REF: scm/define-grobs.scm:1655 Hairpin height = 0.6666
    private const double HairpinHalfHeight = 0.6666 / 2.0;

    // Text spanner text dimensions
    private const double TextSpannerAscent = 1.2;
    private const double TextSpannerDescent = 0.3;

    // Dynamic text half-width estimate for X collision range
    private const double DynamicHalfWidth = 0.75;

    /// <summary>
    /// Adjusts below-staff element Y positions using priority-based stacking.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside_staff_axis_group
    ///
    /// Processing order:
    /// 1. Priority 250 (DynamicLineSpanner): dynamics registered first, then hairpins
    ///    adjusted to avoid overlap with dynamics at the same X range.
    /// 2. Priority 350 (TextSpanner): adjusted to avoid all priority-250 elements.
    ///
    /// Uses Math.Max to ensure the stacker never moves elements closer to the staff
    /// than the individual engraver already calculated.
    /// </remarks>
    public static (ImmutableArray<DynamicLayout> Dynamics,
                    ImmutableArray<HairpinLayout> Hairpins,
                    ImmutableArray<TextSpannerLayout> TextSpanners)
        StackBelowStaff(
            ImmutableArray<SystemLayout> systems,
            ImmutableArray<DynamicLayout> dynamics,
            ImmutableArray<HairpinLayout> hairpins,
            ImmutableArray<TextSpannerLayout> textSpanners)
    {
        if ((dynamics.IsDefaultOrEmpty && hairpins.IsDefaultOrEmpty && textSpanners.IsDefaultOrEmpty)
            || systems.Length == 0)
        {
            return (dynamics, hairpins, textSpanners);
        }

        // Build measure-to-system mapping
        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        // Per-system occupied space tracker
        var trackers = new OccupiedTracker[systems.Length];
        for (int i = 0; i < systems.Length; i++)
            trackers[i] = new OccupiedTracker();

        // --- Priority 250: DynamicLineSpanner (dynamics + hairpins) ---
        // LILYPOND-REF: scm/define-grobs.scm:1270 DynamicLineSpanner.outside-staff-priority = 250

        // Register dynamics (keep their engraver-calculated Y, just record occupied space)
        if (!dynamics.IsDefaultOrEmpty)
        {
            foreach (var dyn in dynamics)
            {
                if (!measureToSystem.TryGetValue(dyn.MeasureIndex, out int sysIdx))
                    continue;

                double xStart = dyn.X - DynamicHalfWidth;
                double xEnd = dyn.X + DynamicHalfWidth;
                double bottom = dyn.Y + DynamicTextDescent;
                trackers[sysIdx].AddRegion(xStart, xEnd, bottom);
            }
        }

        // Adjust hairpins: avoid overlapping with dynamics in the same X range
        var adjHairpins = hairpins;
        if (!hairpins.IsDefaultOrEmpty)
        {
            var builder = hairpins.ToBuilder();
            for (int i = 0; i < builder.Count; i++)
            {
                var hp = builder[i];
                if (!measureToSystem.TryGetValue(hp.StartMeasureIndex, out int sysIdx))
                    continue;

                double occupiedBottom = trackers[sysIdx].MaxYAt(hp.StartX, hp.EndX);
                double requiredY = occupiedBottom + OutsideStaffPadding + HairpinHalfHeight;
                double newY = Math.Max(hp.Y, requiredY);

                if (Math.Abs(newY - hp.Y) > 0.01)
                    builder[i] = hp with { Y = newY };

                // Register hairpin in tracker
                double finalBottom = builder[i].Y + HairpinHalfHeight;
                trackers[sysIdx].AddRegion(hp.StartX, hp.EndX, finalBottom);
            }
            adjHairpins = builder.ToImmutable();
        }

        // --- Priority 350: TextSpanner ---
        // LILYPOND-REF: scm/define-grobs.scm:3472 TextSpanner.outside-staff-priority = 350

        var adjTextSpanners = textSpanners;
        if (!textSpanners.IsDefaultOrEmpty)
        {
            var builder = textSpanners.ToBuilder();
            for (int i = 0; i < builder.Count; i++)
            {
                var sp = builder[i];
                if (!measureToSystem.TryGetValue(sp.StartMeasureIndex, out int sysIdx))
                    continue;

                double occupiedBottom = trackers[sysIdx].MaxYAt(sp.StartX, sp.EndX);
                double requiredY = occupiedBottom + OutsideStaffPadding + TextSpannerAscent;
                double newY = Math.Max(sp.Y, requiredY);

                if (Math.Abs(newY - sp.Y) > 0.01)
                    builder[i] = sp with { Y = newY };

                // Register text spanner in tracker
                double finalBottom = builder[i].Y + TextSpannerDescent;
                trackers[sysIdx].AddRegion(sp.StartX, sp.EndX, finalBottom);
            }
            adjTextSpanners = builder.ToImmutable();
        }

        return (dynamics, adjHairpins, adjTextSpanners);
    }

    /// <summary>
    /// Tracks occupied vertical space per X region for collision avoidance.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:912-972 skyline_spacing
    ///
    /// Simplified interval-based tracker: stores (startX, endX, bottomY) tuples
    /// and queries for the maximum bottomY overlapping a given X range.
    /// This is equivalent to using a DOWN skyline but with simpler implementation
    /// suitable for the discrete set of outside-staff elements.
    /// </remarks>
    private sealed class OccupiedTracker
    {
        private readonly List<(double startX, double endX, double bottomY)> _regions = new();

        /// <summary>
        /// Returns the maximum occupied bottom Y at the given X range.
        /// Returns StaffBottom if no regions overlap.
        /// </summary>
        public double MaxYAt(double startX, double endX)
        {
            double maxY = StaffBottom;
            foreach (var (rStart, rEnd, rBottom) in _regions)
            {
                if (rStart < endX && rEnd > startX) // X overlap
                    maxY = Math.Max(maxY, rBottom);
            }
            return maxY;
        }

        /// <summary>
        /// Registers an occupied region.
        /// </summary>
        public void AddRegion(double startX, double endX, double bottomY)
        {
            _regions.Add((startX, endX, bottomY));
        }
    }
}
