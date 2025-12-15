using Xunit;
using Xunit.Abstractions;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Svg;

namespace LilySharp.Tests;

public class FontDebugTest
{
    private readonly ITestOutputHelper _output;
    public FontDebugTest(ITestOutputHelper output) => _output = output;
    
    [Fact]
    public void Preview_OmitsFontFace()
    {
        var tree = SyntaxTree.Parse("c4 |");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var layout = new LayoutEngine().Layout(score);
        
        var renderer = new SvgRenderer(renderOptions: SvgRenderOptions.Preview());
        var svg = renderer.Render(score, layout);
        
        // Preview mode should NOT contain @font-face
        Assert.DoesNotContain("@font-face", svg);
        // But should still reference the font family
        Assert.Contains("font-family: 'Emmentaler'", svg);
    }
    
    [Fact]
    public void Default_UsesLocalFont()
    {
        var tree = SyntaxTree.Parse("c4 |");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var layout = new LayoutEngine().Layout(score);
        
        var renderer = new SvgRenderer(renderOptions: SvgRenderOptions.Default);
        var svg = renderer.Render(score, layout);
        
        // Default mode should reference local font
        Assert.Contains("src: local('Emmentaler')", svg);
    }
    
    [Fact]
    public void SvgContainsGlyphCharacters()
    {
        var tree = SyntaxTree.Parse("c4 |");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var layout = new LayoutEngine().Layout(score);
        
        var renderer = new SvgRenderer(renderOptions: SvgRenderOptions.Preview());
        var svg = renderer.Render(score, layout);
        
        // Should contain music glyph characters
        Assert.True(svg.Contains(EmmentalerGlyphs.NoteheadBlack) || 
                   svg.Contains(EmmentalerGlyphs.GClef),
                   "SVG should contain music glyph characters");
    }
}
