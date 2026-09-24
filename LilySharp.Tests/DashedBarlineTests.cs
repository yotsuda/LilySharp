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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The dashed bar line (<c>!</c>) is LilyPond's: one dash centred on every staff line, 0.6 of
/// a staff space tall (BarLine's <c>gap</c> 0.4), the outer two cut at the staff's edge plus
/// half a line thickness. Until session 561 the dashes ran 0.67 on / 0.33 off from the TOP of
/// the bar, so only the first straddled a line.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/bar-line.scm:512-553 make-dashed-bar-line;
/// scm/define-grobs.scm:268-276 BarLine allow-span-bar … gap 0.4.
/// MEASURED on 2.26.0 (Lab sessions/p561/dashed-lp.svg, the fixture's twin): a five-line
/// staff's dashes are 0.35 / 0.60 / 0.60 / 0.60 / 0.35 tall, the inner three at ±0.30 about
/// their lines. Poison: the old top-down loop reddens both facts (five dashes of 0.67 that
/// meet no line but the first; a tab's six of 0.67 instead of 0.9).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DashedBarlineTests
{
    private static string Book(string render, string part = "part m { clef treble }")
        => part + "\nsection A { m { c4 d e f ! g4 a b c' | } }\n"
           + "form main { ~A }\nscore main { " + render + " }\n";

    /// <summary>The page the score block asks for (a `tab m` is a tab staff), not
    /// LiveRender's plain staff.</summary>
    private static string Svg(string book)
        => SvgGenerator.Generate(SyntaxTree.Parse(book), new SvgRenderOptions { EmbedFont = false });

    /// <summary>Every 0.19-wide box (the thin bar's width) shorter than a staff space, as
    /// (x, top, height), page Y-down.</summary>
    private static (double X, double Top, double Height)[] Dashes(string svg)
        => Regex.Matches(svg, @"<rect x=""([\d.]+)"" y=""([\d.]+)"" width=""0\.19"" height=""(0\.\d+)""")
            .Cast<Match>()
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToArray();

    /// <summary>The distinct Y of the horizontal staff lines of the given thickness.</summary>
    private static double[] LineYs(string svg, string strokeWidth)
        => Regex.Matches(svg, @"<line x1=""[\d.]+"" y1=""([\d.]+)"" x2=""[\d.]+"" y2=""\1"" stroke=""#000000"" stroke-width=""" + strokeWidth + @"""")
            .Cast<Match>()
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(y => y).ToArray();

    [Fact]
    public void OnAFiveLineStaff_OneDashPerLine_TheInnerOnesCentred()
    {
        string svg = Svg(Book("staff m"));
        var lines = LineYs(svg, "0.100");
        Assert.Equal(5, lines.Length);
        var dashes = Dashes(svg).OrderBy(d => d.Top).ToArray();
        Assert.Equal(5, dashes.Length);
        Assert.True(dashes.All(d => d.X == dashes[0].X), "one bar, one X");

        // The outer dashes: 0.05 outside the staff to 0.30 inside (0.35 tall).
        Assert.Equal(lines[0] - 0.05, dashes[0].Top, 2);
        Assert.Equal(0.35, dashes[0].Height, 2);
        Assert.Equal(lines[4] - 0.30, dashes[4].Top, 2);
        Assert.Equal(0.35, dashes[4].Height, 2);
        // The inner dashes: ±0.30 about their lines.
        for (int i = 1; i <= 3; i++)
        {
            Assert.Equal(0.60, dashes[i].Height, 2);
            Assert.Equal(lines[i], dashes[i].Top + dashes[i].Height / 2, 2);
        }
    }

    /// <summary>A tab staff's dashes step by ITS line spacing (1.5): 0.9 tall inside,
    /// 0.5 at the edges — the is-span / staff-space distinction of the source.</summary>
    [Fact]
    public void OnATabStaff_TheDashesFollowTheStringSpacing()
    {
        string svg = Svg(Book("tab m", "part m { instrument guitar }"));
        var dashes = Dashes(svg).OrderBy(d => d.Top).ToArray();
        Assert.Equal(6, dashes.Length);
        static double Centre((double X, double Top, double Height) d) => d.Top + d.Height / 2;
        // The inner four: 0.9 tall, centred a string (1.5) apart.
        for (int i = 1; i <= 4; i++)
            Assert.Equal(0.90, dashes[i].Height, 2);
        for (int i = 2; i <= 4; i++)
            Assert.Equal(1.5, Centre(dashes[i]) - Centre(dashes[i - 1]), 2);
        // The outer two: cut at the string plus half a line thickness, so 0.5 tall and NOT
        // centred on their string — the top one starts 0.05 above the first string.
        Assert.Equal(0.50, dashes[0].Height, 2);
        Assert.Equal(0.50, dashes[5].Height, 2);
        Assert.Equal(Centre(dashes[1]) - 1.5 - 0.05, dashes[0].Top, 2);
        Assert.Equal(Centre(dashes[4]) + 1.5 + 0.05, dashes[5].Top + dashes[5].Height, 2);
    }
}
