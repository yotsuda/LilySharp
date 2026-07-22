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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

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
internal static class OutsideStaffStacker
{
    // Staff geometry
    private const double StaffBottom = 4.0;

    // LILYPOND-REF: scm/define-grobs.scm outside-staff-padding = 0.46 — governs stacking a
    // grob against OTHER outside-staff grobs.
    private const double OutsideStaffPadding = 0.46;

    // The gap from the note/staff skyline to a below-staff dynamic or hairpin is the
    // DynamicLineSpanner's own side-position padding, NOT outside-staff-padding.
    // LILYPOND-REF: scm/define-grobs.scm:1408 DynamicLineSpanner (padding . 0.6).
    private const double DynamicLineSpannerPadding = 0.6;

    // Element height estimates (staff spaces)
    // LILYPOND-REF: define-grobs.scm:1450 DynamicText Y-offset = (scale-by-font-size -0.6)
    private const double DynamicTextAscent = 1.2;
    private const double DynamicTextDescent = 0.3;

    // LILYPOND-REF: scm/define-grobs.scm:1785 Hairpin height = 0.6666
    private const double HairpinHalfHeight = 0.6666 / 2.0;

    // Text spanner text dimensions
    private const double TextSpannerAscent = 1.2;
    private const double TextSpannerDescent = 0.3;

    // Dynamic text half-width estimate for X collision range
    private const double DynamicHalfWidth = 0.75;

    // Serif text-metric ratios (fraction of the font size / em) used to bound a
    // text grob's vertical extent from its font size. Own tuning approximating a
    // serif face; no single LP grob source.
    private const double CapHeightEm = 0.71;   // cap height (digits: no ascenders/descenders)
    private const double TextAscentEm = 0.75;  // ascent above the baseline
    private const double TextDescentEm = 0.25; // descent below the baseline

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
    /// The stacker only ever moves an element AWAY from the staff (never closer than
    /// the individual engraver already calculated) — a Y-up clamp toward smaller Y-up
    /// below the staff, larger Y-up above.
    /// </remarks>
    public static (ImmutableArray<DynamicLayout> Dynamics,
                    ImmutableArray<HairpinLayout> Hairpins)
        StackBelowStaff(
            ImmutableArray<SystemLayout> systems,
            ImmutableArray<DynamicLayout> dynamics,
            ImmutableArray<HairpinLayout> hairpins,
            ImmutableArray<ArticulationLayout> articulations = default,
            bool applyStaffOffsets = false)
    {
        if ((dynamics.IsDefaultOrEmpty && hairpins.IsDefaultOrEmpty)
            || systems.Length == 0)
        {
            return (dynamics, hairpins);
        }

        // Build measure-to-system mapping
        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        // Each system's own staff-Y offsets. Under hara-kiri a staff's within-system
        // offset can differ between systems, so seed each staff's tracker from ITS
        // system's geometry (not a single global table). Without hara-kiri every
        // system has the same staff Y, so this is byte-identical.
        var staffYBySystem = new List<Dictionary<int, double>>(systems.Length);
        for (int s = 0; s < systems.Length; s++)
        {
            var map = new Dictionary<int, double>();
            if (!systems[s].StaffGroups.IsDefaultOrEmpty)
                foreach (var sg in systems[s].StaffGroups)
                    foreach (var st in sg.Staves)
                        map[st.StaffIndex] = st.Y;
            staffYBySystem.Add(map);
        }

        // Occupancy tracker PER (system, staff): each staff's below-staff column
        // stacks down from ITS OWN bottom, so a hairpin under staff 2 is not pushed
        // by staff 1's dynamics (they share the single Dynamics/Hairpin tables but
        // occupy different vertical bands).
        // FRAME: system-relative Y-up (up-positive, the SYSTEM TOP = 0), the native
        // LP frame the grobs store — below the staff is negative Y-up. Seeded lazily
        // at the staff bottom's Y-up = -(StaffBottom + within-system offset); dir=-1
        // stacks DOWNWARD (the frontier is the SMALLEST Y-up, furthest below). A single
        // staff (offset 0) reproduces the former per-system tracker exactly.
        var trackers = new Dictionary<(int Sys, int Staff), DirectionalOccupancy>();
        DirectionalOccupancy Track(int sys, int staff)
        {
            if (!trackers.TryGetValue((sys, staff), out var t))
            {
                // Only apply per-staff offsets in the final annotation pass (the
                // prelim/single-staff passes supply no staff-Y and stack from a zero
                // baseline, so gating on this keeps their extent estimate unchanged).
                double off = applyStaffOffsets && sys >= 0 && sys < staffYBySystem.Count
                    && staffYBySystem[sys].TryGetValue(staff, out var so) ? so : 0;
                t = new DirectionalOccupancy(-(StaffBottom + off), dir: -1);
                trackers[(sys, staff)] = t;
            }
            return t;
        }

        // Below-staff articulations (Script grobs) have NO outside-staff
        // priority in LilyPond: they sit against the note and everything at
        // priority 250+ side-positions BELOW them. Seed them as immovable.
        // LILYPOND-REF: scm/define-grobs.scm Script — no outside-staff-priority;
        //   DynamicLineSpanner side-positions against the staff skyline
        //   which includes the scripts.
        if (!articulations.IsDefaultOrEmpty)
        {
            foreach (var a in articulations)
            {
                if (a.IsAbove || !measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                    continue;
                // a.YUp is Y-up above the staff middle; the tracker frame is
                // system-relative Y-up, where the staff middle sits at -(off + 2) (staff
                // top is off below the system top, its middle 2 further down), so the
                // grob's system-relative Y-up is a.YUp - off - 2.
                double off = applyStaffOffsets && sysIdx >= 0 && sysIdx < staffYBySystem.Count
                    && staffYBySystem[sysIdx].TryGetValue(a.StaffIndex, out var sso) ? sso : 0;
                double aYup = a.YUp - off - 2.0;
                // Glyph roughly centered on its anchor; half-extent ~0.6sp below.
                Track(sysIdx, a.StaffIndex).AddRegion(a.X - 0.6, a.X + 0.6, aYup - 0.6);
            }
        }

        // --- Priority 250: DynamicLineSpanner (dynamics + hairpins) ---
        // LILYPOND-REF: scm/define-grobs.scm:1407 DynamicLineSpanner.outside-staff-priority = 250

        // Dynamics: push below anything already occupying their X range
        // (below-staff scripts), then record their own extent.
        var adjDynamics = dynamics;
        if (!dynamics.IsDefaultOrEmpty)
        {
            var dynBuilder = dynamics.ToBuilder();
            for (int i = 0; i < dynBuilder.Count; i++)
            {
                var dyn = dynBuilder[i];
                // Forced-above dynamics sit above the staff (DynamicEngraver placed them);
                // the below-staff stacker leaves them untouched and ignores them as
                // below-staff occupiers.
                if (dyn.IsAbove)
                    continue;
                if (!measureToSystem.TryGetValue(dyn.MeasureIndex, out int sysIdx))
                    continue;

                double xStart = dyn.X - DynamicHalfWidth;
                double xEnd = dyn.X + DynamicHalfWidth;
                var tracker = Track(sysIdx, dyn.StaffIndex);
                double occupied = tracker.Frontier(xStart, xEnd);
                double requiredYup = occupied - DynamicLineSpannerPadding - DynamicTextAscent;
                // System-relative Y-up: the grob's dyn.YUp (above the staff middle) sits
                // at dyn.YUp - off - 2; clamp it no closer to the staff than the frontier
                // demands (further below = smaller Y-up), and reflect any push back to the
                // staff-middle frame the grob stores in (+ off + 2).
                double off = applyStaffOffsets && sysIdx >= 0 && sysIdx < staffYBySystem.Count
                    && staffYBySystem[sysIdx].TryGetValue(dyn.StaffIndex, out var so) ? so : 0;
                double dynYup = dyn.YUp - off - 2.0;
                double curDynYup = dynYup;
                if (requiredYup < dynYup)
                {
                    curDynYup = requiredYup;
                    dynBuilder[i] = dyn with { YUp = requiredYup + off + 2.0 };
                }

                double bottom = curDynYup - DynamicTextDescent;
                tracker.AddRegion(xStart, xEnd, bottom);
            }
            adjDynamics = dynBuilder.ToImmutable();
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

                var tracker = Track(sysIdx, hp.StaffIndex);
                double occupiedBottom = tracker.Frontier(hp.StartX, hp.EndX);
                double requiredYup = occupiedBottom - DynamicLineSpannerPadding - HairpinHalfHeight;
                // hp.YUp is already Y-up from the system top — the tracker frame — so it
                // enters directly. Clamp no closer to the staff than the frontier demands
                // (further below = smaller Y-up).
                double newYup = Math.Min(hp.YUp, requiredYup);

                if (Math.Abs(newYup - hp.YUp) > 0.01)
                    builder[i] = hp with { YUp = newYup };

                // Register hairpin in tracker (its bottom edge = centre - half height)
                double finalBottom = builder[i].YUp - HairpinHalfHeight;
                tracker.AddRegion(hp.StartX, hp.EndX, finalBottom);
            }
            adjHairpins = builder.ToImmutable();
        }

        // TextSpanner (priority 350) is now stacked ABOVE the staff (LilyPond
        // TextSpanner direction=UP) by StackAboveStaff, not here.
        return (adjDynamics, adjHairpins);
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
                   ImmutableArray<MusicMarkLayout> MusicMarks,
                   ImmutableArray<DynamicLayout> Dynamics,
                   ImmutableArray<TextSpannerLayout> TextSpanners)
        StackAboveStaff(
            ImmutableArray<SystemLayout> systems,
            IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
            ImmutableArray<TupletBracketLayout> tupletBrackets,
            ImmutableArray<TrillSpannerLayout> trills,
            ImmutableArray<BarNumberLayout> barNumbers,
            ImmutableArray<OttavaBracketLayout> ottavas,
            ImmutableArray<CustomTextLayout> customTexts,
            ImmutableArray<VoltaBracketLayout> voltas,
            ImmutableArray<MusicMarkLayout> musicMarks,
            ImmutableArray<ArticulationLayout> articulations = default,
            ImmutableArray<DynamicLayout> aboveDynamics = default,
            ImmutableArray<TextSpannerLayout> textSpanners = default)
    {
        if (systems.IsDefaultOrEmpty)
            return (trills, barNumbers, ottavas, customTexts, voltas, musicMarks, aboveDynamics, textSpanners);

        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        var trackers = SeedAboveTrackers(systems, systemSkylines, articulations, tupletBrackets, measureToSystem);

        // Movable outside-staff grobs, placed in ascending outside-staff-priority
        // order; each pass clears the occupancy seeded/accumulated by the earlier ones.
        var adjTrills = PlaceTrills(trills, trackers, measureToSystem);
        var adjBarNumbers = PlaceBarNumbers(barNumbers, trackers, measureToSystem);
        var adjDynamics = PlaceAboveDynamics(aboveDynamics, trackers, measureToSystem, systems);
        var adjTextSpanners = PlaceTextSpanners(textSpanners, trackers, measureToSystem);
        var adjOttavas = PlaceOttavas(ottavas, trackers, measureToSystem);
        var adjCustomTexts = PlaceCustomTexts(customTexts, trackers, measureToSystem, systems);
        var adjVoltas = PlaceVoltas(voltas, trackers, measureToSystem);
        var adjMarks = PlaceMusicMarks(musicMarks, trackers, measureToSystem, systems);

        return (adjTrills, adjBarNumbers, adjOttavas, adjCustomTexts, adjVoltas, adjMarks, adjDynamics, adjTextSpanners);
    }

    /// <summary>
    /// Builds and seeds the per-system UP occupancy trackers: the system
    /// up-skyline (staff protrusions), the top-staff prefix clef ink, the
    /// note-bound above-staff scripts, and the above-staff tuplet brackets.
    /// These carry no outside-staff priority, so they seed the occupancy that
    /// the movable grobs must clear.
    /// </summary>
    private static DirectionalOccupancy[] SeedAboveTrackers(
        ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<TupletBracketLayout> tupletBrackets,
        Dictionary<int, int> measureToSystem)
    {
        // UP trackers: larger Y-up = further above the staff. Occupancy
        // records the TOP edge of everything placed so far.
        // FRAME: system-relative Y-up (up-positive, the SYSTEM TOP = 0), the native
        // LP frame the grobs store — above the staff is positive Y-up. Every
        // seed/region/grob Y below is measured from the system top, and every grob
        // writes back its unchanged system-relative YUp, so the stacker reads no
        // absolute SystemLayout.Y — decoupled for the Stage-4 W2 stacking-origin flip.
        var trackers = new DirectionalOccupancy[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            trackers[i] = new DirectionalOccupancy(0.0, dir: +1);
            // Seed with the system's up-skyline (staff content protrusions).
            // VerticalSkyline.Height converts the internal sky-relative value
            // to real Y-up coordinates (positive = above the staff top); raw
            // SkylineBuilding.ValueAt must NOT be used here.
            if (systemSkylines != null && i < systemSkylines.Count
                && !systemSkylines[i].up.IsEmpty)
            {
                var up = systemSkylines[i].up;
                foreach (var b in up.Buildings)
                {
                    if (double.IsInfinity(b.Start) || double.IsInfinity(b.End))
                        continue;
                    double mid = (b.Start + b.End) / 2;
                    double h = up.Height(mid);
                    if (double.IsInfinity(h) || double.IsNaN(h))
                        continue;
                    double protrusion = Math.Max(0, h);
                    if (protrusion > 0)
                        trackers[i].AddRegion(b.Start, b.End, protrusion);
                }
            }

            // Prefix clef ink: the up-skyline is built from music items
            // only, so a treble clef's ~1.8sp protrusion above the staff
            // top is invisible to it. Seed the TOP staff's clef ink so
            // line-start marks clear the clef. Geometry mirrors DrawClef:
            // glyph at (Indent + 0.3, staffTop + anchor line).
            var firstStaff = systems[i].StaffGroups.IsDefaultOrEmpty
                ? null
                : systems[i].StaffGroups
                    .SelectMany(g => g.Staves)
                    .Where(s => !s.IsHidden)
                    .OrderBy(s => s.Y)
                    .FirstOrDefault();
            if (firstStaff != null)
            {
                var (clefBox, anchorLine) = firstStaff.Clef switch
                {
                    ClefType.Bass => (GlyphMetrics.ClefF, 1.0),
                    ClefType.Alto => (GlyphMetrics.ClefC, 2.0),
                    ClefType.Tenor => (GlyphMetrics.ClefC, 1.0),
                    _ => (GlyphMetrics.ClefG, 3.0),
                };
                double clefProtrusion = clefBox.Top - anchorLine;
                if (clefProtrusion > 0)
                {
                    double clefX = systems[i].Indent + 0.3;
                    trackers[i].AddRegion(
                        clefX + clefBox.Left, clefX + clefBox.Right,
                        clefProtrusion - firstStaff.Y);
                }
            }
        }

        // Above-staff scripts (trill, turn, fermata, editorial accidentals …)
        // are bound to their notes: they carry no outside-staff-priority and
        // enter the skyline BEFORE any outside-staff grob is placed, so
        // movable marks (rehearsal/section marks etc.) must clear them.
        // LILYPOND-REF: lily/axis-group-interface.cc:359-474 — grobs without
        // outside-staff-priority stay in the support skyline.
        if (!articulations.IsDefaultOrEmpty)
        {
            foreach (var a in articulations)
            {
                if (!a.IsAbove || !measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                    continue;
                // a.YUp is Y-up above the staff middle; reflect to system-relative
                // Y-up against this staff's WITHIN-SYSTEM middle, which in that frame
                // is the staff's own Y-up offset less half a staff.
                double relY = a.YUp + (LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], a.StaffIndex) - 2.0);
                double inkTop = relY + a.Ink.Top;     // BBox Top is up-positive
                if (inkTop <= 0)
                    continue; // entirely inside the staff — the up-skyline covers it
                trackers[sysIdx].AddRegion(a.X + a.Ink.Left, a.X + a.Ink.Right, inkTop);
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
                // Bracket line + the centered number's upper half
                // (number font = 0.6 x 4sp, cap height ~0.71em). tb.*YUp is Y-up from
                // the system top; the highest (most-above) endpoint is the max YUp,
                // and the top of the number's ink sits above it.
                double top = Math.Max(tb.StartYUp, tb.EndYUp) + CapHeightEm * 2.4 / 2 + 0.1;
                trackers[sysIdx].AddRegion(tb.StartX, tb.EndX, top);
            }
        }

        return trackers;
    }

    // ---- 50: TrillSpanner ----
    private static ImmutableArray<TrillSpannerLayout> PlaceTrills(
        ImmutableArray<TrillSpannerLayout> trills, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem)
    {
        if (trills.IsDefaultOrEmpty)
            return trills;
        var b = trills.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var t = b[i];
            // Lower-staff trills are already positioned over their own staff
            // by the engraver; the staff-0 seeded occupancy would wrongly pull
            // them back up to the top staff.
            if (t.StaffIndex != 0)
                continue;
            if (!measureToSystem.TryGetValue(t.StartMeasureIndex, out int sysIdx))
                continue;
            // System-relative Y-up: t.YUp is Y-up from the system top, entering the
            // tracker directly; the placed anchor writes back unchanged.
            // anchor = "tr" glyph baseline; ink extent from the font bbox
            // (scripts.trill: 2.16sp above the baseline), wave +-0.25.
            double newRel = Place(trackers[sysIdx],
                t.GlyphX + GlyphMetrics.OrnTrillGlyph.Left, t.LineEndX,
                t.YUp,
                topOffset: GlyphMetrics.OrnTrillGlyph.Top,
                bottomOffset: 0.25);
            b[i] = t with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 100: BarNumber (absolute page Y) ----
    private static ImmutableArray<BarNumberLayout> PlaceBarNumbers(
        ImmutableArray<BarNumberLayout> barNumbers, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem)
    {
        if (barNumbers.IsDefaultOrEmpty)
            return barNumbers;
        var b = barNumbers.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var bn = b[i];
            if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int sysIdx))
                continue;
            // Measured digit width; digits have no descenders, cap
            // height ~0.71em above the baseline anchor.
            double width = SerifTextMetrics.MeasureBold(bn.Text, BarNumberEngraver.FontSize);
            double x0 = bn.RightAligned ? bn.X - width : bn.X;
            double x1 = bn.RightAligned ? bn.X : bn.X + width;
            // System-relative Y-up: bn.YUp is Y-up from the system top, entering directly.
            double newRel = Place(trackers[sysIdx], x0, x1,
                bn.YUp,
                topOffset: CapHeightEm * BarNumberEngraver.FontSize, bottomOffset: 0.0);
            b[i] = bn with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 250: DynamicText forced ABOVE (@f.up) ----
    // LILYPOND-REF: scm/define-grobs.scm:1298 DynamicText.outside-staff-priority = 250
    // Below-staff dynamics are handled by StackBelowStaff; here the FORCED-above ones
    // stack outward from the staff and push higher-priority above-staff grobs (ottava,
    // marks, …) clear of them. Text ascends UP from its baseline anchor.
    private static ImmutableArray<DynamicLayout> PlaceAboveDynamics(
        ImmutableArray<DynamicLayout> aboveDynamics, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem, ImmutableArray<SystemLayout> systems)
    {
        if (aboveDynamics.IsDefaultOrEmpty)
            return aboveDynamics;
        var b = aboveDynamics.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var dyn = b[i];
            if (!dyn.IsAbove || !measureToSystem.TryGetValue(dyn.MeasureIndex, out int sysIdx))
                continue;
            // Stack in system-relative Y-up: dyn.YUp relative to this staff's WITHIN-
            // SYSTEM middle is dyn.YUp + midUp; place, then shift back.
            double midUp = LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], dyn.StaffIndex) - 2.0;
            double newRel = Place(trackers[sysIdx],
                dyn.X - DynamicHalfWidth, dyn.X + DynamicHalfWidth,
                dyn.YUp + midUp,
                topOffset: DynamicTextAscent, bottomOffset: 0.0);
            b[i] = dyn with { YUp = newRel - midUp };
        }
        return b.ToImmutable();
    }

    // ---- 350: TextSpanner (accel./rit. — LilyPond TextSpanner direction=UP) ----
    // LILYPOND-REF: scm/define-grobs.scm TextSpanner (direction . UP),
    //   (outside-staff-priority . 350), (staff-padding . 0.8). Placed above the
    //   staff, clearing the up-skyline, instead of below where it hit low notes.
    private static ImmutableArray<TextSpannerLayout> PlaceTextSpanners(
        ImmutableArray<TextSpannerLayout> textSpanners, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem)
    {
        if (textSpanners.IsDefaultOrEmpty)
            return textSpanners;
        var b = textSpanners.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var ts = b[i];
            if (ts.StaffIndex != 0) continue; // top staff only, like trills
            if (!measureToSystem.TryGetValue(ts.StartMeasureIndex, out int sysIdx)) continue;
            // System-relative Y-up: ts.YUp is Y-up from the system top, entering directly.
            double newRel = Place(trackers[sysIdx], ts.StartX, ts.EndX,
                ts.YUp,
                topOffset: TextSpannerAscent, bottomOffset: TextSpannerDescent);
            b[i] = ts with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 400: OttavaBracket (above-staff only) ----
    private static ImmutableArray<OttavaBracketLayout> PlaceOttavas(
        ImmutableArray<OttavaBracketLayout> ottavas, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem)
    {
        if (ottavas.IsDefaultOrEmpty)
            return ottavas;
        var b = ottavas.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var o = b[i];
            if (!o.IsAbove || !measureToSystem.TryGetValue(o.StartMeasureIndex, out int sysIdx))
                continue;
            // Lower-staff brackets are already placed over their OWN staff by
            // OttavaBracketEngraver (via staffYByIndex); the staff-0 seeded
            // occupancy would wrongly pull them up to the top staff. Same
            // treatment as lower-staff trills above.
            if (o.StaffIndex != 0)
                continue;
            // System-relative Y-up: o.YUp is Y-up from the system top, entering directly.
            // anchor = text baseline / line Y; "8va" at 0.45 x 4sp with
            // ~0.75em ascent; the end hook drops EdgeHeight below.
            double newRel = Place(trackers[sysIdx], o.StartX, o.EndX,
                o.YUp,
                topOffset: TextAscentEm * (0.45 * 4.0),
                bottomOffset: Math.Max(0.1, o.EdgeHeight));
            b[i] = o with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 450: TextScript (^"...") ----
    private static ImmutableArray<CustomTextLayout> PlaceCustomTexts(
        ImmutableArray<CustomTextLayout> customTexts, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem, ImmutableArray<SystemLayout> systems)
    {
        if (customTexts.IsDefaultOrEmpty)
            return customTexts;
        var b = customTexts.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var ct = b[i];
            if (!measureToSystem.TryGetValue(ct.MeasureIndex, out int sysIdx))
                continue;
            // Centered italic text at 0.6 x 4sp; measured width (bold
            // table, a slight overestimate for italic), ~0.75em ascent
            // and ~0.25em descent around the baseline anchor.
            const double ctFs = 0.6 * 4.0;
            double halfWidth = SerifTextMetrics.MeasureBold(ct.Text, ctFs) / 2;
            // Stack in system-relative Y-up: ct.YUp relative to this staff's WITHIN-
            // SYSTEM middle is ct.YUp + midUp; place, then shift back.
            double midUp = LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], ct.StaffIndex) - 2.0;
            double newRel = Place(trackers[sysIdx], ct.X - halfWidth, ct.X + halfWidth,
                ct.YUp + midUp,
                topOffset: TextAscentEm * ctFs, bottomOffset: TextDescentEm * ctFs);
            b[i] = ct with { YUp = newRel - midUp };
        }
        return b.ToImmutable();
    }

    // ---- 600: VoltaBracketSpanner ----
    // LilyPond's outside-staff grob here is the SPANNER — an axis group
    // holding ALL volta brackets of a system — so consecutive endings
    // share ONE side-positioned Y per system instead of each bracket
    // finding its own height over its own bars.
    // LILYPOND-REF: scm/define-grobs.scm VoltaBracketSpanner —
    //   (axes . (Y)) (outside-staff-priority . 600) (side-axis . Y).
    private static ImmutableArray<VoltaBracketLayout> PlaceVoltas(
        ImmutableArray<VoltaBracketLayout> voltas, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem)
    {
        if (voltas.IsDefaultOrEmpty)
            return voltas;
        double VoltaBottom(VoltaBracketLayout v)
        {
            // anchor = bracket line. Hooks drop EdgeHeight; the volta
            // number hangs from line+0.3 at 0.6 x 4sp (renderer
            // geometry), so the deeper of the two bounds the extent.
            double textDepth = string.IsNullOrEmpty(v.VoltaText)
                ? 0
                : 0.3 + TextAscentEm * (0.6 * 4.0);
            return Math.Max(VoltaBracketEngraver.GetEdgeHeight(), textDepth);
        }

        var b = voltas.ToBuilder();
        foreach (var sysGroup in Enumerable.Range(0, b.Count)
            .Where(i => measureToSystem.ContainsKey(b[i].StartMeasureIndex))
            .GroupBy(i => measureToSystem[b[i].StartMeasureIndex]))
        {
            int sysIdx = sysGroup.Key;

            // One required anchor for the whole spanner: the highest (largest Y-up)
            // the occupancy demands across all of the system's brackets.
            // Frame = system-relative Y-up (system top = 0).
            double anchor = double.MinValue;
            foreach (int i in sysGroup)
            {
                var v = b[i];
                double required = trackers[sysIdx].Frontier(v.StartX, v.EndX)
                    + OutsideStaffPadding + VoltaBottom(v);
                // v.YUp is Y-up from the system top — the tracker frame — so it enters directly.
                anchor = Math.Max(anchor, Math.Max(v.YUp, required));
            }

            foreach (int i in sysGroup)
            {
                var v = b[i];
                // Write back the placed anchor as Y-up from the system top.
                b[i] = v with { YUp = anchor };
                trackers[sysIdx].AddRegion(v.StartX, v.EndX, anchor + 0.1);
            }
        }
        return b.ToImmutable();
    }

    // ---- 1500: MusicMark (rehearsal/section labels) ----
    private static ImmutableArray<MusicMarkLayout> PlaceMusicMarks(
        ImmutableArray<MusicMarkLayout> musicMarks, DirectionalOccupancy[] trackers,
        Dictionary<int, int> measureToSystem, ImmutableArray<SystemLayout> systems)
    {
        if (musicMarks.IsDefaultOrEmpty)
            return musicMarks;
        var b = musicMarks.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var m = b[i];
            if (!measureToSystem.TryGetValue(m.MeasureIndex, out int sysIdx))
                continue;
            // Spanner-handled marks (cresc./rit./ottava ...) are never
            // drawn by DrawMusicMarks — registering them would reserve
            // PHANTOM space and push real marks above thin air. Marks
            // placed below the staff don't belong to the above pass.
            // m.YUp is Y-up; a mark below the staff-top line (m.YUp < 2.0, the top line
            // sits 2 above the middle) is not part of this above pass. The stacker frame
            // is system-relative Y-up, so shift against the WITHIN-SYSTEM staff middle.
            double midUp = LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], m.StaffIndex) - 2.0;
            if (MusicMarkItem.IsSpannerHandled(m.MarkType) || m.YUp < 2.0)
                continue;
            var (x0, x1, top, bottom) = MusicMarkExtents(m);
            double newRel = Place(trackers[sysIdx], m.X + x0, m.X + x1,
                m.YUp + midUp, topOffset: top, bottomOffset: bottom);
            b[i] = m with { YUp = newRel - midUp };
        }
        return b.ToImmutable();
    }

    /// <summary>
    /// Ink extents of a music mark as X offsets from its anchor (X0..X1) plus
    /// vertical Top/Bottom, mirroring the renderer's DrawSingleMusicMark
    /// geometry. Boxed labels and glyph symbols are CENTERED (X0 = -X1); the
    /// metronome/tempo mark is LEFT-anchored — it draws rightward from m.X, so
    /// its extent is 0..fullWidth, and that full width must include the swing
    /// feel-equation drawn to its right (otherwise a beam/fermata sitting under
    /// the swing symbol is invisible to the stacker and the mark prints on it).
    /// </summary>
    private static (double X0, double X1, double Top, double Bottom) MusicMarkExtents(MusicMarkLayout m)
    {
        const double fontSize = 4.0; // renderer FontSize

        if (m.IsSymbol)
        {
            // Segno/Coda glyphs (U+E062/U+E064), centered on the anchor;
            // ink extents from the font bboxes.
            var box = m.MarkType == MusicMarkType.Segno
                ? GlyphMetrics.MarkSegno
                : GlyphMetrics.MarkCoda;
            double h = Math.Max(-box.Left, box.Right);
            return (-h, h, box.Top, -box.Bottom);
        }

        switch (m.MarkType)
        {
            case MusicMarkType.Rehearsal:
            case MusicMarkType.SectionLabel:
            {
                // Boxed bold text anchored at the box center.
                double fs = m.MarkType == MusicMarkType.Rehearsal
                    ? fontSize * 0.6
                    : fontSize * 0.55;
                const double pad = 0.2;
                double halfW = (SerifTextMetrics.MeasureBold(m.Text, fs) + 2 * pad) / 2;
                double halfH = (fs + 2 * pad) / 2;
                return (-halfW, halfW, halfH, halfH);
            }
            case MusicMarkType.Tempo:
            {
                double textW = SerifTextMetrics.MeasureBold("= " + m.Text, 1.8);
                if (m.SwingSubdivision == 0)
                {
                    // Non-swing metronome: the historical CENTERED estimate.
                    // Physically the mark is left-anchored, but this well-tuned
                    // width clears the line-start clef and reproduces every
                    // existing snapshot, so it is kept as-is.
                    double halfW = (1.1 + textW) / 2 + 0.6;
                    return (-halfW, halfW, 1.5, 0.5);
                }
                // Swing: the feel-equation ("♫ = ♩. ♪" under a triplet 3) is drawn
                // to the RIGHT of "= NNN", so the real ink reaches far past the
                // centered estimate and can sit over a beam or fermata. Use the
                // true LEFT-anchored span (mirrors CoPlaceTempoWithLabels' tempoW)
                // so the stacker lifts the whole mark clear of that content, and
                // the triplet bracket reaches a touch higher (~2sp) than the stem.
                double sw = 2.3 + textW;
                if (m.TempoText != null)
                    sw += SerifTextMetrics.MeasureBold(m.TempoText, 2.2) + 1.5;
                sw += 5.0;
                return (-0.2, sw, 2.0, 0.5);
            }
            default:
            {
                // Plain bold(-italic) text at 0.7 x 4sp, baseline anchor.
                double fs = fontSize * 0.7;
                double halfW = SerifTextMetrics.MeasureBold(m.Text, fs) / 2;
                return (-halfW, halfW, TextAscentEm * fs, TextDescentEm * fs);
            }
        }
    }

    /// <summary>
    /// Places one element: its edge nearest the staff must clear the current
    /// occupancy frontier by outside-staff-padding; the element only ever moves
    /// AWAY from the staff (larger Y-up above). <paramref name="topOffset"/> is the
    /// ascent above the anchor, <paramref name="bottomOffset"/> the descent below.
    /// Registers the element's extent and returns the new anchor.
    /// </summary>
    private static double Place(DirectionalOccupancy tracker, double x0, double x1,
        double anchorY, double topOffset, double bottomOffset)
    {
        double occupiedTop = tracker.Frontier(x0, x1);
        double required = occupiedTop + OutsideStaffPadding + bottomOffset;
        double newAnchor = Math.Max(anchorY, required);
        tracker.AddRegion(x0, x1, newAnchor + topOffset);
        return newAnchor;
    }

    /// <summary>
    /// Single interval-based occupancy skyline shared by both stacking
    /// directions, replacing the former mirror-image below/above trackers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc — outside-staff grobs are
    /// side-positioned against ONE accumulated skyline, parameterized by the
    /// stacking direction, rather than two hand-mirrored implementations.
    ///
    /// Coordinates here are system-relative Y-up (up-positive, the system top as
    /// origin). <c>_dir</c> = +1 stacks UP (above the staff: the frontier is the
    /// largest Y-up, the edge furthest above); <c>_dir</c> = -1 stacks DOWN (below
    /// the staff: the frontier is the smallest Y-up, the edge furthest below).
    /// <see cref="Frontier"/> returns the staff edge when no region overlaps.
    /// </remarks>
    private sealed class DirectionalOccupancy
    {
        private readonly List<(double startX, double endX, double edgeY)> _regions = new();
        private readonly double _staffEdge;
        private readonly int _dir;

        public DirectionalOccupancy(double staffEdge, int dir)
        {
            _staffEdge = staffEdge;
            _dir = dir;
        }

        /// <summary>
        /// Returns the occupied frontier Y-up in the stacking direction
        /// over the given X range (max for up, min for down), or the staff
        /// edge if no region overlaps.
        /// </summary>
        public double Frontier(double startX, double endX)
        {
            double frontier = _staffEdge;
            foreach (var (rStart, rEnd, edgeY) in _regions)
            {
                if (rStart < endX && rEnd > startX) // X overlap
                    frontier = _dir > 0
                        ? Math.Max(frontier, edgeY)
                        : Math.Min(frontier, edgeY);
            }
            return frontier;
        }

        public void AddRegion(double startX, double endX, double edgeY)
            => _regions.Add((startX, endX, edgeY));
    }
}
