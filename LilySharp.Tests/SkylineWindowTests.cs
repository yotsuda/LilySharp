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
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A merge into a resolved skyline reads only the buildings the incoming ink can touch
/// (VerticalSkyline.OpenWindow). The equality half: the same buildings as reading the whole
/// skyline, over random sequences of every unbatched merge. The liveness half: it does read less.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SkylineWindowTests
{
    private static VerticalSkyline Box(Random r, VerticalDirection dir, double span)
    {
        double x = r.NextDouble() * span, w = 0.2 + (r.NextDouble() * 6), y = r.NextDouble() * 8;
        return VerticalSkyline.FromBox(x, x + w, y - 1, y, dir);
    }

    private static VerticalSkyline Sloped(Random r, VerticalDirection dir, double span)
    {
        double x = r.NextDouble() * span, w = 0.5 + (r.NextDouble() * 10);
        return VerticalSkyline.FromSlope(x, r.NextDouble() * 8, x + w, r.NextDouble() * 8, 0.5, dir);
    }

    /// <summary>The same operations, seeded the same, whichever way the merges read.</summary>
    private static List<SkylineBuilding> Run(int seed, int ops, double span, bool whole)
    {
        VerticalSkyline.t_wholeWalk = whole;
        try
        {
            var r = new Random(seed);
            var dir = seed % 2 == 0 ? VerticalDirection.Up : VerticalDirection.Down;
            var sky = new VerticalSkyline(dir);
            var profile = VerticalSkyline.FromBox(0, 2, 0, 1, dir);
            profile.Merge(Sloped(new Random(seed + 1), dir, 1));
            var resolved = profile.Buildings.ToArray();
            for (int i = 0; i < ops; i++)
            {
                switch (r.Next(6))
                {
                    case 0:
                    case 1:
                        sky.Merge(Box(r, dir, span));
                        break;
                    case 2:
                        sky.Merge(Sloped(r, dir, span));
                        break;
                    case 3:
                        sky.Merge(resolved, r.NextDouble() * span, r.NextDouble() * 4);
                        break;
                    case 4:
                        sky.MergeRaised(Box(r, dir, span), r.NextDouble() * 3);
                        break;
                    case 5:
                        sky.MergeBox(r.NextDouble() * span, (r.NextDouble() * span) + span, 0, r.NextDouble() * 3);
                        break;
                }
            }
            return sky.Buildings.ToList();
        }
        finally
        {
            VerticalSkyline.t_wholeWalk = false;
        }
    }

    [Theory]
    [InlineData(1, 40, 30.0)]
    [InlineData(2, 40, 30.0)]
    [InlineData(3, 200, 200.0)]
    [InlineData(4, 200, 200.0)]
    [InlineData(5, 400, 80.0)]
    [InlineData(6, 400, 80.0)]
    public void TheWindowGivesTheWholeWalksBuildings(int seed, int ops, double span)
    {
        for (int round = 0; round < 12; round++)
        {
            var window = Run((seed * 1000) + round, ops, span, whole: false);
            var whole = Run((seed * 1000) + round, ops, span, whole: true);
            Assert.Equal(whole, window);
        }
    }

    [Fact]
    public void ABoxesPaddersDoNotStay()
    {
        var sky = new VerticalSkyline(VerticalDirection.Up);
        sky.Merge(VerticalSkyline.FromBox(0, 1, 0, 1, VerticalDirection.Up));
        sky.Merge(VerticalSkyline.FromBox(5, 6, 0, 1, VerticalDirection.Up));
        Assert.Equal(2, sky.Buildings.Count);
        Assert.All(sky.Buildings, b => Assert.True(double.IsFinite(b.Start) && double.IsFinite(b.End)));
    }

    [Fact]
    public void ABoxIntoALongSkyline_ReadsWhatItTouches()
    {
        var sky = new VerticalSkyline(VerticalDirection.Up);
        sky.BeginBatch();
        for (int i = 0; i < 1000; i++)
            sky.MergeBox(i, i + 0.5, 0, 1 + (i % 3));
        sky.EndBatch();
        Assert.Equal(1000, sky.Buildings.Count);
        VerticalSkyline.t_windowRead = 0;
        sky.Merge(VerticalSkyline.FromBox(500.1, 501.4, 0, 9, VerticalDirection.Up));
        // The two buildings it overlaps and its own three (a box and two padders), not 1000.
        Assert.InRange(VerticalSkyline.t_windowRead, 3, 8);
        Assert.Equal(9, sky.Height(500.2));
        Assert.Equal(1 + (499 % 3), sky.Height(499.2));
    }
}
