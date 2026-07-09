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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A MeasureCollector may be reused for more than one Collect call (Reset runs at
/// the start of each). Reset must clear per-run state so a later call does not carry
/// the previous one's data — PitchTrace in particular used to accumulate unbounded.
/// </summary>
[Trait("Category", "Unit")]
public class MeasureCollectorResetTests
{
    [Fact]
    public void Reuse_PitchTraceReflectsOnlyLatestCollect()
    {
        var collector = new MeasureCollector();

        collector.Collect(SyntaxTree.Parse("c4 d e f"));   // 4 pitches
        Assert.Equal(4, collector.PitchTrace.Count);

        collector.Collect(SyntaxTree.Parse("g4 a"));       // 2 pitches
        // Without Reset clearing _pitchTrace this would be 6 (accumulated).
        Assert.Equal(2, collector.PitchTrace.Count);
    }
}
