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

using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A system-start delimiter spans LilyPond's <c>len</c> — the staves' StaffSymbol extents, half
/// a line thickness past the outer lines — not the line-to-line span
/// (lily/system-start-delimiter.cc:106-124 System_start_delimiter::print). MEASURED, LilyPond
/// 2.26.0 on the twin of the book below (Lab sessions/p850/br): the StaffGroup's bracket box
/// <c>len + 2 × overlap</c> = 22.1 + 0.09 = 22.19, the Score's SystemStartBar 22.1, over a
/// line-to-line span of 22.0. Lily# drew both 0.1 short until 2026-10-06.
/// </summary>
public class SystemStartDelimiterLengthTests
{
    private const string ThreeStaffGroup = """
        part a { clef treble }
        part b { clef treble }
        part c { clef bass }
        section A { a { c''1 | } b { e'1 | } c { c1 | } }
        form main { A }
        score main { staffGroup { staff a staff b staff c } }
        """;

    private static double VerticalStroke(string svg, string width)
    {
        var m = Regex.Match(svg,
            @"<line x1=""(?<x>-?[\d.]+)"" y1=""(?<y1>-?[\d.]+)"" x2=""\k<x>"" y2=""(?<y2>-?[\d.]+)""[^>]*stroke-width="""
            + Regex.Escape(width) + @"""");
        Assert.True(m.Success, $"no vertical stroke {width} wide");
        return double.Parse(m.Groups["y2"].Value) - double.Parse(m.Groups["y1"].Value);
    }

    [Fact]
    public void TheBracketAndTheSystemStartBar_SpanLilyPondsLength()
    {
        string svg = LiveRender.SvgFromRenderSpec(ThreeStaffGroup);
        Assert.Equal(22.19, VerticalStroke(svg, "0.450"), 2);   // SystemStartBracket
        Assert.Equal(22.10, VerticalStroke(svg, "0.160"), 2);   // SystemStartBar
    }
}
