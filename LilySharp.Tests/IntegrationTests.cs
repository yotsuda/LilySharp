using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Tests;

public class IntegrationTests
{
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

        var noteheadCount = System.Text.RegularExpressions.Regex.Matches(svg, "\uE0EA").Count;
        Assert.True(noteheadCount >= 8,
            $"Should have at least 8 noteheads (one per note), but has {noteheadCount}");
    }

    [Fact]
    public void RenderSectionWithPart_CollectsNotes()
    {
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