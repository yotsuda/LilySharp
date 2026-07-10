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

[Trait("Category", "Unit")]
public class BarCheckDebugTests
{
    [Fact]
    public void SimpleNotes_CreatesMeasure()
    {
        var source = "{ c4 d4 e4 f4 | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.Voices);
        Assert.Single(score.Voice.Measures);
        Assert.Equal(4, score.Voice.Measures[0].Items.Length);
    }

    [Fact]
    public void ThreeNotes_AutoCompletesOnFinalize()
    {
        var source = "{ c4 d4 e4 }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.Voice.Measures);
        Assert.Equal(3, score.Voice.Measures[0].Items.Length);
    }

    [Fact]
    public void BarCheck_EmitsWarningForMisaligned()
    {
        var source = "{ c4 d4 e4 | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.Voice.Measures);
        Assert.Equal(3, score.Voice.Measures[0].Items.Length);
    }

    [Fact]
    public void RepeatStructure_CreatesMeasureWithNotes()
    {
        var source = @"
section A {
    melody { c4 d4 e4 f4 | }
}
form main { |: A :| }
";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        // voiceName passed to Collect
        var score = collector.Collect(tree, "melody");

        Assert.True(score.Voice.Measures.Length > 0, "Expected at least one measure");
        Assert.True(score.Voice.Measures[0].Items.Length > 0, "Expected notes in measure");
    }
}
