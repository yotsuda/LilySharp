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

using System;
using System.Globalization;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Core.LilyPond;

public sealed partial class LilyPondExporter
{
    /// <summary>
    /// A fenced text (docs/markdown-text-design.md) as a top-level markup list, before the
    /// score or after it: the page's own lines (<see cref="ProseBand"/>, on the page's metrics
    /// and paper), one markup a line, so LilyPond breaks nothing and pages them where the
    /// page does.
    /// </summary>
    /// <remarks>
    /// <c>\column-lines</c> is what gives each line the box the page gives it — two thirds of
    /// the baseline-skip over the baseline, one third under (scm/markup.scm space-lines); a
    /// bare list of markups stands on its ink instead. A paragraph's first line carries the
    /// white over it as a taller box; the first line and the last carry the text's margins
    /// (<c>yMargin</c>, or ProseBand.DefaultMargin) the same way.
    /// MEASURED 2026-11-12, LilyPond 2.26.0 (LilySharp-Lab sessions/p1226/md, t1tw2.ly against
    /// t1.lys), with no margin — today's <c>yMargin 0</c>: the composer's baseline to the
    /// first line 2.9705, a line to the next 3.0000, a paragraph to the next 4.5000, the last
    /// system's middle line to the closing text 12.0000 — the page's 2.95, 3.00, 4.50 and 12.00.
    /// LILYSHARP-OWN: the breaking, and the white between paragraphs. Not written yet: a
    /// size or a face the score gives the text (<c>fonts { md … }</c>).
    /// </remarks>
    private void EmitText(bool before)
    {
        if (_page is not { } page)
            return;
        var place = ProseBand.Stack(page.Texts.Blocks(before), page.TextMetrics, page.Paper.ContentWidth,
            page.Paper.StaffSpaceMm);
        if (place is null)
            return;
        double skip = ProseBand.Skip(page.TextMetrics);
        double up = ProseBand.LineUp(page.TextMetrics), down = ProseBand.LineDown(page.TextMetrics);
        _sb.Append(before ? "" : "\n").Append("\\markuplist \\column-lines {\n");
        // Every block of the place in the ONE list, in written order (grammar evaluation 4,
        // 2026-11-13: a later fence with a shape of its own is a block of its own). The list
        // stacks its lines extent to extent, and each block's first and last line carry its
        // margins as their extent — which is how the page stacks the blocks (ProseBand.Stack).
        foreach (var (band, _) in place.Blocks)
        {
            if (band.Frame is { } frame)
                EmitBoxedText(band, frame, band.Center, skip, up, down);
            else
                EmitPlainText(band, skip, up, down);
        }
        _sb.Append(before ? "}\n\n" : "}\n");
    }

    /// <summary>A block with no frame: one markup a line of the list.</summary>
    private void EmitPlainText(ProseBand band, double skip, double up, double down)
    {
        double previous = 0, previousScale = 1;
        for (int i = 0; i < band.Lines.Length; i++)
        {
            var line = band.Lines[i];
            _sb.Append("  ");
            // The line's box is the page's: the white over a paragraph, a heading or a loose
            // item, and a heading's own larger box (LilyPond would stand a larger line on its
            // ink, which depends on its letters).
            double lineUp = up * line.Scale, lineDown = down * line.Scale;
            double step = previousScale == 1 && line.Scale == 1 ? skip : down * previousScale + lineUp;
            double white = i == 0 ? 0 : line.Baseline - previous - step;
            // …and the text's margins (yMargin, or the page's default): the white over the
            // first line and under the last, part of those lines' extents as it is of the
            // band's. With `yMargin 0` nothing is written and the list is LilyPond's own.
            double over = i == 0 ? band.MarginTop : 0, under = i == band.Lines.Length - 1 ? band.MarginBottom : 0;
            if (white > 1e-9 || line.Scale != 1 || over > 1e-9 || under > 1e-9)
            {
                _sb.Append("\\with-dimension #Y #'(").Append(Num(-lineDown - under)).Append(" . ")
                    .Append(Num(lineUp + Math.Max(0, white) + over)).Append(") ");
            }
            previous = line.Baseline;
            previousScale = line.Scale;
            // A line with white at its left (xMargin, a list's indent) begins after it; a centred one is LilyPond's to centre
            // in the text's width (\fill-line of one markup), so that its own measure of the
            // words, not the page's, finds the middle. A list's line is not centred.
            bool centred = band.Center && !line.InList;
            double white0 = band.Left + (line.Marker is null ? line.Indent : 0);
            bool wrapped = white0 > 1e-9 || centred || line.Marker is not null;
            if (wrapped)
            {
                _sb.Append("\\concat { ");
                if (white0 > 1e-9)
                    _sb.Append("\\hspace #").Append(Num(white0)).Append(' ');
                EmitMarker(line);
                if (centred)
                    _sb.Append("\\override #'(line-width . ").Append(Num(band.TextWidth)).Append(") \\fill-line { ");
            }
            EmitTextLine(line);
            if (wrapped)
                _sb.Append(centred ? " } }" : " }");
            _sb.Append('\n');
        }
    }

    private static string Num(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// A list item's marker (owner's request 2026-11-13), written before the item's first
    /// line: a box as wide as the list's hanging indent — so the words begin where the page
    /// begins them, whatever LilyPond's face measures the marker — with the marker flush right
    /// in it, short of the words by the page's gap. Nothing for a line that begins no item.
    /// </summary>
    /// <remarks>LILYSHARP-OWN: the indent and the gap (ProseBand.ListIndent, MarkerGap).
    /// LilyPond right-aligns by ITS measure of the marker, as it centres a centred line by its
    /// own: the marker's right end is the page's, its left end differs by what the two faces
    /// measure it.</remarks>
    private void EmitMarker(ProseLine line)
    {
        if (line.Marker is not { } marker)
            return;
        double gap = ProseBand.MarkerGap * ProseBand.Em(_page!.TextMetrics);
        _sb.Append("\\with-dimension #X #'(0 . ").Append(Num(line.Indent)).Append(") \\translate #'(")
            .Append(Num(line.Indent - gap)).Append(" . 0) \\right-align \"").Append(Escape(marker.Text)).Append("\" ");
    }

    /// <summary>One line's words: a string, or its runs in their weights and slants; a
    /// heading's at its size (<c>\fontsize</c>, LilyPond's steps of 2^(1/6)).</summary>
    private void EmitTextLine(ProseLine line)
    {
        if (line.Scale != 1)
            _sb.Append("\\fontsize #").Append(Num(Math.Round(6 * Math.Log2(line.Scale), 6))).Append(' ');
        if (line.Pieces.Length == 0)
        {
            _sb.Append("\"\"");     // an item that writes no words: its marker alone
            return;
        }
        // The page sets a character its face lacks (kana, kanji) as a piece of its own, at its
        // em square (ProseBand.Break, grammar evaluation 4, 2026-11-13); LilyPond sets a
        // string by its own face, so neighbours of one style are one string again here.
        var pieces = new System.Collections.Generic.List<(string Text, FontStyle Style)>();
        foreach (var piece in line.Pieces)
        {
            if (pieces.Count > 0 && pieces[^1].Style == piece.Style)
                pieces[^1] = (pieces[^1].Text + piece.Text, piece.Style);
            else
                pieces.Add((piece.Text, piece.Style));
        }
        if (pieces.Count == 1 && pieces[0].Style == FontStyle.Regular)
        {
            _sb.Append('"').Append(Escape(pieces[0].Text)).Append('"');
            return;
        }
        _sb.Append("\\concat {");
        foreach (var piece in pieces)
        {
            _sb.Append(' ');
            if ((piece.Style & FontStyle.Bold) != 0)
                _sb.Append("\\bold ");
            if ((piece.Style & FontStyle.Italic) != 0)
                _sb.Append("\\italic ");
            _sb.Append('"').Append(Escape(piece.Text)).Append('"');
        }
        _sb.Append(" }");
    }

    /// <summary>
    /// A boxed text (owner-approved 2026-11-13) as ONE line of the markup list: LilyPond's
    /// <c>\box</c> around a <c>\column</c> of the page's lines, the column given the page's
    /// own extent — the text's width, the first line's box above its baseline and the last
    /// line's below — so that the frame stands where the page rules it whatever LilyPond's
    /// face measures the words.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm box, scm/stencil.scm box-stencil — the
    /// rule (<c>thickness</c> × line-thickness = 0.1) is drawn OUTSIDE the extent widened by
    /// <c>box-padding</c>; the page's padding is written as the override.
    /// The white between two paragraphs is a <c>\vspace</c> in the column.
    /// MEASURED 2026-11-13, LilyPond 2.26.0 (LilySharp-Lab sessions/p1226/md/fencelayout):
    /// docs/markdown-text-design.md section 5.1.
    /// </remarks>
    private void EmitBoxedText(ProseBand band, ProseFrame frame, bool center, double skip, double up, double down)
    {
        // ⚠️ The white is to the TEXT's left edge, not the frame's: \concat sets the next
        // markup's ORIGIN at the end of the one before, and the box reaches left of its
        // origin by its padding and rule (measured: with the frame's edge written here the
        // whole box stood 1.1 too far left, off the margin).
        // The white the page keeps above and below the frame (yMargin, or
        // ProseBand.DefaultMargin): the line's own extent, taller than the box by that much.
        _sb.Append("  \\with-dimension #Y #'(").Append(Num(-band.Down)).Append(" . ").Append(Num(band.Up)).Append(") ");
        _sb.Append("\\concat { \\hspace #").Append(Num(band.Left)).Append(' ');
        _sb.Append("\\override #'(box-padding . ").Append(Num(ProseBand.BoxPadding)).Append(") \\box\n");
        _sb.Append("    \\with-dimension #X #'(0 . ").Append(Num(band.TextWidth)).Append(") \\with-dimension #Y #'(")
            .Append(Num(-(band.Lines[^1].Baseline + down * band.Lines[^1].Scale))).Append(" . ")
            .Append(Num(up * band.Lines[0].Scale)).Append(") \\column {\n");
        double previous = 0;
        for (int i = 0; i < band.Lines.Length; i++)
        {
            var line = band.Lines[i];
            // Whatever a line stands lower than one baseline-skip — a paragraph's white, a
            // heading's and its larger box — is a \vspace: the column steps by the skip.
            double white = i == 0 ? 0 : line.Baseline - previous - skip;
            previous = line.Baseline;
            if (white > 1e-9)
                _sb.Append("      \\vspace #").Append(Num(white / skip)).Append('\n');
            _sb.Append("      ");
            bool centred = center && !line.InList;
            bool wrapped = line.InList;
            if (wrapped)
            {
                _sb.Append("\\concat { ");
                if (line.Marker is null)
                    _sb.Append("\\hspace #").Append(Num(line.Indent)).Append(' ');
                EmitMarker(line);
            }
            if (centred)
                _sb.Append("\\override #'(line-width . ").Append(Num(band.TextWidth)).Append(") \\fill-line { ");
            EmitTextLine(line);
            _sb.Append(centred || wrapped ? " }\n" : "\n");
        }
        _sb.Append("    } }\n");
    }
}