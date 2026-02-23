using Xunit;
using Xunit.Abstractions;
using LilySharp.Core.Png;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Tests;

[Trait("Category", "Integration")]
public class IntegrationTests
{
    private readonly ITestOutputHelper _output;

    public IntegrationTests(ITestOutputHelper output) => _output = output;
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

    [Fact]
    public void ShowcaseExpressions_TextSpannerBelowDynamics()
    {
        // Integration test: verify rit./accel. text spanners are placed below
        // overlapping dynamics per outside-staff-priority stacking
        var source = @"
tempo 120
time 4/4
key d major
part melody

phrase intro {
  d4@p e fis g |
  a4@cresc b cis d@f |
}

phrase bridge {
  d4@mf e@p@rit fis g |
  a2. r4 |
}

phrase finale {
  d4@accel e fis g |
  a4 b cis d@fermata |
}

section Main {
  melody { $intro $bridge $finale }
}

structure { Main }

render score ""test.svg"" {
  staff { melody }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "melody");
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);

        // Find rit. text spanner and verify it's below overlapping dynamics
        var ritSpanners = layout.TextSpannerLayouts.Where(ts => ts.Text == "rit.").ToList();
        Assert.NotEmpty(ritSpanners);
        var ritSpanner = ritSpanners[0];

        // Find dynamics that overlap with rit. in the same system
        var ritMeasureIndices = new HashSet<int>();
        foreach (var sys in layout.Systems)
        {
            if (sys.Measures.Any(m => m.MeasureIndex == ritSpanner.StartMeasureIndex))
            {
                foreach (var m in sys.Measures)
                    ritMeasureIndices.Add(m.MeasureIndex);
            }
        }

        var overlappingDynamics = layout.DynamicLayouts
            .Where(d => ritMeasureIndices.Contains(d.MeasureIndex))
            .Where(d => d.X + 1.5 > ritSpanner.StartX && d.X - 1.5 < ritSpanner.EndX)
            .ToList();

        Assert.NotEmpty(overlappingDynamics);
        var maxDynY = overlappingDynamics.Max(d => d.Y);
        Assert.True(ritSpanner.Y > maxDynY,
            $"rit. Y ({ritSpanner.Y:F2}) should be below max overlapping dynamic Y ({maxDynY:F2})");
    }

    [Fact]
    public void PngGenerator_ProducesValidPng()
    {
        var source = "{ c4 d e f | g a b c' | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var fontDir = Path.Combine(AppContext.BaseDirectory, "fonts");
        var options = new PngRenderOptions { Scale = 1.0f, FontDirectory = fontDir };
        var pngBytes = PngGenerator.Generate(tree, options);

        // PNG magic bytes: 0x89 'P' 'N' 'G'
        Assert.True(pngBytes.Length > 100, $"PNG too small: {pngBytes.Length} bytes");
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal((byte)'P', pngBytes[1]);
        Assert.Equal((byte)'N', pngBytes[2]);
        Assert.Equal((byte)'G', pngBytes[3]);

        _output.WriteLine($"PNG size: {pngBytes.Length} bytes");
    }
}
