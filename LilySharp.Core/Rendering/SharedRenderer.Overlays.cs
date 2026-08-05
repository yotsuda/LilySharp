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
    private static void DrawDynamics(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.DynamicLayouts.IsDefaultOrEmpty) return;
        double fontSize = FontSize * 0.5;
        foreach (var d in layout.DynamicLayouts)
        {
            string text = NormalizeDynamicText(d.Text);
            if (!sysTopYUp.ContainsKey(d.MeasureIndex)) continue; // other page
            // A dynamic on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            // Frame B -> device: reflect the Y-up value against this dynamic's own
            // staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(d.StaffIndex, d.MeasureIndex, StaffHeight);
            double y = os.YUp(midYup + d.YUp, d.StaffIndex, d.MeasureIndex);
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
    /// LILYPOND-REF: lily/script-engraver.cc:235 acknowledge_rhythmic_head / :253 acknowledge_note_column
    /// </remarks>
    private static void DrawArticulations(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArticulationLayouts)
        {
            if (string.IsNullOrEmpty(a.Glyph)) continue;
            if (!sysTopYUp.ContainsKey(a.MeasureIndex)) continue; // other page
            // A script on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            // Frame B -> device: reflect the Y-up value against this script's own
            // staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(a.StaffIndex, a.MeasureIndex, StaffHeight);
            double y = os.YUp(midYup + a.YUp, a.StaffIndex, a.MeasureIndex);
            // The grob's own font-size (0 for an ordinary script, −2 for an editorial
            // accidental), composed with the ossia its staff may carry.
            double scale = os.Size(
                EmmentalerDesignSize.Magstep(a.FontSizeStep), a.StaffIndex);
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
            // Bartók (snap) pizzicato takes the ordinary glyph path below: it is the
            // font's scripts.snappizzicato (EmmentalerGlyphs.ScriptSnappizzicato), not
            // primitives — see ArticulationItem's SnapPizz mapping.
            // …and from the design that font-size selects: an editorial accidental is the
            // 16 design's own outline (the same one ArticulationEngraver.ScriptSkylines
            // profiled), not the 20's drawn small. IDrawingContext.MusicFace.
            using (gc.Source(a.SourcePosition))
            using (gc.MusicFace(
                EmmentalerDesignSize.ForFontSizeStep(a.FontSizeStep).Rounded))
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
        gc.DrawLine(x, noteY + h, x + w, noteY - h, c, t, cap: LineCap.Round);
        gc.DrawLine(x, noteY - h, x + w, noteY + h, c, t, cap: LineCap.Round);
    }

    /// <summary>
    /// Draws a jazz "fall" (drops away) or "doit" (rises away) — a short curved
    /// line trailing off to the right of a note, approximated by a polyline along
    /// a quadratic Bézier. LILYPOND-REF: scm/output-lib.scm:1343 bend::print + BendAfter grob in scm/define-grobs.scm:551 (curved fall).
    /// </summary>
    /// <summary>
    /// Draws a jazz scoop (rises into the note) or plop (falls into it) — the
    /// mirror of <see cref="DrawBendAfter"/>: the curve starts away-and-left
    /// and arrives at the notehead nearly horizontal.
    /// </summary>
    private static void DrawBendBefore(double x1, double y1, bool rise, IDrawingContext gc)
    {
        const double len = 1.25;
        double drop = rise ? 1.7 : -1.7;          // start BELOW for a scoop (device down)
        double x0 = x1 - len, y0 = y1 - drop;
        // Control point mirrored: arrives at the note nearly horizontal.
        double cx = x1 - len * 0.62, cy = y1 - drop * 0.08;
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
        double drop = fall ? 1.7 : -1.7;          // vertical reach (down for fall = down device)
        // Control point: leaves the note nearly horizontal, then curves away.
        double cx = x0 + len * 0.62, cy = y0 - drop * 0.08;
        double px = x0, py = y0;
        const int seg = 8;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * (x0 + len);
            double ny = u * u * y0 + 2 * u * t * cy + t * t * (y0 - drop);
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
        // Y-up: the grid top is above the bottom (larger Y).
        double top = bottomY + fretRows * dy; // grid top (above the grid bottom)
        double bottom = top - fretRows * dy;

        // Base fret: shapes above the 4th fret shift down and get "Nfr".
        int minFret = int.MaxValue;
        foreach (var ch in spec)
            if (ch is >= '1' and <= '9')
                minFret = Math.Min(minFret, ch - '0');
        int baseFret = minFret != int.MaxValue && minFret > 4 ? minFret : 1;

        for (int s = 0; s < strings; s++)
            gc.DrawLine(left + s * dx, top, left + s * dx, bottom, Color.Black, 0.05);
        for (int f = 0; f <= fretRows; f++)
            gc.DrawLine(left, top - f * dy, left + width, top - f * dy, Color.Black,
                f == 0 && baseFret == 1 ? 0.16 : 0.05); // nut is thick at position 1

        if (baseFret > 1)
            gc.DrawText($"{baseFret}fr", left + width + 0.35, top - dy * 0.5, 1.1,
                "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);

        for (int s = 0; s < strings; s++)
        {
            char ch = spec[s];
            double sx = left + s * dx;
            if (ch == 'x')
            {
                gc.DrawLine(sx - 0.16, top + 0.5, sx + 0.16, top + 0.18, Color.Black, 0.07);
                gc.DrawLine(sx - 0.16, top + 0.18, sx + 0.16, top + 0.5, Color.Black, 0.07);
            }
            else if (ch is '0' or 'o')
            {
                gc.DrawCircle(sx, top + 0.34, 0.15, Color.Black);
                gc.DrawCircle(sx, top + 0.34, 0.09, Color.White);
            }
            else if (ch is >= '1' and <= '9')
            {
                int fret = ch - '0' - (baseFret - 1);
                if (fret is >= 1 and <= fretRows)
                    gc.DrawCircle(sx, top - (fret - 0.5) * dy, 0.17, Color.Black);
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
        const double rise = 2.6;   // vertical reach (upward = larger Y-up)
        double cx = x0 + len * 0.95, cy = y0 + rise * 0.1; // sharp late turn upward
        double topX = x0 + len, topY = y0 + rise;
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
        gc.DrawLine(topX - aw, topY - ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        gc.DrawLine(topX + aw, topY - ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        // Amount label in guitar convention: semitones → steps.
        string label = semitones switch
        {
            1 => "½",
            2 => "full",
            _ => (semitones % 2 == 0) ? (semitones / 2).ToString() : $"{semitones / 2}½",
        };
        gc.DrawText(label, topX, topY + 0.35, 1.6, "serif", FontStyle.Italic,
            TextAnchor.Middle, Color.Black);
    }

    // ---------- Lyrics ----------

    /// <summary>
    /// Draws lyric syllables (and any hyphen / extender connectors) below
    /// the staff using serif text at the LP-style reduced size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:32-52 LyricText grob
    /// LILYPOND-REF: scm/define-grobs.scm:2213-2230 LyricText, whose self-alignment-X is left-align-at-split-notes, declares (font-size . 1.0) — the size now
    /// comes from EngravingDefaults.LyricTextFontSize, shared with LyricEngraver so the
    /// drawn ink and the reserved ink are the same size. It used to be a local
    /// FontSize * 0.8 (= 3.2), 29.6% larger, with the divergence declared right here.
    /// </remarks>
    private static void DrawLyrics(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.LyricLayouts.IsDefaultOrEmpty) return;
        double lyricFontSize = LilySharp.Core.Svg.EngravingDefaults.LyricTextFontSize;
        foreach (var l in layout.LyricLayouts)
        {
            if (!sysTopYUp.TryGetValue(l.Item.MeasureIndex, out var syUp)) continue; // other page
            // Page Y-up baseline: lift the system top and add the stored offset.
            double y = syUp + l.YUp;
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
    private static void DrawHairpins(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.HairpinLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var h in layout.HairpinLayouts)
        {
            if (!sysTopYUp.TryGetValue(h.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift the system top (H − sy), add the stored offset, then
            // apply the ossia affine in the same frame.
            double absY = os.YUp(syUp + h.YUp, h.StaffIndex, h.StartMeasureIndex);
            double startOpening = os.Size(h.StartOpening, h.StaffIndex);
            double endOpening = os.Size(h.EndOpening, h.StaffIndex);
            double leftTop = absY + startOpening;
            double leftBottom = absY - startOpening;
            double rightTop = absY + endOpening;
            double rightBottom = absY - endOpening;
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
    private static void DrawOttavaBrackets(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.OttavaBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.OttavaBracketLayouts)
        {
            if (!sysTopYUp.TryGetValue(b.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift the system top and add the stored offset, then ossia.
            double absY = os.YUp(syUp + b.YUp, b.StaffIndex, b.StartMeasureIndex);
            // The label rides the paper's text-font-size, which OttavaBracket does not step
            // (it declares only font-series and font-shape) — the em the TextScript pair
            // measured, not the 0.45 × FontSize this drew until 2026-08-02
            // (ledger ottava.label.ink-height, −0.288062616 before the port).
            double textFontSize = os.Size(EngravingDefaults.OttavaBracketFontSize, b.StaffIndex);
            using (gc.Source(b.SourcePosition))
            {
                // LilyPond CENTRES the label's ink on the line — text.align_to (Y_AXIS,
                // CENTER) in Ottava_bracket::print, which its symmetric grob extent shows
                // through — so the baseline sits the ink's own centre BELOW it. Drawing the
                // baseline on the line put the label 0.621000054 too high
                // (ledger ottava.label.line-to-ink-centre), and the reservation reads the
                // same offset (OttavaBracketEngraver.Skylines).
                gc.DrawText(b.Text, b.StartX,
                    absY - os.Size(
                        OttavaBracketEngraver.LabelInkCentre(
                            b.Text, EngravingDefaults.OttavaBracketFontSize),
                        b.StaffIndex),
                    textFontSize, "serif",
                    FontStyle.BoldItalic, TextAnchor.Start, Color.Black);

                double lineStartX = OttavaBracketEngraver.LineStartX(
                    b.Text, b.StartX, textFontSize);
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
                        absY - os.Size(b.EdgeHeight, b.StaffIndex) * hookDir,
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
    private static void DrawVoltaBrackets(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.VoltaBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;
        double edgeHeight = VoltaBracketEngraver.GetEdgeHeight();

        foreach (var v in layout.VoltaBracketLayouts)
        {
            if (!sysTopYUp.TryGetValue(v.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift this segment's own system top and add the stored offset.
            double absY = syUp + v.YUp;
            bool hasText = !string.IsNullOrEmpty(v.VoltaText);
            using (gc.Source(v.SourcePosition))
            {
                if (hasText)
                    gc.DrawLine(v.StartX, absY, v.StartX, absY - edgeHeight,
                        Color.Black, thickness);
                gc.DrawLine(v.StartX, absY, v.EndX, absY,
                    Color.Black, thickness);
                if (v.IsClosed)
                    gc.DrawLine(v.EndX, absY, v.EndX, absY - edgeHeight,
                        Color.Black, thickness);
                if (hasText)
                {
                    // The number's INK TOP sits exactly 0.3 below the horizontal line, so
                    // it hangs inside the bracket without overlapping it. Anchored at the
                    // BASELINE — the only anchor every backend places identically — a
                    // further Ink.Top down; the former VerticalAnchor.Hanging was realised
                    // as a TYPOGRAPHIC-ascent drop on every backend, which left an extra
                    // quarter staff-space of air over a digit's cap and drew the number
                    // deeper than OutsideStaffStacker.VoltaBottom reserves (0.3 + the
                    // string's own InkHeight, which THIS geometry makes exact).
                    var vInk = TextFontMetrics.Ink(
                        v.VoltaText, FontSize * 0.6, sans: false, FontStyle.Bold);
                    double textY = absY - 0.3 - vInk.Top;
                    gc.DrawText(v.VoltaText, v.StartX + 0.5, textY,
                        FontSize * 0.6, "serif", FontStyle.Bold, TextAnchor.Start, Color.Black);
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
    /// LILYPOND-REF: lily/tuplet-bracket.cc:290 Tuplet_bracket::print
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    /// </remarks>
    private static void DrawTupletBrackets(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TupletBracketLayouts.IsDefaultOrEmpty) return;
        // Was a bare 0.13 here, shadowing EngravingDefaults.TupletBracketThickness — which
        // has carried LilyPond's own 1.6 x line-thickness = 0.16, with its LILYPOND-REF,
        // all along and simply had no reader. Drawing and reserving must come from ONE
        // constant or they drift, so both now read that one.
        const double thickness = EngravingDefaults.TupletBracketThickness;

        foreach (var b in layout.TupletBracketLayouts)
        {
            if (!sysTopYUp.TryGetValue(b.MeasureIndex, out var syUp)) continue; // other page
            double edgeHeight = os.Size(TupletBracketEngraver.GetEdgeHeight(), b.StaffIndex);
            // Page Y-up: lift the system top, add the stored offsets, then ossia.
            double startY = os.YUp(syUp + b.StartYUp, b.StaffIndex, b.MeasureIndex);
            double endY = os.YUp(syUp + b.EndYUp, b.StaffIndex, b.MeasureIndex);
            double midX = (b.StartX + b.EndX) / 2;
            double midY = (startY + endY) / 2;
            double hookDir = b.IsStemUp ? 1 : -1;

            using (gc.Source(b.SourcePosition))
            {
                if (b.ShowBracket)
                {
                    gc.DrawLine(b.StartX, startY, b.StartX, startY - edgeHeight * hookDir,
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
                    gc.DrawLine(b.EndX, endY, b.EndX, endY - edgeHeight * hookDir,
                        Color.Black, thickness);
                }

                // CENTRED ON THE BRACKET LINE, both axes — LilyPond's own placement, and
                // the reason the number rather than the bracket is a tuplet's outermost
                // ink. lily/tuplet-number.cc:342 returns the midpoint of the bracket's
                // positions as the number's Y-offset for every tuplet that is not a knee
                // against a beam, and :227-228 aligns its stencil to CENTER on X and Y.
                // Lily# used to hang it off the line by a bare +0.3 / -0.8 at 2.4 units
                // bold-upright; it is now LilyPond's size and face, and SkylineBuilder
                // reserves exactly this ink.
                gc.DrawText(b.NumberText, midX, midY,
                    os.Size(TupletBracketEngraver.NumberFontSize, b.StaffIndex), "serif",
                    TupletBracketEngraver.NumberFontStyle, TextAnchor.Middle, Color.Black,
                    VerticalAnchor.Middle);
            }
        }
    }

    // ---------- Trill spanners (tr + wavy line) ----------

    /// <summary>
    /// Draws trill spanners: the "tr" Emmentaler glyph, then the line as a RUN of
    /// <c>scripts.trill_element</c> glyphs — which is what the line is.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:4083 (style . trill) selects
    ///   lily/line-interface.cc:226-229 <c>line</c> -> :48-108 make_trill_line, which repeats
    ///   the element along the line rather than stroking a curve.
    /// ⚠️ Until 2026-07-30 this stroked a parabolic polyline with an amplitude of its own,
    ///   so the drawn line and the reserved one were two models. They are one now: the
    ///   element positions come from <see cref="Svg.Layout.TrillWaveOutline"/>, the same
    ///   house the engraver's aligned_side and the outside-staff pass read.
    /// </remarks>
    private static void DrawTrillSpanners(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TrillSpannerLayouts.IsDefaultOrEmpty) return;
        foreach (var s in layout.TrillSpannerLayouts)
        {
            if (!sysTopYUp.TryGetValue(s.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift the system top, add the stored offset, then ossia.
            double absY = os.YUp(syUp + s.YUp, s.StaffIndex, s.StartMeasureIndex);
            using (gc.Source(s.SourcePosition))
            {
                bool isContinuation = Math.Abs(s.GlyphX - s.LineStartX) < 0.01;
                if (!isContinuation)
                {
                    // The "tr" glyph hangs a staff space below the LINE — LilyPond's
                    // bound text carries stencil-offset (0 . -1), so the glyph's ink
                    // straddles the line ((-1.0 . 1.1) in LP's own ext dump) instead of
                    // sitting on it. The layout's YUp is the line; the reservation
                    // (OutsideStaffStacker.PlaceTrills) prices the same offset.
                    // LILYPOND-REF: scm/define-grobs.scm:4068 TrillSpanner stencil-offset, inside its left-bound-info text
                    gc.DrawGlyph(EmmentalerGlyphs.OrnTrill, s.GlyphX,
                        absY - os.Size(EngravingDefaults.TrillSpannerTextOffsetDown, s.StaffIndex),
                        os.Size(FontSize, s.StaffIndex));
                }
                if (s.LineStartX < s.LineEndX)
                {
                    // The line: one element glyph per copy, at the run's own positions, with
                    // the element's stencil box CENTRED on the line (align_to Y CENTER), so
                    // only whole elements are drawn and the run stops short of the bound by
                    // the remainder — LilyPond's own picture.
                    // LILYPOND-REF: lily/line-interface.cc:68-97 make_trill_line — find the
                    //   element by name, align_to (Y_AXIS, CENTER), add_at_edge per copy.
                    double baseline = absY
                        + os.Size(TrillWaveOutline.GlyphBaselineOffset, s.StaffIndex);
                    double size = os.Size(FontSize, s.StaffIndex);
                    foreach (double originX in TrillWaveOutline.ElementOrigins(
                                 s.LineStartX, s.LineEndX - s.LineStartX))
                    {
                        gc.DrawGlyph(EmmentalerGlyphs.OrnTrillElement, originX, baseline, size);
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
    private static void DrawGlissandos(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.GlissandoLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var g in layout.GlissandoLayouts)
        {
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (g.MeasureIndex >= 0 && !sysTopYUp.ContainsKey(g.MeasureIndex))
                continue; // other page
            // Frame B -> device: reflect each endpoint against this glissando's own
            // staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(g.StaffIndex, g.MeasureIndex, StaffHeight);
            using (gc.Source(g.SourcePosition))
                gc.DrawLine(
                    g.StartX, os.YUp(midYup + g.StartYUp, g.StaffIndex, g.MeasureIndex),
                    g.EndX, os.YUp(midYup + g.EndYUp, g.StaffIndex, g.MeasureIndex),
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
    private static void DrawArpeggios(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArpeggioLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArpeggioLayouts)
        {
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (a.MeasureIndex >= 0 && !sysTopYUp.ContainsKey(a.MeasureIndex))
                continue; // other page
            // ⚠️ EVERYTHING BELOW IS PAGE Y-UP, IN STAFF SPACES, UP-POSITIVE — the frame the
            // layout stores and the frame this renderer draws in. Nothing here converts to
            // device: YFlipDrawingContext does that at the output boundary, so a larger
            // number is HIGHER ON THE PAGE and stepping DOWN means SUBTRACTING. The names
            // carry the frame (…YUp) because a bare `topY` reads as device and cost a sign
            // error in the bracket's end ticks on 2026-08-03.
            double midYUp = os.StaffMiddleYUp(a.StaffIndex, a.MeasureIndex, StaffHeight);
            // Non-arpeggiate: a straight vertical bracket with end ticks — the chord is NOT
            // rolled.
            // LILYPOND-REF: lily/lookup.cc:542-560 Lookup::bracket — three round_filled_box
            //   calls, which is what lily/arpeggio.cc:191-201 Chord_bracket::print hands the
            //   interval to. Both of the things that were wrong here are in their bounds:
            //   :546-547  the SPINE spans the interval and is `thick` wide about the origin.
            //   :551-554  the TOP tick lies at (iv[UP] - thick .. iv[UP]) — INSIDE the
            //             interval, so it adds no length — and spans
            //             (-thick/2 .. thick/2 + protrusion), i.e. it starts at the spine's
            //             LEFT edge, not at its centre, which is why the grob is
            //             `thick + protrusion` wide.
            //   :556-557  the BOTTOM tick, the same box at (iv[DOWN] .. iv[DOWN] + thick).
            // Drawn as strokes rather than boxes: a butt-capped line of width `thick` centred
            // on each box's midline covers exactly that box.
            // ⚠️ LILYSHARP-OWN: THE CORNERS. LilyPond's boxes are round_filled_box(b, blot)
            //   with blot = thick, so its corners are rounded; a stroked line's are square.
            //   The EXTENTS are identical — which is why the ledger's three bracket points
            //   close — and this engine has no blot anywhere, so the difference is a standing
            //   convention rather than something chosen here. Naming it so the next reader does
            //   not take "covers exactly that box" for "is that stencil".
            if (a.Bracket)
            {
                double topYUp = os.YUp(midYUp + a.TopYUp, a.StaffIndex, a.MeasureIndex);
                double bottomYUp = os.YUp(midYUp + a.BottomYUp, a.StaffIndex, a.MeasureIndex);
                double t = os.Size(ArpeggioEngraver.BracketThickness, a.StaffIndex);
                double half = t / 2;
                // The ticks span the grob's own X extent — the one place it is spelled.
                double left = a.X + os.Size(ArpeggioEngraver.BracketXExtent.Left, a.StaffIndex);
                double right = a.X + os.Size(ArpeggioEngraver.BracketXExtent.Right, a.StaffIndex);
                using (gc.Source(a.SourcePosition))
                {
                    gc.DrawLine(a.X, topYUp, a.X, bottomYUp, Color.Black, t);
                    // The ticks lie INSIDE the interval, so each steps half a thickness AWAY
                    // from its own end — down from the top (subtract, this being Y-up) and up
                    // from the bottom. Signed the other way they land just outside and
                    // lengthen the bracket by a whole thickness: 3.700000 where LilyPond
                    // draws 3.500000, which is what audit/lp-geometry chordbracket.y.length
                    // caught the first time this was written.
                    gc.DrawLine(left, topYUp - half, right, topYUp - half, Color.Black, t);
                    gc.DrawLine(left, bottomYUp + half, right, bottomYUp + half, Color.Black, t);
                }
                continue;
            }
            if (a.Copies <= 0) continue;
            // The wiggle is the GLYPH, stacked upward from the pile's bottom one designed
            // height at a time — the same copies the engraver counted and the spacing
            // reserved, so what is drawn and what is spaced for are one calculation.
            // LILYPOND-REF: lily/arpeggio.cc:180-183 add_at_edge, translate_axis
            //   (Arpeggio::print) — `mol.add_at_edge (Y_AXIS, UP, squiggle, 0.0)` per copy,
            //   then `mol.translate_axis (heads[LEFT], Y_AXIS)`.
            // ⚠️ The glyph's box starts at its baseline (0 . 1.0), so copy i's baseline is
            // the pile's bottom raised i heights — no centring term, unlike the trill
            // element's align_to (see TrillWaveOutline.GlyphBaselineOffset).
            double glyphSize = os.Size(FontSize, a.StaffIndex);
            using (gc.Source(a.SourcePosition))
            {
                for (int i = 0; i < a.Copies; i++)
                {
                    double baseline = os.YUp(
                        midYUp + a.BottomYUp + i * ArpeggioEngraver.WiggleHeight,
                        a.StaffIndex, a.MeasureIndex);
                    gc.DrawGlyph(EmmentalerGlyphs.Arpeggio, a.X, baseline, glyphSize);
                }
            }
        }
    }
}
