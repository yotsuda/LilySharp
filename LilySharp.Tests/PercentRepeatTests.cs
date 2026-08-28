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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PercentRepeatTests
{
    // --- PercentRepeatEngraver ---

    [Fact]
    public void PercentRepeatEngraver_Calculate_EmptyInput()
    {
        var result = PercentRepeatEngraver.Calculate(
            ImmutableArray<PercentRepeatItem>.Empty,
            ImmutableArray<SystemLayout>.Empty,
            ImmutableArray<MeasureLayout>.Empty);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void PercentRepeatEngraver_Calculate_ProducesLayout()
    {
        var items = ImmutableArray.Create(
            new PercentRepeatItem(1, 0));

        var itemLayout = new ItemLayout(0, 0, 1.0);
        var ml0 = new MeasureLayout(0, 0, 10.0, ImmutableArray.Create(itemLayout));
        var ml1 = new MeasureLayout(1, 10.0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0,
            ImmutableArray.Create(ml0, ml1));

        var result = PercentRepeatEngraver.Calculate(
            items,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(ml0, ml1));

        Assert.Single(result);
        Assert.Equal(1, result[0].MeasureIndex);
        // Center of measure 1: ml1.X(10) + ml1.Width(10)/2 = 15
        Assert.Equal(15.0, result[0].X, 1);
    }

    [Fact]
    public void PercentRepeatEngraver_Calculate_CenteredVertically()
    {
        var items = ImmutableArray.Create(
            new PercentRepeatItem(0, 0));

        var itemLayout = new ItemLayout(0, 0, 1.0);
        var ml = new MeasureLayout(0, 0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 0, 50.0, 5.0,
            ImmutableArray.Create(ml));

        var result = PercentRepeatEngraver.Calculate(
            items,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(ml));

        Assert.Single(result);
        Assert.Equal(0.0, result[0].YUp, 1);  // Y-up: centred on the staff middle
    }

    // --- MeasureCollector integration ---

    [Fact]
    public void Collector_PercentRepeat_Basic()
    {
        // repeat percent 2 { c4 d e f } → 2 measures total, measure 1 is percent repeat
        var source = "repeat percent 2 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // Should have 2 measures (body played twice)
        Assert.Equal(2, score.Voice.Measures.Length);

        // One percent repeat marker (on measure 1)
        Assert.Single(score.PercentRepeats);
        Assert.Equal(1, score.PercentRepeats[0].MeasureIndex);
    }

    [Fact]
    public void Collector_PercentRepeat_FourTimes()
    {
        // repeat percent 4 { c4 d e f } → 4 measures, measures 1-3 are percent repeats
        var source = "repeat percent 4 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(4, score.Voice.Measures.Length);
        Assert.Equal(3, score.PercentRepeats.Length);
        Assert.Equal(1, score.PercentRepeats[0].MeasureIndex);
        Assert.Equal(2, score.PercentRepeats[1].MeasureIndex);
        Assert.Equal(3, score.PercentRepeats[2].MeasureIndex);
    }

    [Fact]
    public void Collector_PercentRepeat_NotesPreserved()
    {
        // The first measure should have actual notes
        var source = "repeat percent 2 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // First measure has the original notes
        Assert.True(score.Voice.Measures[0].Items.Length >= 4);
    }

    [Fact]
    public void Collector_PercentRepeat_NoPercentForFirst()
    {
        var source = "repeat percent 3 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // Percent repeat markers should NOT include measure 0
        Assert.DoesNotContain(score.PercentRepeats, pr => pr.MeasureIndex == 0);
    }

    [Fact]
    public void Collector_VoltaRepeat_NoPercentMarkers()
    {
        // Symbolic volta repeats should not create percent markers
        var source = "{ |: c4 d e f :| }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    [Fact]
    public void Collector_UnfoldRepeat_NoPercentMarkers()
    {
        var source = "repeat unfold 2 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    [Fact]
    public void Collector_NoRepeat_NoPercentMarkers()
    {
        var source = "c4 d e f | g a b c'";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    // --- Renderer: the sign lives in its staff's own frame ---

    [Fact]
    public void Renderer_TabPercent_CentresOnTabMiddleAndScalesByStringSpace()
    {
        // LILYPOND-REF: lily/percent-repeat-interface.cc:40-49 brew_slash —
        // "Scale everything by staff-space": a TabStaff's staff-space is 1.5, so
        // its slash runs 3.0 with a 0.72 thickness against the notation staff's
        // 2.0 / 0.48; and the Y-offset-less grob's stencil is align_to'd CENTER
        // on the staff's own middle — 3.75 below a six-string tab's top line,
        // not the 5-line frame's 2.0. Pinned against LilyPond 2.26 SVG output
        // (audit\lpreg\harakiri-percent.ly: tab slash run 3, horizontal edge
        // 1.0182 = 0.72·√2, centre on the tab middle to the digit).
        // ⚠️ THE SLASH IS A POLYGON, not a stroked line (2026-08-28). Its thickness is no
        // longer an attribute to read but the shape's own horizontal edge — the
        // `x_width = hypot (t, t/s)` of lookup.cc's repeat_slash, which on a 45° slash is
        // the perpendicular thickness times √2. That the two are related by exactly √2 is
        // what this test now reads back.
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            part g { }
            section A { g { repeat percent 2 { c4 c c c | } } }
            form main { ~A }
            score main { staff g tab g }
            """);

        // Horizontal staff lines: 5 notation lines (1.0 apart) above 6 tab
        // strings (1.5 apart).
        var horizontals = new List<double>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(svg, "<line ([^>]*)/>"))
        {
            var a = m.Groups[1].Value;
            double x1 = Attr(a, "x1"), x2 = Attr(a, "x2"), y1 = Attr(a, "y1"), y2 = Attr(a, "y2");
            // Staff/string rows all START at the system's left edge; ledger
            // lines and the fret-digit-split later segments of a string do not.
            if (y1 == y2 && x2 > x1 && x1 < 0.05)
                horizontals.Add(y1);
        }
        horizontals.Sort();
        Assert.Equal(11, horizontals.Count);
        double staffMid = horizontals[2];
        double tabMid = (horizontals[5] + horizontals[10]) / 2;

        // The four points are (x0,bottom) (x0+xw,bottom) (x0+xw+w,top) (x0+w,top).
        var slashes = Slashes(svg);
        var staffSlash = Assert.Single(slashes, s => System.Math.Abs(s.Cy - staffMid) < 0.5);
        Assert.Equal(2.0, staffSlash.W, 2);
        Assert.Equal(2.0, staffSlash.Height, 2);
        Assert.Equal(0.48 * System.Math.Sqrt(2), staffSlash.XWidth, 2);

        var tabSlash = Assert.Single(slashes, s => System.Math.Abs(s.Cy - tabMid) < 0.5);
        Assert.Equal(3.0, tabSlash.W, 2);
        Assert.Equal(3.0, tabSlash.Height, 2);
        Assert.Equal(0.72 * System.Math.Sqrt(2), tabSlash.XWidth, 2);

        // Dots: ±0.5·staff-space vertically (percent-repeat-interface.cc:76-77) —
        // ±0.75 on the tab; the dot glyph itself keeps its size.
        var dots = System.Text.RegularExpressions.Regex.Matches(svg, "<circle ([^>]*)/>")
            .Select(c => (Cx: Attr(c.Groups[1].Value, "cx"), Cy: Attr(c.Groups[1].Value, "cy")))
            .Where(c => System.Math.Abs(c.Cx - tabSlash.Cx) < 2
                && System.Math.Abs(c.Cy - tabMid) < 2)
            .OrderBy(c => c.Cy).ToList();
        Assert.Equal(2, dots.Count);
        Assert.Equal(tabMid - 0.75, dots[0].Cy, 2);
        Assert.Equal(tabMid + 0.75, dots[1].Cy, 2);
    }

    /// <summary>
    /// The percent slashes in an SVG, each read back as LilyPond's own four numbers: the
    /// parallelogram's horizontal edge <c>x_width</c>, its slant <c>w</c>, its height, and
    /// its centre. <c>lookup.cc</c>'s repeat_slash emits exactly these four points, in this
    /// order, so a shape that stops being that parallelogram fails to parse rather than
    /// quietly measuring as something else.
    /// </summary>
    private static List<(double X0, double Cx, double Cy, double XWidth, double W, double Height)> Slashes(string svg)
    {
        var found = new List<(double, double, double, double, double, double)>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(svg, "<polygon points=\"([^\"]+)\""))
        {
            var pts = m.Groups[1].Value.Split(' ')
                .Select(p => p.Split(','))
                .Select(p => (X: double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                              Y: double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
            if (pts.Count != 4 || pts[0].Y != pts[1].Y || pts[2].Y != pts[3].Y || IsBeamRibbon(pts))
                continue;
            double xWidth = pts[1].X - pts[0].X;
            double w = pts[3].X - pts[0].X;
            double height = System.Math.Abs(pts[0].Y - pts[3].Y);
            found.Add((pts[0].X, (pts[0].X + pts[2].X) / 2, (pts[0].Y + pts[3].Y) / 2,
                xWidth, w, height));
        }
        return found;
    }

    /// <summary>
    /// A beam ribbon rather than a repeat slash. ⚠️ SLANT IS NOT THE TEST: a beam over notes
    /// at one pitch is FLAT, and a flat ribbon has the repeat slash's own signature of two
    /// horizontal edges — the control of
    /// <see cref="Renderer_DoubleSign_HidesTheFirstMeasuresBeamsAndStemsToo"/> counted two of
    /// its beams as slashes before this existed. What separates them is which pairs of
    /// corners share a coordinate: a ribbon is bounded by two VERTICAL cuts (its left corners
    /// share an X and so do its right ones), a slash by two HORIZONTAL ones, so the slash's
    /// corners step across in X where the ribbon's do not.
    /// </summary>
    private static bool IsBeamRibbon(List<(double X, double Y)> pts) =>
        pts[0].X == pts[3].X && pts[1].X == pts[2].X;

    private static double Attr(string attrs, string name) => double.Parse(
        System.Text.RegularExpressions.Regex.Match(attrs, name + "=\"([^\"]+)\"").Groups[1].Value,
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Collector_PercentRepeat_WithPrecedingMeasures()
    {
        // Measures before the repeat should not be affected
        var source = "c4 d e f | repeat percent 2 { g4 a b c' }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.Single(score.PercentRepeats);
        Assert.Equal(2, score.PercentRepeats[0].MeasureIndex);
    }

    [Fact]
    public void PercentCoveredMeasures_DrawNoWholeRestUnderTheSign()
    {
        // `repeat percent 8 { R1 }`: LilyPond prints the rest ONCE — the covered
        // measures show the % alone, because its percent iterator plays the body a
        // single time and the MMR engraver never sees the copies
        // (lily/percent-repeat-engraver.cc; measured on 2.26.0, the machine-exported
        // twin draws bar 1 = whole rest, bars 2-8 = % only). Lily#'s unfold keeps
        // the R for playback, so the SYMBOL pass must skip it: one whole-rest
        // symbol at bar 0 and none under the seven signs. This pins BOTH halves of
        // the repair — the engraver's percent filter AND the multi-staff path's
        // synthetic annotation Score carrying PercentRepeats at all.
        var tree = SyntaxTree.Parse("""
            part melody {
              section A { repeat percent 8 { R1 } }
            }
            form main { A }
            score main { staff melody }
            """);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var layout = new LayoutEngine().Layout(
            LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, spec));

        var mmr = Assert.Single(layout.MultiMeasureRestLayouts);
        Assert.Equal(0, mmr.StartMeasureIndex);
        Assert.Equal(1, mmr.MeasureCount);
        Assert.Equal(7, layout.PercentRepeatLayouts.Length);
    }

    // --- the DOUBLE sign: a two-measure body ---

    /// <summary>
    /// A TWO-measure body is one double-percent sign per repetition, not a single sign in
    /// each measure. LilyPond chooses by the body's LENGTH, once, on the second iteration —
    /// so `\repeat percent 4 { c1 | d1 }` reports three DoublePercentEvents.
    /// LILYPOND-REF: lily/percent-repeat-iterator.cc:75-92 next_element — body_length_ vs
    ///   measure_length and its double.
    /// </summary>
    [Fact]
    public void Collector_TwoMeasureBody_IsOneDoubleSignPerRepetition()
    {
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("repeat percent 4 { c1 | d1 }"));

        Assert.Equal(8, score.Voice.Measures.Length);
        Assert.Equal(3, score.PercentRepeats.Length);
        Assert.All(score.PercentRepeats, pr => Assert.True(pr.IsDouble));
        // Each sign anchors on the SECOND measure of its pair — the bar line between the
        // two is where LilyPond's item is made and break-aligned.
        Assert.Equal(new[] { 3, 5, 7 },
            score.PercentRepeats.Select(pr => pr.MeasureIndex).ToArray());
        // …and it stands for both of them.
        Assert.Equal(new[] { 2, 4, 6 },
            score.PercentRepeats.Select(pr => pr.FirstCoveredMeasure).ToArray());
    }

    /// <summary>
    /// The one-measure body stays a single sign even when it ends on a bar line — the case
    /// that broke when the choice was made by COUNTING the measures the body produced
    /// instead of measuring its length (2026-08-28): a trailing <c>|</c> can leave the
    /// builder's completed-measure count at two for a body that is one measure long.
    /// </summary>
    [Theory]
    [InlineData("repeat percent 3 { c4 d e f }")]
    [InlineData("repeat percent 3 { c4 d e f | }")]
    public void Collector_OneMeasureBody_StaysSingle(string source)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));

        Assert.Equal(2, score.PercentRepeats.Length);
        Assert.All(score.PercentRepeats, pr => Assert.False(pr.IsDouble));
        Assert.All(score.PercentRepeats, pr => Assert.Equal(pr.MeasureIndex, pr.FirstCoveredMeasure));
    }

    /// <summary>
    /// The double sign sits ON the bar line the pair's second measure opens on, where the
    /// single sign sits in the middle of its own measure.
    /// LILYPOND-REF: scm/define-grobs.scm — the DoublePercentRepeat entry (:1290-1292):
    ///   break-align-symbol = staff-bar; the stencil is centred on X
    ///   (lily/percent-repeat-interface.cc:96-101 double_percent).
    /// </summary>
    [Fact]
    public void PercentRepeatEngraver_DoubleSign_SitsOnTheBarBetweenThePair()
    {
        var itemLayout = new ItemLayout(0, 0, 1.0);
        var ml0 = new MeasureLayout(0, 0, 10.0, ImmutableArray.Create(itemLayout));
        var ml1 = new MeasureLayout(1, 10.0, 10.0, ImmutableArray.Create(itemLayout));
        var system = new SystemLayout(0, 20.0, 50.0, 5.0, ImmutableArray.Create(ml0, ml1));

        var single = PercentRepeatEngraver.Calculate(
            ImmutableArray.Create(new PercentRepeatItem(1, 0)),
            ImmutableArray.Create(system), ImmutableArray.Create(ml0, ml1));
        var dbl = PercentRepeatEngraver.Calculate(
            ImmutableArray.Create(new PercentRepeatItem(1, 0, IsDouble: true)),
            ImmutableArray.Create(system), ImmutableArray.Create(ml0, ml1));

        Assert.Equal(15.0, Assert.Single(single).X, 1);   // measure 1's middle
        Assert.Equal(10.0, Assert.Single(dbl).X, 1);      // measure 1's left edge = the bar
        Assert.True(Assert.Single(dbl).IsDouble);
    }

    /// <summary>
    /// Drawn, the double sign is TWO slashes rather than one, and both measures it stands
    /// for print no music. The slashes overlap: brew_slash adds the second at the group's
    /// right edge with a NEGATIVE padding, so their origins end up
    /// (slash ink width − slash-negative-kern·ss) apart rather than a slash apart.
    /// LILYPOND-REF: lily/percent-repeat-interface.cc:37-60 brew_slash — the
    ///   add_at_edge (X_AXIS, RIGHT, slash, -slash_neg_kern) loop.
    /// </summary>
    [Fact]
    public void Renderer_DoubleSign_DrawsTwoOverlappingSlashesAndHidesBothMeasures()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { repeat percent 2 { c1 | d1 } } }
            form main { ~A }
            score main { staff mel }
            """);

        // ONE sign, drawn as two slashes — not two signs and not one.
        var slashes = Slashes(svg);
        Assert.Equal(2, slashes.Count);
        slashes.Sort((p, q) => p.Cx.CompareTo(q.Cx));
        Assert.Equal(slashes[0].Cy, slashes[1].Cy, 3);
        // wid 2.0 + x_width (0.48·√2) − slash-negative-kern 1.6 = 1.0788, at staff-space 1.
        // Read off the two LEFT CORNERS rather than the centres: each corner is one number
        // the SVG rounded to two decimals, where a centre is the average of two of them and
        // can land 0.01 out. The alternatives this has to separate are far away — 2.68 for
        // no overlap at all, 0 for a single slash.
        Assert.Equal(2.0 + 0.48 * System.Math.Sqrt(2) - 1.6,
            slashes[1].X0 - slashes[0].X0, 2);

        // …and the dots hang off the EDGES OF THE PAIR, not of one slash: the upper one's
        // right edge 0.75 inside the group's left, the lower one's left edge 0.75 inside
        // its right. Measured against LilyPond 2.26.0's own SVG for the two-bar twin,
        // where the dot centres sit ∓1.354 / +1.353 from the group centre at ss = 1.
        double groupLeft = slashes[0].Cx - (slashes[0].XWidth + slashes[0].W) / 2;
        double groupRight = slashes[1].Cx + (slashes[1].XWidth + slashes[1].W) / 2;
        var dots = System.Text.RegularExpressions.Regex.Matches(svg, "<circle ([^>]*)/>")
            .Select(c => (Cx: Attr(c.Groups[1].Value, "cx"),
                          Cy: Attr(c.Groups[1].Value, "cy"),
                          R: Attr(c.Groups[1].Value, "r")))
            .OrderBy(c => c.Cx).ToList();
        Assert.Equal(2, dots.Count);
        // ONE decimal place on the X terms: each is built from coordinates the SVG rounded
        // to two, so the arithmetic carries up to 0.01 of rounding. It still separates what
        // matters by six times the tolerance — the constant this replaced put the dot
        // 1.039 from the centre where LilyPond puts it 1.354.
        // The font's own dots.dot half extent (GlyphMetricsGenerated.AugmentationDot),
        // which the repeat barline's dots read from the same constant — not a 0.25
        // stand-in, and not the 0.224 that LilyPond's SVG appears to say (its scale is
        // printed rounded to four decimals; see the renderer's note).
        // 0.23 and not 0.225: the SVG prints the radius to two decimals. It still separates
        // what matters — the 0.2 stand-in this replaced printed as 0.20.
        Assert.Equal(0.23, dots[0].R, 3);
        Assert.Equal(groupLeft + 0.75 - 0.225, dots[0].Cx, 1);
        Assert.Equal(groupRight - 0.75 + 0.225, dots[1].Cx, 1);
        Assert.Equal(1.354, (groupLeft + groupRight) / 2 - dots[0].Cx, 1);

        // BOTH measures of the repeated pair print no music, which is the FirstCoveredMeasure
        // walk reaching the FIRST measure of the pair and not only the anchored second one.
        // The control is the written pair alone: the sign is lines and circles, never a music
        // glyph, so an unhidden repeat would show up as extra <text class="music"> entries.
        var control = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { c1 | d1 } }
            form main { ~A }
            score main { staff mel }
            """);
        Assert.Equal(
            System.Text.RegularExpressions.Regex.Matches(control, "<text class=\"music\"").Count,
            System.Text.RegularExpressions.Regex.Matches(svg, "<text class=\"music\"").Count);
    }

    /// <summary>
    /// The FIRST measure of a double percent's pair prints no beams and no stems either. It
    /// is the same <see cref="PercentRepeatItem.FirstCoveredMeasure"/> walk as the noteheads',
    /// and until 2026-08-29 <c>DrawBeams</c> was not doing it: it built its own hidden set
    /// from the anchor measure alone, so bar 3 of a beamed two-bar body printed its beams and
    /// stems standing over nothing. Visible in 15 of the corpus's 39 double-percent books.
    /// </summary>
    [Fact]
    public void Renderer_DoubleSign_HidesTheFirstMeasuresBeamsAndStemsToo()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { repeat percent 2 { c8 d e f g a b c' | d'8 c' b a g f e d | } } }
            form main { ~A }
            score main { staff mel }
            """);
        var control = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { c8 d e f g a b c' | d'8 c' b a g f e d | } }
            form main { ~A }
            score main { staff mel }
            """);

        // The repeated pair adds the sign — two slashes and two dots — and NOTHING else:
        // same beams, same stems, same noteheads as the written pair alone.
        Assert.Equal(Beams(control).Count, Beams(svg).Count);
        Assert.Equal(Stems(control).Count, Stems(svg).Count);
        Assert.Equal(
            System.Text.RegularExpressions.Regex.Matches(control, "<text class=\"music\"").Count,
            System.Text.RegularExpressions.Regex.Matches(svg, "<text class=\"music\"").Count);
        Assert.Equal(2, Slashes(svg).Count);
    }

    // --- the BEAT slash: a body SHORTER than a measure (LilyPond's third branch) ---

    /// <summary>
    /// A body shorter than a measure is a BEAT slash, and LilyPond does not re-engrave the
    /// body for it: the iterator hands the context a RepeatSlashEvent instead of the music.
    /// So the repetition contributes one sign and NO notes — which is why the item count in
    /// the measure is the written beat plus one spacer per repetition, not the beat again.
    /// LILYPOND-REF: lily/percent-repeat-iterator.cc:75-92 next_element — the `else` arm.
    /// </summary>
    [Fact]
    public void Collector_SubMeasureBody_IsOneBeatSlashPerRepetitionAndNoRepeatedNotes()
    {
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("repeat percent 4 { c16 d e f }"));

        var measure = Assert.Single(score.Voice.Measures);
        // Four written sixteenths, then one spacer for each of the three repetitions.
        Assert.Equal(7, measure.Items.Length);
        Assert.Equal(4, measure.Items.Count(i => i is NoteItem));
        Assert.Equal(3, measure.Items.Count(i => i is RestItem { IsSpacer: true }));

        Assert.Equal(3, score.PercentRepeats.Length);
        Assert.All(score.PercentRepeats, pr => Assert.True(pr.IsBeatSlash));
        Assert.All(score.PercentRepeats, pr => Assert.False(pr.IsDouble));
        // Each stands at its own beat, all inside the one measure.
        Assert.All(score.PercentRepeats, pr => Assert.Equal(0, pr.MeasureIndex));
        Assert.Equal(
            new[] { new Fraction(1, 4), new Fraction(1, 2), new Fraction(3, 4) },
            score.PercentRepeats.Select(pr => pr.BeatTiming!.Value).ToArray());
    }

    /// <summary>
    /// A beat slash covers NO measure — the one place where Lily# does what LilyPond does and
    /// never emits the repeated body, so there is nothing underneath to hide. Reporting the
    /// anchor here would blank the whole bar, the written beat the slash repeats included.
    /// </summary>
    [Fact]
    public void Collector_BeatSlash_CoversNoMeasure()
    {
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("repeat percent 2 { c16 d e f }"));

        var pr = Assert.Single(score.PercentRepeats);
        Assert.True(pr.FirstCoveredMeasure > pr.MeasureIndex);
    }

    /// <summary>
    /// The slash COUNT comes from the body's written durations: all equal gives
    /// <c>max (duration-log − 2) 1</c>, and anything mixed gives 0, which is not "no slashes"
    /// but the OTHER grob — the dotted <c>DoubleRepeatSlash</c>.
    /// LILYPOND-REF: scm/music-functions.scm:378-390 calc-repeat-slash-count;
    /// LILYPOND-REF: lily/slash-repeat-engraver.cc:56-66 process_music.
    /// ★ The three shapes were measured on 2.26.0 (scratch/p282/slashprobe-lp.png and the
    /// session-278 probe): sixteenths draw two slashes, eighths one, and `g8. c16` the dotted
    /// double.
    /// </summary>
    [Theory]
    [InlineData("repeat percent 2 { c16 d e f }", 2)]   // duration-log 4 → max(2,1)
    [InlineData("repeat percent 2 { c8 d }", 1)]        // duration-log 3 → max(1,1)
    [InlineData("repeat percent 2 { c4 }", 1)]          // duration-log 2 → max(0,1) = 1
    [InlineData("repeat percent 2 { c32 d e f g a b c }", 3)] // duration-log 5 → max(3,1)
    [InlineData("repeat percent 2 { g8. c16 }", 0)]     // mixed → DoubleRepeatSlash
    [InlineData("repeat percent 2 { c8 d16 e }", 0)]    // mixed → DoubleRepeatSlash
    public void Collector_BeatSlashCount_FollowsTheWrittenDurations(string source, int expected)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));

        var pr = Assert.Single(score.PercentRepeats);
        Assert.True(pr.IsBeatSlash);
        Assert.Equal(expected, pr.SlashCount);
    }

    /// <summary>
    /// A body of THREE OR MORE WHOLE measures is a decided divergence and keeps the
    /// per-measure percent — it does NOT become a beat slash. What LilyPond engraves there is
    /// one bare slash and then EMPTY measures for the rest of the repetition (measured
    /// 2026-08-29 on 2.26.0, scratch/p282/wholebody.ly), which is out of the shape both slash
    /// grobs describe themselves as being for; the corpus writes it 200 times in 32 books.
    /// </summary>
    [Fact]
    public void Collector_ThreeMeasureBody_KeepsThePerMeasurePercent()
    {
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("repeat percent 2 { c1 | d1 | e1 | }"));

        Assert.Equal(3, score.PercentRepeats.Length);
        Assert.All(score.PercentRepeats, pr => Assert.False(pr.IsBeatSlash));
        Assert.All(score.PercentRepeats, pr => Assert.False(pr.IsDouble));
        Assert.Equal(new[] { 3, 4, 5 },
            score.PercentRepeats.Select(pr => pr.MeasureIndex).ToArray());
    }

    /// <summary>
    /// Drawn, an equal-duration beat slash is STEEPER and MORE TIGHTLY OVERLAPPED than the
    /// percent family, and carries no dots: slope 1.7 and slash-negative-kern 0.85 against
    /// the percent signs' 1.0 and 1.6.
    /// LILYPOND-REF: scm/define-grobs.scm — the RepeatSlash entry (range-less: one-word grob
    /// name, HANDOFF §5.2.1⑦).
    /// ★ Every number here is read off LilyPond 2.26.0's own SVG for the twin
    /// (scratch/p282/slashprobe-lpsvg.svg): the two slashes of `\repeat percent 2
    /// { c16 d e f }` are `M0 0 l0.5569 0 l1.1765 -2.0000 l-0.5569 0 z` at x = 27.1376 and
    /// 28.0210 — so x_width 0.5569, run 1.1765, height 2.0000, origins 0.8834 apart.
    /// </summary>
    [Fact]
    public void Renderer_EqualDurationBeatSlash_IsSteeperUnkernedAndUndotted()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { repeat percent 2 { c16 d e f } g2 } }
            form main { ~A }
            score main { staff mel }
            """);

        var slashes = Slashes(svg);
        Assert.Equal(2, slashes.Count);
        slashes.Sort((p, q) => p.X0.CompareTo(q.X0));
        // wid = 2.0/1.7 = 1.17647, height = wid·1.7 = 2.0,
        // x_width = hypot (0.48, 0.48/1.7) = 0.556887.
        Assert.Equal(1.1765, slashes[0].W, 2);
        Assert.Equal(2.0, slashes[0].Height, 2);
        // ONE decimal on the terms built from two rounded coordinates — the SVG prints X to
        // two, so a difference carries up to 0.01. It still separates what matters: the
        // percent family's x_width is 0.6788 and its overlap 1.0788.
        Assert.Equal(0.5569, slashes[0].XWidth, 1);
        // (wid + x_width) − 0.85 = 0.883358, where the percent family's overlap is 1.0788.
        Assert.Equal(0.8834, slashes[1].X0 - slashes[0].X0, 1);
        // NO DOTS: brew_slash's stencil, not x_percent's.
        Assert.Empty(System.Text.RegularExpressions.Regex.Matches(svg, "<circle "));
    }

    /// <summary>
    /// A MIXED-duration beat slash is the other grob, and its picture is the double percent's
    /// — two slashes at slope 1.0, overlapped by 1.6, WITH the two dots — standing at a beat
    /// instead of on a bar line. That is what makes `slash-count = 0` worth carrying: it is
    /// not a count of zero slashes.
    /// LILYPOND-REF: scm/define-grobs.scm — the DoubleRepeatSlash entry (range-less: one-word
    ///   grob name);
    /// LILYPOND-REF: lily/percent-repeat-interface.cc:107-121 beat_slash — <c>x_percent
    ///   (me, 2)</c> when the count is 0.
    /// ★ Measured on 2.26.0 (scratch/p282/slashprobe-lpsvg.svg): `M0 0 l0.6788 0 l2.0000
    /// -2.0000 l-0.6788 0 z` at x = 37.3980 and 38.4768 — origins 1.0788 apart.
    /// </summary>
    [Fact]
    public void Renderer_MixedDurationBeatSlash_IsTheDottedDoubleAtABeat()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { repeat percent 2 { g8. c16 } g2 } }
            form main { ~A }
            score main { staff mel }
            """);

        var slashes = Slashes(svg);
        Assert.Equal(2, slashes.Count);
        slashes.Sort((p, q) => p.X0.CompareTo(q.X0));
        Assert.Equal(2.0, slashes[0].W, 2);
        // ONE decimal for the same rounding reason as the plain slash's, and it still
        // separates the two grobs: the plain slash's x_width is 0.5569 and its overlap 0.8834.
        Assert.Equal(0.48 * System.Math.Sqrt(2), slashes[0].XWidth, 1);
        Assert.Equal(2.0 + 0.48 * System.Math.Sqrt(2) - 1.6, slashes[1].X0 - slashes[0].X0, 1);
        // …and it DOES carry the dots.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "<circle ").Count);
    }

    /// <summary>
    /// The measure a beat slash stands in keeps its own beams and stems: the written beat the
    /// slash repeats is still music, and hiding by measure would erase it. This is the arm of
    /// <see cref="PercentRepeatItem.FirstCoveredMeasure"/>'s empty range that the four hiding
    /// passes read, and the first draft of the port blanked the bar because <c>DrawBeams</c>
    /// was not reading it.
    /// </summary>
    [Fact]
    public void Renderer_BeatSlash_LeavesTheWrittenBeatBeamed()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { repeat percent 2 { c16 d e f } g2 } }
            form main { ~A }
            score main { staff mel }
            """);

        // The sixteenths are beamed (two beam levels) and stemmed, exactly as when written
        // out on their own.
        var control = LiveRender.SvgFromRenderSpec("""
            part mel { }
            section A { mel { c16 d e f g2. } }
            form main { ~A }
            score main { staff mel }
            """);
        Assert.Equal(Beams(control).Count, Beams(svg).Count);
        Assert.Equal(Stems(control).Count, Stems(svg).Count);
        Assert.NotEmpty(Beams(svg));
    }

    /// <summary>Beam ribbons — the same discriminator <see cref="Slashes"/> excludes by, read
    /// from the one home (<see cref="IsBeamRibbon"/>) so the two can never disagree about a
    /// polygon.</summary>
    private static List<string> Beams(string svg)
    {
        var found = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(svg, "<polygon points=\"([^\"]+)\""))
        {
            var pts = m.Groups[1].Value.Split(' ')
                .Select(p => p.Split(','))
                .Select(p => (X: double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                              Y: double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
            if (pts.Count == 4 && IsBeamRibbon(pts))
                found.Add(m.Groups[1].Value);
        }
        return found;
    }

    /// <summary>Stems: the vertical hairlines, told from staff lines by running down the page
    /// rather than across it.</summary>
    private static List<string> Stems(string svg) =>
        System.Text.RegularExpressions.Regex.Matches(svg, "<line ([^>]*)/>")
            .Select(m => m.Groups[1].Value)
            .Where(a => System.Math.Abs(Attr(a, "x1") - Attr(a, "x2")) < 1e-9
                     && System.Math.Abs(Attr(a, "y1") - Attr(a, "y2")) > 1e-9)
            .ToList();
}
