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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A hairpin on a LOWER staff of a system opens at its own staff's note column. The layout's
/// per-measure Items are the first staff's; every other staff reads its columns through
/// <see cref="LayoutUtilities.GetItemXOffset"/>, and the hairpin's note bounds did not until
/// 2026-10-16: a left hand's crescendo under a resting right hand opened at the upper staff's
/// column of the same index, or at the bar's start when the index ran past the upper staff's
/// items (the OMR's report of 2026-10-14, <c>hairpin-start-grandstaff-2026-10-14.lys</c>).
/// </summary>
[Trait("Category", "Unit")]
public class HairpinLowerStaffBoundTests
{
    private const string Book =
        "octave absolute\ntime 5/4\npart rh { clef treble }\npart lh { clef bass }\n" +
        "section S {\n" +
        "  rh { r1 r4 | r1 r4 | }\n" +
        "  lh { r2. <c, d,>16@decresc c,8 aes,16 r4 | " +
        "r16 g,16 <d, fis, a,>8 b,8 c16 b,16 g,4.@!decresc c,8 fis,,16 e,,16 e,,16 fis,,16@cresc | }\n" +
        "}\nform { S }\nscore o { grandStaff { staff rh \"Right\" staff lh \"Left\" } }\n";

    [Fact]
    public void AHairpin_OnTheLowerStaff_OpensAtItsOwnNote()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        var lh = score.StaffGroups[0].Staves[1].PrimaryVoice.Measures;
        var system = Assert.Single(layout.AllSystems);
        var hairpins = layout.HairpinLayouts.OrderBy(h => h.StartX).ToList();
        Assert.Equal(2, hairpins.Count);
        // Bar 1's decrescendo opens on <c, d,>16, the left hand's second item — the right hand's
        // second item is its r4, a beat later; bar 2's crescendo on fis,,16, the left hand's
        // twelfth, past every item of the right hand's.
        var bar1 = system.Measures.Single(m => m.MeasureIndex == 0);
        Assert.Equal(bar1.X + LayoutUtilities.GetItemXOffset(lh, 0, 1, bar1), hairpins[0].StartX, 6);
        var bar2 = system.Measures.Single(m => m.MeasureIndex == 1);
        Assert.Equal(bar2.X + LayoutUtilities.GetItemXOffset(lh, 1, 11, bar2), hairpins[1].StartX, 6);
        Assert.True(hairpins[1].StartX > bar2.X + bar2.Width / 2, "the crescendo opens late in bar 2, not at its start");
    }
}
