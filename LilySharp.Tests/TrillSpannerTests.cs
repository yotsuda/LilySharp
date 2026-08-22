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

using LilySharp.Core.Semantics;
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
section A { melody { m } }
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
section A { melody { m } }
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
section A { melody { m } }
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
section A { melody { m } }
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

    /// <summary>
    /// The spanner has ONE spelling, LilyPond's: @startTrillSpan /
    /// @stopTrillSpan. '@trillSpan(start)' was a second way to say the same
    /// thing — and an argument on an event that has none in LilyPond either — so
    /// it is gone rather than silently accepted.
    /// </summary>
    [Fact]
    public void TrillSpanner_CompoundSyntax_IsNoLongerAccepted()
    {
        var source = @"
part melody { clef treble }
phrase m { c'4@trillSpan(start) d e f@trillSpan(stop) | g4 a b c' | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var singleScore = new MeasureCollector().Collect(tree, "melody");

        Assert.Empty(singleScore.TrillSpanners);

        var validator = new AnnotationNameValidator();
        validator.Validate(tree);
        Assert.Equal(2, validator.Diagnostics
            .Count(d => d.Code == DiagnosticCodes.UnknownAnnotation));
    }

    [Fact]
    public void MeasureStartStop_EndsTheWaveAtTheBarline_NotTheStopColumn()
    {
        // slur-vertical-skylines.ly (the outside-staff-vs-slur book): the trill
        // spans g1~ | g1@stopTrillSpan — the stop event lands on a measure
        // START, so the Bar_engraver rewrites the right bound to the BAR LINE
        // and the wave never enters the stop measure. LP draws exactly 3
        // trill_element repetitions (staff-rel X 38.30/39.30/40.30); before the
        // to-barline port Lily# ran the wave to the stop column (5 elements).
        // The tr glyph and wave heights and the f dynamic are the book's CLAIM
        // (priorities 50/250 sit pointwise, not at the slur's apex) — all
        // pinned against the LP twin (audit\lpreg\slurvsky.{lys,-gen.ly}).
        // LILYPOND-REF: lily/bar-engraver.cc:580-588 acknowledge_end_spanner
        // LILYPOND-REF: scm/define-grobs.scm TrillSpanner — (to-barline . #t)
        string svg = Render(
            "octave absolute\n\n" +
            "f8@text(\"rit\").up( c'8 f' c'' f'') r8 r4 |\n" +
            "c''2( c'2 |\n" +
            "g1)~@startTrillSpan |\n" +
            "g1@stopTrillSpan |\n" +
            "g1(@f |\n" +
            "g,1) |\n");
        double middle = MiddleLineY(svg);

        var waves = MusicGlyphs(svg, LilySharp.Core.Svg.EmmentalerGlyphs.OrnTrillElement);
        Assert.Equal(3, waves.Count);                          // LP: 3 repetitions
        Assert.Equal(38.30, waves[0].X, 0.05);                 // LP 38.3022
        Assert.Equal(40.30, waves[2].X, 0.05);                 // LP 40.3022
        Assert.Equal(-3.15, waves[0].Y - middle, 0.011);       // LP -3.15

        var tr = Assert.Single(MusicGlyphs(svg, LilySharp.Core.Svg.EmmentalerGlyphs.OrnTrill));
        Assert.Equal(-2.55, tr.Y - middle, 0.011);             // LP -2.55

        var f = System.Text.RegularExpressions.Regex.Match(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*font-weight=\"bold\"[^>]*>f</text>");
        Assert.True(f.Success, "no f dynamic in the SVG");
        Assert.Equal(5.84, double.Parse(f.Groups[2].Value) - middle, 0.05);  // LP 5.8421
    }

    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new SvgRenderOptions { EmbedFont = false });

    /// <summary>The middle staff line's device Y: the 3rd of the five long horizontals.</summary>
    private static double MiddleLineY(string svg)
    {
        var lineYs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                && double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value) > 5)
            .Select(m => double.Parse(m.Groups[2].Value))
            .Distinct().OrderBy(v => v).ToList();
        Assert.Equal(5, lineYs.Count);
        return lineYs[2];
    }

    /// <summary>All music glyphs of one codepoint: (X, Y) in document order.</summary>
    private static List<(double X, double Y)> MusicGlyphs(string svg, char glyph) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[3].Value[0] == glyph)
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)))
            .ToList();

    [Fact]
    public void TrillSpanner_NoStopEvent_RunsToEndOfScore()
    {
        // An unpaired start runs to the END of the score (it used to be silently
        // dropped): the virtual stop sits at item 0 one measure PAST the last,
        // which the engraver's to-barline branch turns into "stop short of the
        // final barline" — the same term a written stop-at-bar gets (LP measured:
        // both gaps 1.75, trillimpl/trillbar twins,
        // trill-spanner-terminated-implicitly.ly).
        // LILYPOND-REF: scm/scheme-engravers.scm:1797-1860 Trill_spanner_engraver
        //   — finalize sets the open spanner's RIGHT bound to currentCommandColumn.
        var source = @"
part melody { clef treble }
phrase m { c'4@startTrillSpan d e f | }
section A { melody { m } }
form main { A }
score main ""test"" { staff melody }
";
        var tree = SyntaxTree.Parse(source);
        var singleScore = new MeasureCollector().Collect(tree, "melody");

        var spanner = Assert.Single(singleScore.TrillSpanners);
        Assert.Equal(0, spanner.StartMeasureIndex);
        Assert.Equal(0, spanner.StartItemIndex);
        Assert.Equal(1, spanner.EndMeasureIndex); // one past the last measure
        Assert.Equal(0, spanner.EndItemIndex);
    }
}
