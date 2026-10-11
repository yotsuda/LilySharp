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
/// A <c>@todo</c> memo stands in its OWN staff's margin — over that staff, under it for
/// <c>.down</c> — not over the whole system (owner's request 2026-10-12), and the layout
/// opens the gap between two staves for it.
/// </summary>
[Trait("Category", "Unit")]
public class TodoStaffMarginTests
{
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The preview's memo baselines and the two staves' outer lines, in page Y (down).</summary>
    private static (double[] Memos, double UpperBottom, double LowerTop) Measure(string upper, string lower)
    {
        string text = $$"""
            part up { clef treble }
            part low { clef bass }
            section A { up { {{upper}} } low { {{lower}} } }
            form { ~A }
            score { staff up  staff low }
            """;
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(text), SvgRenderOptions.Preview());
        double dy = D(Regex.Match(svg, @"<g transform=""translate\(0\.00,([\d.]+)\)").Groups[1].Value);
        var lines = Regex.Matches(svg, @"<line x1=""([\d.]+)"" y1=""([\d.]+)"" x2=""([\d.]+)"" y2=""([\d.]+)"" stroke=""#000000"" stroke-width=""0\.1\d*""")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value && D(m.Groups[3].Value) - D(m.Groups[1].Value) > 10)
            .Select(m => D(m.Groups[2].Value) + dy).Distinct().OrderBy(y => y).ToArray();
        Assert.Equal(10, lines.Length);   // two five-line staves, one system
        var memos = Regex.Matches(svg, @"<text x=""[\d.]+"" y=""([\d.]+)"" font-size=""[\d.]+"" font-style=""italic"" fill=""#E53935""")
            .Select(m => D(m.Groups[1].Value)).ToArray();
        return (memos, lines[4], lines[5]);
    }

    [Theory]
    [InlineData("c''4 d e f |", "c4 d@todo(\"lower staff memo\") e f |")]          // over the lower staff
    [InlineData("c''4 d@todo(\"under the upper\").down e f |", "c4 d e f |")]     // under the upper one
    public void AMemoBetweenTwoStaves_StandsInTheirGap(string upper, string lower)
    {
        var (memos, upperBottom, lowerTop) = Measure(upper, lower);
        double memo = Assert.Single(memos);
        Assert.True(memo > upperBottom && memo < lowerTop,
            $"memo baseline {memo} is not between the staves ({upperBottom} .. {lowerTop})");
    }

    [Fact]
    public void AnUpperStaffsMemo_StillStandsOverTheSystem()
    {
        var (memos, upperBottom, _) = Measure("c''4 d@todo(\"upper memo\") e f |", "c4 d e f |");
        Assert.True(Assert.Single(memos) < upperBottom - 4, "the top staff's memo stands over it");
    }

    [Fact]
    public void ThreeMemosBetweenTheStaves_OpenTheGapForThemselves()
    {
        // A crowded gap: the layout must make room rather than let the memos climb over
        // the upper staff (TodoMemoBand.StaffBoxes in the staff's skyline).
        var (_, plainBottom, plainTop) = Measure("c''4 d e f |", "c4 d e f |");
        var (memos, upperBottom, lowerTop) = Measure("c''4 d e f |",
            "c'4@todo(\"one long memo here\") d@todo(\"and another long one\") e@todo(\"and a third\") f |");
        Assert.Equal(3, memos.Length);
        Assert.All(memos, m => Assert.True(m > upperBottom && m < lowerTop, $"memo {m} left the gap"));
        Assert.True(lowerTop - upperBottom > plainTop - plainBottom, "the gap did not open");
    }
}
