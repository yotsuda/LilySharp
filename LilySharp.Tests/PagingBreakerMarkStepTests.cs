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
/// A music mark reaches the paging silhouette twice: where the layout stacked it, which the
/// page is spaced by, and where the page BREAKER prices it (its pure height, LilyPond's
/// adjacent_pure_heights), which only the breaker's replay reads. A mark opening a line is
/// also priced on the line before it, by the breaker alone.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PagingBreakerMarkStepTests
{
    private static PagingAugmentProgram Program()
    {
        var b = new PagingAugmentProgram.Builder();
        b.AddMusicMarkBoxes(0, 4, stackedBottom: 5, stackedTop: 8, pureBottom: 1, pureTop: 3,
            pureXLeft: 0, pureXRight: 4);
        b.AddBreakerOnlyMarkBox(10, 12, bottom: 1, top: 3);
        Assert.True(b.HasBreakerSteps);
        return b.Build();
    }

    private static (VerticalSkyline, VerticalSkyline) Empty()
        => (new VerticalSkyline(VerticalDirection.Up), new VerticalSkyline(VerticalDirection.Down));

    [Fact]
    public void TheLayoutsReplay_ReadsTheStackedBoxAlone()
    {
        var (up, _) = Program().Execute(Empty());
        Assert.Equal(8, up.MaxHeightInRange(0, 4), 9);
        Assert.Equal(double.NegativeInfinity, up.MaxHeightInRange(10, 12));
    }

    [Fact]
    public void TheBreakersReplay_ReadsThePureBoxes()
    {
        var (up, _) = Program().Execute(Empty(), forBreaker: true);
        Assert.Equal(3, up.MaxHeightInRange(0, 4), 9);
        Assert.Equal(3, up.MaxHeightInRange(10, 12), 9);
    }
}
