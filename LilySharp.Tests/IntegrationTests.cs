using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Tests;

public class IntegrationTests
{
    [Fact]
    public void ParseHappyBirthday()
    {
        var source = File.ReadAllText("../../../../samples/happy-birthday.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseFurElise()
    {
        var source = File.ReadAllText("../../../../samples/fur-elise.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseMinuet()
    {
        var source = File.ReadAllText("../../../../samples/minuet.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Equal(source, tree.ToFullString());
    }
    
    [Fact]
    public void ParseStructureDemo()
    {
        var source = File.ReadAllText("../../../../samples/structure-demo.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Equal(source, tree.ToFullString());
    }

    // ===== Rendering Regression Tests =====
    
    [Fact]
    public void RenderHappyBirthday_HasExpectedNotes()
    {
        var source = File.ReadAllText("../../../../samples/happy-birthday.lys");
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        
        // Happy Birthday should have notes in multiple measures
        Assert.NotEmpty(score.Voice.Measures);
        
        var totalNotes = score.Voice.Measures.Sum(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem or LilySharp.Core.Svg.Model.ChordItem));
        Assert.True(totalNotes >= 20, $"Happy Birthday should have at least 20 notes, but has {totalNotes}");
    }
    
    [Fact]
    public void RenderFurElise_HasExpectedNotes()
    {
        var source = File.ReadAllText("../../../../samples/fur-elise.lys");
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "rightHand");
        
        // Für Elise should have many notes
        Assert.NotEmpty(score.Voice.Measures);
        
        var totalNotes = score.Voice.Measures.Sum(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem or LilySharp.Core.Svg.Model.ChordItem));
        Assert.True(totalNotes >= 15, $"Für Elise rightHand should have at least 15 notes, but has {totalNotes}");
    }
    
    [Fact(Skip = "Requires Semantic layer for structure/repeat expansion")]
    public void RenderMinuet_HasExpectedStructure()
    {
        var source = File.ReadAllText("../../../../samples/minuet.lys");
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "rightHand");
        
        // Minuet section A has 4 measures with notes
        Assert.NotEmpty(score.Voice.Measures);
        Assert.True(score.Voice.Measures.Length >= 4, 
            $"Minuet should have at least 4 measures, but has {score.Voice.Measures.Length}");
        
        var totalNotes = score.Voice.Measures.Sum(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem or LilySharp.Core.Svg.Model.ChordItem));
        Assert.True(totalNotes >= 10, 
            $"Minuet rightHand should have at least 10 notes, but has {totalNotes}");
    }
    
    [Fact]
    public void RenderStructureDemo_HasExpectedSections()
    {
        var source = File.ReadAllText("../../../../samples/structure-demo.lys");
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "melody");
        
        Assert.NotEmpty(score.Voice.Measures);
        
        var totalNotes = score.Voice.Measures.Sum(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem or LilySharp.Core.Svg.Model.ChordItem));
        Assert.True(totalNotes >= 5, 
            $"Structure demo melody should have at least 5 notes, but has {totalNotes}");
    }
    
    [Fact]
    public void RenderSimpleMelody_SvgHasNoteheads()
    {
        var source = "{ c4 d e f | g a b c' | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        var renderer = new SvgRenderer();
        var svg = renderer.Render(score, layout);
        
        // Emmentaler black notehead (U+E0EA)
        var noteheadCount = System.Text.RegularExpressions.Regex.Matches(svg, "\uE0EA").Count;
        Assert.True(noteheadCount >= 8, 
            $"Should have at least 8 noteheads (one per note), but has {noteheadCount}");
    }
    
    [Fact]
    public void RenderSectionWithPart_CollectsNotes()
    {
        // This tests the section/part syntax that minuet.lys uses
        var source = @"
section A {
    melody {
        c4 d e f |
        g a b c' |
    }
}

structure {
    A
}

render score ""test.svg"" {
    staff treble { melody }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "melody");
        
        Assert.NotEmpty(score.Voice.Measures);
        
        var totalNotes = score.Voice.Measures.Sum(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem));
        Assert.True(totalNotes >= 8, 
            $"Should collect 8 notes from section A melody, but has {totalNotes}");
    }
    
    [Fact]
    public void RenderNewPartDeclaration_DoesNotBreakCollection()
    {
        // Tests that new 'part name { props }' syntax doesn't break collection
        var source = @"
part melody {
    clef: treble
}

section A {
    melody {
        c4 d e f |
    }
}

structure {
    A
}

render score ""test.svg"" {
    staff treble { melody }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "melody");
        
        var totalNotes = score.Voice.Measures.Sum(m => m.Items.Count(i => i is LilySharp.Core.Svg.Model.NoteItem));
        Assert.True(totalNotes >= 4, 
            $"Should collect 4 notes even with new part declaration syntax, but has {totalNotes}");
    }
}
