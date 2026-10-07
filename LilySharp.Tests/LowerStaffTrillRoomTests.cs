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
/// A trill above a LOWER staff is in that staff's skyline when the staves are spaced
/// (lily/axis-group-interface.cc:952-972 add_grobs_of_one_priority: a placed outside-staff
/// grob stays in its VerticalAxisGroup's skyline). MEASURED, LilyPond 2.26.0 on the twin of
/// <c>test/trillspan-lower-staff</c> (Lab sessions/p851): the staves stand 9.6 apart, top
/// line to top line, where the basic distance is 9.0; Lily# left them at 9.0 and the upper
/// staff's down-stems ran into the "tr".
/// </summary>
public class LowerStaffTrillRoomTests
{
    [Fact]
    public void ATrillAboveTheLowerStaff_OpensTheGapAsLilyPondDoes()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "LilySharp.Tests", "Fixtures")))
            dir = System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string svg = LiveRender.SvgFromRenderSpec(System.IO.File.ReadAllText(
            System.IO.Path.Combine(dir!, "LilySharp.Tests", "Fixtures", "test", "trillspan-lower-staff.lys")));

        var lines = Regex.Matches(svg,
                @"<line[^>]*x1=""(?<x1>-?[\d.]+)""[^>]*y1=""(?<y>-?[\d.]+)""[^>]*x2=""(?<x2>-?[\d.]+)""[^>]*y2=""\k<y>""[^>]*stroke-width=""0\.100""")
            .Where(m => double.Parse(m.Groups["x2"].Value) - double.Parse(m.Groups["x1"].Value) > 8)
            .Select(m => double.Parse(m.Groups["y"].Value)).Distinct().OrderBy(y => y).ToList();
        Assert.Equal(10, lines.Count);
        // The SVG speaks in hundredths: ±0.005 a line.
        Assert.InRange(lines[5] - lines[0], 9.6 - 0.015, 9.6 + 0.015);
    }
}
