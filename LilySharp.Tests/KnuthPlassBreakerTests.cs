using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

public class KnuthPlassBreakerTests
{
    [Fact]
    public void BreakIntoLines_FeatureShowcase_CreatesMultipleLines()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\feature-showcase.lys");
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        
        // With optimal line breaking
        var options = new LayoutOptions { UseOptimalLineBreaking = true };
        var engine = new LayoutEngine(options);
        var layout = engine.Layout(score);
        
        // Should create multiple systems for 28 measures
        Assert.True(layout.Systems.Length >= 2, $"Expected at least 2 systems, got {layout.Systems.Length}");
    }
    
    [Fact]
    public void BreakIntoLines_OptimalVsGreedy_MayDiffer()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\feature-showcase.lys");
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        
        // Optimal
        var optimalOptions = new LayoutOptions { UseOptimalLineBreaking = true };
        var optimalEngine = new LayoutEngine(optimalOptions);
        var optimalLayout = optimalEngine.Layout(score);
        
        // Greedy
        var greedyOptions = new LayoutOptions { UseOptimalLineBreaking = false };
        var greedyEngine = new LayoutEngine(greedyOptions);
        var greedyLayout = greedyEngine.Layout(score);
        
        // Both should produce valid layouts
        Assert.True(optimalLayout.Systems.Length > 0);
        Assert.True(greedyLayout.Systems.Length > 0);
        
        // Log for visibility
        var optimalMeasures = optimalLayout.Systems.Select(s => s.Measures.Length).ToArray();
        var greedyMeasures = greedyLayout.Systems.Select(s => s.Measures.Length).ToArray();
        
        // They might differ (optimal may be more balanced)
        Assert.NotEmpty(optimalMeasures);
        Assert.NotEmpty(greedyMeasures);
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
}