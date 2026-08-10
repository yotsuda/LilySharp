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
using System.Globalization;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Builds vertical and horizontal skylines for collision detection.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline()
/// LILYPOND-REF: lily/skyline.cc
/// </remarks>
internal sealed class SkylineBuilder
{
    private readonly double _staffHeight;

    public SkylineBuilder(double staffHeight)
    {
        _staffHeight = staffHeight;
    }

    /// <summary>
    /// Builds vertical skylines for a multi-staff system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline()
    ///
    /// The skylines track the vertical extent of all music elements:
    /// - UP skyline: highest point at each X position (notes above staff, stems up)
    /// - DOWN skyline: lowest point at each X position (notes below staff, stems down)
    /// </remarks>
    public (VerticalSkyline Up, VerticalSkyline Down) BuildSystemSkylines(
        MultiStaffScore score,
        ImmutableArray<MeasureLayout> measureLayouts,
        double systemHeight = 0,
        double systemLeft = double.NaN,
        ImmutableArray<BeamLayout> firstStaffBeams = default,
        ImmutableArray<BeamLayout> lastStaffBeams = default,
        ImmutableArray<StaffGroupLayout> placed = default)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);
        // Building a system skyline merges ~5-8 boxes PER NOTE; resolving each
        // merge individually is O(K^2) (measured: the dominant layout allocation
        // on large scores). Batch the boxes and resolve once at the end.
        upSkyline.BeginBatch();
        downSkyline.BeginBatch();

        // The two staves the silhouette is built from, AS PLACED. Both the element and its
        // height come from the layout, and they travel together: naming one staff and then
        // measuring at another's height is the frame error HANDOFF 5.2.1 (2) keeps catching.
        var (firstStaff, firstLayout) = OuterStaff(score, placed, fromTop: true);
        var (lastStaff, lastLayout) = OuterStaff(score, placed, fromTop: false);
        // Y-up from the system ORIGIN down to that staff's MIDDLE line — the frame every
        // seed below is in. Half a staff for a five-line staff, 3.750000 for a six-string tab
        // staff, and further still where a text row leads the system.
        double firstStaffMiddleUp = MiddleUp(firstLayout, -_staffHeight / 2);
        // A beamed stem is DRAWN to the quanter's length, which is not what
        // Stem::calc_length gives an unbeamed one — the same "draws right, reserves
        // stale" double model BuildStaffSkylines had between staves. When the caller
        // supplies the drawn beams, the members' fixed stems are suppressed and the
        // beam's own outer edge is seeded instead, exactly as BuildStaffSkylines does.
        // audit/lp-geometry system.beam-{under,over}-notes.
        // LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline —
        //   the system stencil the page spaces by carries the real Beam ink, not a
        //   fixed-length stem model.
        AddEdgeStaffInk(firstStaff, measureLayouts, firstStaffMiddleUp, firstStaffBeams,
            systemLeft, upSkyline, downSkyline);

        // Process bottommost staff for DOWN skyline (elements below the system)
        // LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline
        // Both top and bottom staves contribute to the system's vertical extent.
        double lastStaffMiddleUp = MiddleUp(lastLayout, _staffHeight / 2 - systemHeight);
        // ⚠️ THE SAME ELEMENT, NOT AN EQUAL ONE. This asks whether the bottom edge is a
        // different staff from the top one — the positional question OuterStaff's own remark
        // says this pairing is read with — and `Staff` is a RECORD, so `!=` answered a
        // different one: are their FIELDS equal. A score that puts one part on two staves
        // (`staff melody` written twice) builds two Staff records whose fields are all equal —
        // same clef, same instrument name, and literally the same Voices array instance,
        // because MeasureCollector keys its voice table by part name and the second collection
        // overwrites the first — so value equality called the two ends one staff and the
        // bottom edge seeded NOTHING: neither its ink nor its staff symbol.
        // MEASURED on `staff melody` twice against the same music written as two parts: the
        // system's down extent read 0.000000 where the two-part twin reads 1.545000, the
        // inter-system distance fell to the pair's basic-distance 12.000000 from 20.145000,
        // and the second system was drawn through the first system's lower staff.
        // ⚠️ It is not "a duplicate part anywhere": `bass melody melody` was always right and
        // `melody bass melody` was always wrong, because only the two ENDS are compared.
        // ⚠️ AND IT IS ASKED BY COUNTING, not by reference identity. `OuterStaff` takes
        // StaffGroups[0].Staves[0] and StaffGroups[^1].Staves[^1], so the two ends are the
        // same element exactly when the score has one staff — that is a fact about THIS
        // walk. `!ReferenceEquals` also gives the right answer today, but only because
        // RenderSpec.ToStaffGroups happens to call Staff.Create afresh per entry; resting on
        // that is resting on an invariant of another file, which is the shape HANDOFF 5.2
        // names (the same reason StackStaves asks for GapSpan instead of deciding it).
        // ⚠️ LILYSHARP-OWN: THE TWO-EDGE MODEL ITSELF. LilyPond has no such pair —
        // build_system_skyline merges EVERY element raised by its own translation
        // (page-layout-problem.cc:1093-1108), so an inner staff's ink is in its silhouette
        // too. Lily# takes the outer two because the interior is covered by the staves'
        // own alignment; that holds while the inner staves are no wider than the outer ones.
        int staffCount = 0;
        foreach (var g in score.StaffGroups)
            staffCount += g.StaffCount;
        bool twoEnds = lastStaff is not null && staffCount > 1 && systemHeight > 0;
        if (twoEnds)
            AddEdgeStaffInk(lastStaff, measureLayouts, lastStaffMiddleUp, lastStaffBeams,
                systemLeft, upSkyline, downSkyline);

        // ★ EACH EDGE STAFF SEEDS ITS OWN STAFF SYMBOL — BOTH its lines, not one each
        // (2026-07-28). It used to seed the outer pair, the first staff's TOP and the last
        // staff's BOTTOM, which is the same silhouette exactly while both ends are staves: the
        // lines this adds are interior, and a merge keeps the extreme. MEASURED inert on the
        // whole corpus.
        // ⚠️ WHAT IT IS FOR is the end that is NOT a staff. A text row draws no lines, so with
        // an independent lyrics row under the staff the system had no bottom line at all and
        // the profile under the syllables read 3.000000 (the down-stems) — while LilyPond's
        // floor there decomposes as 2.050000 (that very line) + the syllable's ascender +
        // padding 0.500000 (audit/lp-geometry lyrics.two-staff.two-verse.staff-to-lyric).
        // LILYPOND-REF: lily/page-layout-problem.cc:1093-1108 — build_system_skyline merges
        // EVERY element raised by its own translation, so a StaffSymbol's lines are in the
        // union whatever stands under them.
        // ⚠️ AND AT THE HEIGHT THAT STAFF WAS PLACED AT. The lines are the same element as
        // the notes above, so they take the same middle: the span is read off the layout
        // (a six-string tab staff spans 7.500000, not 4.000000) and the middle is the middle
        // of that span, which is what makes a tab staff's own line the extreme of its
        // silhouette (audit/lp-geometry page.tab-only.first-staff-refpoint).
        var firstSpan = StaffSpan(firstLayout, firstStaffMiddleUp);
        SeedSystemStaffSymbol(measureLayouts, systemLeft,
            seed: firstStaff is not null,
            firstSpan.TopLineY, firstSpan.BottomLineY, upSkyline, downSkyline);
        if (twoEnds)
        {
            var lastSpan = StaffSpan(lastLayout, lastStaffMiddleUp);
            SeedSystemStaffSymbol(measureLayouts, systemLeft,
                seed: true, lastSpan.TopLineY, lastSpan.BottomLineY, upSkyline, downSkyline);
        }

        upSkyline.EndBatch();
        downSkyline.EndBatch();
        return (upSkyline, downSkyline);
    }

    /// <summary>
    /// Seeds ONE edge staff's own ink — its notes, its drawn beams and its clef — into the
    /// system's silhouette. The clef is here with the notes because on a plain score it is
    /// the extreme ink in both directions, further out than any note that stays inside the
    /// staff, so it is what the page's springs are floored by.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1093-1108 <c>build_system_skyline</c> —
    /// each element's own skyline is raised by its own translation and merged, which is the
    /// order this keeps: the staff's ink is built in the staff's frame and only then put
    /// where the staff sits.
    /// <para>
    /// ⚠️ AN OSSIA IS ENGRAVED SMALL and its ink was not, until 2026-07-28. The staff SYMBOL
    /// already came out right because <see cref="StaffSpan"/> reads the placed layout's own
    /// height, but every notehead, stem, beam and clef seeded here read full-size
    /// <c>GlyphMetrics</c> while the renderer draws them inside a 0.7071 group — so an ossia
    /// reserved a full-size staff's room. MEASURED: the page's top spring was floored at
    /// <c>GlyphMetrics.ClefG.Top</c> on BOTH halves of books OSSK/OSSKN and the two solved
    /// the identical force 0.214493, where LilyPond solves 0.212184 against 0.213548 — the
    /// ossia's smaller ink never reached the page at all
    /// (audit/lp-geometry page.ossia-pair.compressed.first-staff-refpoint).
    /// </para>
    /// <para>
    /// ⚠️ SCALED IN ITS OWN FRAME AND RAISED AFTERWARDS, in that order, because the scale is
    /// anchored on the staff's MIDDLE line — the point the renderer's group is anchored on
    /// too. Scaling after the raise would move the staff as well as shrink it.
    /// </para>
    /// </remarks>
    private void AddEdgeStaffInk(
        Staff? staff, ImmutableArray<MeasureLayout> measureLayouts, double staffMiddleUp,
        ImmutableArray<BeamLayout> beams, double systemLeft,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        // ★ THE STAFF'S OWN SIZE, asked for once and carried into the seeds, so that every
        // quantity BELONGING to this staff arrives already at its size and no seed multiplies
        // anything. That is LilyPond's shape (a grob reads its font, and the font is its
        // context's), and it replaced a post-hoc squeeze of the finished silhouette which
        // could only reach the Y axis — see StaffSize.
        var size = StaffSize.Of(staff);
        if (staff is not null)
        {
            AddStaffToSkylines(staff, measureLayouts, staffMiddleUp, upSkyline, downSkyline,
                BeamedItemsToSuppress(beams));
            SeedClef(staff, staffMiddleUp, systemLeft, size, upSkyline, downSkyline);
        }
        AddBeamsToSkyline(beams, staffMiddleUp, size, upSkyline, downSkyline);
    }

    /// <summary>
    /// The system's outermost ELEMENT and the layout that placed it — the two the silhouette
    /// is built from. A text row draws no staff and returns none, exactly as before.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN: THE SELECTION IS DELIBERATELY THE OLD ONE, and it is a KNOWN
    /// deviation rather than an oversight: LilyPond's <c>build_system_skyline</c> merges
    /// EVERY element of the
    /// alignment (page-layout-problem.cc:1093-1108), so a system whose outermost element is a
    /// Lyrics or ChordNames row still has its STAVES in its own silhouette, while this puts
    /// nothing there. Changing it to "the outermost SPACEABLE staff" was tried and MEASURED on
    /// 2026-07-28: it drops book LYRHKG's inter-system floor from 8.900000 to 1.707200 and the
    /// staves interleave, because the lyric rows Lily# leaves OUT of the skyline are then no
    /// longer standing in for the staff that was left out with them. The two exclusions are
    /// one quantity and no corpus point measures either — that is the ▶ item, not this one.
    /// IT GOES the day a point measures a system whose outer element is a row, and the two
    /// exclusions (the staff here, the rows in <c>AugmentExtentsWithLooseLines</c>) go
    /// together or neither.
    /// <para>
    /// ⚠️ WHAT DID CHANGE is the HEIGHT, not the choice: the middle line and the line span now
    /// come from the layout, so a staff that is not four staff spaces tall is seeded where it
    /// was drawn (audit/lp-geometry <c>page.tab-only.first-staff-refpoint</c>).
    /// </para>
    /// <para>
    /// ⚠️ WITHOUT <paramref name="placed"/> there is no layout and the caller keeps its
    /// derived height — the skyline-less overloads, which want only the scalar extents.
    /// </para>
    /// </remarks>
    private static (Staff? Staff, StaffLayout? Layout) OuterStaff(
        MultiStaffScore score, ImmutableArray<StaffGroupLayout> placed, bool fromTop)
    {
        var staff = fromTop
            ? score.StaffGroups[0].PrimaryStaff
            : score.StaffGroups[^1].Staves[^1];
        if (staff.IsTextRow)
            return (null, null);

        // BY POSITION, the same pairing the span used to be read with: the seeds are about
        // StaffGroups[0].Staves[0] and StaffGroups[^1].Staves[^1], and the layouts are yielded
        // in that order, so the layout has to be taken at the SAME end.
        StaffLayout? layout = null;
        if (!placed.IsDefaultOrEmpty)
        {
            foreach (var group in placed)
            {
                if (group.Staves.IsDefaultOrEmpty) continue;
                if (fromTop) { layout = group.Staves[0]; break; }
                layout = group.Staves[^1];
            }
        }
        return (staff, layout);
    }

    /// <summary>Y-up from the system origin down to a placed staff's MIDDLE line.</summary>
    /// <remarks>
    /// <c>MultiStaffLayouter.StaffRefpoint</c> read in this builder's frame.
    /// <para>
    /// ⚠️ A TAB STAFF IS NOT 4.000000 TALL, which is the whole reason the height comes from
    /// the layout: LilyPond's TabStaff sets <c>StaffSymbol.staff-space = 1.5</c> for every
    /// string count, so six strings span <c>(6-1) * 1.5 = 7.500000</c> and its middle line is
    /// 3.750000 below its top one (pinned by <c>tab.staff.line-span.{six,four}-string</c> and
    /// by <c>page.tab-only.first-staff-refpoint</c>).
    /// </para>
    /// <para>
    /// ⚠️ LILYSHARP-OWN: THE FALLBACK TO THE DERIVED VALUE IS A SECOND ANSWER FOR ONE
    /// QUANTITY, the shape HANDOFF 5.2.1② calls the worst — a main path and an approximate
    /// one, where a port can land on the copy that never runs. LilyPond has no such branch:
    /// <c>build_system_skyline</c> is handed the elements as placed
    /// (page-layout-problem.cc:1093-1108) and there is nothing to fall back to. It is here
    /// only because two skyline-less overloads exist for callers that want the scalar extents;
    /// the product path always passes the layouts, so what it serves today is tests, and it
    /// goes when those overloads do.
    /// </para>
    /// </remarks>
    private static double MiddleUp(StaffLayout? layout, double derived)
        => layout is { } lay && lay.Height > 0 ? lay.Y - lay.Height / 2.0 : derived;

    /// <summary>
    /// A placed staff's TOP and BOTTOM line, device-DOWN from the system origin — its middle
    /// widened by half its own span, so the two travel together.
    /// </summary>
    private (double TopLineY, double BottomLineY) StaffSpan(StaffLayout? layout, double middleUp)
    {
        double half = (layout is { } lay && lay.Height > 0 ? lay.Height : _staffHeight) / 2.0;
        return (-middleUp - half, -middleUp + half);
    }

    /// <summary>
    /// Seeds the inter-system skylines with the outer staff LINES over the full
    /// drawn width. The renderer draws the staff lines from the system indent
    /// (under the clef/key prefix — see SharedRenderer), so the system skylines
    /// must carry the same roof/floor: built from music items only, the skyline
    /// was empty left of the first measure, which let a neighbouring system's
    /// left-margin grobs (the line-start bar number) sit level with this
    /// system's staff lines. Text rows (lead-sheet chords/lyrics) print no
    /// staff lines and get no seed. <paramref name="systemLeft"/> = NaN skips
    /// seeding entirely (callers that only read the scalar extents).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline —
    /// each staff's vertical-skyline includes the StaffSymbol grob, whose
    /// stencil spans the whole system width.
    /// </remarks>
    private static void SeedSystemStaffSymbol(
        ImmutableArray<MeasureLayout> measureLayouts, double systemLeft,
        bool seed, double topLineY, double bottomLineY,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (!seed || double.IsNaN(systemLeft) || measureLayouts.IsDefaultOrEmpty)
            return;
        double xRight = double.NegativeInfinity;
        foreach (var ml in measureLayouts)
            xRight = Math.Max(xRight, ml.X + ml.Width);
        if (xRight <= systemLeft)
            return;

        // Staff lines are given in device Y from the system top; translate to the
        // skyline's Y-up frame by negating. The caller names the line's CENTRE, and what
        // belongs in a skyline is its INK, so each outer line reaches half its own
        // thickness further out — the same fact SeedStaffSymbol carries, seen from the
        // system's frame instead of the staff's.
        double halfLine = EngravingDefaults.StaffLineThickness / 2.0;
        upSkyline.Merge(VerticalSkyline.FromBox(
            systemLeft, xRight, -topLineY + halfLine, -topLineY + halfLine,
            VerticalDirection.Up));
        downSkyline.Merge(VerticalSkyline.FromBox(
            systemLeft, xRight, -bottomLineY - halfLine, -bottomLineY - halfLine,
            VerticalDirection.Down));
    }

    // The clef's X comes from EngravingDefaults, not from a literal copied out of the
    // renderer. It was written here as `0.3` first, which took a number that is already
    // Lily#'s own invention and made a SECOND home for it — exactly the habit that let
    // `SystemSpacing * 0.5` sit next to a LILYPOND-REF for years. One home, marked as
    // ours, or the next reader cannot tell which of the two is the real one.

    /// <summary>
    /// Seeds a staff's opening clef into both skylines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-940 skyline_spacing —
    /// inside_staff_skylines carry every inside-staff grob, and a Clef is one, so it
    /// joins the staff's vertical skyline exactly as a notehead does.
    ///
    /// The anchor mirrors <c>SharedRenderer.DrawClef</c>: the glyph sits on the line it
    /// names, which is that clef's middle integer in
    /// LILYPOND-REF: scm/parser-clef.scm supported-clefs (treble G = staff position -2,
    /// bass F = +2, alto C = 0). Keeping the two in one shape is the point — a skyline
    /// that anchors the clef anywhere else reserves space where no ink is.
    /// </remarks>
    private void SeedClef(
        Staff staff, double staffMiddleUp, double systemLeft,
        StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        // NaN means the caller only wants the scalar extents (same contract as
        // SeedSystemStaffSymbol); a text row prints no clef.
        if (double.IsNaN(systemLeft) || staff.IsTextRow)
            return;

        var (glyph, aboveMiddleStaff) = ClefGlyph(staff.Clef);
        // The glyph's OUTLINE, not a box around it: the two profiles of two facing staves are
        // compared pointwise, so the shape between the extremes is part of the answer. See
        // VerticalSkyline.FromGlyphOutline for the measurement that named this.
        // Placed through the RESOLVED-buildings cache below — a clef outline is hundreds
        // of contour edges and this seed runs per (staff, skyline build): spacing builds,
        // the below-pass profiles, the chord/lyric lookups all pay it.
        var (down, up) = GlyphMetrics.ClefVerticalSkylineQuads(glyph);
        // The line the glyph sits on, through THIS staff's own spaces.
        double aboveMiddle = size.Span(aboveMiddleStaff);
        // ⚠️ X here is the glyph ORIGIN. The outline carries its own left edge from there
        // (0.008 on the G clef, -0.052 on the F), so the span is the glyph's, not a box from
        // the origin as it was while this seeded a box. The anchor itself is still the
        // approximation it was: the correct one is the break-align GROUP's left ink
        // (SpacingRules.ClefGroupExtent, ported into DrawClef), a score-wide quantity this
        // method cannot see, holding only two staves. It feeds the VERTICAL skyline, where an
        // X that is off by a fraction costs a little page spacing and no position.
        double x = systemLeft + EngravingDefaults.ClefGlyphXOffset;
        double originUp = aboveMiddle + staffMiddleUp;
        // PERTURBATION (temporary, 2026-07-28): UP back to a box to name the owner of the
        // 0.890000 the hara-kiri snapshot moved by.
        upSkyline.Merge(PlaceGlyphOutlineCached(VerticalDirection.Up, up, size, x, originUp));
        downSkyline.Merge(PlaceGlyphOutlineCached(VerticalDirection.Down, down, size, x, originUp));
    }

    // The RESOLVED baseline-origin glyph profiles, keyed by the baked quad ARRAY (a
    // static singleton per glyph, so reference identity is the key) and the staff
    // magnification. FromGlyphOutline's overlap resolve is the expensive step and is a
    // pure function of (quads, size); a placement is a shift/raise copy — a monotone
    // transform, which commutes with the resolve (the same cache shape
    // TextOutlineSkylines and DynamicOutline already use, and the session-31 +15ms
    // lesson applied here: SeedClef runs per staff per skyline build).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (double[] Quads, VerticalDirection Dir, double Mag), SkylineBuilding[]>
        GlyphOutlineCache = new();

    private static VerticalSkyline PlaceGlyphOutlineCached(
        VerticalDirection direction, double[] quads, StaffSize size, double x, double originUp)
    {
        var resolved = GlyphOutlineCache.GetOrAdd((quads, direction, size.Magnification),
            k => VerticalSkyline.FromGlyphOutline(k.Dir, k.Quads, size, 0, 0)
                .Buildings.ToArray());
        var placed = new SkylineBuilding[resolved.Length];
        double raise = (int)direction * originUp;
        for (int i = 0; i < resolved.Length; i++)
            placed[i] = resolved[i].ShiftedHorizon(x).RaisedBy(raise);
        return VerticalSkyline.FromResolvedBuildings(direction, placed);
    }

    /// <summary>
    /// Which clef GLYPH is engraved, and the staff-spaces above the MIDDLE line its glyph
    /// origin sits at — <c>SharedRenderer.DrawClef</c>'s <c>staffY - n</c> read in this frame,
    /// where the top line is 2 above the middle, so it is <c>2 - n</c>.
    /// </summary>
    /// <remarks>
    /// The percussion clef has no outline of its own in the baked skylines, so it borrows the
    /// C clef's. Both are centred on the middle line and the C clef is the taller, so this
    /// over-reserves rather than under-reserves — a KNOWN approximation, and the one clef here
    /// whose silhouette is not the font's own.
    /// <para>
    /// ⚠️ THE GLYPH, NOT A BOX, because this feeds a SKYLINE. Until 2026-07-28 the caller
    /// seeded the OUTLINE'S BOUNDING BOX, which is right about how far the clef reaches and
    /// silent about WHERE, and a skyline distance is decided pointwise. See
    /// <see cref="VerticalSkyline.FromGlyphOutline"/>, and
    /// LILYPOND-REF: scm/define-grobs.scm:902-927 <c>grob::always-vertical-skylines-from-stencil</c>
    /// is what the Clef declares, and it is what routes a clef through the outline at all.
    /// </para>
    /// </remarks>
    private static (string Glyph, double AboveMiddle) ClefGlyph(ClefType clef) => clef switch
    {
        ClefType.Bass or ClefType.Bass8Below => ("F", 1.0),
        ClefType.Alto => ("C", 0.0),
        ClefType.Tenor => ("C", 1.0),
        ClefType.Soprano => ("C", -2.0),
        ClefType.MezzoSoprano => ("C", -1.0),
        ClefType.Baritone => ("C", 2.0),
        ClefType.Percussion => ("C", 0.0),
        _ => ("G", -1.0),
    };

    /// <remarks>
    /// ⚠️ A TEXT ROW HAS NO MUSIC INK. A lead sheet's chords/lyrics track carries a VOICE —
    /// that is what gives it the bar grid and the timings its symbols are placed by — but the
    /// renderer draws none of it (<c>SharedRenderer</c>: a text row draws no staff lines, no
    /// clef and no notes, only its text). Seeding those noteheads reserved ink that is never
    /// drawn: measured 2026-07-27, a chords row of whole notes put a flat ±0.500000 band in
    /// its skyline while its actual symbols were in no skyline at all. The row's OWN ink is
    /// seeded by <c>MultiStaffLayouter.BuildAllStaffSkylines</c> from the chord symbols.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-940 <c>skyline_spacing</c> — a group's
    /// skyline is its MEMBERS' stencils, and a ChordNames group's members are ChordNames.
    /// </remarks>
    private void AddStaffToSkylines(
        Staff staff, ImmutableArray<MeasureLayout> measureLayouts,
        double staffMiddleUp,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline,
        IReadOnlySet<(int Voice, int Measure, int Item)>? suppressStems = null,
        IReadOnlyDictionary<RestShiftKey, double>? restShifts = null)
    {
        if (staff.IsTextRow)
            return;

        // A TAB staff draws FRET DIGITS, not noteheads and stems at pitch positions, so the
        // notation seeds below describe ink that is never drawn — and, worse, omit the ink
        // that IS. Everything that reads a staff's silhouette (the chord row, the lyric row,
        // inter-system distance) was therefore placed against a tab staff it could not see.
        // MEASURED on test/tab-with-chords: growing the digits from 2.6 to 3.3 left the chord
        // name's baseline at y=11.70 to the hundredth — it had never depended on them — while
        // the digits grew up past it. The overlap was latent at 2.6 too, with 0.894 of digit
        // above the top line against 0.65 of clearance; enlarging them only made it visible.
        if (staff.IsTab)
        {
            AddTabStaffToSkylines(staff, measureLayouts, staffMiddleUp, upSkyline, downSkyline);
            return;
        }

        // The rest-dot column memo, once per staff (a static CWT hit) — the per-item
        // read below is a plain dictionary lookup.
        var restDotOffsets = ElementCoordinator.RestDotOffsetsOf(staff);

        // A voice the collision pass pushed sideways is reserved WHERE IT IS DRAWN.
        // The renderer adds this shift to every head, stem and ledger X it draws
        // (SharedRenderer via LayoutEngine.CalculateVoiceCollisions), so a seed without
        // it puts the whole shifted column one head-width from its ink — and the gap to
        // the next staff rests on ink that is not there. The answer is the one memoised
        // home the spacing pass reads too (SpacingRules.VoiceCollisionShiftsOf), not a
        // recomputation. LILYPOND-REF: lily/note-collision.cc calc_positioning_done runs
        // before vertical spacing reads the grobs, so LilyPond's skylines carry the
        // shift by construction. Measured: stems-clash-between-staves.ly, where the
        // shifted voice's down stem is the whole inter-staff constraint.
        // (Empty → null, so a shift-less multi-voice staff — e.g. a rests-only second
        // voice — pays no per-item lookup at all.)
        var collisionShifts = staff.Voices.Length >= 2
            && SpacingRules.VoiceCollisionShiftsOf(staff) is { IsEmpty: false } shifts
            ? shifts
            : null;

        for (int vi = 0; vi < staff.Voices.Length; vi++)
        {
            var voice = staff.Voices[vi];
            // Iterate over measureLayouts (which are for the current system only).
            // Use MeasureLayout.MeasureIndex to look up the correct voice measure.
            for (int layoutIndex = 0; layoutIndex < measureLayouts.Length; layoutIndex++)
            {
                var measureLayout = measureLayouts[layoutIndex];
                int measureIndex = measureLayout.MeasureIndex;

                if (measureIndex >= voice.Measures.Length)
                    continue;

                // Inside a voice { } span the stems are forced by voice (v1 up,
                // v2 down, ...), exactly as the renderer draws them. The note's own
                // pitch-based StemUp is wrong for the skyline then — a low bass note
                // in voice 2 is drawn stem-DOWN but its natural direction is up, so
                // its down-stem would be missing from the down-skyline and
                // lyrics/staves below would collide. Outside the span nothing forces
                // it.
                // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
                bool? forcedStemUp = VoiceDefaults.GetDefaultStemUpAt(
                    staff.Voices, vi, measureIndex);

                var measure = voice.Measures[measureIndex];
                for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
                {
                    if (measureLayout.Columns.IsDefaultOrEmpty
                        && itemIndex >= measureLayout.Items.Length)
                        continue;

                    var item = measure.Items[itemIndex];
                    double itemX = measureLayout.X + LayoutUtilities.GetItemXOffset(
                        voice.Measures, measureIndex, itemIndex, measureLayout);

                    // ...plus the drawn collision shift (VoiceId is 1-based), RAW — not
                    // through StaffSize.Span. The renderer adds this offset to itemX in
                    // UNSCALED page X even on an ossia (SharedRenderer wraps ossia drawing
                    // in UnscaledXDrawingContext, so the X axis never scales, and voiceX is
                    // the full-size head width there too — a pre-existing renderer
                    // simplification this seed must MATCH, not correct). Scaling it here
                    // was tried first, from the seed rule "a staff-owned length scales":
                    // identical on a plain staff (Span is the identity), wrong by
                    // 1 − magstep(-3) of a head on an ossia's shifted voice. No corpus
                    // point reaches an ossia multi-voice collision yet — the audit caught
                    // it, not a measurement.
                    if (collisionShifts is not null
                        && collisionShifts.TryGetValue(
                            new VoiceItemKey(measureIndex, vi + 1, itemIndex), out var collisionShift))
                        itemX += collisionShift;

                    // A beamed note whose beam is seeded (AddBeamsToSkyline) must NOT also
                    // reserve an unbeamed stem, or the stale over-reservation would win.
                    bool reserveStem = suppressStems is null
                        || !suppressStems.Contains((vi, measureIndex, itemIndex));

                    // ...and a rest that another voice pushed out of the staff is reserved
                    // WHERE IT WAS PUSHED TO. Without this the seed reads the rest's default
                    // position while the renderer draws it elsewhere — the same two-spellings
                    // shape that made the whole rest seed unable to bind anything
                    // (audit/lp-geometry staff.staff.rest-under-notes).
                    double restShiftUp = restShifts is null
                        ? 0.0
                        : restShifts.TryGetValue(
                            new RestShiftKey(measureIndex, vi, itemIndex), out var rs)
                            ? rs * 0.5
                            : 0.0;

                    // ...and its DOT where the dot column put it (relative to the rest,
                    // so it rides the same shift) — the renderer's DrawRest reads the
                    // identical memo, which is what keeps this a seed and not a second
                    // spelling. Null falls to the solo default inside. The memo is
                    // fetched ONCE per staff (restDotOffsets above), not per item —
                    // this loop is per keystroke.
                    int? restDotRel = item is RestItem { Dots: > 0 }
                        && restDotOffsets.TryGetValue(
                            new RestShiftKey(measureIndex, vi, itemIndex), out var rd)
                        ? rd
                        : null;

                    AddMusicItemToSkylines(item, itemX, staffMiddleUp, StaffSize.Of(staff),
                        upSkyline, downSkyline, forcedStemUp, reserveStem, restShiftUp,
                        restDotRel);
                }
            }
        }
    }

    /// <summary>
    /// A TAB staff's own ink: one box per fret digit, on the string line it is drawn on.
    /// </summary>
    /// <remarks>
    /// The tab analogue of <see cref="AddNoteBoxToSkylines"/>, and it exists for the same
    /// reason that one seeds a notehead's LILC ink rather than a nominal staff space — the
    /// silhouette has to be what gets drawn. A digit is centred on its string line and is
    /// <see cref="TabConstants.FretDigitHeight"/> tall, which is more than the 1.5 string gap,
    /// so the digits on the OUTER strings are the extreme of a tab staff's silhouette in both
    /// directions. Everything else about the staff (stems, beams, articulations) is seeded by
    /// the shared paths, which already read the tab geometry.
    /// <para>
    /// The string a note lands on is asked of <c>Tunings.CalculateFret</c> — the same call the
    /// renderer makes, with the same sounding shift — so the box is at the drawn string and
    /// not at a pitch position. The width follows the digit count for the same reason: a
    /// two-digit fret really is wider, and the chord row above it should know.
    /// </para>
    /// </remarks>
    private static void AddTabStaffToSkylines(
        Staff staff, ImmutableArray<MeasureLayout> measureLayouts, double staffMiddleUp,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        int[] tuning = Tunings.GetTuning(staff.Tuning ?? TuningType.Guitar);
        if (tuning.Length == 0)
            return;
        double space = EngravingDefaults.TabStringSpace(tuning.Length);
        // String 1 is the TOP line; the middle of the span is this staff's reference point.
        double topLineUp = staffMiddleUp + (tuning.Length - 1) * space / 2.0;
        int shift = Tunings.SoundingShift(staff.TabSourceClef, staff.Transposition);
        double half = TabConstants.FretDigitHeight / 2.0;

        foreach (var voice in staff.Voices)
        {
            foreach (var measureLayout in measureLayouts)
            {
                int measureIndex = measureLayout.MeasureIndex;
                if (measureIndex >= voice.Measures.Length)
                    continue;
                var measure = voice.Measures[measureIndex];
                for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
                {
                    var item = measure.Items[itemIndex];
                    // The digit sits under the notehead CENTRE, the same offset the renderer
                    // draws it at (SharedRenderer.Tab, TabHeadCenterOffset).
                    double x = measureLayout.X
                        + LayoutUtilities.GetItemXOffset(
                            voice.Measures, measureIndex, itemIndex, measureLayout)
                        + EngravingDefaults.TabHeadCenterOffset;
                    foreach (var (midi, preferred) in TabSoundingNotes(item))
                    {
                        var (stringNum, fret) = Tunings.CalculateFret(midi + shift, tuning, preferred);
                        double lineUp = topLineUp - (stringNum - 1) * space;
                        double width = TabConstants.FretGlyphWidth(
                            fret.ToString(CultureInfo.InvariantCulture), TabConstants.FretFontSize);
                        upSkyline.Merge(VerticalSkyline.FromBox(
                            x - width / 2, x + width / 2, lineUp - half, lineUp + half,
                            VerticalDirection.Up));
                        downSkyline.Merge(VerticalSkyline.FromBox(
                            x - width / 2, x + width / 2, lineUp - half, lineUp + half,
                            VerticalDirection.Down));
                    }
                }
            }
        }
    }

    /// <summary>The (midi, preferred string) of every fret digit an item draws.</summary>
    private static IEnumerable<(int Midi, int PreferredString)> TabSoundingNotes(MusicItem item)
    {
        switch (item)
        {
            case NoteItem note:
                yield return (note.Midi, note.StringNumber ?? 0);
                break;
            case ChordItem chord:
                foreach (var n in chord.Notes)
                    yield return (n.Midi, n.StringNumber ?? 0);
                break;
        }
    }

    /// <summary>
    /// Builds vertical skylines for a single staff about its REFERENCE POINT — Y=0 at the
    /// middle line, the frame LilyPond's own VerticalAxisGroup skylines are in. Used for
    /// staff-to-staff spacing within a multi-staff system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 — per-staff skylines for spacing
    /// LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline()
    /// <para>
    /// ⚠️ THE ORIGIN IS THE MIDDLE LINE (changed 2026-07-27; it was the TOP line before).
    /// Every distance <c>Align_interface</c> computes is between two VerticalAxisGroup
    /// reference points, and a staff's reference point is its middle line — MEASURED, the
    /// probe dumps a staff group's extent as (-3.550000 . 3.800000), symmetric about the
    /// middle and not about the top. While this built about the top line, every consumer that
    /// wanted LilyPond's number added a half-staff back at its own call site, written out
    /// separately each time: a port acquires one frame adapter per call, and one of them
    /// eventually gets forgotten or doubled.
    /// </para>
    /// <para>
    /// ⚠️ STAFF-TO-STAFF DISTANCES DID NOT MOVE, and could not have: every staff's skyline
    /// shifts by the same <c>_staffHeight / 2</c> (this builder has ONE staff height, not one
    /// per staff), so <c>Skyline::distance</c> between any two of them is unchanged. What
    /// moved is the meaning of a single skyline's own numbers, so the consumers that read a
    /// HEIGHT out of one — rather than a distance between two — are the ones that changed
    /// with it: <c>LyricEngraver</c> (whose two adapters this removed) and the chord-row
    /// lookup in <c>LayoutEngine</c> (which now reflects once, at the edge).
    /// </para>
    /// <para>
    /// ⚠️ THE CLEF IS IN IT, and it was not until 2026-07-27. <see cref="SeedClef"/> used to
    /// be called only from <c>BuildSystemSkylines</c>, so the per-staff silhouette that
    /// <c>Align_interface</c> walks — staff-to-staff spacing, and the room a loose-line
    /// block is solved into — had never carried the one grob that reaches furthest out of
    /// a plain staff. LILYPOND-REF: lily/axis-group-interface.cc:914-940
    /// <c>skyline_spacing</c> — the inside-staff skylines carry every inside-staff grob and
    /// a Clef is one, the same sentence <see cref="SeedClef"/>'s own remark quotes.
    /// MEASURED on probe book LYRB before the fix: an up-height of 3.000000, which is a′'s
    /// stem tip and nothing else, where LilyPond dumps a VerticalAxisGroup up-extent of
    /// 3.800000 — its clef and its stems. The metric was never wrong: with
    /// <c>GlyphMetrics.ClefG</c> at (-2.550000 . 4.800000) on the G line at -1.000000 this
    /// now reads 3.800000, LilyPond's number exactly.
    /// ⚠️ <paramref name="systemLeft"/> IS WHERE THE CLEF IS, and NaN means "no clef" —
    /// the same contract <see cref="SeedClef"/> and <c>SeedSystemStaffSymbol</c> already
    /// had, for callers that want only the scalar extents.
    /// </para>
    /// <para>
    /// ⚠️ THE SEEDS ARRIVE IN TWO STAFF-LOCAL FRAMES and always did — see
    /// <c>StaffSkylineFrameTests</c>, which names each one. The staff symbol, noteheads,
    /// dynamics, articulations and beams are placed about the staff MIDDLE
    /// (<c>staffMiddleUp</c>); tuplet brackets, slurs and ties come from engravers run with no
    /// staff offset and are about the staff TOP (<c>staffTopUp</c>). The two read alike only
    /// while this origin was the top line, which is why the difference had never mattered.
    /// </para>
    /// </remarks>
    public (VerticalSkyline Up, VerticalSkyline Down) BuildStaffSkylines(
        Staff staff, ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<DynamicItem> dynamics = default,
        ImmutableArray<ArticulationLayout> articulationLayouts = default,
        ImmutableArray<TupletBracketLayout> tupletBrackets = default,
        ImmutableArray<SlurLayout> slurs = default,
        ImmutableArray<TieLayout> ties = default,
        ImmutableArray<BeamLayout> beams = default,
        double systemLeft = double.NaN,
        IReadOnlyDictionary<RestShiftKey, double>? restShifts = null)
        => PlaceDynamicsOn(
            BuildInsideStaffSkylines(staff, measureLayouts, articulationLayouts,
                tupletBrackets, slurs, ties, beams, systemLeft, restShifts),
            staff, dynamics, measureLayouts, beams, copyFirst: false,
            articulationLayouts: articulationLayouts);

    /// <summary>
    /// THE inside-staff skyline: every grob that declares no <c>outside-staff-priority</c> —
    /// the staff symbol, the clef, the notes (with the shifts a rest collision gave them),
    /// the scripts, tuplet brackets, slurs, ties and beams. Nothing that the outside-staff
    /// pass places is in it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines — the
    ///   elements whose priority is unset, collected ONCE; :952-972
    ///   add_grobs_of_one_priority then places the rest against it in priority order.
    /// <para>
    /// ★ ONE PER (system, staff), and every consumer reads the same object. Lily# used to
    /// rebuild this silhouette at five call sites, each passing its own subset of the side
    /// tables, which is the shape HANDOFF §5.2.1② names: the subsets could not agree, and
    /// they did not — the chain's closing staff and the chord row were missing scripts and
    /// beams, the stacker's seed was missing scripts. It also cost what a rebuild costs:
    /// measured on a 512-script page, one script's profile was merged FOUR times per layout.
    /// </para>
    /// </remarks>
    public (VerticalSkyline Up, VerticalSkyline Down) BuildInsideStaffSkylines(
        Staff staff, ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<ArticulationLayout> articulationLayouts = default,
        ImmutableArray<TupletBracketLayout> tupletBrackets = default,
        ImmutableArray<SlurLayout> slurs = default,
        ImmutableArray<TieLayout> ties = default,
        ImmutableArray<BeamLayout> beams = default,
        double systemLeft = double.NaN,
        IReadOnlyDictionary<RestShiftKey, double>? restShifts = null,
        ImmutableArray<FingeringLayout> fingerings = default)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);

        // The middle line IS the origin: every helper that takes this places its ink relative
        // to the staff middle, so this one number says which frame the result is in.
        const double staffMiddleUp = 0;
        // ...and THIS staff's size says what a staff-space is worth here. Asked for once, at
        // the top, and carried into the seeds — LilyPond's own shape, where the size lives on
        // the context's font rather than being applied to a finished skyline (see StaffSize).
        var size = StaffSize.Of(staff);
        // ...and the step to the OTHER staff-local frame, for the seeds whose engravers were
        // run with no staff offset and so measure from the staff's TOP line. Derived from the
        // origin rather than written as a half-staff, so the two cannot drift apart: it was 0
        // while the origin was the top line and is the half-staff now that it is the middle.
        double staffTopUp = staffMiddleUp + size.Span(_staffHeight / 2.0);

        // A beamed stem is DRAWN to whatever length the quanter gives its beat, which is not
        // what Stem::calc_length gives an unbeamed one — the "draws right, reserves stale"
        // double model. When the drawn beams are known, the
        // beamed members' fixed stems are SUPPRESSED here and the beam's own outer edge is
        // seeded instead (AddBeamsToSkyline), the way AddTiesToSkyline seeds the drawn bow.
        // audit/lp-geometry staff.staff.beam-{under,over}-notes.
        var suppressStems = BeamedItemsToSuppress(beams);

        // Everything up to the dynamics is a pure ACCUMULATION — one skyline built from many
        // boxes and outlines, with nothing reading it in between — which is what the batch
        // contract is for: append now, resolve once at EndBatch, instead of re-resolving the
        // whole profile at each of a staff's hundreds of seeds. Byte-identical (the resolve
        // keeps the highest at each point and max is commutative), and the same lever the
        // figure row's stacking needed for the same reason.
        // ⚠️ THE BATCH ENDS BEFORE AddDynamicsToSkyline, which READS the accumulated down
        // profile (its collision pass) and must see each dynamic it merges.
        upSkyline.BeginBatch();
        downSkyline.BeginBatch();

        // The staff symbol itself (the 5 lines, ±StaffHeight/2 around the middle)
        // is part of LilyPond's VerticalAxisGroup skyline, so adjacent staves are
        // spaced to clear each other's STAFF LINES — not just their notes. Seed it
        // first as the baseline; notes/ledgers then extend it outward.
        // LILYPOND-REF: lily/axis-group-interface.cc:914-940 skyline_spacing —
        //   inside_staff_skylines include the StaffSymbol grob.
        // ⚠️ A TEXT ROW HAS NO StaffSymbol GROB. A lead sheet's chords/lyrics track prints
        // no staff lines — the fact SeedSystemStaffSymbol and SeedClef already carry — so it
        // has none to put in a skyline. Until 2026-07-27 this seeded a full five-line box
        // (±2.050000) for a row that draws nothing: ink reserved where there is none, and
        // the reason a text row's skyline looked populated while the row's OWN ink was in no
        // skyline at all (MultiStaffLayouter.BuildAllStaffSkylines seeds that).
        if (!staff.IsTextRow)
            SeedStaffSymbol(measureLayouts, staffMiddleUp, size, upSkyline, downSkyline);

        // ...and the clef, which on a plain staff is the extreme ink in BOTH directions —
        // further out than any note that stays inside the staff. The same seed the system
        // silhouette gets, in this staff's own frame.
        // LILYPOND-REF: lily/axis-group-interface.cc:914-940 skyline_spacing — the
        // inside-staff skylines carry every inside-staff grob, and a Clef is one, so it is
        // in the vertical skyline Align_interface walks exactly as a notehead is.
        SeedClef(staff, staffMiddleUp, systemLeft, size, upSkyline, downSkyline);

        AddStaffToSkylines(staff, measureLayouts, staffMiddleUp,
            upSkyline, downSkyline, suppressStems, restShifts);

        // A tab staff's above/below Scripts (fermata, flageolet, accent, …) are
        // engraved only after spacing, so they were absent from this skyline and a
        // forced-above fermata dropped into the gap onto the staff above's low
        // noteheads. Their staff-local extent is spacing-independent, so seed it now.
        AddArticulationLayoutsToSkyline(articulationLayouts, staffMiddleUp, size, upSkyline, downSkyline);

        // ...and the FINGERINGS beside them, for the same reason and by the same rule: a
        // Fingering declares no outside-staff-priority, so it is never MOVED by the collision
        // pass and is always IN the profile that pass starts from.
        // LILYPOND-REF: lily/axis-group-interface.cc:541-561 internal_calc_pure_relevant_grobs — a grob with no
        //   outside-staff-priority is left at -infinity and so is not a mover; :914-950
        //   inside_staff_skylines collects exactly those, and add_grobs_of_one_priority then
        //   places the movers against them.
        // ⚠️ NOTHING RESERVED A FINGERING UNTIL 2026-08-10, and the defect was reported by
        // eye before it was measured: on corpus book chord-repetition the down digit and the
        // \p printed on top of each other. MEASURED (ledger fingering.chord.dynamic-*, books
        // DYF/DYN/DYU): LilyPond puts that dynamic 1.495999 further down when the digit is
        // there and NOT ONE THOUSANDTH further when the same book's fingering goes up
        // instead — the pass, pointwise, clearing real ink.
        AddFingeringsToSkyline(fingerings, staffMiddleUp, size, upSkyline, downSkyline);

        // A tuplet bracket is ordinary ink INSIDE the staff's own axis group in LilyPond,
        // so the staff below must clear it exactly as it clears the clef. Nothing seeded
        // it here before, and the consequence was visible rather than theoretical: a
        // bracket over notes reaching towards the neighbour was drawn straight across that
        // neighbour's staff lines.
        AddTupletBracketsToSkyline(tupletBrackets, staffTopUp, size, upSkyline, downSkyline);

        // A slur is an ordinary inside-staff grob in LilyPond too (no
        // outside-staff-priority), so it joins the staff's vertical skyline like the
        // clef and the tuplet bracket, and the staff below must clear its bow. Nothing
        // seeded it here before; the gap rested on the notes and a slur drooping into it
        // was reserved nowhere. audit/lp-geometry staff.staff.slur-{under,over}-notes.
        AddSlursToSkyline(slurs, staffTopUp, size, upSkyline, downSkyline);

        // A tie is the same kind of grob as the slur — vertical-skylines from its stencil,
        // no outside-staff-priority (scm/define-grobs.scm Tie) — so it joins the staff's own
        // skyline too, and a staff below must clear its bow. Ties were reserved nowhere;
        // audit/lp-geometry staff.staff.tie-{under,over}-notes measured the gap resting on
        // the notes alone (-0.560901).
        AddTiesToSkyline(ties, staffTopUp, size, upSkyline, downSkyline);

        // A beam is ordinary ink inside the staff's own axis group (scm/define-grobs.scm Beam
        // carries vertical-skylines from its stencil and sets no outside-staff-priority), so a
        // staff below must clear its outer edge exactly as it clears a tuplet bracket. The
        // members' fixed stems were suppressed above; this seeds the real drawn geometry.
        AddBeamsToSkyline(beams, staffMiddleUp, size, upSkyline, downSkyline);

        // ...and the accumulation is complete: resolve once, before the one pass that reads.
        upSkyline.EndBatch();
        downSkyline.EndBatch();

        return (upSkyline, downSkyline);
    }

    /// <summary>
    /// The outside-staff pass at priority 250: places each dynamic against the accumulated
    /// inside-staff profile and merges its ink back in, exactly as LilyPond's
    /// <c>add_grobs_of_one_priority</c> does.
    /// </summary>
    /// <remarks>
    /// Dynamics LAST — they are OUTSIDE-staff grobs: LilyPond first builds the inside-staff
    /// skyline and then places each outside-staff grob against it, so a below dynamic's final
    /// position needs the full inside profile (the beam face is what pushes it in the DSB
    /// regime). The seed runs the same two steps the draw runs — the pointwise side-position
    /// quiet position, then the outside-staff collision pass over the accumulated down
    /// profile — and merges the label's own outline so the inter-staff gap reserves it.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-950 skyline_spacing —
    ///   inside skylines first, then add_grobs_of_one_priority in priority order.
    /// audit/lp-geometry staff.staff.dynamic-{head-support,beam-avoid,stem-binding}.
    /// <para>
    /// ⚠️ <paramref name="copyFirst"/> is what lets the inside skyline be SHARED: this step
    /// mutates, so a caller holding the one per-(system, staff) inside profile must not hand
    /// it in directly. Copying a resolved skyline is a list copy; rebuilding it is hundreds
    /// of seeds and a resolve.
    /// </para>
    /// </remarks>
    public (VerticalSkyline Up, VerticalSkyline Down) PlaceDynamicsOn(
        (VerticalSkyline Up, VerticalSkyline Down) inside,
        Staff staff, ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<BeamLayout> beams = default, bool copyFirst = true,
        ImmutableArray<ArticulationLayout> articulationLayouts = default)
    {
        var upSkyline = copyFirst ? Copy(inside.Up) : inside.Up;
        var downSkyline = copyFirst ? Copy(inside.Down) : inside.Down;
        var size = StaffSize.Of(staff);
        // Priority 75 FIRST, then 250 — LilyPond's ascending order. The fermata family is
        // the only script that declares one, and the room reserves it where its engraver put
        // it (Lily#'s outside-staff pass runs later than this; see
        // AddArticulationLayoutsToSkyline's remark).
        AddArticulationLayoutsToSkyline(articulationLayouts, staffMiddleUp: 0, size,
            upSkyline, downSkyline, moversInstead: true);
        AddDynamicsToSkyline(staff, dynamics, measureLayouts, staffMiddleUp: 0,
            size, upSkyline, downSkyline, beams);
        return (upSkyline, downSkyline);
    }

    /// <summary>An independent copy of a RESOLVED skyline — the cheap half of sharing one
    /// inside-staff profile between the consumers that mutate their own view of it.</summary>
    /// <remarks>
    /// ⚠️ "CHEAP" IS MEASURED, NOT ASSUMED (2026-08-05, session 93), because a handoff item
    /// had it named as the island's whole perf debt and the fix as "stop copying". COUNTED
    /// over one steady-state layout: a 256-script four-staff book makes 32 copies totalling
    /// 7 632 buildings = 238.50 KB against 80 972 KB of layout allocation — 0.29%. The
    /// script-free control is 62.50 KB of 11 907 KB and a tab book 76.12 KB of 17 545 KB.
    /// ⇒ REMOVING EVERY COPY CANNOT RETURN AN 11% DEBT. Whatever that number is, it is the
    /// profile's CONTENTS (a script is ~10 buildings of padded outline where it used to be
    /// one flat box, which is fidelity and not waste), not the sharing. Do not rewrite
    /// VerticalSkyline into an offset view on the strength of the old diagnosis.
    /// </remarks>
    internal static VerticalSkyline Copy(VerticalSkyline sky)
        => VerticalSkyline.FromResolvedBuildings(sky.Direction, sky.Buildings);

    /// <summary>
    /// The (voice, measure, item) keys of notes whose FIXED per-note stem must be suppressed
    /// because their beam's real geometry is seeded instead (see <see cref="AddBeamsToSkyline"/>).
    /// A beamed stem is drawn to the quanter's length, not <c>DefaultStemLength</c>, so leaving
    /// the fixed 3.5 stem in the box would over-reserve it and the seed would never bind.
    /// </summary>
    /// <remarks>
    /// A CROSS-STAFF beam's members are suppressed WITHOUT a seed: LilyPond leaves
    /// cross-staff grobs out of the vertical skylines altogether — the beam cannot be
    /// formatted until the staff distance is known, so spacing deliberately ignores it —
    /// and a stem whose beam is cross-staff is itself cross-staff, so the stems are out
    /// too (only the noteheads remain, and those this builder reserves regardless). The
    /// fixed 3.5 stem Lily# used to keep for them reserved ink LilyPond does not.
    /// LILYPOND-REF: lily/axis-group-interface.cc:850-858 ("we just leave cross-staff
    ///   grobs out of the skyline altogether"), :921,:954 skyline_spacing skips
    ///   cross-staff elements; lily/stem.cc:1278-1290 Stem::is_cross_staff — a stem is
    ///   cross-staff iff its beam is.
    /// A same-staff KNEED beam still keeps the per-note fixed stem: LilyPond DOES carry
    /// its real Beam/Stem stencils in the skylines, but Lily# has no faithful model of
    /// the knee's stencil band yet (a knee has stems on BOTH sides, so neither of
    /// OuterEdgeStaffSpaceAtX's two faces names its band), so porting it is deferred
    /// until the band is measured from LP — see HANDOFF §1.
    /// audit/lp-geometry staff.staff.beam-{under,over}-notes.
    /// </remarks>
    internal static HashSet<(int Voice, int Measure, int Item)> BeamedItemsToSuppress(
        ImmutableArray<BeamLayout> beams)
    {
        var set = new HashSet<(int, int, int)>();
        if (beams.IsDefaultOrEmpty)
            return set;
        foreach (var b in beams)
        {
            var g = b.Group;
            if (g.IsKnee && !g.IsCrossStaff)
                continue;
            foreach (var m in g.Members)
                set.Add((g.VoiceIndex, m.ResolveMeasureIndex(g.MeasureIndex), m.ItemIndex));
        }
        return set;
    }

    /// <summary>
    /// Seeds the drawn beams into the per-staff skylines, so the inter-staff gap reserves the
    /// real beam geometry rather than the fixed stem <see cref="AddNoteBoxToSkylines"/> would
    /// give each member. The analogue of <see cref="AddTupletBracketsToSkyline"/> and
    /// <see cref="AddTiesToSkyline"/> for the beam.
    /// </summary>
    /// <remarks>
    /// The outer edge is <see cref="BeamLayout.OuterEdgeStaffSpaceAtX"/> — the stem tip plus
    /// half <c>Beam.thickness</c> (plus the stack for multiple beams) — the one canonical line
    /// scripts, slurs and tuplet brackets already clear a beam by, so the reservation and the
    /// drawing come from ONE computation. It is Y-up from the staff MIDDLE, so it is rebased
    /// to this skyline's origin (the staff top) by <c>+ staffMiddleUp</c>, exactly as the note
    /// boxes are. A beam sits entirely on its stems' side, so it joins only that direction's
    /// skyline, like the tuplet bracket. A CROSS-STAFF beam is not seeded — and its members'
    /// stems are suppressed all the same — because LilyPond leaves cross-staff grobs out of
    /// the skylines entirely (see <see cref="BeamedItemsToSuppress"/> for the references).
    /// A same-staff KNEED beam is not seeded either; its members keep the per-note fixed stem
    /// until the knee's stencil band is measured from LP (same remark).
    /// LILYPOND-REF: scm/define-grobs.scm Beam (vertical-skylines from the stencil,
    /// thickness 0.48); lily/beam.cc — a beamed stem ends at the beam's outer face.
    /// </remarks>
    internal static void AddBeamsToSkyline(
        ImmutableArray<BeamLayout> beams, double staffMiddleUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (beams.IsDefaultOrEmpty)
            return;
        foreach (var b in beams)
        {
            var g = b.Group;
            if (g.IsCrossStaff || g.IsKnee)
                continue;
            double xLeft = Math.Min(b.LeftX, b.RightX);
            double xRight = Math.Max(b.LeftX, b.RightX);
            if (xRight <= xLeft)
                continue;

            bool stemUp = g.StemUp;
            // The beam's edge is given in the STAFF's staff-spaces (the quanter works in
            // them), so it arrives at this staff's size; its X is the drawn column and does not.
            double yLeft = size.Span(b.OuterEdgeStaffSpaceAtX(xLeft, stemUp)) + staffMiddleUp;
            double yRight = size.Span(b.OuterEdgeStaffSpaceAtX(xRight, stemUp)) + staffMiddleUp;
            var direction = stemUp ? VerticalDirection.Up : VerticalDirection.Down;
            var sky = stemUp ? upSkyline : downSkyline;
            sky.Merge(VerticalSkyline.FromSlope(xLeft, yLeft, xRight, yRight, thickness: 0, direction));
        }
    }

    /// <summary>
    /// Seeds the drawn tuplet bracket lines into the per-staff skylines, so the
    /// inter-staff gap reserves the room they occupy.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket — it carries
    /// <c>vertical-skylines</c> built from its own stencil and, although it lists
    /// <c>outside-staff-interface</c>, it sets NO <c>outside-staff-priority</c>, so
    /// <c>lily/axis-group-interface.cc</c> never pushes it out and it joins the staff's
    /// INSIDE skyline like the clef and the staff symbol do.
    /// <para>
    /// The layouts arrive in this skyline's own frame: <c>TupletBracketLayout.*YUp</c> is
    /// Y-up from the staff top line whenever the engraver is run without a staff offset,
    /// which is how the caller runs it here. Only the bracket LINE is seeded — the edge
    /// hooks point INWARD, towards the notes, and never reach the outward side (LilyPond's
    /// own grob reports extent (4.365 . 5.225) about positions 5.145: 0.08 out, 0.78 in).
    /// </para>
    /// <para>
    /// THE NUMBER IS THE OUTERMOST INK, not the bracket, and it is seeded too.
    /// <c>lily/tuplet-number.cc:342</c> gives the TupletNumber the bracket's own midpoint
    /// as its Y-offset for any tuplet that is not a knee against a beam, and <c>:227-228</c>
    /// aligns its stencil to CENTER on both axes — so the digit STRADDLES the bracket line
    /// and reaches half its own height past it, further out than the bracket's 0.08. The
    /// height comes from the font, per string, exactly as LilyPond's does
    /// (<see cref="Rendering.TextFontMetrics"/>); it was a named residual of -0.547717 on
    /// all four tuplet ledger entries for as long as Lily# had no way to measure text.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Also called by <c>LayoutEngine.AugmentSkylinesForPaging</c> for the PAGE's skyline.
    /// It works in either frame because it does nothing but translate the layouts it is
    /// given: the caller decides whether <c>*YUp</c> was produced with a staff offset
    /// (system frame) or without one (staff frame), and both are Y-up.
    /// </remarks>
    /// <param name="staffTopUp">
    /// The step from the bracket engraver's frame — the staff's TOP line, since it is run
    /// with no staff offset — to this skyline's. ⚠️ IT APPLIES TO BOTH SEEDS BELOW. The
    /// number reaches further out than the line it straddles, so converting the line alone
    /// moves the line and leaves the BINDING ink where it was: a change that looks like
    /// nothing until a staff below it collides. <c>StaffSkylineFrameTests</c> holds the
    /// number to the same frame as its line for exactly that reason.
    /// </param>
    internal static void AddTupletBracketsToSkyline(
        ImmutableArray<TupletBracketLayout> tupletBrackets, double staffTopUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (tupletBrackets.IsDefaultOrEmpty)
            return;

        double half = size.Span(EngravingDefaults.TupletBracketThickness / 2.0);
        foreach (var b in tupletBrackets)
        {
            double dir = b.IsStemUp ? 1.0 : -1.0;
            // The DRAWN ends, not the logical bounds: a TupletBracket's skyline is built
            // from its STENCIL, and shorten-pair puts that stencil 0.2 further out at each
            // end — TupletBracketLayout.DrawnStartX carries the rule.
            // LILYPOND-REF: scm/define-grobs.scm:4114 grob::unpure-vertical-skylines-from-stencil
            //   — TupletBracket's vertical-skylines come from its stencil, not its bounds.
            bool leftFirst = b.StartX <= b.EndX;
            double xLeft = leftFirst ? b.DrawnStartX : b.DrawnEndX;
            double xRight = leftFirst ? b.DrawnEndX : b.DrawnStartX;
            if (xRight <= xLeft)
                continue;

            var direction = b.IsStemUp ? VerticalDirection.Up : VerticalDirection.Down;
            var sky = b.IsStemUp ? upSkyline : downSkyline;

            // A fully beamed tuplet draws no bracket LINE (bracket-visibility =
            // if-no-beam), but its NUMBER still prints — and a printed grob is staff
            // skyline ink like any other. This used to skip the WHOLE tuplet ("its
            // number rides the beam"), assuming the beam's own seed covers the number;
            // it does not — the number hangs a bracket-padding past the beam's edge
            // (measured six-digit on 2.26.0: centre at beam edge + 1.100, ledger
            // staff.staff.beamed-tuplet-number, which read Lily# identical to its
            // no-tuplet control while LilyPond reads 1.427717 more).
            if (b.ShowBracket)
            {
                // The OUTWARD edge of the line, computed here rather than handed to
                // FromSlope's thickness parameter: that parameter's DOWN arm is
                // unexercised by any production caller and its sign is not pinned by a
                // test, while thickness 0 means "store exactly this edge" in both arms.
                double yLeft = size.Span(leftFirst ? b.StartYUp : b.EndYUp) + dir * half + staffTopUp;
                double yRight = size.Span(leftFirst ? b.EndYUp : b.StartYUp) + dir * half + staffTopUp;
                sky.Merge(VerticalSkyline.FromSlope(xLeft, yLeft, xRight, yRight, thickness: 0, direction));
            }

            // THE NUMBER, which reaches further out than the line it straddles. Centred on
            // the bracket's midpoint on both axes (tuplet-number.cc:342 and :227-228), so
            // half its own ink stands proud on the outward side. Measured from the font,
            // per string, because that is what LilyPond measures — a "3" and a "12" are
            // not the same width and an italic digit is not a nominal box.
            if (!string.IsNullOrEmpty(b.NumberText))
            {
                // ...at THIS staff's size, which is what a font size is: LilyPond's
                // fontSize applies to the number as to every other glyph in the context.
                double fontSize = size.Span(TupletBracketEngraver.NumberFontSize);
                double halfW = Rendering.TextFontMetrics.Advance(
                    b.NumberText, fontSize, sans: false, TupletBracketEngraver.NumberFontStyle) / 2;
                double halfH = Rendering.TextFontMetrics.InkHeight(
                    b.NumberText, fontSize, sans: false, TupletBracketEngraver.NumberFontStyle) / 2;
                double midYUp = size.Span(b.NumberYUp) + staffTopUp;
                sky.Merge(VerticalSkyline.FromBox(
                    b.NumberX - halfW, b.NumberX + halfW,
                    midYUp - halfH, midYUp + halfH, direction));
            }
        }
    }

    /// <summary>
    /// Seeds the drawn slur bows into the per-staff skylines so the inter-staff gap
    /// reserves the room they occupy — the analogue of <see cref="AddTupletBracketsToSkyline"/>
    /// for slurs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm Slur carries <c>vertical-skylines</c> from its
    /// stencil and sets NO <c>outside-staff-priority</c>, so lily/axis-group-interface.cc
    /// keeps it inside the staff's own axis group like the clef and the tuplet bracket.
    /// <para>
    /// The bow's control points are the CENTRELINE of a bezier sandwich (see
    /// <c>SharedRenderer.DrawBow</c> / lily/lookup.cc Lookup::slur): the drawn ink stands
    /// half the middle thickness plus half the round-cap stroke proud of it on the OUTWARD
    /// side. So the cubic is flattened into segments — as LilyPond flattens its own bezier
    /// for the skyline (lily/stencil-integral.cc add_draw_bezier_segments) — and each
    /// sample is pushed out by that half-extent before it is stored. The <c>*YUp</c> here
    /// is measured from the staff's top line (the caller runs the slur scorer with no staff
    /// offset), exactly this skyline's origin, the same frame the tuplet bracket arrives in.
    /// </para>
    /// </remarks>
    internal static void AddSlursToSkyline(
        ImmutableArray<SlurLayout> slurs, double staffTopUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (slurs.IsDefaultOrEmpty)
            return;
        foreach (var s in slurs)
            SeedBowInk(s.StartX, s.StartYUp, s.Control1, s.Control2, s.EndX, s.EndYUp,
                staffTopUp, size, upSkyline, downSkyline);
    }

    /// <summary>
    /// Seeds the drawn tie bows into the per-staff skylines. A Tie is the same kind of grob
    /// as the Slur — <c>vertical-skylines</c> from its stencil, no <c>outside-staff-priority</c>
    /// (scm/define-grobs.scm Tie), same bezier-sandwich ink (<c>thickness 1.2</c> /
    /// <c>line-thickness 0.8</c>) — so it reserves its bow exactly as the slur does. See
    /// <see cref="SeedBowInk"/>. audit/lp-geometry staff.staff.tie-{under,over}-notes.
    /// </summary>
    internal static void AddTiesToSkyline(
        ImmutableArray<TieLayout> ties, double staffTopUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (ties.IsDefaultOrEmpty)
            return;
        foreach (var t in ties)
            SeedBowInk(t.StartX, t.StartYUp, t.Control1, t.Control2, t.EndX, t.EndYUp,
                staffTopUp, size, upSkyline, downSkyline);
    }

    /// <summary>
    /// Seeds one bow (slur or tie) into the per-staff skylines so the inter-staff gap
    /// reserves the ink it occupies — the analogue of <see cref="AddTupletBracketsToSkyline"/>
    /// for the curved spanners. Shared by <see cref="AddSlursToSkyline"/> and
    /// <see cref="AddTiesToSkyline"/> so the drawn bow and its reservation come from ONE model.
    /// </summary>
    /// <remarks>
    /// The control points are the CENTRELINE of a bezier sandwich (lily/lookup.cc:395-415
    /// Lookup::slur, used for the tie too): the two INTERIOR control points are pushed out by
    /// half the mid thickness, normal to the chord, to form the outer edge, and the whole
    /// outline is stroked with a round pen of the line thickness. The bow ENDS (control_[0]/[3])
    /// do NOT move, so the ink stands only half the pen proud at the ends and bulges to
    /// +0.375·curvethick at the peak — a taper, not a flat half-extent. The cubic is flattened
    /// into segments as LilyPond flattens its own bezier for the skyline
    /// (lily/stencil-integral.cc add_draw_bezier_segments). The <c>*YUp</c> is measured from
    /// the staff's top line (the caller runs the scorer with no staff offset), exactly this
    /// skyline's origin. LILYPOND-REF: lily/lookup.cc:403-407 (back.control_[1,2] +=
    /// 0.5·curvethick·perp) and :484-515 bezier_sandwich (stroked with line_thick).
    /// <para>
    /// The bow direction is read from the geometry — the control points sit above the chord
    /// for an up bow, below for a down one — so a Slur (which knows its CurveUp) and a Tie
    /// (which does not carry one) are handled identically. Slur and tie share the mid/line
    /// thickness (both 1.2 / 0.8 × line-thickness), so a single half-extent serves both.
    /// </para>
    /// </remarks>
    private static void SeedBowInk(
        double p0x, double p0y, (double X, double Y) c1, (double X, double Y) c2,
        double p3x, double p3y, double staffTopUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        // ⚠️ ONE CONVERSION AT THE DOOR: the bow scorer's frame is the staff's TOP line and
        // this skyline's is the staff's REFERENCE POINT. Applied to all four points here so
        // that every y below — including the direction test, which compares the controls
        // against the ends — is in one frame from this line on.
        // ...and the SIZE at the same door, for the same reason: the scorer works in the
        // staff's own staff-spaces, so its four Y values are this staff's before they are
        // the system's. X is the drawn column and stays.
        p0y = size.Span(p0y) + staffTopUp;
        p3y = size.Span(p3y) + staffTopUp;
        c1.Y = size.Span(c1.Y) + staffTopUp;
        c2.Y = size.Span(c2.Y) + staffTopUp;

        // The bow's own ink, which is font-sized like any other: a small staff's slur is
        // drawn with a thinner pen.
        double halfCurve = size.Span(0.5 * EngravingDefaults.SlurMidThickness);
        double halfPen = size.Span(0.5 * EngravingDefaults.BowEndRounding);

        // Up bow if the controls sit above the chord midline (Y-up), else down.
        bool curveUp = (c1.Y + c2.Y) >= (p0y + p3y);
        var sky = curveUp ? upSkyline : downSkyline;
        var direction = curveUp ? VerticalDirection.Up : VerticalDirection.Down;
        MergeBowOuterEdge(sky, direction, p0x, p0y, c1, c2, p3x, p3y, halfCurve, halfPen);
    }

    /// <summary>
    /// The sampling core of <see cref="SeedBowInk"/>: one bow's OUTER edge (centreline
    /// controls pushed out by <paramref name="halfCurve"/>, plus the
    /// <paramref name="halfPen"/> stroke), flattened into slope segments and merged into
    /// <paramref name="sky"/>. The four points arrive ALREADY in the target skyline's own
    /// Y-up frame — the callers each own their one conversion at their door (SeedBowInk's
    /// staff-top/size door; ArticulationEngraver's staff-middle door for the tie a script
    /// must clear). Split out so the drawn bow's reservation and the script's tie support
    /// read ONE outline model, not two spellings of the sandwich.
    /// </summary>
    internal static void MergeBowOuterEdge(
        VerticalSkyline sky, VerticalDirection direction,
        double p0x, double p0y, (double X, double Y) c1, (double X, double Y) c2,
        double p3x, double p3y, double halfCurve, double halfPen)
    {
        const int samples = 16;
        double dir = direction == VerticalDirection.Up ? 1.0 : -1.0;

        // Shift the interior control points onto the outer edge. Only the Y component of the
        // chord normal matters to this vertical skyline: |perp_Y| of a unit normal is
        // |chord_x|/|chord|, so a flat bow shifts the full halfCurve and a steep one less.
        double cdx = p3x - p0x, cdy = p3y - p0y;
        double clen = System.Math.Sqrt(cdx * cdx + cdy * cdy);
        double ctrlOutY = dir * halfCurve * (clen > 1e-9 ? System.Math.Abs(cdx) / clen : 1.0);
        double p1x = c1.X, p1y = c1.Y + ctrlOutY;
        double p2x = c2.X, p2y = c2.Y + ctrlOutY;

        double prevX = 0, prevY = 0;
        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples;
            double mt = 1 - t;
            double b0 = mt * mt * mt, b1 = 3 * mt * mt * t, b2 = 3 * mt * t * t, b3 = t * t * t;
            double x = b0 * p0x + b1 * p1x + b2 * p2x + b3 * p3x;
            double y = b0 * p0y + b1 * p1y + b2 * p2y + b3 * p3y + dir * halfPen;
            if (i > 0)
            {
                double xl = System.Math.Min(prevX, x), xr = System.Math.Max(prevX, x);
                if (xr > xl)
                {
                    bool leftFirst = prevX <= x;
                    double yl = leftFirst ? prevY : y;
                    double yr = leftFirst ? y : prevY;
                    sky.Merge(VerticalSkyline.FromSlope(xl, yl, xr, yr, thickness: 0, direction));
                }
            }
            prevX = x;
            prevY = y;
        }
    }

    /// <summary>
    /// Seeds staff-local Script articulation ink into the per-staff skylines so the
    /// inter-staff gap reserves room for them. The layouts carry Y relative to the
    /// staff top line (= this skyline's Y origin); the ink transform matches
    /// LayoutEngine.AugmentSkylinesWithScripts (BBox Top is up-positive).
    /// </summary>
    /// <summary>
    /// Seeds each fingering's drawn digit run as a box in the staff's own inside profile:
    /// the run's advance width about its centre, its ink from the baseline the engraver
    /// placed.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1540-1568 Fingering — ly:script-interface::calc-positioning-done at :1553;
    ///   no outside-staff-priority anywhere in the block, which is what puts it here rather
    ///   than among the movers.
    /// LILYPOND-REF: lily/grob.cc:81-85 simple_vertical_skylines_from_extents — a grob with
    ///   no vertical-skylines declaration contributes its EXTENT box, and Fingering declares
    ///   none, so a box is the right shape and not an approximation of an outline.
    /// <para>
    /// The metric home is <see cref="FingeringEngraver.DigitRun"/> — the same run the pen
    /// draws and the script column stacks by, so the reserved band cannot drift from the
    /// printed one.
    /// </para>
    /// <para>
    /// ⚠️ THE POSITION SCALES WITH THE STAFF AND THE GLYPH DOES NOT, and that asymmetry is
    /// the PEN's, copied here rather than corrected: <c>SharedRenderer.DrawFingerings</c>
    /// draws the run at <c>FiguredBassGlyphRun.Em</c> flat while it reflects the baseline
    /// through the ossia shrink, and its own remark records that (the figured bass is the
    /// same). Scaling the ink here — which is what every neighbouring seed does, because
    /// every other grob's pen scales — would reserve 0.7 of the ink an ossia staff really
    /// prints. LilyPond has no such split (font-size is context-relative there, so the
    /// digits shrink with the staff); reserving what Lily# DRAWS keeps the two halves of
    /// one grob together, and the divergence stays one thing rather than two.
    /// </para>
    /// </remarks>
    private static void AddFingeringsToSkyline(
        ImmutableArray<FingeringLayout> fingerings,
        double staffMiddleUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (fingerings.IsDefaultOrEmpty)
            return;
        foreach (var f in fingerings)
        {
            var (_, ink, width) = FingeringEngraver.DigitRun(f.Number);
            double baseline = size.Span(f.YUp) + staffMiddleUp;
            double bottom = baseline + ink.Bottom;
            double top = baseline + ink.Top;
            double x0 = f.X - width / 2.0;
            var box = VerticalSkyline.FromBox(x0, x0 + width, bottom, top, VerticalDirection.Up);
            upSkyline.Merge(box);
            downSkyline.Merge(VerticalSkyline.FromBox(
                x0, x0 + width, bottom, top, VerticalDirection.Down));
        }
    }

    private void AddArticulationLayoutsToSkyline(
        ImmutableArray<ArticulationLayout> articulationLayouts,
        double staffMiddleUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline,
        bool moversInstead = false)
    {
        if (articulationLayouts.IsDefaultOrEmpty)
            return;
        foreach (var a in articulationLayouts)
        {
            if (moversInstead != (a.OutsideStaffPriority is not null))
                continue;
            // ⚠️ A SCRIPT THAT DECLARES A PRIORITY IS A MOVER, NOT INSIDE-STAFF INK. The
            // fermata family declares 75 (scm/script.scm), so LilyPond leaves it OUT of
            // inside_staff_skylines and places it against them — a profile holding it would
            // make the mover clear ITSELF. Everything else declares none and belongs here.
            // LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines takes
            //   the elements whose priority is unset; :952-972 places the others.
            // ⚠️ MEASURED, 2026-08-05: feeding a profile that held them to the outside-staff
            // seed moved seven LP-EXACT ledger points (script.quiet / high-head /
            // stem-support / below / accidental / lower-staff, trill.fermata-priority) and
            // twelve snapshots — a mover avoiding its own ink, in person.
            // ⚠️ The ROOM still reserves them, at their engraver position, through
            // <see cref="PlaceDynamicsOn"/>'s movers pass — Lily# places them later than the
            // room runs, which is the standing approximation this split makes visible.
            // ArticulationLayout.YUp is Y-up (staff-spaces above the staff middle);
            // translate it to this skyline's Y-up frame (its origin is the staff top).
            // Both the offset and the glyph belong to this staff; a.X is the column.
            double y = size.Span(a.YUp) + staffMiddleUp;
            // THE Script grob's profile, from its one house: the glyph's real outline padded
            // by its own skyline-horizontal-padding. The same object the movers of this very
            // grob are placed with, because LilyPond has ONE vertical-skylines per grob and
            // hands it to every consumer.
            // LILYPOND-REF: scm/define-grobs.scm:3006 grob::always-vertical-skylines-from-stencil
            //   — the Script's own declaration; lily/axis-group-interface.cc:914-935
            //   inside_staff_skylines merges exactly that property, and the outside-staff
            //   movers then clear it.
            ArticulationEngraver.MergeScriptProfile(
                a.IsAbove ? upSkyline : downSkyline, a, y, size.Magnification);
        }
    }

    /// <summary>
    /// Seeds both skylines with the staff symbol's own vertical extent (the five
    /// lines span ±StaffHeight/2 about the middle). LilyPond's per-staff spacing
    /// skyline includes the StaffSymbol, so a neighbour's high/low notes must
    /// clear these lines, not merely the notes at the same X.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/axis-group-interface.cc:914-940.</remarks>
    private void SeedStaffSymbol(
        ImmutableArray<MeasureLayout> measureLayouts, double staffMiddleUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (measureLayouts.IsDefaultOrEmpty)
            return;
        double xLeft = double.PositiveInfinity, xRight = double.NegativeInfinity;
        foreach (var ml in measureLayouts)
        {
            xLeft = Math.Min(xLeft, ml.X);
            xRight = Math.Max(xRight, ml.X + ml.Width);
        }
        if (xRight <= xLeft)
            return;

        // Half the staff PLUS half a line's thickness: a staff line is ink, and a
        // skyline carries a grob's ink, not the path its centre follows. Measured on the
        // grob itself — probes/glyph-skyline.ly asks StaffSymbol for its extent and its
        // vertical-skylines and gets (-2.05 . 2.05) for both, where the outermost line
        // CENTRES are at 2.0. Written as the derivation rather than as 2.05 so that a
        // staff of a different size or line weight still gets its own ink.
        // ⚠️ AT THIS STAFF'S SIZE. Both terms are staff-space quantities of the staff being
        // seeded, and a magnified staff's lines are closer together AND thinner, exactly as
        // magnifyStaff makes them (it sets staff-space and fontSize together).
        double half = size.Span(
            _staffHeight / 2.0 + EngravingDefaults.StaffLineThickness / 2.0);
        // Translate the device-frame staff lines to this skyline's Y-up frame (negate):
        // the top line sits above the origin, the bottom line below.
        double staffTop = half + staffMiddleUp;      // Y-up of the top line's ink
        double staffBottom = -half + staffMiddleUp;  // Y-up of the bottom line's ink

        // UP skyline takes the top line; DOWN skyline takes the bottom line.
        upSkyline.Merge(VerticalSkyline.FromBox(
            xLeft, xRight, staffBottom, staffTop, VerticalDirection.Up));
        downSkyline.Merge(VerticalSkyline.FromBox(
            xLeft, xRight, staffBottom, staffTop, VerticalDirection.Down));
    }

    /// <summary>
    /// Adds each dynamic's extent to the inter-staff skyline so staff-to-staff spacing
    /// reserves room for it (mirrors <see cref="DynamicEngraver"/>'s Y): a below dynamic
    /// widens the gap to the staff BELOW (DOWN skyline), a forced-above one (@f.up) widens
    /// the gap to the staff ABOVE (UP skyline).
    /// </summary>
    private void AddDynamicsToSkyline(
        Staff staff, ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<MeasureLayout> measureLayouts,
        double staffMiddleUp, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline,
        ImmutableArray<BeamLayout> beams = default)
    {
        if (dynamics.IsDefaultOrEmpty)
            return;

        var voices = staff.Voices;
        var primaryMeasures = staff.PrimaryVoice.Measures;
        var beamMembers = DynamicEngraver.BuildBeamMembers(beams);

        // Same-column dynamics stack AWAY from the staff (see DynamicEngraver); track
        // depth per side so the reservation reflects the outermost stacked glyph.
        var stackAt = new Dictionary<(int, int, bool), int>();
        foreach (var dyn in dynamics)
        {
            int layoutIdx = -1;
            for (int i = 0; i < measureLayouts.Length; i++)
            {
                if (measureLayouts[i].MeasureIndex == dyn.MeasureIndex)
                {
                    layoutIdx = i;
                    break;
                }
            }
            if (layoutIdx < 0)
                continue;
            var measureLayout = measureLayouts[layoutIdx];

            var key = (dyn.MeasureIndex, dyn.ItemIndex, dyn.IsAbove);
            int depth = stackAt.GetValueOrDefault(key, 0);
            stackAt[key] = depth + 1;

            // The column X and the label's anchor — the SAME two X's the engraver
            // computes (DynamicEngraver.Calculate), so seed and draw agree.
            double xColumn = measureLayout.X + LayoutUtilities.GetItemXOffset(
                primaryMeasures, dyn.MeasureIndex, dyn.ItemIndex, measureLayout);
            double xLabel = xColumn + DynamicEngraver.AnchorCentreOffset(
                DynamicEngraver.AnchorItem(voices, dyn.VoiceIndex, dyn.MeasureIndex, dyn.ItemIndex));

            int mi = dyn.MeasureIndex, ii = dyn.ItemIndex;
            int dynStaff = dyn.StaffIndex;
            double baselineUp = DynamicEngraver.PointwiseBaselineY(dyn.IsAbove, voices,
                dyn.VoiceIndex, mi, ii, xColumn, xLabel, dyn.Text, dyn.IsExpressiveText,
                vi => beamMembers.TryGetValue((dynStaff, vi, mi, ii), out var b) ? b : null);

            // This label's own ink, from the font. LilyPond's DynamicText extent IS the
            // drawn glyph's ink, so it differs per dynamic — see DynamicEngraver.InkOf.
            var (ascent, descent) = DynamicEngraver.InkOf(dyn.Text, dyn.IsExpressiveText);

            if (dyn.IsAbove)
            {
                // Upward reach (text ascends from the above baseline); reserve room
                // toward the staff above; stacking pushes it further UP (+). The above
                // side keeps the box reservation (the above-staff DRAW pass is the
                // stacker's, which runs its own pointwise placement).
                double topUp = size.Span(baselineUp + depth * DynamicEngraver.StackStep
                    + ascent) + staffMiddleUp;
                double dynamicWidth = size.Span(1.3);
                var box = VerticalSkyline.FromBox(
                    xLabel - dynamicWidth / 2, xLabel + dynamicWidth / 2,
                    topUp - size.Span(0.5), topUp, VerticalDirection.Up);
                upSkyline.Merge(box);
            }
            else
            {
                baselineUp -= depth * DynamicEngraver.StackStep;
                // The outside-staff collision pass over the ACCUMULATED down profile
                // (staff symbol, clef, notes, real stems, scripts, brackets, bows,
                // beams — everything merged before this call): the quiet side-position
                // Y only cleared the dynamic's OWN column; the pass clears everything
                // else, pointwise, at outside-staff padding — the beam face reaches the
                // dynamic HERE (DSB), while a thin stem tucks beside the f's outline.
                // LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions
                //   (padding 0.46).
                // ⚠️ Pointwise runs at FULL size only: an ossia staff's mixed scale has
                // no measured regime yet, so it keeps the former box reservation.
                if (size.Span(1.0) == 1.0)
                {
                    var my = DynamicEngraver.LabelSkylines(
                        dyn.Text, dyn.IsExpressiveText, xLabel, baselineUp + staffMiddleUp);
                    double move = DynamicEngraver.BelowCollisionMove(
                        downSkyline, my.Up, OutsideStaffPadding);
                    if (move != 0)
                    {
                        my.Up.Raise(move);
                        my.Down.Raise(move);
                    }
                    // The label's own outline joins the down profile, so the staff
                    // below is spaced off the glyph's real ink — and a LATER dynamic's
                    // collision pass sees this one (the priority-order accumulation).
                    downSkyline.Merge(my.Down);
                }
                else
                {
                    double bottomUp = size.Span(baselineUp - descent) + staffMiddleUp;
                    double dynamicWidth = size.Span(1.3);
                    var box = VerticalSkyline.FromBox(
                        xLabel - dynamicWidth / 2, xLabel + dynamicWidth / 2,
                        bottomUp, bottomUp + size.Span(0.5), VerticalDirection.Down);
                    downSkyline.Merge(box);
                }
            }
        }
    }

    // LILYPOND-REF: scm/define-grobs.scm outside-staff-padding default = 0.46
    //   (lily/axis-group-interface.cc:45 default_outside_staff_padding_).
    private const double OutsideStaffPadding = 0.46;

    /// <summary>
    /// Adds a music item's bounding boxes to the skylines.
    /// Dispatches to appropriate handler based on item type.
    /// </summary>
    private void AddMusicItemToSkylines(
        MusicItem item,
        double x,
        double staffMiddleUp,
        StaffSize size,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline,
        bool? forcedStemUp = null,
        bool reserveStem = true,
        double restShiftUp = 0.0,
        int? restDotRel = null)
    {
        switch (item)
        {
            case NoteItem note:
                AddNoteToSkylines(note, x, staffMiddleUp, size,
                    upSkyline, downSkyline, forcedStemUp, reserveStem);
                // The augmentation dots the renderer draws, as their EXTENT boxes: a Dots
                // grob declares no vertical-skylines, so it enters LilyPond's inside-staff
                // profile through the default — a box from its extents — and the
                // outside-staff pass then clears it pointwise (a wide fermata over a
                // dotted note binds on the DOT, its short sibling on the head; LP prints
                // three levels where an unseeded dot gave two).
                // LILYPOND-REF: scm/define-grobs.scm:1272-1288 Dots — dots::calc-dot-stencil,
                //   dot-count, staff-position; NO vertical-skylines entry among them, so the
                //   extents default applies.
                // LILYPOND-REF: lily/grob.cc:81-85 simple_vertical_skylines_from_extents_proc
                //   — the vertical-skylines default for a grob that declares none (the same
                //   sentence the notehead seed above cites).
                // LILYPOND-REF: lily/axis-group-interface.cc:914-935 skyline_spacing —
                //   Dots is an inside-staff grob, so it is in what the movers clear.
                if (note.Dots > 0)
                {
                    // The renderer's own column X and Dot_configuration position
                    // (SharedRenderer.Noteheads, single-note branch): base X = head INK
                    // right + one dot width, position = the badness solve with the
                    // voice-wide preferred direction. The NoteCollision DotAdjustment
                    // (ColumnMinX push / direction flip) is NOT read here — the seed has
                    // no collision tables, the same simplification the head seed already
                    // makes about collision X offsets.
                    int dottedValue = LayoutUtilities.GetNoteValueFromFraction(note.BaseDuration);
                    double noteDotX = x + size.Ink(GlyphMetrics.GetNoteheadBBox(dottedValue)).Right
                        + size.Span(GlyphMetrics.AugmentationDot.Width);
                    int noteDotDir = forcedStemUp switch { true => 1, false => -1, null => 0 };
                    int noteDotPos = DotConfiguration.Resolve(
                        new[] { note.StaffPosition },
                        noteDotDir != 0 ? new[] { noteDotDir } : null)[0];
                    MergeDotRow(note.Dots, noteDotX,
                        staffMiddleUp + size.Span(noteDotPos * 0.5),
                        size, upSkyline, downSkyline);
                }
                if (note.Accidental != null)
                {
                    double accY = size.Span(note.StaffPosition * 0.5) + staffMiddleUp;
                    // Packed with the rest of the staff column when another voice stands on
                    // it, and `x` IS that column (Collector.StaffAccidentalColumns).
                    if (note.AccidentalX is { } packedX)
                        MergeAccidentalInk(note.Accidental, x + size.Span(packedX), accY, size,
                            upSkyline, downSkyline);
                    else
                        AddAccidentalToSkylines(note.Accidental, x, accY, size,
                            upSkyline, downSkyline);
                }
                break;
            case ChordItem chord:
                int chordNoteValue = LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration);
                // Every note of a chord shares the chord's single stem, so the
                // stem box must use the chord's resolved direction — not a
                // per-note threshold. Mirrors the note case (note.StemUp) and
                // the renderer (chord.StemUp). A multi-voice staff forces it.
                // LILYPOND-REF: lily/stem.cc — one Stem per NoteColumn.
                // Writer's @stemUp/@stemDown outranks the voice default, as the
                // renderer draws it (SharedRenderer.DrawNote).
                bool chordStemUp = chord.ForcedStemUp ?? forcedStemUp ?? chord.StemUp;
                foreach (var chordNote in chord.Notes)
                {
                    AddNoteBoxToSkylines(chordNote.StaffPosition, x, staffMiddleUp, size,
                        chordStemUp, chordNoteValue,
                        upSkyline, downSkyline, reserveStem, chord.Notehead);
                }
                // Chord accidentals go through the REAL placement machinery
                // (stagger columns, reversed-head offsets) so the skyline
                // carries each glyph at its true X — the same call the
                // renderer draws with.
                // LILYPOND-REF: lily/accidental-placement.cc position_apes.
                foreach (var (acc, accPos, accOffset) in ChordAccidentalXs(chord, chordStemUp, chordNoteValue))
                {
                    // Head Y in the skyline's Y-up frame: staff-position → staff-spaces
                    // above the middle (pos*0.5), then to the skyline origin (staff top).
                    double accHeadY = size.Span(accPos * 0.5) + staffMiddleUp;
                    // The stagger's offset is a distance in staff-spaces from the column, so
                    // it belongs to this staff too; `x` alone is the column.
                    double accX = x + size.Span(accOffset);
                    MergeAccidentalInk(acc, accX, accHeadY, size,
                        upSkyline, downSkyline);
                }
                // The chord's dot rows, as the renderer draws them (see the NoteItem case
                // for why a drawn dot belongs in this profile): one row per member, all in
                // one column right of the heads — reversed heads push the column right, so
                // the same offsets solve feeds it. The collision DotAdjustment is not read
                // here either (NoteItem case's remark).
                if (chord.Dots > 0 && chord.Notes.Length > 0)
                {
                    var chordHeadOffsets = ChordHeadPositioning.CalculateOffsets(
                        chord.Notes, chordStemUp, chordNoteValue, 1.0);
                    double chordDotX = x + size.Ink(GlyphMetrics.GetNoteheadBBox(chordNoteValue)).Right
                        + size.Span(Math.Max(0, chordHeadOffsets.Max()))
                        + size.Span(GlyphMetrics.AugmentationDot.Width);
                    int chordDotDir = forcedStemUp switch { true => 1, false => -1, null => 0 };
                    var chordDotPositions = DotConfiguration.Resolve(
                        chord.Notes.Select(n => n.StaffPosition).ToArray(),
                        chordDotDir != 0
                            ? Enumerable.Repeat(chordDotDir, chord.Notes.Length).ToArray()
                            : null);
                    foreach (int p in chordDotPositions)
                        MergeDotRow(chord.Dots, chordDotX,
                            staffMiddleUp + size.Span(p * 0.5),
                            size, upSkyline, downSkyline);
                }
                break;
            case RestItem restItem:
                // The GLYPH THE RENDERER DRAWS, at the ORIGIN IT DRAWS IT AT — not a nominal
                // box. LilyPond has no rest-size constant: a Rest's extent is its stencil's,
                // so the reservation and the ink are one quantity there and must be one here.
                // LILYPOND-REF: lily/rest.cc:33-45 y_offset_callback — the Y is
                //   `staff_position_internal` times half a staff space, and :47-145
                //   staff_position_internal is where the whole rest's own line comes from;
                //   lily/rest.cc:229-257 brew_internal_stencil takes the stencil from
                //   `find_by_name` on the glyph the duration-log selects, so the extent IS
                //   the glyph's.
                // ⚠️ THE ORIGIN RULE IS SHARED WITH THE DRAWING, deliberately spelled the same
                // way SharedRenderer.DrawRest spells it: a whole rest hangs from the fourth
                // line (one space below the top line) and everything else sits about the
                // middle. Until 2026-08-04 this seeded a 1.0 x 1.0 square centred on the
                // middle line for every duration alike, which is the two-spellings shape
                // HANDOFF 7.7 names — and a square the staff symbol's own 2.05 swallowed
                // whole, so the seed could not bind anything at all (audit/lp-geometry
                // staff.staff.rest-under-notes).
                int restValue = GlyphMetrics.NoteValueOf(restItem.BaseDuration);
                // The SKYLINE box, not the LILC one: LilyPond's vertical-skylines are the
                // traced outline and its extent is the metric box, and for a quarter rest
                // they differ by 0.030000 at the bottom. See GetRestSkylineBBox.
                var restBox = size.Ink(GlyphMetrics.GetRestSkylineBBox(restValue));
                // Y-up of the glyph's own origin, in this skyline's frame: the middle line is
                // this frame's zero, and a whole rest's origin is one space above it.
                double restOriginUp = staffMiddleUp
                    + (restValue == 1 ? size.Span(1.0) : 0.0)
                    + size.Span(restShiftUp);
                double restTop = restOriginUp + restBox.Top;
                double restBottom = restOriginUp + restBox.Bottom;
                var restUp = VerticalSkyline.FromBox(
                    x + restBox.Left, x + restBox.Right, restBottom, restTop, VerticalDirection.Up);
                var restDown = VerticalSkyline.FromBox(
                    x + restBox.Left, x + restBox.Right, restBottom, restTop, VerticalDirection.Down);
                upSkyline.Merge(restUp);
                downSkyline.Merge(restDown);
                // The rest's dots, where the renderer puts them (SharedRenderer.DrawRest):
                // one dot width right of the rest's LILC ink, at the dot-column answer
                // RELATIVE to the glyph origin — riding the same shift the glyph took.
                // Until 2026-08-09 this seeded a fixed "space above the middle line" while
                // the drawn dot followed the shifted rest (the comment here even claimed
                // the drawing did not follow — it did), the two-spellings shape again.
                // The default arm is DrawRest's: +1 off the origin's line, −1 for a
                // hanging semibreve.
                if (restItem.Dots > 0)
                {
                    double restDotX = x + size.Ink(GlyphMetrics.GetRestBBox(restValue)).Right
                        + size.Span(GlyphMetrics.AugmentationDot.Width);
                    double restDotUp = restOriginUp + size.Span(
                        (restDotRel ?? ElementCoordinator.RestDotDefaultOffset(restValue)) * 0.5);
                    MergeDotRow(restItem.Dots, restDotX, restDotUp,
                        size, upSkyline, downSkyline);
                }
                break;
        }
    }

    /// <summary>
    /// Merges one drawn dot row — <paramref name="dotCount"/> augmentation dots advancing
    /// two dot widths apart from <paramref name="dotStartX"/> (the renderer's spacing) —
    /// into both skylines as ONE extent box at <paramref name="dotUp"/> (Y-up, this
    /// skyline's frame).
    /// </summary>
    /// <remarks>
    /// ONE BOX FOR THE WHOLE ROW, deliberately, and a box rather than an outline: the
    /// Dots grob's stencil is the entire row — <c>ly:dots::print</c> stacks
    /// <c>dot-count</c> copies of the dot stencil with one dot width of padding — and
    /// Dots declares no <c>vertical-skylines</c>, so LilyPond's profile for it is the
    /// extents default over that WHOLE stencil: the inter-dot gaps are inside the box.
    /// Per-dot boxes would leave those gaps open, which is a shape LilyPond never shows
    /// anybody. The same extents choice the notehead seed makes, and the opposite of the
    /// Clef/Accidental outline walks beside it.
    /// LILYPOND-REF: scm/output-lib.scm:686-690 ly:dots::print — stack-stencils of
    ///   dot-count dot stencils, padding = one dot width.
    /// LILYPOND-REF: scm/define-grobs.scm:1272-1288 Dots — <c>dots::calc-dot-stencil</c>,
    ///   <c>Y-extent</c> from stencil; no vertical-skylines entry among them.
    /// LILYPOND-REF: lily/grob.cc:81-85 simple_vertical_skylines_from_extents_proc — the
    ///   extents default that entry's absence falls to.
    /// </remarks>
    private static void MergeDotRow(int dotCount, double dotStartX, double dotUp,
        StaffSize size, VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (dotCount <= 0)
            return;
        var dotBox = size.Ink(GlyphMetrics.AugmentationDot);
        double advance = 2 * size.Span(GlyphMetrics.AugmentationDot.Width);
        double left = dotStartX + dotBox.Left;
        double right = dotStartX + (dotCount - 1) * advance + dotBox.Right;
        upSkyline.Merge(VerticalSkyline.FromBox(left, right,
            dotUp + dotBox.Bottom, dotUp + dotBox.Top, VerticalDirection.Up));
        downSkyline.Merge(VerticalSkyline.FromBox(left, right,
            dotUp + dotBox.Bottom, dotUp + dotBox.Top, VerticalDirection.Down));
    }

    /// <summary>
    /// Seeds a printed accidental (left of its head) into the skylines. LilyPond's skylines
    /// are built from every grob's stencil, accidentals included — omitting them made
    /// everything spaced against these skylines (the chord-name line, page stacking) graze a
    /// sharp or flat over a high note, papered over by a flat allowance until now.
    /// Chord accidental COLUMNS go through the real placement machinery
    /// (see the ChordItem case in AddMusicItemToSkylines).
    /// LILYPOND-REF: lily/stencil-integral.cc — every stencil contributes its box.
    /// </summary>
    private static void AddAccidentalToSkylines(
        string accidental, double headX, double headY, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        var bbox = size.Ink(GlyphMetrics.GetAccidentalSkylineBBox(accidental));
        double right = headX - size.Span(GlyphMetrics.AccidentalNoteGap);
        MergeAccidentalInk(accidental, right - bbox.Width, headY, size,
            upSkyline, downSkyline);
    }

    /// <summary>Placement machinery shared with the renderer, for chord
    /// accidental columns (see the ChordItem case).</summary>
    private static readonly AccidentalPlacement AccidentalStagger = new();

    /// <summary>
    /// Each of a chord's accidentals as (glyph, staff position, ink-left X from the column):
    /// the packing the whole staff column got when another voice stands on it
    /// (<see cref="Collector.StaffAccidentalColumns"/>), else this chord's own solve.
    /// </summary>
    private static IEnumerable<(string Accidental, int StaffPosition, double X)> ChordAccidentalXs(
        ChordItem chord, bool stemUp, int noteValue)
    {
        if (chord.HasPackedAccidentals)
        {
            foreach (var n in chord.Notes)
                if (n.Accidental is { } acc && n.AccidentalX is { } x)
                    yield return (acc, n.StaffPosition, x);
            yield break;
        }

        foreach (var al in AccidentalStagger.CalculatePositions(
            chord.Notes,
            ChordHeadPositioning.CalculateOffsets(chord.Notes, stemUp, noteValue, 1.0)))
            yield return (al.Accidental, al.StaffPosition, al.XOffset);
    }

    /// <summary>
    /// Merges one accidental's OWN PROFILE — the drawn glyph's outline, walked — into the
    /// staff skylines, its ink starting at <paramref name="inkLeft"/> and its origin line at
    /// <paramref name="headY"/> (Y-up).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:35 Accidental grob::unpure-vertical-skylines-from-stencil
    ///   — so what enters a staff's profile is the glyph's OUTLINE, and the walk is
    ///   lily/stencil-integral.cc:535-563 add_named_glyph_segments, which
    ///   <see cref="TextOutlineSkylines.PlaceMusicGlyph"/> reproduces (the same house the
    ///   Script's own profile reads, so a mover and the ink it clears cannot drift apart).
    /// <para>
    /// ⚠️ IT USED TO BE A FLAT BOX AT THE OUTLINE'S TOP, and pointwise that is a WALL where
    /// the glyph is a spike: a flat's ascender is 0.216 wide against a glyph 0.92 wide
    /// (LilyPond's own dump, ledger script.accidental.staff-to-ink-bottom), so a fermata over
    /// an accidental-bearing head cleared 1.86 of ink that is not above it. The X is
    /// unchanged by this — the glyph is placed so its ink lands exactly where the box's edges
    /// were — and only the SHAPE between them is now the font's.
    /// </para>
    /// </remarks>
    private static void MergeAccidentalInk(
        string accidental, double inkLeft, double headY, StaffSize size,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        // A restore-first composite (♮♯ / ♮♭) is two glyphs — merge each at its place in
        // the composed stencil, the same offsets the box and the renderer use
        // (GlyphMetrics.RestoreMainOf / RestoreMainOffset).
        if (GlyphMetrics.RestoreMainOf(accidental) is { } restoreMain)
        {
            var natBox = GlyphMetrics.GetAccidentalSkylineBBox("natural");
            double mainOrigin = GlyphMetrics.RestoreMainOffset(GlyphMetrics.Design20, restoreMain);
            var mainBox = GlyphMetrics.GetAccidentalSkylineBBox(restoreMain);
            MergeAccidentalInk("natural", inkLeft, headY, size, upSkyline, downSkyline);
            // The main glyph's ink left in the composite, converted through the same
            // origin frame: composite origin = inkLeft − natural.Left.
            MergeAccidentalInk(restoreMain,
                inkLeft + size.Span(mainOrigin + mainBox.Left - natBox.Left),
                headY, size, upSkyline, downSkyline);
            return;
        }
        // headY is Y-up; the outline's own Y is measured from the same origin line the
        // renderer draws the glyph on (SharedRenderer.DrawAccidentalAtInkLeft's noteheadY).
        var bbox = size.Ink(GlyphMetrics.GetAccidentalSkylineBBox(accidental));
        // The glyph ORIGIN: the outline carries its own left bearing, so the origin sits one
        // bearing left of where the ink starts — the same conversion the renderer makes.
        // The RESOLVED profile, merged with its placement rather than copied into a skyline
        // of its own first: an accidental's outline is about eight buildings and a staff
        // carries hundreds of them (see VerticalSkyline.Merge's own remark for the measurement).
        var (up, down) = TextOutlineSkylines.MusicGlyphProfile(
            EmmentalerGlyphs.AccidentalGlyph(accidental),
            size.Span(Rendering.SharedRenderer.FontSize));
        if (up.Count > 0 || down.Count > 0)
        {
            double originX = inkLeft - bbox.Left;
            upSkyline.Merge(up, originX, headY);
            downSkyline.Merge(down, originX, headY);
            return;
        }
        // No walkable glyph — the bundled music font could not be located. The designed
        // outline box, which is what this seeded before the walk existed.
        upSkyline.Merge(VerticalSkyline.FromBox(inkLeft, inkLeft + bbox.Width,
            headY + bbox.Bottom, headY + bbox.Top, VerticalDirection.Up));
        downSkyline.Merge(VerticalSkyline.FromBox(inkLeft, inkLeft + bbox.Width,
            headY + bbox.Bottom, headY + bbox.Top, VerticalDirection.Down));
    }

    /// <summary>
    /// Adds a note's bounding boxes to the skylines.
    /// All coordinates in staff spaces.
    /// </summary>
    private void AddNoteToSkylines(
        NoteItem note,
        double x,
        double staffMiddleUp,
        StaffSize size,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline,
        bool? forcedStemUp = null,
        bool reserveStem = true)
    {
        int noteValue = LayoutUtilities.GetNoteValueFromFraction(note.BaseDuration);
        // Writer's @stemUp/@stemDown outranks the voice default, as the
        // renderer draws it (SharedRenderer.DrawNote).
        bool stemUp = note.ForcedStemUp ?? forcedStemUp ?? note.StemUp;

        AddNoteBoxToSkylines(note.StaffPosition, x, staffMiddleUp, size,
            stemUp, noteValue, upSkyline, downSkyline, reserveStem, note.Notehead);
    }

    /// <summary>
    /// Adds bounding boxes for a note at the given position.
    /// Includes notehead, stem, and ledger lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents
    /// LILYPOND-REF: lily/stencil-integral.cc:55-62 add_*_segments functions
    /// Each graphical element contributes its bounding box to the vertical skyline.
    ///
    /// COORDINATE SYSTEM: like <see cref="StemCalculator"/> and the engravers, the
    /// extents below are reasoned in LilyPond's native <b>Y-up</b> frame — staff-
    /// spaces above the staff middle line, up-positive — so a stem-up box ADDS its
    /// length (matching <c>stem.cc</c>) and ledgers/flags read sign-for-sign against
    /// <c>grob.cc</c>. The skyline itself stores Y-up too (origin at the system/staff
    /// top): the local <c>ToSystemUp</c> only re-bases from the staff middle to that
    /// origin (no reflection), sign-for-sign with <c>skyline.cc</c>. The note center's
    /// Y-up coordinate is just its staff position in staff-spaces (<c>staffPosition/2</c>).
    /// </remarks>
    private void AddNoteBoxToSkylines(
        int staffPosition,
        double x,
        double staffMiddleUp,
        StaffSize size,
        bool stemUp,
        int noteValue,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline,
        bool reserveStem = true,
        NoteheadStyle headStyle = NoteheadStyle.Default)
    {
        // Translate a Y-up coordinate (staff-spaces above THIS staff's middle line)
        // into the shared skyline Y-up frame (whose origin is the system/staff top).
        // No reflection — the skyline now stores Y-up sign-for-sign with skyline.cc.
        double ToSystemUp(double up) => up + staffMiddleUp;

        // ⚠️ THE ONLY BARE NUMBER IN THIS METHOD IS `x`, and that is the rule the port rests
        // on (see StaffSize): x is a paper column shared with every staff of the system and
        // must not scale, while every OTHER quantity here belongs to this staff and does.
        // Each X extent below is written as `x ± <a length>`, so the split is visible in the
        // arithmetic itself.
        double noteUp = size.Span(staffPosition * 0.5);   // staff-spaces above middle, up+

        // The head's VERTICAL extent is the glyph's own ink, from the font's LILC table
        // (GlyphMetricsGenerated), not a nominal staff space. LilyPond builds a grob's
        // skyline from its stencil, so a notehead contributes 0.545 above and below its
        // centre, not 0.5 — measured against 2.26.0 on audit/lp-geometry probe W, where
        // the ink below the last system's refpoint is 3.545000 (= 3.0 + 0.545) and Lily#
        // read 3.500000. That 0.045 propagated into last-bottom-spacing's floor and from
        // there into the page's whole force.
        // LILYPOND-REF: lily/grob.cc:81-85 Grob::simple_vertical_skylines_from_extents_proc — the `vertical-skylines` default, set for a grob that declares none
        //   The extents are the stencil's, and those come from the font's LILC table.
        // LILYPOND-REF: lily/open-type-font.cc:288-289,389-393 lily_character_table_ — load_scheme_table ("LILC", face_), then the per-glyph `bbox` alist read out of it
        //   ec7a2254 moved the X axis onto the same table.
        // ⚠️ THE ADDRESS SAID :85-89 UNTIL 2026-08-05 (session 96), AND THAT IS THE
        //   HORIZONTAL BRANCH — `horizontal-skylines`, three lines further down. The name
        //   beside it was right and the range pointed at the neighbour, which is the exact
        //   failure HANDOFF §7 records from 2026-07-28 ("範囲だけ書いて別の分岐を指した").
        //   Found by re-reading the source to name a symbol for a NEW citation next door.
        // ⚠️ THE EXTENT, DELIBERATELY, and it is not the same choice the Clef and the
        // Accidental make a few lines away: NoteHead declares NO vertical-skylines, so it
        // falls to the default above, while Clef and Accidental ask for skylines from their
        // STENCIL and therefore get the glyph outline.
        // LILYPOND-REF: scm/define-grobs.scm:2595 NoteHead — no vertical-skylines entry, so grob::simple-vertical-skylines-from-extents applies
        // MEASURED, LilyPond dumping both for one glyph
        // (audit/lp-geometry/probes/glyph-skyline.ly): the notehead reports 0.545 for its
        // extent AND its skyline, while its outline stops at 0.544 — so seeding the outline
        // here would be an invention that happens to land 0.001 away.
        var headBox = size.Ink(GlyphMetrics.GetNoteheadBBox(noteValue));

        // Notehead bounding box (head spans noteUp + the glyph's ink in the up frame).
        // ⚠️ X IS THE COLUMN, AND THE GLYPH STARTS THERE (2026-07-29): the renderer
        // draws the head at x as its glyph ORIGIN (left edge) and the dots after
        // x + advance, while this seed used to centre a box ON x — half a head left of
        // every drawn head, stem and ledger. Boxes read the same against a flat
        // neighbour profile, which is why nothing pointwise had caught it; the below
        // outside-staff pass (dynamics vs the real stem/beam X) reads the difference.
        // Same drawn frame the beams, clef and accidentals already seed in.
        // ⚠️ BOTH AXES COME FROM headBox. X read the ADVANCE until 2026-08-05 (session 95)
        // while Y read the ink, in this one expression — the same split the dynamics'
        // support box carried, and the paragraph above already states the rule the X side
        // was breaking: a NoteHead's skyline is its EXTENT, and the extent is the stencil's.
        // MEASURED (audit/lp-geometry/probes/dynamic-support.ly with NoteHead added to the
        // dump): LilyPond reads x=(8.7034 . 10.6654) for the whole head, width 1.9620 —
        // the ink — against the 1.960 advance, and 1.3042 against 1.304 on the black one.
        double noteLeft = x + headBox.Left;
        double noteRight = x + headBox.Right;
        double noteheadWidth = headBox.Right - headBox.Left;
        double headTopUp = noteUp + headBox.Top;
        double headBottomUp = noteUp + headBox.Bottom;

        var noteheadUp = VerticalSkyline.FromBox(noteLeft, noteRight, ToSystemUp(headBottomUp), ToSystemUp(headTopUp), VerticalDirection.Up);
        var noteheadDown = VerticalSkyline.FromBox(noteLeft, noteRight, ToSystemUp(headBottomUp), ToSystemUp(headTopUp), VerticalDirection.Down);
        upSkyline.Merge(noteheadUp);
        downSkyline.Merge(noteheadDown);

        // LILYPOND-REF: lily/ledger-line-spanner.cc:228-230 — `Interval head_extent =
        //   h->extent (common_x, X_AXIS); ledger_extent.widen (length_fraction *
        //   head_extent.length ())`. BOTH the base interval and the basis of the fraction
        //   are the head's grob EXTENT, so both are the ink here too.
        // (noteheadWidth is already this staff's, so the fraction of it needs no second scale.)
        double ledgerExtension = EngravingDefaults.LedgerLengthFraction * noteheadWidth;
        double ledgerThickness = size.Span(EngravingDefaults.LegerLineThickness);
        double ledgerLeft = noteLeft - ledgerExtension;
        double ledgerRight = noteRight + ledgerExtension;

        // Ledger lines above staff (staffPosition >= 6). Each ledger sits at the
        // staff position it serves: its Y-up coordinate is pos/2.
        if (staffPosition >= 6)
        {
            for (int pos = 6; pos <= staffPosition; pos += 2)
            {
                double ledgerUp = size.Span(pos * 0.5);
                double ledgerTopUp = ledgerUp + ledgerThickness / 2;
                double ledgerBottomUp = ledgerUp - ledgerThickness / 2;
                var ledger = VerticalSkyline.FromBox(ledgerLeft, ledgerRight, ToSystemUp(ledgerBottomUp), ToSystemUp(ledgerTopUp), VerticalDirection.Up);
                upSkyline.Merge(ledger);
            }
        }

        // Ledger lines below staff (staffPosition <= -6)
        if (staffPosition <= -6)
        {
            for (int pos = -6; pos >= staffPosition; pos -= 2)
            {
                double ledgerUp = size.Span(pos * 0.5);
                double ledgerTopUp = ledgerUp + ledgerThickness / 2;
                double ledgerBottomUp = ledgerUp - ledgerThickness / 2;
                var ledger = VerticalSkyline.FromBox(ledgerLeft, ledgerRight, ToSystemUp(ledgerBottomUp), ToSystemUp(ledgerTopUp), VerticalDirection.Down);
                downSkyline.Merge(ledger);
            }
        }

        // Stem bounding box. A whole note or a breve has NO stem, so it must not reserve
        // one: the head is the outermost ink and the staff's own line is what a neighbour
        // clears. LILYPOND-REF: lily/stem.cc Stem::is_normal_stem — a stem exists only for
        // duration-log >= 1, i.e. half notes and shorter, which is noteValue >= 2 here.
        //
        // The renderer has always had this test (SharedRenderer.Noteheads.cs, noteValue >= 2)
        // and this side never did, so a whole note was DRAWN stemless and SPACED as though
        // it carried 3.5 of stem. Nothing could see it until the ledger grew a two-staff
        // point: on a single staff the clef reaches further than any of this, and between
        // systems basic-distance wins. audit/lp-geometry staff.staff.lower-note-to-upper-lines
        // measured it as +1.450000 against LilyPond, the whole of which is this stem.
        //
        // A BEAMED note's stem is not this fixed length either: it is drawn to the beam the
        // quanter placed, so when the beam is seeded (AddBeamsToSkyline) reserveStem is false
        // and the stem and its phantom flag are left to the beam. audit/lp-geometry
        // staff.staff.beam-{under,over}-notes.
        if (noteValue < 2 || !reserveStem)
            return;

        // The stem the RENDERER draws, through the same port: Stem::calc_length is not the
        // details.lengths entry, it is that entry LESS the shortening a stem takes when it
        // points the way its own head already lies (stem.cc:519-555). A middle-line head —
        // position 0, which stem.cc:522's `dir * hp[dir] >= 0` includes — is the deepest ink
        // a plain system has, so seeding EngravingDefaults.DefaultStemLength here reserved
        // more room than LilyPond does and the page's last-bottom spring sat on a floor that
        // was too tall. audit/lp-geometry page.{stretched,compressed}.staff-staff-inside and
        // system.{stretched,compressed}-distance.two-staff are four readings of the two
        // forces that error produced.
        // The length rule is a named read in the single house of a column's reach
        // (NoteColumnLayout — HANDOFF §5.2.1②); this seed applies it per HEAD of a chord.
        // LILYPOND-REF: lily/stem.cc:506-557 internal_calc_stem_end_position.
        double stemLength = size.Span(
            NoteColumnLayout.RendererStemLength(stemUp, noteValue, staffPosition));

        // The stem's REAL drawn span: the renderer attaches an up stem at THIS head's right
        // edge (per shape — a half head's is 0.073200 wider) and a down stem at the head's
        // left edge, StemThickness wide (SharedRenderer.Noteheads). The old seed gave a ±1 ss box —
        // wide enough that the below outside-staff pass would push a dynamic off a
        // stem LilyPond's thin sliver lets it tuck beside (DSQ).
        // LILYPOND-REF: lily/stem.cc internal_calc_stem_offset_from_head;
        //   scm/define-grobs.scm Stem thickness 1.3 (line-thickness units).
        // Per head SHAPE too: a styled head's stem stands at that glyph's own
        // attachment point (the head BOX above stays the default head's — a separate
        // pre-existing simplification, out of this seed's stem claim).
        double stemCentre = x + size.Span(LayoutUtilities.StemAttachX(stemUp, noteValue, headStyle));
        double stemHalfWidth = size.Span(EngravingDefaults.StemThickness / 2);
        double flagWidth = size.Span(EngravingDefaults.FlagWidth);

        if (stemUp)
        {
            // Stem extends UPWARD from the head: tip = noteUp + stemLength.
            double stemTipUp = noteUp + stemLength;
            double stemBaseUp = noteUp;
            var stemSkyline = VerticalSkyline.FromBox(stemCentre - stemHalfWidth, stemCentre + stemHalfWidth, ToSystemUp(stemBaseUp), ToSystemUp(stemTipUp), VerticalDirection.Up);
            upSkyline.Merge(stemSkyline);

            // LILYPOND-REF: lily/flag.cc:51-69 Flag::width
            // Flag for eighth notes and shorter (noteValue >= 8), hanging DOWN
            // from the stem tip — drawn AT the stem, running right of it.
            if (noteValue >= 8)
            {
                double flagHeight = size.Span(LayoutUtilities.CalculateFlagHeight(noteValue));
                double flagLeft = stemCentre - stemHalfWidth;
                double flagRight = flagLeft + flagWidth;
                double flagTopUp = stemTipUp;
                double flagBottomUp = stemTipUp - flagHeight;
                var flagSkyline = VerticalSkyline.FromBox(flagLeft, flagRight, ToSystemUp(flagBottomUp), ToSystemUp(flagTopUp), VerticalDirection.Up);
                upSkyline.Merge(flagSkyline);
            }
        }
        else
        {
            // Stem extends DOWNWARD from the head: tip = noteUp - stemLength.
            double stemTipUp = noteUp - stemLength;
            double stemBaseUp = noteUp;
            var stemSkyline = VerticalSkyline.FromBox(stemCentre - stemHalfWidth, stemCentre + stemHalfWidth, ToSystemUp(stemTipUp), ToSystemUp(stemBaseUp), VerticalDirection.Down);
            downSkyline.Merge(stemSkyline);

            // LILYPOND-REF: lily/flag.cc:51-69 Flag::width
            // Flag rises UP from the stem bottom.
            if (noteValue >= 8)
            {
                double flagHeight = size.Span(LayoutUtilities.CalculateFlagHeight(noteValue));
                double flagLeft = stemCentre - stemHalfWidth;
                double flagRight = flagLeft + flagWidth;
                double flagTopUp = stemTipUp + flagHeight;
                double flagBottomUp = stemTipUp;
                var flagSkyline = VerticalSkyline.FromBox(flagLeft, flagRight, ToSystemUp(flagBottomUp), ToSystemUp(flagTopUp), VerticalDirection.Down);
                downSkyline.Merge(flagSkyline);
            }
        }
    }
}
