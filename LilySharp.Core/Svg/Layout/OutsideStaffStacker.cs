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

    // =================================================================
    // Above-staff stacking
    // =================================================================

    // LILYPOND-REF: scm/define-grobs.scm outside-staff-priority —
    // TrillSpanner 50, BarNumber 100, TupletBracket 200, OttavaBracket 400,
    // TextScript 450, VoltaBracketSpanner 600, RehearsalMark 1500.
    // Lower priority = placed first = closer to the staff; later (higher
    // priority) grobs are pushed ABOVE everything already placed.

    /// <summary>
    /// Unified above-staff stacking: every above-staff annotation is placed
    /// in ascending outside-staff-priority order against a per-system
    /// occupancy seeded from the system's UP skyline (note/stem/beam
    /// content) and the above-staff tuplet brackets (which are bound to
    /// their beams and therefore registered as immovable).
    /// Replaces the previous pairwise special cases (bar-number-vs-volta in
    /// the renderer, music-mark-vs-volta in MusicMarkEngraver).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474
    /// outside_staff_axis_group — sort by priority, side-position each grob
    /// against the accumulated skyline, then merge its extent in.
    /// </remarks>
    public static (ImmutableArray<TrillSpannerLayout> Trills,
                   ImmutableArray<BarNumberLayout> BarNumbers,
                   ImmutableArray<OttavaBracketLayout> Ottavas,
                   ImmutableArray<CustomTextLayout> CustomTexts,
                   ImmutableArray<VoltaBracketLayout> Voltas,
                   ImmutableArray<MusicMarkLayout> MusicMarks)
        StackAboveStaff(
            ImmutableArray<SystemLayout> systems,
            IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
            ImmutableArray<TupletBracketLayout> tupletBrackets,
            ImmutableArray<TrillSpannerLayout> trills,
            ImmutableArray<BarNumberLayout> barNumbers,
            ImmutableArray<OttavaBracketLayout> ottavas,
            ImmutableArray<CustomTextLayout> customTexts,
            ImmutableArray<VoltaBracketLayout> voltas,
            ImmutableArray<MusicMarkLayout> musicMarks)
    {
        if (systems.IsDefaultOrEmpty)
            return (trills, barNumbers, ottavas, customTexts, voltas, musicMarks);

        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        // UP trackers: smaller page Y = further above the staff. Occupancy
        // records the TOP edge of everything placed so far.
        var trackers = new UpTracker[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            trackers[i] = new UpTracker(systems[i].Y);
            // Seed with the system's up-skyline (staff content protrusions).
            // VerticalSkyline.Height converts the internal sky-relative value
            // to real coordinates (negative = above the staff top); raw
            // Building.Height must NOT be used here.
            if (systemSkylines != null && i < systemSkylines.Count
                && !systemSkylines[i].up.IsEmpty)
            {
                var up = systemSkylines[i].up;
                foreach (var b in up.Buildings)
                {
                    if (double.IsInfinity(b.XLeft) || double.IsInfinity(b.XRight))
                        continue;
                    double mid = (b.XLeft + b.XRight) / 2;
                    double h = up.Height(mid);
                    if (double.IsInfinity(h) || double.IsNaN(h))
                        continue;
                    double protrusion = Math.Max(0, -h);
                    if (protrusion > 0)
                        trackers[i].AddRegion(b.XLeft, b.XRight, systems[i].Y - protrusion);
                }
            }
        }

        // Priority 200: above-staff tuplet brackets/numbers. They are bound
        // to their beams in this model, so they seed the occupancy without
        // being moved themselves.
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var tb in tupletBrackets)
            {
                if (!tb.IsStemUp || !measureToSystem.TryGetValue(tb.MeasureIndex, out int sysIdx))
                    continue;
                double sy = systems[sysIdx].Y;
                double top = sy + Math.Min(tb.StartY, tb.EndY) - 1.6; // number above the line
                trackers[sysIdx].AddRegion(tb.StartX, tb.EndX, top);
            }
        }

        // ---- 50: TrillSpanner ----
        var adjTrills = trills;
        if (!trills.IsDefaultOrEmpty)
        {
            var b = trills.ToBuilder();
            for (int i = 0; i < b.Count; i++)
            {
                var t = b[i];
                if (!measureToSystem.TryGetValue(t.StartMeasureIndex, out int sysIdx))
                    continue;
                double sy = systems[sysIdx].Y;
                // anchor = glyph baseline; body extends ~1.5sp up, 0.3 down.
                double newAbs = Place(trackers[sysIdx], t.GlyphX, t.LineEndX,
                    sy + t.Y, topOffset: -1.5, bottomOffset: 0.3);
                b[i] = t with { Y = newAbs - sy };
            }
            adjTrills = b.ToImmutable();
        }

        // ---- 100: BarNumber (absolute page Y) ----
        var adjBarNumbers = barNumbers;
        if (!barNumbers.IsDefaultOrEmpty)
        {
            var b = barNumbers.ToBuilder();
            for (int i = 0; i < b.Count; i++)
            {
                var bn = b[i];
                if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int sysIdx))
                    continue;
                double halfWidth = bn.Text.Length * 0.9;
                double x0 = bn.RightAligned ? bn.X - 2 * halfWidth : bn.X;
                double x1 = bn.RightAligned ? bn.X : bn.X + 2 * halfWidth;
                double newY = Place(trackers[sysIdx], x0, x1,
                    bn.Y, topOffset: -1.4, bottomOffset: 0.0);
                b[i] = bn with { Y = newY };
            }
            adjBarNumbers = b.ToImmutable();
        }

        // ---- 400: OttavaBracket (above-staff only) ----
        var adjOttavas = ottavas;
        if (!ottavas.IsDefaultOrEmpty)
        {
            var b = ottavas.ToBuilder();
            for (int i = 0; i < b.Count; i++)
            {
                var o = b[i];
                if (!o.IsAbove || !measureToSystem.TryGetValue(o.StartMeasureIndex, out int sysIdx))
                    continue;
                double sy = systems[sysIdx].Y;
                double newAbs = Place(trackers[sysIdx], o.StartX, o.EndX,
                    sy + o.Y, topOffset: -1.3, bottomOffset: Math.Max(0.3, o.EdgeHeight));
                b[i] = o with { Y = newAbs - sy };
            }
            adjOttavas = b.ToImmutable();
        }

        // ---- 450: TextScript (^"...") ----
        var adjCustomTexts = customTexts;
        if (!customTexts.IsDefaultOrEmpty)
        {
            var b = customTexts.ToBuilder();
            for (int i = 0; i < b.Count; i++)
            {
                var ct = b[i];
                if (!measureToSystem.TryGetValue(ct.MeasureIndex, out int sysIdx))
                    continue;
                double sy = systems[sysIdx].Y;
                double halfWidth = Math.Max(1.0, ct.Text.Length * 0.55);
                double newAbs = Place(trackers[sysIdx], ct.X - halfWidth, ct.X + halfWidth,
                    sy + ct.Y, topOffset: -1.6, bottomOffset: 0.3);
                b[i] = ct with { Y = newAbs - sy };
            }
            adjCustomTexts = b.ToImmutable();
        }

        // ---- 600: VoltaBracket ----
        var adjVoltas = voltas;
        if (!voltas.IsDefaultOrEmpty)
        {
            var b = voltas.ToBuilder();
            for (int i = 0; i < b.Count; i++)
            {
                var v = b[i];
                if (!measureToSystem.TryGetValue(v.StartMeasureIndex, out int sysIdx))
                    continue;
                double sy = systems[sysIdx].Y;
                // anchor = bracket line; hooks and text hang ~1.6sp below it.
                double newAbs = Place(trackers[sysIdx], v.StartX, v.EndX,
                    sy + v.Y, topOffset: -0.15, bottomOffset: 1.6);
                b[i] = v with { Y = newAbs - sy };
            }
            adjVoltas = b.ToImmutable();
        }

        // ---- 1500: MusicMark (rehearsal/section labels) ----
        var adjMarks = musicMarks;
        if (!musicMarks.IsDefaultOrEmpty)
        {
            var b = musicMarks.ToBuilder();
            for (int i = 0; i < b.Count; i++)
            {
                var m = b[i];
                if (!measureToSystem.TryGetValue(m.MeasureIndex, out int sysIdx))
                    continue;
                double sy = systems[sysIdx].Y;
                double halfWidth = Math.Max(1.4, m.Text.Length * 0.7);
                // anchor = text baseline inside the box: box top ~2.1 above,
                // box bottom ~0.6 below.
                double newAbs = Place(trackers[sysIdx], m.X - halfWidth, m.X + halfWidth,
                    sy + m.Y, topOffset: -2.1, bottomOffset: 0.6);
                b[i] = m with { Y = newAbs - sy };
            }
            adjMarks = b.ToImmutable();
        }

        return (adjTrills, adjBarNumbers, adjOttavas, adjCustomTexts, adjVoltas, adjMarks);
    }

    /// <summary>
    /// Places one element: its BOTTOM edge must clear the current occupancy
    /// top by outside-staff-padding; the element only ever moves AWAY from
    /// the staff. Registers the element's extent and returns the new anchor.
    /// </summary>
    private static double Place(UpTracker tracker, double x0, double x1,
        double anchorY, double topOffset, double bottomOffset)
    {
        double occupiedTop = tracker.MinYAt(x0, x1);
        double required = occupiedTop - OutsideStaffPadding - bottomOffset;
        double newAnchor = Math.Min(anchorY, required);
        tracker.AddRegion(x0, x1, newAnchor + topOffset);
        return newAnchor;
    }

    /// <summary>
    /// Occupancy above the staff: tracks the topmost (smallest page Y) edge
    /// per X region. Mirror image of <see cref="OccupiedTracker"/>.
    /// </summary>
    private sealed class UpTracker
    {
        private readonly List<(double startX, double endX, double topY)> _regions = new();
        private readonly double _staffTop;

        public UpTracker(double staffTop) => _staffTop = staffTop;

        public double MinYAt(double startX, double endX)
        {
            double minY = _staffTop;
            foreach (var (rStart, rEnd, rTop) in _regions)
            {
                if (rStart < endX && rEnd > startX)
                    minY = Math.Min(minY, rTop);
            }
            return minY;
        }

        public void AddRegion(double startX, double endX, double topY)
            => _regions.Add((startX, endX, topY));
    }
}
