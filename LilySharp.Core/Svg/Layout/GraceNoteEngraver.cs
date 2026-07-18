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
/// LILYPOND-REF: lily/grace-spacing-engraver.cc:46 Grace_spacing_engraver::process_music
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
    // The tab staff's resolved transposition (bass = −12); combines with TabClef for
    // the written→sounding shift, matching the main tab digits.
    int TabTransposition = 0,
    int SourceIndex = -1,                // F3/B: index into score.GraceNotes (data-pos resolved at render)
    int StaffIndex = -1                  // owning staff (ossia shrink); -1 = unknown/test construction
);

/// <summary>
/// Calculates positions for grace notes.
/// Implements LilyPond's grace note positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/grace-engraver.cc:81 Grace_engraver::process_music
/// LILYPOND-REF: lily/grace-spacing-engraver.cc:46 Grace_spacing_engraver::process_music
///   (there is no grace-spacing.cc / Grace_spacing::calc_springs; the spring logic
///   lives in Grace_spacing_engraver)
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
        Dictionary<int, Staff>? staffByIndex = null,
        ImmutableArray<ArticulationItem> articulations = default)
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
            int tabTransposition = 0;
            if (staffByIndex != null
                && staffByIndex.TryGetValue(grace.StaffIndex, out var gst) && gst.IsTab)
            {
                tabTuning = gst.Tuning ?? TuningType.Guitar;
                tabClef = gst.TabSourceClef;
                tabTransposition = gst.Transposition;
            }
            if (grace.MeasureIndex >= graceMeasures.Length)
                continue;

            double mainNoteX = LayoutUtilities.GetItemXOffset(
                graceMeasures, grace.MeasureIndex, grace.MainNoteItemIndex, measureLayout);

            // LILYPOND-REF: lily/grace-spacing-engraver.cc:46 process_music — spring-based grace group width
            double graceGroupWidth = SpacingRules.CalculateGraceGroupSpringWidth(grace.Notes)
                                   - SpacingRules.GraceToMainRod;  // Exclude junction rod (added separately below)

            // Account for the main item's leftward accidental reach so the grace
            // clears it. For a CHORD this is the STAGGERED accidental stack's leftmost
            // extent, not one accidental — a chord main note reserved nothing before,
            // so a grace collided with the chord's flats/sharps.
            // LILYPOND-REF: lily/grace-spacing-engraver.cc:46 positioning before main note;
            //   lily/accidental-placement.cc for the staggered stack.
            double accidentalExtent = 0;
            var measure = graceMeasures[grace.MeasureIndex];
            if (grace.MainNoteItemIndex < measure.Items.Length)
            {
                var mainItem = measure.Items[grace.MainNoteItemIndex];
                bool hasAccidental = mainItem switch
                {
                    NoteItem n => n.Accidental != null,
                    ChordItem c => c.Notes.Any(cn => cn.Accidental != null),
                    _ => false
                };
                if (hasAccidental)
                    accidentalExtent = SpacingRules.CalculateLeftExtent(mainItem);
            }

            // A wide above-script on the main note (fermata / ornament) overhangs
            // its notehead to the LEFT; a leading grace's flag collides with it
            // unless the grace is pushed further left. Reserve that overhang (plus
            // the grace's own flag reach) so the grace clears the script, the way
            // LilyPond keeps a grace and a fermata apart. Y-gated to a grace sitting
            // at/above the main note, whose flag actually reaches the script's band.
            // LILYPOND-REF: lily/grace-spacing-engraver.cc + the Script joining the
            //   main column's outside-staff skyline.
            double scriptOverhang = ScriptOverhangForGrace(
                articulations, grace, measure, graceGroupWidth);

            // Position grace notes to the left of the main note (including accidental)
            double x = measureLayout.X + mainNoteX - accidentalExtent - graceGroupWidth
                     - GraceToMainSpacing - scriptOverhang;

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
                TabTransposition: tabTransposition,
                SourceIndex: gi,
                StaffIndex: grace.StaffIndex
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Extra leftward shift for a grace group so its flag clears a wide above-script
    /// (fermata / ornament) on the main note. Zero unless the main note carries such
    /// a script AND the grace sits at or above it (only then does the grace's flag
    /// rise into the script's band). The amount is the script's left overhang past
    /// where the grace would otherwise reach, plus the grace's own flag reach — every
    /// term glyph-derived, no hand-tuned constant.
    /// </summary>
    private static double ScriptOverhangForGrace(
        ImmutableArray<ArticulationItem> articulations, GraceNoteItem grace, Measure measure,
        double graceGroupWidth)
    {
        if (articulations.IsDefaultOrEmpty || grace.Notes.IsDefaultOrEmpty
            || grace.MainNoteItemIndex >= measure.Items.Length)
            return 0;
        var mainItem = measure.Items[grace.MainNoteItemIndex];

        int mainPos = mainItem switch
        {
            NoteItem n => n.StaffPosition,
            ChordItem { Notes.Length: > 0 } c => c.Notes.Max(cn => cn.StaffPosition),
            _ => 0
        };
        // Y-gate proxy: a grace below the main note keeps its flag out of the
        // above-script's band, so it needs no extra room.
        if (grace.Notes.Max(n => n.StaffPosition) < mainPos)
            return 0;

        foreach (var art in articulations)
        {
            if (art.StaffIndex != grace.StaffIndex || art.MeasureIndex != grace.MeasureIndex
                || art.ItemIndex != grace.MainNoteItemIndex)
                continue;
            if (ArticulationEngraver.SpacingInkBox(art, mainItem, staffY: 0) is not { } box)
                continue;
            // Clear the grace's rightmost ink from the script's left edge by the SAME
            // gap LilyPond keeps between two note-column grobs: each grob's separation
            // box grows by extra-spacing-width (default 0.1) on the facing side, so a
            // script and a grace clear by 0.1 + 0.1. Everything else is a real glyph
            // extent, not a tuned constant.
            // LILYPOND-REF: lily/separation-item.cc extra-spacing-width default
            //   (-0.1 . 0.1); scm/define-grobs.scm Script inherits it.
            double leftFromCenter = -box.XLeft;                 // script left edge
            int noteValue = mainItem.Duration.Denominator <= 1 ? 1
                : mainItem.Duration.Denominator <= 2 ? 2 : 4;
            double centerX = GlyphMetrics.GetNoteheadBBox(noteValue).CenterX;
            // The grace's rightmost ink, measured LEFTWARD from the main centre.
            double graceInkRightFromCenter = centerX + graceGroupWidth + GraceToMainSpacing
                - GraceInkRight(grace, graceGroupWidth);
            double overhang = leftFromCenter + 2 * ArticulationSpacing.ScriptExtraSpacingWidth
                            - graceInkRightFromCenter;
            return Math.Max(0, overhang);
        }
        return 0;
    }

    /// <summary>The grace group's rightmost ink, measured from the group's LEFT edge.
    /// A single flagged grace protrudes past its head by the flag (placed at the stem
    /// like a normal note's, then scaled); any other group keeps its ink within the
    /// reserved spring width, so the junction (width) is used.</summary>
    private static double GraceInkRight(GraceNoteItem grace, double graceGroupWidth)
    {
        if (grace.Notes.Length == 1)
        {
            var d = grace.Notes[0].BaseDuration;
            if (d.Numerator == 1 && d.Denominator >= 8)
            {
                var flag = GlyphMetrics.GetFlagBBox(d.Denominator, stemUp: true);
                if (flag != default)
                    return (GlyphMetrics.StemUpSE.X + flag.Width) * GraceScale;
            }
        }
        return graceGroupWidth;
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
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:46 Grace_spacing_engraver::process_music
    /// Uses per-group common shortest duration for LP-compliant spacing.
    /// </remarks>
    public static double GetGraceGroupWidth(ImmutableArray<GraceNoteInfo> notes)
    {
        return SpacingRules.CalculateGraceGroupSpringWidth(notes);
    }
}