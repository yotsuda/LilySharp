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
        foreach (var voice in staff.Voices)
        {
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
                    if (itemIndex >= measureLayout.Items.Length)
                        continue;

                    var item = measure.Items[itemIndex];
                    var itemLayout = measureLayout.Items[itemIndex];
                    double itemX = measureLayout.X + itemLayout.X;

                    AddMusicItemToSkylines(item, itemX, staffMiddleY,
                        stemLength, noteheadHeight, upSkyline, downSkyline);
                }
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
        VerticalSkyline downSkyline)
    {
        switch (item)
        {
            case NoteItem note:
                AddNoteToSkylines(note, x, staffMiddleY,
                    stemLength, noteheadHeight, upSkyline, downSkyline);
                break;
            case ChordItem chord:
                int chordNoteValue = LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration);
                foreach (var chordNote in chord.Notes)
                {
                    double noteY = staffMiddleY - chordNote.StaffPosition / 2.0;
                    bool stemUp = chordNote.StaffPosition < 4;
                    AddNoteBoxToSkylines(chordNote.StaffPosition, x, noteY,
                        stemLength, noteheadHeight, stemUp, chordNoteValue,
                        upSkyline, downSkyline);
                }
                break;
            case RestItem:
                // LILYPOND-REF: lily/rest.cc:61-77 - Rest vertical extent
                // Rests are centered on the staff middle line
                double restHeight = EngravingDefaults.RestHeight;
                double restWidth = EngravingDefaults.RestWidth;
                double restY = staffMiddleY; // Rests centered vertically
                double restTop = restY - restHeight / 2;
                double restBottom = restY + restHeight / 2;
                var restUp = VerticalSkyline.FromBox(x - restWidth / 2, x + restWidth / 2, restBottom, restTop, VerticalDirection.Up);
                var restDown = VerticalSkyline.FromBox(x - restWidth / 2, x + restWidth / 2, restBottom, restTop, VerticalDirection.Down);
                upSkyline.Merge(restUp);
                downSkyline.Merge(restDown);
                break;
        }
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
        VerticalSkyline downSkyline)
    {
        double noteY = staffMiddleY - note.StaffPosition / 2.0;
        int noteValue = LayoutUtilities.GetNoteValueFromFraction(note.BaseDuration);
        bool stemUp = note.StemUp;

        AddNoteBoxToSkylines(note.StaffPosition, x, noteY,
            stemLength, noteheadHeight, stemUp, noteValue, upSkyline, downSkyline);
    }

    /// <summary>
    /// Adds bounding boxes for a note at the given position.
    /// Includes notehead, stem, and ledger lines.
    /// All coordinates in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents
    /// LILYPOND-REF: lily/stencil-integral.cc:55-62 add_*_segments functions
    /// Each graphical element contributes its bounding box to the vertical skyline.
    /// </remarks>
    private void AddNoteBoxToSkylines(
        int staffPosition,
        double x,
        double noteY,
        double stemLength,
        double noteheadHeight,
        bool stemUp,
        int noteValue,
        VerticalSkyline upSkyline,
        VerticalSkyline downSkyline)
    {
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;
        double halfNoteheadHeight = noteheadHeight / 2;

        // Notehead bounding box
        double noteLeft = x - noteheadWidth / 2;
        double noteRight = x + noteheadWidth / 2;
        double noteTop = noteY - halfNoteheadHeight;  // Remember: Y increases downward
        double noteBottom = noteY + halfNoteheadHeight;

        // Add notehead to both skylines
        var noteheadUp = VerticalSkyline.FromBox(noteLeft, noteRight, noteBottom, noteTop, VerticalDirection.Up);
        var noteheadDown = VerticalSkyline.FromBox(noteLeft, noteRight, noteBottom, noteTop, VerticalDirection.Down);
        upSkyline.Merge(noteheadUp);
        downSkyline.Merge(noteheadDown);

        // LILYPOND-REF: lily/ledger-line-engraver.cc:82-127
        // Ledger lines extend horizontally from the note
        double ledgerExtension = EngravingDefaults.LegerLineExtension;
        double ledgerThickness = EngravingDefaults.LegerLineThickness;
        double ledgerLeft = x - noteheadWidth / 2 - ledgerExtension;
        double ledgerRight = x + noteheadWidth / 2 + ledgerExtension;

        // Ledger lines above staff (staffPosition >= 6)
        // Use noteY-based calculation to correctly handle any staff position
        if (staffPosition >= 6)
        {
            for (int pos = 6; pos <= staffPosition; pos += 2)
            {
                // Ledger Y is at the same position as a note at this staff position
                double ledgerY = noteY + (staffPosition - pos) / 2.0;
                double ledgerTop = ledgerY - ledgerThickness / 2;
                double ledgerBottom = ledgerY + ledgerThickness / 2;
                var ledgerUp = VerticalSkyline.FromBox(ledgerLeft, ledgerRight, ledgerBottom, ledgerTop, VerticalDirection.Up);
                upSkyline.Merge(ledgerUp);
            }
        }

        // Ledger lines below staff (staffPosition <= -6)
        if (staffPosition <= -6)
        {
            for (int pos = -6; pos >= staffPosition; pos -= 2)
            {
                double ledgerY = noteY + (staffPosition - pos) / 2.0;
                double ledgerTop = ledgerY - ledgerThickness / 2;
                double ledgerBottom = ledgerY + ledgerThickness / 2;
                var ledgerDown = VerticalSkyline.FromBox(ledgerLeft, ledgerRight, ledgerBottom, ledgerTop, VerticalDirection.Down);
                downSkyline.Merge(ledgerDown);
            }
        }

        // Stem bounding box (if applicable - quarter notes and shorter)
        // For half notes and whole notes, no stem
        if (stemUp)
        {
            // Stem goes up from notehead
            double stemTop = noteY - stemLength;
            double stemBottom = noteY;
            var stemSkyline = VerticalSkyline.FromBox(noteRight - 1, noteRight + 1, stemBottom, stemTop, VerticalDirection.Up);
            upSkyline.Merge(stemSkyline);

            // LILYPOND-REF: lily/flag.cc:51-69 Flag::width
            // Flag for eighth notes and shorter (noteValue >= 8)
            if (noteValue >= 8)
            {
                // Flag extends from stem top, curving down-right
                double flagHeight = LayoutUtilities.CalculateFlagHeight(noteValue);
                double flagLeft = x;
                double flagRight = x + EngravingDefaults.FlagWidth;
                double flagTop = stemTop;
                double flagBottom = stemTop + flagHeight;
                var flagSkyline = VerticalSkyline.FromBox(flagLeft, flagRight, flagBottom, flagTop, VerticalDirection.Up);
                upSkyline.Merge(flagSkyline);
            }
        }
        else
        {
            // Stem goes down from notehead
            double stemTop = noteY;
            double stemBottom = noteY + stemLength;
            var stemSkyline = VerticalSkyline.FromBox(noteLeft - 1, noteLeft + 1, stemBottom, stemTop, VerticalDirection.Down);
            downSkyline.Merge(stemSkyline);

            // LILYPOND-REF: lily/flag.cc:51-69 Flag::width
            // Flag for eighth notes and shorter (noteValue >= 8)
            if (noteValue >= 8)
            {
                // Flag extends from stem bottom, curving up-right
                double flagHeight = LayoutUtilities.CalculateFlagHeight(noteValue);
                double flagLeft = x;
                double flagRight = x + EngravingDefaults.FlagWidth;
                double flagTop = stemBottom - flagHeight;
                double flagBottom = stemBottom;
                var flagSkyline = VerticalSkyline.FromBox(flagLeft, flagRight, flagBottom, flagTop, VerticalDirection.Down);
                downSkyline.Merge(flagSkyline);
            }
        }
    }
}
