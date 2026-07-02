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
public sealed class SkylineBuilder
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
        double systemHeight = 0)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);

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

        return (upSkyline, downSkyline);
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
        ImmutableArray<DynamicItem> dynamics = default)
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

        return (upSkyline, downSkyline);
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
        double staffTop = staffMiddleY - half;     // device Y of the top line
        double staffBottom = staffMiddleY + half;   // device Y of the bottom line

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
        double staffTopDevice = staffMiddleY - _staffHeight / 2;
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
                // toward the staff above.
                double baseline = DynamicEngraver.ColumnAboveBaselineY(
                    voices, dyn.MeasureIndex, dyn.ItemIndex) - depth * DynamicEngraver.StackStep;
                double deviceTop = staffTopDevice + baseline - DynamicEngraver.DynamicAboveAscent;
                var box = VerticalSkyline.FromBox(
                    x - dynamicWidth / 2, x + dynamicWidth / 2,
                    deviceTop + 0.5, deviceTop, VerticalDirection.Up);
                upSkyline.Merge(box);
            }
            else
            {
                double baseline = DynamicEngraver.ColumnBaselineY(
                    voices, dyn.MeasureIndex, dyn.ItemIndex) + depth * DynamicEngraver.StackStep;
                double deviceBottom = staffTopDevice + baseline + dynamicDescent;
                var box = VerticalSkyline.FromBox(
                    x - dynamicWidth / 2, x + dynamicWidth / 2,
                    deviceBottom, deviceBottom - 0.5, VerticalDirection.Down);
                downSkyline.Merge(box);
            }
        }
    }

    /// <summary>
    /// Builds vertical skylines for a single-staff system.
    /// </summary>
    public (VerticalSkyline Up, VerticalSkyline Down) BuildSystemSkylines(
        List<Measure> measures,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var upSkyline = new VerticalSkyline(VerticalDirection.Up);
        var downSkyline = new VerticalSkyline(VerticalDirection.Down);

        // All dimensions in staff spaces (coordinate system is unified)
        double staffMiddleY = _staffHeight / 2;
        double stemLength = EngravingDefaults.DefaultStemLength;
        double noteheadHeight = EngravingDefaults.NoteheadHeight;

        // Process measures in this system
        for (int measureIndex = 0; measureIndex < measures.Count; measureIndex++)
        {
            if (measureIndex >= measureLayouts.Length)
                continue;

            var measure = measures[measureIndex];
            var measureLayout = measureLayouts[measureIndex];
            for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
            {
                if (itemIndex >= measureLayout.Items.Length)
                    continue;

                var item = measure.Items[itemIndex];
                var itemLayout = measureLayout.Items[itemIndex];
                double itemX = measureLayout.X + itemLayout.X;

                // LILYPOND-REF: lily/grob.cc:85-89 - Each grob contributes to skyline
                AddMusicItemToSkylines(item, itemX, staffMiddleY,
                    stemLength, noteheadHeight, upSkyline, downSkyline);
            }
        }

        return (upSkyline, downSkyline);
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
                        staffMiddleY - note.StaffPosition * 0.5, upSkyline, downSkyline);
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
                    double accHeadY = staffMiddleY - al.StaffPosition * 0.5;
                    MergeAccidentalInk(
                        x + al.XOffset, x + al.XOffset + accBox.Width,
                        accHeadY - accBox.Top, accHeadY - accBox.Bottom,
                        upSkyline, downSkyline);
                }
                break;
            case RestItem:
                // LILYPOND-REF: lily/rest.cc:61-77 - Rest vertical extent
                // Rests are centered on the staff middle line
                double restHeight = EngravingDefaults.RestHeight;
                double restWidth = EngravingDefaults.RestWidth;
                // Rests are centered on the staff middle line — Y-up 0 — and span
                // ±restHeight/2; reflect to device via staffMiddleY - up.
                double restTop = staffMiddleY - restHeight / 2;     // up frame: +restHeight/2
                double restBottom = staffMiddleY + restHeight / 2;  // up frame: -restHeight/2
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
        MergeAccidentalInk(right - bbox.Width, right,
            headY - bbox.Top, headY - bbox.Bottom, upSkyline, downSkyline);
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
    /// <c>grob.cc</c>. They are reflected to the shared device frame (Y-down) only
    /// at the <see cref="VerticalSkyline.FromBox"/> boundary via
    /// <c>staffMiddleY - up</c> (the local <c>ToDevice</c>), the single chokepoint
    /// that stands in for LilyPond's stencil-time flip. The note center's Y-up
    /// coordinate is just its staff position in staff-spaces (<c>staffPosition/2</c>).
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
        // Reflect a Y-up coordinate (staff-spaces above the middle line) to device.
        double ToDevice(double up) => staffMiddleY - up;

        double noteUp = staffPosition * 0.5;   // staff-spaces above middle, up+
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;
        double halfNoteheadHeight = noteheadHeight / 2;

        // Notehead bounding box (head spans noteUp ± half in the up frame).
        double noteLeft = x - noteheadWidth / 2;
        double noteRight = x + noteheadWidth / 2;
        double headTopUp = noteUp + halfNoteheadHeight;
        double headBottomUp = noteUp - halfNoteheadHeight;

        var noteheadUp = VerticalSkyline.FromBox(noteLeft, noteRight, ToDevice(headBottomUp), ToDevice(headTopUp), VerticalDirection.Up);
        var noteheadDown = VerticalSkyline.FromBox(noteLeft, noteRight, ToDevice(headBottomUp), ToDevice(headTopUp), VerticalDirection.Down);
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
                var ledger = VerticalSkyline.FromBox(ledgerLeft, ledgerRight, ToDevice(ledgerBottomUp), ToDevice(ledgerTopUp), VerticalDirection.Up);
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
                var ledger = VerticalSkyline.FromBox(ledgerLeft, ledgerRight, ToDevice(ledgerBottomUp), ToDevice(ledgerTopUp), VerticalDirection.Down);
                downSkyline.Merge(ledger);
            }
        }

        // Stem bounding box (quarter notes and shorter; half/whole have no stem).
        if (stemUp)
        {
            // Stem extends UPWARD from the head: tip = noteUp + stemLength.
            double stemTipUp = noteUp + stemLength;
            double stemBaseUp = noteUp;
            var stemSkyline = VerticalSkyline.FromBox(noteRight - 1, noteRight + 1, ToDevice(stemBaseUp), ToDevice(stemTipUp), VerticalDirection.Up);
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
                var flagSkyline = VerticalSkyline.FromBox(flagLeft, flagRight, ToDevice(flagBottomUp), ToDevice(flagTopUp), VerticalDirection.Up);
                upSkyline.Merge(flagSkyline);
            }
        }
        else
        {
            // Stem extends DOWNWARD from the head: tip = noteUp - stemLength.
            double stemTipUp = noteUp - stemLength;
            double stemBaseUp = noteUp;
            var stemSkyline = VerticalSkyline.FromBox(noteLeft - 1, noteLeft + 1, ToDevice(stemTipUp), ToDevice(stemBaseUp), VerticalDirection.Down);
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
                var flagSkyline = VerticalSkyline.FromBox(flagLeft, flagRight, ToDevice(flagBottomUp), ToDevice(flagTopUp), VerticalDirection.Down);
                downSkyline.Merge(flagSkyline);
            }
        }
    }
}
