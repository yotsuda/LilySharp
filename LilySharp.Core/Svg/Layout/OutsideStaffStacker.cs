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

    // LILYPOND-REF: scm/define-grobs.scm:3806 TextScript outside-staff-horizontal-padding
    // = 0.2 — the same 0.2 is declared by the mark family (RehearsalMark:2887,
    // SectionLabel:3055, SegnoMark:3094, CodaMark:1012, TextMark:3772, JumpScript:1909,
    // MetronomeMark:2345). avoid_outside_staff_collisions passes it into
    // Skyline::distance, which pads the profile flat + 45° by that much
    // (lily/skyline.cc:558-615 Skyline::padded, the horizon_padding argument) — the
    // plateau this creates is why
    // LilyPond's poco-over-mum step equals the box arithmetic to 1.6e-5 (ledger
    // textscript.stacked.box-step): the padding covers the m-arch's slope under the
    // descender. Grobs that do NOT declare it (BarNumber, TrillSpanner, TextSpanner,
    // OttavaBracket, DynamicText, VoltaBracketSpanner) take the 0.0 default.
    private const double OutsideStaffHorizontalPadding = 0.2;

    // The per-grob side-position declarations (padding / staff-padding / the trill's
    // stencil-offset reach) live in ONE home, EngravingDefaults' outside-staff
    // declaration table — TextScript's 0.5 floor, the trill's 0.5 + 1.0, the text
    // spanner's 0.8 and DynamicLineSpanner's 0.6 are all read from there. How each is
    // consumed: aligned_side floors the grob's REFPOINT at staff ink + staff-padding
    // (lily/side-position-interface.cc:401-453, applied BEFORE the outside-staff
    // pass — see PlaceCustomTexts / PlaceTextSpanners), and declaring staff-padding
    // also puts the STAFF EXTENT into the support (include_staff, :219-222 and
    // :323-330), over which a grob pays its OWN padding where it declares one
    // (PlaceTrills; the trill engraver's quiet height is the staff-extent case).
    private const double TextScriptStaffPadding = EngravingDefaults.TextScriptStaffPadding;

    // The gap from the note/staff skyline to a below-staff dynamic or hairpin is the
    // DynamicLineSpanner's own side-position padding, NOT outside-staff-padding.
    private const double DynamicLineSpannerPadding = EngravingDefaults.DynamicLineSpannerPadding;

    // A dynamic's own ink comes from the font, per label — DynamicEngraver.InkOf. There is
    // no constant here any more: LilyPond's DynamicText extent IS the drawn glyphs' ink,
    // so `p` descends below its baseline and `m` does not. The 1.2 / 0.3 pair that used to
    // live here was a nominal box for a glyph 2.588 tall, and cited
    // define-grobs.scm:1450 — which is the Y-offset (-0.6) inside the line spanner, not an
    // ascent. A LILYPOND-REF beside a number is not evidence that the number came from it.

    // LILYPOND-REF: scm/define-grobs.scm:1785 Hairpin height = 0.6666
    private const double HairpinHalfHeight = 0.6666 / 2.0;

    // Dynamic text half-width estimate for X collision range
    private const double DynamicHalfWidth = 0.75;

    // A text grob's vertical extent comes from the FACE, per string, at the size and style
    // the draw uses (TextFontMetrics.Ink) — never from a letter-class fraction of the em.
    // LILYPOND-REF: scm/define-grobs.scm:3800-3833 TextScript (the outside-staff-priority block) —
    // its Y-extent is grob::always-Y-extent-from-stencil, its stencil ly:text-interface::print, so
    // what outside_staff_axis_group clears is the TEXT'S ink; the same declaration pattern
    // covers the tuplet number, the ottava label and the mark texts this file stacks.
    // The letter-class trio that used to live here (CapHeightEm 0.71 / TextAscentEm 0.75 /
    // TextDescentEm 0.25, "no single LP grob source") priced "dolce" and "poco" identically;
    // the dedicated pair (audit/lp-geometry/probes/textscript-ink.ly, ledger textscript.*)
    // measured LilyPond's baseline RIDING the p's own descender, 0.404430 apart.

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
                        map[st.StaffIndex] = -st.Y;   // Y-up storage → device-down offset
            staffYBySystem.Add(map);
        }

        // Occupancy PER (system, staff): each staff's below-staff column stacks down
        // from ITS OWN bottom, so a hairpin under staff 2 is not pushed by staff 1's
        // dynamics (they share the single Dynamics/Hairpin tables but occupy
        // different vertical bands).
        // FRAME: system-relative Y-up (up-positive, the SYSTEM TOP = 0), the native
        // LP frame the grobs store — below the staff is negative Y-up. The support
        // starts as the staff bottom's flat edge at Y-up = -(StaffBottom + within-
        // system offset); dir=-1 stacks DOWNWARD (moves ≤ 0). A single staff
        // (offset 0) reproduces the former per-system tracker exactly.
        var trackers = new Dictionary<(int Sys, int Staff), OutsideStaffSkylines>();
        OutsideStaffSkylines Track(int sys, int staff)
        {
            if (!trackers.TryGetValue((sys, staff), out var t))
            {
                // Only apply per-staff offsets in the final annotation pass (the
                // prelim/single-staff passes supply no staff-Y and stack from a zero
                // baseline, so gating on this keeps their extent estimate unchanged).
                double off = applyStaffOffsets && sys >= 0 && sys < staffYBySystem.Count
                    && staffYBySystem[sys].TryGetValue(staff, out var so) ? so : 0;
                double edge = -(StaffBottom + off);
                // allowPockets: false — this support has no note-column DOWN ink yet
                // (see the flag's remark); placement stays monotone below.
                t = new OutsideStaffSkylines(dir: -1,
                    FlatBase(edge, VerticalDirection.Up),
                    FlatBase(edge, VerticalDirection.Down),
                    allowPockets: false);
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
                // Support ink: only the DOWN side carries information for a
                // below-staff stack (the up side stays the staff-edge base).
                Track(sysIdx, a.StaffIndex).MergeSupport(down: VerticalSkyline.FromBox(
                    a.X - 0.6, a.X + 0.6, aYup - 0.6, aYup + 0.6, VerticalDirection.Down));
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
                var (ascent, descent) = DynamicEngraver.InkOf(dyn.Text, dyn.IsExpressiveText);
                var tracker = Track(sysIdx, dyn.StaffIndex);
                // System-relative Y-up: the grob's dyn.YUp (above the staff middle) sits
                // at dyn.YUp - off - 2; the placement only ever moves it AWAY (down), and
                // any push reflects back to the staff-middle frame the grob stores (+ off + 2).
                double off = applyStaffOffsets && sysIdx >= 0 && sysIdx < staffYBySystem.Count
                    && staffYBySystem[sysIdx].TryGetValue(dyn.StaffIndex, out var so) ? so : 0;
                double dynYup = dyn.YUp - off - 2.0;
                // Box pair about the baseline anchor: ascent above, descent below. The
                // dynamic glyphs' ink is per label (DynamicEngraver.InkOf) but stays a
                // BOX — no baked outline exists for the dynamic glyphs yet; named, not
                // hidden (the text grobs above the staff carry real outlines).
                double move = tracker.Place(
                    VerticalSkyline.FromBox(xStart, xEnd,
                        dynYup - descent, dynYup + ascent, VerticalDirection.Up),
                    VerticalSkyline.FromBox(xStart, xEnd,
                        dynYup - descent, dynYup + ascent, VerticalDirection.Down),
                    DynamicLineSpannerPadding);
                if (move != 0)
                    dynBuilder[i] = dyn with { YUp = dynYup + move + off + 2.0 };
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
                // hp.YUp (the CENTRE) is already Y-up from the system top — the tracker
                // frame — so it enters directly; the box spans half a height each way.
                double move = tracker.Place(
                    VerticalSkyline.FromBox(hp.StartX, hp.EndX,
                        hp.YUp - HairpinHalfHeight, hp.YUp + HairpinHalfHeight,
                        VerticalDirection.Up),
                    VerticalSkyline.FromBox(hp.StartX, hp.EndX,
                        hp.YUp - HairpinHalfHeight, hp.YUp + HairpinHalfHeight,
                        VerticalDirection.Down),
                    DynamicLineSpannerPadding);
                if (move != 0)
                    builder[i] = hp with { YUp = hp.YUp + move };
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
    private static OutsideStaffSkylines[] SeedAboveTrackers(
        ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<TupletBracketLayout> tupletBrackets,
        Dictionary<int, int> measureToSystem)
    {
        // UP stacking: larger Y-up = further above the staff.
        // FRAME: system-relative Y-up (up-positive, the SYSTEM TOP = 0), the native
        // LP frame the grobs store — above the staff is positive Y-up. Every
        // seed/box/grob Y below is measured from the system top, and every grob
        // writes back its unchanged system-relative YUp, so the stacker reads no
        // absolute SystemLayout.Y — decoupled for the Stage-4 W2 stacking-origin flip.
        var trackers = new OutsideStaffSkylines[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            // The support starts at the flat staff-top base (Y-up 0) and merges the
            // system's up-skyline RAW — sloped buildings (beams) keep their slopes,
            // where the interval tracker used to flatten each building at its
            // midpoint. The base plays the old "max(0, h)" clamp pointwise. The
            // support's DOWN side stays the flat base: for an UP stack it only
            // enters the forbidden intervals' lower bounds, which positive moves
            // never reach.
            var supportUp = FlatBase(0.0, VerticalDirection.Up);
            trackers[i] = new OutsideStaffSkylines(dir: +1,
                supportUp, FlatBase(0.0, VerticalDirection.Down));
            if (systemSkylines != null && i < systemSkylines.Count
                && !systemSkylines[i].up.IsEmpty)
            {
                supportUp.Merge(systemSkylines[i].up);
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
                    // staff.Y is Y-up ⇒ the TOP staff is the LARGEST.
                    .OrderByDescending(s => s.Y)
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
                    double top = clefProtrusion + firstStaff.Y;
                    trackers[i].MergeSupport(up: VerticalSkyline.FromBox(
                        clefX + clefBox.Left, clefX + clefBox.Right,
                        top, top, VerticalDirection.Up));
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
                trackers[sysIdx].MergeSupport(up: VerticalSkyline.FromBox(
                    a.X + a.Ink.Left, a.X + a.Ink.Right, inkTop, inkTop,
                    VerticalDirection.Up));
            }
        }

        // Priority 200: above-staff tuplet brackets/numbers. They are bound
        // to their beams in this model, so they seed the occupancy without
        // being moved themselves. The geometry mirrors what is DRAWN — and what
        // SkylineBuilder.AddTupletBracketsToSkyline already reserves in the staff
        // skylines: the bracket line's outward edge, and the number's own ink
        // centred on the bracket midpoint.
        // LILYPOND-REF: lily/tuplet-number.cc:342 calc_y_offset — the bracket midpoint —
        // and :227-228 print, which centers the number's stencil on X and Y.
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var tb in tupletBrackets)
            {
                if (!tb.IsStemUp || !measureToSystem.TryGetValue(tb.MeasureIndex, out int sysIdx))
                    continue;
                // A fully beamed tuplet draws no bracket (bracket-visibility =
                // if-no-beam); its number still prints, riding the beam.
                if (tb.ShowBracket)
                {
                    double lineTop = Math.Max(tb.StartYUp, tb.EndYUp)
                        + EngravingDefaults.TupletBracketThickness / 2.0;
                    trackers[sysIdx].MergeSupport(up: VerticalSkyline.FromBox(
                        tb.StartX, tb.EndX, lineTop, lineTop, VerticalDirection.Up));
                }
                if (!string.IsNullOrEmpty(tb.NumberText))
                {
                    double fs = TupletBracketEngraver.NumberFontSize;
                    double halfW = TextFontMetrics.Advance(
                        tb.NumberText, fs, sans: false, TupletBracketEngraver.NumberFontStyle) / 2;
                    double halfH = TextFontMetrics.InkHeight(
                        tb.NumberText, fs, sans: false, TupletBracketEngraver.NumberFontStyle) / 2;
                    trackers[sysIdx].MergeSupport(up: VerticalSkyline.FromBox(
                        tb.NumberX - halfW, tb.NumberX + halfW,
                        tb.NumberYUp + halfH, tb.NumberYUp + halfH, VerticalDirection.Up));
                }
            }
        }

        return trackers;
    }

    // ---- 50: TrillSpanner ----
    private static ImmutableArray<TrillSpannerLayout> PlaceTrills(
        ImmutableArray<TrillSpannerLayout> trills, OutsideStaffSkylines[] trackers,
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
            // anchor = the LINE. The grob's ink about it: the "tr" glyph rides
            // stencil-offset (0 . -1), so on a glyph-bearing piece the pair spans
            // (line - reach .. line + glyphTop - reach) — LilyPond's own ext dump reads
            // (-1.0 . 1.1) — and a glyphless continuation carries just the wave
            // (draw amplitude 0.2 + half thickness). The side padding is the trill's
            // OWN declared 0.5, not outside-staff-padding: the trill is the
            // first-placed outside-staff grob, so its only counterpart here IS the
            // support, which aligned_side clears by the grob's padding (ledger
            // trill.support.staff-to-line = box top + 0.5 + 1.0, exact). ⚠️ Named
            // approximation: a support building that is NOT a side-support column
            // (a beam, a script) would be cleared at 0.46 by LilyPond's outside-staff
            // pass; this single-pass tracker pays 0.5 against everything.
            bool hasGlyph = t.GlyphX < t.LineStartX;
            double wave = EngravingDefaults.TrillWaveAmplitude
                + EngravingDefaults.StaffLineThickness / 2.0;
            double reach = hasGlyph
                ? EngravingDefaults.TrillSpannerTextOffsetDown
                : wave;
            double top = hasGlyph
                ? GlyphMetrics.OrnTrillGlyph.Top - EngravingDefaults.TrillSpannerTextOffsetDown
                : wave;
            // X reach: the glyph's TRUE (outline) left, not its bounding box —
            // LilyPond wraps the bound text in make-with-true-dimension-markup exactly
            // because "the trill glyph has a loop on its left, which sticks out of its
            // bounding box" (its own comment).
            // LILYPOND-REF: scm/define-grobs.scm:4056-4066 TrillSpanner bound-details,
            //   make-with-true-dimension-markup on scripts.trill
            double x0 = hasGlyph
                ? t.GlyphX + GlyphMetrics.OrnTrillGlyphOutline.Left
                : t.LineStartX;
            double newRel = Place(trackers[sysIdx],
                x0, t.LineEndX,
                t.YUp,
                topOffset: top,
                bottomOffset: reach,
                padding: EngravingDefaults.TrillSpannerPadding);
            b[i] = t with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 100: BarNumber (absolute page Y) ----
    private static ImmutableArray<BarNumberLayout> PlaceBarNumbers(
        ImmutableArray<BarNumberLayout> barNumbers, OutsideStaffSkylines[] trackers,
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
            // The digits' OWN ink about their baseline, both ways, from the face — the same
            // call the width beside it already used, and the same one the paging skyline
            // makes. It said "digits have no descenders, cap height ~0.71em" until
            // 2026-07-28, and BOTH halves of that were wrong: a round digit OVERSHOOTS its
            // baseline, and the cap height is the face's, not a tuning.
            // LILYPOND-REF: scm/define-grobs.scm BarNumber — side-axis Y, direction UP,
            // padding 1.0, and its stencil is ly:text-interface::print, so what
            // outside_staff_axis_group clears is the TEXT's ink and not a designed box
            // (lily/grob.cc:85-89 simple_vertical_skylines_from_extents).
            // MEASURED (audit/lp-geometry/probes/page-vertical.ly, books BNL/BNH): LilyPond
            // puts the number's ink bottom at 3.050000 over the staff refpoint — which is
            // the staff line's own ink 2.050000 plus that padding of 1.0, exactly — and its
            // BASELINE at 3.076208. The 0.026208 between them is the overshoot this used to
            // reserve as zero, and it is the whole of barnumber.*.staff-to-baseline.
            // The digits' OUTLINE profiles, baseline-anchored at the drawn origin —
            // BarNumber's vertical-skylines come from its stencil like every text grob
            // (grob::always-vertical-skylines-from-stencil), so the pair replaces the
            // ink box the interval tracker held.
            double width = TextFontMetrics.SerifBold(bn.Text, BarNumberEngraver.FontSize);
            double originX = bn.RightAligned ? bn.X - width : bn.X;
            // System-relative Y-up: bn.YUp is Y-up from the system top, entering directly.
            var (bnUp, bnDown) = TextOutlineSkylines.Place(
                bn.Text, BarNumberEngraver.FontSize, sans: false, FontStyle.Bold,
                originX, bn.YUp);
            double move = trackers[sysIdx].Place(bnUp, bnDown, OutsideStaffPadding);
            b[i] = bn with { YUp = bn.YUp + move };
        }
        return b.ToImmutable();
    }

    // ---- 250: DynamicText forced ABOVE (@f.up) ----
    // LILYPOND-REF: scm/define-grobs.scm:1298 DynamicText.outside-staff-priority = 250
    // Below-staff dynamics are handled by StackBelowStaff; here the FORCED-above ones
    // stack outward from the staff and push higher-priority above-staff grobs (ottava,
    // marks, …) clear of them. Text ascends UP from its baseline anchor.
    private static ImmutableArray<DynamicLayout> PlaceAboveDynamics(
        ImmutableArray<DynamicLayout> aboveDynamics, OutsideStaffSkylines[] trackers,
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
            var (ascent, _) = DynamicEngraver.InkOf(dyn.Text, dyn.IsExpressiveText);
            double newRel = Place(trackers[sysIdx],
                dyn.X - DynamicHalfWidth, dyn.X + DynamicHalfWidth,
                dyn.YUp + midUp,
                topOffset: ascent, bottomOffset: 0.0);
            b[i] = dyn with { YUp = newRel - midUp };
        }
        return b.ToImmutable();
    }

    // ---- 350: TextSpanner (accel./rit. — LilyPond TextSpanner direction=UP) ----
    // LILYPOND-REF: scm/define-grobs.scm TextSpanner (direction . UP),
    //   (outside-staff-priority . 350). Placed above the staff, clearing the
    //   up-skyline, instead of below where it hit low notes.
    private static ImmutableArray<TextSpannerLayout> PlaceTextSpanners(
        ImmutableArray<TextSpannerLayout> textSpanners, OutsideStaffSkylines[] trackers,
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
            // aligned_side's staff-padding refpoint floor, applied to the anchor (the
            // LINE) BEFORE the collision pass — the same order PlaceCustomTexts uses.
            // With no declared side padding (side-position's default 0.0) and a facing
            // reach of just the dash half-thickness, this floor is what stands on a
            // quiet staff (ledger textspanner.floor.staff-to-line = 2.05 + 0.8, exact;
            // the old anchor was staff edge + 0.46 + an invented 0.3 box descent).
            // LILYPOND-REF: lily/side-position-interface.cc:401-453 aligned_side —
            //   staff_padding; :361-363 padding default 0.0.
            double anchor = Math.Max(ts.YUp,
                EngravingDefaults.StaffLineThickness / 2.0
                + EngravingDefaults.TextSpannerStaffPadding);
            // The drawn ink about the line: the dashed rule's half thickness both
            // ways, widened by the text's own ink on the piece that carries it — the
            // same face, size and style DrawTextSpanners draws (serif italic at
            // 4.0 × 0.5). For "rit."/"accel." the descender side stays the line's
            // half thickness, which is LilyPond's facing reach here (ledger
            // textspanner.support.staff-to-line = box top + 0.46 + 0.05, exact;
            // the invented TextSpannerDescent 0.3 box was that entry's +0.25).
            double lineHalf = EngravingDefaults.StaffLineThickness / 2.0;
            double top = lineHalf, bottom = lineHalf;
            if (!string.IsNullOrEmpty(ts.Text))
            {
                var ink = TextFontMetrics.Ink(ts.Text, 4.0 * 0.5, sans: false, FontStyle.Italic);
                top = Math.Max(top, ink.Top);
                bottom = Math.Max(bottom, -ink.Bottom);
            }
            double newRel = Place(trackers[sysIdx], ts.StartX, ts.EndX,
                anchor,
                topOffset: top, bottomOffset: bottom);
            b[i] = ts with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 400: OttavaBracket (above-staff only) ----
    private static ImmutableArray<OttavaBracketLayout> PlaceOttavas(
        ImmutableArray<OttavaBracketLayout> ottavas, OutsideStaffSkylines[] trackers,
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
            // anchor = text baseline / line Y; the label's ascent is its own ink at the
            // size and face the draw uses (DrawOttavaBrackets: BoldItalic at 0.45 x 4sp);
            // the end hook drops EdgeHeight below.
            double newRel = Place(trackers[sysIdx], o.StartX, o.EndX,
                o.YUp,
                topOffset: TextFontMetrics.Ink(
                    o.Text, 0.45 * 4.0, sans: false, FontStyle.BoldItalic).Top,
                bottomOffset: Math.Max(0.1, o.EdgeHeight));
            b[i] = o with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 450: TextScript (^"...") ----
    private static ImmutableArray<CustomTextLayout> PlaceCustomTexts(
        ImmutableArray<CustomTextLayout> customTexts, OutsideStaffSkylines[] trackers,
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
            // Italic text at TextScriptFontSize — the same face, size and style
            // DrawCustomTexts draws. The grob's pair is the string's OUTLINE profiles
            // (TextScript declares vertical-skylines from the stencil), placed at the
            // drawn PEN ORIGIN — ct.X itself, the Start-anchored draw's start (X-offset
            // 0 about the note column; ledger textscript.x.pen-to-notehead-left). This
            // is what lands a descender over a neighbour's bowls the way
            // avoid_outside_staff_collisions does (ledger textscript.stacked.outline-step).
            double ctFs = EngravingDefaults.TextScriptFontSize;
            // Stack in system-relative Y-up: ct.YUp relative to this staff's WITHIN-
            // SYSTEM middle is ct.YUp + midUp; place, then shift back.
            double midUp = LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], ct.StaffIndex) - 2.0;
            // The staff-padding refpoint floor, applied to the anchor BEFORE the
            // collision pass — aligned_side runs before the outside-staff pass, so the
            // 0.46 raise starts FROM the floored baseline and the entries register the
            // ink where it lands. The floor is against the STAFF's own ink edge
            // (2.0 + half a line), not the accumulated skylines — that is what "on a
            // row" in aligned_side's comment means. See TextScriptStaffPadding.
            double staffPaddingFloor = midUp
                + (2.0 + EngravingDefaults.StaffLineThickness / 2.0) + TextScriptStaffPadding;
            double anchor = Math.Max(ct.YUp + midUp, staffPaddingFloor);
            var (ctUp, ctDown) = TextOutlineSkylines.Place(
                ct.Text, ctFs, sans: false, FontStyle.Italic, ct.X, anchor);
            double move = trackers[sysIdx].Place(ctUp, ctDown, OutsideStaffPadding,
                OutsideStaffHorizontalPadding);
            b[i] = ct with { YUp = anchor + move - midUp };
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
        ImmutableArray<VoltaBracketLayout> voltas, OutsideStaffSkylines[] trackers,
        Dictionary<int, int> measureToSystem)
    {
        if (voltas.IsDefaultOrEmpty)
            return voltas;
        double VoltaBottom(VoltaBracketLayout v)
        {
            // anchor = bracket line. Hooks drop EdgeHeight; the volta number hangs from
            // 0.3 below the line with VerticalAnchor.Hanging ("y is the top of the glyph
            // extents"), so its ink reaches its own HEIGHT further down — measured from
            // the face at the size and style the draw uses (DrawVoltaBrackets: Bold at
            // 0.6 x 4sp). The deeper of the two bounds the extent.
            double textDepth = string.IsNullOrEmpty(v.VoltaText)
                ? 0
                : 0.3 + TextFontMetrics.InkHeight(
                    v.VoltaText, 0.6 * 4.0, sans: false, FontStyle.Bold);
            return Math.Max(VoltaBracketEngraver.GetEdgeHeight(), textDepth);
        }

        var b = voltas.ToBuilder();
        foreach (var sysGroup in Enumerable.Range(0, b.Count)
            .Where(i => measureToSystem.ContainsKey(b[i].StartMeasureIndex))
            .GroupBy(i => measureToSystem[b[i].StartMeasureIndex]))
        {
            int sysIdx = sysGroup.Key;

            // The spanner is ONE grob: merge every bracket's extent into one pair at
            // the shared starting anchor (the highest engraver anchor in the group),
            // place it once, and the move applies to all brackets together.
            // Frame = system-relative Y-up (system top = 0); v.YUp enters directly.
            double anchor0 = double.MinValue;
            foreach (int i in sysGroup)
                anchor0 = Math.Max(anchor0, b[i].YUp);

            var spanUp = new VerticalSkyline(VerticalDirection.Up);
            var spanDown = new VerticalSkyline(VerticalDirection.Down);
            foreach (int i in sysGroup)
            {
                var v = b[i];
                // Per bracket the extent stays the box the interval tracker held —
                // line top at anchor + 0.1, hooks/number depth below — but the
                // SPANNER's profile is their pointwise union across the system.
                spanUp.Merge(VerticalSkyline.FromBox(v.StartX, v.EndX,
                    anchor0 - VoltaBottom(v), anchor0 + 0.1, VerticalDirection.Up));
                spanDown.Merge(VerticalSkyline.FromBox(v.StartX, v.EndX,
                    anchor0 - VoltaBottom(v), anchor0 + 0.1, VerticalDirection.Down));
            }

            double anchor = anchor0 + trackers[sysIdx].Place(spanUp, spanDown, OutsideStaffPadding);

            foreach (int i in sysGroup)
                b[i] = b[i] with { YUp = anchor };
        }
        return b.ToImmutable();
    }

    // ---- 1500: MusicMark (rehearsal/section labels) ----
    private static ImmutableArray<MusicMarkLayout> PlaceMusicMarks(
        ImmutableArray<MusicMarkLayout> musicMarks, OutsideStaffSkylines[] trackers,
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
            // Plain-text marks (D.S./Fine/pedal words/…) carry their string's OUTLINE
            // pair like every other stencil-skylined text grob; boxed labels ARE drawn
            // boxes, tempo marks mix glyphs and text, and segno/coda are glyphs with no
            // baked outline — those keep their box extents (named, not hidden).
            if (!m.IsSymbol && m.MarkType is not (MusicMarkType.Rehearsal
                or MusicMarkType.SectionLabel or MusicMarkType.Tempo))
            {
                double fs = 4.0 * 0.7; // renderer FontSize * plain-text factor
                var style = m.MarkType is MusicMarkType.SustainOn or MusicMarkType.SustainOff
                    ? FontStyle.Bold
                    : FontStyle.BoldItalic;
                double halfW = TextFontMetrics.Advance(m.Text, fs, sans: false, style) / 2;
                var (mUp, mDown) = TextOutlineSkylines.Place(
                    m.Text, fs, sans: false, style, m.X - halfW, m.YUp + midUp);
                double mMove = trackers[sysIdx].Place(mUp, mDown, OutsideStaffPadding,
                    OutsideStaffHorizontalPadding);
                b[i] = m with { YUp = m.YUp + mMove };
                continue;
            }
            var (x0, x1, top, bottom) = MusicMarkExtents(m);
            // The whole mark family declares the horizontal 0.2 (see the constant).
            double newRel = Place(trackers[sysIdx], m.X + x0, m.X + x1,
                m.YUp + midUp, topOffset: top, bottomOffset: bottom,
                horizonPadding: OutsideStaffHorizontalPadding);
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
                double halfW = (TextFontMetrics.SerifBold(m.Text, fs) + 2 * pad) / 2;
                double halfH = (fs + 2 * pad) / 2;
                return (-halfW, halfW, halfH, halfH);
            }
            case MusicMarkType.Tempo:
            {
                double textW = TextFontMetrics.SerifBold("= " + m.Text, 1.8);
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
                    sw += TextFontMetrics.SerifBold(m.TempoText, 2.2) + 1.5;
                sw += 5.0;
                return (-0.2, sw, 2.0, 0.5);
            }
            default:
            {
                // Plain text marks (D.S./Fine/pedal/…), baseline anchor at 0.7 x 4sp.
                // ⚠️ PlaceMusicMarks routes these through their OUTLINE pair before
                // reaching this method, so this arm is not hit from the stacker any
                // more; it stays as the box description of the same geometry (the
                // sizes/styles here and there must not drift apart).
                // Both extents are the string's own metrics at the style the draw picks
                // (DrawSingleMusicMark: BoldItalic, except the sustain-pedal words,
                // which stay upright Bold): ink about the baseline vertically, the
                // advance horizontally — LilyPond has no "estimated" widths, a mark's
                // X extent is its markup stencil's. (To-Coda still prices its text
                // only; the coda glyph beside it stays an unreserved approximation.)
                double fs = fontSize * 0.7;
                var style = m.MarkType is MusicMarkType.SustainOn or MusicMarkType.SustainOff
                    ? FontStyle.Bold
                    : FontStyle.BoldItalic;
                double halfW = TextFontMetrics.Advance(m.Text, fs, sans: false, style) / 2;
                var ink = TextFontMetrics.Ink(m.Text, fs, sans: false, style);
                return (-halfW, halfW, ink.Top, -ink.Bottom);
            }
        }
    }

    /// <summary>
    /// Places one BOX-extent element: a flat pair spanning [<paramref name="x0"/>,
    /// <paramref name="x1"/>], <paramref name="topOffset"/> above the anchor and
    /// <paramref name="bottomOffset"/> below, cleared against every prior skyline by
    /// <paramref name="padding"/> (outside-staff-padding unless the grob declares a
    /// side-position padding of its own, as the trill does); the element only ever
    /// moves AWAY from the staff. Registers the pair and returns the new anchor.
    /// Against a flat support this is the old interval-frontier arithmetic exactly;
    /// the grobs whose ink has a real profile (the text grobs) build outline pairs
    /// instead of calling this.
    /// </summary>
    private static double Place(OutsideStaffSkylines tracker, double x0, double x1,
        double anchorY, double topOffset, double bottomOffset, double horizonPadding = 0,
        double padding = OutsideStaffPadding)
    {
        double move = tracker.Place(
            VerticalSkyline.FromBox(x0, x1,
                anchorY - bottomOffset, anchorY + topOffset, VerticalDirection.Up),
            VerticalSkyline.FromBox(x0, x1,
                anchorY - bottomOffset, anchorY + topOffset, VerticalDirection.Down),
            padding, horizonPadding);
        return anchorY + move;
    }

    /// <summary>Flat, X-infinite skyline at Y-up <paramref name="y"/> — the staff-edge
    /// floor a support skyline starts from, playing the old interval tracker's
    /// "staff edge when nothing overlaps" default pointwise.</summary>
    private static VerticalSkyline FlatBase(double y, VerticalDirection dir)
        => VerticalSkyline.FromBox(
            double.NegativeInfinity, double.PositiveInfinity, y, y, dir);

    /// <summary>
    /// LilyPond's accumulated outside-staff state: the support skylines plus every
    /// placed grob's own skyline PAIR, each entry keeping its own padding — queried
    /// pairwise per placement, never merged into one frontier. Replaces the interval
    /// tracker (<c>DirectionalOccupancy</c>), whose flat boxes could not read two
    /// profiles pointwise (ledger textscript.stacked.outline-step).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:937-950 skyline_spacing —
    /// <c>all_v_skylines</c> starts with the inside-staff skylines at padding 0;
    /// :700-806 add_grobs_of_one_priority pushes each placed grob's shifted/raised
    /// pair with its own padding after placing it.
    ///
    /// Coordinates are system-relative Y-up (the system top as origin), the native
    /// frame the grobs store. <c>_dir</c> = +1 stacks UP (moves are ≥ 0),
    /// <c>_dir</c> = -1 stacks DOWN (moves are ≤ 0). For flat box pairs against a
    /// flat support this reproduces the old frontier arithmetic exactly; profiles
    /// and multiple entries add what the boxes could not: pointwise binding and
    /// LilyPond's pocket placement between two prior grobs.
    /// </remarks>
    private sealed class OutsideStaffSkylines
    {
        private readonly List<(VerticalSkyline Up, VerticalSkyline Down, double Padding, double HorizonPadding)>
            _entries = new();
        private readonly int _dir;

        /// <summary>⚠️ LILYSHARP-OWN: pockets (settling between two prior grobs, LilyPond's
        /// <c>nearest_point</c> over the interval-set complement) are honest only where the
        /// support skyline carries the system's REAL ink profile. The above pass does (the
        /// up-skyline holds note/stem/beam protrusions); the below pass's support is still
        /// the flat staff edge plus scripts — no note-column DOWN ink — so a pocket there
        /// lands a hairpin on a ledger-line note the support cannot see. Until the below
        /// support merges the real down profiles, the below pass stays MONOTONE (clear
        /// beyond everything x-overlapping, the old frontier semantics).</summary>
        private readonly bool _allowPockets;

        public OutsideStaffSkylines(int dir, VerticalSkyline supportUp, VerticalSkyline supportDown,
            bool allowPockets = true)
        {
            _dir = dir;
            _allowPockets = allowPockets;
            // LILYPOND-REF: lily/axis-group-interface.cc:945-950 all_v_skylines / all_paddings
            // — the support entry carries padding 0: a grob against the staff pays only
            // its own padding.
            _entries.Add((supportUp, supportDown, 0.0, 0.0));
        }

        /// <summary>Merges more inside-staff ink into the support entry. Must be
        /// complete before the first <see cref="Place"/> — LilyPond builds the
        /// support (its priority -inf elements) before any outside-staff grob moves.</summary>
        public void MergeSupport(VerticalSkyline? up = null, VerticalSkyline? down = null)
        {
            var (sUp, sDown, _, _) = _entries[0];
            if (up != null) sUp.Merge(up);
            if (down != null) sDown.Merge(down);
        }

        /// <summary>
        /// Places one grob: computes the forbidden move intervals against every prior
        /// entry, takes the nearest allowed move at or beyond zero in the stacking
        /// direction, RAISES the pair by it and appends the pair to the entries.
        /// Returns the move (≥ 0 stacking up, ≤ 0 stacking down).
        /// </summary>
        /// <remarks>
        /// LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions
        /// — per prior skyline j the forbidden interval
        /// is (-down_j, up_j) with up_j = mine[DOWN].distance(other[UP], hpad) + pad and
        /// down_j = mine[UP].distance(other[DOWN], hpad) + pad, pad the LARGER of the
        /// two entries' paddings (:660); the move is
        /// <c>Interval_set::interval_union(...).complement().nearest_point(0, dir)</c>.
        /// </remarks>
        public double Place(VerticalSkyline up, VerticalSkyline down,
            double padding, double horizonPadding = 0)
        {
            // The padded copy of the mover's own profile is the same object for every
            // entry that resolves to the same horizon padding (LP recomputes it per
            // distance call; Skyline::padded is the expensive resolve here, and one
            // Place queries every prior entry) — build each distinct padding once.
            // Byte-identical: distance(other, hPad) IS paddedBy(hPad).distance(other).
            var paddedUp = new Dictionary<double, VerticalSkyline>();
            var paddedDown = new Dictionary<double, VerticalSkyline>();
            VerticalSkyline PaddedBy(Dictionary<double, VerticalSkyline> cache,
                VerticalSkyline sky, double hPad)
            {
                if (hPad <= 0)
                    return sky;
                if (!cache.TryGetValue(hPad, out var p))
                    cache[hPad] = p = sky.Padded(hPad);
                return p;
            }

            var forbidden = new List<(double Lo, double Hi)>();
            for (int j = 0; j < _entries.Count; j++)
            {
                var (eUp, eDown, ePad, eHPad) = _entries[j];
                double pad = Math.Max(padding, ePad);
                double hPad = Math.Max(horizonPadding, eHPad);
                double pushUp = PaddedBy(paddedDown, down, hPad).Distance(eUp) + pad;
                double pushDown = PaddedBy(paddedUp, up, hPad).Distance(eDown) + pad;
                // LILYSHARP-OWN: the SUPPORT entry cannot be passed on its far side.
                // LilyPond needs no such branch — its support pair carries the staff
                // contents' REAL far profile (notes, ledger ink, the other staff edge),
                // which an outside-staff grob's move never crosses. This support's far
                // side is a flat placeholder (see SeedAboveTrackers), so crossing it
                // must be forbidden here or a grob whose engraver anchor starts on the
                // wrong side of the staff "fits" under the whole support and never
                // moves. Goes when the support merges the real far profiles.
                if (j == 0)
                {
                    if (_dir > 0) pushDown = double.PositiveInfinity;
                    else pushUp = double.PositiveInfinity;
                }
                if (!_allowPockets)
                {
                    // Monotone: the grob must end up beyond EVERY x-overlapping entry in
                    // the stacking direction, so the interval reaches to infinity on the
                    // near side and no pocket between entries is reachable.
                    if (_dir > 0) pushDown = double.PositiveInfinity;
                    else pushUp = double.PositiveInfinity;
                }
                if (-pushDown < pushUp)   // empty when either side has no skyline
                    forbidden.Add((-pushDown, pushUp));
            }

            double move = NearestAllowed(forbidden, _dir);
            if (move != 0)
            {
                up.Raise(move);
                down.Raise(move);
            }
            _entries.Add((up, down, padding, horizonPadding));
            return move;
        }

        /// <summary>The allowed point nearest 0 in direction <paramref name="dir"/>,
        /// where "allowed" is outside every (open) forbidden interval — LilyPond's
        /// <c>Interval_set::nearest_point (0, dir)</c>. Touching an interval's edge is
        /// allowed (the paddings are already inside the bounds).</summary>
        private static double NearestAllowed(List<(double Lo, double Hi)> forbidden, int dir)
        {
            double m = 0;
            if (dir > 0)
            {
                forbidden.Sort(static (a, b) => a.Lo.CompareTo(b.Lo));
                foreach (var (lo, hi) in forbidden)
                    if (m > lo && m < hi)
                        m = hi;
            }
            else
            {
                forbidden.Sort(static (a, b) => b.Hi.CompareTo(a.Hi));
                foreach (var (lo, hi) in forbidden)
                    if (m > lo && m < hi)
                        m = lo;
            }
            return m;
        }
    }
}
