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
    /// <summary>X coordinate of the text anchor.</summary>
    double X,
    /// <summary>Y coordinate of the text baseline (above the staff).</summary>
    double Y,
    /// <summary>
    /// True for line-start numbers: the anchor is the system's LEFT EDGE
    /// (before the clef) and the text right-aligns to it, per BarNumber's
    /// begin-of-line alignment.
    /// </summary>
    bool RightAligned = false);

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
    /// Bar number text height: normal text is 11pt at a 20pt staff
    /// (= 2.2 staff spaces) and BarNumber uses font-size -2, i.e.
    /// magstep(-2) = 2^(-2/6).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size . -2)
    /// LILYPOND-REF: ly/paper-defaults-init.ly — text-font-size 11 @ 20pt staff
    /// </remarks>
    public static readonly double FontSize = 2.2 * Math.Pow(2, -2.0 / 6.0);

    /// <summary>
    /// Calculates bar number layouts. When <paramref name="period"/> is greater
    /// than 1, also numbers every Nth measure within a system; default 0 means
    /// system starts only. <paramref name="numberFirstMeasure"/> set to false (LP
    /// default) suppresses the score's very first measure number.
    /// Collision handling lives in OutsideStaffStacker.StackAboveStaff.
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

                // Line-start numbers break-align to the LEFT EDGE — the very
                // start of the staff lines, BEFORE the clef — and right-align
                // to it (plus a small horizon padding), so the number hangs
                // just left of the system. Mid-line (period) numbers keep the
                // measure-start anchor, left-aligned (staff-bar alignment).
                // LILYPOND-REF: scm/define-grobs.scm BarNumber —
                //   break-align-symbols (left-edge staff-bar),
                //   self-alignment-X (break-alignment-list LEFT LEFT RIGHT),
                //   horizon-padding 0.05
                bool atLineStart = isFirstInSystem;
                // The system's left edge is where the staff lines start:
                // the indent (margins live in the page transform). ml.X is
                // the prefix END, and PrefixWidth is not reliable per-system
                // here, so anchor on the staff-line origin directly.
                double x = atLineStart
                    ? system.Indent + 0.05
                    : ml.X;

                // Baseline = digit bottom, padding 1.0sp above the staff
                // top. Collisions with protruding staff content and other
                // outside-staff grobs are resolved afterwards by the unified
                // OutsideStaffStacker.StackAboveStaff pass.
                // LILYPOND-REF: scm/define-grobs.scm BarNumber — padding 1.0.
                double y = system.Y - 1.0;

                builder.Add(new BarNumberLayout(
                    MeasureIndex: measureIndex,
                    Text: displayedNumber.ToString(),
                    X: x,
                    Y: y,
                    RightAligned: atLineStart));
            }
        }

        return builder.ToImmutable();
    }
}
