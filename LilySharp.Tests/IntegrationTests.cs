using Xunit;
using Xunit.Abstractions;
using LilySharp.Core.Png;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg;
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

    [Fact]
    public void GenerateAll_MultipleRenderBlocks_ProducesSeparateOutputs()
    {
        var source = @"
title ""Test""
tempo 120
time 4/4

rh1 = { c'4 d' e' f' | g'2 g' | }
lh1 = { c2 e | g g, | }
rh2 = { e'4 d' c' d' | e'1 | }
lh2 = { c2 g, | c1 | }

render score ""mvt1"" {
  grandStaff {
    staff treble { rh1 }
    staff bass { lh1 }
  }
}

render score ""mvt2"" {
  grandStaff {
    staff treble { rh2 }
    staff bass { lh2 }
  }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var results = SvgGenerator.GenerateAll(tree);

        Assert.Equal(2, results.Count);
        Assert.Equal("mvt1", results[0].Filename);
        Assert.Equal("mvt2", results[1].Filename);
        Assert.Contains("<svg", results[0].Svg);
        Assert.Contains("<svg", results[1].Svg);
        // Content should differ because they reference different variables
        Assert.NotEqual(results[0].Svg, results[1].Svg);
    }

    [Fact]
    public void GenerateMultiMovement_CombinesMovements()
    {
        var source = @"
title ""Test""
tempo 120
time 4/4

rh1 = { c'4 d' e' f' | g'2 g' | }
lh1 = { c2 e | g g, | }
rh2 = { e'4 d' c' d' | e'1 | }
lh2 = { c2 g, | c1 | }

render score ""movement1"" {
  grandStaff {
    staff treble { rh1 }
    staff bass { lh1 }
  }
}

render score ""movement2"" {
  grandStaff {
    staff treble { rh2 }
    staff bass { lh2 }
  }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var svg = SvgGenerator.GenerateMultiMovement(tree);

        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
        // Should contain movement title for second movement
        Assert.Contains("movement2", svg);
        // Should have transform groups for each movement
        Assert.Contains("translate", svg);
    }

    [Fact]
    public void GenerateAll_NoRenderBlocks_FallsBackToDefault()
    {
        var source = "{ c4 d e f | g a b c' | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var results = SvgGenerator.GenerateAll(tree);

        Assert.Single(results);
        Assert.Equal(string.Empty, results[0].Filename);
        Assert.Contains("<svg", results[0].Svg);
    }

    [Fact]
    public void FindAll_ReturnsAllRenderSpecs()
    {
        var source = @"
rh = { c'4 d' e' f' | }
lh = { c2 e | }

render score ""first"" { staff treble { rh } }
render score ""second"" { staff treble { lh } }
render score ""third"" { staff treble { rh } }
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var specs = LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree);

        Assert.Equal(3, specs.Count);
        Assert.Equal("first", specs[0].OutputFile);
        Assert.Equal("second", specs[1].OutputFile);
        Assert.Equal("third", specs[2].OutputFile);
    }

    [Fact]
    public void CueNotes_RenderedWithScaleTransform()
    {
        // LILYPOND-REF: ly/engraver-init.ly CueVoice context — fontSize = #-4
        var source = "{ c'4 d' e'@cue f'@cue | g'1 | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        // Verify cue flag on collected items
        var items = score.Voice.Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.NoteItem>().ToList();
        Assert.True(items.Count >= 4, $"Should have at least 4 notes, but has {items.Count}");
        Assert.False(items[0].IsCue, "c' should not be cue");
        Assert.False(items[1].IsCue, "d' should not be cue");
        Assert.True(items[2].IsCue, "e'@cue should be cue");
        Assert.True(items[3].IsCue, "f'@cue should be cue");

        // Verify SVG contains scale transforms for cue notes
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        var renderer = new SvgRenderer();
        var svg = renderer.Render(score, layout);

        var scaleCount = System.Text.RegularExpressions.Regex.Matches(svg, @"scale\(0\.66\)").Count;
        Assert.True(scaleCount >= 2,
            $"Should have at least 2 scale(0.66) transforms (one per cue note), but has {scaleCount}");
    }

    [Fact]
    public void CueChords_RenderedWithScaleTransform()
    {
        var source = "{ <c' e'>4 <d' f'>@cue | <c' e' g'>1 | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        var chords = score.Voice.Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.ChordItem>().ToList();
        Assert.True(chords.Count >= 2, $"Should have at least 2 chords, but has {chords.Count}");
        Assert.False(chords[0].IsCue, "First chord should not be cue");
        Assert.True(chords[1].IsCue, "Second chord @cue should be cue");
    }

    [Fact]
    public void CueNotes_NonCueNotesUnaffected()
    {
        // Ensure @cue doesn't affect non-annotated notes
        var source = "{ c'4 d' e' f' | g'1 | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        var items = score.Voice.Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.NoteItem>().ToList();
        Assert.All(items, note => Assert.False(note.IsCue));

        // SVG should have no scale(0.66) transforms
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        var renderer = new SvgRenderer();
        var svg = renderer.Render(score, layout);

        Assert.DoesNotContain("scale(0.66)", svg);
    }
}
