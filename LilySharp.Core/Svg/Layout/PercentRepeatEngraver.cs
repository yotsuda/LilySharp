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
/// Layout result for a percent repeat symbol.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - PercentRepeat grob
/// </remarks>
public readonly record struct PercentRepeatLayout(
    int MeasureIndex,
    double X,                // X center of the percent symbol (staff spaces)
    double Y,                // Y center of the percent symbol (staff spaces from system top)
    double Width,            // Measure width for proportional sizing
    int SourcePosition,
    int SourceIndex = -1     // F3/B: index into score.PercentRepeats (data-pos resolved at render)
);

/// <summary>
/// Calculates layout positions for percent repeat symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-interface.cc - x_percent() rendering
/// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - self-alignment-X = CENTER
///
/// The percent symbol is centered horizontally and vertically within the measure.
/// </remarks>
public static class PercentRepeatEngraver
{
    /// <summary>
    /// Calculates percent repeat layouts from collected items.
    /// </summary>
    public static ImmutableArray<PercentRepeatLayout> Calculate(
        ImmutableArray<PercentRepeatItem> percentRepeats,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Func<int, int, double>? staffYAt = null)
    {
        if (percentRepeats.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<PercentRepeatLayout>.Empty;

        // The sign belongs to the staff the repeat was WRITTEN on; a cello
        // percent must not print its ％ over the flute.
        // LILYPOND-REF: lily/percent-repeat-engraver.cc — the engraver lives
        // in the Voice context of its own staff.
        var measureToSystem = LayoutUtilities.BuildMeasureMap(systems);

        var results = ImmutableArray.CreateBuilder<PercentRepeatLayout>(percentRepeats.Length);

        for (int i = 0; i < percentRepeats.Length; i++)
        {
            var item = percentRepeats[i];
            if (item.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[item.MeasureIndex];

            // Center of the measure
            double x = ml.X + ml.Width / 2;

            // Y = middle line of the OWN staff (2 ss below its top line).
            double staffOffset = 0;
            if (staffYAt != null
                && measureToSystem.TryGetValue(item.MeasureIndex, out var info))
            {
                staffOffset = staffYAt(info.System.SystemIndex, item.StaffIndex);
            }
            double y = staffOffset + 2.0;

            results.Add(new PercentRepeatLayout(
                item.MeasureIndex, x, y, ml.Width, item.SourcePosition, i));
        }

        return results.ToImmutable();
    }
}
