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
/// Layout for a measure number printed above the staff at a system start
/// (or at a fixed period — see <see cref="BarNumberEngraver"/>).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/bar-number-engraver.cc — BarNumber grob
/// LILYPOND-REF: scm/define-grobs.scm BarNumber — outside-staff-priority = 100
/// </remarks>
public readonly record struct BarNumberLayout(
    int MeasureIndex,
    /// <summary>Bar number text (typically a 1-based integer).</summary>
    string Text,
    /// <summary>X coordinate of the text anchor (start of the measure).</summary>
    double X,
    /// <summary>Y coordinate of the text baseline (above the staff).</summary>
    double Y);

/// <summary>
/// Calculates BarNumber positions for each system. By default, the first
/// measure of every system after the first gets a bar number. Optionally
/// every Nth measure can be numbered too via the period parameter.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/bar-number-engraver.cc Bar_number_engraver
/// LILYPOND-REF: scm/translation-functions.scm — barNumberFormatter default
/// LILYPOND-REF: scm/define-grobs.scm BarNumber:
///   self-alignment-X = LEFT, padding = 1.0, font-size = -2 (small)
/// </remarks>
public static class BarNumberEngraver
{
    /// <summary>
    /// Calculates bar number layouts. When <paramref name="period"/> is greater
    /// than 1, also numbers every Nth measure within a system; default 0 means
    /// system starts only. <paramref name="numberFirstMeasure"/> set to false (LP
    /// default) suppresses the score's very first measure number.
    /// </summary>
    public static ImmutableArray<BarNumberLayout> Calculate(
        ImmutableArray<SystemLayout> systems,
        int period = 0,
        bool numberFirstMeasure = false)
    {
        if (systems.IsDefaultOrEmpty)
            return ImmutableArray<BarNumberLayout>.Empty;

        var builder = ImmutableArray.CreateBuilder<BarNumberLayout>();

        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
        {
            var system = systems[sysIdx];
            if (system.Measures.IsDefaultOrEmpty)
                continue;

            // First measure of every system after the first is always numbered.
            // LILYPOND-REF: scm/translation-functions.scm — barNumberVisibility default
            // (first-bar-number-invisible-and-no-parenthesized-bar-numbers).
            for (int i = 0; i < system.Measures.Length; i++)
            {
                var ml = system.Measures[i];
                int measureIndex = ml.MeasureIndex;
                bool isFirstSystem = sysIdx == 0;
                bool isFirstInSystem = i == 0;
                bool isFirstOfScore = measureIndex == 0 || ml.MeasureIndex == 0;

                bool show =
                    (isFirstInSystem && !isFirstSystem) ||
                    (isFirstOfScore && numberFirstMeasure) ||
                    (period > 0 && measureIndex > 0 && (measureIndex % period == 0));

                if (!show)
                    continue;

                // LP shows 1-based numbers. measureIndex is 0-based.
                int displayedNumber = measureIndex + 1;
                double x = ml.X;
                // Sit above the staff: a couple staff spaces above system top.
                double y = system.Y - 1.0;

                builder.Add(new BarNumberLayout(
                    MeasureIndex: measureIndex,
                    Text: displayedNumber.ToString(),
                    X: x,
                    Y: y));
            }
        }

        return builder.ToImmutable();
    }
}
