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

namespace LilySharp.Tests;

/// <summary>
/// HorizontalSkyline keeps a building LIST (Merge concatenates); these tests
/// pin that all queries still return ENVELOPE values — a shadowed building
/// must never influence Distance or X. LILYPOND-REF: lily/skyline.cc
/// internal_distance / height semantics.
/// </summary>
[Trait("Category", "Unit")]
public class HorizontalSkylineTests
{
    [Fact]
    public void Distance_IsExactForBoxes()
    {
        // prev item: right edge at x=2 over y∈[-2,2]
        var right = HorizontalSkyline.FromBox(-2, 2, 0, 2, HorizontalDirection.Right);
        // next item: left edge at x=-1 (extends 1 left of its reference) over y∈[-1,1]
        var left = HorizontalSkyline.FromBox(-1, 1, -1, 3, HorizontalDirection.Left);

        // Required separation = rightExtent(2) - leftExtent(-1) = 3
        Assert.Equal(3.0, right.Distance(left), 6);
    }

    [Fact]
    public void Distance_IgnoresShadowedBuilding()
    {
        var right = HorizontalSkyline.FromBoxes(new[]
        {
            (-2.0, 2.0, 0.0, 2.0),   // envelope: right edge x=2
            (-1.0, 1.0, 0.0, 0.5),   // fully shadowed (smaller extent, inside y-range)
        }, HorizontalDirection.Right);
        var left = HorizontalSkyline.FromBox(-2, 2, 0, 1, HorizontalDirection.Left);

        var rightNoShadow = HorizontalSkyline.FromBox(-2, 2, 0, 2, HorizontalDirection.Right);

        Assert.Equal(rightNoShadow.Distance(left), right.Distance(left), 6);
    }

    [Fact]
    public void Distance_SlopedBuilding_MaxAtOverlapEndpoint()
    {
        // Sloped right edge: x=0 at y=-2 rising to x=4 at y=2 (e.g. a beam).
        var right = HorizontalSkyline.FromSlope(-2, 0, 2, 4, HorizontalDirection.Right);
        var left = HorizontalSkyline.FromBox(0, 2, 0, 1, HorizontalDirection.Left);

        // Overlap y∈[0,2]; sum is linear, max at y=2: x=4, left extent 0 → 4.
        Assert.Equal(4.0, right.Distance(left), 6);
    }

    [Fact]
    public void X_ReturnsOutermostCoveringBuilding()
    {
        var right = HorizontalSkyline.FromBoxes(new[]
        {
            (-2.0, 2.0, 0.0, 0.5),   // small
            (-1.0, 1.0, 0.0, 2.0),   // outer at y=0
        }, HorizontalDirection.Right);

        Assert.Equal(2.0, right.X(0), 6);   // envelope, not first-in-list
        Assert.Equal(0.5, right.X(1.5), 6); // only the tall-thin building covers
    }

    [Fact]
    public void X_LeftSkyline_ReturnsOutermostLeftEdge()
    {
        var left = HorizontalSkyline.FromBoxes(new[]
        {
            (-2.0, 2.0, -0.5, 3.0),  // left edge -0.5
            (-1.0, 1.0, -2.0, 3.0),  // outer (further left) at y=0
        }, HorizontalDirection.Left);

        Assert.Equal(-2.0, left.X(0), 6);
        Assert.Equal(-0.5, left.X(1.5), 6);
    }

    [Fact]
    public void Merge_ThenDistance_EqualsEnvelope()
    {
        var a = HorizontalSkyline.FromBox(-2, 0, 0, 1, HorizontalDirection.Right);
        var b = HorizontalSkyline.FromBox(0, 2, 0, 3, HorizontalDirection.Right);
        a.Merge(b);

        var left = HorizontalSkyline.FromBox(-2, 2, 0, 1, HorizontalDirection.Left);

        // Envelope: max(1, 3) over the overlapping band → 3.
        Assert.Equal(3.0, a.Distance(left), 6);
    }
}
