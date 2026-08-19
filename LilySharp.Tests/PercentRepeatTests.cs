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
        var diagonals = new List<(double Cx, double Cy, double Run, double Rise, string Thick)>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(svg, "<line ([^>]*)/>"))
        {
            var a = m.Groups[1].Value;
            double x1 = Attr(a, "x1"), x2 = Attr(a, "x2"), y1 = Attr(a, "y1"), y2 = Attr(a, "y2");
            // Staff/string rows all START at the system's left edge; ledger
            // lines and the fret-digit-split later segments of a string do not.
            if (y1 == y2 && x2 > x1 && x1 < 0.05)
                horizontals.Add(y1);
            else if (x1 != x2 && y1 != y2)
                diagonals.Add(((x1 + x2) / 2, (y1 + y2) / 2, x2 - x1,
                    System.Math.Abs(y2 - y1),
                    System.Text.RegularExpressions.Regex.Match(a, "stroke-width=\"([^\"]+)\"").Groups[1].Value));
        }
        horizontals.Sort();
        Assert.Equal(11, horizontals.Count);
        double staffMid = horizontals[2];
        double tabMid = (horizontals[5] + horizontals[10]) / 2;

        var staffSlash = Assert.Single(diagonals, d => d.Thick == "0.480");
        Assert.Equal(2.0, staffSlash.Run, 2);
        Assert.Equal(2.0, staffSlash.Rise, 2);
        Assert.Equal(staffMid, staffSlash.Cy, 2);

        var tabSlash = Assert.Single(diagonals, d => d.Thick == "0.720");
        Assert.Equal(3.0, tabSlash.Run, 2);
        Assert.Equal(3.0, tabSlash.Rise, 2);
        Assert.Equal(tabMid, tabSlash.Cy, 2);

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

        static double Attr(string attrs, string name) => double.Parse(
            System.Text.RegularExpressions.Regex.Match(attrs, name + "=\"([^\"]+)\"").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

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
}
