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
/// Two voices' dynamics on one column stack in LilyPond's outside-staff order — by each
/// DynamicLineSpanner's LEFT edge — and nothing else: no fixed step between them.
/// MEASURED, LilyPond 2.26.0 on this book's twin (Lab sessions/p851/dyn, the DynamicText's
/// origin relative to its VerticalAxisGroup once the skylines are built): the lower voice's p,
/// whose ink starts 0.10 left of the f's, sits by the staff at −4.708 and the upper voice's f
/// under it at −6.884. Lily# stacked in source order with a 2.0 step: f −4.55, p −6.78.
/// </summary>
public class VoiceDynamicsStackOrderTests
{
    private const string Book = """
        time 4/4
        part top { clef treble }
        part bot { clef alto octave 3 }
        section Main {
          top { c'4 d e f | voice { g'2@f a } { c2@p d }  | }
          bot { c'4 b a g | f2 e2 | }
        }
        form main { Main }
        score main { staff top  staff bot }
        """;

    [Fact]
    public void TheLowerVoicesP_SitsByTheStaff_AndTheFUnderIt()
    {
        string svg = LiveRender.SvgFromRenderSpec(Book);
        double middle = Regex.Matches(svg,
                @"<line x1=""[\d.]+"" y1=""(?<y>[\d.]+)"" x2=""[\d.]+"" y2=""\k<y>"" stroke=""#000000"" stroke-width=""0\.100""")
            .Select(m => double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(y => y).ElementAt(2);
        double Below(string text) => middle - Regex.Matches(svg,
                @"<text x=""[\d.]+"" y=""(?<y>[\d.]+)""[^>]*font-style=""italic""[^>]*>(?<t>\w+)</text>")
            .Where(m => m.Groups["t"].Value == text)
            .Select(m => double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture)).Single();

        Assert.Equal(-4.708, Below("p"), 1);
        Assert.Equal(-6.884, Below("f"), 1);
        Assert.InRange(Below("p") - -4.708, -0.015, 0.015);
        Assert.InRange(Below("f") - -6.884, -0.015, 0.015);
    }
}
