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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The line-break table's rows skip the starts whose line cannot fit. The equality half: the
/// same breaks as reading every start, over vectors whose first and last bars can take a lot off
/// a line's minimum, which is what the bound has to allow for. The liveness half: it does skip.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LineBreakStartBoundTests
{
    private static MeasureSpringData[] RandomBars(Random r, int n)
    {
        var bars = new MeasureSpringData[n];
        for (int m = 0; m < n; m++)
        {
            double min = 3 + (r.NextDouble() * 12);
            double ideal = min + (r.NextDouble() * 6);
            bool opens = r.Next(3) == 0;
            bool closes = r.Next(3) == 0;
            var whole = new Spring(ideal, min, 1);
            bars[m] = new MeasureSpringData(ideal, min, 1,
                InverseCompressStrength: 1,
                Spring0Ideal: ideal,
                Spring0Min: min,
                Spring0Stretch: 1,
                Spring0Compress: 1,
                LineStartSpring: opens ? new Spring(ideal * 0.3, min * 0.2, 1) : null,
                LineStartPrefixExtra: r.Next(4) == 0 ? r.NextDouble() * 5 : 0,
                LineEndCourtesyWidth: r.Next(4) == 0 ? r.NextDouble() * 5 : 0,
                Springs: [whole],
                LineEndSprings: closes ? [new Spring(ideal * 0.3, min * 0.2, 1)] : default);
        }
        return bars;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheBoundDropsNoLine(int seed)
    {
        var r = new Random(seed);
        for (int round = 0; round < 60; round++)
        {
            var bars = RandomBars(r, 5 + r.Next(40));
            double width = 20 + (r.NextDouble() * 80);
            var breaker = new KnuthPlassBreaker(width, r.NextDouble() * 6, r.NextDouble() * 6);
            var bounded = breaker.Solve(bars).IdealBreaks;
            KnuthPlassBreaker.t_readEveryStart = true;
            try
            {
                Assert.Equal(breaker.Solve(bars).IdealBreaks, bounded);
            }
            finally
            {
                KnuthPlassBreaker.t_readEveryStart = false;
            }
        }
    }

    [Fact]
    public void ALongBook_ReadsFewStartsARow()
    {
        var bars = Enumerable.Repeat(new MeasureSpringData(10, 6, 1, InverseCompressStrength: 1), 200).ToArray();
        KnuthPlassBreaker.t_startsRead = 0;
        new KnuthPlassBreaker(40, 0, 0).Solve(bars);
        long read = KnuthPlassBreaker.t_startsRead;
        // A line holds at most 6 of these bars, so about 7 starts a row, not the 100 on average
        // of reading them all. (The poison: the bound never moves.)
        Assert.InRange(read, 200, 200 * 8);
    }
}
