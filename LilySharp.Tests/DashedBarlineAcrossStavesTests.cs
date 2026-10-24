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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A dashed bar line (<c>!</c>) written in every staff of a score of two or more staves vanished
/// (owner's report 2026-10-24): the bar lines of the staves are folded into one per boundary
/// (<c>MeasureCollector.Stronger</c>), and the dashed type had no rank — it tied with None, which
/// starts the fold, so None won. One staff never folds, and drew it.
/// </summary>
[Trait("Category", "Unit")]
public class DashedBarlineAcrossStavesTests
{
    [Fact]
    public void EveryBarlineType_HasARank_AndTheDashedOneBeatsNoneAndThePlainOne()
    {
        foreach (var type in Enum.GetValues<BarlineType>())
            MeasureCollector.Stronger(BarlineType.None, type);   // throws for an unranked type
        Assert.Equal(BarlineType.Dashed, MeasureCollector.Stronger(BarlineType.None, BarlineType.Dashed));
        Assert.Equal(BarlineType.Dashed, MeasureCollector.Stronger(BarlineType.Dashed, BarlineType.None));
        Assert.Equal(BarlineType.Dashed, MeasureCollector.Stronger(BarlineType.Single, BarlineType.Dashed));
        Assert.Equal(BarlineType.Double, MeasureCollector.Stronger(BarlineType.Dashed, BarlineType.Double));
    }

    [Theory]
    [InlineData("staffGroup { staff p1 staff p2 }")]
    [InlineData("staff p1 staff p2")]
    public void ADashedBarInEveryStaff_StaysDashed(string score)
    {
        var tree = SyntaxTree.Parse(
            "section A {\n  p1 { r1 ! c1 }\n  p2 { c1 ! c1 }\n}\nform { A }\nscore { " + score + " }\n");
        var collected = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var staves = collected.StaffGroups.SelectMany(g => g.Staves).ToList();
        Assert.Equal(2, staves.Count);
        Assert.All(staves, s => Assert.Equal(BarlineType.Dashed, s.PrimaryVoice.Measures[0].EndBarline));
    }
}
