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
/// The preview's page carries one invisible box per bar per staff (rect.bar-staff) with where
/// that bar is written (data-start): a click on the bar in the preview takes the editor there
/// (owner request 2026-10-14). The place is the bar's first note, rest or chord — not the
/// barline before it, and in a section's first bar not the section's own start.
/// </summary>
[Trait("Category", "Unit")]
public class BarStaffBoxTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part rh { }
        part lh { clef bass }
        section A {
          partial 4
          rh { g'4 | c''1 | d''2 e'' | }
          lh { r4 | c1 | d1 | }
        }
        section B {
          rh { f''1 | }
          lh {
            f,2 g, | }
        }
        form { A B }
        score { staff rh staff lh }
        """;

    private static readonly Regex Box = new(
        @"<rect class=""bar-staff"" data-start=""(\d+)"" x=""([^""]+)"" y=""([^""]+)"" width=""([^""]+)"" height=""([^""]+)""");

    [Fact]
    public void EachBarOfEachStaff_GoesToItsFirstNote()
    {
        string preview = SvgGenerator.Generate(SyntaxTree.Parse(Book), SvgRenderOptions.Preview());
        var boxes = Box.Matches(preview).Cast<Match>().ToList();
        Assert.Equal(8, boxes.Count);   // four bars, two staves

        // The upper staff stands higher on the page (a smaller device y).
        double top = boxes.Min(b => Num(b, 3));
        string Written(Match b) => Book[int.Parse(b.Groups[1].Value)..].TrimStart().Split(' ', '\n', '\r')[0];
        Assert.Equal(new[] { "g'4", "c''1", "d''2", "f''1" },
            boxes.Where(b => Num(b, 3) == top).Select(Written));
        Assert.Equal(new[] { "r4", "c1", "d1", "f,2" },
            boxes.Where(b => Num(b, 3) != top).Select(Written));

        // A staff tall, and as wide as the bar's own box.
        Assert.All(boxes, b => Assert.Equal(4, Num(b, 5), 3));
        var bars = Regex.Matches(preview, @"<rect class=""bar-box"" data-bar=""\d+"" x=""([^""]+)"" y=""[^""]+"" width=""([^""]+)""");
        Assert.Equal(bars.Select(m => m.Groups[1].Value).OrderBy(x => x),
            boxes.Where(b => Num(b, 3) == top).Select(b => b.Groups[2].Value).OrderBy(x => x));
    }

    [Fact]
    public void AnExportedPage_HasNone()
        => Assert.DoesNotContain("bar-staff", SvgGenerator.Generate(SyntaxTree.Parse(Book), SvgRenderOptions.Default));

    private static double Num(Match m, int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
}
