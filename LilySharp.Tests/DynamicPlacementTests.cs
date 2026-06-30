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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// '@p.up' / '@f.down' force a dynamic above / below the staff (default is below).
/// Unlike articulation placement, this is a real engraver path: the above branch
/// computes a Y above the staff (clearing up-stems) and the below-staff stacker
/// leaves it alone.
/// </summary>
[Trait("Category", "Unit")]
public class DynamicPlacementTests
{
    private static MultiStaffScore Collect(string body)
    {
        var src =
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} }} }}\n" +
            "structure { S }\n" +
            "score \"o\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void UpQualifier_MarksDynamicAbove()
    {
        var dyns = Collect("c''4@f.up d''4@p.down e''4@mf").Dynamics
            .OrderBy(d => d.ItemIndex).ToList();
        Assert.Equal(3, dyns.Count);
        Assert.True(dyns[0].IsAbove);   // @f.up
        Assert.False(dyns[1].IsAbove);  // @p.down (explicit below)
        Assert.False(dyns[2].IsAbove);  // @mf (default below)
    }

    [Fact]
    public void Placement_OnHairpinTrigger_IsRejected_NotSilentlyDropped()
    {
        // cresc/decresc/dim drive a hairpin (always below); '.up'/'.down' is meaningless
        // there and must be flagged, not silently swallowed.
        var tree = SyntaxTree.Parse(
            "part m { clef treble } section S { m { c''4@p@cresc.up d e f@f } }\n" +
            "structure { S } score \"o\" { staff m }\n");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("cresc"));

        // A dynamic LEVEL placement is fine.
        Assert.False(SyntaxTree.Parse(
            "part m { clef treble } section S { m { c''4@f.up } }\n" +
            "structure { S } score \"o\" { staff m }\n").HasErrors);
    }

    [Fact]
    public void AboveDynamic_LaysOutHigherThanBelow()
    {
        var score = Collect("c''4@f.up c''4@mf");
        var layout = new LayoutEngine().Layout(score);

        var above = layout.DynamicLayouts.Single(d => d.IsAbove);
        var below = layout.DynamicLayouts.Single(d => !d.IsAbove);

        // Smaller Y = higher on the page.
        Assert.True(above.Y < below.Y,
            $"above dynamic (Y={above.Y}) should sit higher than below (Y={below.Y})");
    }

    [Fact]
    public void AboveDynamic_ClearsOtherAboveStaffGrobs()
    {
        // @f.up (dynamic, priority 250) and @mark.A (rehearsal mark, 1500) share a column.
        // The above-staff stacker must separate them — the higher-priority mark sits
        // ABOVE the dynamic (smaller Y), not overlapping it.
        var score = Collect("c''4@f.up@mark.A");
        var layout = new LayoutEngine().Layout(score);

        var dyn = layout.DynamicLayouts.Single(d => d.IsAbove);
        var mark = layout.MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.Rehearsal);

        Assert.True(mark.Y < dyn.Y,
            $"rehearsal mark (Y={mark.Y}) should sit above the above-dynamic (Y={dyn.Y})");
    }
}
