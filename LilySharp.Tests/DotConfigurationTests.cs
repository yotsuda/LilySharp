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
/// Pins the Dot_configuration port's behaviour (LILYPOND-REF:
/// lily/dot-configuration.cc, lily/dot-column.cc:194-224). Expected values
/// hand-traced through LilyPond's algorithm.
/// </summary>
[Trait("Category", "Unit")]
public class DotConfigurationTests
{
    [Theory]
    // A dot on a LINE moves to the space above (UP wins the badness tie:
    // 2·1²+0 vs 2·1²+1).
    [InlineData(new[] { 0 }, new[] { 1 })]
    [InlineData(new[] { -2 }, new[] { -1 })]
    // A dot already in a SPACE stays put.
    [InlineData(new[] { 1 }, new[] { 1 })]
    [InlineData(new[] { -3 }, new[] { -3 })]
    // Two spaced notes a third apart keep their own spaces.
    [InlineData(new[] { -3, -1 }, new[] { -3, -1 })]
    // Line + the space above: the line dot wants the same space; the
    // configuration fans out downward/upward by least badness.
    [InlineData(new[] { 0, 1 }, new[] { -1, 1 })]
    // Cluster c-d-e (line, space, line): classic fan-out to the three
    // surrounding spaces.
    [InlineData(new[] { 0, 1, 2 }, new[] { -1, 1, 3 })]
    // Unison: two dots cannot share a slot; the EXISTING dot yields upward
    // (up costs 2·2²=8, down costs 2·2²+1=9), the new dot takes the slot.
    [InlineData(new[] { 1, 1 }, new[] { 3, 1 })]
    public void Resolve_MatchesLilyPondAlgorithm(int[] input, int[] expected)
    {
        Assert.Equal(expected, DotConfiguration.Resolve(input));
    }

    [Fact]
    public void Resolve_NeverLeavesADotOnALine()
    {
        var rng = new Random(20260611);
        for (int trial = 0; trial < 200; trial++)
        {
            int count = rng.Next(1, 6);
            var positions = Enumerable.Range(0, count)
                .Select(_ => rng.Next(-8, 9)).ToArray();
            var resolved = DotConfiguration.Resolve(positions);

            Assert.All(resolved, p => Assert.True(p % 2 != 0,
                $"dot on line {p} for input [{string.Join(",", positions)}]"));
            Assert.Equal(resolved.Length, resolved.Distinct().Count());
        }
    }
}
