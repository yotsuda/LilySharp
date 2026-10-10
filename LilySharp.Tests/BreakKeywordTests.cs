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
using Xunit;
using Xunit.Abstractions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class BreakKeywordTests
{
    private readonly ITestOutputHelper _output;

    public BreakKeywordTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void MeasureCollector_BreakKeyword_SetsHasBreakAfter()
    {
        // 'break' sets HasBreakAfter on the previous measure
        var source = "c4 d e f | break g a b c' |";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        _output.WriteLine($"Measure count: {score.Voice.Measures.Length}");
        for (int i = 0; i < score.Voice.Measures.Length; i++)
        {
            var m = score.Voice.Measures[i];
            _output.WriteLine($"  Measure {i}: Items={m.Items.Length}, HasBreakAfter={m.HasBreakAfter}");
        }

        Assert.Equal(2, score.Voice.Measures.Length);
        // break after measure 0
        Assert.True(score.Voice.Measures[0].HasBreakAfter, "First measure should have HasBreakAfter=true");
    }

    [Fact]
    public void MeasureCollector_BreakAfterSecondMeasure_Works()
    {
        // c4 d e f | g a b c' | break d' c' b a |
        // break sets HasBreakAfter on measure 1
        var source = "c4 d e f | g a b c' | break d' c' b a |";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        _output.WriteLine($"Measure count: {score.Voice.Measures.Length}");
        for (int i = 0; i < score.Voice.Measures.Length; i++)
        {
            var m = score.Voice.Measures[i];
            _output.WriteLine($"  Measure {i}: Items={m.Items.Length}, HasBreakAfter={m.HasBreakAfter}");
        }

        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.False(score.Voice.Measures[0].HasBreakAfter);
        Assert.True(score.Voice.Measures[1].HasBreakAfter, "Second measure should have HasBreakAfter=true");
    }

    /// <summary>
    /// A <c>break</c> written after the prelude a bar opens with — its time signature, key or
    /// clef — stands at the bar line, exactly as one written before it: the clock has not
    /// moved, so it is the bar-line break and not a mid-bar one. Until 2026-10-10 the
    /// signature item already in the bar made <c>time 4/4 break</c> read as mid-bar, and the
    /// break was deferred onto the END of the bar it opened (If You Leave Me Now, bar 12:
    /// the page broke after bar 13 where LilyPond breaks after 12).
    /// </summary>
    [Theory]
    [InlineData("c4 d e f | time 2/4 g a | time 4/4 break b c' d' e' | f' g' a' b' |")]
    [InlineData("c4 d e f | g a b c' | key g major break b c' d' e' | f' g' a' b' |")]
    [InlineData("c4 d e f | g a b c' | clef bass break b c' d' e' | f' g' a' b' |")]
    [InlineData("c4 d e f | g a b c' | time 3/4 key g major break b c' d' | f' g' a' |")]
    public void MeasureCollector_BreakAfterABarsOpeningSignature_StandsAtTheBarLine(string music)
    {
        var measures = BookMeasures(music);
        Assert.Equal(4, measures.Count);
        Assert.False(measures[0].HasBreakAfter);
        Assert.True(measures[1].HasBreakAfter, "the break stands at the bar line the signature opens");
        Assert.False(measures[2].HasBreakAfter, "…and is not deferred onto the end of that bar");
    }

    [Fact]
    public void MeasureCollector_NoBreakAfterABarsOpeningSignature_StandsAtTheBarLine()
    {
        var measures = BookMeasures("c4 d e f | g a b c' | time 2/4 noBreak b c' | f' g' |");
        Assert.Equal(4, measures.Count);
        Assert.Equal(LilySharp.Core.Svg.Layout.BreakPermission.Forbid, measures[1].LineBreakPermission);
        Assert.Equal(LilySharp.Core.Svg.Layout.BreakPermission.Allow, measures[2].LineBreakPermission);
    }

    /// <summary>The primary voice's measures of a one-part book in 4/4 playing <paramref name="music"/>.</summary>
    private static IReadOnlyList<LilySharp.Core.Svg.Model.Measure> BookMeasures(string music)
    {
        var tree = SyntaxTree.Parse("time 4/4 part m { section A { " + music + " } } form { A } score { staff m }");
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.EnumerateStaves().First().Staff.PrimaryVoice.Measures;
    }

    [Fact]
    public void MeasureCollector_BreakWithoutBarline_Works()
    {
        // break can appear without barline
        var source = "c4 d e f g a b c' break d'4 e' f' g'";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        _output.WriteLine($"Measure count: {score.Voice.Measures.Length}");
        for (int i = 0; i < score.Voice.Measures.Length; i++)
        {
            var m = score.Voice.Measures[i];
            _output.WriteLine($"  Measure {i}: Items={m.Items.Length}, HasBreakAfter={m.HasBreakAfter}");
        }

        // Should auto-split at 4/4 time signature boundaries
        Assert.True(score.Voice.Measures.Length >= 2);
        // At least one measure should have HasBreakAfter=true
        Assert.Contains(score.Voice.Measures, m => m.HasBreakAfter);
    }

    [Fact]
    public void FormBreak_ForcesASystemBreakAfterThePrecedingSection()
    {
        // `form { A B break C }`: A=bar0, B=bar1, break flags bar 1 (after B), C=bar2.
        var source = """
            time 4/4
            key c major
            part m { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 a' b' c'' | }
              section C { c'4 d' e' f' | }
            }
            form { A B break C }
            score { staff m }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source), "m");
        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.True(score.Voice.Measures[1].HasBreakAfter, "break after B (bar 1)");
        Assert.False(score.Voice.Measures[0].HasBreakAfter);
    }

    [Fact]
    public void FormNoBreak_ForbidsASystemBreakAfterThePrecedingSection()
    {
        var source = """
            time 4/4
            key c major
            part m { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 a' b' c'' | }
            }
            form { A noBreak B }
            score { staff m }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source), "m");
        Assert.Equal(LilySharp.Core.Svg.Layout.BreakPermission.Forbid,
            score.Voice.Measures[0].LineBreakPermission);
    }
}
