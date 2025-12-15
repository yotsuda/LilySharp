using Xunit;
using Xunit.Abstractions;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;

namespace LilySharp.Tests;

public class LineBreakBarTests
{
    private readonly ITestOutputHelper _output;
    
    public LineBreakBarTests(ITestOutputHelper output) => _output = output;
    
    [Fact]
    public void MeasureCollector_LineBreakBar_SetsHasBreakAfter()
    {
        // |/ sets HasBreakAfter on the measure being completed
        var source = "c4 d e f |/ g a b c' |";
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
        // |/ after measure 0
        Assert.True(score.Voice.Measures[0].HasBreakAfter, "First measure should have HasBreakAfter=true");
    }
    
    [Fact]
    public void MeasureCollector_LineBreakBarAfterSecondMeasure_Works()
    {
        // c4 d e f | g a b c' |/ d' c' b a |
        // |/ sets HasBreakAfter on measure 1
        var source = "c4 d e f | g a b c' |/ d' c' b a |";
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
}