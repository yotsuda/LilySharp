using Xunit;
using Xunit.Abstractions;
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
}
