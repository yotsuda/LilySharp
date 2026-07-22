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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

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
        double systemLeft = double.NaN)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);
        // Building a system skyline merges ~5-8 boxes PER NOTE; resolving each
        // merge individually is O(K^2) (measured: the dominant layout allocation
        // on large scores). Batch the boxes and resolve once at the end.
        upSkyline.BeginBatch();
        downSkyline.BeginBatch();

        // All dimensions in staff spaces (coordinate system is unified)
        double stemLength = EngravingDefaults.DefaultStemLength;
        double noteheadHeight = EngravingDefaults.NoteheadHeight;

        // Process topmost staff for UP skyline (elements above the system)
        var firstStaff = score.StaffGroups[0].PrimaryStaff;
        double firstStaffMiddleY = _staffHeight / 2;
        AddStaffToSkylines(firstStaff, measureLayouts, firstStaffMiddleY,
            stemLength, noteheadHeight, upSkyline, downSkyline);

        // Process bottommost staff for DOWN skyline (elements below the system)
        // LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline
        // Both top and bottom staves contribute to the system's vertical extent.
        var lastGroup = score.StaffGroups[^1];
        var lastStaff = lastGroup.Staves[^1];
        if (lastStaff != firstStaff && systemHeight > 0)
        {
            // Bottom staff's top line is at systemHeight - staffHeight from system reference
            double lastStaffMiddleY = systemHeight - _staffHeight / 2;
            AddStaffToSkylines(lastStaff, measureLayouts, lastStaffMiddleY,
                stemLength, noteheadHeight, upSkyline, downSkyline);
        }

        double bottomLineY = lastStaff != firstStaff && systemHeight > 0
            ? systemHeight
            : _staffHeight;
        SeedSystemStaffSymbol(measureLayouts, systemLeft,
            seedTop: !firstStaff.IsTextRow, topLineY: 0.0,
            seedBottom: !lastStaff.IsTextRow, bottomLineY: bottomLineY,
            upSkyline, downSkyline);

        upSkyline.EndBatch();
        downSkyline.EndBatch();
        return (upSkyline, downSkyline);
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
        bool seedTop, double topLineY, bool seedBottom, double bottomLineY,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (double.IsNaN(systemLeft) || measureLayouts.IsDefaultOrEmpty)
            return;
        double xRight = double.NegativeInfinity;
        foreach (var ml in measureLayouts)
            xRight = Math.Max(xRight, ml.X + ml.Width);
        if (xRight <= systemLeft)
            return;

        // Staff lines are given in device Y from the system top; translate to the
        // skyline's Y-up frame by negating.
        if (seedTop)
            upSkyline.Merge(VerticalSkyline.FromBox(
                systemLeft, xRight, -topLineY, -topLineY, VerticalDirection.Up));
        if (seedBottom)
            downSkyline.Merge(VerticalSkyline.FromBox(
                systemLeft, xRight, -bottomLineY, -bottomLineY, VerticalDirection.Down));
    }

    private void AddStaffToSkylines(
        Staff staff, ImmutableArray<MeasureLayout> measureLayouts,
        double staffMiddleY, double stemLength, double noteheadHeight,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        bool multiVoice = staff.Voices.Length > 1;
        for (int vi = 0; vi < staff.Voices.Length; vi++)
        {
            var voice = staff.Voices[vi];
            // A staff with multiple voices forces stem directions by voice (v1 up,
            // v2 down, ...), exactly as the renderer does (SharedRenderer uses
            // VoiceDefaults.GetDefaultStemUp). The note's own pitch-based StemUp is
            // wrong for the skyline then — e.g. a low bass note in voice 2 is drawn
            // stem-DOWN but its natural direction is up, so its down-stem would be
            // missing from the down-skyline and lyrics/staves below would collide.
            bool? forcedStemUp = multiVoice ? VoiceDefaults.GetDefaultStemUp(vi + 1) : null;

            // Iterate over measureLayouts (which are for the current system only).
            // Use MeasureLayout.MeasureIndex to look up the correct voice measure.
            for (int layoutIndex = 0; layoutIndex < measureLayouts.Length; layoutIndex++)
            {
                var measureLayout = measureLayouts[layoutIndex];
                int measureIndex = measureLayout.MeasureIndex;

                if (measureIndex >= voice.Measures.Length)
                    continue;

                var measure = voice.Measures[measureIndex];
                for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
                {
                    if (measureLayout.Columns.IsDefaultOrEmpty
                        && itemIndex >= measureLayout.Items.Length)
                        continue;

                    var item = measure.Items[itemIndex];
                    double itemX = measureLayout.X + LayoutUtilities.GetItemXOffset(
                        voice.Measures, measureIndex, itemIndex, measureLayout);

                    AddMusicItemToSkylines(item, itemX, staffMiddleY,
                        stemLength, noteheadHeight, upSkyline, downSkyline, forcedStemUp);
                }
            }
        }
    }

    /// <summary>
    /// Builds vertical skylines for a single staff, relative to its own origin (Y=0 at top line).
    /// Used for staff-to-staff spacing within a multi-staff system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 — per-staff skylines for spacing
    /// LILYPOND-REF: lily/page-layout-problem.cc:1075-1124 build_system_skyline()
    /// </remarks>
    public (VerticalSkyline Up, VerticalSkyline Down) BuildStaffSkylines(
        Staff staff, ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<DynamicItem> dynamics = default,
        ImmutableArray<ArticulationLayout> articulationLayouts = default)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);

        double staffMiddleY = _staffHeight / 2;
        double stemLength = EngravingDefaults.DefaultStemLength;
        double noteheadHeight = EngravingDefaults.NoteheadHeight;

        // The staff symbol itself (the 5 lines, ±StaffHeight/2 around the middle)
        // is part of LilyPond's VerticalAxisGroup skyline, so adjacent staves are
        // spaced to clear each other's STAFF LINES — not just their notes. Seed it
        // first as the baseline; notes/ledgers then extend it outward.
        // LILYPOND-REF: lily/axis-group-interface.cc:914-940 skyline_spacing —
        //   inside_staff_skylines include the StaffSymbol grob.
        SeedStaffSymbol(measureLayouts, staffMiddleY, upSkyline, downSkyline);

        AddStaffToSkylines(staff, measureLayouts, staffMiddleY,
            stemLength, noteheadHeight, upSkyline, downSkyline);

        // Dynamics hang below the lowest stem of any voice (or rise above for @f.up);
        // they must widen the inter-staff gap or a dynamic overlaps the adjacent staff.
        // (Score-level dynamics render against the primary staff, so the caller
        // passes them only for that staff.)
        // LILYPOND-REF: lily/align-interface.cc:217-268 — outside-staff grobs join
        // the staff's skyline used for spacing.
        AddDynamicsToSkyline(staff, dynamics, measureLayouts, staffMiddleY, upSkyline, downSkyline);

        // A tab staff's above/below Scripts (fermata, flageolet, accent, …) are
        // engraved only after spacing, so they were absent from this skyline and a
        // forced-above fermata dropped into the gap onto the staff above's low
        // noteheads. Their staff-local extent is spacing-independent, so seed it now.
        AddArticulationLayoutsToSkyline(articulationLayouts, staffMiddleY, upSkyline, downSkyline);

        return (upSkyline, downSkyline);
    }

    /// <summary>
    /// Seeds staff-local Script articulation ink into the per-staff skylines so the
    /// inter-staff gap reserves room for them. The layouts carry Y relative to the
    /// staff top line (= this skyline's Y origin); the ink transform matches
    /// LayoutEngine.AugmentSkylinesWithScripts (BBox Top is up-positive).
    /// </summary>
    private void AddArticulationLayoutsToSkyline(
        ImmutableArray<ArticulationLayout> articulationLayouts,
        double staffMiddleY, VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (articulationLayouts.IsDefaultOrEmpty)
            return;
        foreach (var a in articulationLayouts)
        {
            // ArticulationLayout.YUp is Y-up (staff-spaces above the staff middle);
            // translate it to this skyline's Y-up frame (its origin is the staff top).
            // Ink.Top/Bottom stay up-positive, so they ADD in Y-up.
            double y = a.YUp - staffMiddleY;
            double inkTop = y + a.Ink.Top;
            double inkBottom = y + a.Ink.Bottom;
            var box = VerticalSkyline.FromBox(
                a.X + a.Ink.Left, a.X + a.Ink.Right, inkBottom, inkTop,
                a.IsAbove ? VerticalDirection.Up : VerticalDirection.Down);
            (a.IsAbove ? upSkyline : downSkyline).Merge(box);
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
        ImmutableArray<MeasureLayout> measureLayouts, double staffMiddleY,
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

        double half = _staffHeight / 2.0;
        // Translate the device-frame staff lines to this skyline's Y-up frame (negate):
        // the top line sits above the origin, the bottom line below.
        double staffTop = half - staffMiddleY;      // Y-up of the top line
        double staffBottom = -half - staffMiddleY;  // Y-up of the bottom line

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
        double staffMiddleY, VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        if (dynamics.IsDefaultOrEmpty)
            return;

        var voices = staff.Voices;
        var primaryMeasures = staff.PrimaryVoice.Measures;
        const double dynamicWidth = 1.3;    // approx width of a dynamic glyph
        const double dynamicDescent = 0.3;  // text reaches a little below baseline

        // Same-column dynamics stack AWAY from the staff (see DynamicEngraver); track
        // depth per side so the box reflects the outermost stacked glyph.
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

            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                primaryMeasures, dyn.MeasureIndex, dyn.ItemIndex, measureLayout);

            if (dyn.IsAbove)
            {
                // Upward reach (text ascends from the above baseline); reserve room
                // toward the staff above. DynamicEngraver gives the baseline in the
                // native Y-up frame (above the staff middle); stacking pushes it
                // further UP (+). Ink top = baseline + ascent, mapped to the skyline
                // Y-up frame (origin at the staff top) by ToSystemUp.
                double baselineUp = DynamicEngraver.ColumnAboveBaselineY(
                    voices, dyn.MeasureIndex, dyn.ItemIndex) + depth * DynamicEngraver.StackStep;
                double topUp = baselineUp + DynamicEngraver.DynamicAboveAscent - staffMiddleY;
                var box = VerticalSkyline.FromBox(
                    x - dynamicWidth / 2, x + dynamicWidth / 2,
                    topUp - 0.5, topUp, VerticalDirection.Up);
                upSkyline.Merge(box);
            }
            else
            {
                // Below baseline (negative Y-up); stacking pushes it further DOWN (−).
                // Ink bottom = baseline − descent, mapped to the skyline Y-up frame.
                double baselineUp = DynamicEngraver.ColumnBaselineY(
                    voices, dyn.MeasureIndex, dyn.ItemIndex) - depth * DynamicEngraver.StackStep;
                double bottomUp = baselineUp - dynamicDescent - staffMiddleY;
                var box = VerticalSkyline.FromBox(
                    x - dynamicWidth / 2, x + dynamicWidth / 2,
                    bottomUp, bottomUp + 0.5, VerticalDirection.Down);
                downSkyline.Merge(box);
            }
        }
    }

    /// <summary>
    /// Adds a music item's bounding boxes to the skylines.
    /// Dispatches to appropriate handler based on item type.
    /// </summary>
    private void AddMusicItemToSkylines(
        MusicItem item,
        double x,
        double staffMiddleY,
        double stemLength,
        double noteheadHeight,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline,
        bool? forcedStemUp = null)
    {
        switch (item)
        {
            case NoteItem note:
                AddNoteToSkylines(note, x, staffMiddleY,
                    stemLength, noteheadHeight, upSkyline, downSkyline, forcedStemUp);
                if (note.Accidental != null)
                    AddAccidentalBoxToSkylines(note.Accidental, x,
                        note.StaffPosition * 0.5 - staffMiddleY, upSkyline, downSkyline);
                break;
            case ChordItem chord:
                int chordNoteValue = LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration);
                // Every note of a chord shares the chord's single stem, so the
                // stem box must use the chord's resolved direction — not a
                // per-note threshold. Mirrors the note case (note.StemUp) and
                // the renderer (chord.StemUp). A multi-voice staff forces it.
                // LILYPOND-REF: lily/stem.cc — one Stem per NoteColumn.
                bool chordStemUp = forcedStemUp ?? chord.StemUp;
                foreach (var chordNote in chord.Notes)
                {
                    AddNoteBoxToSkylines(chordNote.StaffPosition, x, staffMiddleY,
                        stemLength, noteheadHeight, chordStemUp, chordNoteValue,
                        upSkyline, downSkyline);
                }
                // Chord accidentals go through the REAL placement machinery
                // (stagger columns, reversed-head offsets) so the skyline
                // carries each glyph at its true X — the same call the
                // renderer draws with.
                // LILYPOND-REF: lily/accidental-placement.cc position_apes.
                foreach (var al in AccidentalStagger.CalculatePositions(
                    chord.Notes,
                    ChordHeadPositioning.CalculateOffsets(chord.Notes, chordStemUp, chordNoteValue, 1.0)))
                {
                    var accBox = GlyphMetrics.GetAccidentalBBox(al.Accidental);
                    // Head Y in the skyline's Y-up frame: staff-position → staff-spaces
                    // above the middle (pos*0.5), then to the skyline origin (staff top).
                    double accHeadY = al.StaffPosition * 0.5 - staffMiddleY;
                    MergeAccidentalInk(
                        x + al.XOffset, x + al.XOffset + accBox.Width,
                        accHeadY + accBox.Top, accHeadY + accBox.Bottom,
                        upSkyline, downSkyline);
                }
                break;
            case RestItem:
                // LILYPOND-REF: lily/rest.cc:61-77 - Rest vertical extent
                // Rests are centered on the staff middle line
                double restHeight = EngravingDefaults.RestHeight;
                double restWidth = EngravingDefaults.RestWidth;
                // Rests are centered on the staff middle line and span ±restHeight/2;
                // translate to this skyline's Y-up frame (staff middle → -staffMiddleY).
                double restTop = restHeight / 2 - staffMiddleY;     // Y-up top edge
                double restBottom = -restHeight / 2 - staffMiddleY; // Y-up bottom edge
                var restUp = VerticalSkyline.FromBox(x - restWidth / 2, x + restWidth / 2, restBottom, restTop, VerticalDirection.Up);
                var restDown = VerticalSkyline.FromBox(x - restWidth / 2, x + restWidth / 2, restBottom, restTop, VerticalDirection.Down);
                upSkyline.Merge(restUp);
                downSkyline.Merge(restDown);
                break;
        }
    }

    /// <summary>
    /// Seeds a printed accidental's ink box (left of its head) into the
    /// skylines. LilyPond's skylines are built from every grob's stencil,
    /// accidentals included — omitting them made everything spaced against
    /// these skylines (the chord-name line, page stacking) graze a sharp or
    /// flat over a high note, papered over by a flat allowance until now.
    /// Chord accidental COLUMNS go through the real placement machinery
    /// (see the ChordItem case in AddMusicItemToSkylines).
    /// LILYPOND-REF: lily/stencil-integral.cc — every stencil contributes
    /// its box.
    /// </summary>
    private static void AddAccidentalBoxToSkylines(
        string accidental, double headX, double headY,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        var bbox = GlyphMetrics.GetAccidentalBBox(accidental);
        double right = headX - GlyphMetrics.AccidentalNoteGap;
        // headY is Y-up; BBox Top/Bottom are up-positive so they ADD.
        MergeAccidentalInk(right - bbox.Width, right,
            headY + bbox.Top, headY + bbox.Bottom, upSkyline, downSkyline);
    }

    /// <summary>Placement machinery shared with the renderer, for chord
    /// accidental columns (see the ChordItem case).</summary>
    private static readonly AccidentalPlacement AccidentalStagger = new();

    private static void MergeAccidentalInk(
        double left, double right, double top, double bottom,
        VerticalSkyline upSkyline, VerticalSkyline downSkyline)
    {
        upSkyline.Merge(VerticalSkyline.FromBox(left, right, bottom, top, VerticalDirection.Up));
        downSkyline.Merge(VerticalSkyline.FromBox(left, right, bottom, top, VerticalDirection.Down));
    }

    /// <summary>
    /// Adds a note's bounding boxes to the skylines.
    /// All coordinates in staff spaces.
    /// </summary>
    private void AddNoteToSkylines(
        NoteItem note,
        double x,
        double staffMiddleY,
        double stemLength,
        double noteheadHeight,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline,
        bool? forcedStemUp = null)
    {
        int noteValue = LayoutUtilities.GetNoteValueFromFraction(note.BaseDuration);
        bool stemUp = forcedStemUp ?? note.StemUp;

        AddNoteBoxToSkylines(note.StaffPosition, x, staffMiddleY,
            stemLength, noteheadHeight, stemUp, noteValue, upSkyline, downSkyline);
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
        double staffMiddleY,
        double stemLength,
        double noteheadHeight,
        bool stemUp,
        int noteValue,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline)
    {
        // Translate a Y-up coordinate (staff-spaces above THIS staff's middle line)
        // into the shared skyline Y-up frame (whose origin is the system/staff top).
        // No reflection — the skyline now stores Y-up sign-for-sign with skyline.cc.
        double ToSystemUp(double up) => up - staffMiddleY;

        double noteUp = staffPosition * 0.5;   // staff-spaces above middle, up+
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;

        // The head's VERTICAL extent is the glyph's own ink, from the font's LILC table
        // (GlyphMetricsGenerated), not a nominal staff space. LilyPond builds a grob's
        // skyline from its stencil, so a notehead contributes 0.545 above and below its
        // centre, not 0.5 — measured against 2.26.0 on audit/lp-geometry probe W, where
        // the ink below the last system's refpoint is 3.545000 (= 3.0 + 0.545) and Lily#
        // read 3.500000. That 0.045 propagated into last-bottom-spacing's floor and from
        // there into the page's whole force.
        // LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents —
        //   the extents are the stencil's, and lily/open-type-font.cc:288,389-407 takes
        //   those from LILC. ec7a2254 moved the X axis onto the same table.
        var headBox = GlyphMetrics.GetNoteheadBBox(noteValue);

        // Notehead bounding box (head spans noteUp + the glyph's ink in the up frame).
        double noteLeft = x - noteheadWidth / 2;
        double noteRight = x + noteheadWidth / 2;
        double headTopUp = noteUp + headBox.Top;
        double headBottomUp = noteUp + headBox.Bottom;

        var noteheadUp = VerticalSkyline.FromBox(noteLeft, noteRight, ToSystemUp(headBottomUp), ToSystemUp(headTopUp), VerticalDirection.Up);
        var noteheadDown = VerticalSkyline.FromBox(noteLeft, noteRight, ToSystemUp(headBottomUp), ToSystemUp(headTopUp), VerticalDirection.Down);
        upSkyline.Merge(noteheadUp);
        downSkyline.Merge(noteheadDown);

        // LILYPOND-REF: lily/ledger-line-spanner.cc:204-233 — ledger extent is
        // the head extent widened by length-fraction (0.25) of the head width.
        double ledgerExtension = EngravingDefaults.LedgerLengthFraction * noteheadWidth;
        double ledgerThickness = EngravingDefaults.LegerLineThickness;
        double ledgerLeft = x - noteheadWidth / 2 - ledgerExtension;
        double ledgerRight = x + noteheadWidth / 2 + ledgerExtension;

        // Ledger lines above staff (staffPosition >= 6). Each ledger sits at the
        // staff position it serves: its Y-up coordinate is pos/2.
        if (staffPosition >= 6)
        {
            for (int pos = 6; pos <= staffPosition; pos += 2)
            {
                double ledgerUp = pos * 0.5;
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
                double ledgerUp = pos * 0.5;
                double ledgerTopUp = ledgerUp + ledgerThickness / 2;
                double ledgerBottomUp = ledgerUp - ledgerThickness / 2;
                var ledger = VerticalSkyline.FromBox(ledgerLeft, ledgerRight, ToSystemUp(ledgerBottomUp), ToSystemUp(ledgerTopUp), VerticalDirection.Down);
                downSkyline.Merge(ledger);
            }
        }

        // Stem bounding box (quarter notes and shorter; half/whole have no stem).
        if (stemUp)
        {
            // Stem extends UPWARD from the head: tip = noteUp + stemLength.
            double stemTipUp = noteUp + stemLength;
            double stemBaseUp = noteUp;
            var stemSkyline = VerticalSkyline.FromBox(noteRight - 1, noteRight + 1, ToSystemUp(stemBaseUp), ToSystemUp(stemTipUp), VerticalDirection.Up);
            upSkyline.Merge(stemSkyline);

            // LILYPOND-REF: lily/flag.cc:51-69 Flag::width
            // Flag for eighth notes and shorter (noteValue >= 8), hanging DOWN
            // from the stem tip.
            if (noteValue >= 8)
            {
                double flagHeight = LayoutUtilities.CalculateFlagHeight(noteValue);
                double flagLeft = x;
                double flagRight = x + EngravingDefaults.FlagWidth;
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
            var stemSkyline = VerticalSkyline.FromBox(noteLeft - 1, noteLeft + 1, ToSystemUp(stemTipUp), ToSystemUp(stemBaseUp), VerticalDirection.Down);
            downSkyline.Merge(stemSkyline);

            // LILYPOND-REF: lily/flag.cc:51-69 Flag::width
            // Flag rises UP from the stem bottom.
            if (noteValue >= 8)
            {
                double flagHeight = LayoutUtilities.CalculateFlagHeight(noteValue);
                double flagLeft = x;
                double flagRight = x + EngravingDefaults.FlagWidth;
                double flagTopUp = stemTipUp + flagHeight;
                double flagBottomUp = stemTipUp;
                var flagSkyline = VerticalSkyline.FromBox(flagLeft, flagRight, ToSystemUp(flagBottomUp), ToSystemUp(flagTopUp), VerticalDirection.Down);
                downSkyline.Merge(flagSkyline);
            }
        }
    }
}
