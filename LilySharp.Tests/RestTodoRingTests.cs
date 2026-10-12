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
/// A <c>@todo</c> on a rest rings the rest where it is DRAWN — moved up or down by its voice,
/// by another voice's notes, or by its written pitch (owner's report 2026-10-12: an upper
/// voice's <c>r4.</c> was ringed at the middle line, under the rest). The ring reads the same
/// shift the rest is drawn by (ScoreLayout.GetRestShift).
/// </summary>
[Trait("Category", "Unit")]
public class RestTodoRingTests
{
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [Theory]
    // The upper voice's rest, raised over the lower voice's chord.
    [InlineData("time 6/8", "voice { r4.@todo(\"x\") s4 s8 } { bes8 <ges' bes' des''>4. bes8 } |")]
    // The lower voice's rest, lowered under the upper voice.
    [InlineData("time 4/4", "voice { c''2 d'' } { r2@todo(\"x\") g2 } |")]
    // A pitched rest, at the position written.
    [InlineData("time 4/4", "g''4@rest@todo(\"x\") c'2. |")]
    [InlineData("time 4/4", "c''1@rest@todo(\"x\") |")]
    public void TheRingStandsRoundTheRestAsDrawn(string time, string music)
    {
        string text = time + "\npart p { clef treble }\nsection A { p { " + music + " } }\nform { ~A }\nscore { staff p }";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(text), SvgRenderOptions.Preview());
        // The staff's contents are drawn in a group moved down by the system's offset; the
        // rings are overlay, in the page's own frame.
        var shift = Regex.Match(svg, @"<g transform=""translate\(0\.00,(-?[\d.]+)\)");
        double dy = shift.Success ? D(shift.Groups[1].Value) : 0;
        var rest = Regex.Match(svg, @"<text class=""music"" x=""(-?[\d.]+)"" y=""(-?[\d.]+)""[^>]*data-todo="""" data-pos=""(\d+)"">");
        Assert.True(rest.Success, "no ringed rest glyph in the SVG");
        double y = D(rest.Groups[2].Value) + dy;
        string pos = rest.Groups[3].Value;
        var ys = Regex.Matches(svg, $@"<path d=""M -?[\d.]+,(-?[\d.]+) C -?[\d.]+,(-?[\d.]+) -?[\d.]+,(-?[\d.]+) -?[\d.]+,(-?[\d.]+)""[^>]*data-todo="""" data-pos=""{pos}""/>")
            .SelectMany(m => Enumerable.Range(1, 4).Select(k => D(m.Groups[k].Value)))
            .ToList();
        Assert.NotEmpty(ys);
        double centre = (ys.Min() + ys.Max()) / 2;
        Assert.True(ys.Min() < y && y < ys.Max() && System.Math.Abs(centre - y) < 1.0,
            $"ring {ys.Min():F2}..{ys.Max():F2} (centre {centre:F2}) is not round the rest's origin at {y:F2}");
    }
}
