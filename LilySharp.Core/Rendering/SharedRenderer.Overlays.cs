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

/// <summary>
/// Overlay engravers for <see cref="SharedRenderer"/>: the above/below-staff
/// annotations and spanners (dynamics, articulations, lyrics, hairpins, ottava/
/// volta/tuplet brackets, trill spanners, glissandos, arpeggios). Split out of
/// the main SharedRenderer file as a partial class; no behavior change.
/// </summary>
internal static partial class SharedRenderer
{
    // ---------- Dynamics ----------

    /// <summary>
    /// Draws dynamic markings ("p", "f", "mf", etc.) below the staff using
    /// serif bold-italic text (matching LP's DynamicText grob font).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1433 DynamicText grob
    /// LILYPOND-REF: scm/define-grobs.scm:1444 self-alignment-X = CENTER
    /// </remarks>
    private static void DrawDynamics(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.DynamicLayouts.IsDefaultOrEmpty) return;
        double fontSize = FontSize * 0.5;
        foreach (var d in layout.DynamicLayouts)
        {
            string text = NormalizeDynamicText(d.Text);
            if (!sysY.TryGetValue(d.MeasureIndex, out var sy)) continue; // other page
            // A dynamic on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            double y = os.Y(sy + d.Y, d.StaffIndex, d.MeasureIndex);
            double size = os.Size(fontSize, d.StaffIndex);
            // Free expressive text (@text) prints plain italic; dynamic levels
            // keep LP's bold-italic DynamicText face.
            var style = d.IsExpressiveText ? FontStyle.Italic : FontStyle.BoldItalic;
            using (gc.Source(d.SourcePosition))
                gc.DrawText(text, d.X, y, size, "serif",
                    style, TextAnchor.Middle, Color.Black);
        }
    }

    private static string NormalizeDynamicText(string raw) => raw switch
    {
        "cresc" => "cresc.",
        "decresc" => "decresc.",
        "dim" => "dim.",
        _ => raw,
    };

    // ---------- Articulations ----------

    /// <summary>
    /// Draws articulation marks (staccato, accent, tenuto, fermata, etc.)
    /// using their precomputed Emmentaler glyphs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2992 Script grob
    /// LILYPOND-REF: lily/script-engraver.cc:92-125 acknowledge_note_head
    /// </remarks>
    private static void DrawArticulations(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArticulationLayouts)
        {
            if (string.IsNullOrEmpty(a.Glyph)) continue;
            if (!sysY.TryGetValue(a.MeasureIndex, out var sy)) continue; // other page
            // A script on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            double y = os.Y(sy + a.Y, a.StaffIndex, a.MeasureIndex);
            double scale = os.Size(a.Scale, a.StaffIndex);
            // Bend sentinels ("bendFall"/"bendDoit"): a trailing curve, not a glyph.
            if (a.Glyph is "bendFall" or "bendDoit")
            {
                using (gc.Source(a.SourcePosition))
                    DrawBendAfter(a.X, y, fall: a.Glyph == "bendFall", gc);
                continue;
            }
            // Approach curves INTO the note from the left: scoop rises, plop falls.
            if (a.Glyph is "bendScoop" or "bendPlop")
            {
                using (gc.Source(a.SourcePosition))
                    DrawBendBefore(a.X, y, rise: a.Glyph == "bendScoop", gc);
                continue;
            }
            // Guitar bend-up sentinel ("bendUp:N", N = semitones): rising arrow
            // with the bend amount labelled above the arrowhead.
            if (a.Glyph.StartsWith("bendUp:", StringComparison.Ordinal))
            {
                int semis = int.TryParse(a.Glyph.AsSpan(7), out int bs) ? bs : 2;
                using (gc.Source(a.SourcePosition))
                    DrawGuitarBend(a.X, y, semis, gc);
                continue;
            }
            // TAB technique letters (H / P / T): small italic serif text.
            if (a.Glyph.StartsWith("tabtech:", StringComparison.Ordinal))
            {
                using (gc.Source(a.SourcePosition))
                    gc.DrawText(a.Glyph[8..], a.X, y, 1.5, "serif",
                        FontStyle.Italic, TextAnchor.Middle, Color.Black);
                continue;
            }
            // Chord diagram sentinel ("frame:x32010").
            if (a.Glyph.StartsWith("frame:", StringComparison.Ordinal))
            {
                using (gc.Source(a.SourcePosition))
                    DrawFretFrame(a.X, y, a.Glyph[6..], gc);
                continue;
            }
            // Bartók (snap) pizzicato: a circle with a stem rising from its
            // centre. LILYPOND-REF: scripts.snappizzicato.
            if (a.Glyph == "snappizz")
            {
                using (gc.Source(a.SourcePosition))
                {
                    // Ring = black disc + white core (no stroked-circle API).
                    gc.DrawCircle(a.X, y, 0.45, Color.Black);
                    gc.DrawCircle(a.X, y, 0.33, Color.White);
                    gc.DrawLine(a.X, y - 0.45, a.X, y - 1.4, Color.Black, 0.14);
                }
                continue;
            }
            using (gc.Source(a.SourcePosition))
                gc.DrawGlyph(a.Glyph[0], a.X, y, FontSize * scale);
        }
    }

    /// <summary>
    /// Draws a dead-note "×" notehead (two crossing strokes) sized like a normal
    /// black head, anchored at the head's left edge / vertical centre.
    /// LILYPOND-REF: cross notehead style for \deadNote.
    /// </summary>
    private static void DrawDeadNotehead(double x, double noteY, Color? color, IDrawingContext gc)
    {
        double w = EngravingDefaults.NoteheadBlackWidth;
        const double h = 0.55;                 // half-height of the cross
        double t = EngravingDefaults.StemThickness * 1.4;
        var c = color ?? Color.Black;
        gc.DrawLine(x, noteY - h, x + w, noteY + h, c, t, cap: LineCap.Round);
        gc.DrawLine(x, noteY + h, x + w, noteY - h, c, t, cap: LineCap.Round);
    }

    /// <summary>
    /// Draws a jazz "fall" (drops away) or "doit" (rises away) — a short curved
    /// line trailing off to the right of a note, approximated by a polyline along
    /// a quadratic Bézier. LILYPOND-REF: lily/bend-after.cc BendAfter (curved fall).
    /// </summary>
    /// <summary>
    /// Draws a jazz scoop (rises into the note) or plop (falls into it) — the
    /// mirror of <see cref="DrawBendAfter"/>: the curve starts away-and-left
    /// and arrives at the notehead nearly horizontal.
    /// </summary>
    private static void DrawBendBefore(double x1, double y1, bool rise, IDrawingContext gc)
    {
        const double len = 1.25;
        double drop = rise ? 1.7 : -1.7;          // start BELOW for a scoop
        double x0 = x1 - len, y0 = y1 + drop;
        // Control point mirrored: arrives at the note nearly horizontal.
        double cx = x1 - len * 0.62, cy = y1 + drop * 0.08;
        double px = x0, py = y0;
        const int seg = 8;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * x1;
            double ny = u * u * y0 + 2 * u * t * cy + t * t * y1;
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
    }

    private static void DrawBendAfter(double x0, double y0, bool fall, IDrawingContext gc)
    {
        const double len = 1.25;                 // horizontal reach
        double drop = fall ? 1.7 : -1.7;          // vertical reach (down for fall)
        // Control point: leaves the note nearly horizontal, then curves away.
        double cx = x0 + len * 0.62, cy = y0 + drop * 0.08;
        double px = x0, py = y0;
        const int seg = 8;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * (x0 + len);
            double ny = u * u * y0 + 2 * u * t * cy + t * t * (y0 + drop);
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
    }

    /// <summary>
    /// Draws a chord diagram (fret frame): string grid, finger dots, o/x
    /// row, and an "Nfr" label when the shape sits above the 4th fret.
    /// Spec is LOW string first ("x32010").
    /// LILYPOND-REF: LP \fret-diagram-terse / MusicXML &lt;frame&gt;.
    /// </summary>
    private static void DrawFretFrame(double cx, double bottomY, string spec, IDrawingContext gc)
    {
        int strings = spec.Length;
        const double dx = 0.55;   // string spacing
        const double dy = 0.5;    // fret spacing
        const int fretRows = 4;
        double width = (strings - 1) * dx;
        double left = cx - width / 2;
        // The anchor Y comes from the script/skyline machinery (the frame's
        // real ink box is seeded there) — the grid bottom sits ON the anchor.
        double top = bottomY - fretRows * dy; // grid top (below the o/x row)
        double bottom = top + fretRows * dy;

        // Base fret: shapes above the 4th fret shift down and get "Nfr".
        int minFret = int.MaxValue;
        foreach (var ch in spec)
            if (ch is >= '1' and <= '9')
                minFret = Math.Min(minFret, ch - '0');
        int baseFret = minFret != int.MaxValue && minFret > 4 ? minFret : 1;

        for (int s = 0; s < strings; s++)
            gc.DrawLine(left + s * dx, top, left + s * dx, bottom, Color.Black, 0.05);
        for (int f = 0; f <= fretRows; f++)
            gc.DrawLine(left, top + f * dy, left + width, top + f * dy, Color.Black,
                f == 0 && baseFret == 1 ? 0.16 : 0.05); // nut is thick at position 1

        if (baseFret > 1)
            gc.DrawText($"{baseFret}fr", left + width + 0.35, top + dy * 0.5, 1.1,
                "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);

        for (int s = 0; s < strings; s++)
        {
            char ch = spec[s];
            double sx = left + s * dx;
            if (ch == 'x')
            {
                gc.DrawLine(sx - 0.16, top - 0.5, sx + 0.16, top - 0.18, Color.Black, 0.07);
                gc.DrawLine(sx - 0.16, top - 0.18, sx + 0.16, top - 0.5, Color.Black, 0.07);
            }
            else if (ch is '0' or 'o')
            {
                gc.DrawCircle(sx, top - 0.34, 0.15, Color.Black);
                gc.DrawCircle(sx, top - 0.34, 0.09, Color.White);
            }
            else if (ch is >= '1' and <= '9')
            {
                int fret = ch - '0' - (baseFret - 1);
                if (fret is >= 1 and <= fretRows)
                    gc.DrawCircle(sx, top + (fret - 0.5) * dy, 0.17, Color.Black);
            }
        }
    }

    /// <summary>
    /// Draws a guitar bend-up: a curve leaving the note/fret nearly
    /// horizontally then rising steeply, an arrowhead at the top, and the
    /// bend amount ("½", "full", "1½", …) above the arrowhead.
    /// LILYPOND-REF: TAB bend convention (bend-alter in MusicXML terms);
    /// curve idiom follows DrawBendAfter.
    /// </summary>
    private static void DrawGuitarBend(double x0, double y0, int semitones, IDrawingContext gc)
    {
        const double len = 1.6;    // horizontal reach
        const double rise = 2.6;   // vertical reach (upward)
        double cx = x0 + len * 0.95, cy = y0 - rise * 0.1; // sharp late turn upward
        double topX = x0 + len, topY = y0 - rise;
        double px = x0, py = y0;
        const int seg = 10;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * topX;
            double ny = u * u * y0 + 2 * u * t * cy + t * t * topY;
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
        // Arrowhead: a V of two strokes at the curve's top (no polygon API).
        const double ah = 0.55, aw = 0.32;
        gc.DrawLine(topX - aw, topY + ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        gc.DrawLine(topX + aw, topY + ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        // Amount label in guitar convention: semitones → steps.
        string label = semitones switch
        {
            1 => "½",
            2 => "full",
            _ => (semitones % 2 == 0) ? (semitones / 2).ToString() : $"{semitones / 2}½",
        };
        gc.DrawText(label, topX, topY - 0.35, 1.6, "serif", FontStyle.Italic,
            TextAnchor.Middle, Color.Black);
    }

    // ---------- Lyrics ----------

    /// <summary>
    /// Draws lyric syllables (and any hyphen / extender connectors) below
    /// the staff using serif text at the LP-style reduced size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:32-52 LyricText grob
    /// LILYPOND-REF: scm/define-grobs.scm:3025 font-size = -1
    /// </remarks>
    private static void DrawLyrics(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.LyricLayouts.IsDefaultOrEmpty) return;
        double lyricFontSize = FontSize * 0.8;
        foreach (var l in layout.LyricLayouts)
        {
            if (!sysY.TryGetValue(l.Item.MeasureIndex, out var sy)) continue; // other page
            double y = sy + l.Y;
            // Tag the syllable with its source offset (data-pos) so the preview can
            // click-to-jump and editor-highlight it like a note. SourcePosition 0
            // means "unknown" (would clash with the bar-0 section mark), so only
            // scope a real offset.
            if (l.Item.SourcePosition > 0)
                using (gc.Source(l.Item.SourcePosition))
                    gc.DrawText(l.Item.Text, l.X, y, lyricFontSize, "serif",
                        FontStyle.Regular, TextAnchor.Middle, Color.Black);
            else
                gc.DrawText(l.Item.Text, l.X, y, lyricFontSize, "serif",
                    FontStyle.Regular, TextAnchor.Middle, Color.Black);
            // Hyphen dashes / extender lines: DrawLyricHyphens (LyricHyphen
            // layouts) — the single source, matching LP's grobs.
        }
    }

    // ---------- Hairpins (cresc / dim wedges) ----------

    /// <summary>
    /// Draws crescendo/decrescendo wedges as a pair of straight lines that
    /// converge to a point (cresc) or open from a point (dim).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:110-358 print()
    /// LILYPOND-REF: scm/define-grobs.scm:1777 Hairpin grob (thickness = 1.0)
    /// </remarks>
    private static void DrawHairpins(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.HairpinLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var h in layout.HairpinLayouts)
        {
            if (!sysY.TryGetValue(h.StartMeasureIndex, out var sy)) continue; // other page
            double absY = os.Y(sy + h.Y, h.StaffIndex, h.StartMeasureIndex);
            double startOpening = os.Size(h.StartOpening, h.StaffIndex);
            double endOpening = os.Size(h.EndOpening, h.StaffIndex);
            double leftTop = absY - startOpening;
            double leftBottom = absY + startOpening;
            double rightTop = absY - endOpening;
            double rightBottom = absY + endOpening;
            using (gc.Source(h.SourcePosition))
            {
                // Round caps so the two arms close cleanly at the wedge apex
                // (where StartOpening or EndOpening is 0): butt caps left a
                // small V-notch at the point. Matches LilyPond's blot-rounded
                // line ends. LILYPOND-REF: lily/hairpin.cc — Round_ blot.
                gc.DrawLine(h.StartX, leftTop, h.EndX, rightTop, Color.Black, thickness, cap: LineCap.Round);
                gc.DrawLine(h.StartX, leftBottom, h.EndX, rightBottom, Color.Black, thickness, cap: LineCap.Round);
            }
        }
    }

    // ---------- Ottava brackets (8va / 8vb / 15ma) ----------

    /// <summary>
    /// Draws ottava brackets: serif italic-bold text label, dashed extension
    /// line, and a vertical hook on the closing end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm OttavaBracket grob
    /// LILYPOND-REF: lily/ottava-bracket.cc — Ottava_bracket
    /// </remarks>
    private static void DrawOttavaBrackets(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.OttavaBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.OttavaBracketLayouts)
        {
            if (!sysY.TryGetValue(b.StartMeasureIndex, out var sy)) continue; // other page
            double absY = os.Y(sy + b.Y, b.StaffIndex, b.StartMeasureIndex);
            double textFontSize = os.Size(FontSize * 0.45, b.StaffIndex);
            using (gc.Source(b.SourcePosition))
            {
                gc.DrawText(b.Text, b.StartX, absY, textFontSize, "serif",
                    FontStyle.BoldItalic, TextAnchor.Start, Color.Black);

                double textWidth = SerifTextMetrics.MeasureBold(b.Text, textFontSize);
                double lineStartX = b.StartX + textWidth + 0.5;
                if (lineStartX < b.EndX)
                {
                    double dashOn = b.DashPeriod * b.DashFraction;
                    double dashOff = b.DashPeriod * (1 - b.DashFraction);
                    gc.DrawLine(lineStartX, absY, b.EndX, absY,
                        Color.Black, thickness, (dashOn, dashOff));
                }
                if (b.EdgeHeight > 0)
                {
                    double hookDir = b.IsAbove ? 1 : -1;
                    gc.DrawLine(b.EndX, absY, b.EndX,
                        absY + os.Size(b.EdgeHeight, b.StaffIndex) * hookDir,
                        Color.Black, thickness);
                }
            }
        }
    }

    // ---------- Volta brackets (1./2. endings) ----------

    /// <summary>
    /// Draws volta (repeat ending) brackets: optional left hook, horizontal
    /// line, optional right hook, and the volta-number text label.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc:1-170 Volta_bracket_interface
    /// LILYPOND-REF: scm/define-grobs.scm:4292-4317 VoltaBracket grob
    /// </remarks>
    private static void DrawVoltaBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.VoltaBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;
        double edgeHeight = VoltaBracketEngraver.GetEdgeHeight();

        foreach (var v in layout.VoltaBracketLayouts)
        {
            if (!sysY.TryGetValue(v.StartMeasureIndex, out var sy)) continue; // other page
            double absY = sy + v.Y;
            bool hasText = !string.IsNullOrEmpty(v.VoltaText);
            using (gc.Source(v.SourcePosition))
            {
                if (hasText)
                    gc.DrawLine(v.StartX, absY, v.StartX, absY + edgeHeight,
                        Color.Black, thickness);
                gc.DrawLine(v.StartX, absY, v.EndX, absY,
                    Color.Black, thickness);
                if (v.IsClosed)
                    gc.DrawLine(v.EndX, absY, v.EndX, absY + edgeHeight,
                        Color.Black, thickness);
                if (hasText)
                {
                    // Hang the number from just below the horizontal line so it sits
                    // inside the bracket instead of overlapping the line (matches the
                    // reference SvgRenderer: y = top of glyph at absY + 0.3).
                    double textY = absY + 0.3;
                    gc.DrawText(v.VoltaText, v.StartX + 0.5, textY,
                        FontSize * 0.6, "serif", FontStyle.Bold, TextAnchor.Start, Color.Black,
                        VerticalAnchor.Hanging);
                }
            }
        }
    }

    // ---------- Tuplet brackets ----------

    /// <summary>
    /// Draws tuplet brackets: hook + sloped line (split around the number) +
    /// hook + centered number text. When all members are beamed the bracket
    /// is suppressed (number-only).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 print()
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    /// </remarks>
    private static void DrawTupletBrackets(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TupletBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;

        foreach (var b in layout.TupletBracketLayouts)
        {
            if (!sysY.TryGetValue(b.MeasureIndex, out var sy)) continue; // other page
            double edgeHeight = os.Size(TupletBracketEngraver.GetEdgeHeight(), b.StaffIndex);
            double startY = os.Y(sy + b.StartY, b.StaffIndex, b.MeasureIndex);
            double endY = os.Y(sy + b.EndY, b.StaffIndex, b.MeasureIndex);
            double midX = (b.StartX + b.EndX) / 2;
            double midY = (startY + endY) / 2;
            double hookDir = b.IsStemUp ? 1 : -1;

            using (gc.Source(b.SourcePosition))
            {
                if (b.ShowBracket)
                {
                    gc.DrawLine(b.StartX, startY, b.StartX, startY + edgeHeight * hookDir,
                        Color.Black, thickness);

                    const double numberGap = 1.0;
                    double totalWidth = b.EndX - b.StartX;
                    double leftFrac = totalWidth > 0 ? (midX - numberGap - b.StartX) / totalWidth : 0.5;
                    double rightFrac = totalWidth > 0 ? (midX + numberGap - b.StartX) / totalWidth : 0.5;
                    double leftGapY = startY + (endY - startY) * leftFrac;
                    double rightGapY = startY + (endY - startY) * rightFrac;

                    gc.DrawLine(b.StartX, startY, midX - numberGap, leftGapY,
                        Color.Black, thickness);
                    gc.DrawLine(midX + numberGap, rightGapY, b.EndX, endY,
                        Color.Black, thickness);
                    gc.DrawLine(b.EndX, endY, b.EndX, endY + edgeHeight * hookDir,
                        Color.Black, thickness);
                }

                double textY = b.IsStemUp ? midY - 0.3 : midY + 0.8;
                gc.DrawText(b.NumberText, midX, textY,
                    os.Size(FontSize * 0.6, b.StaffIndex), "serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
        }
    }

    // ---------- Trill spanners (tr + wavy line) ----------

    /// <summary>
    /// Draws trill spanners: the "tr" Emmentaler glyph followed by a wavy
    /// extension line. The wave is approximated as a polyline through
    /// peak/valley points (enough segments per cycle that it reads as smooth).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:4082 (style . trill)
    /// </remarks>
    private static void DrawTrillSpanners(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TrillSpannerLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TrillSpannerLayouts)
        {
            if (!sysY.TryGetValue(s.StartMeasureIndex, out var sy)) continue; // other page
            double absY = os.Y(sy + s.Y, s.StaffIndex, s.StartMeasureIndex);
            double waveAmplitude = os.Size(0.2, s.StaffIndex);
            using (gc.Source(s.SourcePosition))
            {
                bool isContinuation = Math.Abs(s.GlyphX - s.LineStartX) < 0.01;
                if (!isContinuation)
                    gc.DrawGlyph(EmmentalerGlyphs.OrnTrill, s.GlyphX, absY,
                        os.Size(FontSize, s.StaffIndex));
                if (s.LineStartX < s.LineEndX)
                {
                    double length = s.LineEndX - s.LineStartX;
                    int halfWaves = Math.Max(1, (int)(length / (wavePeriod / 2)));
                    double seg = length / halfWaves;
                    double prevX = s.LineStartX, prevY = absY;
                    // Approximate Q-curves with 4 line segments per half-wave;
                    // visually indistinguishable at typical print sizes.
                    const int subdivisions = 4;
                    for (int i = 0; i < halfWaves; i++)
                    {
                        double startX = s.LineStartX + i * seg;
                        double sign = (i % 2 == 0) ? -1 : 1;
                        for (int j = 1; j <= subdivisions; j++)
                        {
                            double t = (double)j / subdivisions;
                            double x = startX + t * seg;
                            // Parabolic shape: y = absY + sign * amplitude * 4 t (1-t)
                            double y = absY + sign * waveAmplitude * 4 * t * (1 - t);
                            gc.DrawLine(prevX, prevY, x, y, Color.Black, thickness);
                            prevX = x; prevY = y;
                        }
                    }
                }
            }
        }
    }

    // ---------- Glissandos ----------

    /// <summary>Draws a simple straight glissando line between two notes.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm, scm/define-grobs.scm Glissando grob
    /// </remarks>
    private static void DrawGlissandos(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.GlissandoLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var g in layout.GlissandoLayouts)
        {
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (g.MeasureIndex >= 0 && !sysY.ContainsKey(g.MeasureIndex))
                continue; // other page
            using (gc.Source(g.SourcePosition))
                gc.DrawLine(
                    g.StartX, os.Y(g.StartY, g.StaffIndex, g.MeasureIndex),
                    g.EndX, os.Y(g.EndY, g.StaffIndex, g.MeasureIndex),
                    Color.Black, thickness);
        }
    }

    // ---------- Arpeggios (wavy vertical line) ----------

    /// <summary>
    /// Draws arpeggio markings: a wavy vertical line on the left of a chord.
    /// Like the trill wavy line, the curve is approximated as a polyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
    /// </remarks>
    private static void DrawArpeggios(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArpeggioLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var a in layout.ArpeggioLayouts)
        {
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (a.MeasureIndex >= 0 && !sysY.ContainsKey(a.MeasureIndex))
                continue; // other page
            // Frame B -> device: reflect the stored Y-up extents against this
            // arpeggio's own staff middle (the single per-grob draw boundary),
            // then apply the ossia affine exactly as before.
            double staffMiddleY = os.StaffMiddleDeviceY(a.StaffIndex, a.MeasureIndex, StaffHeight);
            double topY = os.Y(StaffFrame.ToDevice(a.TopYUp, staffMiddleY), a.StaffIndex, a.MeasureIndex);
            double bottomY = os.Y(StaffFrame.ToDevice(a.BottomYUp, staffMiddleY), a.StaffIndex, a.MeasureIndex);
            double waveAmplitude = os.Size(0.2, a.StaffIndex);
            double length = bottomY - topY;
            if (length <= 0) continue;
            // Non-arpeggiate: a straight vertical bracket with end ticks —
            // the chord is NOT rolled. LILYPOND-REF: \arpeggioBracket.
            if (a.Bracket)
            {
                using (gc.Source(a.SourcePosition))
                {
                    gc.DrawLine(a.X, topY, a.X, bottomY, Color.Black, thickness * 1.6);
                    gc.DrawLine(a.X, topY, a.X + 0.7, topY, Color.Black, thickness * 1.6);
                    gc.DrawLine(a.X, bottomY, a.X + 0.7, bottomY, Color.Black, thickness * 1.6);
                }
                continue;
            }
            int halfWaves = Math.Max(1, (int)(length / (wavePeriod / 2)));
            double seg = length / halfWaves;
            double prevX = a.X, prevY = topY;
            const int subdivisions = 4;
            using (gc.Source(a.SourcePosition))
            {
                for (int i = 0; i < halfWaves; i++)
                {
                    double startY = topY + i * seg;
                    double sign = (i % 2 == 0) ? -1 : 1;
                    for (int j = 1; j <= subdivisions; j++)
                    {
                        double t = (double)j / subdivisions;
                        double y = startY + t * seg;
                        double x = a.X + sign * waveAmplitude * 4 * t * (1 - t);
                        gc.DrawLine(prevX, prevY, x, y, Color.Black, thickness);
                        prevX = x; prevY = y;
                    }
                }
            }
        }
    }
}
