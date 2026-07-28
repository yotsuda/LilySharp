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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Tests for trill spanner functionality.
/// LILYPOND-REF: scm/scheme-engravers.scm
/// </summary>
[Trait("Category", "Unit")]
public class TrillSpannerTests
{
    private readonly ITestOutputHelper _output;

    public TrillSpannerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TrillSpannerItem_StoresStartEndIndices()
    {
        var item = new TrillSpannerItem(0, 1, 2, 3, 42);
        Assert.Equal(0, item.StartMeasureIndex);
        Assert.Equal(1, item.StartItemIndex);
        Assert.Equal(2, item.EndMeasureIndex);
        Assert.Equal(3, item.EndItemIndex);
        Assert.Equal(42, item.SourcePosition);
    }

    [Fact]
    public void StartTrillSpan_Detected_InSingleMeasure()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4@startTrillSpan d e f@stopTrillSpan | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, spec);

        // Convert to Score for TrillSpanners access
        var singleScore = collector.Collect(tree, "melody");

        _output.WriteLine($"TrillSpanners count: {singleScore.TrillSpanners.Length}");
        Assert.Single(singleScore.TrillSpanners);

        var spanner = singleScore.TrillSpanners[0];
        Assert.Equal(0, spanner.StartMeasureIndex);
        Assert.Equal(0, spanner.StartItemIndex);  // c' (first note)
        Assert.Equal(0, spanner.EndMeasureIndex);
        Assert.Equal(3, spanner.EndItemIndex);     // f (fourth note)
    }

    [Fact]
    public void StartTrillSpan_SpanningMultipleMeasures()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4@startTrillSpan d e f | g4 a b c'@stopTrillSpan | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var singleScore = new MeasureCollector().Collect(tree, "melody");

        Assert.Single(singleScore.TrillSpanners);

        var spanner = singleScore.TrillSpanners[0];
        Assert.Equal(0, spanner.StartMeasureIndex);
        Assert.Equal(1, spanner.EndMeasureIndex);
    }

    [Fact]
    public void MultipleTrillSpanners_PairedCorrectly()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4@startTrillSpan d@stopTrillSpan e4@startTrillSpan f@stopTrillSpan | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var singleScore = new MeasureCollector().Collect(tree, "melody");

        _output.WriteLine($"TrillSpanners count: {singleScore.TrillSpanners.Length}");
        Assert.Equal(2, singleScore.TrillSpanners.Length);

        // First spanner: c' to d
        Assert.Equal(0, singleScore.TrillSpanners[0].StartItemIndex);
        Assert.Equal(1, singleScore.TrillSpanners[0].EndItemIndex);

        // Second spanner: e to f
        Assert.Equal(2, singleScore.TrillSpanners[1].StartItemIndex);
        Assert.Equal(3, singleScore.TrillSpanners[1].EndItemIndex);
    }

    [Fact]
    public void TrillSpanner_RenderedInSvg_ContainsGlyph()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4@startTrillSpan d e f@stopTrillSpan | g4 a b c' | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain trill glyph (U+E05C = OrnTrill / scripts.trill)
        Assert.Contains(EmmentalerGlyphs.OrnTrill.ToString(), svg);
        // SharedRenderer emits the wavy line as a chain of <line> segments
        // rather than a single classed <path>. The SvgRenderer-era
        // "trill-spanner-line" class no longer exists post Phase 3-B; verify
        // multiple short segments are present near the trill glyph instead.
        var lineCount = System.Text.RegularExpressions.Regex.Matches(svg, "<line ").Count;
        Assert.True(lineCount > 5, $"Expected multiple wavy-line segments, got {lineCount}.");
    }

    [Fact]
    public void TrillSpanner_CompoundSyntax_Detected()
    {
        // Test @trillSpan(start) / @trillSpan(stop) compound syntax
        var source = @"
part melody { clef treble }
phrase m { c'4@trillSpan(start) d e f@trillSpan(stop) | g4 a b c' | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var singleScore = new MeasureCollector().Collect(tree, "melody");

        _output.WriteLine($"TrillSpanners count: {singleScore.TrillSpanners.Length}");
        Assert.Single(singleScore.TrillSpanners);

        var spanner = singleScore.TrillSpanners[0];
        Assert.Equal(0, spanner.StartMeasureIndex);
        Assert.Equal(0, spanner.EndMeasureIndex);
    }

    [Fact]
    public void TrillSpanner_NoStopEvent_NoPairing()
    {
        // Unpaired start should not produce a spanner
        var source = @"
part melody { clef treble }
phrase m { c'4@startTrillSpan d e f | }
section A { melody { $m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var singleScore = new MeasureCollector().Collect(tree, "melody");

        Assert.Empty(singleScore.TrillSpanners);
    }
}
