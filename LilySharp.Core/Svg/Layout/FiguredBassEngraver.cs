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
/// Layout information for a figured bass group.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc:200-350 print method
/// LILYPOND-REF: scm/define-grobs.scm:362-380 BassFigure defaults
/// </remarks>
public readonly record struct FiguredBassLayout(
    int MeasureIndex,
    double X,                                         // X position (staff spaces)
    double Y,                                         // Y position of topmost figure (staff spaces)
    ImmutableArray<string> FigureTexts,               // Text for each figure, top to bottom
    int SourcePosition
);

/// <summary>
/// Calculates positions for figured bass figures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - Figured_bass_engraver
/// LILYPOND-REF: lily/figured-bass-position-engraver.cc - positioning
/// LILYPOND-REF: scm/define-grobs.scm:362-380 BassFigure defaults
///
/// Figured bass appears below the staff, with figures stacked vertically.
/// Each figure occupies 1.6 staff spaces of vertical height (baseline-skip).
/// </remarks>
public static class FiguredBassEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:362 BassFigure defaults
    private const double StaffPadding = 1.0;   // Padding below staff bottom
    // LILYPOND-REF: scm/define-grobs.scm:369 (baseline-skip . 1.6)
    private const double FigureSpacing = 1.6;  // Vertical spacing between stacked figures
    private const double BelowStaffY = 5.0;    // Y offset below staff (staff bottom = 4.0)

    /// <summary>
    /// Calculates layout for all figured bass items.
    /// </summary>
    public static ImmutableArray<FiguredBassLayout> Calculate(
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, double>? staffYByIndex = null)
    {
        if (figuredBasses.IsDefaultOrEmpty)
            return ImmutableArray<FiguredBassLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<FiguredBassLayout>(figuredBasses.Length);

        foreach (var fb in figuredBasses)
        {
            if (fb.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[fb.MeasureIndex];

            if (measureLayout.Columns.IsDefaultOrEmpty
                && fb.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this figure's OWN staff (multi-staff): its measures (X) and
            // the staff's vertical offset, so it sits below its own staff.
            var fbMeasures = measuresByStaff != null
                && measuresByStaff.TryGetValue(fb.StaffIndex, out var mm) ? mm : measures;
            double staffOffset = staffYByIndex != null
                && staffYByIndex.TryGetValue(fb.StaffIndex, out var so) ? so : 0;

            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                fbMeasures, fb.MeasureIndex, fb.ItemIndex, measureLayout);
            double y = BelowStaffY + StaffPadding + staffOffset;

            var figureTexts = fb.Figures
                .Select(f => f.DisplayText)
                .ToImmutableArray();

            layouts.Add(new FiguredBassLayout(
                fb.MeasureIndex,
                x,
                y,
                figureTexts,
                fb.SourcePosition));
        }

        return layouts.ToImmutable();
    }
}
