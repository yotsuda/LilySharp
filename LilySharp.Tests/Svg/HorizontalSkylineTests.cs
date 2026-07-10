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
/// Tests for sloped SkylineBuilding support in HorizontalSkyline.
/// LILYPOND-REF: lily/skyline.cc Building struct (horizontal axis variant)
/// </summary>
[Trait("Category", "Unit")]
public class HorizontalSkylineTests
{
    private const double Epsilon = 1e-6;

    [Fact]
    public void HorizontalBuilding_VerticalEdge_ReturnsConstant()
    {
        // Vertical building: x = 5 everywhere
        var b = new SkylineBuilding(0, 10, 5);

        Assert.Equal(5, b.ValueAt(0), Epsilon);
        Assert.Equal(5, b.ValueAt(5), Epsilon);
        Assert.Equal(5, b.ValueAt(10), Epsilon);
    }

    [Fact]
    public void HorizontalBuilding_SlopedEdge_ReturnsLinearInterpolation()
    {
        // Sloped building: from (y=0, x=0) to (y=10, x=10)
        var b = new SkylineBuilding(0, 0, 10, 10);

        Assert.Equal(0, b.ValueAt(0), Epsilon);
        Assert.Equal(5, b.ValueAt(5), Epsilon);
        Assert.Equal(10, b.ValueAt(10), Epsilon);
    }

    [Fact]
    public void HorizontalBuilding_IntersectionY_CrossingLines()
    {
        // b1: from (y=0, x=0) to (y=10, x=10) -> x = y
        // b2: from (y=0, x=10) to (y=10, x=0) -> x = -y + 10
        // Intersection: y = -y + 10 -> 2y = 10 -> y = 5
        var b1 = new SkylineBuilding(0, 0, 10, 10);
        var b2 = new SkylineBuilding(0, 10, 0, 10);

        double iy = b1.Intersection(b2);
        Assert.Equal(5, iy, Epsilon);
    }

    [Fact]
    public void HorizontalSkyline_FromBox_CreatesCorrectBuilding()
    {
        var right = HorizontalSkyline.FromBox(0, 100, 10, 50, HorizontalDirection.Right);
        var left = HorizontalSkyline.FromBox(0, 100, 10, 50, HorizontalDirection.Left);

        Assert.Single(right.Buildings);
        Assert.Single(left.Buildings);

        // RIGHT skyline stores xRight = 50
        Assert.Equal(50, right.X(50), Epsilon);

        // LEFT skyline stores -xLeft = -10, returns real coordinate 10
        Assert.Equal(10, left.X(50), Epsilon);
    }

    [Fact]
    public void HorizontalSkyline_Distance_VerticalBuildings()
    {
        // RIGHT skyline at x=20
        var right = HorizontalSkyline.FromBox(0, 100, 0, 20, HorizontalDirection.Right);
        // LEFT skyline at x=50
        var left = HorizontalSkyline.FromBox(0, 100, 50, 100, HorizontalDirection.Left);

        // Gap = 50 - 20 = 30
        // Distance = right.internal + left.internal = 20 + (-50) = -30...
        // Wait, LilyPond convention: RIGHT stores +xRight, LEFT stores -xLeft
        // Distance = xRight + (-xLeft) = 20 + (-50) = -30 (overlap!)
        double dist = right.Distance(left);

        // This indicates overlap of 30 pixels
        Assert.Equal(-30, dist, Epsilon);
    }

    [Fact]
    public void HorizontalSkyline_Distance_NoOverlap()
    {
        // RIGHT skyline at x=20
        var right = HorizontalSkyline.FromBox(0, 100, 0, 20, HorizontalDirection.Right);
        // LEFT skyline at x=10
        var left = HorizontalSkyline.FromBox(0, 100, 10, 100, HorizontalDirection.Left);

        // Gap = 10 - 20 = -10 (right is to the right of left's left edge)
        // Distance = 20 + (-10) = 10
        double dist = right.Distance(left);

        Assert.Equal(10, dist, Epsilon);
    }

    [Fact]
    public void HorizontalSkyline_Distance_SlopedBuildings()
    {
        // RIGHT skyline with sloped edge: y=0 at x=10, y=100 at x=20
        var right = HorizontalSkyline.FromSlope(0, 10, 100, 20, HorizontalDirection.Right);
        // LEFT skyline at x=50
        var left = HorizontalSkyline.FromBox(0, 100, 50, 100, HorizontalDirection.Left);

        // At y=0: right=10, left stores -50, dist = 10 + (-50) = -40
        // At y=100: right=20, left stores -50, dist = 20 + (-50) = -30
        // Max distance (least overlap) = -30
        double dist = right.Distance(left);

        Assert.True(dist >= -31 && dist <= -29, $"Expected ~-30, got {dist}");
    }

    [Fact]
    public void HorizontalBuilding_WithYRange_PreservesSlope()
    {
        var original = new SkylineBuilding(0, 0, 10, 10);
        var middle = original.WithRange(3, 7);

        Assert.Equal(3, middle.Start, Epsilon);
        Assert.Equal(7, middle.End, Epsilon);
        Assert.Equal(3, middle.ValueAt(3), Epsilon);
        Assert.Equal(7, middle.ValueAt(7), Epsilon);
    }
}
