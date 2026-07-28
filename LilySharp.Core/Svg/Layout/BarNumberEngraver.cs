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
    // Bar number text (typically a 1-based integer).
    string Text,
    // X coordinate of the text anchor.
    double X,
    // Y of the text baseline in the Y-up frame (frame B): staff-spaces ABOVE the
    // system top, up-positive. The renderer reflects it to device
    // (system top − Y-up) against the measure's system top.
    double YUp,
    // When true the text right-aligns to X (TextAnchor.End).
    // Line-start and mid-line bar numbers LEFT-align (false) per BarNumber's
    // self-alignment-X = LEFT, so the number sits above the staff start and
    // extends rightward, clear of the system-start brace.
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
internal static class BarNumberEngraver
{
    /// <summary>
    /// Bar number text height: normal text is 11pt at a 20pt staff
    /// (= 2.2 staff spaces) and BarNumber uses font-size -2, i.e.
    /// magstep(-2) = 2^(-2/6).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size . -2)
    /// LILYPOND-REF: scm/paper.scm:69-77 sets <c>text-font-size</c> to <c>11 * (staff-height / 20pt)</c> and <c>output-scale</c> to <c>staff-height / 4</c>, so 11pt against a 5pt staff space = 2.2 ss.
    /// LILYPOND-REF: scm/lily-library.scm <c>magstep</c> = <c>exp((s/6) * log 2)</c>.
    /// ⚠️ THE SECOND ADDRESS SAID <c>ly/paper-defaults-init.ly</c> UNTIL 2026-07-28 and that
    /// file does not mention text-font-size at all. The value was right; the citation was
    /// never read (HANDOFF 5.2.1①). It is corrected because two later constants —
    /// <see cref="EngravingDefaults.LyricTextFontSize"/> and
    /// <see cref="EngravingDefaults.ChordNameFontSize"/> — were derived by copying it.
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
        bool numberFirstMeasure = false,
        int numberOffset = 0)
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

                // LP shows 1-based numbers. measureIndex is 0-based. A leading
                // \partial pickup shifts everything down by one (numberOffset = -1)
                // so the pickup is bar 0 and the first full measure is bar 1.
                int displayedNumber = measureIndex + 1 + numberOffset;

                // Line-start numbers break-align to the LEFT EDGE — the staff-line
                // origin, BEFORE the clef, as LilyPond's own comment on
                // break-align-symbols says — and at a line start they align their
                // RIGHT edge to it, so the number hangs into the left margin and
                // the clef is never underneath it.
                //
                // LILYPOND-REF: scm/define-grobs.scm:323 BarNumber
                //   break-align-symbols = (left-edge staff-bar), and :334
                //   self-alignment-X = (break-alignment-list LEFT LEFT RIGHT).
                // ⚠️ THAT TRIPLE IS (end-of-line middle begin-of-line) —
                // scm/output-lib.scm:506 names the three arguments in that order — so
                // at a LINE START it is RIGHT, and only a mid-line number is LEFT.
                // This code read the triple the other way round for as long as it
                // existed, put the number over the clef, and the above-staff stacker
                // then lifted it clear: MEASURED at 4.260000 above the staff refpoint
                // against LilyPond's 3.074440 (audit/lp-geometry,
                // barnumber.{low,high}-melody.staff-to-baseline). That excess is not
                // cosmetic — a bar number is inside its staff's skyline, so it IS the
                // ink the system reserves above its own reference point, which floors
                // the system-to-system spring and closes the previous system's
                // loose-line chain (page-layout-problem.cc:625-629, :931-932).
                //
                // MEASURED, LilyPond 2.26.0 on a continuation system (probe
                // page-vertical.ly, book BNL): the number spans X (-0.956013 .. 0.0)
                // and the clef (0.800000 .. 3.365000). Disjoint, by 0.8.
                //
                // ⚠️ horizon-padding 0.05 is a SKYLINE padding, not an X shift; the
                // 0.05 that used to be added here had no counterpart in LilyPond.
                bool atLineStart = isFirstInSystem;
                // The system's left edge is where the staff lines start:
                // the indent (margins live in the page transform). ml.X is
                // the prefix END, and PrefixWidth is not reliable per-system
                // here, so anchor on the staff-line origin directly.
                double x = atLineStart ? system.Indent : ml.X;

                // The number's INK BOTTOM sits padding 1.0 above the staff's own
                // up-skyline, and that skyline is the top staff LINE plus half its
                // thickness — not the line's centre. Written as the derivation rather
                // than as 1.05 so it follows the staff symbol if that ever changes
                // (HANDOFF 5.2.1⑤).
                // Collisions with protruding staff content and other outside-staff
                // grobs are resolved afterwards by OutsideStaffStacker.StackAboveStaff.
                // LILYPOND-REF: scm/define-grobs.scm:333 BarNumber padding = 1.0;
                // lily/side-position-interface.cc y_aligned_side.
                //
                // ...and the BASELINE is that ink bottom plus the digits' OWN overshoot
                // below it, which is why this reads the face rather than assuming zero.
                // It said "Lily# has no measured bottom overshoot for its digits" until
                // 2026-07-28 and that had stopped being true: TextFontMetrics.Ink measures
                // the drawn path. MEASURED, and it is PER STRING, which is the shape
                // LilyPond's own dump has: a round digit overshoots by 0.024446 and a "1"
                // by nothing, against LilyPond's 3.074440 for "6" and 3.076208 for another
                // digit over the staff refpoint (probe page-vertical.ly, books BNL/BNH).
                // A constant here would be right for one numeral and wrong for the next.
                const double padding = 1.0;
                string text = displayedNumber.ToString();
                double overshoot = -Rendering.TextFontMetrics.Ink(
                    text, FontSize, sans: false, Rendering.FontStyle.Bold).Bottom;
                double yUp = EngravingDefaults.StaffLineThickness / 2 + padding + overshoot;

                builder.Add(new BarNumberLayout(
                    MeasureIndex: measureIndex,
                    Text: text,
                    X: x,
                    YUp: yUp,
                    RightAligned: atLineStart));
            }
        }

        return builder.ToImmutable();
    }
}
