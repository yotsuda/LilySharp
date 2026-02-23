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

using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class KnuthPlassBreakerTests
{
    [Fact]
    public void BreakIntoLines_ManyMeasures_CreatesMultipleLines()
    {
        // Inline source with 8 measures - enough to test line breaking
        var source = "c4 d e f | g a b c' | d' c' b a | g f e d | c d e f | g a b c' | d' c' b a | g f e d |";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var options = new LayoutOptions { UseOptimalLineBreaking = true };
        var engine = new LayoutEngine(options);
        var layout = engine.Layout(score);

        Assert.True(layout.Systems.Length >= 1, $"Expected at least 1 system, got {layout.Systems.Length}");
    }

    [Fact]
    public void BreakIntoLines_SingleMeasure_SingleLine()
    {
        var source = "c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var options = new LayoutOptions { UseOptimalLineBreaking = true };
        var engine = new LayoutEngine(options);
        var layout = engine.Layout(score);

        Assert.Single(layout.Systems);
    }

    [Fact]
    public void BreakIntoLines_WithLineBreakBar_ForcesLineBreak()
    {
        var source = "c4 d e f | g a b c' | break d' c' b a | g f e d |";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(4, score.Voice.Measures.Length);
        Assert.True(score.Voice.Measures[1].HasBreakAfter);

        var options = new LayoutOptions { UseOptimalLineBreaking = true };
        var engine = new LayoutEngine(options);
        var layout = engine.Layout(score);

        Assert.True(layout.Systems.Length >= 2,
            $"Expected at least 2 systems due to | break, got {layout.Systems.Length}");
        Assert.Equal(2, layout.Systems[0].Measures.Length);
    }

    [Fact]
    public void BreakIntoLines_GreedyWithLineBreakBar_ForcesLineBreak()
    {
        var source = "c4 d e f | break g a b c' |";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var options = new LayoutOptions { UseOptimalLineBreaking = false };
        var engine = new LayoutEngine(options);
        var layout = engine.Layout(score);

        Assert.Equal(2, layout.Systems.Length);
        Assert.Single(layout.Systems[0].Measures);
    }
}
