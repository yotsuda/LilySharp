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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// At a line start AFTER A BREAK the reprinted clef and key are LilyPond's broken copies, which
/// have no neighbours: their spacing boxes keep their own ink's Y, so a first note far below the
/// staff passes under the key and only its stem meets it. A meter (or key, or clef) CHANGE
/// written there is made at the break column and keeps its neighbours, so its box still
/// stretches over the first column.
/// MEASURED, LilyPond 2.26.0 on these books' twins (Lab sessions/p851/tie): the second line's
/// bar lines at the numbers below. With every box stretched Lily# drew the first one at 40.14
/// (the key reprinted); a meter change there is unchanged by the rule.
/// </summary>
public class BrokenLineStartBandTests
{
    private static string Book(string lineOneEnd, string lineTwoStart) => $$"""
        octave absolute
        key ees major
        part b {
          clef bass
          tuning bass5
          section A {
            c4 c c c | c1 | c1 | {{lineOneEnd}} | break
            {{lineTwoStart}} c c c | c1 | c1 | c1 |
          }
        }
        form main { A }
        score main { staff b  tab b }
        """;

    private static double[] SecondLineBars(string svg)
    {
        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        double staffLeft = Regex.Matches(svg,
                @"<line x1=""(?<x1>[\d.]+)"" y1=""(?<y>[\d.]+)"" x2=""(?<x2>[\d.]+)"" y2=""\k<y>"" stroke=""#000000"" stroke-width=""0\.100""")
            .Where(m => D(m.Groups["x2"].Value) - D(m.Groups["x1"].Value) > 8)
            .Min(m => D(m.Groups["x1"].Value));
        var bars = Regex.Matches(svg, @"<rect x=""(?<x>[\d.]+)"" y=""(?<y>[\d.]+)"" width=""0\.19""")
            .Select(m => (X: D(m.Groups["x"].Value) + 0.095, Y: D(m.Groups["y"].Value))).ToList();
        // Staff tops in order: line one's bass staff and tab, then line two's bass staff.
        double secondStaff = bars.Select(b => b.Y).Distinct().OrderBy(y => y).ElementAt(2);
        return bars.Where(b => b.Y == secondStaff).Select(b => b.X - staffLeft).OrderBy(x => x).ToArray();
    }

    [Theory]
    [InlineData("c1", "ees,,4", new[] { 39.74, 60.59, 81.44, 102.28 })]                 // the key reprinted
    [InlineData("time 2/4 c2", "time 4/4 ees,,4", new[] { 41.80, 61.96, 82.12, 102.28 })] // a meter change
    public void TheSecondLine_IsLaidOutAsLilyPondsIs(string lineOneEnd, string lineTwoStart, double[] lilyPond)
    {
        var bars = SecondLineBars(LiveRender.SvgFromRenderSpec(Book(lineOneEnd, lineTwoStart)));
        Assert.Equal(lilyPond.Length, bars.Length);
        for (int i = 0; i < lilyPond.Length; i++)
            Assert.InRange(bars[i], lilyPond[i] - 0.02, lilyPond[i] + 0.02);
    }
}
