using Lilysharp.Core.Svg;
using Lilysharp.Core.Syntax;
using Xunit;

namespace Lilysharp.Tests;

public class SvgTests
{
    [Fact]
    public void ExportSimpleNote()
    {
        var source = "{ c4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new SvgExporter();
        
        var svg = exporter.Export(tree);
        
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
        Assert.Contains("class=\"music\"", svg);
    }
    
    [Fact]
    public void ExportNoteWithAccidental()
    {
        var source = "{ cis4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new SvgExporter();
        
        var svg = exporter.Export(tree);
        
        // Should contain sharp accidental (U+E262)
        Assert.Contains("\uE262", svg);
    }
    
    [Fact]
    public void ExportRest()
    {
        var source = "{ r4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new SvgExporter();
        
        var svg = exporter.Export(tree);
        
        // Should contain quarter rest (U+E4E5)
        Assert.Contains("\uE4E5", svg);
    }
    
    [Fact]
    public void ExportWithClef()
    {
        var source = "clef treble { c4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new SvgExporter();
        
        var svg = exporter.Export(tree);
        
        // Should contain G clef (U+E050)
        Assert.Contains("\uE050", svg);
    }
    
    [Fact]
    public void ExportWithTimeSignature()
    {
        var source = "time 4/4 { c4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new SvgExporter();
        
        var svg = exporter.Export(tree);
        
        // Should contain time sig digits (U+E084 = 4)
        Assert.Contains("\uE084", svg);
    }
    
    [Fact]
    public void ExportChord()
    {
        var source = "{ <c e g>4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new SvgExporter();
        
        var svg = exporter.Export(tree);
        
        // Should contain multiple noteheads
        var noteheadCount = System.Text.RegularExpressions.Regex.Matches(svg, "\uE0A4").Count;
        Assert.True(noteheadCount >= 3);
    }
    
    [Fact]
    public void ExportBarline()
    {
        var source = "{ c4 | d4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new SvgExporter();
        
        var svg = exporter.Export(tree);
        
        Assert.Contains("class=\"barline\"", svg);
    }
    
    [Fact]
    public void SmuflGlyphs_GetNotehead()
    {
        Assert.Equal('\uE0A2', SmuflGlyphs.GetNotehead(1)); // Whole
        Assert.Equal('\uE0A3', SmuflGlyphs.GetNotehead(2)); // Half
        Assert.Equal('\uE0A4', SmuflGlyphs.GetNotehead(4)); // Quarter
        Assert.Equal('\uE0A4', SmuflGlyphs.GetNotehead(8)); // Eighth
    }
    
    [Fact]
    public void SmuflGlyphs_GetRest()
    {
        Assert.Equal('\uE4E3', SmuflGlyphs.GetRest(1));  // Whole
        Assert.Equal('\uE4E4', SmuflGlyphs.GetRest(2));  // Half
        Assert.Equal('\uE4E5', SmuflGlyphs.GetRest(4));  // Quarter
        Assert.Equal('\uE4E6', SmuflGlyphs.GetRest(8));  // Eighth
        Assert.Equal('\uE4E7', SmuflGlyphs.GetRest(16)); // 16th
    }
    
    [Fact]
    public void SmuflGlyphs_GetFlag()
    {
        Assert.Equal('\uE240', SmuflGlyphs.GetFlag(8, true));   // 8th up
        Assert.Equal('\uE241', SmuflGlyphs.GetFlag(8, false));  // 8th down
        Assert.Equal('\uE242', SmuflGlyphs.GetFlag(16, true));  // 16th up
        Assert.Null(SmuflGlyphs.GetFlag(4, true));              // No flag for quarter
    }
}