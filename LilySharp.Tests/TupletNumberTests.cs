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
        Assert.Equal((bracket.StartYUp + bracket.EndYUp) / 2.0, bracket.NumberYUp, precision: 4);
    }

    [Fact]
    public void TupletNumberY_TracksBracketSlope()
    {
        // Even on a flat bracket the midpoint formula returns the average Y.
        var layout = BuildLayout("tuplet 3/2 { c8 d e } |");
        var bracket = layout.TupletBracketLayouts[0];
        // For a flat bracket StartYUp == EndYUp ⇒ NumberYUp equals StartYUp.
        if (System.Math.Abs(bracket.StartYUp - bracket.EndYUp) < 1e-9)
            Assert.Equal(bracket.StartYUp, bracket.NumberYUp, precision: 4);
    }

    [Fact]
    public void Quintuplet_NumberTextIsFive()
    {
        var layout = BuildLayout("tuplet 5/4 { c16 d e f g } |");
        Assert.NotEmpty(layout.TupletBracketLayouts);
        Assert.Equal("5", layout.TupletBracketLayouts[0].NumberText);
    }

    /// <summary>
    /// A fully beamed tuplet's number sits centred on the INVISIBLE bracket: the beam's
    /// outer edge plus TupletBracket padding 1.1 — NOT stem tip + TupletNumber padding
    /// 0.5, and NOT a clearance/digit-height arithmetic. Measured six-digit in two musics
    /// on 2.26.0 (audit/lp-geometry staff.staff.beamed-tuplet-number: centre = beam lower
    /// edge 3.240 + 1.100). The 1.1 is pinned as the measured number, deliberately not
    /// read back from the engraver's constant.
    /// </summary>
    [Theory]
    [InlineData("tuplet 3/2 { c8 d e } |")]   // stems up — number above the beam
    [InlineData("tuplet 3/2 { b8 c' b } |")]  // stems down — number below the beam
    public void BeamedTupletNumber_CentresOnTheInvisibleBracket_BeamEdgePlusPadding(
        string source)
    {
        var layout = BuildLayout(source);
        var bracket = Assert.Single(layout.TupletBracketLayouts);
        Assert.False(bracket.ShowBracket); // beat-long, fully beamed: number only
        var beam = Assert.Single(layout.BeamLayouts);

        double edgeLeft = beam.OuterEdgeStaffSpaceAtX(beam.LeftX, bracket.IsStemUp);
        double edgeRight = beam.OuterEdgeStaffSpaceAtX(beam.RightX, bracket.IsStemUp);
        double padding = bracket.IsStemUp ? -1.1 : 1.1; // device down-positive
        // Device frame from the staff top (middle line = 2.0), reflected to stored Y-up.
        double expected = -((2.0 - edgeLeft) + padding + (2.0 - edgeRight) + padding) / 2.0;
        Assert.Equal(expected, bracket.NumberYUp, precision: 6);
    }
}
