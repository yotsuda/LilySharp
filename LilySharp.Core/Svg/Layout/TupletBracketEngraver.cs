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
/// Layout information for a tuplet bracket together with its number.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 print method
/// LILYPOND-REF: lily/tuplet-number.cc — TupletNumber grob (LP keeps it as its
/// own grob, but the rendered stencil is always centered on the bracket).
/// LilySharp combines bracket + number into one Layout record; the number's
/// position derives from the bracket midpoint and ShowBracket=false suppresses
/// the bracket lines (number-only display, matching LP's standard appearance
/// for fully beamed tuplets).
/// </remarks>
public readonly record struct TupletBracketLayout(
    int MeasureIndex,           // Measure containing this tuplet
    double StartX,              // X position of bracket start
    double EndX,                // X position of bracket end
    double StartY,              // Y position at bracket start (supports slope)
    double EndY,                // Y position at bracket end (supports slope)
    string NumberText,          // Text to display (e.g., "3")
    bool IsStemUp,              // Whether bracket goes above (true) or below (false)
    bool ShowBracket,           // False = all notes beamed, show number only
    int SourcePosition          // For click-to-source mapping
)
{
    /// <summary>
    /// X coordinate of the tuplet number's visual center (LP TupletNumber X).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-number.cc — TupletNumber sits at bracket midpoint.
    /// </remarks>
    public double NumberX => (StartX + EndX) / 2.0;

    /// <summary>
    /// Y coordinate of the tuplet number's visual center (LP TupletNumber Y).
    /// </summary>
    public double NumberY => (StartY + EndY) / 2.0;
}

/// <summary>
/// Calculates positions for tuplet brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:1-400 Tuplet_bracket_interface
/// LILYPOND-REF: lily/tuplet-bracket.cc:560-630 get_default_dir
/// LILYPOND-REF: lily/tuplet-engraver.cc:1-200 Tuplet_engraver
///
/// LilyPond tuplet brackets:
/// - Horizontal bracket above or below the note group
/// - Number (e.g., "3") centered on the bracket
/// - Small hooks at bracket ends
/// - Position depends on majority stem direction of notes
/// </remarks>
public static class TupletBracketEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    private const double BracketPadding = 0.5;
    private const double EdgeHeight = 0.7;
    private const double StaffMiddleY = 2.0;    // staff-top frame: middle line = StaffHeight/2
    private const double YOffsetAbove = -2.5;  // Above staff
    private const double YOffsetBelow = 5.5;   // Below staff
    private const double HalfNoteheadWidth = 0.59;  // NoteheadBlackWidth / 2

    // LILYPOND-REF: scm/define-grobs.scm TupletBracket.max-slope
    private const double MaxSlope = 0.5;

    // LILYPOND-REF: stem.cc:93 default stem-length = 3.5
    private const double DefaultStemLength = 3.5;

    /// <summary>
    /// Y offset per nesting depth level for stacked nested tuplet brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:400-500 nested bracket stacking
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket.outside-staff-priority
    /// </remarks>
    private const double NestingDepthOffset = 2.0;

    /// <summary>
    /// Calculates layout for all tuplet brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:560-630 get_default_dir
    /// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 calc_position_and_height (slope)
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket.bracket-visibility = if-no-beam
    ///
    /// Direction is determined by counting stem directions:
    /// - If stems UP > stems DOWN, bracket goes above (UP)
    /// - If stems DOWN > stems UP, bracket goes below (DOWN)
    /// - If equal, default to above (UP)
    ///
    /// bracket-visibility: if all notes in the tuplet are beamed, the bracket
    /// is hidden but the number is still shown.
    ///
    /// Slope: bracket follows the contour of the first and last note's staff position.
    /// </remarks>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures,
        ImmutableArray<BeamGroup> beamGroups = default)
    {
        if (tuplets.IsDefaultOrEmpty)
            return ImmutableArray<TupletBracketLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<TupletBracketLayout>(tuplets.Length);

        foreach (var tuplet in tuplets)
        {
            // Find measure layout
            if (tuplet.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[tuplet.MeasureIndex];

            // Find start and end X positions. Multi-staff layouts use
            // timing-aligned columns, not Items — go through the shared
            // resolver (a direct Items index silently shifts the bracket).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && (tuplet.StartNoteIndex >= measureLayout.Items.Length ||
                    tuplet.EndNoteIndex >= measureLayout.Items.Length))
                continue;

            double startOffset = LayoutUtilities.GetItemXOffset(
                measures, tuplet.MeasureIndex, tuplet.StartNoteIndex, measureLayout);
            double endOffset = LayoutUtilities.GetItemXOffset(
                measures, tuplet.MeasureIndex, tuplet.EndNoteIndex, measureLayout);

            // LILYPOND-REF: lily/tuplet-bracket.cc:145-180 calc_x_positions
            double startX = measureLayout.X + startOffset - HalfNoteheadWidth;
            double endX = measureLayout.X + endOffset + HalfNoteheadWidth;

            // LILYPOND-REF: lily/tuplet-bracket.cc:560-630 get_default_dir
            bool isStemUp = CalculateDirection(tuplet, measures);

            // LILYPOND-REF: scm/define-grobs.scm bracket-visibility = if-no-beam
            bool showBracket = !AreAllNotesBeamed(tuplet, beamGroups);

            // LILYPOND-REF: lily/tuplet-bracket.cc:200-350 slope calculation
            // Calculate slope based on first/last note staff positions
            var (startY, endY) = CalculateSlope(tuplet, measures, isStemUp);

            layouts.Add(new TupletBracketLayout(
                tuplet.MeasureIndex,
                startX,
                endX,
                startY,
                endY,
                tuplet.DisplayText,
                isStemUp,
                showBracket,
                tuplet.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Overload for backward compatibility (defaults to stems up, no beam info).
    /// </summary>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        return Calculate(tuplets, systems, measureLayouts, ImmutableArray<Measure>.Empty);
    }

    /// <summary>
    /// Overload with measures but no beam info.
    /// </summary>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures)
    {
        return Calculate(tuplets, systems, measureLayouts, measures, default);
    }

    /// <summary>
    /// Calculates the bracket direction based on stem directions of notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:597-629 get_default_dir implementation
    /// Counts stem directions and returns majority direction.
    /// If equal, returns UP (bracket above).
    /// </remarks>
    private static bool CalculateDirection(TupletBracketItem tuplet, ImmutableArray<Measure> measures)
    {
        if (measures.IsDefaultOrEmpty || tuplet.MeasureIndex >= measures.Length)
            return true; // Default: stems up (bracket above)

        var measure = measures[tuplet.MeasureIndex];
        int stemsUp = 0;
        int stemsDown = 0;

        // Count stem directions for notes in the tuplet
        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            
            // LILYPOND-REF: lily/tuplet-bracket.cc:605-615
            // Skip rests when counting directions
            if (item is NoteItem note)
            {
                if (note.StemUp)
                    stemsUp++;
                else
                    stemsDown++;
            }
            else if (item is ChordItem chord)
            {
                if (chord.StemUp)
                    stemsUp++;
                else
                    stemsDown++;
            }
        }

        // LILYPOND-REF: lily/tuplet-bracket.cc:627-629
        // Return majority direction, or UP if equal
        return stemsUp >= stemsDown;
    }

    /// <summary>
    /// Checks whether all notes in the tuplet are covered by a beam group.
    /// If so, the bracket can be hidden (bracket-visibility = if-no-beam).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket.bracket-visibility = if-no-beam
    /// LILYPOND-REF: lily/tuplet-bracket.cc:79-95 bracket visibility check
    /// </remarks>
    private static bool AreAllNotesBeamed(TupletBracketItem tuplet, ImmutableArray<BeamGroup> beamGroups)
    {
        if (beamGroups.IsDefaultOrEmpty)
            return false;

        // Find a beam group in the same measure that covers the entire tuplet range
        foreach (var beam in beamGroups)
        {
            if (beam.MeasureIndex != tuplet.MeasureIndex)
                continue;

            int beamEnd = beam.StartIndex + beam.Members.Length - 1;

            // Check if the beam completely covers the tuplet's note range
            if (beam.StartIndex <= tuplet.StartNoteIndex && beamEnd >= tuplet.EndNoteIndex)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates the Y positions (with slope) for the tuplet bracket
    /// based on the staff positions of the first and last notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 calc_position_and_height
    /// The bracket follows the contour of the notes. The slope is limited
    /// to avoid excessively tilted brackets.
    /// </remarks>
    private static (double startY, double endY) CalculateSlope(
        TupletBracketItem tuplet, ImmutableArray<Measure> measures, bool isStemUp)
    {
        double nestingOffset = tuplet.NestingDepth * NestingDepthOffset;
        double baseY = isStemUp
            ? YOffsetAbove - nestingOffset
            : YOffsetBelow + nestingOffset;

        if (measures.IsDefaultOrEmpty || tuplet.MeasureIndex >= measures.Length)
            return (baseY, baseY);

        var measure = measures[tuplet.MeasureIndex];

        // Get staff positions of first and last notes
        int? firstPos = null, lastPos = null;
        int? highestPos = null, lowestPos = null;

        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < measure.Items.Length; i++)
        {
            int? pos = measure.Items[i] switch
            {
                NoteItem note => note.StaffPosition,
                ChordItem chord when chord.Notes.Length > 0 =>
                    isStemUp ? chord.Notes.Max(n => n.StaffPosition)
                             : chord.Notes.Min(n => n.StaffPosition),
                _ => null
            };

            if (pos == null) continue;

            firstPos ??= pos;
            lastPos = pos;
            highestPos = highestPos == null ? pos : Math.Max(highestPos.Value, pos.Value);
            lowestPos = lowestPos == null ? pos : Math.Min(lowestPos.Value, pos.Value);
        }

        if (firstPos == null || lastPos == null)
            return (baseY, baseY);

        // LILYPOND-REF: lily/tuplet-bracket.cc:270-310 slope calculation
        // Convert staff position difference to slope (half staff spaces)
        double positionDiff = (lastPos.Value - firstPos.Value) * 0.5;

        // Limit slope to MaxSlope staff spaces per bracket width
        // LILYPOND-REF: scm/define-grobs.scm TupletBracket.max-slope = 0.5
        double slope = positionDiff;
        if (Math.Abs(slope) > MaxSlope)
            slope = Math.Sign(slope) * MaxSlope;

        // Direction: bracket above → negative slope follows ascending pitch;
        // bracket below → positive slope follows ascending pitch
        double slopeDir = isStemUp ? -slope : slope;

        double startY = baseY + slopeDir * 0.5;
        double endY = baseY - slopeDir * 0.5;

        // Ensure bracket clears all notes in the range
        // LILYPOND-REF: lily/tuplet-bracket.cc:320-340 collision avoidance
        if (isStemUp)
        {
            // Bracket above: ensure it's above the highest note.
            // Y is in the staff-top frame (Y=0 at the top line); a note at staff
            // position p sits at StaffMiddle - p*0.5 = 2.0 - p*0.5. The +2.0 was
            // missing here, placing the edge two staff-spaces too high.
            double highestNoteY = YOffsetAbove - nestingOffset;
            if (highestPos != null)
            {
                double noteEdge = StaffMiddleY - (highestPos.Value * 0.5) - DefaultStemLength - BracketPadding;
                highestNoteY = Math.Min(highestNoteY, noteEdge);
            }
            if (startY > highestNoteY) startY = highestNoteY;
            if (endY > highestNoteY) endY = highestNoteY;
        }
        else
        {
            // Bracket below: ensure it's below the lowest note (same staff-top
            // frame; the +2.0 staff-middle offset was missing here too).
            double lowestNoteY = YOffsetBelow + nestingOffset;
            if (lowestPos != null)
            {
                double noteEdge = StaffMiddleY - (lowestPos.Value * 0.5) + DefaultStemLength + BracketPadding;
                lowestNoteY = Math.Max(lowestNoteY, noteEdge);
            }
            if (startY < lowestNoteY) startY = lowestNoteY;
            if (endY < lowestNoteY) endY = lowestNoteY;
        }

        return (startY, endY);
    }

    /// <summary>
    /// Gets the edge height for tuplet bracket hooks.
    /// </summary>
    public static double GetEdgeHeight() => EdgeHeight;
}
