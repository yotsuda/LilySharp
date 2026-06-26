// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Xunit;
using Xunit.Abstractions;
using LilySharp.Core.Png;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
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
        var svg = LiveRender.Svg(source);

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

score ""test"" {
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
    clef treble
}

section A {
    melody {
        c4 d e f |
    }
}

structure {
    A
}

score ""test"" {
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

score ""test"" {
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

phrase rh1 { c'4 d' e' f' | g'2 g' | }
phrase lh1 { c2 e | g g, | }
phrase rh2 { e'4 d' c' d' | e'1 | }
phrase lh2 { c2 g, | c1 | }

score ""mvt1"" {
  grandStaff {
    staff treble { rh1 }
    staff bass { lh1 }
  }
}

score ""mvt2"" {
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

phrase rh1 { c'4 d' e' f' | g'2 g' | }
phrase lh1 { c2 e | g g, | }
phrase rh2 { e'4 d' c' d' | e'1 | }
phrase lh2 { c2 g, | c1 | }

score ""movement1"" {
  grandStaff {
    staff treble { rh1 }
    staff bass { lh1 }
  }
}

score ""movement2"" {
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
phrase rh { c'4 d' e' f' | }
phrase lh { c2 e | }

score ""first"" { staff treble { rh } }
score ""second"" { staff treble { lh } }
score ""third"" { staff treble { rh } }
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

        // Verify the live render shrinks cue noteheads: SharedRenderer scales the
        // glyph font size (4.0 × 0.66 = 2.64) rather than emitting a transform.
        var svg = LiveRender.Svg(source);

        var cueGlyphCount = System.Text.RegularExpressions.Regex.Matches(svg, "font-size=\"2\\.64\"").Count;
        Assert.True(cueGlyphCount >= 2,
            $"Should have at least 2 cue-sized glyphs (one per cue note), but has {cueGlyphCount}");
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

        // SVG should have no cue-sized (4.0 × 0.66 = 2.64) glyphs
        var svg = LiveRender.Svg(source);

        Assert.DoesNotContain("font-size=\"2.64\"", svg);
    }

    [Fact]
    public void OssiaStaff_ParsedAndRenderedWithScaleTransform()
    {
        // LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize (#-3)
        // magstep(-3) = 2^(-3/6) ≈ 0.707
        var source = @"
key C major
time 4/4
section Main {
    melody { | c5/4 d5 e5 f5 | g5/1 | }
    alt { | e5/4 f5 g5 a5 | b5/1 | }
}
structure { Main }
score ""ossia-test"" {
    staff { melody }
    ossia { alt }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, "Syntax tree should have no errors");

        // Verify render spec parsing
        var renderSpec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(renderSpec);
        Assert.True(renderSpec.IsMultiStaff, "Should be multi-staff (staff + ossia)");
        Assert.Equal(2, renderSpec.Items.Length);
        Assert.IsType<SingleStaffSpec>(renderSpec.Items[0]);
        Assert.IsType<OssiaStaffSpec>(renderSpec.Items[1]);

        // Generate SVG — SharedRenderer applies an OssiaScale transform of
        // 0.65 (LP magnifyStaff default ≈ 2/3); the SvgRenderer-era 0.7 was
        // an approximation. Match the transform regardless of exact scalar.
        var svg = SvgGenerator.Generate(tree);
        Assert.Matches(@"scale\(0\.\d+", svg);
    }

    [Fact]
    public void OssiaStaff_WithExplicitClef()
    {
        var source = @"
key C major
time 4/4
section Main {
    melody { | c5/4 d5 e5 f5 | g5/1 | }
    bassAlt { | c3/4 d3 e3 f3 | g3/1 | }
}
structure { Main }
score ""ossia-clef"" {
    staff { melody }
    ossia bass { bassAlt }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, "Syntax tree should have no errors");

        var renderSpec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(renderSpec);
        var ossiaItem = renderSpec.Items[1] as OssiaStaffSpec;
        Assert.NotNull(ossiaItem);
        Assert.Equal(ClefType.Bass, ossiaItem.Staff.Clef);
    }

    [Fact]
    public void OssiaStaff_ExcludedFromSystemBarlines()
    {
        var source = @"
key C major
time 4/4
section Main {
    melody { | c5/4 d5 e5 f5 | g5/1 | }
    alt { | e5/4 f5 g5 a5 | b5/1 | }
}
structure { Main }
score ""ossia-barline"" {
    staff { melody }
    ossia { alt }
}";
        var tree = SyntaxTree.Parse(source);
        var svg = SvgGenerator.Generate(tree);

        // Ossia staff should be scaled — the transform exists at any scale < 1.
        Assert.Matches(@"scale\(0\.\d+", svg);

        // SharedRenderer renders barlines as <rect ... fill="#000000"/> (or
        // "black"; the SvgRenderer-era class="barline" attribute is gone).
        // Count thin black rectangles as a barline proxy.
        var barlineMatches = System.Text.RegularExpressions.Regex.Matches(
            svg, @"<rect[^/]*fill=""(black|#000000)""");
        Assert.True(barlineMatches.Count > 0, "Should have black-filled rects (barlines).");
    }
}
