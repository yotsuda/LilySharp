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
        // c4 d e f | % | → 2 measures total, measure 1 is percent repeat
        var source = "c4 d e f | % |";
        var tree = TestPaper.ParseAtIndentZero(source);
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
        // c4 d e f | % | % | % | → 4 measures, measures 1-3 are percent repeats
        var source = "c4 d e f | % | % | % |";
        var tree = TestPaper.ParseAtIndentZero(source);
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
        var source = "c4 d e f | % |";
        var tree = TestPaper.ParseAtIndentZero(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        // First measure has the original notes
        Assert.True(score.Voice.Measures[0].Items.Length >= 4);
    }

    [Fact]
    public void Collector_PercentRepeat_NoPercentForFirst()
    {
        var source = "c4 d e f | % | % |";
        var tree = TestPaper.ParseAtIndentZero(source);
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
        var tree = TestPaper.ParseAtIndentZero(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    [Fact]
    public void Collector_UnfoldRepeat_NoPercentMarkers()
    {
        var source = "c4 d e f c4 d e f";
        var tree = TestPaper.ParseAtIndentZero(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.PercentRepeats.IsEmpty);
    }

    [Fact]
    public void Collector_NoRepeat_NoPercentMarkers()
    {
        var source = "c4 d e f | g a b c'";
        var tree = TestPaper.ParseAtIndentZero(source);
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
        // ⚠️ THE PART IS NAMED `mel`, NOT `g` (session 286). Written `part g { }` this book
        // does not parse — `g` is a note name, LYS0002 — and lysc renders "from the part of
        // the file that parsed", which left a body measuring 5/4 instead of one measure. The
        // test still passed, because a 5/4 body took the same per-measure percent an
        // in-range body did. It stopped passing the moment that branch became LilyPond's
        // repeat slash, which is how the broken book came to light: the assertions below are
        // pinned to LilyPond 2.26 output for a ONE-MEASURE body, and that is what they now
        // actually measure.
        var svg = TestPaper.SvgFromRenderSpec("""
            octave absolute
            part mel { }
            section A { mel { c4 c c c | % } }
            form { ~A }
            score { staff mel tab mel }
            """);

        // Horizontal staff lines: 5 notation lines (1.0 apart) above 6 tab
        // strings (1.5 apart).
        var horizontals = new List<double>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(svg, "<line ([^>]*)/>"))
        {
            var a = m.Groups[1].Value;
            double x1 = Attr(a, "x1"), x2 = Attr(a, "x2"), y1 = Attr(a, "y1"), y2 = Attr(a, "y2");
            // Staff/string rows all START at the system's left edge — their ink half a line
            // thickness after it (lily/staff-symbol.cc:84); ledger lines and the fret-digit-split
            // later segments of a string do not.
            if (y1 == y2 && x2 > x1 && x1 < LilySharp.Core.Svg.EngravingDefaults.StaffLineThickness)
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
        var source = "c4 d e f | g4 a b c' | % |";
        var tree = TestPaper.ParseAtIndentZero(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(3, score.Voice.Measures.Length);
        Assert.Single(score.PercentRepeats);
        Assert.Equal(2, score.PercentRepeats[0].MeasureIndex);
    }

    [Fact]
    public void PercentCoveredMeasures_DrawNoWholeRestUnderTheSign()
    {
        // `R1 | % | % | % | % | % | % | % |`: LilyPond prints the rest ONCE — the covered
        // measures show the % alone, because its percent iterator plays the body a
        // single time and the MMR engraver never sees the copies
        // (lily/percent-repeat-engraver.cc; measured on 2.26.0, the machine-exported
        // twin draws bar 1 = whole rest, bars 2-8 = % only). Lily#'s unfold keeps
        // the R for playback, so the SYMBOL pass must skip it: one whole-rest
        // symbol at bar 0 and none under the seven signs. This pins BOTH halves of
        // the repair — the engraver's percent filter AND the multi-staff path's
        // synthetic annotation Score carrying PercentRepeats at all.
        var tree = TestPaper.ParseAtIndentZero("""
            part melody {
              section A { R1 | % | % | % | % | % | % | % }
            }
            form { A }
            score { staff melody }
            """);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var layout = new LayoutEngine(TestPaper.IndentZero).Layout(
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
            TestPaper.ParseAtIndentZero("c1 | d1 | %2 | %2 | %2 |"));

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
    [InlineData("c4 d e f | % | % |")]
    [InlineData("c4 d e f | % | % |")]
    public void Collector_OneMeasureBody_StaysSingle(string source)
    {
        var score = new MeasureCollector().Collect(TestPaper.ParseAtIndentZero(source));

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
        var svg = TestPaper.SvgFromRenderSpec("""
            part mel { }
            section A { mel { c1 | d1 | %2 } }
            form { ~A }
            score { staff mel }
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
        // ⚠️ A TOLERANCE AND NOT A ROUNDING, because 1.354 sits 0.004 from the boundary that
        // `Assert.Equal(…, 1)` rounds at, and this number carries up to 0.01 of the SVG's
        // two-decimal printing (three printed coordinates go into it). Rounding to one place
        // therefore left 0.004 of room on the low side and 0.046 on the high — a gate whose
        // width depended on which side the noise fell. It held until session 332 spaced a
        // repeated bar as an empty one, which moved the SIGN and so re-rolled the rounding.
        // The SHAPE is what this line is about and the shape did not move: rendering the same
        // sign at three anchor positions (scratch/p332/t7/pd1..pd3, dotoffset.ps1) gives the
        // same slash-group span 3.76 every time, with the two dots reading 1.35 and 1.36 —
        // LilyPond's 1.354 straddled by the printing. 0.01 admits both and still stands 31×
        // clear of the 1.039 this replaced.
        Assert.Equal(1.354, (groupLeft + groupRight) / 2 - dots[0].Cx, 0.01);

        // BOTH measures of the repeated pair print no music, which is the FirstCoveredMeasure
        // walk reaching the FIRST measure of the pair and not only the anchored second one.
        // The control is the written pair alone: the sign is lines and circles, never a music
        // glyph, so an unhidden repeat would show up as extra <text class="music"> entries.
        var control = TestPaper.SvgFromRenderSpec("""
            part mel { }
            section A { mel { c1 | d1 } }
            form { ~A }
            score { staff mel }
            """);
        Assert.Equal(
            System.Text.RegularExpressions.Regex.Matches(control, "<text class=\"music\"").Count + 1, // + the count "2" (session 905)
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
        var svg = TestPaper.SvgFromRenderSpec("""
            part mel { }
            section A { mel { c8 d e f g a b c' | d'8 c' b a g f e d | %2 } }
            form { ~A }
            score { staff mel }
            """);
        var control = TestPaper.SvgFromRenderSpec("""
            part mel { }
            section A { mel { c8 d e f g a b c' | d'8 c' b a g f e d | } }
            form { ~A }
            score { staff mel }
            """);

        // The repeated pair adds the sign — two slashes and two dots — and NOTHING else:
        // same beams, same stems, same noteheads as the written pair alone.
        Assert.Equal(Beams(control).Count, Beams(svg).Count);
        Assert.Equal(Stems(control).Count, Stems(svg).Count);
        Assert.Equal(
            System.Text.RegularExpressions.Regex.Matches(control, "<text class=\"music\"").Count + 1, // + the count "2" (session 905)
            System.Text.RegularExpressions.Regex.Matches(svg, "<text class=\"music\"").Count);
        Assert.Equal(2, Slashes(svg).Count);
    }

    // --- three or more whole bars: the multi-bar sign (session 905) ---
    //
    // ⚠️ A body SHORTER than a bar — LilyPond's beat slash — cannot be written since
    // 2026-10-14 (docs/percent-repeat-design.md), and since session 905 a body of three or
    // more WHOLE bars takes the multi-bar sign, not LilyPond's RepeatSlash (owner's decision:
    // PercentRepeatItem.Bars). The beat-slash grob is left only for an error's recovery (a
    // body that is not whole bars, LYS1049); its LilyPond-measured stencil is pinned on the
    // geometry itself below.

    /// <summary>
    /// Three whole bars are ONE multi-bar sign, in the repetition's MIDDLE bar, covering all
    /// three — whatever the body's durations (the beat slash's count read them; this sign does
    /// not), and its spacers carry no slash count (no bar of the three holds a column).
    /// </summary>
    [Theory]
    [InlineData("c1")]
    [InlineData("c16 d e f c d e f c d e f c d e f")]
    [InlineData("c8 d c d c d c d")]
    [InlineData("g8. c16 g8. c16 g8. c16 g8. c16")]
    public void Collector_ThreeWholeBars_AreOneMultiBarSignInTheMiddleBar(string bar)
    {
        var score = new MeasureCollector().Collect(
            TestPaper.ParseAtIndentZero($"{bar} | {bar} | {bar} | %3 |"));

        var pr = Assert.Single(score.PercentRepeats);
        Assert.False(pr.IsBeatSlash);
        Assert.False(pr.IsDouble);
        Assert.Equal(3, pr.Bars);
        Assert.Equal(4, pr.MeasureIndex);          // the middle of bars 3..5
        Assert.Equal(3, pr.FirstCoveredMeasure);
        Assert.Equal(5, pr.LastCoveredMeasure);
        Assert.Equal(6, score.Voice.Measures.Length);
        var spacers = score.Voice.Measures.SelectMany(m => m.Items)
            .OfType<RestItem>().Where(r => r.IsSpacer).ToList();
        Assert.Equal(3, spacers.Count);
        Assert.All(spacers, s => Assert.Null(s.RepeatSlashCount));
    }

    /// <summary>
    /// An EVEN count stands on the bar line between its halves — anchored, as the double is,
    /// on the second half's first bar — and the count goes past 4 bars with the slashes capped
    /// at 4; the bar lines inside the repetition forbid a line break, so the sign's bars stay
    /// on one line.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Collector_EvenWholeBars_StandOnTheMiddleBarLine_AndDoNotBreak(int n)
    {
        string body = string.Join(" | ", Enumerable.Repeat("c1", n));
        var score = new MeasureCollector().Collect(TestPaper.ParseAtIndentZero($"{body} | %{n} |"));

        var pr = Assert.Single(score.PercentRepeats);
        Assert.True(pr.IsDouble);
        Assert.Equal(n, pr.Bars);
        Assert.Equal(n + n / 2, pr.MeasureIndex);
        Assert.Equal(n, pr.FirstCoveredMeasure);
        Assert.Equal(2 * n - 1, pr.LastCoveredMeasure);
        var measures = score.Voice.Measures;
        for (int m = n; m < 2 * n - 1; m++)
            Assert.Equal(BreakPermission.Forbid, measures[m].LineBreakPermission);
        Assert.Equal(System.Math.Min(n, 4),
            PercentRepeatEngraver.Geometry(false, 0, true, 1.0, pr.Bars).Slashes);
    }

    /// <summary>
    /// Drawn: N slashes (4 at most) of the percent family — slope 1.0, overlapped by 1.6, with
    /// the dots — and the count above the staff in the multi-measure rest's digits. The double
    /// sign carries its "2" too.
    /// </summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    public void Renderer_MultiBarSign_DrawsMinNFourSlashesAndTheCountAbove(int n, int slashCount)
    {
        string body = string.Join(" | ", Enumerable.Repeat("c1", n));
        var svg = TestPaper.SvgFromRenderSpec($$"""
            part mel { }
            section A { mel { {{body}} | %{{n}} | } }
            form { ~A }
            score { staff mel }
            """);
        var control = TestPaper.SvgFromRenderSpec($$"""
            part mel { }
            section A { mel { {{body}} | } }
            form { ~A }
            score { staff mel }
            """);

        var slashes = Slashes(svg);
        Assert.Equal(slashCount, slashes.Count);
        slashes.Sort((p, q) => p.X0.CompareTo(q.X0));
        Assert.Equal(2.0, slashes[0].W, 2);
        Assert.Equal(2.0 + 0.48 * System.Math.Sqrt(2) - 1.6, slashes[1].X0 - slashes[0].X0, 1);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "<circle ").Count);
        // The count's digits are the only music glyphs the repetition adds.
        Assert.Equal(
            System.Text.RegularExpressions.Regex.Matches(control, "<text class=\"music\"").Count
                + n.ToString().Length,
            System.Text.RegularExpressions.Regex.Matches(svg, "<text class=\"music\"").Count);
    }

    /// <summary>
    /// The blank bars of a multi-bar sign's span stand EVENLY — the bars beside the sign are not
    /// widened alone (the owner, session 905: "only the bar the % stands in is longer"). Every
    /// bar of the span takes the same rod (ScoreSideTables.PercentSignBarRods), and the double's
    /// bar-line reservation is the 2-bar pair's alone. On a staff and on a tab (1.5-sized sign).
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Layout_AMultiBarSpan_StandsEvenly(int n)
    {
        string body = string.Join(" | ", Enumerable.Repeat("c1", n));
        // Ragged: a justified line stretches past every rod, and the rod is what is asked.
        var svg = TestPaper.SvgFromRenderSpec($$"""
            octave absolute
            paper { raggedRight }
            part bl { clef bass  tuning bass }
            section A { bl { {{body}} | break %{{n}} | } }
            form { ~A }
            score { staff bl  tab bl }
            """);
        // The second system's bar lines on one staff: its bars are the span alone.
        var rows = System.Text.RegularExpressions.Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" width=\"0.19\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .GroupBy(r => r.Y).OrderBy(g => g.Key).ToList();
        var last = rows[^1].Select(r => r.X).Distinct().OrderBy(x => x).ToList();
        var widths = last.Zip(last.Skip(1), (a, b) => b - a).ToList();
        // The line's first bar opens at the prefix, not a bar line: the rest are bar to bar.
        Assert.True(widths.Count >= n - 1, $"{widths.Count} bars");
        Assert.True(widths.Max() - widths.Min() < 0.05,
            $"the span's bars must stand evenly: {string.Join(" ", widths.Select(w => w.ToString("F2")))}");
    }

    /// <summary>
    /// A JUSTIFIED line that opens on a multi-bar sign's span stands evenly too: the span's first
    /// bar, after the clef and the key, stretches by its bar-to-bar pair as the others do — not by
    /// the prefix's reach as well (MultiStaffLayouter.LineStartSpringForLine, LILYSHARP-OWN). The
    /// owner's percent4-center.lys (session 905): `key fis major`, `%4` on a line of its own drew
    /// 35.9 / 18.5 / 18.5 / 18.5, and the sign stood off the middle of the line.
    /// </summary>
    [Theory]
    [InlineData("fis major")]
    [InlineData("c major")]
    public void Layout_AJustifiedLineOpeningOnASpan_StandsEvenly(string key)
    {
        var svg = TestPaper.SvgFromRenderSpec($$"""
            octave absolute
            key {{key}}
            part m { clef treble }
            section A { m { fis'1 | d'1 | b1 | e'1 | break %4 | } }
            form { ~A }
            score { staff m }
            """);
        var rows = System.Text.RegularExpressions.Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" width=\"0.19\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .GroupBy(r => r.Y).OrderBy(g => g.Key).ToList();
        var bars = rows[^1].Select(r => r.X).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(4, bars.Count);
        double bar = bars[3] - bars[2];
        Assert.InRange(bars[2] - bars[1], bar - 0.05, bar + 0.05);   // (the SVG writes two decimals)
        // The first bar from the prefix's ink: the clef's and the key's music glyphs on that line.
        double y = rows[^1].Key;
        double prefixRight = System.Text.RegularExpressions.Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Where(g => System.Math.Abs(g.Y - y) < 6 && g.X < bars[0]).Max(g => g.X) + 1.0;
        Assert.InRange(bars[0] - prefixRight, bar - 1.5, bar + 1.5);
    }

    /// <summary>
    /// A ONE-bar `%` on a squeezed line keeps LilyPond's PercentRepeat rod: its bar holds the
    /// empty bar's minimum + full-measure-extra-space + the bar's duration space, 5.39 between two
    /// single bar lines in 4/4 (SpacingRules.PercentRepeatRodDistance).
    /// ★ MEASURED, 2.26.0 (Lab probes/percent-last-bar, the owner's clef-change.lys): bars of
    /// 5.79, 5.18 × 12 and the `%` bar 5.39 — Lily# squeezed the `%` bar to 2.91 until session 905.
    /// </summary>
    [Fact]
    public void Layout_ASingleSignBarOnASqueezedLine_HoldsLilyPondsRod()
    {
        var svg = LiveRender.SvgFromRenderSpec("""
            key d major
            part melody { clef treble }
            section A { melody { c'4 d e f | c1 | d1 | d1 | d1 | d1 | d1 | d1 | d1 | d1 | d1 | d1 | d1 | d1 | % } }
            form { A }
            score { staff melody }
            """);
        var xs = System.Text.RegularExpressions.Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"[-\\d.]+\" width=\"0.19\"")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToList();
        var widths = xs.Zip(xs.Skip(1), (a, b) => b - a).ToList();
        Assert.InRange(widths[^1], 5.39 - 0.02, 5.39 + 0.02);      // the % bar
        Assert.InRange(widths[^2], 5.18 - 0.02, 5.18 + 0.02);      // a whole-note bar beside it
    }

    /// <summary>
    /// An ODD multi-bar sign stands at the CENTRE of its middle bar and need not fit inside it:
    /// its bars take the same rod an even sign's do, half the group plus the padding
    /// (ScoreSideTables.PercentSignBarRods), so a long odd span leaves the written bars their
    /// room (the owner, session 905: `%11`'s bars each held a whole sign and squeezed bars
    /// 12-16). On a tab the 1.5-sized three-slash sign crosses its bar's lines, as an even sign
    /// crosses its middle bar line.
    /// </summary>
    [Theory]
    [InlineData("staff bl  tab bl", 3)]
    [InlineData("staff bl", 11)]
    public void Layout_AnOddSign_StandsAtItsMiddleBarsCentre(string score, int n)
    {
        string body = string.Join(" | ", Enumerable.Repeat("c1", n));
        var svg = TestPaper.SvgFromRenderSpec($$"""
            octave absolute
            paper { raggedRight }
            part bl { clef bass  tuning bass }
            section A { bl { {{body}} | break %{{n}} | } }
            form { ~A }
            score { {{score}} }
            """);
        // The lowest staff's slashes (the tab's when there is one).
        var slashes = Slashes(svg);
        double y = slashes.Max(s => s.Cy);
        var sign = slashes.Where(s => System.Math.Abs(s.Cy - y) < 0.5).ToList();
        Assert.Equal(System.Math.Min(n, 4), sign.Count);
        double centre = (sign.Min(s => s.X0) + sign.Max(s => s.X0 + s.XWidth + s.W)) / 2;
        var bars = System.Text.RegularExpressions.Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"[-\\d.]+\" width=\"0.19\"")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToList();
        double before = bars.Where(x => x < centre).Max();
        double after = bars.Where(x => x > centre).Min();
        // Within half a staff space of the bar's middle: the single sign's own centring
        // (PercentRepeatEngraver: the measure's X + width / 2), which reads the bar's layout box.
        Assert.InRange(centre, (before + 0.19 + after) / 2 - 0.5, (before + 0.19 + after) / 2 + 0.5);
        // The span's bars a blank bar's natural width, not a whole sign's: a ragged line's
        // middle bar here is under the sign's width plus two paddings for the 11-bar sign.
        if (n == 11)
            Assert.True(after - before < PercentRepeatEngraver.Geometry(false, 0, false, 1.0, n).GroupWidth + 1.0,
                $"the middle bar {after - before:F2} still holds the whole sign");
    }

    /// <summary>
    /// The beat-slash stencils LilyPond 2.26.0 draws, pinned on the geometry (no spelling
    /// reaches them since session 905 but an error's recovery). The plain slash is steeper and
    /// more tightly overlapped than the percent family and carries no dots; the mixed-duration
    /// one is the percent family's dotted pair.
    /// ★ Read off LilyPond's own SVG (scratch/p282/slashprobe-lpsvg.svg): the plain slashes of
    /// `\repeat percent 2 { c16 d e f }` are x_width 0.5569, run 1.1765, height 2.0, origins
    /// 0.8834 apart; the mixed `g8. c16` pair is x_width 0.6788, run 2.0, origins 1.0788 apart.
    /// LILYPOND-REF: scm/define-grobs.scm — the RepeatSlash and DoubleRepeatSlash entries
    ///   (range-less: one-word grob names, HANDOFF §5.2.1⑦).
    /// </summary>
    [Fact]
    public void Geometry_BeatSlashStencils_AreLilyPonds()
    {
        var plain = PercentRepeatEngraver.Geometry(isBeatSlash: true, slashCount: 2, isDouble: false, staffSpace: 1.0);
        Assert.True(plain.PlainSlash);
        Assert.Equal(2, plain.Slashes);
        Assert.Equal(1.1765, plain.SlashWidth, 3);
        Assert.Equal(2.0, plain.SlashHeight, 6);
        Assert.Equal(0.5569, plain.XWidth, 3);
        Assert.Equal(0.8834, plain.PairGap, 3);

        var mixed = PercentRepeatEngraver.Geometry(isBeatSlash: true, slashCount: 0, isDouble: false, staffSpace: 1.0);
        Assert.False(mixed.PlainSlash);
        Assert.Equal(2, mixed.Slashes);
        Assert.Equal(2.0, mixed.SlashWidth, 6);
        Assert.Equal(0.48 * System.Math.Sqrt(2), mixed.XWidth, 6);
        Assert.Equal(2.0 + 0.48 * System.Math.Sqrt(2) - 1.6, mixed.PairGap, 6);
    }

    /// <summary>
    /// A repeat inside a percent repeat's bars is collected as written.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TREMOLO IN FRONT IS THE POINT, not decoration. The collector lends each repeat's
    /// body list from one list a thread keeps (<c>MeasureCollector.RentRepeatBody</c>), and the
    /// inner repeat must get a list of its OWN while the percent repeat's is still being
    /// walked. On a thread's first repeat the drawer is empty either way, so a nested repeat
    /// on its own cannot tell a correct drawer from one that hands the outer list out twice —
    /// the repeat before it is what fills the drawer. MEASURED (session 461, on the old
    /// <c>repeat unfold</c>): with the rent leaving the drawer filled, the inner repeat appends
    /// to the outer list, walks it from the start, meets itself, and the collect overflows
    /// the stack.
    /// </remarks>
    [Fact]
    public void ARepeatInsideAPercentRepeatsBars_IsCollectedAsWritten()
    {
        var score = new MeasureCollector().Collect(TestPaper.ParseAtIndentZero(
            "repeat tremolo 4 { c16 } r2. | repeat tremolo 2 { d8 } e4 f g | % |"));
        Assert.Equal(5, score.Voice.Measures.SelectMany(m => m.Items).Count(i => i is NoteItem));
        Assert.Single(score.PercentRepeats);
    }

    // --- what the body WRITES prints once ---

    /// <summary>
    /// A slur, a tie, a script and a dynamic inside a `repeat percent` body belong to the
    /// written iteration alone: LilyPond's iterator never re-plays the body — every later
    /// repetition is ONE percent event and nothing else (lily/percent-repeat-iterator.cc
    /// next_element). Lily# re-walks the body to keep the notes, and until 2026-09-02 the
    /// re-walk carried the markers and post-events too, so the slur (owner report,
    /// scratch/ベースタブLy/tab-percent.lys, a numbers tab) and the p / accent / f drew a
    /// second time over the sign. The gate is the collector's percent depth, so this pins
    /// the bows, the scripts and the dynamics together — one per written bar, none under
    /// the sign — on the notation staff and on the numbers tab alike.
    /// </summary>
    [Theory]
    [InlineData("staff melody")]
    [InlineData("tab melody as numbers")]
    public void PercentCoveredMeasures_CarryNoSlurTieScriptOrDynamic(string staffSpec)
    {
        // A ONE-bar body, so every repetition is a single sign. Since session 332 EVERY
        // repetition is written as spacers (MeasureCollector.MusicWalk,
        // WriteRepetitionAsSpacers) and the body is never re-walked, so the gate this
        // pins is honoured by absence — nothing is collected under a sign at all.
        // Bar 0 is written; bars 1-3 are the signs.
        var layout = LayoutOf($$"""
            part melody {
              section A { c4@p@accent( d) e~ e@f | % | % | % }
            }
            form { A }
            score { {{staffSpec}} }
            """);

        Assert.Equal(3, layout.PercentRepeatLayouts.Length);
        Assert.All(layout.PercentRepeatLayouts, pr => Assert.InRange(pr.MeasureIndex, 1, 3));

        var slur = Assert.Single(layout.SlurLayouts);
        Assert.Equal(0, slur.Slur.StartMeasureIndex);
        // The notation staff lays out exactly one tie, two dynamics and one script; a
        // numbers tab has rules of its own for those (no tie, no dynamics) and is only
        // held to "nothing under a sign" — the slur above is the tab's own report.
        if (staffSpec.StartsWith("staff"))
        {
            Assert.Single(layout.TieLayouts);
            Assert.Equal(2, layout.DynamicLayouts.Length);
            Assert.Single(layout.ArticulationLayouts);
        }
        Assert.All(layout.TieLayouts, t => Assert.Equal(0, t.Tie.StartMeasureIndex));
        Assert.All(layout.DynamicLayouts, d => Assert.Equal(0, d.MeasureIndex));
        Assert.All(layout.ArticulationLayouts, a => Assert.Equal(0, a.MeasureIndex));
    }

    /// <summary>
    /// Positive control for the test above: the same body under `repeat unfold` IS re-played
    /// in full — LilyPond's unfold iterator plays the body every time — so every bow, script
    /// and dynamic prints twice. If the gate ever leaked past the percent repeat, this is
    /// the test that reddens.
    /// </summary>
    [Fact]
    public void UnfoldRepeat_StillCarriesEverySlurTieScriptAndDynamic()
    {
        var layout = LayoutOf("""
            part melody {
              section A { c4@p@accent( d) e~ e@f | c4@p@accent( d) e~ e@f | c4@p@accent( d) e~ e@f | c4@p@accent( d) e~ e@f | }
            }
            form { A }
            score { staff melody }
            """);

        Assert.Empty(layout.PercentRepeatLayouts);
        Assert.Equal(4, layout.SlurLayouts.Length);
        Assert.Equal(4, layout.TieLayouts.Length);
        Assert.Equal(8, layout.DynamicLayouts.Length);
        Assert.Equal(4, layout.ArticulationLayouts.Length);
    }

    /// <summary>
    /// The rest of what a body writes prints once too: a chord name, a figured bass, a
    /// glissando and a cross-staff mark. HANDOFF §2 F ⒳ (session 320) listed these four as
    /// what the body's re-walk still carried under the sign, unmeasured; the re-walk itself
    /// went in session 332 (a repetition is spacers), so nothing written can reach a covered
    /// bar. MEASURED (Lab sessions/p884/px/px3): one C, one 6, one glissando, two signs.
    /// This pins the absence, with <see cref="UnfoldRepeat_StillCarriesEverySlurTieScriptAndDynamic"/>
    /// as the shape of the control.
    /// </summary>
    [Fact]
    public void PercentCoveredMeasures_CarryNoChordNameFiguredBassGlissandoOrCrossStaff()
    {
        var tree = TestPaper.ParseAtIndentZero("""
            part rh { clef treble }
            part lh { clef bass }
            section A {
              rh { c4@chord(C)@figuredBass(6) d@glissando e f@cross | % | % | g1 | }
              lh { c2 g | c2 g | c2 g | c1 | }
            }
            form { ~A }
            score { grandStaff { staff rh staff lh } }
            """);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, spec);

        Assert.Equal(2, score.PercentRepeats.Length);
        Assert.All(score.PercentRepeats, pr => Assert.InRange(pr.MeasureIndex, 1, 2));
        Assert.Equal(0, Assert.Single(score.ChordNames).MeasureIndex);
        Assert.Equal(0, Assert.Single(score.FiguredBasses).MeasureIndex);
        Assert.Equal(0, Assert.Single(score.CrossStaffItems).MeasureIndex);
        var glissandi = score.StaffGroups
            .SelectMany(g => g.Staves).SelectMany(s => s.Voices)
            .SelectMany(v => v.Measures.Select((m, i) => (Measure: m, Index: i)))
            .SelectMany(mi => mi.Measure.Items.OfType<NoteItem>().Where(n => n.HasGlissando).Select(_ => mi.Index))
            .ToList();
        Assert.Equal(new[] { 0 }, glissandi);
    }

    /// <summary>
    /// A TWO-bar body (the double sign) re-walks the body too, through the same depth.
    /// </summary>
    [Fact]
    public void DoubleSignCoveredMeasures_CarryNoSlur()
    {
        var layout = LayoutOf("""
            part melody {
              section A { c1( | d1) | %2 | %2 }
            }
            form { A }
            score { staff melody }
            """);

        Assert.Equal(2, layout.PercentRepeatLayouts.Length);
        var slur = Assert.Single(layout.SlurLayouts);
        Assert.Equal(0, slur.Slur.StartMeasureIndex);
    }

    private static ScoreLayout LayoutOf(string source)
    {
        var tree = TestPaper.ParseAtIndentZero(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new LayoutEngine(TestPaper.IndentZero).Layout(LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, spec));
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
