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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The per-count default rhythm table for independent chord parts (4/4).
/// </summary>
[Trait("Category", "Unit")]
public class ChordRhythmTests
{
    [Theory]
    [InlineData(1, new[] { "1" })]
    [InlineData(2, new[] { "1/2", "1/2" })]
    [InlineData(3, new[] { "1/4", "1/4", "1/2" })]
    [InlineData(4, new[] { "1/4", "1/4", "1/4", "1/4" })]
    [InlineData(5, new[] { "1/4", "1/4", "1/4", "1/8", "1/8" })]
    [InlineData(6, new[] { "1/4", "1/4", "1/8", "1/8", "1/8", "1/8" })]
    [InlineData(7, new[] { "1/4", "1/8", "1/8", "1/8", "1/8", "1/8", "1/8" })]
    [InlineData(8, new[] { "1/8", "1/8", "1/8", "1/8", "1/8", "1/8", "1/8", "1/8" })]
    public void DefaultDurations_44_MatchesAgreedTable(int count, string[] expected)
    {
        var durs = ChordRhythm.DefaultDurations(count, 4, 4);
        Assert.NotNull(durs);
        Assert.Equal(expected, durs!.Value.Select(FractionString).ToArray());

        // Each row fills exactly one whole-note measure.
        var sum = durs.Value.Aggregate(Fraction.Zero, (a, b) => a + b);
        Assert.Equal(Fraction.Whole, sum);
    }

    [Fact]
    public void DefaultDurations_OutOfRangeCount_IsNull()
    {
        Assert.Null(ChordRhythm.DefaultDurations(0, 4, 4));
        Assert.Null(ChordRhythm.DefaultDurations(9, 4, 4));
    }

    [Fact]
    public void DefaultDurations_NonCommonTime_IsNull()
    {
        // Other meters require explicit durations (no table yet).
        Assert.Null(ChordRhythm.DefaultDurations(3, 3, 4));
        Assert.Null(ChordRhythm.DefaultDurations(2, 6, 8));
    }

    private static string FractionString(Fraction f) =>
        f.Denominator == 1 ? f.Numerator.ToString() : $"{f.Numerator}/{f.Denominator}";
}
