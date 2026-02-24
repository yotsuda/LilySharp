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
/// Layout information for a tremolo marking.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2760-2810 StemTremolo grob
/// LILYPOND-REF: stem-tremolo.cc positioning logic
/// </remarks>
public readonly record struct TremoloLayout(
    int MeasureIndex,       // Measure containing this tremolo
    int ItemIndex,          // Item index within measure
    double X,               // X position (center of tremolo beams)
    double Y,               // Y position (center of tremolo group)
    int BeamCount,          // Number of tremolo beams
    bool StemUp,            // Whether the note has stem up
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for tremolo markings.
/// Implements LilyPond's tremolo positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: stem-tremolo.cc:92-125 Stem_tremolo::calc_y_offset
/// LILYPOND-REF: stem-tremolo.cc:36-80 beam positioning
///
/// Tremolo beams are placed on the stem:
/// - Centered vertically on the stem
/// - Angled at approximately 30 degrees
/// - Spaced 0.8 staff spaces apart
/// </remarks>
public static class TremoloEngraver
{
    // LILYPOND-REF: define-grobs.scm:2780 beam-gap = 0.8
    private const double BeamGap = 0.8;

    // LILYPOND-REF: define-grobs.scm:2785 beam-thickness = 0.48
    private const double BeamThickness = 0.48;

    // LILYPOND-REF: stem-tremolo.cc:45-79 calc-slope
    // Default slope; steeper (0.40) for down stems with flags
    private const double DefaultBeamSlope = 0.25;
    private const double FlagDownStemSlope = 0.40;

    // LILYPOND-REF: stem-tremolo.cc:81-94 calc-width
    // Width depends on flag presence: 1.0 with flag, 1.5 without
    private const double FlagBeamWidth = 1.0;
    private const double NoFlagBeamWidth = 1.5;

    /// <summary>
    /// Calculates layout for all tremolos in a score.
    /// </summary>
    public static ImmutableArray<TremoloLayout> Calculate(
        Score score,
        ImmutableArray<TremoloItem> tremolos,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (tremolos.IsDefaultOrEmpty)
            return ImmutableArray<TremoloLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<TremoloLayout>(tremolos.Length);

        foreach (var tremolo in tremolos)
        {
            // Find the measure layout
            if (tremolo.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[tremolo.MeasureIndex];

            // Find the item layout within the measure
            if (tremolo.ItemIndex >= measureLayout.Items.Length)
                continue;

            var itemLayout = measureLayout.Items[tremolo.ItemIndex];

            // Get stem direction from the measure's items
            // For now, assume stem up for notes above middle of staff
            bool stemUp = true;

            // Try to find the corresponding note in the score
            var measure = score.Voice.Measures[tremolo.MeasureIndex];
            if (tremolo.ItemIndex < measure.Items.Length)
            {
                var item = measure.Items[tremolo.ItemIndex];
                if (item is NoteItem note)
                {
                    stemUp = note.StemUp;
                }
            }

            // Calculate X position (on the stem)
            // LILYPOND-REF: stem-tremolo.cc:65 x_offset calculation
            double x = measureLayout.X + itemLayout.X;

            // Calculate Y position (center of stem)
            // LILYPOND-REF: stem-tremolo.cc:92-125 y_offset calculation
            // Tremolo is placed at the midpoint of the stem
            double y = stemUp ? 0.5 : 3.5; // Approximate center of stem

            layouts.Add(new TremoloLayout(
                tremolo.MeasureIndex,
                tremolo.ItemIndex,
                x,
                y,
                tremolo.BeamCount,
                stemUp,
                tremolo.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>Gets the beam thickness.</summary>
    public static double GetBeamThickness() => BeamThickness;

    /// <summary>Gets the gap between beams.</summary>
    public static double GetBeamGap() => BeamGap;

    /// <summary>
    /// Gets the beam width based on flag presence.
    /// LILYPOND-REF: stem-tremolo.cc:81-94 calc-width
    /// </summary>
    public static double GetBeamWidth(bool hasFlag) => hasFlag ? FlagBeamWidth : NoFlagBeamWidth;

    /// <summary>
    /// Gets the beam slope based on stem direction and flag presence.
    /// LILYPOND-REF: stem-tremolo.cc:45-79 calc-slope
    /// </summary>
    public static double GetBeamSlope(bool stemUp, bool hasFlag) =>
        (!stemUp && hasFlag) ? FlagDownStemSlope : DefaultBeamSlope;
}