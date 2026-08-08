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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Observers for the slur-scoring machinery (lily/slur-scoring.cc +
/// lily/slur-configuration.cc): the attachment grid, the generated curve, and
/// the extra-encompass terms, pinned against LilyPond twins.
/// </summary>
[Trait("Category", "Unit")]
public class SlurScoringTests
{
    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

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

    /// <summary>
    /// The first drawn bow's outer curve: M x0,y0 C x1,y1 x2,y2 x3,y3 (the sandwich's
    /// second arm is ignored). Device coordinates.
    /// </summary>
    private static double[] BowCurve(string svg)
    {
        var m = System.Text.RegularExpressions.Regex.Match(svg,
            "<path d=\"M ([-\\d.]+),([-\\d.]+) C ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+)");
        Assert.True(m.Success, "no bow path in the SVG");
        return Enumerable.Range(1, 8).Select(i => double.Parse(m.Groups[i].Value)).ToArray();
    }

    [Fact]
    public void SteepSlur_StaysFlatOverTheDrop_AndClearsTheDot()
    {
        // slur-dot-collision.ly ("Slurs avoid dots"): E6 dotted 16th slurred to
        // E4 32nd. Two regimes in one book, both pinned against the LP twin
        // (scratch/lpreg/slurdot.{ly,lys}):
        //  - The attachment grid climbs to the OTHER side's base
        //    (get_y_attachment_range), and steep candidates are scored, not
        //    dropped — the old skip-and-fallback shipped a head-to-head dive to
        //    +0.95; LP floats the right end at -3.195, a shallow bow.
        //  - The dot enters the slur's extra encompass ('inside, dots widened
        //    0.2) and pushes the left end one grid step up: base -6.045 → -6.545.
        //    The right base steps off the E-line first (move_away_from_staffline
        //    +0.15: -0.955 → -0.805) and then climbs 8 steps to -3.195 exactly.
        // LILYPOND-REF: lily/slur-scoring.cc:483-516 get_y_attachment_range
        // LILYPOND-REF: lily/slur-scoring.cc:639-658 move_away_from_staffline
        // LILYPOND-REF: lily/slur-scoring.cc:850-884 get_extra_encompass_infos
        string svg = Render("e''16.( e,,32)");
        double middle = MiddleLineY(svg);
        var c = BowCurve(svg);

        Assert.Equal(9.51, c[0], 2);            // start X (LP 9.51)
        Assert.Equal(-6.54, c[1] - middle, 2);  // start Y (LP -6.545)
        Assert.Equal(13.67, c[6], 2);           // end X (LP 13.67)
        Assert.Equal(-3.19, c[7] - middle, 2);  // end Y (LP -3.195)
        Assert.Equal(-6.64, c[3] - middle, 2);  // control1 Y (LP -6.638)
        Assert.Equal(-4.80, c[5] - middle, 2);  // control2 Y (LP -4.799)

        // The claim itself: the curve passes ABOVE the dot's ink (dot centre
        // (10.34 + dot half-width, -5.5), ink top -5.725). Sample the cubic
        // over the dot's x-range.
        double dotLeft = 10.34, dotRight = 10.34 + 0.45;
        double worst = double.NegativeInfinity;
        for (int i = 0; i <= 100; i++)
        {
            double t = i / 100.0;
            double x = Cubic(c[0], c[2], c[4], c[6], t);
            if (x < dotLeft || x > dotRight)
                continue;
            worst = Math.Max(worst, Cubic(c[1], c[3], c[5], c[7], t) - middle);
        }
        Assert.True(worst < -5.725,
            $"slur runs into the dot: curve Y {worst} vs dot ink top -5.725");
    }

    private static double Cubic(double c0, double c1, double c2, double c3, double t)
    {
        double mt = 1 - t;
        return c0 * mt * mt * mt + 3 * c1 * t * mt * mt + 3 * c2 * t * t * mt + c3 * t * t * t;
    }

    [Fact]
    public void OuterSlur_EncompassesTheGraceColumnsStem()
    {
        // slur-grace.ly: an outer slur spans a note carrying an acciaccatura.
        // The grace column is a note column the engraver acknowledges while the
        // slur is open, and its FORCED-UP stem points with the up slur — so its
        // stem tip (not its head) is what the curve must clear, through the
        // encompass stem term, the avoid offsets and the variance's
        // get_point(dir). Without the stem the ends sat at -1.545/-3.045 (the
        // bases) with a lumpy fitted curve; LP parks both ends at -3.545 with
        // the plain bow. Pinned against the LP twin
        // (scratch/lpreg/slurgrace.{ly,lys}): all four curve Ys and the span.
        // LILYPOND-REF: lily/slur-scoring.cc:111-161 get_encompass_info —
        //   stem_ = stem extent on the slur's side; x_ moves to the stem.
        // LILYPOND-REF: lily/slur-configuration.cc:283-302 — get_point(dir) in
        //   the variance term, and the stem term over every column.
        string svg = Render(
            "octave absolute\ntime 4/4\n\n" +
            "c'4( acciaccatura { e'8 } d'4 e'4 f'4) |\n" +
            "c'4( appoggiatura { e'8 } d'4 e'4 f'4) |\n" +
            "c'4 appoggiatura { e'8 } d'4 e'4 f'4 |\n");
        double middle = MiddleLineY(svg);
        var c = BowCurve(svg);

        Assert.Equal(-3.54, c[1] - middle, 2);  // start Y (LP -3.5450)
        Assert.Equal(-3.54, c[7] - middle, 2);  // end Y (LP -3.5450)
        Assert.Equal(-5.02, c[3] - middle, 2);  // control1 Y (LP -5.0225)
        Assert.Equal(-5.02, c[5] - middle, 2);  // control2 Y (LP -5.0225)
        // X span: LP 10.9970-0.6521 = 10.3449 (a 2-decimal Equal would split on
        // the rounding boundary, so bound the difference instead).
        Assert.True(Math.Abs((c[6] - c[0]) - 10.3449) < 0.01,
            $"X span {c[6] - c[0]} vs LP 10.3449");
    }

    [Fact]
    public void VoiceTwoSlur_AttachesToItsSlurwardStems_AndStaysAtBase()
    {
        // A voice-two down-slur on stem-down quarters (test/multivoice-spanners'
        // g'( e')). Both edge stems point WITH the slur, so each candidate's X
        // moves onto the stem's face + 0.3 (slur-scoring.cc:738-760) — which
        // puts the encompass x_ OUTSIDE the attachment range, so the base pays
        // no stem penalty and the slur stays hugging the heads. Before that
        // rule was ported, plumbing the stems alone made the base pay the /5
        // stem term and the slur fled 3ss below the tips — away from LP.
        // Pinned against scratch/lpreg/mvslur-probe.ly: LP start -1.4550,
        // end -0.4550, X span 2.5772-0.4300 = 2.147.
        // LILYPOND-REF: lily/slur-scoring.cc:738-760 enumerate_attachments.
        string svg = Render(
            "octave absolute\n" +
            "part mel { clef treble }\n" +
            "section S {\n" +
            "  mel {\n" +
            "    voice { c''4 d'' e'' f'' }\n" +
            "    { e'4 g' g'( e') }\n" +
            "  }\n" +
            "}\n" +
            "form main { S }\n" +
            "score main \"obs\" { staff mel }\n");
        double middle = MiddleLineY(svg);
        var c = BowCurve(svg);

        // Start/end Y = the bases (LP -1.4550 / -0.4550). The SVG prints 2
        // decimals, so a .xx5 value can land either way — bound the difference.
        Assert.True(Math.Abs((c[1] - middle) - (-1.455)) < 0.015,
            $"start Y {c[1] - middle} vs LP -1.4550");
        Assert.True(Math.Abs((c[7] - middle) - (-0.455)) < 0.015,
            $"end Y {c[7] - middle} vs LP -0.4550");
        // X span (stem-to-stem): LP 2.5772-0.4300 = 2.1472.
        Assert.True(Math.Abs((c[6] - c[0]) - 2.1472) < 0.01,
            $"X span {c[6] - c[0]} vs LP 2.1472");
    }

    /// <summary>All drawn bows: M x0,y0 C x1,y1 x2,y2 x3,y3 per bow (outer arm).</summary>
    private static List<double[]> AllBowCurves(string svg)
    {
        var list = new List<double[]>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(svg,
                "<path d=\"M ([-\\d.]+),([-\\d.]+) C ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+)"))
            list.Add(Enumerable.Range(1, 8).Select(i => double.Parse(m.Groups[i].Value)).ToArray());
        return list;
    }

    [Fact]
    public void RestBoundSlurs_ExistAndKeepTheDefaultDownDirection()
    {
        // slur-rest-direction.ly (half-note row): rests don't change slur
        // direction (default down), and a rest is a legal slur BOUND — the
        // all-rest figure r2( r r) used to be dropped on the floor entirely
        // (the rest's slur flags never reached the RestItem).
        // Pinned against the LP twin (scratch/lpreg/slurrest-h.{ly,lys}):
        //  fig1 C3( r C3)   -> DOWN (below middle), LP start/end +1.545
        //  fig2 C4( r C4)   -> UP,   LP -4.045
        //  fig3 C4( r C3)   -> UP,   LP -4.045 .. -0.545
        //  fig4 C3( r C4)   -> UP,   LP -0.545 .. -4.045
        //  fig5 r2( r r)    -> DOWN at the rest base, LP +0.5 exact (a half
        //     rest's ink sits ON the middle, so the base binds; the 16th row's
        //     deep rest ink is a disclosed residual, see HANDOFF).
        // LILYPOND-REF: lily/slur.cc calc_direction — default DOWN, UP only
        //   for a stem-DOWN note column; rest columns cast no vote.
        // LILYPOND-REF: lily/slur-scoring.cc:587-619 get_base_attachments —
        //   a rest bound takes the no-note-column loop: y = the edge encompassed
        //   column's Y extent (the rest's ink) + dir * 0.5 (a half rest's ink
        //   bottom is 0, hence the 0.5 pin below).
        string svg = Render(
            "octave absolute\nclef bass\ntime 4/2\n\n" +
            "c,2( r c,) r | c( r c) r | c( r c,) r | c,( r c) r | r( r r) r |\n");
        double middle = MiddleLineY(svg);
        var bows = AllBowCurves(svg);

        Assert.Equal(5, bows.Count);   // the all-rest slur exists
        double[] startY = bows.Select(b => b[1] - middle).ToArray();
        double[] endY = bows.Select(b => b[7] - middle).ToArray();

        Assert.True(startY[0] > 0 && endY[0] > 0, "fig1 must bow DOWN");
        Assert.True(startY[1] < 0 && endY[1] < 0, "fig2 must bow UP");
        Assert.True(startY[2] < 0 && endY[2] < 0, "fig3 must bow UP");
        Assert.True(startY[3] < 0 && endY[3] < 0, "fig4 must bow UP");
        // fig5: DOWN at the rest-bound base, LP +0.5000 on both ends.
        Assert.Equal(0.5, startY[4], 2);
        Assert.Equal(0.5, endY[4], 2);
    }

    [Fact]
    public void AllRestSlur_BasesOnTheRestInk_NotTheMiddleLine()
    {
        // The 16th row of slur-rest-direction.ly separates the two spellings the
        // half row cannot: a rest bound's base is the REST'S INK edge + 0.5 (the
        // r16 glyph reaches 2.05 below the middle -> 2.55), not middle + 0.5.
        // MEASURED with debug-slur-scoring (scratch/lpreg/slurrest-dbg.ly): the
        // winning candidate is idx=0 TOTAL=0.00 AT 2.55 — the base itself.
        // LILYPOND-REF: lily/slur-scoring.cc:587-619 get_base_attachments, the
        //   no-note-column loop: y = robust_relative_extent(col, Y)[dir] +
        //   dir * 0.5 * staff_space.
        string svg = Render(
            "octave absolute\nclef bass\ntime 2/4\n\n" +
            "r16( r r) r r4 |\n");
        double middle = MiddleLineY(svg);
        var c = BowCurve(svg);

        Assert.Equal(2.55, c[1] - middle, 2);  // start (LP 2.5500)
        Assert.Equal(2.55, c[7] - middle, 2);  // end (LP 2.5500)
    }

    [Fact]
    public void FlaggedSlurwardEdge_AttachesPastItsFlag_AndClimbsOneGrid()
    {
        // slur-rest-direction fig4, 16th row: the left C3's up stem points WITH
        // the up slur and carries a FLAG. LP's stem_extent_ is the stem's
        // extent UNITED with the flag's (get_bound_info), so the attachment X
        // sits past the flag's ink (stem centre +1.128, not stem face +0.365)
        // — the base pair's post-tilt slope then breaks max-slope 1.1 and the
        // slope charge (≈1.86) sends the winner one grid up, where only the
        // L-edge term (LP 0.0708, LS 0.0713) remains. Before the flag unite the
        // base pair scored a clean 0.00 and never climbed.
        // Pinned against LP (scratch/lpreg/slurrest-dbg.ly fig4; the 16th and
        // 8th rows of scratch/lpreg/slurrest.ly climb, quarter/half stay).
        // LILYPOND-REF: lily/slur-scoring.cc:188-203 get_bound_info —
        //   s.unite (flag->extent (common_[ax], ax)).
        // LILYPOND-REF: lily/slur-scoring.cc:738-760 enumerate_attachments.
        string svg = Render(
            "octave absolute\nclef bass\ntime 2/4\n\n" +
            "c,16( r c) r |\n");
        double middle = MiddleLineY(svg);
        var c = BowCurve(svg);

        // The SVG prints 2-decimal coordinates, so two roundings can drift
        // ±0.01 — the tolerance is the printed precision, not the claim's.
        Assert.Equal(-1.045, c[1] - middle, 0.011);  // start (LP -1.0450)
        Assert.Equal(-4.045, c[7] - middle, 0.011);  // end (LP -4.0450)
        Assert.Equal(2.95, c[6] - c[0], 0.011);      // X span (LP 2.9573) — a
                                                     // head-centre attach reads ≈3.9
    }

    [Fact]
    public void UnflaggedSlurwardEdge_StaysAtItsBase()
    {
        // The quarter spelling of the same figure is the control: no flag, so
        // the united extent IS the bare stem, the base pair keeps a legal slope,
        // and the slur stays at its base — as LP's quarter and half rows do
        // (slurrest.ly rows 3-4: -0.5450, where rows 1-2 climb to -1.0450).
        string svg = Render(
            "octave absolute\nclef bass\ntime 4/4\n\n" +
            "c,4( r c) r |\n");
        double middle = MiddleLineY(svg);
        var c = BowCurve(svg);

        Assert.Equal(-0.545, c[1] - middle, 0.011);  // start (LP -0.5450)
        Assert.Equal(-4.045, c[7] - middle, 0.011);  // end (LP -4.0450)
    }
}
