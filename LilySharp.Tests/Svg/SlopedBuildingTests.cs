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

using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests.Svg;

/// <summary>
/// Tests for sloped SkylineBuilding support in VerticalSkyline.
/// LILYPOND-REF: lily/skyline.cc SkylineBuilding struct
/// </summary>
[Trait("Category", "Unit")]
public class SlopedBuildingTests
{
    private const double Epsilon = 1e-6;

    [Fact]
    public void Building_HorizontalHeight_ReturnsConstant()
    {
        // Horizontal building: height = 5 everywhere
        var b = new SkylineBuilding(0, 10, 5);

        Assert.Equal(5, b.ValueAt(0), Epsilon);
        Assert.Equal(5, b.ValueAt(5), Epsilon);
        Assert.Equal(5, b.ValueAt(10), Epsilon);
    }

    [Fact]
    public void Building_SlopedHeight_ReturnsLinearInterpolation()
    {
        // Sloped building: from (0, 0) to (10, 10)
        var b = new SkylineBuilding(0, 0, 10, 10);

        Assert.Equal(0, b.ValueAt(0), Epsilon);
        Assert.Equal(5, b.ValueAt(5), Epsilon);
        Assert.Equal(10, b.ValueAt(10), Epsilon);
    }

    [Fact]
    public void Building_IntersectionX_ParallelLines()
    {
        // Parallel buildings - should return max of left X
        var b1 = new SkylineBuilding(0, 5, 5, 10);
        var b2 = new SkylineBuilding(5, 10, 10, 15);

        double ix = b1.Intersection(b2);
        Assert.Equal(5, ix, Epsilon);
    }

    [Fact]
    public void Building_IntersectionX_CrossingLines()
    {
        // b1: from (0, 0) to (10, 10) -> y = x
        // b2: from (0, 10) to (10, 0) -> y = -x + 10
        // Intersection: x = -x + 10 -> 2x = 10 -> x = 5
        var b1 = new SkylineBuilding(0, 0, 10, 10);
        var b2 = new SkylineBuilding(0, 10, 0, 10);

        double ix = b1.Intersection(b2);
        Assert.Equal(5, ix, Epsilon);
    }

    [Fact]
    public void Building_Above_HorizontalVsSloped()
    {
        // Horizontal at y=5
        var horizontal = new SkylineBuilding(0, 10, 5);
        // Sloped from y=0 to y=10
        var sloped = new SkylineBuilding(0, 0, 10, 10);

        // At x=3: sloped=3, horizontal=5 -> horizontal above
        Assert.True(horizontal.Above(sloped, 3));
        Assert.False(sloped.Above(horizontal, 3));

        // At x=7: sloped=7, horizontal=5 -> sloped above
        Assert.True(sloped.Above(horizontal, 7));
        Assert.False(horizontal.Above(sloped, 7));
    }

    [Fact]
    public void VerticalSkyline_FromSlope_CreatesCorrectBuilding()
    {
        // Create a sloped skyline (like a beam)
        var skyline = VerticalSkyline.FromSlope(0, 10, 100, 20, 2, VerticalDirection.Up);

        Assert.False(skyline.IsEmpty);
        Assert.Single(skyline.Buildings);

        var b = skyline.Buildings[0];
        Assert.Equal(0, b.Start, Epsilon);
        Assert.Equal(100, b.End, Epsilon);

        // SkylineBuilding.Height() returns the internal representation (+y_up for UP skyline)
        // VerticalSkyline.Height() returns the real Y-up coordinate
        Assert.Equal(10, skyline.Height(0), Epsilon);
        Assert.Equal(20, skyline.Height(100), Epsilon);
    }

    [Fact]
    public void VerticalSkyline_Merge_HorizontalBuildings()
    {
        // Two horizontal UP skylines at different top edges
        // s1: top y_up=10 (internal height = +10)
        // s2: top y_up=15 (internal height = +15)
        var s1 = VerticalSkyline.FromBox(0, 50, 0, 10, VerticalDirection.Up);
        var s2 = VerticalSkyline.FromBox(25, 75, 0, 15, VerticalDirection.Up);

        s1.Merge(s2);

        // For UP skyline, we keep the topmost (largest Y-up)
        // s2 (15) is higher than s1 (10).

        // At x=30 (overlap): keep y_up=15 (larger Y-up = topmost)
        Assert.Equal(15, s1.Height(30), Epsilon);

        // At x=10: only s1, y_up=10
        Assert.Equal(10, s1.Height(10), Epsilon);

        // At x=60: only s2, y_up=15
        Assert.Equal(15, s1.Height(60), Epsilon);
    }

    [Fact]
    public void VerticalSkyline_Merge_SlopedBuildings()
    {
        // Create two crossing sloped UP skylines
        // s1: rises from y_up=0 to y_up=10 (internal: 0 to 10)
        // s2: falls from y_up=10 to y_up=0 (internal: 10 to 0)
        var s1 = VerticalSkyline.FromSlope(0, 0, 10, 10, 0, VerticalDirection.Up);
        var s2 = VerticalSkyline.FromSlope(0, 10, 10, 0, 0, VerticalDirection.Up);

        s1.Merge(s2);

        // At x=5 (intersection), both have real height 5.
        // For UP skyline, "higher" means larger Y-up.
        // At x=2: s1=2, s2=8 -> s2 is higher -> result should be 8
        // At x=8: s1=8, s2=2 -> s1 is higher -> result should be 8
        Assert.True(s1.Height(2) >= 7.9, $"At x=2, expected ~8 (max of 2,8), got {s1.Height(2)}");
        Assert.True(s1.Height(8) >= 7.9, $"At x=8, expected ~8 (max of 8,2), got {s1.Height(8)}");
    }

    [Fact]
    public void VerticalSkyline_Distance_WithSlopedBuildings()
    {
        // UP skyline with a sloped roof from y_up=-10 to y_up=-20
        var up = VerticalSkyline.FromSlope(0, -10, 100, -20, 0, VerticalDirection.Up);

        // DOWN skyline with its floor at y_up=-50 (below the UP skyline)
        var down = VerticalSkyline.FromBox(0, 100, -50, -40, VerticalDirection.Down);

        // Distance = gap between the two skylines
        // UP skyline top edge: y_up=-10 to y_up=-20
        // DOWN skyline bottom edge: y_up=-50
        // Gap at x=0: -10 - (-50) = 40
        // Gap at x=100: -20 - (-50) = 30
        // LilyPond distance = UP.internalHeight + DOWN.internalHeight
        //   = (-10 to -20) + 50 = 40 to 30
        double dist = up.Distance(down);

        // Maximum distance is 40 (at x=0)
        Assert.True(dist >= 38 && dist <= 42, $"Expected ~40, got {dist}");
    }

    [Fact]
    public void Building_WithXRange_PreservesSlope()
    {
        // Original: from (0, 0) to (10, 10)
        var original = new SkylineBuilding(0, 0, 10, 10);

        // Extract middle portion
        var middle = original.WithRange(3, 7);

        Assert.Equal(3, middle.Start, Epsilon);
        Assert.Equal(7, middle.End, Epsilon);
        Assert.Equal(3, middle.ValueAt(3), Epsilon);
        Assert.Equal(7, middle.ValueAt(7), Epsilon);
        Assert.Equal(original.Slope, middle.Slope, Epsilon);
    }
}
