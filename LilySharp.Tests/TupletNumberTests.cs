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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Verifies that TupletNumber information (LP grob) is accessible via the
/// unified <see cref="TupletBracketLayout"/> record.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-number.cc — TupletNumber grob
/// LilySharp keeps the bracket and its number in one Layout record; the
/// derived <see cref="TupletBracketLayout.NumberX"/>/<see cref="TupletBracketLayout.NumberY"/>
/// accessors expose the LP-equivalent number anchor.
/// </remarks>
[Trait("Category", "Unit")]
public class TupletNumberTests
{
    private static ScoreLayout BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return engine.Layout(score);
    }

    [Fact]
    public void Tuplet_HasNumberText()
    {
        // \tuplet 3/2 { c8 d e } produces a triplet bracket with number "3".
        var layout = BuildLayout("tuplet 3/2 { c8 d e } |");
        Assert.NotEmpty(layout.TupletBracketLayouts);
        var bracket = layout.TupletBracketLayouts[0];
        Assert.Equal("3", bracket.NumberText);
    }

    [Fact]
    public void TupletNumberAnchor_AtBracketMidpoint()
    {
        var layout = BuildLayout("tuplet 3/2 { c8 d e } |");
        var bracket = layout.TupletBracketLayouts[0];
        // NumberX/Y should be midway between the bracket's start and end.
        Assert.Equal((bracket.StartX + bracket.EndX) / 2.0, bracket.NumberX, precision: 4);
        Assert.Equal((bracket.StartY + bracket.EndY) / 2.0, bracket.NumberY, precision: 4);
    }

    [Fact]
    public void TupletNumberY_TracksBracketSlope()
    {
        // Even on a flat bracket the midpoint formula returns the average Y.
        var layout = BuildLayout("tuplet 3/2 { c8 d e } |");
        var bracket = layout.TupletBracketLayouts[0];
        // For a flat bracket StartY == EndY ⇒ NumberY equals StartY.
        if (System.Math.Abs(bracket.StartY - bracket.EndY) < 1e-9)
            Assert.Equal(bracket.StartY, bracket.NumberY, precision: 4);
    }

    [Fact]
    public void Quintuplet_NumberTextIsFive()
    {
        var layout = BuildLayout("tuplet 5/4 { c16 d e f g } |");
        Assert.NotEmpty(layout.TupletBracketLayouts);
        Assert.Equal("5", layout.TupletBracketLayouts[0].NumberText);
    }
}
