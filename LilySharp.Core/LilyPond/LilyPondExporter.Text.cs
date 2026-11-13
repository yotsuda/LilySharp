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
    /// white over it as a taller box.
    /// MEASURED 2026-11-12, LilyPond 2.26.0 (LilySharp-Lab sessions/p1226/md, t1tw2.ly against
    /// t1.lys): the composer's baseline to the first line 2.9705, a line to the next 3.0000,
    /// a paragraph to the next 4.5000, the last system's middle line to the closing text
    /// 12.0000 — the page's 2.95, 3.00, 4.50 and 12.00.
    /// LILYSHARP-OWN: the breaking, and the white between paragraphs. Not written yet: a
    /// size or a face the score gives the text (<c>fonts { md … }</c>).
    /// </remarks>
    private void EmitText(bool before)
    {
        if (_page is not { } page)
            return;
        var texts = page.Texts;
        var shape = ProseShape.Of(before ? texts.BeforeShape : texts.AfterShape, page.Paper.StaffSpaceMm);
        var band = ProseBand.Build(before ? texts.Before : texts.After,
            before ? texts.BeforePosition : texts.AfterPosition, page.TextMetrics, page.Paper.ContentWidth, shape);
        if (band is null)
            return;
        double skip = ProseBand.Skip(page.TextMetrics);
        double up = ProseBand.LineUp(page.TextMetrics), down = ProseBand.LineDown(page.TextMetrics);
        _sb.Append(before ? "" : "\n").Append("\\markuplist \\column-lines {\n");
        if (band.Frame is { } frame)
        {
            EmitBoxedText(band, frame, shape.Center, skip, up, down);
            _sb.Append(before ? "}\n\n" : "}\n");
            return;
        }
        double previous = 0;
        for (int i = 0; i < band.Lines.Length; i++)
        {
            var line = band.Lines[i];
            _sb.Append("  ");
            double white = i == 0 ? 0 : line.Baseline - previous - skip;
            if (white > 1e-9)
                _sb.Append("\\with-dimension #Y #'(").Append(Num(-down)).Append(" . ").Append(Num(up + white)).Append(") ");
            previous = line.Baseline;
            // An indented line begins after its white; a centred one is LilyPond's to centre
            // in the text's width (\fill-line of one markup), so that its own measure of the
            // words, not the page's, finds the middle.
            bool wrapped = band.Left > 1e-9 || shape.Center;
            if (wrapped)
            {
                _sb.Append("\\concat { ");
                if (band.Left > 1e-9)
                    _sb.Append("\\hspace #").Append(Num(band.Left)).Append(' ');
                if (shape.Center)
                    _sb.Append("\\override #'(line-width . ").Append(Num(band.TextWidth)).Append(") \\fill-line { ");
            }
            EmitTextLine(line);
            if (wrapped)
                _sb.Append(shape.Center ? " } }" : " }");
            _sb.Append('\n');
        }
        _sb.Append(before ? "}\n\n" : "}\n");
    }

    private static string Num(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>One line's words: a string, or its pieces in their weights and slants.</summary>
    private void EmitTextLine(ProseLine line)
    {
        if (line.Pieces.Length == 1 && line.Pieces[0].Style == FontStyle.Regular)
        {
            _sb.Append('"').Append(Escape(line.Pieces[0].Text)).Append('"');
            return;
        }
        _sb.Append("\\concat {");
        foreach (var piece in line.Pieces)
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
        // The white the page keeps above and below the frame (ProseBand.BoxMargin): the line's
        // own extent, taller than the box by that much each way.
        _sb.Append("  \\with-dimension #Y #'(").Append(Num(-band.Down)).Append(" . ").Append(Num(band.Up)).Append(") ");
        _sb.Append("\\concat { \\hspace #").Append(Num(band.Left)).Append(' ');
        _sb.Append("\\override #'(box-padding . ").Append(Num(ProseBand.BoxPadding)).Append(") \\box\n");
        _sb.Append("    \\with-dimension #X #'(0 . ").Append(Num(band.TextWidth)).Append(") \\with-dimension #Y #'(")
            .Append(Num(-(band.Lines[^1].Baseline + down))).Append(" . ").Append(Num(up)).Append(") \\column {\n");
        double previous = 0;
        for (int i = 0; i < band.Lines.Length; i++)
        {
            var line = band.Lines[i];
            double white = i == 0 ? 0 : line.Baseline - previous - skip;
            previous = line.Baseline;
            if (white > 1e-9)
                _sb.Append("      \\vspace #").Append(Num(white / skip)).Append('\n');
            _sb.Append("      ");
            if (center)
                _sb.Append("\\override #'(line-width . ").Append(Num(band.TextWidth)).Append(") \\fill-line { ");
            EmitTextLine(line);
            _sb.Append(center ? " }\n" : "\n");
        }
        _sb.Append("    } }\n");
    }
}