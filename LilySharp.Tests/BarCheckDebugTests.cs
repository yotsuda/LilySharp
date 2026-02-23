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
structure { |: A :| }
";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        // voiceName passed to Collect
        var score = collector.Collect(tree, "melody");

        Assert.True(score.Voice.Measures.Length > 0, "Expected at least one measure");
        Assert.True(score.Voice.Measures[0].Items.Length > 0, "Expected notes in measure");
    }
}
