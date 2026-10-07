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
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A styled head is spaced by its own glyph: Note_spacing's left_head_end and the column's
/// extent read the stencil the head draws (a black slash 1.7152 wide, a whole slash 3.0152),
/// and a column closing onto a bar line averages its voices' wishes as every other column does
/// (spring.cc merge_springs). MEASURED, LilyPond 2.26.0 on this book's twin (Lab
/// sessions/p851/slash): the bars are 12.02, 13.91, 17.42, 9.38 wide. Lily# priced every
/// slash as a plain 1.3042 head and drew them 11.02, 12.99, 16.09, 8.85.
/// </summary>
public class StyledHeadSpacingTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part g { }
        part b { clef bass }
        section A {
          g { /4 4 4 4 | /2 2 | /4. 8 /2 | /8 8 8 8 /2 | /1 | }
          b { c4 d e f | g2 a | c4. d8 e2 | c8 d e f g2 | c1 | }
        }
        form main { A }
        score main { staff g  staff b }
        """;

    private static readonly double[] LilyPond = [12.02, 13.91, 17.42, 9.38];

    [Fact]
    public void SlashBars_AreAsWideAsLilyPondDrawsThem()
    {
        string svg = LiveRender.SvgFromRenderSpec(Book);
        var bars = Regex.Matches(svg,
                @"<rect x=""(?<x>[\d.]+)"" y=""(?<y>[\d.]+)"" width=""0\.19"" height=""4\.00""")
            .Select(m => (X: double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture)))
            .ToList();
        double top = bars.Min(b => b.Y);
        var xs = bars.Where(b => b.Y == top).Select(b => b.X).OrderBy(x => x).ToList();
        Assert.Equal(LilyPond.Length + 1, xs.Count);
        for (int i = 0; i < LilyPond.Length; i++)
            Assert.True(Math.Abs(xs[i + 1] - xs[i] - LilyPond[i]) <= 0.05,
                $"bar {i + 2}: Lily# {xs[i + 1] - xs[i]:F2}, LilyPond {LilyPond[i]:F2}");
    }
}
