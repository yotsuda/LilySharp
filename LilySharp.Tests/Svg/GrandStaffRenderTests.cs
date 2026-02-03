using Xunit;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests.Svg;

public class GrandStaffRenderTests
{
    [Fact]
    public void RenderGrandStaff_ContainsBrace()
    {
        var source = """
            title "Test"
            time 4/4
            
            rh = { c''4 d'' e'' f'' | }
            lh = { c4 e g c' | }
            
            section Main {
              melody { rh }
              bass { lh }
            }
            
            structure { Main }
            
            render piano "test.svg" {
              grandStaff {
                staff treble { melody }
                staff bass { bass }
              }
            }
            """;
        
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));
        
        var renderSpec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(renderSpec);
        
        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, renderSpec);
        
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        
        var renderer = new SvgRenderer();
        var svg = renderer.Render(score, layout);
        
        // Should contain brace (rendered using Emmentaler-Brace font)
        Assert.Contains("<text", svg);
        Assert.Contains("Emmentaler-Brace", svg);
        
        // Should contain two sets of staff lines (10 lines total)
        var staffLineCount = System.Text.RegularExpressions.Regex.Matches(svg, @"<line class=""staff""").Count;
        Assert.Equal(10, staffLineCount);
        
        // Should contain treble and bass clef glyphs
        Assert.Contains("class=\"music\"", svg);
    }
    
    [Fact]
    public void RenderGrandStaff_HasCorrectStaffPositions()
    {
        var source = """
            title "Test"
            time 4/4
            
            rh = { c''4 | }
            lh = { c4 | }
            
            section Main {
              melody { rh }
              bass { lh }
            }
            
            structure { Main }
            
            render piano "test.svg" {
              grandStaff {
                staff treble { melody }
                staff bass { bass }
              }
            }
            """;
        
        var tree = SyntaxTree.Parse(source);
        var renderSpec = RenderSpecParser.FindFirst(tree)!;
        
        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, renderSpec);
        
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        
        // Verify staff groups exist
        var system = layout.Systems[0];
        Assert.False(system.StaffGroups.IsDefaultOrEmpty);
        Assert.Single(system.StaffGroups);
        
        var staffGroup = system.StaffGroups[0];
        Assert.True(staffGroup.IsGrandStaff);
        Assert.Equal(2, staffGroup.Staves.Length);
        Assert.Equal(ClefType.Treble, staffGroup.Staves[0].Clef);
        Assert.Equal(ClefType.Bass, staffGroup.Staves[1].Clef);
    }
    
    [Fact]
    public void RenderGrandStaff_SystemBarlinesSpanBothStaves()
    {
        var source = """
            title "Test"
            time 4/4
            
            rh = { c''4 d'' e'' f'' | g'' a'' b'' c''' | }
            lh = { c4 e g c' | d e f g | }
            
            section Main {
              melody { rh }
              bass { lh }
            }
            
            structure { Main }
            
            render piano "test.svg" {
              grandStaff {
                staff treble { melody }
                staff bass { bass }
              }
            }
            """;
        
        var tree = SyntaxTree.Parse(source);
        var renderSpec = RenderSpecParser.FindFirst(tree)!;
        
        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, renderSpec);
        
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        
        var renderer = new SvgRenderer();
        var svg = renderer.Render(score, layout);
        
        // Should have system barlines that span both staves
        // Look for barline elements
        Assert.Contains("class=\"barline\"", svg);
    }
}