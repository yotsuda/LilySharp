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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Rendering;

internal static partial class SharedRenderer
{
    // ---------- Barlines ----------

    private static void DrawBarlines(MultiStaffScore score, SystemLayout system, Staff staff,
        double staffY, ScoreLayout layout, IDrawingContext gc, double? barHeight = null,
        int fromMeasure = int.MinValue, int toMeasure = int.MaxValue)
    {
        // A lead-sheet text row has no staff, so its barlines are short ticks the
        // chord/lyric row hangs on; a real staff uses its full height.
        double height = barHeight ?? StaffHeight;
        // …and it has no staff LINES either, which is what LilyPond's repeat-dot search
        // reads. Zero selects its no-staff-symbol default; a real staff hands over its own
        // count, so a one-line rhythm staff gets the search's answer rather than a five-line
        // staff's (EngravingDefaults.RepeatDotHalfSpan).
        int staffLines = staff.IsTextRow ? 0 : staff.Lines;
        var voice = staff.PrimaryVoice;
        // Where the bar line a system OPENS with stands, past the measure's X: the
        // staff-bar column of the line-start break-align group, which at a line start
        // comes AFTER the meter — the same table the first measure was spaced from, so the
        // stroke lands where the column was priced (MultiStaffLayouter.LineStartBarGap).
        // LILYPOND-REF: scm/define-grobs.scm:668-683 break-align-orders, begin of line.
        double lineStartBarGap = MultiStaffLayouter.LineStartBarGap(score, system);
        bool lineStart = true;
        foreach (var ml in system.Measures)
        {
            // Ossia fragment trim: no barlines where no staff exists.
            if (ml.MeasureIndex < fromMeasure || ml.MeasureIndex > toMeasure)
                continue;
            if (ml.MeasureIndex >= voice.Measures.Length)
                continue;
            var measure = voice.Measures[ml.MeasureIndex];
            bool atLineStart = lineStart;
            lineStart = false;

            // Start barline (e.g. repeat-start) at the measure's left edge — at a line
            // start, past the redrawn clef/key/time prefix by the column gap above.
            var startType = StartBarWithBreakPieces(measure, voice, ml.MeasureIndex, atLineStart);
            if (startType != BarlineType.None)
            {
                double sx = atLineStart ? ml.X + lineStartBarGap : ml.X;
                // The barline is clickable/highlightable in the preview: it carries
                // the measure boundary's source position (the written `|:` token's
                // spot). The transparent hit rect widens the thin ink to a
                // comfortable click target.
                using (gc.Source(measure.SourceStart))
                {
                    DrawBarline(startType, sx, staffY, height, gc, staffLines: staffLines);
                    gc.DrawHitRect(sx - BarlineHitPad, staffY,
                        GetVisualBarlineWidth(startType) + 2 * BarlineHitPad, height);
                }
            }

            // End barline drawn so its right edge sits on the column boundary
            // (matches SvgRenderer: endX - visualWidth). Normal measures carry
            // BarlineType.Single from the collector.
            //
            // Plain barlines INSIDE a multi-measure-rest run are suppressed —
            // the MMR symbol spans the whole run without internal barlines
            // (LILYPOND-REF: lily/multi-measure-rest.cc). Non-Single barlines
            // (double / final / repeat) keep their meaning and stay visible.
            if (measure.EndBarline == BarlineType.Single
                && IsMmrInnerEndBarline(layout, ml.MeasureIndex))
                continue;

            // A plain Single immediately before a repeat-start in the SAME system
            // yields to it -- the boundary is ONE bar line (see
            // EndBarYieldsToRepeatStart).
            if (measure.EndBarline == BarlineType.Single
                && EndBarYieldsToRepeatStart(voice, system, ml.MeasureIndex))
                continue;

            if (measure.EndBarline == BarlineType.None)
                continue;

            var endType = EndBarWithBreakPieces(measure, system, ml.MeasureIndex);
            double endX = ml.X + ml.Width;
            double width = GetVisualBarlineWidth(endType);
            // Clickable/highlightable like the start barline: SourceEnd is the
            // written `|` token's position (or, for an auto-filled close, the
            // point just after the bar's last note — still the boundary).
            // data-pos = SourceEnd (the click target: the outermost/section bar), data-alt
            // = the phrase bars that also collapse here — so a click jumps to the section
            // bar while a caret on any contributing bar highlights the whole glyph.
            using (measure.EndHighlightAliases.IsDefaultOrEmpty
                ? gc.Source(measure.SourceEnd)
                : gc.Source(measure.SourceEnd, measure.EndHighlightAliases))
            {
                DrawBarline(endType, endX - width, staffY, height, gc, staffLines: staffLines);
                gc.DrawHitRect(endX - width - BarlineHitPad, staffY,
                    width + 2 * BarlineHitPad, height);
            }
        }
    }

    /// <summary>
    /// True when a measure's END bar must not print: a plain Single immediately
    /// before a repeat-start in the SAME system. LilyPond's start repeat is ONE bar
    /// line at the boundary (".|:" = thick + thin + dots) — MEASURED on 2.26.0
    /// (scratch/p226/lprep.ly, c1 \repeat volta 2 { c1 c1 } c1: the SVG carries
    /// exactly 4 thin + 2 thick rects; no leading thin exists before the ".|:") —
    /// where Lily#'s split model (End piece + Start piece) printed the previous
    /// bar's Single too, fusing thin against thick into one over-heavy blob (user
    /// report 2026-08-20, first seen on the lead-sheet grid). At a LINE BREAK the
    /// two pieces separate exactly as LilyPond's break pieces do — the end-of-line
    /// piece of ".|:" is the plain thin bar — so a Single ENDING the system before
    /// a next-system repeat-start keeps printing, which is why the test is "the
    /// next measure is rendered in THIS system", not merely "starts a repeat".
    /// LILYPOND-REF: scm/bar-line.scm define-bar-line ".|:" — begin-of-line piece
    /// ".|:", end-of-line piece "|".
    /// </summary>
    private static bool EndBarYieldsToRepeatStart(Voice voice, SystemLayout system, int measureIndex)
    {
        if (measureIndex + 1 >= voice.Measures.Length)
            return false;
        // The system's measures are contiguous, so "in this system" is one compare
        // against its last index; the system's own last measure ends at a break.
        if (system.Measures.Length == 0 || measureIndex >= system.Measures[^1].MeasureIndex)
            return false;
        return voice.Measures[measureIndex + 1].StartBarline == BarlineType.RepeatStart;
    }

    /// <summary>
    /// The end barline TYPE a measure prints at its position in the system — the
    /// break-piece substitution: a combined repeat (RepeatBoth) ENDING a system
    /// prints only its end-of-line piece, the repeat-END, and its begin piece
    /// moves to the next system's start (<see cref="StartBarWithBreakPieces"/>).
    /// Mid-line the combined glyph prints whole.
    /// LILYPOND-REF: scm/bar-line.scm define-bar-line ":|.|:" / ":|.:" — end-of-line
    /// piece ":|.", begin-of-line piece ".|:" (user report 2026-08-20: the whole
    /// combined glyph used to print at the line end and nothing opened the next line).
    /// </summary>
    private static BarlineType EndBarWithBreakPieces(
        Measure measure, SystemLayout system, int measureIndex)
        => measure.EndBarline == BarlineType.RepeatBoth
           && system.Measures.Length > 0
           && measureIndex >= system.Measures[^1].MeasureIndex
            ? BarlineType.RepeatEnd
            : measure.EndBarline;

    /// <summary>
    /// The start barline TYPE a measure prints — the other half of
    /// <see cref="EndBarWithBreakPieces"/>: a measure OPENING a system whose
    /// predecessor carries the combined repeat prints the begin-of-line piece, the
    /// repeat-START (the collector folded the pair into the predecessor's end, so
    /// this measure's own record says None).
    /// </summary>
    /// <remarks>The line-start rule itself lives in
    /// <see cref="MultiStaffLayouter.DrawnLineStartBarline"/>, because the layout prices
    /// that bar line's column and must agree with the pen about which type stands there.</remarks>
    private static BarlineType StartBarWithBreakPieces(
        Measure measure, Voice voice, int measureIndex, bool atLineStart)
        => atLineStart
            ? MultiStaffLayouter.DrawnLineStartBarline(voice, measureIndex)
            : measure.StartBarline;

    /// <summary>Extra clickable margin on each side of a barline's ink (staff
    /// spaces) — the interactive hit rect only; the drawn ink is unchanged.</summary>
    private const double BarlineHitPad = 0.4;

    /// <summary>True iff the measure lies inside a multi-measure-rest run.</summary>
    private static bool IsMmrCovered(ScoreLayout layout, int measureIndex)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return false;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (measureIndex >= mmr.StartMeasureIndex &&
                measureIndex < mmr.StartMeasureIndex + mmr.MeasureCount)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True iff the measure's END barline is internal to a multi-measure-rest
    /// run (i.e. the run continues into the next measure).
    /// </summary>
    private static bool IsMmrInnerEndBarline(ScoreLayout layout, int measureIndex)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return false;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (measureIndex >= mmr.StartMeasureIndex &&
                measureIndex < mmr.StartMeasureIndex + mmr.MeasureCount - 1)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Draws a barline of the given type. Mirrors <c>SvgRenderer.DrawBarline</c>.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm — bar-line glyph composition.</remarks>
    private static void DrawBarline(BarlineType type, double x, double staffY, double height,
        IDrawingContext gc, bool withDots = true, (double Y1, double Y2)? tabDots = null,
        int staffLines = 5)
    {
        if (type == BarlineType.None) return;

        double thin = EngravingDefaults.ThinBarlineThickness;
        double thick = EngravingDefaults.ThickBarlineThickness;
        double sep = EngravingDefaults.BarlineSeparation;
        double dotSep = EngravingDefaults.RepeatBarlineDotSeparation;
        double dotsOffset = EngravingDefaults.RepeatDotsOffset;

        switch (type)
        {
            case BarlineType.Single:
                gc.DrawRectangle(x, staffY, thin, height, fill: Color.Black);
                break;

            case BarlineType.Double:
                gc.DrawRectangle(x, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(x + thin + sep, staffY, thin, height, fill: Color.Black);
                break;

            case BarlineType.Dashed:
            {
                // LILYPOND-REF: scm/bar-line.scm (dashed bar glyph) — dash
                // length tuned so segments straddle the staff lines evenly
                // (~⅔ dash, ⅓ gap per staff space).
                const double dash = 0.67, gap = 0.33;
                for (double dy = 0; dy < height; dy += dash + gap)
                    gc.DrawRectangle(x, staffY - dy, thin,
                        Math.Min(dash, height - dy), fill: Color.Black);
                break;
            }

            case BarlineType.Final:
                gc.DrawRectangle(x, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(x + thin + sep, staffY, thick, height, fill: Color.Black);
                break;

            case BarlineType.RepeatStart:
                gc.DrawRectangle(x, staffY, thick, height, fill: Color.Black);
                gc.DrawRectangle(x + thick + sep, staffY, thin, height, fill: Color.Black);
                if (withDots) DrawRepeatDots(x + thick + sep + thin + dotSep, staffY, height, gc, tabDots, staffLines);
                break;

            case BarlineType.RepeatEnd:
                if (withDots) DrawRepeatDots(x, staffY, height, gc, tabDots, staffLines);
                double afterDots = x + dotsOffset;
                gc.DrawRectangle(afterDots, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(afterDots + thin + sep, staffY, thick, height, fill: Color.Black);
                break;

            case BarlineType.RepeatBoth:
                if (withDots) DrawRepeatDots(x, staffY, height, gc, tabDots, staffLines);
                double pos = x + dotsOffset;
                gc.DrawRectangle(pos, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep, staffY, thick, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep + thick + sep, staffY, thin, height, fill: Color.Black);
                if (withDots) DrawRepeatDots(pos + thin + sep + thick + sep + thin + dotSep, staffY, height, gc, tabDots, staffLines);
                break;
        }
    }

    // LILYPOND-REF: scm/bar-line.scm:360-368 make-colon-bar-line — the two dots are
    // translated to `center ± dist/2`, i.e. they straddle the CENTRE of the band the
    // barline spans; nothing in that procedure refers to its top edge.
    // ⚠️ `height` is what makes that true here. The default pair used to be two stored
    // constants measured DOWN FROM THE TOP LINE (1.5 / 2.5), which is the same answer only
    // while the band is a five-line staff. A lead-sheet row grows by one LyricVerseSpacing
    // per extra verse, so on a two-verse row the dots sat 1.6 ss above the row's centre
    // while the barline around them had already grown past them (user report, session 240).
    private static void DrawRepeatDots(double x, double staffY, double height,
        IDrawingContext gc, (double Y1, double Y2)? tabDots = null, int staffLines = 5)
    {
        double r = EngravingDefaults.RepeatDotRadius;
        // How far each dot sits from the band's centre is LilyPond's search over the
        // staff's own line positions, not one number: a one-line rhythm staff wants 0.45
        // and a four-line one 1.0 where five lines want 0.5 (EngravingDefaults).
        double half = EngravingDefaults.RepeatDotHalfSpan(staffLines);
        // On a tab staff the dots straddle the centre, each centred in a string
        // space (passed in, already in this frame); otherwise they straddle the band's own
        // centre — which for a five-line staff is 2.0 below its top line, giving back the
        // 1.5 / 2.5 this used to store.
        double y1 = tabDots?.Y1 ?? (height / 2.0 - half);
        double y2 = tabDots?.Y2 ?? (height / 2.0 + half);
        // staffY is the Y-up top edge; the dot rows sit below it (device down =
        // smaller Y-up).
        gc.DrawCircle(x + r, staffY - y1, r, Color.Black);
        gc.DrawCircle(x + r, staffY - y2, r, Color.Black);
    }

    /// <summary>Total horizontal extent of a barline glyph (for right-edge alignment).</summary>
    // The drawn extent and the reserved spacing width are the same quantity;
    // both come from EngravingDefaults.BarlineDrawnWidth so they cannot drift.
    // internal: SkylineBuilder's key-change seed mirrors the opening-change anchor
    // (EnumerateStaffItems), which starts past the measure's start barline.
    internal static double GetVisualBarlineWidth(BarlineType type)
        => EngravingDefaults.BarlineDrawnWidth(type);

}
