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
/// Layout information for an ornament mark.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2175-2230 TrillSpanner grob
/// LILYPOND-REF: script-interface.cc positioning logic
/// </remarks>
public readonly record struct OrnamentLayout(
    int MeasureIndex,       // Measure containing this ornament
    int ItemIndex,          // Item index within measure
    double X,               // X position (center of ornament)
    double Y,               // Y position (above the note)
    string Glyph,           // SMuFL glyph to render
    OrnamentType Type,      // Ornament type
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for ornament marks using LilyPond's side-position-interface algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/script-interface.cc — script positioning
/// LILYPOND-REF: lily/side-position-interface.cc:360-378 total_off calculation
/// LILYPOND-REF: lily/side-position-interface.cc:426-445 staff-padding clamp
///
/// Ornaments are always placed above the note with:
/// - direction: UP (always)
/// - padding: 0.20 staff spaces (scm/script.scm — per ornament script)
/// - staff-padding: 0.25 staff spaces (Script grob default)
///
/// Uses the same non-quantized positioning as ArticulationEngraver for fermata/ornaments:
///   1. Calculate support extent (stem length or notehead height)
///   2. Add ornament near-extent + padding = total_off
///   3. Apply staff-padding clamp
/// </remarks>
public static class OrnamentEngraver
{
    // Ornaments are Script grobs; their padding is set per script in
    // scm/script.scm, where trill / prall / mordent / turn (and variants) all
    // use (padding . 0.20). define-grobs.scm:2195 (the old citation) is an
    // unrelated lyric grob, and 0.5 was the wrong value.
    // LILYPOND-REF: scm/script.scm trill/prall/mordent/turn (padding . 0.20)
    private const double Padding = 0.20;

    // LILYPOND-REF: scm/define-grobs.scm:3004 Script (staff-padding . 0.25)
    private const double StaffPadding = 0.25;

    // Ornament glyph near-extent (half-height, how far glyph extends toward note)
    // LILYPOND-REF: feta-scripts.mf — ornament glyph vertical extent ≈ 1.0 staff space total
    private const double OrnamentNearExtent = 0.5;

    // Staff geometry + notehead/stem extents: canonical values in EngravingDefaults.
    private const double StaffMiddle = EngravingDefaults.StaffMiddle;
    private const double StaffTop = 0.0;
    private const double NoteheadHalfHeight = EngravingDefaults.NoteheadHalfHeight;
    private const double DefaultStemLength = EngravingDefaults.DefaultStemLength;

    /// <summary>
    /// Calculates layout for all ornaments in a score.
    /// </summary>
    public static ImmutableArray<OrnamentLayout> Calculate(
        Score score,
        ImmutableArray<OrnamentItem> ornaments,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (ornaments.IsDefaultOrEmpty)
            return ImmutableArray<OrnamentLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<OrnamentLayout>(ornaments.Length);

        foreach (var ornament in ornaments)
        {
            if (ornament.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[ornament.MeasureIndex];

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && ornament.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Calculate X position (centered on the note)
            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                score.Voice.Measures, ornament.MeasureIndex, ornament.ItemIndex, measureLayout);

            // Get staff position and stem direction from the music item
            // LILYPOND-REF: script-engraver.cc:92-125 acknowledge_note_head
            int staffPosition = 0;
            bool stemUp = true;
            if (ornament.MeasureIndex < score.Voice.Measures.Length)
            {
                var measure = score.Voice.Measures[ornament.MeasureIndex];
                if (ornament.ItemIndex < measure.Items.Length)
                {
                    var item = measure.Items[ornament.ItemIndex];
                    staffPosition = GetStaffPosition(item);
                    stemUp = GetStemUp(item, staffPosition);
                }
            }

            // Calculate Y position using side-position-interface algorithm
            // LILYPOND-REF: side-position-interface.cc:360-378 total_off calculation
            double y = CalculateYPosition(staffPosition, stemUp);

            layouts.Add(new OrnamentLayout(
                ornament.MeasureIndex,
                ornament.ItemIndex,
                x,
                y,
                ornament.GetGlyph(),
                ornament.Type,
                ornament.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Calculates Y position using the non-quantized path from side-position-interface.
    /// Ornaments are always placed above the note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:360-378 total_off calculation
    /// LILYPOND-REF: lily/side-position-interface.cc:426-445 staff-padding clamp
    /// </remarks>
    private static double CalculateYPosition(int staffPosition, bool stemUp)
    {
        // Convert staff position to Y coordinate
        double noteY = StaffMiddle - staffPosition * 0.5;

        // Support extent: how far the note+stem extends above
        // LILYPOND-REF: side-position-interface.cc:229-264 support skyline
        double supportExtent = stemUp ? DefaultStemLength : NoteheadHalfHeight;

        // total_off = support extent + glyph near extent + padding
        double totalOff = supportExtent + OrnamentNearExtent + Padding;
        double targetY = noteY - totalOff;

        // Staff-padding clamp: ensure glyph clears staff top
        // LILYPOND-REF: side-position-interface.cc:426-445
        double glyphBottom = targetY + OrnamentNearExtent;
        double staffEdge = StaffTop - StaffPadding;
        if (glyphBottom > staffEdge)
            targetY = staffEdge - OrnamentNearExtent;

        return targetY;
    }

    private static int GetStaffPosition(MusicItem item) => item switch
    {
        NoteItem note => note.StaffPosition,
        ChordItem chord => chord.Notes.Length > 0
            ? (chord.Notes.Max(n => n.StaffPosition) + chord.Notes.Min(n => n.StaffPosition)) / 2
            : 4,
        _ => 0
    };

    private static bool GetStemUp(MusicItem item, int staffPosition) => item switch
    {
        NoteItem note => note.StemUp,
        ChordItem chord => chord.StemUp,
        _ => staffPosition < 0
    };
}
