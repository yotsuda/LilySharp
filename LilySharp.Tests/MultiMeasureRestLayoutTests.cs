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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests detection of multi-measure rest spans in the layout pipeline.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob detection
/// </remarks>
[Trait("Category", "Unit")]
public class MultiMeasureRestLayoutTests
{
    private static ScoreLayout BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return engine.Layout(score);
    }

    [Fact]
    public void NoRests_ProducesNoMmrLayout()
    {
        var layout = BuildLayout("c4 d e f |");
        Assert.Empty(layout.MultiMeasureRestLayouts);
    }

    [Fact]
    public void SingleR1_ProducesOneMmrSpanCount1()
    {
        var layout = BuildLayout("R1 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(0, mmr.StartMeasureIndex);
        Assert.Equal(1, mmr.MeasureCount);
        Assert.True(mmr.UseChurchRest);
    }

    [Fact]
    public void R1Star4_ProducesOneMmrSpanCount4()
    {
        var layout = BuildLayout("R1*4 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(0, mmr.StartMeasureIndex);
        Assert.Equal(4, mmr.MeasureCount);
        Assert.True(mmr.UseChurchRest);
    }

    [Fact]
    public void R1Star12_ExceedsExpandLimit_UsesBigRest()
    {
        // 12 > ExpandLimit (10), so big_rest should be selected.
        var layout = BuildLayout("R1*12 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(12, mmr.MeasureCount);
        Assert.False(mmr.UseChurchRest);
    }

    [Fact]
    public void MmrAtBoundary10_StaysChurchRest()
    {
        // 10 == ExpandLimit, still church_rest (≤ ExpandLimit).
        var layout = BuildLayout("R1*10 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        Assert.Equal(10, layout.MultiMeasureRestLayouts[0].MeasureCount);
        Assert.True(layout.MultiMeasureRestLayouts[0].UseChurchRest);
    }

    [Fact]
    public void RestAndMusic_BoundaryStopsRun()
    {
        // R1*3 then notes: MMR run is exactly 3.
        var layout = BuildLayout("R1*3 c4 d e f |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        Assert.Equal(3, layout.MultiMeasureRestLayouts[0].MeasureCount);
    }

    [Fact]
    public void MusicThenRest_RunStartsAfterMusic()
    {
        // First measure has notes, then 2 measures of rest.
        var layout = BuildLayout("c4 d e f | R1*2 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(1, mmr.StartMeasureIndex);
        Assert.Equal(2, mmr.MeasureCount);
    }

    [Fact]
    public void ConsecutiveR1_ProducesSingleRunOfThree()
    {
        // Three explicit R1 measures should be detected as one MMR run, not three.
        var layout = BuildLayout("R1 | R1 | R1 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        Assert.Equal(3, layout.MultiMeasureRestLayouts[0].MeasureCount);
    }

    [Fact]
    public void MmrSpan_HasSensibleXRange()
    {
        var layout = BuildLayout("R1*3 |");
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.True(mmr.EndX > mmr.StartX,
            $"MMR span EndX must be greater than StartX, got {mmr.StartX}/{mmr.EndX}");
    }
}
