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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every page after the first prints its number in its header, as LilyPond's default
/// oddHeaderMarkup / evenHeaderMarkup do, and the header takes its height from the page: the
/// breaker prices the band less it and the first system is floored under it.
/// MEASURED, LilyPond 2.26.0 on test/multi-page-vertical's twin (Lab sessions/p851/ws and
/// p851/pn): "2" at the left margin and "3" ending at the right one, both baselines at 7.2394;
/// the first staff of page 2 at 10.545 and of page 3 at 10.578, where Lily# drew 9.72 on both
/// with no number.
/// </summary>
public class PageNumberTests
{
    private static string Book()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "LilySharp.Tests", "Fixtures")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, "LilySharp.Tests", "Fixtures", "test", "multi-page-vertical.lys"));
    }

    private static string[] Pages(string svg) => svg.Split("<g class=\"page\"").Skip(1).ToArray();

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The page's number: the one plain 2.2-em text that is all digits.</summary>
    private static Match[] Numbers(string page) => Regex.Matches(page,
            @"<text x=""(?<x>[\d.]+)"" y=""(?<y>[\d.]+)"" font-size=""(?<em>[\d.]+)""(?<rest>[^>]*)>(?<n>\d+)</text>")
        .Where(m => !m.Groups["rest"].Value.Contains("font-weight")).ToArray();

    [Fact]
    public void PagesTwoAndOn_PrintTheirNumber_WhereLilyPondDoes()
    {
        var pages = Pages(LiveRender.SvgFromRenderSpec(Book()));
        Assert.Equal(3, pages.Length);
        Assert.Empty(Numbers(pages[0]));

        var two = Assert.Single(Numbers(pages[1]));
        Assert.Equal("2", two.Groups["n"].Value);
        Assert.Equal(8.54, D(two.Groups["x"].Value), 2);           // the left margin, start-anchored
        Assert.DoesNotContain("text-anchor", two.Groups["rest"].Value);
        Assert.Equal(7.24, D(two.Groups["y"].Value), 2);

        var three = Assert.Single(Numbers(pages[2]));
        Assert.Equal("3", three.Groups["n"].Value);
        Assert.Equal(110.97, D(three.Groups["x"].Value), 2);       // the right margin, end-anchored
        Assert.Contains("text-anchor=\"end\"", three.Groups["rest"].Value);
        Assert.Equal(7.24, D(three.Groups["y"].Value), 2);
    }

    [Fact]
    public void TheHeader_FloorsThePagesFirstSystem()
    {
        var pages = Pages(LiveRender.SvgFromRenderSpec(Book()));
        double FirstStaffTop(string page) => Regex.Matches(page,
                @"<line x1=""(?<x1>[\d.]+)"" y1=""(?<y>[\d.]+)"" x2=""(?<x2>[\d.]+)"" y2=""\k<y>"" stroke=""#000000"" stroke-width=""0\.100""")
            .Where(m => D(m.Groups["x2"].Value) - D(m.Groups["x1"].Value) > 8)
            .Min(m => D(m.Groups["y"].Value));
        Assert.InRange(FirstStaffTop(pages[1]), 10.545 - 0.015, 10.545 + 0.015);
        Assert.InRange(FirstStaffTop(pages[2]), 10.578 - 0.015, 10.578 + 0.015);
    }

    [Fact]
    public void AHeaderStep_ReachesThePageNumber()
    {
        // The page number has no fonts { } key of its own; the header group reaches it, and
        // `step +6` is a doubling.
        string book = Book();
        var plain = Numbers(Pages(LiveRender.SvgFromRenderSpec(book))[1]).Single();
        var stepped = Numbers(Pages(LiveRender.SvgFromRenderSpec("fonts { header step +6 }\n" + book))[1]).Single();
        Assert.Equal(D(plain.Groups["em"].Value) * 2, D(stepped.Groups["em"].Value), 1);
    }
}
