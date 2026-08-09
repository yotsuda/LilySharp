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

        var noteheadCount = System.Text.RegularExpressions.Regex.Matches(svg, EmmentalerGlyphs.NoteheadBlack.ToString()).Count;
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

form main {
    A
}

score main ""test"" {
    staff treble melody
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

form main {
    A
}

score main ""test"" {
    staff treble melody
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
    public void ShowcaseExpressions_TextSpannerAboveDynamics()
    {
        // Integration test: rit./accel. text spanners are placed ABOVE the staff
        // (LilyPond TextSpanner direction=UP), clear of the below-staff dynamics.
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

form main { Main }

score main ""test"" {
  staff melody
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
        // Dynamics store Y-up now; reflect to device (single-staff → middle at 2.0)
        // to compare against the still-device TextSpanner Y. Y grows downward: an
        // above-staff spanner sits at a SMALLER Y than the below-staff dynamics.
        var maxDynY = overlappingDynamics.Max(d => 2.0 - d.YUp);
        // The text spanner now stores Y-up from the system top; its device value is -YUp.
        double ritY = -ritSpanner.YUp;
        Assert.True(ritY < maxDynY,
            $"rit. Y ({ritY:F2}) should be above (smaller Y than) max overlapping dynamic Y ({maxDynY:F2})");
    }

    [Fact]
    public void PngGenerator_ProducesValidPng()
    {
        var tree = MusicSource.Parse("c4 d e f | g a b c' |");
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

score main ""mvt1"" {
  grandStaff {
    staff treble rh1
    staff bass lh1
  }
}

score main ""mvt2"" {
  grandStaff {
    staff treble rh2
    staff bass lh2
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

score main ""movement1"" {
  grandStaff {
    staff treble rh1
    staff bass lh1
  }
}

score main ""movement2"" {
  grandStaff {
    staff treble rh2
    staff bass lh2
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
        // Hand-written rather than MusicSource.Wrap: the subject is a document with NO
        // score block, and the wrapper always emits one.
        var source = "part melody\nsection A { melody { c4 d e f | g a b c' | } }\nform main { ~A }\n";
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

score main ""first"" { staff treble rh }
score main ""second"" { staff treble lh }
score main ""third"" { staff treble rh }
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
        var source = MusicSource.Wrap("c'4 d' cue { e'4 f' } | g'1 |");
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);

        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        // Verify cue flag on collected items
        var items = score.Voice.Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.NoteItem>().ToList();
        Assert.True(items.Count >= 4, $"Should have at least 4 notes, but has {items.Count}");
        Assert.False(items[0].IsCue, "c' should not be cue");
        Assert.False(items[1].IsCue, "d' should not be cue");
        Assert.True(items[2].IsCue, "e' inside cue { } should be cue");
        Assert.True(items[3].IsCue, "f' inside cue { } should be cue");

        // Verify the live render shrinks cue noteheads: SharedRenderer scales the glyph font
        // size rather than emitting a transform.
        // ⚠️ THE EXPECTED SIZE IS DERIVED, NOT TYPED. It read "2.64" until 2026-08-03 — 4.0 ×
        // a hand-written 0.66 — so the day the cue took LilyPond's own font-size −4 this test
        // failed for the improvement. A test that hard-codes what a constant computes to is a
        // second spelling of that constant.
        double cueSize = 4.0 * LilySharp.Core.Svg.EngravingDefaults.CueScale;
        var svg = LiveRender.Svg(source);

        var cueGlyphCount = System.Text.RegularExpressions.Regex.Matches(
            svg, $"font-size=\"{cueSize:F2}\"").Count;
        Assert.True(cueGlyphCount >= 2,
            $"Should have at least 2 cue-sized glyphs (one per cue note), but has {cueGlyphCount}");
    }

    [Fact]
    public void CueChords_RenderedWithScaleTransform()
    {
        var source = MusicSource.Wrap("<c' e'>4 cue { <d' f'>4 } | <c' e' g'>1 |");
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
        var source = MusicSource.Wrap("c'4 d' e' f' | g'1 |");
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
    melody { c'4 d e f | g1 | }
    alt { e'4 f g a | b1 | }
}
form main { Main }
score main ""ossia-test"" {
    staff melody
    ossia alt
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
    melody { c'4 d e f | g1 | }
    bassAlt { c,4 d e f | g1 | }
}
form main { Main }
score main ""ossia-clef"" {
    staff melody
    ossia bass bassAlt
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
    melody { c'4 d e f | g1 | }
    alt { e'4 f g a | b1 | }
}
form main { Main }
score main ""ossia-barline"" {
    staff melody
    ossia alt
}";
        var tree = SyntaxTree.Parse(source);
        var svg = SvgGenerator.Generate(tree);

        // Ossia staff should be scaled — the transform exists at any scale < 1.
        Assert.Matches(@"scale\(0\.\d+", svg);

        // Barlines render as filled <rect>s. A black fill is now the SVG default
        // (omitted to shrink the document), so a barline rect carries NO fill attribute —
        // unlike the fill="none" hit/outline rects. Count a non-none rect as a barline proxy.
        bool hasFilledRect = false;
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(svg, @"<rect\b[^>]*>"))
            if (!m.Value.Contains("fill=\"none\"")) { hasFilledRect = true; break; }
        Assert.True(hasFilledRect, "Should have a filled rect (barline).");
    }
}
