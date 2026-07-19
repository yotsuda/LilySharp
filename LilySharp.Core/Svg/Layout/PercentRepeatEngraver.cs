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
/// LILYPOND-REF: scm/define-grobs.scm:2788-2807 - PercentRepeat grob
/// </remarks>
public readonly record struct PercentRepeatLayout(
    int MeasureIndex,
    double X,                // X center of the percent symbol (staff spaces)
    double YUp,              // Y-up (frame B): staff-spaces above the staff middle (0 = middle)
    double Width,            // Measure width for proportional sizing
    int SourcePosition,
    int SourceIndex = -1,    // F3/B: index into score.PercentRepeats (data-pos resolved at render)
    int StaffIndex = -1       // owning staff, so the draw can resolve its staff middle
);

/// <summary>
/// Calculates layout positions for percent repeat symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-interface.cc - x_percent() rendering
/// LILYPOND-REF: scm/define-grobs.scm:2788-2807 - self-alignment-X = CENTER
///
/// The percent symbol is centered horizontally and vertically within the measure.
/// </remarks>
internal static class PercentRepeatEngraver
{
    /// <summary>
    /// Calculates percent repeat layouts from collected items.
    /// </summary>
    public static ImmutableArray<PercentRepeatLayout> Calculate(
        ImmutableArray<PercentRepeatItem> percentRepeats,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (percentRepeats.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<PercentRepeatLayout>.Empty;

        // The sign belongs to the staff the repeat was WRITTEN on (StaffIndex); a
        // cello percent must not print its ％ over the flute. Its own staff middle
        // is resolved at draw time.
        // LILYPOND-REF: lily/percent-repeat-engraver.cc — the engraver lives
        // in the Voice context of its own staff.
        var results = ImmutableArray.CreateBuilder<PercentRepeatLayout>(percentRepeats.Length);

        for (int i = 0; i < percentRepeats.Length; i++)
        {
            var item = percentRepeats[i];
            if (item.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[item.MeasureIndex];

            // Center of the measure
            double x = ml.X + ml.Width / 2;

            // Y-up (frame B): the percent sign is centred on the OWN staff's middle
            // line = 0 staff-spaces above the middle. The staff (and thus its device
            // middle) is resolved at draw time from StaffIndex.
            results.Add(new PercentRepeatLayout(
                item.MeasureIndex, x, 0.0, ml.Width, item.SourcePosition, i,
                StaffIndex: item.StaffIndex));
        }

        return results.ToImmutable();
    }
}
