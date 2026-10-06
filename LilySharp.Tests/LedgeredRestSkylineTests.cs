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
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rest pushed off the staff prints its ledgered glyph, and the staff's skyline is THAT
/// glyph's outline, ledger included (lily/stencil-integral.cc:535-563
/// add_named_glyph_segments over the rest's stencil). MEASURED, LilyPond 2.26.0 on the twin of
/// <c>test/hara-kiri</c> (Lab sessions/p850/hk3): in the third system a whole rest that the
/// lower staff's second voice pushes to +10 is where the two staves' skylines touch — upper
/// down -3.333 (a stem) against lower up 5.1 (the rest's ledger, 0.1 above its line) — so the
/// staves stand 8.433 + padding 1 apart and the gap between the upper staff's bottom line and
/// the lower staff's top line is 5.4333. Lily# read the unledgered glyph and drew 5.34.
/// </summary>
public class LedgeredRestSkylineTests
{
    [Fact]
    public void AWholeRestOffTheStaff_SpacesTheStavesByItsLedger()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "LilySharp.Tests", "Fixtures")))
            dir = System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string svg = LiveRender.SvgFromRenderSpec(System.IO.File.ReadAllText(
            System.IO.Path.Combine(dir!, "LilySharp.Tests", "Fixtures", "test", "hara-kiri.lys")));

        var lines = Regex.Matches(svg,
                @"<line[^>]*x1=""(?<x1>-?[\d.]+)""[^>]*y1=""(?<y>-?[\d.]+)""[^>]*x2=""(?<x2>-?[\d.]+)""[^>]*y2=""\k<y>""[^>]*stroke-width=""0\.100""")
            .Where(m => double.Parse(m.Groups["x2"].Value) - double.Parse(m.Groups["x1"].Value) > 60)
            .Select(m => double.Parse(m.Groups["y"].Value)).Distinct().OrderBy(y => y).ToList();
        // Systems: one staff, two, two, one — the third system's staves are lines 15..24.
        Assert.Equal(30, lines.Count);
        // The SVG speaks in hundredths, so each line carries ±0.005.
        Assert.InRange(lines[20] - lines[19], 5.4333 - 0.015, 5.4333 + 0.015);
        // ...and the second system, which has no rest off the staff, is LilyPond's 9.0 apart.
        Assert.InRange(lines[10] - lines[9], 5.0 - 0.015, 5.0 + 0.015);
    }
}
