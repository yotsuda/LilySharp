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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PercentRepeatTests
{
    // --- PercentRepeatEngraver ---

    [Fact]
    public void PercentRepeatEngraver_Calculate_EmptyInput()
    {
        var result = PercentRepeatEngraver.Calculate(
            ImmutableArray<PercentRepeatItem>.Empty,
            ImmutableArray<SystemLayout>.Empty,
            ImmutableArray<MeasureLayout>.Empty);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void PercentRepeatEngraver_Calculate_ProducesLayout()
    {
        var items = ImmutableArray.Create(
            new PercentRepeatItem(1, 0));

        var itemLayout = new ItemLayout(0, 0, 1.0);
        var ml0 = new MeasureLayout(0, 0, 10.0, ImmutableArray.Create(itemLayout));
        var ml1 = new MeasureLayout(1, 10.0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0,
            ImmutableArray.Create(ml0, ml1));

        var result = PercentRepeatEngraver.Calculate(
            items,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(ml0, ml1));

        Assert.Single(result);
        Assert.Equal(1, result[0].MeasureIndex);
        // Center of measure 1: ml1.X(10) + ml1.Width(10)/2 = 15
        Assert.Equal(15.0, result[0].X, 1);
    }

    [Fact]
    public void PercentRepeatEngraver_Calculate_CenteredVertically()
    {
        var items = ImmutableArray.Create(
            new PercentRepeatItem(0, 0));

        var itemLayout = new ItemLayout(0, 0, 1.0);
        var ml = new MeasureLayout(0, 0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 0, 50.0, 5.0,
            ImmutableArray.Create(ml));

        var result = PercentRepeatEngraver.Calculate(
            items,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(ml));

        Assert.Single(result);
        Assert.Equal(0.0, result[0].YUp, 1);  // Y-up: centred on the staff middle
    }

    // --- MeasureCollector integration ---

    [Fact]
    public void Collector_PercentRepeat_Basic()
    {
        // repeat percent 2 { c4 d e f } → 2 measures total, measure 1 is percent repeat
        var source = "repeat percent 2 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // Should have 2 measures (body played twice)
        Assert.Equal(2, score.Voice.Measures.Length);

        // One percent repeat marker (on measure 1)
        Assert.Single(score.PercentRepeats);
        Assert.Equal(1, score.PercentRepeats[0].MeasureIndex);
    }

    [Fact]
    public void Collector_PercentRepeat_FourTimes()
    {
        // repeat percent 4 { c4 d e f } → 4 measures, measures 1-3 are percent repeats
        var source = "repeat percent 4 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(4, score.Voice.Measures.Length);
        Assert.Equal(3, score.PercentRepeats.Length);
        Assert.Equal(1, score.PercentRepeats[0].MeasureIndex);
        Assert.Equal(2, score.PercentRepeats[1].MeasureIndex);
        Assert.Equal(3, score.PercentRepeats[2].MeasureIndex);
    }

    [Fact]
    public void Collector_PercentRepeat_NotesPreserved()
    {
        // The first measure should have actual notes
        var source = "repeat percent 2 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // First measure has the original notes
        Assert.True(score.Voice.Measures[0].Items.Length >= 4);
    }

    [Fact]
    public void Collector_PercentRepeat_NoPercentForFirst()
    {
        var source = "repeat percent 3 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // Percent repeat markers should NOT include measure 0
        Assert.DoesNotContain(score.PercentRepeats, pr => pr.MeasureIndex == 0);
    }

    [Fact]
    public void Collector_VoltaRepeat_NoPercentMarkers()
    {
        // Symbolic volta repeats should not create percent markers
        var source = "{ |: c4 d e f :| }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    [Fact]
    public void Collector_UnfoldRepeat_NoPercentMarkers()
    {
        var source = "repeat unfold 2 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    [Fact]
    public void Collector_NoRepeat_NoPercentMarkers()
    {
        var source = "c4 d e f | g a b c'";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    [Fact]
    public void Collector_PercentRepeat_WithPrecedingMeasures()
    {
        // Measures before the repeat should not be affected
        var source = "c4 d e f | repeat percent 2 { g4 a b c' }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.Single(score.PercentRepeats);
        Assert.Equal(2, score.PercentRepeats[0].MeasureIndex);
    }
}
