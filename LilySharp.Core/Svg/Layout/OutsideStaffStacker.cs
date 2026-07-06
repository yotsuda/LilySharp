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
            ImmutableArray<TextSpannerLayout> textSpanners,
            ImmutableArray<ArticulationLayout> articulations = default,
            bool applyStaffOffsets = false)
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
        // occupy different vertical bands). Seeded lazily at StaffBottom + the
        // staff's within-system Y offset. A single staff (offset 0) reproduces the
        // former per-system tracker exactly, so single-staff output is unchanged.
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
                t = new DirectionalOccupancy(StaffBottom + off, dir: +1);
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
                // Glyph roughly centered on its anchor; half-extent ~0.6sp.
                Track(sysIdx, a.StaffIndex).AddRegion(a.X - 0.6, a.X + 0.6, a.Y + 0.6);
            }
        }

        // --- Priority 250: DynamicLineSpanner (dynamics + hairpins) ---
        // LILYPOND-REF: scm/define-grobs.scm:1270 DynamicLineSpanner.outside-staff-priority = 250

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
                double requiredY = occupied + OutsideStaffPadding + DynamicTextAscent;
                if (requiredY > dyn.Y)
                    dynBuilder[i] = dyn with { Y = requiredY };

                double bottom = dynBuilder[i].Y + DynamicTextDescent;
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
                double requiredY = occupiedBottom + OutsideStaffPadding + HairpinHalfHeight;
                double newY = Math.Max(hp.Y, requiredY);

                if (Math.Abs(newY - hp.Y) > 0.01)
                    builder[i] = hp with { Y = newY };

                // Register hairpin in tracker
                double finalBottom = builder[i].Y + HairpinHalfHeight;
                tracker.AddRegion(hp.StartX, hp.EndX, finalBottom);
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

                var tracker = Track(sysIdx, sp.StaffIndex);
                double occupiedBottom = tracker.Frontier(sp.StartX, sp.EndX);
                double requiredY = occupiedBottom + OutsideStaffPadding + TextSpannerAscent;
                double newY = Math.Max(sp.Y, requiredY);

                if (Math.Abs(newY - sp.Y) > 0.01)
                    builder[i] = sp with { Y = newY };

                // Register text spanner in tracker
                double finalBottom = builder[i].Y + TextSpannerDescent;
                tracker.AddRegion(sp.StartX, sp.EndX, finalBottom);
            }
            adjTextSpanners = builder.ToImmutable();
        }

        return (adjDynamics, adjHairpins, adjTextSpanners);
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
                   ImmutableArray<DynamicLayout> Dynamics)
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
            ImmutableArray<DynamicLayout> aboveDynamics = default)
    {
        if (systems.IsDefaultOrEmpty)
            return (trills, barNumbers, ottavas, customTexts, voltas, musicMarks, aboveDynamics);

        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        // UP trackers: smaller page Y = further above the staff. Occupancy
        // records the TOP edge of everything placed so far.
        var trackers = new DirectionalOccupancy[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            trackers[i] = new DirectionalOccupancy(systems[i].Y, dir: -1);
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
                        systems[i].Y + firstStaff.Y - clefProtrusion);
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
                double absY = systems[sysIdx].Y + a.Y;
                double inkTop = absY - a.Ink.Top;     // BBox Top is up-positive
                if (inkTop >= systems[sysIdx].Y)
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
                double sy = systems[sysIdx].Y;
                // Bracket line + the centered number's upper half
                // (number font = 0.6 x 4sp, cap height ~0.71em).
                double top = sy + Math.Min(tb.StartY, tb.EndY) - 0.71 * 2.4 / 2 - 0.1;
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
                // Lower-staff trills are already positioned over their own staff
                // by the engraver; the staff-0 seeded occupancy would wrongly pull
                // them back up to the top staff.
                if (t.StaffIndex != 0)
                    continue;
                if (!measureToSystem.TryGetValue(t.StartMeasureIndex, out int sysIdx))
                    continue;
                double sy = systems[sysIdx].Y;
                // anchor = "tr" glyph baseline; ink extent from the font bbox
                // (scripts.trill: 2.16sp above the baseline), wave +-0.25.
                double newAbs = Place(trackers[sysIdx],
                    t.GlyphX + GlyphMetrics.OrnTrillGlyph.Left, t.LineEndX,
                    sy + t.Y,
                    topOffset: -GlyphMetrics.OrnTrillGlyph.Top,
                    bottomOffset: 0.25);
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
                // Measured digit width; digits have no descenders, cap
                // height ~0.71em above the baseline anchor.
                double width = SerifTextMetrics.MeasureBold(bn.Text, BarNumberEngraver.FontSize);
                double x0 = bn.RightAligned ? bn.X - width : bn.X;
                double x1 = bn.RightAligned ? bn.X : bn.X + width;
                double newY = Place(trackers[sysIdx], x0, x1,
                    bn.Y, topOffset: -0.71 * BarNumberEngraver.FontSize, bottomOffset: 0.0);
                b[i] = bn with { Y = newY };
            }
            adjBarNumbers = b.ToImmutable();
        }

        // ---- 250: DynamicText forced ABOVE (@f.up) ----
        // LILYPOND-REF: scm/define-grobs.scm:1298 DynamicText.outside-staff-priority = 250
        // Below-staff dynamics are handled by StackBelowStaff; here the FORCED-above ones
        // stack outward from the staff and push higher-priority above-staff grobs (ottava,
        // marks, …) clear of them. Text ascends UP from its baseline anchor.
        var adjDynamics = aboveDynamics;
        if (!aboveDynamics.IsDefaultOrEmpty)
        {
            var b = aboveDynamics.ToBuilder();
            for (int i = 0; i < b.Count; i++)
            {
                var dyn = b[i];
                if (!dyn.IsAbove || !measureToSystem.TryGetValue(dyn.MeasureIndex, out int sysIdx))
                    continue;
                double newAbs = Place(trackers[sysIdx],
                    dyn.X - DynamicHalfWidth, dyn.X + DynamicHalfWidth,
                    systems[sysIdx].Y + dyn.Y,
                    topOffset: -DynamicTextAscent, bottomOffset: 0.0);
                b[i] = dyn with { Y = newAbs - systems[sysIdx].Y };
            }
            adjDynamics = b.ToImmutable();
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
                // Lower-staff brackets are already placed over their OWN staff by
                // OttavaBracketEngraver (via staffYByIndex); the staff-0 seeded
                // occupancy would wrongly pull them up to the top staff. Same
                // treatment as lower-staff trills above.
                if (o.StaffIndex != 0)
                    continue;
                double sy = systems[sysIdx].Y;
                // anchor = text baseline / line Y; "8va" at 0.45 x 4sp with
                // ~0.75em ascent; the end hook drops EdgeHeight below.
                double newAbs = Place(trackers[sysIdx], o.StartX, o.EndX,
                    sy + o.Y,
                    topOffset: -0.75 * (0.45 * 4.0),
                    bottomOffset: Math.Max(0.1, o.EdgeHeight));
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
                // Centered italic text at 0.6 x 4sp; measured width (bold
                // table, a slight overestimate for italic), ~0.75em ascent
                // and ~0.25em descent around the baseline anchor.
                const double ctFs = 0.6 * 4.0;
                double halfWidth = SerifTextMetrics.MeasureBold(ct.Text, ctFs) / 2;
                double newAbs = Place(trackers[sysIdx], ct.X - halfWidth, ct.X + halfWidth,
                    sy + ct.Y, topOffset: -0.75 * ctFs, bottomOffset: 0.25 * ctFs);
                b[i] = ct with { Y = newAbs - sy };
            }
            adjCustomTexts = b.ToImmutable();
        }

        // ---- 600: VoltaBracketSpanner ----
        // LilyPond's outside-staff grob here is the SPANNER — an axis group
        // holding ALL volta brackets of a system — so consecutive endings
        // share ONE side-positioned Y per system instead of each bracket
        // finding its own height over its own bars.
        // LILYPOND-REF: scm/define-grobs.scm VoltaBracketSpanner —
        //   (axes . (Y)) (outside-staff-priority . 600) (side-axis . Y).
        var adjVoltas = voltas;
        if (!voltas.IsDefaultOrEmpty)
        {
            double VoltaBottom(VoltaBracketLayout v)
            {
                // anchor = bracket line. Hooks drop EdgeHeight; the volta
                // number hangs from line+0.3 at 0.6 x 4sp (renderer
                // geometry), so the deeper of the two bounds the extent.
                double textDepth = string.IsNullOrEmpty(v.VoltaText)
                    ? 0
                    : 0.3 + 0.75 * (0.6 * 4.0);
                return Math.Max(VoltaBracketEngraver.GetEdgeHeight(), textDepth);
            }

            var b = voltas.ToBuilder();
            foreach (var sysGroup in Enumerable.Range(0, b.Count)
                .Where(i => measureToSystem.ContainsKey(b[i].StartMeasureIndex))
                .GroupBy(i => measureToSystem[b[i].StartMeasureIndex]))
            {
                int sysIdx = sysGroup.Key;
                double sy = systems[sysIdx].Y;

                // One required anchor for the whole spanner: the highest
                // (smallest page Y) the occupancy demands across all of the
                // system's brackets.
                double anchor = double.MaxValue;
                foreach (int i in sysGroup)
                {
                    var v = b[i];
                    double required = trackers[sysIdx].Frontier(v.StartX, v.EndX)
                        - OutsideStaffPadding - VoltaBottom(v);
                    anchor = Math.Min(anchor, Math.Min(sy + v.Y, required));
                }

                foreach (int i in sysGroup)
                {
                    var v = b[i];
                    b[i] = v with { Y = anchor - sy };
                    trackers[sysIdx].AddRegion(v.StartX, v.EndX, anchor - 0.1);
                }
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
                // Spanner-handled marks (cresc./rit./ottava ...) are never
                // drawn by DrawMusicMarks — registering them would reserve
                // PHANTOM space and push real marks above thin air. Marks
                // placed below the staff don't belong to the above pass.
                if (MusicMarkItem.IsSpannerHandled(m.MarkType) || m.Y > 0)
                    continue;
                double sy = systems[sysIdx].Y;
                var (halfWidth, top, bottom) = MusicMarkExtents(m);
                double newAbs = Place(trackers[sysIdx], m.X - halfWidth, m.X + halfWidth,
                    sy + m.Y, topOffset: top, bottomOffset: bottom);
                b[i] = m with { Y = newAbs - sy };
            }
            adjMarks = b.ToImmutable();
        }

        return (adjTrills, adjBarNumbers, adjOttavas, adjCustomTexts, adjVoltas, adjMarks, adjDynamics);
    }

    /// <summary>
    /// Extents of a music mark around its anchor, mirroring the renderer's
    /// DrawSingleMusicMark geometry (boxed labels are anchored at the box
    /// CENTER; plain text marks at the baseline).
    /// </summary>
    private static (double HalfWidth, double Top, double Bottom) MusicMarkExtents(MusicMarkLayout m)
    {
        const double fontSize = 4.0; // renderer FontSize

        if (m.IsSymbol)
        {
            // Segno/Coda glyphs (U+E062/U+E064), centered on the anchor;
            // ink extents from the font bboxes.
            var box = m.MarkType == MusicMarkType.Segno
                ? GlyphMetrics.MarkSegno
                : GlyphMetrics.MarkCoda;
            return (Math.Max(-box.Left, box.Right), -box.Top, -box.Bottom);
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
                return (halfW, -halfH, halfH);
            }
            case MusicMarkType.Tempo:
            {
                // Metronome: notehead + stem (reaching ~1.5sp up) + "= NNN".
                double textW = SerifTextMetrics.MeasureBold("= " + m.Text, 1.8);
                return ((1.1 + textW) / 2 + 0.6, -1.5, 0.5);
            }
            default:
            {
                // Plain bold(-italic) text at 0.7 x 4sp, baseline anchor.
                double fs = fontSize * 0.7;
                double halfW = SerifTextMetrics.MeasureBold(m.Text, fs) / 2;
                return (halfW, -0.75 * fs, 0.25 * fs);
            }
        }
    }

    /// <summary>
    /// Places one element: its BOTTOM edge must clear the current occupancy
    /// top by outside-staff-padding; the element only ever moves AWAY from
    /// the staff. Registers the element's extent and returns the new anchor.
    /// </summary>
    private static double Place(DirectionalOccupancy tracker, double x0, double x1,
        double anchorY, double topOffset, double bottomOffset)
    {
        double occupiedTop = tracker.Frontier(x0, x1);
        double required = occupiedTop - OutsideStaffPadding - bottomOffset;
        double newAnchor = Math.Min(anchorY, required);
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
    /// Coordinates here are device Y (Y-down). <c>_dir</c> = +1
    /// stacks DOWN (below the staff: the frontier is the largest device Y, the
    /// edge furthest below); <c>_dir</c> = -1 stacks UP (above the
    /// staff: the frontier is the smallest device Y, the edge furthest above).
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
        /// Returns the occupied frontier device-Y in the stacking direction
        /// over the given X range (max for down, min for up), or the staff
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
