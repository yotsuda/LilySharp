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
/// A one-line staff (<c>as lines 1</c>, LilyPond's RhythmicStaff) engraves no clef and no key,
/// so it books neither: its meter stands where LilyPond's left edge puts a meter with nothing in
/// front of it, extra-space 1.0 in.
/// MEASURED, LilyPond 2.26.0 on this book's twin (Lab sessions/p851/ws, corpora oneline-rest):
/// the first bar line 14.64 from the staff's left end. Lily# drew no clef there but booked one,
/// 18.52, and then started the meter at the clef's 0.8, 14.45.
/// </summary>
public class OneLineStaffPrefixTests
{
    private const string Book = """
        part melody {
          section A { r1 | r2 r4 r8 r16 r32 r64 r | }
        }
        form { A }
        score {
          staff melody as lines 1
        }
        """;

    [Fact]
    public void TheFirstBarLine_StandsWhereLilyPondsDoes()
    {
        string svg = LiveRender.SvgFromRenderSpec(Book);
        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        double staffLeft = Regex.Matches(svg,
                @"<line x1=""(?<x1>[\d.]+)"" y1=""(?<y>[\d.]+)"" x2=""(?<x2>[\d.]+)"" y2=""\k<y>"" stroke=""#000000"" stroke-width=""0\.100""")
            .Where(m => D(m.Groups["x2"].Value) - D(m.Groups["x1"].Value) > 8)
            .Min(m => D(m.Groups["x1"].Value));
        double firstBar = Regex.Matches(svg, @"<rect x=""(?<x>[\d.]+)"" y=""[\d.]+"" width=""0\.19""")
            .Select(m => D(m.Groups["x"].Value) + 0.095).Min();
        Assert.InRange(firstBar - staffLeft, 14.64 - 0.02, 14.64 + 0.02);
    }
}
