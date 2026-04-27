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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests the multi-measure rest syntax <c>R<dur>*N</c>: parser accepts the
/// multiplier and the collector expands it into N consecutive measure-rests.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob
/// LILYPOND-REF: lily/lily-parser.yy — R<dur>*N grammar
/// Current scope is structural (N measures get filled) — proper church-rest /
/// big-rest visual rendering is L-1b.
/// </remarks>
[Trait("Category", "Unit")]
public class MultiMeasureRestTests
{
    private static (Score Score, ScoreLayout Layout) BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return (score, engine.Layout(score));
    }

    [Fact]
    public void Parse_R1_NoMultiplier_HasMeasureCountOne()
    {
        var tree = SyntaxTree.Parse("R1 |");
        var rest = (RestSyntax)tree.GetRoot().DescendantNodes().First(n => n is RestSyntax);
        Assert.Equal(1, rest.MeasureCount);
        Assert.False(rest.IsMultiMeasure);
    }

    [Fact]
    public void Parse_R1Star8_HasMeasureCountEight()
    {
        var tree = SyntaxTree.Parse("R1*8 |");
        var rest = (RestSyntax)tree.GetRoot().DescendantNodes().First(n => n is RestSyntax);
        Assert.Equal(8, rest.MeasureCount);
        Assert.True(rest.IsMultiMeasure);
    }

    [Fact]
    public void Collect_R1Star4_ProducesFourMeasures()
    {
        var (score, _) = BuildLayout("R1*4 |");
        // Expansion: 4 measures, each a single full rest.
        Assert.Equal(4, score.Voice.Measures.Length);
        foreach (var m in score.Voice.Measures)
        {
            Assert.Single(m.Items);
            Assert.IsType<RestItem>(m.Items[0]);
        }
    }

    [Fact]
    public void Collect_R1Star1_BehavesLikePlainR1()
    {
        var (sourceA, _) = BuildLayout("R1 |");
        var (sourceB, _) = BuildLayout("R1*1 |");
        Assert.Equal(sourceA.Voice.Measures.Length, sourceB.Voice.Measures.Length);
    }

    [Fact]
    public void Collect_R1Star2_FollowedByMusic_ContinuesAfterMmr()
    {
        // After an MMR, regular notes continue normally.
        var (score, _) = BuildLayout("R1*2 c4 d e f |");
        // 2 MMR measures + 1 measure of music = 3 measures total.
        Assert.Equal(3, score.Voice.Measures.Length);

        // Last measure should be the c-d-e-f line.
        var lastMeasure = score.Voice.Measures[^1];
        Assert.Equal(4, lastMeasure.Items.Length);
        Assert.IsType<NoteItem>(lastMeasure.Items[0]);
    }

    [Fact]
    public void Layout_R1Star4_DoesNotCrash()
    {
        var ex = Record.Exception(() => BuildLayout("R1*4 |"));
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_RegularRest_StillWorks()
    {
        // Regression: r4 (lowercase = silent rest) with no multiplier still parses normally.
        var (score, _) = BuildLayout("r4 r4 r4 r4 |");
        Assert.Single(score.Voice.Measures);
        Assert.Equal(4, score.Voice.Measures[0].Items.Length);
    }
}
