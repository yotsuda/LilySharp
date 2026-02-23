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
/// Layout information for a tuplet bracket.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 print method
/// </remarks>
public readonly record struct TupletBracketLayout(
    int MeasureIndex,           // Measure containing this tuplet
    double StartX,              // X position of bracket start
    double EndX,                // X position of bracket end
    double Y,                   // Y position (above or below notes)
    string NumberText,          // Text to display (e.g., "3")
    bool IsStemUp,              // Whether bracket goes above (true) or below (false)
    int SourcePosition          // For click-to-source mapping
);

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
    private const double YOffsetAbove = -2.5;  // Above staff
    private const double YOffsetBelow = 5.5;   // Below staff

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
    /// Direction is determined by counting stem directions:
    /// - If stems UP > stems DOWN, bracket goes above (UP)
    /// - If stems DOWN > stems UP, bracket goes below (DOWN)
    /// - If equal, default to above (UP)
    /// </remarks>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures)
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
            
            // Find start and end X positions from item layouts
            if (tuplet.StartNoteIndex >= measureLayout.Items.Length ||
                tuplet.EndNoteIndex >= measureLayout.Items.Length)
                continue;

            var startItem = measureLayout.Items[tuplet.StartNoteIndex];
            var endItem = measureLayout.Items[tuplet.EndNoteIndex];

            // LILYPOND-REF: lily/tuplet-bracket.cc:145-180 calc_x_positions
            // ItemLayout.X is the CENTER of the notehead (Spring-Rod reference point)
            // Bracket should span from left edge of first note to right edge of last note
            const double HalfNoteheadWidth = 0.59;  // NoteheadBlackWidth / 2 = 1.18 / 2
            double startX = measureLayout.X + startItem.X - HalfNoteheadWidth;
            double endX = measureLayout.X + endItem.X + HalfNoteheadWidth;

            // LILYPOND-REF: lily/tuplet-bracket.cc:560-630 get_default_dir
            // Determine bracket direction based on stem directions of notes
            bool isStemUp = CalculateDirection(tuplet, measures);

            // Y position based on stem direction, offset by nesting depth
            // LILYPOND-REF: lily/tuplet-bracket.cc:400-500 nested bracket stacking
            double nestingOffset = tuplet.NestingDepth * NestingDepthOffset;
            double y = isStemUp
                ? YOffsetAbove - nestingOffset   // Deeper nesting → further above
                : YOffsetBelow + nestingOffset;  // Deeper nesting → further below

            layouts.Add(new TupletBracketLayout(
                tuplet.MeasureIndex,
                startX,
                endX,
                y,
                tuplet.DisplayText,
                isStemUp,
                tuplet.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Overload for backward compatibility (defaults to stems up).
    /// </summary>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        return Calculate(tuplets, systems, measureLayouts, ImmutableArray<Measure>.Empty);
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
    /// Gets the edge height for tuplet bracket hooks.
    /// </summary>
    public static double GetEdgeHeight() => EdgeHeight;
}
