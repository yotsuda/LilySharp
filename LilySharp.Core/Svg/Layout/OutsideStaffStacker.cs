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
/// LILYPOND-REF: lily/axis-group-interface.cc:860-985 Axis_group_interface::skyline_spacing
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

    /// <summary>
    /// How far below its own top the element at <paramref name="staff"/> keeps its REFERENCE
    /// POINT — the frame step between a profile (which is about that refpoint) and this
    /// stacker's system-relative Y-up.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:201-285 internal_get_minimum_translations — the
    /// alignment works between
    /// VerticalAxisGroup REFERENCE POINTS, and a group's refpoint is not in general the middle
    /// of its extent: a Lyrics or ChordNames group's IS the text baseline. The quantity is
    /// decided once, in <c>MultiStaffLayouter.RefpointBelowTop</c>, and travels on the PLACED
    /// layout; this reads it rather than holding a second copy (HANDOFF 5.2.1②).
    /// <para>
    /// ⚠️ THE FALLBACK IS THE OLD FOLD, and it is only for layouts nobody placed: the
    /// harnesses that construct a <see cref="StaffLayout"/> by hand leave
    /// <c>RefpointBelowTop</c> unset, and those are all ordinary four-space staves, for which
    /// the two answers are the same 2.0.
    /// </para>
    /// </remarks>
    private static double RefpointBelowTop(
        ImmutableArray<SystemLayout> systems, int sys, int staff)
    {
        if (sys < 0 || sys >= systems.Length || systems[sys].StaffGroups.IsDefaultOrEmpty)
            return StaffBottom / 2.0;
        foreach (var sg in systems[sys].StaffGroups)
            foreach (var st in sg.Staves)
                if (st.StaffIndex == staff)
                    return st.RefpointBelowTop ?? StaffBottom / 2.0;
        return StaffBottom / 2.0;
    }

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

    // (DynamicLineSpanner's side-position padding 0.6 is the ENGRAVER's quiet-position
    // business — DynamicEngraver.BaselineY spends it against the supports. The stacker
    // runs only the outside-staff COLLISION pass, whose padding is outside-staff-padding
    // 0.46 — the split LilyPond itself has between the two passes.
    // LILYPOND-REF: lily/side-position-interface.cc:361-370 aligned_side — the grob's
    //   own "padding" (DynamicLineSpanner's 0.6) is spent there, once, against the
    //   side-position supports.
    // LILYPOND-REF: lily/axis-group-interface.cc:747-749 add_grobs_of_one_priority —
    //   the collision pass pays outside-staff-padding (:45
    //   default_outside_staff_padding_ = 0.46), never the side padding.)

    // A dynamic's own ink comes from the font, per label — DynamicEngraver.InkOf. There is
    // no constant here any more: LilyPond's DynamicText extent IS the drawn glyphs' ink,
    // so `p` descends below its baseline and `m` does not. The 1.2 / 0.3 pair that used to
    // live here was a nominal box for a glyph 2.588 tall, and cited
    // define-grobs.scm:1450 — which is the Y-offset (-0.6) inside the line spanner, not an
    // ascent. A LILYPOND-REF beside a number is not evidence that the number came from it.

    // ⚠️ LILYSHARP-OWN, AND IT IS THE THIRD SPELLING OF ONE QUANTITY — named 2026-07-31 by
    // the §7.7 pass over the placement port, not fixed, because no point observes it.
    // The wedge's real ink half-height is its OWN opening plus half the rule's thickness,
    // and the drawn opening's cap IS 0.6666 (HairpinEngraver.Height carries that citation),
    // so this is about HALF the ink LilyPond's Hairpin skyline has. The other two spellings
    // are already right and already differ in shape on purpose: HairpinEngraver.WedgeSkylines
    // is the pointwise outline (side-position, the wedge narrows to its apex) and
    // LayoutEngine's annotation-protrusion pass is the max fold over the piece. This one is
    // neither — it UNDER-reserves, which is the direction that prints a collision.
    // ⚠️ WHY IT IS NOT FIXED HERE: no ledger point reads it. hairpin.page.quiet reads the
    // DEEPEST ink under the last staff, which the protrusion pass supplies either way, and
    // this box only decides how far the collision pass pushes a wedge that sits under
    // something tall. The pair that observes it is a hairpin under a below-staff script or a
    // second dynamic — the same missing pair the script seed box waits on. Point first.
    // LILYPOND-REF: scm/define-grobs.scm:1785 Hairpin (height . 0.6666)
    private const double HairpinHalfHeight = 0.6666 / 2.0;

    // (DynamicHalfWidth 0.75 — the last nominal box of the dynamic pipeline — died on
    // 2026-07-29: every pass now reads the label's own outline pair,
    // DynamicEngraver.LabelSkylines.)

    // A text grob's vertical extent comes from the FACE, per string, at the size and style
    // the draw uses (TextFontMetrics.Ink) — never from a letter-class fraction of the em.
    // LILYPOND-REF: scm/define-grobs.scm:3800-3833 TextScript (the outside-staff-priority block) —
    // its Y-extent is grob::always-Y-extent-from-stencil, its stencil ly:text-interface::print, so
    // what skyline_spacing clears is the TEXT'S ink; the same declaration pattern
    // covers the tuplet number, the ottava label and the mark texts this file stacks.
    // The letter-class trio that used to live here (CapHeightEm 0.71 / TextAscentEm 0.75 /
    // TextDescentEm 0.25, "no single LP grob source") priced "dolce" and "poco" identically;
    // the dedicated pair (audit/lp-geometry/probes/textscript-ink.ly, ledger textscript.*)
    // measured LilyPond's baseline RIDING the p's own descender, 0.404430 apart.

    /// <summary>
    /// Adjusts below-staff element Y positions using priority-based stacking.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:860-985 Axis_group_interface::skyline_spacing
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
                    ImmutableArray<HairpinLayout> Hairpins,
                    ImmutableArray<ArticulationLayout> Articulations,
                    ImmutableArray<TrillSpannerLayout> Trills)
        StackBelowStaff(
            ScoreTextMetrics fonts,
            ImmutableArray<SystemLayout> systems,
            ImmutableArray<DynamicLayout> dynamics,
            ImmutableArray<HairpinLayout> hairpins,
            ImmutableArray<ArticulationLayout> articulations = default,
            bool applyStaffOffsets = false,
            Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? staffProfile = null,
            ImmutableArray<DynamicAlignEngraver.AlignedLineGroup> lineGroups = default,
            ImmutableArray<TrillSpannerLayout> trills = default)
    {
        // A below-staff script that DECLARES a priority (the fermata family's 75) is a mover
        // of this pass in its own right, so the pass has to run for it even on a page with
        // no dynamic and no hairpin anywhere — LilyPond's pass is not conditional on what
        // else is present. A DOWN trill spanner (priority 50, below the fermatas) is the
        // same kind of mover on this side.
        bool anyBelowScriptMover = !articulations.IsDefaultOrEmpty
            && articulations.Any(a => !a.IsAbove && a.OutsideStaffPriority is not null);
        bool anyBelowTrill = !trills.IsDefaultOrEmpty && trills.Any(t => t.Direction < 0);
        if ((dynamics.IsDefaultOrEmpty && hairpins.IsDefaultOrEmpty && !anyBelowScriptMover
                && !anyBelowTrill)
            || systems.Length == 0)
        {
            return (dynamics, hairpins, articulations, trills);
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
                // The staff's REAL profile (staff symbol, clef, notes, thin real stems,
                // beams — the same ingredients the inter-staff seed accumulated): the
                // below pass runs LilyPond's collision pass over real ink, pockets
                // included. Both production passes (final and prelim) supply it; the
                // flat-edge fallback is the degenerate support for harnesses that
                // construct no staff, and takes the SAME placement path.
                // LILYPOND-REF: lily/axis-group-interface.cc:937-950 skyline_spacing —
                //   all_v_skylines starts from the INSIDE-staff skylines.
                if (staffProfile?.Invoke(sys, staff) is { } p)
                {
                    // The profile is about the element's own REFERENCE POINT; the tracker
                    // frame is system-relative Y-up, where that refpoint sits at
                    // -(off + how far below its top the refpoint is). For an ordinary staff
                    // that is the half staff this line folded before; for a tab staff, an
                    // ossia or a TEXT ROW it is not (RefpointBelowTop).
                    double toSystem = -(off + RefpointBelowTop(systems, sys, staff));
                    p.Up.Raise(toSystem);
                    p.Down.Raise(toSystem);
                    t = new OutsideStaffSkylines(dir: -1, p.Up, p.Down);
                }
                else
                {
                    double edge = -(StaffBottom + off);
                    t = new OutsideStaffSkylines(dir: -1,
                        FlatBase(edge, VerticalDirection.Up),
                        FlatBase(edge, VerticalDirection.Down));
                }
                trackers[(sys, staff)] = t;
            }
            return t;
        }

        // Only the (system, staff) pairs that will actually PLACE something need a
        // tracker — and building one costs a real staff profile (BuildStaffSkylines).
        // Without this scope a single dynamic anywhere made every below-staff SCRIPT
        // build a profile for its own staff, staves the pass never places anything
        // on: pure waste, and preview relayout pays it on every keystroke. Placement
        // is unchanged — a support merged into a tracker nothing places against has
        // no effect (the scripts are already in the movers' quiet Y via the seed).
        var placedStaves = new HashSet<(int Sys, int Staff)>();
        if (!dynamics.IsDefaultOrEmpty)
            foreach (var dyn in dynamics)
                if (!dyn.IsAbove && measureToSystem.TryGetValue(dyn.MeasureIndex, out int ds))
                    placedStaves.Add((ds, dyn.StaffIndex));
        if (!hairpins.IsDefaultOrEmpty)
            foreach (var hp in hairpins)
                if (measureToSystem.TryGetValue(hp.StartMeasureIndex, out int hs))
                    placedStaves.Add((hs, hp.StaffIndex));
        // A below-staff script that declares a priority is itself placed here, so its
        // (system, staff) needs a tracker whatever else is on that staff.
        if (!articulations.IsDefaultOrEmpty)
            foreach (var a in articulations)
                if (!a.IsAbove && a.OutsideStaffPriority is not null
                    && measureToSystem.TryGetValue(a.MeasureIndex, out int asys))
                    placedStaves.Add((asys, a.StaffIndex));
        // ...and so does a DOWN trill spanner.
        if (!trills.IsDefaultOrEmpty)
            foreach (var t in trills)
                if (t.Direction < 0
                    && measureToSystem.TryGetValue(t.StartMeasureIndex, out int tsys))
                    placedStaves.Add((tsys, t.StaffIndex));

        // Below-staff scripts that declare NO outside-staff-priority sit against the note,
        // and everything at 250+ side-positions BELOW them: seed them as occupancy. The
        // fermata family declares 75 and is placed instead, below — before the dynamics,
        // which is what its lower priority means.
        // LILYPOND-REF: scm/define-grobs.scm:2992 Script — no outside-staff-priority of its
        //   own; scm/script.scm gives the fermata family 75.
        //   lily/axis-group-interface.cc:914-935 — no priority ⇒ inside_staff_skylines.
        if (!articulations.IsDefaultOrEmpty)
        {
            foreach (var a in articulations)
            {
                if (a.IsAbove || a.OutsideStaffPriority is not null
                    || !measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                    continue;
                if (!placedStaves.Contains((sysIdx, a.StaffIndex)))
                    continue;   // no mover on this staff — nothing would read the merge
                // a.YUp is Y-up above the staff middle; the tracker frame is
                // system-relative Y-up, where the staff middle sits at -(off + 2) (staff
                // top is off below the system top, its middle 2 further down), so the
                // grob's system-relative Y-up is a.YUp - off - 2.
                double off = applyStaffOffsets && sysIdx >= 0 && sysIdx < staffYBySystem.Count
                    && staffYBySystem[sysIdx].TryGetValue(a.StaffIndex, out var sso) ? sso : 0;
                double aYup = a.YUp - off - EngravingDefaults.StaffMiddle;
                // The Script grob's profile, from its one house (the padded outline) — the
                // same object the staff skyline seeds and the movers of this grob are placed
                // with. Only the DOWN side carries information for a below-staff stack (the
                // up side stays the staff-edge base).
                // LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines —
                //   a priority-less script's own profile is what goes into it.
                // ⚠️ In the regime the ledger books measure this merge is INERT (the
                // tracker's base is already the staff profile, which now carries the same
                // object): widening the old ±0.6 box to ±3.0 moved neither DSK nor DSM. It is
                // load-bearing where no staff profile is supplied, and it must not be a
                // second spelling there either.
                var seedDown = ArticulationEngraver.ScriptSkyline(a, aYup, VerticalDirection.Down);
                Track(sysIdx, a.StaffIndex).MergeSupport(down: seedDown);
            }
        }

        // --- Priority 50: DOWN trill spanners, BELOW the staff ---
        // The first mover of the below pass (the fermatas' 75 and the dynamics' 250 come
        // after), the mirror of PlaceTrills on the above side: the same 2-piece profile
        // (glyph plateau + wave run), the same outside-staff-padding, against this side's
        // trackers. The trill's YUp is already system-relative (the engraver resolved the
        // staff offset), so it enters the tracker directly, like the above pass's does.
        // LILYPOND-REF: scm/define-grobs.scm:4078 TrillSpanner outside-staff-priority 50;
        //   lily/axis-group-interface.cc:945-972 skyline_spacing — one pass, both
        //   directions.
        var adjTrills = trills;
        if (anyBelowTrill)
        {
            var tb = trills.ToBuilder();
            for (int i = 0; i < tb.Count; i++)
            {
                var t = tb[i];
                if (t.Direction >= 0
                    || !measureToSystem.TryGetValue(t.StartMeasureIndex, out int sysIdx))
                    continue;
                var (qUp, qDown) = TrillProfileSkylines(t);
                double move = Track(sysIdx, t.StaffIndex).Place(qUp, qDown, OutsideStaffPadding);
                if (move != 0)
                    tb[i] = t with { YUp = t.YUp + move };
            }
            adjTrills = tb.ToImmutable();
        }

        // --- Priority 75: the fermata family, BELOW the staff ---
        // The down half of the same pass the above side runs (LilyPond's is one pass over
        // both directions, axis-group-interface.cc:945-972 looping over UP and DOWN), and
        // it comes BEFORE the dynamics at 250, so a dynamic under a fermata clears the
        // fermata where it has landed.
        var adjArticulations = articulations;
        if (!articulations.IsDefaultOrEmpty)
        {
            var artBuilder = articulations.ToBuilder();
            for (int i = 0; i < artBuilder.Count; i++)
            {
                var a = artBuilder[i];
                if (a.IsAbove || a.OutsideStaffPriority is null
                    || !measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                    continue;
                double off = applyStaffOffsets && sysIdx >= 0 && sysIdx < staffYBySystem.Count
                    && staffYBySystem[sysIdx].TryGetValue(a.StaffIndex, out var so2) ? so2 : 0;
                double aYup = a.YUp - off - EngravingDefaults.StaffMiddle;
                var (myUp, myDown) = ArticulationEngraver.ScriptSkylines(a, aYup);
                double move = Track(sysIdx, a.StaffIndex).Place(myUp, myDown, OutsideStaffPadding);
                if (move != 0)
                    artBuilder[i] = a with { YUp = a.YUp + move };
            }
            adjArticulations = artBuilder.ToImmutable();
        }

        // --- Priority 250: DynamicLineSpanner (dynamics + hairpins) ---
        // LILYPOND-REF: scm/define-grobs.scm:1407 DynamicLineSpanner.outside-staff-priority = 250

        // A multi-member line (texts + wedges linked by running hairpins) is ONE grob of
        // this pass: its members' combined outline takes one Place and one move, so a tie
        // under the wedge pushes the WHOLE line down and the members stay aligned —
        // placing them one by one would push the wedge below its own line's text.
        // The members' Y is already the shared line DynamicAlignEngraver seated them on.
        // LILYPOND-REF: lily/axis-group-interface.cc:700-760 add_grobs_of_one_priority —
        //   the grob placed at 250 is the DynamicLineSpanner, not its children.
        var groupedDynIdx = new HashSet<int>();
        var groupedHpIdx = new HashSet<int>();
        var adjDynamics = dynamics;
        var adjHairpins = hairpins;
        if (!lineGroups.IsDefaultOrEmpty)
        {
            var dynB = dynamics.IsDefaultOrEmpty ? null : dynamics.ToBuilder();
            var hpB = hairpins.IsDefaultOrEmpty ? null : hairpins.ToBuilder();
            foreach (var g in lineGroups)
            {
                foreach (int di in g.DynamicIndices)
                    groupedDynIdx.Add(di);
                foreach (int hi in g.HairpinIndices)
                    groupedHpIdx.Add(hi);

                // The group's system and staff, from any member (one broken piece =
                // one system, one staff, by construction).
                int sysIdx, staffIdx;
                if (g.DynamicIndices.Length > 0 && dynB != null)
                {
                    var d0 = dynB[g.DynamicIndices[0]];
                    if (!measureToSystem.TryGetValue(d0.MeasureIndex, out sysIdx))
                        continue;
                    staffIdx = d0.StaffIndex;
                }
                else if (g.HairpinIndices.Length > 0 && hpB != null)
                {
                    var h0 = hpB[g.HairpinIndices[0]];
                    if (!measureToSystem.TryGetValue(h0.StartMeasureIndex, out sysIdx))
                        continue;
                    staffIdx = h0.StaffIndex;
                }
                else
                    continue;

                double off = applyStaffOffsets && sysIdx >= 0 && sysIdx < staffYBySystem.Count
                    && staffYBySystem[sysIdx].TryGetValue(staffIdx, out var gso) ? gso : 0;

                // The members' combined outline, each in the tracker's system-relative
                // frame — the same shapes the individual placements below use.
                (VerticalSkyline Up, VerticalSkyline Down)? dim = null;
                void Fold((VerticalSkyline Up, VerticalSkyline Down) part)
                {
                    if (dim is { } d)
                    {
                        d.Up.Merge(part.Up);
                        d.Down.Merge(part.Down);
                    }
                    else
                        dim = part;
                }
                foreach (int di in g.DynamicIndices)
                {
                    var dyn = dynB![di];
                    Fold(DynamicEngraver.LabelSkylines(
                        fonts, dyn.Text, dyn.IsExpressiveText, dyn.X,
                        dyn.YUp - off - EngravingDefaults.StaffMiddle));
                }
                foreach (int hi in g.HairpinIndices)
                {
                    // The wedge's REAL sloped outline, not a box over its extremes: the
                    // pass clears the tie off the arm where the arm actually is.
                    // LILYPOND-REF: scm/define-grobs.scm Hairpin vertical-skylines =
                    //   grob::unpure-vertical-skylines-from-stencil — the profile is the
                    //   drawn wedge.
                    var hp = hpB![hi];
                    Fold(HairpinEngraver.WedgeSkylines(
                        hp.StartX, hp.EndX, hp.StartOpening, hp.EndOpening, hp.YUp));
                }
                if (dim is not { } my)
                    continue;

                double move = Track(sysIdx, staffIdx).Place(my.Up, my.Down, OutsideStaffPadding);
                if (move != 0)
                {
                    foreach (int di in g.DynamicIndices)
                        dynB![di] = dynB[di] with { YUp = dynB[di].YUp + move };
                    foreach (int hi in g.HairpinIndices)
                        hpB![hi] = hpB[hi] with { YUp = hpB[hi].YUp + move };
                }
            }
            if (dynB != null)
                adjDynamics = dynB.ToImmutable();
            if (hpB != null)
                adjHairpins = hpB.ToImmutable();
        }

        // Dynamics: push below anything already occupying their X range
        // (below-staff scripts), then record their own extent.
        if (!adjDynamics.IsDefaultOrEmpty)
        {
            var dynBuilder = adjDynamics.ToBuilder();
            for (int i = 0; i < dynBuilder.Count; i++)
            {
                if (groupedDynIdx.Contains(i))
                    continue;
                var dyn = dynBuilder[i];
                // Forced-above dynamics sit above the staff (DynamicEngraver placed them);
                // the below-staff stacker leaves them untouched and ignores them as
                // below-staff occupiers.
                if (dyn.IsAbove)
                    continue;
                if (!measureToSystem.TryGetValue(dyn.MeasureIndex, out int sysIdx))
                    continue;

                var tracker = Track(sysIdx, dyn.StaffIndex);
                // System-relative Y-up: the grob's dyn.YUp (above the staff middle) sits
                // at dyn.YUp - off - 2; the placement only ever moves it AWAY (down), and
                // any push reflects back to the staff-middle frame the grob stores (+ off + 2).
                double off = applyStaffOffsets && sysIdx >= 0 && sysIdx < staffYBySystem.Count
                    && staffYBySystem[sysIdx].TryGetValue(dyn.StaffIndex, out var so) ? so : 0;
                double dynYup = dyn.YUp - off - EngravingDefaults.StaffMiddle;
                // LilyPond's outside-staff collision pass: the label's own OUTLINE
                // (my_dim) against the staff's real ink, outside-staff padding — a beam
                // face pushes the dynamic here while a thin stem tucks beside the f's
                // outline (ledger staff.staff.dynamic-beam-avoid vs -head-support).
                // LILYPOND-REF: lily/axis-group-interface.cc:648-676,:747-749 avoid_outside_staff_collisions
                //   — padding = outside-staff-padding.
                var (myUp, myDown) = DynamicEngraver.LabelSkylines(
                    fonts, dyn.Text, dyn.IsExpressiveText, dyn.X, dynYup);
                double move = tracker.Place(myUp, myDown, OutsideStaffPadding);
                if (move != 0)
                    dynBuilder[i] = dyn with
                    { YUp = dynYup + move + off + EngravingDefaults.StaffMiddle };
            }
            adjDynamics = dynBuilder.ToImmutable();
        }

        // Adjust hairpins: avoid overlapping with dynamics in the same X range
        if (!adjHairpins.IsDefaultOrEmpty)
        {
            var builder = adjHairpins.ToBuilder();
            for (int i = 0; i < builder.Count; i++)
            {
                if (groupedHpIdx.Contains(i))
                    continue;
                var hp = builder[i];
                if (!measureToSystem.TryGetValue(hp.StartMeasureIndex, out int sysIdx))
                    continue;

                var tracker = Track(sysIdx, hp.StaffIndex);
                // hp.YUp (the CENTRE) is already Y-up from the system top — the tracker
                // frame — so it enters directly; the box spans half a height each way
                // (the hairpin's stencil IS its wedge box), at outside-staff padding.
                double move = tracker.Place(
                    VerticalSkyline.FromBox(hp.StartX, hp.EndX,
                        hp.YUp - HairpinHalfHeight, hp.YUp + HairpinHalfHeight,
                        VerticalDirection.Up),
                    VerticalSkyline.FromBox(hp.StartX, hp.EndX,
                        hp.YUp - HairpinHalfHeight, hp.YUp + HairpinHalfHeight,
                        VerticalDirection.Down),
                    OutsideStaffPadding);
                if (move != 0)
                    builder[i] = hp with { YUp = hp.YUp + move };
            }
            adjHairpins = builder.ToImmutable();
        }

        // TextSpanner (priority 350) is now stacked ABOVE the staff (LilyPond
        // TextSpanner direction=UP) by StackAboveStaff, not here.
        return (adjDynamics, adjHairpins, adjArticulations, adjTrills);
    }

    /// <summary>
    /// A trill spanner's 2-piece profile pair about its stored YUp — the glyph plateau
    /// (its stencil-offset reach below the line, its outline top above it) and the wave
    /// run — the one house both stacking directions and the engraver's aligned_side read.
    /// LILYPOND-REF: scm/define-grobs.scm:4054-4068 make-with-dimension-from-markup
    ///   ("straight line as the vertical skyline"), :4085 vertical-skylines from the
    ///   stencil.
    /// </summary>
    private static (VerticalSkyline Up, VerticalSkyline Down) TrillProfileSkylines(
        in TrillSpannerLayout t)
    {
        bool hasGlyph = t.GlyphX < t.LineStartX;
        double reach = EngravingDefaults.TrillSpannerTextOffsetDown;
        double top = GlyphMetrics.OrnTrillGlyph.Top - reach;
        var qUp = new VerticalSkyline(VerticalDirection.Up);
        var qDown = new VerticalSkyline(VerticalDirection.Down);
        if (hasGlyph)
        {
            double gx0 = t.GlyphX + GlyphMetrics.OrnTrillGlyphOutline.Left;
            double gx1 = t.GlyphX + GlyphMetrics.OrnTrillGlyphOutline.Right;
            qUp.Merge(VerticalSkyline.FromBox(gx0, gx1,
                t.YUp - reach, t.YUp + top, VerticalDirection.Up));
            qDown.Merge(VerticalSkyline.FromBox(gx0, gx1,
                t.YUp - reach, t.YUp + top, VerticalDirection.Down));
        }
        if (t.LineStartX < t.LineEndX)
        {
            var line = TrillWaveOutline.Place(
                t.LineStartX, t.LineEndX - t.LineStartX, t.YUp);
            qUp.Merge(line.Up);
            qDown.Merge(line.Down);
        }
        return (qUp, qDown);
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
    /// Unified above-staff stacking: every above-staff annotation is placed in ascending
    /// outside-staff-priority order against a per-(system, staff) occupancy seeded from that
    /// STAFF's own profile (staff symbol, clef, notes, real thin stems, beams), the
    /// note-bound scripts and the above-staff tuplet brackets (which are bound to their beams
    /// and therefore registered as immovable).
    /// Replaces the previous pairwise special cases (bar-number-vs-volta in
    /// the renderer, music-mark-vs-volta in MusicMarkEngraver).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:860-985 <c>Axis_group_interface::skyline_spacing</c>
    /// — sort by priority, side-position each grob against the accumulated skyline, then merge
    /// its extent in (:969 <c>add_grobs_of_one_priority</c>, defined at :700).
    /// <para>
    /// ⚠️ ONE TRACKER PER STAFF, and this is the LITERAL shape rather than a reading of it:
    /// the pass is a property callback on ONE axis group — <c>calc_skylines</c> takes a single
    /// grob and hands it straight to <c>skyline_spacing (Grob *me)</c> — and the interface it
    /// belongs to says what that grob is in so many words: "A vertical axis group on which
    /// outside-staff skyline calculations are done".
    /// LILYPOND-REF: scm/define-grob-interfaces.scm:532-535 <c>outside-staff-axis-group-interface</c>
    ///   — that sentence is its docstring, verbatim.
    /// LILYPOND-REF: lily/axis-group-interface.cc:476-484 <c>Axis_group_interface::calc_skylines</c>
    ///   (the <c>ly:axis-group-interface::calc-skylines</c> callback) → :860
    ///   <c>skyline_spacing (Grob *me)</c>.
    /// ⚠️ THE C++ FUNCTION IS <c>skyline_spacing</c>. The snake-cased interface name this file
    /// and its siblings cited for many sessions is not a C++ symbol at all — only the
    /// hyphenated Scheme interface exists — which is why LpReferenceCitationTests carried it
    /// as a known-unverifiable name. Checked against the real source on 2026-07-30, corrected
    /// here and everywhere it had spread, and the test's entry removed with it.
    /// Until 2026-07-30 there was one tracker per SYSTEM, seeded from the system's
    /// silhouette: a mover on the lower staff then "cleared" the TOP staff's ink and flew
    /// over it, and four movers dodged that with the same line (<c>if (StaffIndex != 0)
    /// continue</c>) which held every lower-staff trill, ottava, script and text spanner out
    /// of the pass entirely. MEASURED, three grob families, LilyPond identical on the lower
    /// staff to fifteen digits and Lily# short by one whole pass: ledger
    /// <c>script.lower-staff.staff-to-ink-bottom</c> −0.261, <c>trill.lower-staff.staff-to-line</c>
    /// −2.455 (its base <c>trill.other-voice</c> being 0 exact), and
    /// <c>ottava.lower-staff.staff-to-line</c> −1.727520, whose bracket was DRAWN through the
    /// noteheads.
    /// </para>
    /// </remarks>
    /// <param name="staffProfile">That staff's real up/down profile, per (system, staff) —
    /// <c>MultiStaffLayouter</c>/<c>SkylineBuilder.BuildStaffSkylines</c>, the same delegate
    /// (and the same arguments) <see cref="StackBelowStaff"/> takes. Fresh skylines per call:
    /// the tracker raises them into its own frame. Without it the support falls back to the
    /// system silhouette, which is what a harness that builds no staff has.</param>
    public static (ImmutableArray<TrillSpannerLayout> Trills,
                   ImmutableArray<BarNumberLayout> BarNumbers,
                   ImmutableArray<OttavaBracketLayout> Ottavas,
                   ImmutableArray<CustomTextLayout> CustomTexts,
                   ImmutableArray<VoltaBracketLayout> Voltas,
                   ImmutableArray<MusicMarkLayout> MusicMarks,
                   ImmutableArray<DynamicLayout> Dynamics,
                   ImmutableArray<TextSpannerLayout> TextSpanners,
                   ImmutableArray<ArticulationLayout> Articulations)
        StackAboveStaff(
            ScoreTextMetrics fonts,
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
            ImmutableArray<TextSpannerLayout> textSpanners = default,
            Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? staffProfile = null,
            AboveStackMemo? memo = null,
            Func<int, int, (object Up, object Down)?>? profileIdentity = null)
    {
        if (memo is null || profileIdentity is null || systems.IsDefaultOrEmpty)
            return StackAboveStaffCore(fonts, systems, systemSkylines, tupletBrackets, trills,
                barNumbers, ottavas, customTexts, voltas, musicMarks, articulations,
                aboveDynamics, textSpanners, staffProfile);
        return StackAboveStaffMemoized(fonts, systems, systemSkylines, tupletBrackets, trills,
            barNumbers, ottavas, customTexts, voltas, musicMarks, articulations,
            aboveDynamics, textSpanners, staffProfile, memo, profileIdentity);
    }

    /// <summary>Per-system index lists into the pass's ten input arrays — one system's
    /// slice of each family, in array order.</summary>
    private sealed class SysPart
    {
        public readonly List<int> Trills = new(), BarNumbers = new(), Ottavas = new(),
            CustomTexts = new(), Voltas = new(), MusicMarks = new(), Articulations = new(),
            Dynamics = new(), TextSpanners = new(), TupletBrackets = new();
    }

    /// <summary>
    /// The memoized front of <see cref="StackAboveStaffCore"/>: partition every input by
    /// system, replay the systems whose program matches the memo, and run the core only on
    /// the rest. Sound because the pass holds no cross-system state — every placement and
    /// seed goes through a per-(system, staff) tracker, so filtering out whole systems
    /// leaves the remaining systems' work byte-identical (the coverage inventory is
    /// <see cref="AboveStackMemo"/>'s remarks).
    /// </summary>
    private static (ImmutableArray<TrillSpannerLayout> Trills,
                   ImmutableArray<BarNumberLayout> BarNumbers,
                   ImmutableArray<OttavaBracketLayout> Ottavas,
                   ImmutableArray<CustomTextLayout> CustomTexts,
                   ImmutableArray<VoltaBracketLayout> Voltas,
                   ImmutableArray<MusicMarkLayout> MusicMarks,
                   ImmutableArray<DynamicLayout> Dynamics,
                   ImmutableArray<TextSpannerLayout> TextSpanners,
                   ImmutableArray<ArticulationLayout> Articulations)
        StackAboveStaffMemoized(
            ScoreTextMetrics fonts,
            ImmutableArray<SystemLayout> systems,
            IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
            ImmutableArray<TupletBracketLayout> tupletBrackets,
            ImmutableArray<TrillSpannerLayout> trills,
            ImmutableArray<BarNumberLayout> barNumbers,
            ImmutableArray<OttavaBracketLayout> ottavas,
            ImmutableArray<CustomTextLayout> customTexts,
            ImmutableArray<VoltaBracketLayout> voltas,
            ImmutableArray<MusicMarkLayout> musicMarks,
            ImmutableArray<ArticulationLayout> articulations,
            ImmutableArray<DynamicLayout> aboveDynamics,
            ImmutableArray<TextSpannerLayout> textSpanners,
            Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? staffProfile,
            AboveStackMemo memo,
            Func<int, int, (object Up, object Down)?> profileIdentity)
    {
        // The same measure→system map and top-staff resolution the core builds (cheap:
        // one dictionary fill + one pass over the staves).
        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;
        var topStaff = TopStaffBySystem(systems);

        // 1. Partition every family by system (grobs whose measure maps to none are the
        // core's untouched passthroughs and stay on the live path).
        var parts = new Dictionary<int, SysPart>();
        SysPart PartOf(int s)
        {
            if (!parts.TryGetValue(s, out var p))
                parts[s] = p = new SysPart();
            return p;
        }
        void Collect<T>(ImmutableArray<T> arr, Func<T, int> measureOf, Func<SysPart, List<int>> sel)
        {
            if (arr.IsDefaultOrEmpty)
                return;
            for (int i = 0; i < arr.Length; i++)
                if (measureToSystem.TryGetValue(measureOf(arr[i]), out int s))
                    sel(PartOf(s)).Add(i);
        }
        Collect(trills, t => t.StartMeasureIndex, p => p.Trills);
        Collect(barNumbers, bn => bn.MeasureIndex, p => p.BarNumbers);
        Collect(ottavas, o => o.StartMeasureIndex, p => p.Ottavas);
        Collect(customTexts, ct => ct.MeasureIndex, p => p.CustomTexts);
        Collect(voltas, v => v.StartMeasureIndex, p => p.Voltas);
        Collect(musicMarks, m => m.MeasureIndex, p => p.MusicMarks);
        Collect(articulations, a => a.MeasureIndex, p => p.Articulations);
        Collect(aboveDynamics, d => d.MeasureIndex, p => p.Dynamics);
        Collect(textSpanners, ts => ts.StartMeasureIndex, p => p.TextSpanners);
        Collect(tupletBrackets, tb => tb.MeasureIndex, p => p.TupletBrackets);

        // 2. Build each occupied system's program and consult the memo.
        var hits = new HashSet<int>();
        var toStore = new List<(int Sys, AboveStackMemo.SystemEntry Entry)>();
        foreach (var (s, part) in parts)
        {
            var entry = BuildProgram(systems, systemSkylines, s, part, topStaff,
                profileIdentity, tupletBrackets, trills, barNumbers, ottavas, customTexts,
                voltas, musicMarks, articulations, aboveDynamics, textSpanners);
            if (entry == null)
                continue; // a profile with no stable identity: never memoized
            if (memo.TryMatch(s, entry))
                hits.Add(s);
            else
                toStore.Add((s, entry));
        }

        // 3. The live subset: everything not in a hit system, original order preserved.
        (ImmutableArray<T> Live, int[] Map) Filter<T>(ImmutableArray<T> arr, Func<T, int> measureOf)
        {
            if (arr.IsDefaultOrEmpty || hits.Count == 0)
                return (arr, Array.Empty<int>());
            var live = ImmutableArray.CreateBuilder<T>(arr.Length);
            var map = new List<int>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
            {
                if (measureToSystem.TryGetValue(measureOf(arr[i]), out int s) && hits.Contains(s))
                    continue;
                live.Add(arr[i]);
                map.Add(i);
            }
            return (live.ToImmutable(), map.ToArray());
        }
        var (liveTuplets, _) = Filter(tupletBrackets, tb => tb.MeasureIndex);
        var (liveTrills, mapTrills) = Filter(trills, t => t.StartMeasureIndex);
        var (liveBarNumbers, mapBarNumbers) = Filter(barNumbers, bn => bn.MeasureIndex);
        var (liveOttavas, mapOttavas) = Filter(ottavas, o => o.StartMeasureIndex);
        var (liveCustomTexts, mapCustomTexts) = Filter(customTexts, ct => ct.MeasureIndex);
        var (liveVoltas, mapVoltas) = Filter(voltas, v => v.StartMeasureIndex);
        var (liveMarks, mapMarks) = Filter(musicMarks, m => m.MeasureIndex);
        var (liveArtics, mapArtics) = Filter(articulations, a => a.MeasureIndex);
        var (liveDynamics, mapDynamics) = Filter(aboveDynamics, d => d.MeasureIndex);
        var (liveTextSpanners, mapTextSpanners) = Filter(textSpanners, ts => ts.StartMeasureIndex);

        // 4. Stack the live systems (byte-identical to stacking them in the full call:
        // a system's grobs are all-in or all-out, and only same-system grobs interact).
        var core = StackAboveStaffCore(fonts, systems, systemSkylines, liveTuplets, liveTrills,
            liveBarNumbers, liveOttavas, liveCustomTexts, liveVoltas, liveMarks, liveArtics,
            liveDynamics, liveTextSpanners, staffProfile);

        // 5. Reassemble: live results scatter back by index; hit systems replay their
        // stored outputs, positionally parallel to the (equal) stored inputs.
        ImmutableArray<T> Rebuild<T>(ImmutableArray<T> original, ImmutableArray<T> coreOut,
            int[] map, Func<SysPart, List<int>> sel, Func<AboveStackMemo.SystemEntry, T[]> outsOf)
        {
            if (original.IsDefaultOrEmpty || hits.Count == 0)
                return coreOut;
            var b = original.ToBuilder();
            for (int k = 0; k < map.Length; k++)
                b[map[k]] = coreOut[k];
            foreach (int s in hits)
            {
                if (!parts.TryGetValue(s, out var part))
                    continue;
                var idxs = sel(part);
                var vals = outsOf(memo.Get(s)!);
                for (int k = 0; k < idxs.Count; k++)
                    b[idxs[k]] = vals[k];
            }
            return b.ToImmutable();
        }
        var resTrills = Rebuild(trills, core.Trills, mapTrills, p => p.Trills, e => e.OutTrills);
        var resBarNumbers = Rebuild(barNumbers, core.BarNumbers, mapBarNumbers,
            p => p.BarNumbers, e => e.OutBarNumbers);
        var resOttavas = Rebuild(ottavas, core.Ottavas, mapOttavas, p => p.Ottavas, e => e.OutOttavas);
        var resCustomTexts = Rebuild(customTexts, core.CustomTexts, mapCustomTexts,
            p => p.CustomTexts, e => e.OutCustomTexts);
        var resVoltas = Rebuild(voltas, core.Voltas, mapVoltas, p => p.Voltas, e => e.OutVoltas);
        var resMarks = Rebuild(musicMarks, core.MusicMarks, mapMarks,
            p => p.MusicMarks, e => e.OutMusicMarks);
        var resDynamics = Rebuild(aboveDynamics, core.Dynamics, mapDynamics,
            p => p.Dynamics, e => e.OutDynamics);
        var resTextSpanners = Rebuild(textSpanners, core.TextSpanners, mapTextSpanners,
            p => p.TextSpanners, e => e.OutTextSpanners);
        var resArtics = Rebuild(articulations, core.Articulations, mapArtics,
            p => p.Articulations, e => e.OutArticulations);

        // 6. Store the missed systems' outputs for the next keystroke.
        foreach (var (s, entry) in toStore)
        {
            var part = parts[s];
            entry.OutTrills = Gather(resTrills, part.Trills);
            entry.OutBarNumbers = Gather(resBarNumbers, part.BarNumbers);
            entry.OutOttavas = Gather(resOttavas, part.Ottavas);
            entry.OutCustomTexts = Gather(resCustomTexts, part.CustomTexts);
            entry.OutVoltas = Gather(resVoltas, part.Voltas);
            entry.OutMusicMarks = Gather(resMarks, part.MusicMarks);
            entry.OutDynamics = Gather(resDynamics, part.Dynamics);
            entry.OutTextSpanners = Gather(resTextSpanners, part.TextSpanners);
            entry.OutArticulations = Gather(resArtics, part.Articulations);
            memo.Store(s, entry);
        }

        return (resTrills, resBarNumbers, resOttavas, resCustomTexts, resVoltas, resMarks,
            resDynamics, resTextSpanners, resArtics);
    }

    private static T[] Gather<T>(ImmutableArray<T> arr, List<int> idxs)
    {
        if (idxs.Count == 0)
            return Array.Empty<T>();
        var result = new T[idxs.Count];
        for (int k = 0; k < idxs.Count; k++)
            result[k] = arr[idxs[k]];
        return result;
    }

    /// <summary>
    /// One system's program: every input the pass reads for it (the inventory is
    /// <see cref="AboveStackMemo"/>'s remarks). Null when any profile this system's
    /// stacking would consume has no stable identity — that system is stacked live
    /// every keystroke rather than risking a false match.
    /// </summary>
    private static AboveStackMemo.SystemEntry? BuildProgram(
        ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        int s, SysPart part, int[] topStaff,
        Func<int, int, (object Up, object Down)?> profileIdentity,
        ImmutableArray<TupletBracketLayout> tupletBrackets,
        ImmutableArray<TrillSpannerLayout> trills,
        ImmutableArray<BarNumberLayout> barNumbers,
        ImmutableArray<OttavaBracketLayout> ottavas,
        ImmutableArray<CustomTextLayout> customTexts,
        ImmutableArray<VoltaBracketLayout> voltas,
        ImmutableArray<MusicMarkLayout> musicMarks,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<DynamicLayout> aboveDynamics,
        ImmutableArray<TextSpannerLayout> textSpanners)
    {
        var sys = systems[s];

        // Geometry: the read set of TopStaffIndex / StaffOffsetInSystemUp / SeedClefInk.
        var staves = new List<(int StaffIndex, double Y, bool IsHidden, ClefType Clef)>();
        if (!sys.StaffGroups.IsDefaultOrEmpty)
            foreach (var group in sys.StaffGroups)
                if (!group.Staves.IsDefaultOrEmpty)
                    foreach (var st in group.Staves)
                        staves.Add((st.StaffIndex, st.Y, st.IsHidden, st.Clef));

        // The staves this system's stacking consumes a profile for: each grob's own
        // staff with the tracker's sentinel resolution (-1 → the top staff), plus the
        // top staff itself (bar numbers and voltas hang there).
        var used = new SortedSet<int> { topStaff[s] };
        int Resolve(int rawStaff) => rawStaff < 0 ? topStaff[s] : rawStaff;
        foreach (int i in part.Trills) used.Add(Resolve(trills[i].StaffIndex));
        foreach (int i in part.Ottavas) used.Add(Resolve(ottavas[i].StaffIndex));
        foreach (int i in part.CustomTexts) used.Add(Resolve(customTexts[i].StaffIndex));
        foreach (int i in part.MusicMarks) used.Add(Resolve(musicMarks[i].StaffIndex));
        foreach (int i in part.Articulations) used.Add(Resolve(articulations[i].StaffIndex));
        foreach (int i in part.Dynamics) used.Add(Resolve(aboveDynamics[i].StaffIndex));
        foreach (int i in part.TextSpanners) used.Add(Resolve(textSpanners[i].StaffIndex));
        foreach (int i in part.TupletBrackets) used.Add(Resolve(tupletBrackets[i].StaffIndex));

        var profUps = new List<object>(used.Count);
        var profDowns = new List<object>(used.Count);
        foreach (int staff in used)
        {
            if (profileIdentity(s, staff) is not { } id)
                return null; // unstable identity: this system stacks live
            profUps.Add(id.Up);
            profDowns.Add(id.Down);
        }

        object? silUp = null, silDown = null;
        if (systemSkylines != null && s >= 0 && s < systemSkylines.Count)
        {
            silUp = systemSkylines[s].up;
            silDown = systemSkylines[s].down;
        }

        return new AboveStackMemo.SystemEntry
        {
            Indent = sys.Indent,
            TopStaff = topStaff[s],
            Staves = staves.ToArray(),
            ProfileUps = profUps.ToArray(),
            ProfileDowns = profDowns.ToArray(),
            SilhouetteUp = silUp,
            SilhouetteDown = silDown,
            Trills = Gather(trills, part.Trills),
            BarNumbers = Gather(barNumbers, part.BarNumbers),
            Ottavas = Gather(ottavas, part.Ottavas),
            CustomTexts = Gather(customTexts, part.CustomTexts),
            Voltas = Gather(voltas, part.Voltas),
            MusicMarks = Gather(musicMarks, part.MusicMarks),
            Articulations = Gather(articulations, part.Articulations),
            Dynamics = Gather(aboveDynamics, part.Dynamics),
            TextSpanners = Gather(textSpanners, part.TextSpanners),
            TupletBrackets = Gather(tupletBrackets, part.TupletBrackets),
        };
    }

    private static (ImmutableArray<TrillSpannerLayout> Trills,
                   ImmutableArray<BarNumberLayout> BarNumbers,
                   ImmutableArray<OttavaBracketLayout> Ottavas,
                   ImmutableArray<CustomTextLayout> CustomTexts,
                   ImmutableArray<VoltaBracketLayout> Voltas,
                   ImmutableArray<MusicMarkLayout> MusicMarks,
                   ImmutableArray<DynamicLayout> Dynamics,
                   ImmutableArray<TextSpannerLayout> TextSpanners,
                   ImmutableArray<ArticulationLayout> Articulations)
        StackAboveStaffCore(
            ScoreTextMetrics fonts,
            ImmutableArray<SystemLayout> systems,
            IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
            ImmutableArray<TupletBracketLayout> tupletBrackets,
            ImmutableArray<TrillSpannerLayout> trills,
            ImmutableArray<BarNumberLayout> barNumbers,
            ImmutableArray<OttavaBracketLayout> ottavas,
            ImmutableArray<CustomTextLayout> customTexts,
            ImmutableArray<VoltaBracketLayout> voltas,
            ImmutableArray<MusicMarkLayout> musicMarks,
            ImmutableArray<ArticulationLayout> articulations,
            ImmutableArray<DynamicLayout> aboveDynamics,
            ImmutableArray<TextSpannerLayout> textSpanners,
            Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? staffProfile)
    {
        if (systems.IsDefaultOrEmpty)
            return (trills, barNumbers, ottavas, customTexts, voltas, musicMarks,
                aboveDynamics, textSpanners, articulations);

        var measureToSystem = new Dictionary<int, int>();
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            foreach (var m in systems[sysIdx].Measures)
                measureToSystem[m.MeasureIndex] = sysIdx;

        var topStaff = TopStaffBySystem(systems);
        var trackers = AboveTrackers(systems, systemSkylines, staffProfile, topStaff);
        SeedAboveTrackers(fonts, systems, trackers, articulations, tupletBrackets, measureToSystem);

        // Movable outside-staff grobs, placed in ascending outside-staff-priority
        // order; each pass clears the occupancy seeded/accumulated by the earlier ones.
        var adjTrills = PlaceTrills(trills, trackers, measureToSystem);
        var adjArticulations = PlaceArticulations(
            articulations, trackers, measureToSystem, systems);
        var adjBarNumbers = PlaceBarNumbers(fonts, barNumbers, trackers, measureToSystem, topStaff, systems);
        var adjDynamics = PlaceAboveDynamics(fonts, aboveDynamics, trackers, measureToSystem, systems);
        var adjTextSpanners = PlaceTextSpanners(fonts, textSpanners, trackers, measureToSystem, systems);
        var adjOttavas = PlaceOttavas(fonts, ottavas, trackers, measureToSystem);
        var adjCustomTexts = PlaceCustomTexts(fonts, customTexts, trackers, measureToSystem, systems);
        var adjVoltas = PlaceVoltas(fonts, voltas, trackers, measureToSystem, topStaff);
        var adjMarks = PlaceMusicMarks(fonts, musicMarks, trackers, measureToSystem, systems);

        return (adjTrills, adjBarNumbers, adjOttavas, adjCustomTexts, adjVoltas, adjMarks,
            adjDynamics, adjTextSpanners, adjArticulations);
    }

    /// <summary>
    /// The per-(system, staff) UP occupancy trackers, built on first use: that staff's own
    /// real profile (staff symbol, clef, notes, thin real stems, beams) and its prefix clef
    /// ink. LilyPond's pass runs on one staff's VerticalAxisGroup at a time and its support
    /// starts from that staff's INSIDE-staff skylines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:860 <c>Axis_group_interface::skyline_spacing
    ///   (Grob *me)</c> — one call per axis group; :914 <c>std::vector&lt;Skyline_pair&gt;
    ///   inside_staff_skylines</c> collects the grobs with no outside-staff-priority and
    ///   :937 <c>Skyline_pair skylines (inside_staff_skylines)</c> is what :969's
    ///   <c>add_grobs_of_one_priority</c> then places against. That copy-construction IS this
    ///   method: the tracker's support opens as that staff's inside-staff ink and the movers
    ///   accumulate onto it.
    /// <para>
    /// FRAME: system-relative Y-up (up-positive, the SYSTEM TOP = 0), the native LP frame the
    /// grobs store — above the staff is positive Y-up. Only the support's CONTENT became
    /// per-staff; every seed/box/grob Y below is still measured from the system top and every
    /// grob writes back its unchanged system-relative YUp, so the stacker reads no absolute
    /// <c>SystemLayout.Y</c> (decoupled for the Stage-4 W2 stacking-origin flip).
    /// </para>
    /// <para>
    /// Lazily, because building one costs a real staff profile: a (system, staff) that places
    /// nothing never asks. The same scope <see cref="StackBelowStaff"/> keeps. COUNTED
    /// (2026-07-30, HANDOFF 5.3's "measure performance in CALLS, not milliseconds"): the added
    /// builds per render are 2 for showcase/08-chorale (one (system, staff)), 4 for test/notes
    /// and 4 for showcase/04-advanced (two each) — i.e. one per (system, staff) that places
    /// something, TWICE, because the annotation pass runs once for the extents and once final.
    /// Halving that is the shared per-(system, staff) profile cache the handoff names; it needs
    /// the cache to hand out COPIES, since the tracker raises the skyline it is given.
    /// </para>
    /// </remarks>
    private static Func<int, int, OutsideStaffSkylines> AboveTrackers(
        ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? staffProfile,
        int[] topStaff)
    {
        var trackers = new Dictionary<(int Sys, int Staff), OutsideStaffSkylines>();
        return (sys, rawStaff) =>
        {
            // ⚠️ -1 IS THE TOP STAFF, not a staff of its own. CustomTextLayout and
            // MusicMarkLayout both spell "the top staff" as -1 (their draw resolves the
            // middle from it), and a tracker keyed on -1 would be a PHANTOM staff: it would
            // hold none of the occupancy the trill and the scripts merged into the real
            // staff's, so a mark would clear thin air. MEASURED while porting: ledger
            // tempo.trill-cleared went 5.110000000 -> 2.883000002, i.e. the mark stopped
            // seeing the trill under it at all.
            // It resolves to the system's TOPMOST PLACED staff rather than to the constant 0,
            // which is what "-1" means and is not always the same number (TopStaffIndex).
            // ⚠️ The real end of this is the two layouts carrying a staff index like every
            // other above-staff grob does; LilyPond has no such sentinel, a grob's Y-parent IS
            // its staff. Until then this is where the sentinel is resolved, once.
            int staff = rawStaff < 0 && sys >= 0 && sys < topStaff.Length ? topStaff[sys] : Math.Max(0, rawStaff);
            if (trackers.TryGetValue((sys, staff), out var found))
                return found;
            // This staff's TOP line in the tracker frame — 0 for the first staff, negative
            // below it. The support starts as that flat edge and merges the staff's real
            // profile RAW: sloped buildings (beams) keep their slopes, and the base plays
            // the old "max(edge, h)" clamp pointwise. The support's DOWN side stays the flat
            // base: for an UP stack it only enters the forbidden intervals' lower bounds,
            // which positive moves never reach.
            double topUp = sys >= 0 && sys < systems.Length
                ? LayoutUtilities.StaffOffsetInSystemUp(systems[sys], staff)
                : 0;
            var supportUp = FlatBase(topUp, VerticalDirection.Up);
            var t = new OutsideStaffSkylines(dir: +1,
                supportUp, FlatBase(topUp, VerticalDirection.Down));
            if (staffProfile?.Invoke(sys, staff) is { } p)
            {
                // The profile is about the element's own REFERENCE POINT; the tracker frame
                // is system-relative Y-up, where that refpoint sits RefpointBelowTop below
                // the element's top. The same reflection StackBelowStaff makes.
                // ⚠️ IT USED TO FOLD THE NOMINAL HALF STAFF, which reads right for an
                // ordinary staff and wrong for everything else: a TEXT ROW's profile is about
                // its text BASELINE (MultiStaffLayouter merges RowSkylines saying so), so the
                // pass placed marks and numbers against ink it thought was 0.6 lower than it
                // is. HANDOFF 1 bone 9: the same nominal-half-staff fold, one layer down from
                // the repeat dots and the grid meter.
                p.Up.Raise(topUp - RefpointBelowTop(systems, sys, staff));
                supportUp.Merge(p.Up);
            }
            else if (systemSkylines != null && sys >= 0 && sys < systemSkylines.Count
                && !systemSkylines[sys].up.IsEmpty)
            {
                // No profile: the system silhouette, which is what a harness that builds no
                // staff has. The reason it is not the production support any more is that it
                // is the whole SYSTEM's ink, so a lower staff's mover clears the TOP staff's
                // (the flying-fermata bug).
                // ⚠️ AND THAT IS THE ONLY REASON. A 2026-07-30 note here claimed a second one —
                // that the FIRST system's silhouette "carries no music ink at all", sampled as
                // the staff line 0.050 across the whole line where the staff's own profile read
                // 0.667 at x 10 and 0.517 at x 30 — and it was WRONG IN THE OTHER DIRECTION.
                // Re-measured against the live pipeline: those two numbers are system 1's BEAM
                // edges to the digit, and they were in the PROFILE because the profile's beams
                // were filtered by staff and not by system (LayoutEngine.SystemStaffBeams now
                // does both). test/notes' first system has no beamed note at all, so the
                // silhouette's 0.050 was the correct answer and the profile was reading another
                // system's ink. With the filter restored the two agree POINTWISE on every
                // system, up to the exact half-staff frame step this method applies below, and
                // test/notes' 0.4 page growth is gone — the snapshot is byte-identical to its
                // pre-port baseline.
                // ⇒ THE SILHOUETTE IS NOT KNOWN TO BE WRONG ANYWHERE. perSystemExtents reads
                // it and that is not an island. What remains true of the three other readers
                // (ChordNameEngraver, FiguredBassEngraver, LyricEngraver) is only the FIRST
                // reason: they side-position against the system, so a row belonging to one
                // staff is spaced by another staff's ink. ChordName and Lyric already take a
                // per-(system, staff) delegate for their NON-edge cases; the edge case and
                // FiguredBass do not. Each wants a point of its own.
                supportUp.Merge(systemSkylines[sys].up);
            }
            SeedClefInk(systems, sys, staff, t);
            trackers[(sys, staff)] = t;
            return t;
        };
    }

    /// <summary>
    /// The index of the system's TOPMOST placed staff — the axis group a Score-context grob
    /// hangs on. Falls back to 0 when the system places none.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOT the constant 0. The staff whose index is 0 is not always the top one: a
    /// <c>\RemoveEmptyStaves</c> system drops staves, and an ossia adds one, so "the first
    /// staff" and "the staff at the top of this system" are different questions and only the
    /// second one is what LilyPond's Score-context grobs are side-positioned against.
    /// LILYPOND-REF: lily/align-interface.cc:274 <c>where += stacking_dir * dy</c> — the
    ///   accumulator walks with <c>stacking_dir</c> = DOWN, so the topmost placed staff is the
    ///   one with the greatest Y-up; that is how <c>SkylineBuilder.OuterStaff</c> picks the
    ///   silhouette's edge staff too.
    /// </remarks>
    /// <remarks>
    /// ⚠️ ONE PASS, NO SORT, and asked ONCE PER SYSTEM rather than once per grob (see
    /// <see cref="TopStaffBySystem"/>): a bar number exists on every system, so a LINQ
    /// SelectMany + OrderByDescending per bar number would put an allocation and a sort on the
    /// preview's per-keystroke path for a quantity that cannot change between two grobs of the
    /// same system.
    /// </remarks>
    private static int TopStaffIndex(ImmutableArray<SystemLayout> systems, int sys)
    {
        if (sys < 0 || sys >= systems.Length || systems[sys].StaffGroups.IsDefaultOrEmpty)
            return 0;
        // ⚠️ NOT A SECOND WALK. This used to answer "the topmost PLACED element", which is
        // the CHORD ROW when a score is written `chords / staff / lyrics` — while the draw
        // resolved the same -1 sentinel to the SYSTEM TOP. One sentinel, two answers, and
        // they part company exactly when the top element is not a staff (user report,
        // session 243). Both seams now ask LayoutUtilities.TopScoreGrobStaff, whose remark
        // carries the LilyPond citation and the measurement.
        int resolved = LayoutUtilities.TopScoreGrobStaff(systems[sys]);
        return resolved >= 0 ? resolved : 0;
    }

    /// <summary>
    /// The topmost placed staff of each system, computed once for the whole pass.
    /// </summary>
    private static int[] TopStaffBySystem(ImmutableArray<SystemLayout> systems)
    {
        var top = new int[systems.Length];
        for (int i = 0; i < systems.Length; i++)
            top[i] = TopStaffIndex(systems, i);
        return top;
    }

    /// <summary>
    /// This staff's prefix clef ink, as a flat line at the glyph's top over the glyph's
    /// X span. Geometry mirrors DrawClef: glyph at (Indent + 0.3, staff top + anchor line).
    /// </summary>
    /// <remarks>
    /// EVERY staff's clef, because a Clef is inside-staff ink of ITS OWN axis group — the same
    /// sentence <c>SkylineBuilder.SeedClef</c> quotes for the per-staff silhouette.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914 <c>inside_staff_skylines</c> — a grob
    ///   whose outside-staff-priority is unset goes in, and a Clef never declares one
    ///   (scm/define-grobs.scm Clef), so it is in the skyline the pass starts from.
    /// <para>
    /// ⚠️ A SECOND SPELLING of a seed the profile already carries: with a staff profile the
    /// clef is in it as its real OUTLINE (SkylineBuilder.SeedClef, ported 2026-07-27), where
    /// this is a flat plateau at the same top. A max-merge keeps the plateau, so what this
    /// costs is pointwise: a mark whose X falls where the clef's outline has dropped away
    /// clears the plateau here and the outline in LilyPond. It was the TOP staff's clef only
    /// until 2026-07-30.
    /// </para>
    /// <para>
    /// ⚠️ DELETING IT WAS TRIED AND REVERTED (2026-07-30), and the measurement is why it is
    /// still here: with the seed removed, 123 SNAPSHOTS move and NOT ONE LEDGER POINT does.
    /// Every line-start grob in the corpus shifts and nothing says whether the new place is
    /// LilyPond's — the <c>system.clef-floor.*</c> family measures the CLEF's own floor, not a
    /// grob side-positioned over the clef, so it never fires here. ⇒ The book this needs, in
    /// one line: a mark (or bar number) at a line start whose X falls over the clef's SLOPE
    /// rather than its plateau, read against LilyPond. Until that exists, removing the seed
    /// would be output-moving with no observer, which is the one move HANDOFF 5.0 forbids.
    /// </para>
    /// </remarks>
    private static void SeedClefInk(
        ImmutableArray<SystemLayout> systems, int sys, int staffIndex, OutsideStaffSkylines t)
    {
        if (sys < 0 || sys >= systems.Length || systems[sys].StaffGroups.IsDefaultOrEmpty)
            return;
        var staff = systems[sys].StaffGroups
            .SelectMany(g => g.Staves)
            .FirstOrDefault(s => !s.IsHidden && s.StaffIndex == staffIndex);
        if (staff == null)
            return;
        // ⚠️ A TEXT ROW DRAWS NO CLEF, and this seeded one anyway until session 243.
        // StaffLayout.Clef defaults to Treble for every staff including a chords or lyrics
        // row, so the switch below fell through to its `_ =>' arm and merged a PHANTOM
        // treble clef -- 1.800000 tall (GlyphMetrics.ClefG.Top 4.8 less the 3.0 anchor
        // line), spanning x 0.300..2.865 -- into the support of a row that prints nothing
        // there. On a rows-only lead sheet that row is what a section label hangs on, and
        // the label's own X is 0.300..2.373, so the label cleared a clef nobody drew.
        // MEASURED (user report 2026-08-24, scratch/ベースタブLy/Untitled-6.lys):
        // the label's ink bottom stood 1.960000 over the chord row's ink where LilyPond
        // puts it at 0.460000 -- its own outside-staff-padding -- i.e. 1.500000 too far,
        // and the support read exactly 1.800000 at the label's X and 0.000000 everywhere
        // else. LP measured in audit/lp-geometry/probes/mark-chord-row.ly book MKT, the
        // rows-only sheet with a \sectionLabel.
        // ⚠️ THE PREDICATE IS SPACEABILITY, the same one everything else in this island
        // asks: a text row is exactly a non-spaceable staff (Staff.CreateTextRow gives it a
        // staff-affinity; an ossia has none and is a real staff with a real clef).
        // LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 Page_layout_problem::is_spaceable.
        if (!StaffAffinity.IsSpaceable(staff.StaffAffinity))
            return;
        var (clefBox, anchorLine) = staff.Clef switch
        {
            ClefType.Bass => (GlyphMetrics.ClefF, 1.0),
            ClefType.Alto => (GlyphMetrics.ClefC, 2.0),
            ClefType.Tenor => (GlyphMetrics.ClefC, 1.0),
            _ => (GlyphMetrics.ClefG, 3.0),
        };
        double clefProtrusion = clefBox.Top - anchorLine;
        if (clefProtrusion <= 0)
            return;
        double clefX = systems[sys].Indent + 0.3;
        double top = clefProtrusion + staff.Y;
        t.MergeSupport(up: VerticalSkyline.FromBox(
            clefX + clefBox.Left, clefX + clefBox.Right, top, top, VerticalDirection.Up));
    }

    /// <summary>
    /// Merges the immovable above-staff occupancy into the trackers: the note-bound scripts
    /// (which carry no outside-staff-priority) and the above-staff tuplet brackets (bound to
    /// their beams in this model), each into ITS OWN staff's tracker.
    /// </summary>
    private static void SeedAboveTrackers(
        ScoreTextMetrics fonts,
        ImmutableArray<SystemLayout> systems,
        Func<int, int, OutsideStaffSkylines> trackers,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<TupletBracketLayout> tupletBrackets,
        Dictionary<int, int> measureToSystem)
    {
        // Above-staff scripts that declare NO outside-staff-priority (accents, staccato,
        // ornaments, bows, editorial accidentals …) are bound to their notes: they enter
        // the skyline BEFORE any outside-staff grob is placed, so movable marks
        // (rehearsal/section marks etc.) must clear them. The ones that DO declare a
        // priority — the fermata family's 75 — are movers instead, placed by
        // PlaceArticulations in priority order; seeding them here as well would both
        // reserve their old height and forbid their move.
        // LILYPOND-REF: lily/axis-group-interface.cc:914-935 — the grobs whose priority is
        //   infinite (unset) are exactly the ones that go into inside_staff_skylines;
        //   :952-972 places the others by ascending priority.
        if (!articulations.IsDefaultOrEmpty)
        {
            foreach (var a in articulations)
            {
                if (!a.IsAbove || a.OutsideStaffPriority is not null
                    || !measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                    continue;
                // a.YUp is Y-up above the staff middle; reflect to system-relative
                // Y-up against this staff's WITHIN-SYSTEM middle.
                double staffTopUp = LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], a.StaffIndex);
                double relY = a.YUp + LayoutUtilities.StaffMiddleUpInSystem(systems[sysIdx], a.StaffIndex);
                double inkTop = relY + a.Ink.Top;     // BBox Top is up-positive
                if (inkTop <= staffTopUp)
                    continue; // entirely inside the staff — its own profile covers it
                // The Script grob's profile, from its one house (the padded outline) — the
                // same object the staff profile carries (SkylineBuilder) and the movers of
                // this grob are placed with; LilyPond has ONE vertical-skylines per grob.
                // It was a flat plateau at the ink box's top until 2026-08-07, and the flat
                // top max-merged OVER the staff profile's real outline — a fermata over an
                // accent cleared the plateau where LilyPond clears the wedge's slope
                // (fermata-dot-position block B measured the 0.135 it cost). Like the below
                // side's twin of this merge, it is INERT where a staff profile is supplied
                // and load-bearing where none is, and it must not be a second spelling
                // there either.
                // LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines —
                //   a priority-less script's own profile is what goes into it.
                var seedUp = ArticulationEngraver.ScriptSkyline(a, relY, VerticalDirection.Up);
                trackers(sysIdx, a.StaffIndex).MergeSupport(up: seedUp);
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
                    trackers(sysIdx, tb.StaffIndex).MergeSupport(up: VerticalSkyline.FromBox(
                        tb.StartX, tb.EndX, lineTop, lineTop, VerticalDirection.Up));
                }
                if (!string.IsNullOrEmpty(tb.NumberText))
                {
                    double fs = TupletBracketEngraver.NumberFontSize;
                    double halfW = fonts.Advance(
                        tb.NumberText, fs, TextRole.Tuplet,
                        TupletBracketEngraver.NumberFontStyle) / 2;
                    double halfH = fonts.InkHeight(
                        tb.NumberText, fs, TextRole.Tuplet,
                        TupletBracketEngraver.NumberFontStyle) / 2;
                    trackers(sysIdx, tb.StaffIndex).MergeSupport(up: VerticalSkyline.FromBox(
                        tb.NumberX - halfW, tb.NumberX + halfW,
                        tb.NumberYUp + halfH, tb.NumberYUp + halfH, VerticalDirection.Up));
                }
            }
        }
    }

    // ---- 50: TrillSpanner ----
    private static ImmutableArray<TrillSpannerLayout> PlaceTrills(
        ImmutableArray<TrillSpannerLayout> trills, Func<int, int, OutsideStaffSkylines> trackers,
        Dictionary<int, int> measureToSystem)
    {
        if (trills.IsDefaultOrEmpty)
            return trills;
        var b = trills.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var t = b[i];
            // A DOWN trill was placed by the below pass (StackBelowStaff, priority 50
            // there too) and passes through here untouched.
            if (t.Direction < 0
                || !measureToSystem.TryGetValue(t.StartMeasureIndex, out int sysIdx))
                continue;
            // System-relative Y-up: t.YUp is Y-up from the system top, entering the
            // tracker directly; the placed anchor writes back unchanged.
            // anchor = the LINE. The grob's ink about it: the "tr" glyph rides
            // stencil-offset (0 . -1), so on a glyph-bearing piece the glyph's plateau
            // spans (line - reach .. line + glyphTop - reach) — LilyPond's own ext dump
            // reads (-1.0 . 1.1) — and the LINE's own ink, on every piece, is the run of
            // trill_element glyphs (TrillWaveOutline).
            //
            // LilyPond's TWO steps both exist here: aligned_side pays the trill's OWN
            // padding 0.5 against its side supports (the note columns and, via
            // include_staff, the staff) — that is the ENGRAVER's quiet height, the
            // anchor entering this pass (ledger trill.{quiet,support}.staff-to-line,
            // exact) — and the collision pass below pays the grob's
            // outside-staff-padding 0.46 against the accumulated skylines, which add
            // the ink aligned_side never sees (beams, scripts). Where a column or the
            // staff decides, the engraver's 0.5 stands and this pass moves nothing.
            // LILYPOND-REF: lily/side-position-interface.cc:361-370 aligned_side padding;
            // lily/axis-group-interface.cc:747-749 add_grobs_of_one_priority — the
            //   collision padding is outside-staff-padding (default 0.46).
            // The entry carries the outside-staff-padding — the
            // trill declares none, so the 0.46 default — which is what a later grob
            // (a metronome mark at 1300) pays to clear it. MEASURED:
            // tempo.trill-cleared.staff-to-baseline read +0.073 with the trill's 0.5
            // registered, and 0.040 of that was exactly this substitution.
            // LILYPOND-REF: lily/axis-group-interface.cc:747-749,:804 add_grobs_of_one_priority
            //   — all_paddings gets outside-staff-padding.
            //
            // The trill's profile is its STENCIL skyline shape, not its extent box: a FLAT
            // plateau over the glyph's x-range — LilyPond's OWN construction, not an
            // approximation: the bound text wraps the glyph so as to "set up a straight
            // line as the vertical skyline for the trill glyph" (its comment's words) —
            // and the low WAVE over the rest of the span.
            // ⚠️ ONE profile does BOTH jobs (2026-07-30): the collision the move is
            // computed from and the entry a later grob clears. Until then the move ran on
            // a flat glyph-high box over the WHOLE span while the 2-piece pair was only
            // registered — two spellings of one grob's skyline, and the wrong one was
            // load bearing. LilyPond passes the SAME v_skylines to
            // avoid_outside_staff_collisions and to all_v_skylines, so a grob's own thin
            // wave is what it clears an obstacle with. MEASURED: ledger trill.x.wave-zone
            // = the stop column's LEDGER ink 4.100000 + 0.460000 + the wave's own reach;
            // with the glyph-high box the trill sat a full 1.0 - waveReach too high there.
            // MEASURED (the registration half, session 33): the metronome mark's digits
            // sit over the wave; with one flat plateau over the whole SPAN registered, TMT
            // read the "0"'s overshoot (+0.033); with the glyph/wave split it reads
            // 0.000000000 — the binding ink is a flat-baseline glyph over the plateau.
            // LILYPOND-REF: scm/define-grobs.scm:4054-4068 TrillSpanner bound-details,
            //   make-with-dimension-from-markup ("straight line as the vertical
            //   skyline"), :4085 vertical-skylines from the stencil;
            // lily/axis-group-interface.cc:770-773,:798-800 add_grobs_of_one_priority
            //   — v_skylines goes to avoid_outside_staff_collisions AND to all_v_skylines.
            // X reach inside the helper: the glyph's TRUE (outline) left and right, not
            // its bounding box — LilyPond wraps the bound text in
            // make-with-true-dimension-markup exactly because "the trill glyph has a
            // loop on its left, which sticks out of its bounding box" (its own comment).
            // The line: the run of trill_element glyphs, the same house the engraver's
            // aligned_side reads (TrillWaveOutline). Its ink is what a later grob clears
            // over the wave (ledger tempo.trill-cleared.staff-to-baseline).
            // LILYPOND-REF: scm/define-grobs.scm:4056-4066 TrillSpanner bound-details,
            //   make-with-true-dimension-markup on scripts.trill
            var (qUp, qDown) = TrillProfileSkylines(t);
            double move = trackers(sysIdx, t.StaffIndex).Place(qUp, qDown, OutsideStaffPadding, 0);
            b[i] = t with { YUp = t.YUp + move };
        }
        return b.ToImmutable();
    }

    // ---- 75: Script, but ONLY the family that declares a priority (fermatas) ----
    /// <summary>
    /// Places the above-staff scripts that DECLARE an outside-staff-priority — the fermata
    /// family's 75, which lands between the trill's 50 and the bar number's 100. Scripts
    /// that declare none are not here: they seeded the occupancy (SeedAboveTrackers).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/script.scm fermata (outside-staff-priority . 75);
    ///   lily/axis-group-interface.cc:699-810 add_grobs_of_one_priority — the padding is the
    ///   grob's own outside-staff-padding, and Script declares none, so the 0.46 default.
    /// <para>
    /// TWO stages, and only the second is here: the ENGRAVER already spent the script's own
    /// side-position padding (scm/script.scm fermata 0.40) against its supports — the heads
    /// and, with add-stem-support, the stem FLATTENED to its tip across all X
    /// (side-position-interface.cc:302-305 <c>set_minimum_height (max_height ())</c>) —
    /// floored by include_staff. This pass pays 0.46 against the real accumulated
    /// skylines, where the stem is thin again. MEASURED: over a plain staff the pass
    /// decides (ledger script.quiet.staff-to-ink-bottom = staff ink 2.05 + 0.46), over a
    /// high head too (script.high-head = head ink 4.545 + 0.46), and over a forced-up stem
    /// the engraver's 0.40 stands because the fermata's ARCH straddles the thin stem
    /// (script.stem-support = drawn tip + 0.40, no move at all) — which is why the profile
    /// here has to be the glyph's real outline and not its box.
    /// </para>
    /// </remarks>
    private static ImmutableArray<ArticulationLayout> PlaceArticulations(
        ImmutableArray<ArticulationLayout> articulations, Func<int, int, OutsideStaffSkylines> trackers,
        Dictionary<int, int> measureToSystem, ImmutableArray<SystemLayout> systems)
    {
        if (articulations.IsDefaultOrEmpty)
            return articulations;
        var b = articulations.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var a = b[i];
            if (!a.IsAbove || a.OutsideStaffPriority is null
                || !measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                continue;
            // Stack in system-relative Y-up: a.YUp is above this staff's WITHIN-SYSTEM
            // middle, so it enters at a.YUp + midUp and the move reflects straight back.
            double midUp = LayoutUtilities.StaffMiddleUpInSystem(systems[sysIdx], a.StaffIndex);
            var (myUp, myDown) = ArticulationEngraver.ScriptSkylines(a, a.YUp + midUp);
            // Script declares no outside-staff-horizontal-padding, so the horizon padding
            // is the 0.0 default (its horizon-padding 0.1 is aligned_side's, spent by the
            // engraver, not this pass's).
            double move = trackers(sysIdx, a.StaffIndex).Place(myUp, myDown, OutsideStaffPadding);
            if (move != 0)
                b[i] = a with { YUp = a.YUp + move };
        }
        return b.ToImmutable();
    }

    // ---- 100: BarNumber (absolute page Y) ----
    // A BarNumber belongs to the SCORE context, so it hangs on the top staff's axis group and
    // clears that staff's ink and no other.
    // LILYPOND-REF: ly/engraver-init.ly:774 \consists Bar_number_engraver — declared inside
    //   the Score context (its \name is at :729), not in Staff, so there is one BarNumber per
    //   system and its side support is the topmost staff.
    private static ImmutableArray<BarNumberLayout> PlaceBarNumbers(
        ScoreTextMetrics fonts,
        ImmutableArray<BarNumberLayout> barNumbers, Func<int, int, OutsideStaffSkylines> trackers,
        Dictionary<int, int> measureToSystem, int[] topStaff,
        ImmutableArray<SystemLayout> systems)
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
            // skyline_spacing clears is the TEXT's ink and not a designed box
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
            double width = fonts.Advance(
                bn.Text, BarNumberEngraver.FontSize, TextRole.BarNumber, FontStyle.Bold);
            double originX = bn.RightAligned ? bn.X - width : bn.X;
            // System-relative Y-up: bn.YUp is Y-up from the system top, entering directly.
            var (bnUp, bnDown) = TextOutlineSkylines.Place(
                bn.Text, BarNumberEngraver.FontSize,
                fonts.Face(TextRole.BarNumber, FontStyle.Bold),
                originX, bn.YUp);
            // The tracker of the staff or ROW the number HANGS ON. LilyPond re-parents the
            // grob onto that element and the outside-staff pass then runs inside THAT
            // element's axis group, so the pass and the placement have to name the same one.
            // ⚠️ IT IS READ OFF THE LAYOUT, NOT RE-DERIVED. This used to call
            // BarNumberEngraver.AnchorStaff a second time; the engraver now decides once —
            // staff, else grid row, else nothing — and carries the answer, so the two cannot
            // drift apart (HANDOFF 5.2.1②). Null means LilyPond's move_to_extremal_staff
            // found nothing to re-parent onto, and the top placed staff stands in.
            // LILYPOND-REF: lily/side-position-interface.cc:545-547 move_to_extremal_staff.
            int anchorStaff = bn.AnchorStaffIndex ?? topStaff[sysIdx];
            double move = trackers(sysIdx, anchorStaff)
                .Place(bnUp, bnDown, OutsideStaffPadding);
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
        ScoreTextMetrics fonts,
        ImmutableArray<DynamicLayout> aboveDynamics, Func<int, int, OutsideStaffSkylines> trackers,
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
            // SYSTEM middle is dyn.YUp + midUp; place, then shift back. The mover is the
            // label's own OUTLINE pair (my_dim, from-stencil), not a nominal box — the
            // same profile the below pass and the side-position support read.
            // LILYPOND-REF: scm/define-grobs.scm:1446 DynamicText Grob::vertical_skylines_from_stencil.
            double midUp = LayoutUtilities.StaffMiddleUpInSystem(systems[sysIdx], dyn.StaffIndex);
            var (myUp, myDown) = DynamicEngraver.LabelSkylines(
                fonts, dyn.Text, dyn.IsExpressiveText, dyn.X, dyn.YUp + midUp);
            double move = trackers(sysIdx, dyn.StaffIndex).Place(myUp, myDown, OutsideStaffPadding);
            b[i] = dyn with { YUp = dyn.YUp + move };
        }
        return b.ToImmutable();
    }

    // ---- 350: TextSpanner (accel./rit. — LilyPond TextSpanner direction=UP) ----
    // LILYPOND-REF: scm/define-grobs.scm TextSpanner (direction . UP),
    //   (outside-staff-priority . 350). Placed above the staff, clearing the
    //   up-skyline, instead of below where it hit low notes.
    private static ImmutableArray<TextSpannerLayout> PlaceTextSpanners(
        ScoreTextMetrics fonts,
        ImmutableArray<TextSpannerLayout> textSpanners, Func<int, int, OutsideStaffSkylines> trackers,
        Dictionary<int, int> measureToSystem, ImmutableArray<SystemLayout> systems)
    {
        if (textSpanners.IsDefaultOrEmpty)
            return textSpanners;
        var b = textSpanners.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var ts = b[i];
            if (!measureToSystem.TryGetValue(ts.StartMeasureIndex, out int sysIdx)) continue;
            // aligned_side's staff-padding refpoint floor, applied to the anchor (the
            // LINE) BEFORE the collision pass — the same order PlaceCustomTexts uses.
            // With no declared side padding (side-position's default 0.0) and a facing
            // reach of just the dash half-thickness, this floor is what stands on a
            // quiet staff (ledger textspanner.floor.staff-to-line = 2.05 + 0.8, exact;
            // the old anchor was staff edge + 0.46 + an invented 0.3 box descent).
            // ⚠️ OVER ITS OWN STAFF's ink edge, not the system's: aligned_side floors the
            // refpoint at that STAFF's own extent plus staff-padding, so on a lower staff the
            // floor sits that staff's offset lower. It read the system top until 2026-07-30,
            // when lower-staff spanners were held out of this pass entirely and nothing could
            // see it.
            // LILYPOND-REF: lily/side-position-interface.cc:401-453 aligned_side — the
            //   staff-padding branch reads staff_extent[dir] of the grob's OWN staff symbol
            //   ("Ensure 'staff-padding' from my refpoint to the staff"); :361-363 padding
            //   default 0.0.
            double anchor = Math.Max(ts.YUp,
                LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], ts.StaffIndex)
                + EngravingDefaults.StaffLineThickness / 2.0
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
                var ink = fonts.Ink(ts.Text, 4.0 * 0.5, TextRole.Text, FontStyle.Italic);
                top = Math.Max(top, ink.Top);
                bottom = Math.Max(bottom, -ink.Bottom);
            }
            double newRel = Place(trackers(sysIdx, ts.StaffIndex), ts.StartX, ts.EndX,
                anchor,
                topOffset: top, bottomOffset: bottom);
            b[i] = ts with { YUp = newRel };
        }
        return b.ToImmutable();
    }

    // ---- 400: OttavaBracket (above-staff only) ----
    private static ImmutableArray<OttavaBracketLayout> PlaceOttavas(
        ScoreTextMetrics fonts,
        ImmutableArray<OttavaBracketLayout> ottavas, Func<int, int, OutsideStaffSkylines> trackers,
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
            // System-relative Y-up: o.YUp is Y-up from the system top, entering directly,
            // and it already carries LilyPond's FIRST step — aligned_side, which spent the
            // bracket's own padding 0.5 against its side supports
            // (OttavaBracketEngraver.AlignedSideLineY). This pass is the SECOND one, and it
            // pays outside-staff-padding 0.46 against the accumulated skylines, which hold
            // the ink aligned_side never sees (other voices, beams, scripts). Where a
            // column decides, the engraver's 0.5 stands and this moves nothing — 0.5 > 0.46.
            // LILYPOND-REF: lily/side-position-interface.cc:361-370 aligned_side padding;
            //   lily/axis-group-interface.cc:747-749 add_grobs_of_one_priority — the
            //   collision padding is outside-staff-padding (default 0.46).
            //
            // The mover is the bracket's OWN profile — label outline, dashed rule, hook,
            // with the gap between label and line EMPTY — the same pair aligned_side read.
            // ⚠️ Until 2026-08-02 this was a FLAT box at the hook's depth 0.8 over the whole
            // span, which over-reserved by 0.067480009 at OTC's binding x (the first
            // notehead's left edge, under the label's sloped outline) — the mover's half of
            // ledger ottava.support.staff-to-line's residual.
            var (myUp, myDown) = OttavaBracketEngraver.Skylines(
                fonts, o.Text, o.StartX,
                OttavaBracketEngraver.LineStartX(
                    fonts, o.Text, o.StartX, EngravingDefaults.OttavaBracketFontSize),
                o.EndX, o.EdgeHeight, o.IsAbove, o.YUp);
            double move = trackers(sysIdx, o.StaffIndex).Place(myUp, myDown, OutsideStaffPadding);
            b[i] = o with { YUp = o.YUp + move };
        }
        return b.ToImmutable();
    }

    // ---- 450: TextScript (^"...") ----
    private static ImmutableArray<CustomTextLayout> PlaceCustomTexts(
        ScoreTextMetrics fonts,
        ImmutableArray<CustomTextLayout> customTexts, Func<int, int, OutsideStaffSkylines> trackers,
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
            double midUp = LayoutUtilities.StaffMiddleUpInSystem(systems[sysIdx], ct.StaffIndex);
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
                ct.Text, ctFs, fonts.Face(TextRole.Text, FontStyle.Italic), ct.X, anchor);
            double move = trackers(sysIdx, ct.StaffIndex).Place(ctUp, ctDown, OutsideStaffPadding,
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
    // A VoltaBracketSpanner belongs to the SCORE context too, so like the bar number it hangs
    // on the top staff's axis group.
    // LILYPOND-REF: ly/engraver-init.ly:767 \consists Volta_engraver — in the Score context
    //   (its \name is at :729), the same place the bar number's engraver lives.
    private static ImmutableArray<VoltaBracketLayout> PlaceVoltas(
        ScoreTextMetrics fonts,
        ImmutableArray<VoltaBracketLayout> voltas, Func<int, int, OutsideStaffSkylines> trackers,
        Dictionary<int, int> measureToSystem, int[] topStaff)
    {
        if (voltas.IsDefaultOrEmpty)
            return voltas;
        var b = voltas.ToBuilder();
        foreach (var sysGroup in Enumerable.Range(0, b.Count)
            .Where(i => measureToSystem.ContainsKey(b[i].StartMeasureIndex))
            .GroupBy(i => measureToSystem[b[i].StartMeasureIndex]))
        {
            int sysIdx = sysGroup.Key;

            // ONE spanner per CHAIN of consecutive endings, not per system: the
            // engraver closes the spanner when a bracket ends with no new one
            // starting, and the next repeat's alternatives get a fresh spanner —
            // so two repeats on one line side-position INDEPENDENTLY (volta-
            // bracket-vertical-skylines.ly: LP fits the d'''' chain at 8.771 and
            // the a'''' chain at 6.771 in the same system).
            // LILYPOND-REF: lily/volta-engraver.cc:371-374 make_spanner — a
            //   bracket with no open spanner makes one;
            //   lily/volta-engraver.cc:493-499 add_support — the chain's last
            //   end hands the spanner its staves and closes it.
            // Within a chain the brackets share one placed Y — that part is the
            // spanner being a single axis group.
            // LILYPOND-REF: scm/define-grobs.scm VoltaBracketSpanner —
            //   (axes . (Y)) (outside-staff-priority . 600) (side-axis . Y).
            var ordered = sysGroup.OrderBy(i => b[i].StartMeasureIndex).ToList();
            var chains = new List<List<int>>();
            foreach (int i in ordered)
            {
                if (chains.Count > 0
                    && b[i].StartMeasureIndex
                        <= b[chains[^1][^1]].EndMeasureIndex + 1)
                    chains[^1].Add(i);
                else
                    chains.Add(new List<int> { i });
            }

            foreach (var chain in chains)
            {
                // Merge the chain's extents into one profile at the shared
                // starting anchor (the highest engraver anchor in the chain),
                // place it once, and the move applies to the whole chain.
                // Frame = system-relative Y-up (system top = 0); v.YUp enters directly.
                double anchor0 = double.MinValue;
                foreach (int i in chain)
                    anchor0 = Math.Max(anchor0, b[i].YUp);

                var spanUp = new VerticalSkyline(VerticalDirection.Up);
                var spanDown = new VerticalSkyline(VerticalDirection.Down);
                foreach (int i in chain)
                {
                    var v = b[i];
                    // The spanner's profile is the DRAWN stencil, pointwise: the
                    // thin line spans the bracket, while the hooks and the number
                    // reach deeper only over their own X. A flat full-width box
                    // held the hook depth over every note and floated the whole
                    // chain ~2 ss above LP (volta-bracket-vertical-skylines.ly:
                    // LP's line clears a d'''' head by padding alone, 0.56, its
                    // hook dropping harmlessly beside it).
                    // Geometry mirrored from SharedRenderer.DrawVoltaBrackets:
                    // line thickness 0.13, start hook iff text, end hook iff
                    // closed, number ink 0.3 below the line at StartX + 0.5.
                    void AddBox(double x0, double x1, double bottom)
                    {
                        spanUp.Merge(VerticalSkyline.FromBox(
                            x0, x1, bottom, anchor0 + 0.1, VerticalDirection.Up));
                        spanDown.Merge(VerticalSkyline.FromBox(
                            x0, x1, bottom, anchor0 + 0.1, VerticalDirection.Down));
                    }
                    bool hasText = !string.IsNullOrEmpty(v.VoltaText);
                    AddBox(v.StartX, v.EndX, anchor0 - 0.065);          // the line
                    if (hasText)
                        AddBox(v.StartX - 0.065, v.StartX + 0.065,      // start hook
                            anchor0 - VoltaBracketEngraver.GetEdgeHeight());
                    if (v.IsClosed)
                        AddBox(v.EndX - 0.065, v.EndX + 0.065,          // end hook
                            anchor0 - VoltaBracketEngraver.GetEdgeHeight());
                    if (hasText)
                    {
                        double w = fonts.Advance(
                            v.VoltaText, 0.6 * 4.0, TextRole.Volta, FontStyle.Bold);
                        AddBox(v.StartX + 0.5, v.StartX + 0.5 + w,      // the number
                            anchor0 - 0.3 - fonts.InkHeight(
                                v.VoltaText, 0.6 * 4.0, TextRole.Volta, FontStyle.Bold));
                    }
                }

                double anchor = anchor0 + trackers(sysIdx, topStaff[sysIdx])
                    .Place(spanUp, spanDown, OutsideStaffPadding);

                foreach (int i in chain)
                    b[i] = b[i] with { YUp = anchor };
            }
        }
        return b.ToImmutable();
    }

    // ---- 1500: MusicMark (rehearsal/section labels) ----
    private static ImmutableArray<MusicMarkLayout> PlaceMusicMarks(
        ScoreTextMetrics fonts,
        ImmutableArray<MusicMarkLayout> musicMarks, Func<int, int, OutsideStaffSkylines> trackers,
        Dictionary<int, int> measureToSystem, ImmutableArray<SystemLayout> systems)
    {
        if (musicMarks.IsDefaultOrEmpty)
            return musicMarks;
        // A boundary "To Coda" and the section label it shares a barline with are one
        // arrangement, not two grobs: pair them and move the sign beside the label
        // BEFORE anything is priced, then place each pair as ONE union extent below —
        // so whatever stands under either drawn column (a second ending's volta
        // bracket under the sign, high ink under the label) raises the pair together,
        // and the members never price each other (their inks overlap by design).
        // See CoPlaceToCodaWithLabels' remarks for why the post-stack shape failed.
        musicMarks = MusicMarkEngraver.CoPlaceToCodaWithLabels(
            musicMarks,
            (ma, mb) => measureToSystem.TryGetValue(ma, out int sa)
                     && measureToSystem.TryGetValue(mb, out int sb) && sa == sb,
            out var toCodaPairs);
        var signOfLabel = new Dictionary<int, int>(); // label index -> its sign's index
        foreach (var (sign, label) in toCodaPairs)
            signOfLabel[label] = sign;
        var pairedSigns = new HashSet<int>(signOfLabel.Values);
        var b = musicMarks.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var m = b[i];
            if (!measureToSystem.TryGetValue(m.MeasureIndex, out int sysIdx))
                continue;
            // A paired sign rides its label: the union placement below moves both.
            if (pairedSigns.Contains(i))
                continue;
            // Spanner-handled marks (cresc./rit./ottava ...) are never
            // drawn by DrawMusicMarks — registering them would reserve
            // PHANTOM space and push real marks above thin air. Marks
            // placed below the staff don't belong to the above pass.
            // m.YUp is Y-up; a mark below the staff-top line (m.YUp < 2.0, the top line
            // sits 2 above the middle) is not part of this above pass. The stacker frame
            // is system-relative Y-up, so shift against the WITHIN-SYSTEM staff middle.
            // ⚠️ THE SENTINEL IS RESOLVED, and it was not until session 243: passing the
            // raw -1 to StaffOffsetInSystemUp falls through its `staffIndex >= 0` guard and
            // returns 0 — the SYSTEM TOP — so the mark was priced against the staff the
            // tracker resolved and shifted against a different line. The draw resolves it
            // the same way (SharedRenderer.DrawMusicMarks); both go through this one home.
            double midUp = LayoutUtilities.StaffMiddleUpInSystem(
                systems[sysIdx],
                LayoutUtilities.ResolveScoreGrobStaff(systems[sysIdx], m.StaffIndex));
            if (MusicMarkItem.IsSpannerHandled(m.MarkType) || m.YUp < 2.0)
                continue;
            // The metronome mark's pair is its STENCIL's, piecewise like the draw:
            // outline profiles under the text runs (so a flat-footed digit sits ON the
            // baseline where a round one overshoots — the split LilyPond's pointwise
            // clearing reads, ledger tempo.trill-cleared) and under the note GLYPHS
            // (head, flag, dots — the same freetype walk LilyPond's named-glyph
            // skyline runs); the STEM is a box because it is a box in LilyPond too
            // (note-by-number builds it with ly:round-filled-box). A glyph falls back
            // to its designed box only when the bundled music font cannot be located.
            // LILYPOND-REF: scm/define-grobs.scm:2357 MetronomeMark vertical-skylines
            //   = grob::always-vertical-skylines-from-stencil.
            if (m.MarkType == MusicMarkType.Tempo)
            {
                double em = EngravingDefaults.MetronomeMarkFontSize;
                double anchor = m.YUp + midUp;
                var tUp = new VerticalSkyline(VerticalDirection.Up);
                var tDown = new VerticalSkyline(VerticalDirection.Down);
                double tx = m.X;
                bool hasMetronome = m.Text.Length > 0;

                double noteSize = MetronomeMarkGeometry.NoteSize;
                double noteScale = MetronomeMarkGeometry.NoteScale;
                void MergeGlyph(char g, double gx, double gy, GlyphMetrics.BBox box)
                {
                    var (gUp, gDown) = TextOutlineSkylines.PlaceMusicGlyph(
                        g, noteSize, gx, gy);
                    if (gUp.IsEmpty && gDown.IsEmpty)
                    {
                        gUp = VerticalSkyline.FromBox(
                            gx + box.Left * noteScale, gx + box.Right * noteScale,
                            gy + box.Bottom * noteScale, gy + box.Top * noteScale,
                            VerticalDirection.Up);
                        gDown = VerticalSkyline.FromBox(
                            gx + box.Left * noteScale, gx + box.Right * noteScale,
                            gy + box.Bottom * noteScale, gy + box.Top * noteScale,
                            VerticalDirection.Down);
                    }
                    tUp.Merge(gUp);
                    tDown.Merge(gDown);
                }

                if (m.TempoText != null)
                {
                    var (mtUp, mtDown) = TextOutlineSkylines.Place(
                        m.TempoText, em, fonts.Face(TextRole.Tempo, FontStyle.Bold), tx, anchor);
                    tUp.Merge(mtUp);
                    tDown.Merge(mtDown);
                    tx += fonts.Advance(m.TempoText, em, TextRole.Tempo, FontStyle.Bold);
                    if (hasMetronome)
                    {
                        var (pUp, pDown) = TextOutlineSkylines.Place(
                            "(", em, fonts.Face(TextRole.Tempo),
                            tx + MetronomeMarkGeometry.LeadingSpaceAdvance(fonts, "("), anchor);
                        tUp.Merge(pUp);
                        tDown.Merge(pDown);
                        tx += fonts.Advance(" (", em, TextRole.Tempo);
                    }
                }
                if (hasMetronome)
                {
                    // The note, DOWN-aligned: its head origin (the centre line) rides
                    // half a scaled head above the baseline — the same arithmetic the
                    // draw runs.
                    var headBox = MetronomeMarkGeometry.HeadBox(m.TempoBeatUnit);
                    int tempoLog = MetronomeMarkGeometry.Log(m.TempoBeatUnit);
                    double centreY = anchor - headBox.Bottom * noteScale;
                    MergeGlyph(MetronomeMarkGeometry.HeadGlyph(m.TempoBeatUnit),
                        tx, centreY, headBox);
                    if (tempoLog > 0)
                    {
                        var att = MetronomeMarkGeometry.StemAttachment(m.TempoBeatUnit);
                        double stemTh = MetronomeMarkGeometry.StemThickness;
                        double stemRight = tx + att.X * noteScale;
                        double stemTop = centreY
                            + MetronomeMarkGeometry.StemTopAboveCentre(m.TempoBeatUnit);
                        tUp.Merge(VerticalSkyline.FromBox(stemRight - stemTh, stemRight,
                            centreY + att.Y * noteScale, stemTop, VerticalDirection.Up));
                        tDown.Merge(VerticalSkyline.FromBox(stemRight - stemTh, stemRight,
                            centreY + att.Y * noteScale, stemTop, VerticalDirection.Down));
                        if (tempoLog >= 3)
                            MergeGlyph(EmmentalerGlyphs.Flag8thUp,
                                stemRight - stemTh / 2, stemTop, GlyphMetrics.Flag8thUp);
                    }
                    for (int d = 0; d < m.TempoDots; d++)
                        MergeGlyph(EmmentalerGlyphs.AugmentationDot,
                            tx + MetronomeMarkGeometry.DotX(m.TempoBeatUnit, d), centreY,
                            GlyphMetrics.AugmentationDot);

                    double noteRight = MetronomeMarkGeometry.NoteRight(
                        m.TempoBeatUnit, m.TempoDots);
                    string eq = MetronomeMarkGeometry.EquationText(
                        m.Text, m.TempoText != null);
                    double eqX = tx + noteRight
                        + MetronomeMarkGeometry.LeadingSpaceAdvance(fonts, eq);
                    var (eUp, eDown) = TextOutlineSkylines.Place(
                        eq, em, fonts.Face(TextRole.Tempo), eqX, anchor);
                    tUp.Merge(eUp);
                    tDown.Merge(eDown);
                    if (m.SwingSubdivision != 0)
                    {
                        // The swing feel-equation keeps its named box estimate — a
                        // Lily#-own device; the label lives at
                        // MetronomeMarkGeometry.SwingEquationReach.
                        double sw0 = eqX + fonts.Advance(eq, em, TextRole.Tempo);
                        double sw1 = sw0 + MetronomeMarkGeometry.SwingEquationReach;
                        tUp.Merge(VerticalSkyline.FromBox(sw0, sw1,
                            anchor - 0.5, anchor + 2.0, VerticalDirection.Up));
                        tDown.Merge(VerticalSkyline.FromBox(sw0, sw1,
                            anchor - 0.5, anchor + 2.0, VerticalDirection.Down));
                    }
                }
                double tMove = trackers(sysIdx, m.StaffIndex).Place(tUp, tDown, OutsideStaffPadding,
                    OutsideStaffHorizontalPadding);
                b[i] = m with { YUp = m.YUp + tMove };
                continue;
            }
            // Plain-text marks (D.S./Fine/pedal words/…) carry their string's OUTLINE
            // pair like every other stencil-skylined text grob; boxed labels ARE drawn
            // boxes and segno/coda are glyphs with no baked outline — those keep their
            // box extents (named, not hidden).
            // ⚠️ THE SUSTAIN PEDAL MOVED TO THE BOX SIDE OF THAT LINE on 2026-08-18: its
            // word is a run of MUSIC glyphs (MusicMarkEngraver.SustainPedalStencil), so
            // asking TextOutlineSkylines for the outline of "Ped." would trace a serif
            // string nobody draws. It falls through to MusicMarkExtents, which prices the
            // glyphs' own LILC boxes — the boxes LilyPond juxtaposes them by.
            if (!m.IsSymbol && !MusicMarkEngraver.IsGlyphPedal(m.MarkType)
                && m.MarkType is not (MusicMarkType.Rehearsal
                    or MusicMarkType.SectionLabel or MusicMarkType.Tempo))
            {
                double fs = MusicMarkEngraver.PlainTextFontSize;
                var style = MusicMarkEngraver.TextStyleOf(m.MarkType);
                var role = MusicMarkEngraver.TextRoleOf(m.MarkType);
                double halfW = fonts.Advance(m.Text, fs, role, style) / 2;
                var (mUp, mDown) = TextOutlineSkylines.Place(
                    m.Text, fs, fonts.Face(role, style), m.X - halfW, m.YUp + midUp);
                double mMove = trackers(sysIdx, m.StaffIndex).Place(mUp, mDown, OutsideStaffPadding,
                    OutsideStaffHorizontalPadding);
                b[i] = m with { YUp = m.YUp + mMove };
                continue;
            }
            var (x0, x1, top, bottom) = MusicMarkExtents(fonts, m);
            // A label with a co-placed "To Coda" is priced as the pair's union: the
            // sign sits inside the label's vertical band (baseline at the box's
            // bottom edge, cap height under the box top), so the union is the
            // label's box widened to the sign's left edge. The sign's width is the
            // same advance its stand-alone placement branch above would reserve.
            if (signOfLabel.TryGetValue(i, out int signIdx))
            {
                var sign = b[signIdx];
                // The DRAWN composition's width ("To " + the coda glyph), from the
                // same home the renderer reads — Advance(sign.Text) prices the word
                // "Coda" nobody draws and its extra reach cleared neighbouring
                // labels the ink never touches (ToCodaStencilWidths' remarks).
                var (textW, glyphW) = MusicMarkEngraver.ToCodaStencilWidths(fonts);
                double signHalfW = (textW + glyphW) / 2;
                x0 = Math.Min(x0, sign.X - signHalfW - m.X);
            }
            // The whole mark family declares the horizontal 0.2 (see the constant).
            double newRel = Place(trackers(sysIdx, m.StaffIndex), m.X + x0, m.X + x1,
                m.YUp + midUp, topOffset: top, bottomOffset: bottom,
                horizonPadding: OutsideStaffHorizontalPadding);
            b[i] = m with { YUp = newRel - midUp };
            // The pair moves as one: the sign keeps its tucked offset under the
            // label's line wherever the union landed.
            if (signOfLabel.TryGetValue(i, out int si2))
                b[si2] = b[si2] with { YUp = b[si2].YUp + (newRel - midUp - m.YUp) };
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
    private static (double X0, double X1, double Top, double Bottom) MusicMarkExtents(
        ScoreTextMetrics fonts, MusicMarkLayout m)
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
                double halfW =
                    (fonts.Advance(m.Text, fs, TextRole.Mark, FontStyle.Bold) + 2 * pad) / 2;
                double halfH = (fs + 2 * pad) / 2;
                return (-halfW, halfW, halfH, halfH);
            }
            case MusicMarkType.Tempo:
            {
                // LEFT-anchored at its ink left (self-aligned LEFT on the break-aligned
                // meter), baseline-anchored vertically — from the ONE geometry home the
                // draw uses (the centered width estimate and the bold-1.8 pricing died
                // with the tempo port). ⚠️ PlaceMusicMarks routes Tempo through its
                // piecewise stencil pair before reaching this method; this arm stays as
                // the box description of the same ink.
                var ink = MetronomeMarkGeometry.Ink(fonts, m.Text, m.TempoText,
                    m.TempoBeatUnit, m.TempoDots, m.SwingSubdivision);
                return (0.0, ink.Width, ink.Top, -ink.Bottom);
            }
            default:
            {
                // THE SUSTAIN PEDAL, whose word is a run of MUSIC glyphs and not a string
                // at all (lily/sustain-pedal.cc:47-76). This arm IS hit from the stacker —
                // PlaceMusicMarks sends it here rather than to the outline pair, because a
                // text outline of "Ped." traces a face nobody draws. The glyphs' own LILC
                // boxes are what LilyPond juxtaposes them by, so the box description IS the
                // stencil here rather than an approximation of it, and the ink sits ON the
                // baseline (every pedal glyph's bbox bottom is 0).
                if (MusicMarkEngraver.IsGlyphPedal(m.MarkType))
                {
                    var (pedalWidth, pedalTop) =
                        MusicMarkEngraver.SustainPedalExtent(m.Text);
                    return (-pedalWidth / 2, pedalWidth / 2, pedalTop, 0.0);
                }
                // Plain text marks (D.S./Fine/sostenuto/una corda/…), baseline anchor at
                // 0.7 x 4sp.
                // ⚠️ PlaceMusicMarks routes these through their OUTLINE pair before
                // reaching this method, so this arm is not hit from the stacker any
                // more; it stays as the box description of the same geometry (the
                // sizes/styles here and there must not drift apart).
                // Both extents are the string's own metrics at the size and style the draw
                // picks, read from the one home (MusicMarkEngraver.PlainTextFontSize /
                // TextStyleOf): ink about the baseline vertically, the advance horizontally
                // — LilyPond has no "estimated" widths, a mark's X extent is its markup
                // stencil's. (To-Coda still prices its text only; the coda glyph beside it
                // stays an unreserved approximation.)
                double fs = MusicMarkEngraver.PlainTextFontSize;
                var style = MusicMarkEngraver.TextStyleOf(m.MarkType);
                var role = MusicMarkEngraver.TextRoleOf(m.MarkType);
                double halfW = fonts.Advance(m.Text, fs, role, style) / 2;
                var ink = fonts.Ink(m.Text, fs, role, style);
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
        double padding = OutsideStaffPadding, double? registerPadding = null)
    {
        double move = tracker.Place(
            VerticalSkyline.FromBox(x0, x1,
                anchorY - bottomOffset, anchorY + topOffset, VerticalDirection.Up),
            VerticalSkyline.FromBox(x0, x1,
                anchorY - bottomOffset, anchorY + topOffset, VerticalDirection.Down),
            padding, horizonPadding, registerPadding);
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

        // (The pocket seal that used to live here was LILYSHARP-OWN — the below pass forced
        // MONOTONE because its support was a flat staff edge with no note-column DOWN
        // ink — died on 2026-07-29: the below support now carries the staff's real down
        // profile, so pockets are honest on both sides.
        // LILYPOND-REF: lily/axis-group-interface.cc:672-673 Interval_set — the move IS
        //   interval_union(forbidden).complement().nearest_point(0, dir), pockets and
        //   all; LilyPond has no monotone branch because its supports are always real.)

        public OutsideStaffSkylines(int dir, VerticalSkyline supportUp, VerticalSkyline supportDown)
        {
            _dir = dir;
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
            double padding, double horizonPadding = 0, double? registerPadding = null)
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
                if (-pushDown < pushUp)   // empty when either side has no skyline
                    forbidden.Add((-pushDown, pushUp));
            }

            double move = NearestAllowed(forbidden, _dir);
            // The pair stored for LATER grobs is the SAME pair the move was computed with
            // — LilyPond hands one v_skylines to avoid_outside_staff_collisions and then
            // pushes that same pair onto all_v_skylines (:798-803). (Until 2026-07-30 the
            // trill passed a different profile for each job; the placement one was a flat
            // glyph-high box and it was the load-bearing one. Ledger trill.x.wave-zone.)
            // Only the PADDING may differ: a grob whose SIDE-POSITION padding differs (the
            // trill's 0.5) pays that against the support in this single pass, but later
            // grobs pay its outside-staff-padding against it, not the side padding.
            // LILYPOND-REF: lily/axis-group-interface.cc:747-749,:770-773,:798-804 add_grobs_of_one_priority
            //   — padding = outside-staff-padding (default), avoid_outside_staff_collisions
            //   then all_v_skylines.push_back get the same v_skylines,
            //   all_paddings.push_back (padding).
            if (move != 0)
            {
                up.Raise(move);
                down.Raise(move);
            }
            _entries.Add((up, down, registerPadding ?? padding, horizonPadding));
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
