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
        var band = ProseBand.Build(before ? texts.Before : texts.After,
            before ? texts.BeforePosition : texts.AfterPosition, page.TextMetrics, page.Paper.ContentWidth);
        if (band is null)
            return;
        double skip = ProseBand.Skip(page.TextMetrics);
        double up = ProseBand.LineUp(page.TextMetrics), down = ProseBand.LineDown(page.TextMetrics);
        _sb.Append(before ? "" : "\n").Append("\\markuplist \\column-lines {\n");
        double previous = 0;
        for (int i = 0; i < band.Lines.Length; i++)
        {
            var line = band.Lines[i];
            _sb.Append("  ");
            double white = i == 0 ? 0 : line.Baseline - previous - skip;
            if (white > 1e-9)
                _sb.Append("\\with-dimension #Y #'(").Append(Num(-down)).Append(" . ").Append(Num(up + white)).Append(") ");
            previous = line.Baseline;
            if (line.Pieces.Length == 1 && line.Pieces[0].Style == FontStyle.Regular)
            {
                _sb.Append('"').Append(Escape(line.Pieces[0].Text)).Append("\"\n");
                continue;
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
            _sb.Append(" }\n");
        }
        _sb.Append(before ? "}\n\n" : "}\n");

        static string Num(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
    }
}