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
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a grace note group.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:1358-1402 GraceSpacing grob
/// LILYPOND-REF: grace-spacing.cc positioning logic
/// </remarks>
public readonly record struct GraceNoteLayout(
    int MeasureIndex,                    // Measure containing this grace
    int MainNoteItemIndex,               // Item index of the main note
    double X,                            // X position (left edge of grace group)
    double Y,                            // Y position of first grace note
    ImmutableArray<GraceNoteInfo> Notes, // Notes in the grace group
    GraceNoteType Type,                  // Grace type (for slash rendering)
    double Scale,                        // Scale factor (0.65 for grace notes)
    int SourcePosition,                  // For click-to-source mapping
    // Main-note anchor for the grace slur (acciaccatura/appoggiatura).
    // LILYPOND-REF: ly/grace-init.ly startGraceSlur/stopGraceSlur
    double MainNoteX = 0,                // Absolute X of the main notehead
    int MainNoteStaffPosition = 0,       // Staff position of the main notehead
    // Multi-staff: vertical offset of this grace's OWN staff within the system.
    // The renderer recomputes note Y from staff positions, so the offset must
    // travel to it rather than being baked into Y here.
    double StaffYOffset = 0,
    // Non-null when this grace sits on a TAB staff: the renderer then draws each
    // grace as a small fret number (resolved from GraceNoteInfo.Midi) instead of
    // a notehead. null for ordinary notation staves.
    TuningType? Tuning = null,
    // The tab staff's clef (treble_8 shifts written→sounding an octave down).
    ClefType TabClef = ClefType.Treble,
    int SourceIndex = -1,                // F3/B: index into score.GraceNotes (data-pos resolved at render)
    int StaffIndex = -1                  // owning staff (ossia shrink); -1 = unknown/test construction
);

/// <summary>
/// Calculates positions for grace notes.
/// Implements LilyPond's grace note positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: grace-engraver.cc:92-125 Grace_engraver::process_music
/// LILYPOND-REF: grace-spacing.cc:36-80 Grace_spacing::calc_springs
///
/// Grace notes are placed immediately before their main note with:
/// - Smaller size (65% of normal)
/// - Tighter spacing between grace notes
/// - Acciaccatura slash through stem
/// </remarks>
internal static class GraceNoteEngraver
{
    // LILYPOND-REF: define-grobs.scm:1389 font-size = -3 (approximately 0.65)
    private const double GraceScale = GraceNoteItem.ScaleFactor;

    // Width of a single grace note in staff spaces (scaled)
    private const double GraceNoteWidth = 1.2;

    // Space between grace notes
    private const double GraceNoteSpacing = 0.3;

    // Space between grace group and main note
    private const double GraceToMainSpacing = 0.4;

    /// <summary>
    /// Calculates layout for all grace notes in a score.
    /// </summary>
    public static ImmutableArray<GraceNoteLayout> Calculate(
        Score score,
        ImmutableArray<GraceNoteItem> graceNotes,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, double>? staffYByIndex = null,
        Dictionary<int, Staff>? staffByIndex = null)
    {
        if (graceNotes.IsDefaultOrEmpty)
            return ImmutableArray<GraceNoteLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<GraceNoteLayout>(graceNotes.Length);

        for (int gi = 0; gi < graceNotes.Length; gi++)
        {
            var grace = graceNotes[gi];
            // Find the measure layout
            if (grace.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[grace.MeasureIndex];

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && grace.MainNoteItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this grace's OWN staff (multi-staff): its measures (for the
            // main-note X / accidental) and the staff's vertical offset (carried
            // to the renderer via StaffYOffset, since it recomputes note Y).
            var graceMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, grace.StaffIndex, score.Voice.Measures);
            double staffOffset = staffYByIndex != null
                && staffYByIndex.TryGetValue(grace.StaffIndex, out var so) ? so : 0;
            // Tab staves render grace notes as small fret numbers, not noteheads.
            TuningType? tabTuning = null;
            var tabClef = ClefType.Treble;
            if (staffByIndex != null
                && staffByIndex.TryGetValue(grace.StaffIndex, out var gst) && gst.IsTab)
            {
                tabTuning = gst.Tuning ?? TuningType.Guitar;
                tabClef = gst.TabSourceClef;
            }
            if (grace.MeasureIndex >= graceMeasures.Length)
                continue;

            double mainNoteX = LayoutUtilities.GetItemXOffset(
                graceMeasures, grace.MeasureIndex, grace.MainNoteItemIndex, measureLayout);

            // LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 — spring-based grace group width
            double graceGroupWidth = SpacingRules.CalculateGraceGroupSpringWidth(grace.Notes)
                                   - SpacingRules.GraceToMainRod;  // Exclude junction rod (added separately below)

            // Account for main note's accidental width
            // The layout reference point is at the notehead CENTER, but accidentals are
            // drawn from the notehead LEFT edge. We need: centerX + accWidth + gap.
            // LILYPOND-REF: grace-spacing.cc:65-80 positioning before main note
            double accidentalExtent = 0;
            var measure = graceMeasures[grace.MeasureIndex];
            if (grace.MainNoteItemIndex < measure.Items.Length
                && measure.Items[grace.MainNoteItemIndex] is NoteItem mainNote
                && mainNote.Accidental != null)
            {
                var noteheadBBox = GlyphMetrics.GetNoteheadBBox(
                    mainNote.BaseDuration.Denominator <= 1 ? 1 : mainNote.BaseDuration.Denominator <= 2 ? 2 : 4);
                var accBBox = GlyphMetrics.GetAccidentalBBox(mainNote.Accidental);
                accidentalExtent = noteheadBBox.CenterX + accBBox.Width + GlyphMetrics.AccidentalNoteGap;
            }

            // Position grace notes to the left of the main note (including accidental)
            double x = measureLayout.X + mainNoteX - accidentalExtent - graceGroupWidth - GraceToMainSpacing;

            // Y position based on first note's staff position
            double y = 0;
            if (grace.Notes.Length > 0)
            {
                // Convert staff position to Y coordinate
                // Staff position 0 = top line (B5 in treble), each step = 0.5 staff spaces
                y = grace.Notes[0].StaffPosition * 0.5;
            }

            // Main-note anchor for the grace slur (acciaccatura/appoggiatura).
            int mainStaffPosition = grace.MainNoteItemIndex < measure.Items.Length
                ? measure.Items[grace.MainNoteItemIndex] switch
                {
                    NoteItem n => n.StaffPosition,
                    ChordItem { Notes.Length: > 0 } c => c.Notes.Min(cn => cn.StaffPosition),
                    _ => 0
                }
                : 0;

            layouts.Add(new GraceNoteLayout(
                grace.MeasureIndex,
                grace.MainNoteItemIndex,
                x,
                y,
                grace.Notes,
                grace.Type,
                GraceScale,
                grace.SourcePosition,
                MainNoteX: measureLayout.X + mainNoteX,
                MainNoteStaffPosition: mainStaffPosition,
                StaffYOffset: staffOffset,
                Tuning: tabTuning,
                TabClef: tabClef,
                SourceIndex: gi,
                StaffIndex: grace.StaffIndex
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Gets the total width required for a grace note group (fixed-width fallback).
    /// Used when grace note durations are not available.
    /// </summary>
    public static double GetGraceGroupWidth(int noteCount)
    {
        return noteCount * GraceNoteWidth * GraceScale
             + (noteCount - 1) * GraceNoteSpacing * GraceScale
             + GraceToMainSpacing;
    }

    /// <summary>
    /// Gets the total width required for a grace note group using spring-based calculation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
    /// Uses per-group common shortest duration for LP-compliant spacing.
    /// </remarks>
    public static double GetGraceGroupWidth(ImmutableArray<GraceNoteInfo> notes)
    {
        return SpacingRules.CalculateGraceGroupSpringWidth(notes);
    }
}