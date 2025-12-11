using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

public class SvgTests
{
    private static string RenderSvg(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        var renderer = new SvgRenderer();
        return renderer.Render(score, layout);
    }
    
    [Fact]
    public void ExportSimpleNote()
    {
        var svg = RenderSvg("{ c4 }");
        
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
        Assert.Contains("class=\"music\"", svg);
    }
    
    [Fact]
    public void ExportNoteWithAccidental()
    {
        var svg = RenderSvg("{ cis4 }");
        
        // Should contain sharp accidental (U+E262)
        Assert.Contains("\uE262", svg);
    }
    
    [Fact]
    public void ExportRest()
    {
        var svg = RenderSvg("{ r4 }");
        
        // Should contain quarter rest (U+E4E5)
        Assert.Contains("\uE4E5", svg);
    }
    
    [Fact]
    public void ExportWithClef()
    {
        var svg = RenderSvg("clef treble { c4 }");
        
        // Should contain G clef (U+E050)
        Assert.Contains("\uE050", svg);
    }
    
    [Fact]
    public void ExportWithTimeSignature()
    {
        var svg = RenderSvg("time 4/4 { c4 }");
        
        // Should contain time sig digits (U+E084 = 4)
        Assert.Contains("\uE084", svg);
    }
    
    [Fact]
    public void ExportChord()
    {
        var svg = RenderSvg("{ <c e g>4 }");
        
        // Should contain multiple noteheads
        var noteheadCount = System.Text.RegularExpressions.Regex.Matches(svg, "\uE0A4").Count;
        Assert.True(noteheadCount >= 3);
    }
    
    [Fact]
    public void ExportBarline()
    {
        var svg = RenderSvg("{ c4 | d4 }");
        
        // Barline is now drawn using SMuFL glyph (class="music")
        Assert.Contains(SmuflGlyphs.BarlineSingle.ToString(), svg);
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
    
    [Fact]
    public void ExportRepeatBarlines()
    {
        var source = @"
section A {
    melody relative c' { c4 d e | }
}
structure {
    |: A :|
}
render score ""test.svg"" {
    staff treble { melody }
}
";
        var svg = RenderSvg(source);
        
        // Check for repeat barlines (SMuFL glyphs U+E040 and U+E041)
        // RepeatLeft = U+E040, RepeatRight = U+E041
        Assert.Contains("\uE040", svg); // |: 
        Assert.Contains("\uE041", svg); // :|
    }


    [Fact]
    public void AccidentalCollisionTest_SpringLayout()
    {
        // Test that accidentals don't overlap with previous notes
        var source = @"{ c4 cis4 d4 dis4 | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        
        var measure = layout.Systems[0].Measures[0];
        Console.WriteLine("Accidental collision test:");
        Console.WriteLine($"Measure width: {measure.Width:F1}");
        
        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            var musicItem = score.Voice.Measures[0].Items[i];
            var leftExtent = SpacingRules.CalculateLeftExtent(musicItem);
            var rightExtent = SpacingRules.CalculateRightExtent(musicItem);
            
            string accidental = musicItem switch
            {
                NoteItem note => note.Accidental ?? "none",
                _ => "n/a"
            };
            
            double leftEdge = item.X - leftExtent;
            double rightEdge = item.X + rightExtent;
            
            Console.WriteLine($"  Item {i}: X={item.X:F1}, W={item.Width:F1}, Acc={accidental}, LeftExt={leftExtent:F1}, RightExt={rightExtent:F1}");
            Console.WriteLine($"          LeftEdge={leftEdge:F1}, RightEdge={rightEdge:F1}");
            
            // Check for collision with previous item
            if (i > 0)
            {
                var prevItem = measure.Items[i - 1];
                var prevMusicItem = score.Voice.Measures[0].Items[i - 1];
                var prevRightExtent = SpacingRules.CalculateRightExtent(prevMusicItem);
                double prevRightEdge = prevItem.X + prevRightExtent;
                double gap = leftEdge - prevRightEdge;
                Console.WriteLine($"          Gap from prev: {gap:F1}");
                Assert.True(gap >= 0, $"Item {i} overlaps with item {i-1}! Gap={gap:F1}");
            }
        }
    }

    [Fact]
    public void GenerateFurEliseSvg_Check()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\fur-elise.lys");
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "rightHand");
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        var renderer = new SvgRenderer();
        var svg = renderer.Render(score, layout);
        File.WriteAllText(@"C:\MyProj\LilySharp\samples\fur-elise-check.svg", svg);
    }
}