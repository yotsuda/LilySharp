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
/// Layout information for a custom text annotation.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: text-interface.cc Text rendering
/// LILYPOND-REF: define-grobs.scm:3800-3833 TextScript grob
/// </remarks>
public readonly record struct CustomTextLayout(
    int MeasureIndex,       // Measure containing this text
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    string Text,            // Display text
    int SourcePosition,     // For click-to-source mapping
    int SourceIndex = -1,   // F3/B: index into score.CustomTexts (data-pos resolved at render)
    int StaffIndex = -1      // owning staff (-1 = top staff); the draw resolves its middle
);

/// <summary>
/// Calculates positions for custom text annotations.
/// </summary>
/// <remarks>
/// LILYPOND-REF: text-interface.cc:36-89 Text positioning
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// Custom text is placed at the end of measures, typically below the staff
/// for expression indications like "molto rit.", "a tempo", etc.
/// </remarks>
internal static class CustomTextEngraver
{
    // LILYPOND-REF: define-grobs.scm:3925 padding = 0.5
    private const double Padding = 0.5;

    // Y offset below staff for custom text
    private const double BelowStaffOffset = 5.5;

    /// <summary>
    /// Calculates layout for all custom text items in a score.
    /// </summary>
    public static ImmutableArray<CustomTextLayout> Calculate(
        ImmutableArray<CustomTextItem> customTexts,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (customTexts.IsDefaultOrEmpty)
            return ImmutableArray<CustomTextLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<CustomTextLayout>(customTexts.Length);

        for (int ci = 0; ci < customTexts.Length; ci++)
        {
            var customText = customTexts[ci];
            // Find the measure layout
            if (customText.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[customText.MeasureIndex];

            // Calculate X position (at end of measure)
            // LILYPOND-REF: text-interface.cc:75-80 self-alignment-X
            double x = measureLayout.X + measureLayout.Width - 1.0;

            // Calculate Y position (below staff)
            double y = BelowStaffOffset + Padding;

            layouts.Add(new CustomTextLayout(
                customText.MeasureIndex,
                x,
                y,
                customText.Text,
                customText.SourcePosition,
                ci
            ));
        }

        return layouts.ToImmutable();
    }
}