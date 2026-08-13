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

    private static void DrawBarlines(SystemLayout system, Staff staff, double staffY,
        ScoreLayout layout, IDrawingContext gc, double? barHeight = null,
        int fromMeasure = int.MinValue, int toMeasure = int.MaxValue)
    {
        // A lead-sheet text row has no staff, so its barlines are short ticks the
        // chord/lyric row hangs on; a real staff uses its full height.
        double height = barHeight ?? StaffHeight;
        var voice = staff.PrimaryVoice;
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

            // Start barline (e.g. repeat-start) at the measure's left edge. At a
            // line start the redrawn clef/key/time prefix sits immediately left of
            // this measure, so nudge the barline right by LilyPond's prefix→bar
            // extra-space (1.15 ss) — otherwise a `|:` opening a line overprints
            // the clef. LILYPOND-REF: scm/define-grobs.scm BarLine space-alist
            // (clef/key-signature/time-signature . (extra-space . 1.15)).
            if (measure.StartBarline != BarlineType.None)
            {
                double sx = atLineStart ? ml.X + LineStartBarClearance : ml.X;
                // The barline is clickable/highlightable in the preview: it carries
                // the measure boundary's source position (the written `|:` token's
                // spot). The transparent hit rect widens the thin ink to a
                // comfortable click target.
                using (gc.Source(measure.SourceStart))
                {
                    DrawBarline(measure.StartBarline, sx, staffY, height, gc);
                    gc.DrawHitRect(sx - BarlineHitPad, staffY,
                        GetVisualBarlineWidth(measure.StartBarline) + 2 * BarlineHitPad, height);
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

            if (measure.EndBarline == BarlineType.None)
                continue;

            double endX = ml.X + ml.Width;
            double width = GetVisualBarlineWidth(measure.EndBarline);
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
                DrawBarline(measure.EndBarline, endX - width, staffY, height, gc);
                gc.DrawHitRect(endX - width - BarlineHitPad, staffY,
                    width + 2 * BarlineHitPad, height);
            }
        }
    }

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
        IDrawingContext gc, bool withDots = true, (double Y1, double Y2)? tabDots = null)
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
                if (withDots) DrawRepeatDots(x + thick + sep + thin + dotSep, staffY, gc, tabDots);
                break;

            case BarlineType.RepeatEnd:
                if (withDots) DrawRepeatDots(x, staffY, gc, tabDots);
                double afterDots = x + dotsOffset;
                gc.DrawRectangle(afterDots, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(afterDots + thin + sep, staffY, thick, height, fill: Color.Black);
                break;

            case BarlineType.RepeatBoth:
                if (withDots) DrawRepeatDots(x, staffY, gc, tabDots);
                double pos = x + dotsOffset;
                gc.DrawRectangle(pos, staffY, thin, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep, staffY, thick, height, fill: Color.Black);
                gc.DrawRectangle(pos + thin + sep + thick + sep, staffY, thin, height, fill: Color.Black);
                if (withDots) DrawRepeatDots(pos + thin + sep + thick + sep + thin + dotSep, staffY, gc, tabDots);
                break;
        }
    }

    // LILYPOND-REF: scm/bar-line.scm make-colon-bar-line — two repeat dots centred on
    // the middle of the staff, one falling into each space adjacent to the centre line.
    private static void DrawRepeatDots(double x, double staffY, IDrawingContext gc,
        (double Y1, double Y2)? tabDots = null)
    {
        double r = EngravingDefaults.RepeatDotRadius;
        // On a tab staff the dots straddle the centre, each centred in a string
        // space (passed in); otherwise the notation 2nd/3rd-space positions.
        double y1 = tabDots?.Y1 ?? EngravingDefaults.RepeatDotPosition1;
        double y2 = tabDots?.Y2 ?? EngravingDefaults.RepeatDotPosition2;
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
