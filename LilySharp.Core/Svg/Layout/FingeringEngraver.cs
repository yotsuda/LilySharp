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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a single Fingering grob (a finger number digit attached to a note).
/// Coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob
/// LILYPOND-REF: scm/define-grobs.scm — Fingering (font-size . -5, padding . 0.5)
/// </remarks>
public readonly record struct FingeringLayout(
    /// <summary>Measure containing the host note.</summary>
    int MeasureIndex,
    /// <summary>Item index of the host note within its measure.</summary>
    int ItemIndex,
    /// <summary>Finger number (typically 1-5).</summary>
    int Number,
    /// <summary>X coordinate of the digit center (staff spaces from score start).</summary>
    double X,
    /// <summary>Y coordinate of the digit baseline (staff spaces from staff top, positive=down).</summary>
    double Y,
    /// <summary>True = above the staff, false = below.</summary>
    bool IsAbove,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition);

/// <summary>
/// Calculates positions for fingering numbers attached to notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/fingering-engraver.cc Fingering_engraver
/// LILYPOND-REF: lily/script-interface.cc — direction calculation (opposite to stem)
/// LILYPOND-REF: scm/define-grobs.scm Fingering
///   font-size = -5 (very small), padding = 0.5, avoid-slur = #'inside, side-axis = Y
///
/// Fingering numbers default to the side opposite the stem; multiple fingerings
/// on a chord stack vertically (FingeringColumn). This implementation handles
/// single fingerings per note and stacks chord fingerings via item-index ordering.
/// </remarks>
public static class FingeringEngraver
{
    /// <summary>
    /// LILYPOND-REF: scm/define-grobs.scm Fingering — staff-padding default.
    /// Distance from the staff edge to the first fingering digit.
    /// </summary>
    private const double StaffPadding = 0.5;

    /// <summary>
    /// LILYPOND-REF: scm/define-grobs.scm Fingering — padding default 0.3.
    /// Distance between stacked fingerings on the same column.
    /// </summary>
    private const double InterFingeringPadding = 0.3;

    /// <summary>
    /// Approximate digit height for fingering at LP's font-size = -5.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm Fingering (font-size . -5)
    /// magstep(-5) ≈ 0.561; a glyph at full size is ~1.0 staff space tall, so
    /// reduced height ≈ 0.56.
    /// </remarks>
    private const double DigitHeight = 0.56;

    /// <summary>
    /// Calculates layouts for all fingerings in a single-staff score.
    /// </summary>
    public static ImmutableArray<FingeringLayout> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        int staffIndex = -1)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<FingeringLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureLayoutMap(systems);
        var systemMap = LayoutUtilities.BuildMeasureMap(systems);

        var layouts = ImmutableArray.CreateBuilder<FingeringLayout>();

        var voice = score.Voice;
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            var measure = voice.Measures[mi];
            if (!measureMap.TryGetValue(mi, out var measureLayout))
                continue;
            if (!systemMap.TryGetValue(mi, out var info))
                continue;
            var (system, _) = info;

            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                var item = measure.Items[ii];
                if (item is NoteItem note && note.Fingering.HasValue)
                {
                    var layout = BuildLayout(
                        note, mi, ii, measureLayout, system, staffIndex, voice.Measures);
                    if (layout.HasValue)
                        layouts.Add(layout.Value);
                }
                else if (item is ChordItem chord)
                {
                    // LILYPOND-REF: lily/fingering-column.cc — stack chord fingerings.
                    BuildChordFingerings(
                        chord, mi, ii, measureLayout, system, staffIndex, layouts, voice.Measures);
                }
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Emits one <see cref="FingeringLayout"/> per chord pitch that carries a
    /// finger number, stacked vertically on the appropriate side of the chord.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/fingering-column.cc — FingeringColumn vertical stacking.
    /// Stem-up chords place fingerings on the LEFT side ordered top-to-bottom;
    /// stem-down chords place them on the RIGHT, ordered bottom-to-top. This
    /// implementation uses Y placement that mirrors each pitch's staff position
    /// so each finger sits at its corresponding note (a simpler, equally LP-compatible
    /// arrangement).
    /// </remarks>
    private static void BuildChordFingerings(
        ChordItem chord, int measureIndex, int itemIndex,
        MeasureLayout measureLayout, SystemLayout system, int staffIndex,
        ImmutableArray<FingeringLayout>.Builder layouts,
        ImmutableArray<Measure> measures)
    {
        if (measureLayout.Columns.IsDefaultOrEmpty
            && itemIndex >= measureLayout.Items.Length)
            return;
        double centerX = measureLayout.X + LayoutUtilities.GetItemXOffset(
            measures, measureIndex, itemIndex, measureLayout)
            + GlyphMetrics.GetNoteheadAdvance(
                chord.BaseDuration.Numerator == 1 ? (int)chord.BaseDuration.Denominator : 1) / 2.0;

        bool isAbove = !chord.StemUp;
        // Within-system staff offset (0 for the top/only staff). The renderer
        // adds the system's page Y, so Y must NOT include system.Y here.
        double staffY = LayoutUtilities.FindStaffYInSystem(system, staffIndex) - system.Y;
        const double StaffHeight = 4.0;
        double staffMiddle = staffY + StaffHeight / 2.0;

        foreach (var note in chord.Notes)
        {
            if (!note.Fingering.HasValue)
                continue;

            // Y position: align with the note's own staff position, matching the
            // visual convention of putting each finger next to its note.
            double y = StaffFrame.PositionToDevice(note.StaffPosition, staffMiddle);

            layouts.Add(new FingeringLayout(
                MeasureIndex: measureIndex,
                ItemIndex: itemIndex,
                Number: note.Fingering.Value,
                X: centerX,
                Y: y,
                IsAbove: isAbove,
                SourcePosition: chord.SourcePosition));
        }
    }

    private static FingeringLayout? BuildLayout(
        NoteItem note, int measureIndex, int itemIndex,
        MeasureLayout measureLayout, SystemLayout system, int staffIndex,
        ImmutableArray<Measure> measures)
    {
        if (measureLayout.Columns.IsDefaultOrEmpty
            && itemIndex >= measureLayout.Items.Length)
            return null;

        // Centered on the notehead glyph (self-alignment-X = CENTER), via the
        // Items/Columns-aware resolver.
        double centerX = measureLayout.X + LayoutUtilities.GetItemXOffset(
            measures, measureIndex, itemIndex, measureLayout)
            + GlyphMetrics.GetNoteheadAdvance(
                note.BaseDuration.Numerator == 1 ? (int)note.BaseDuration.Denominator : 1) / 2.0;

        // Direction: opposite to stem (LILYPOND-REF: script-interface.cc:23-45).
        bool isAbove = !note.StemUp;

        // Within-system staff offset (0 for the top/only staff). The renderer
        // adds the system's page Y, so Y must NOT include system.Y here.
        double staffY = LayoutUtilities.FindStaffYInSystem(system, staffIndex) - system.Y;
        // Default staff height = 4 staff spaces (5 lines).
        const double StaffHeight = 4.0;

        // Y baseline: place the digit just outside the staff on the appropriate side.
        // For the "above" case, the digit's baseline sits above the staff top by
        // StaffPadding + DigitHeight (so the digit's bottom is StaffPadding above
        // the staff top). For "below", baseline sits below staff bottom.
        double y = isAbove
            ? staffY - StaffPadding
            : staffY + StaffHeight + StaffPadding + DigitHeight;

        return new FingeringLayout(
            MeasureIndex: measureIndex,
            ItemIndex: itemIndex,
            Number: note.Fingering!.Value,
            X: centerX,
            Y: y,
            IsAbove: isAbove,
            SourcePosition: note.SourcePosition);
    }
}
