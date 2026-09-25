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
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A grace group's staff offset is its OWN system's: every system is spaced against its own
/// staves' skylines, so a lower staff sits at a different depth below the system top from one
/// system to the next, and the renderer draws a grace BEAM and its stems from this offset
/// while the heads come from the system's staff. One score-wide value stood the beams of a
/// lower staff off their heads by the difference (scratch/SongsByChatGPT/
/// 01_glass_harbor_suite.lys page 6, owner report 2026-09-25).
/// </summary>
public class GraceStaffOffsetTests
{
    [Fact]
    public void AGraceOnALowerStaff_TakesItsSystemsStaffOffset()
    {
        var src = """
            octave absolute
            time 4/4
            part up
            part low { clef bass }
            section Main {
              up { c''4 c'' c'' c'' | c''1 | break c,4 c, c, c, | c,1 | }
              low { c4 c c c | grace { d16 e } c4 c c c | c4 c c c | grace { d16 e } c4 c c c | }
            }
            form main { ~Main }
            score main "x" { staff ~up  staff ~low }
            """;
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(src);
        var multi = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .CollectMultiStaff(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);

        SystemLayout SystemOf(int measure) => layout.Systems.Single(s => s.Measures.Any(m => m.MeasureIndex == measure));
        double LowStaffOffset(int measure) =>
            -SystemOf(measure).StaffGroups.SelectMany(g => g.Staves).Single(st => st.StaffIndex == 1).Y;

        // The net must bite: the two graces stand on two systems whose lower staff is at two
        // different depths (the second system's upper staff reaches far below its lines).
        Assert.NotSame(SystemOf(1), SystemOf(3));
        Assert.True(System.Math.Abs(LowStaffOffset(1) - LowStaffOffset(3)) > 0.5,
            $"the lower staff sits at {LowStaffOffset(1)} and {LowStaffOffset(3)} — the net does not bite");

        var graces = layout.GraceNoteLayouts.Where(g => g.StaffIndex == 1).ToList();
        Assert.Equal(2, graces.Count);
        foreach (var g in graces)
            Assert.Equal(LowStaffOffset(g.MeasureIndex), g.StaffYOffset, 6);
    }
}
