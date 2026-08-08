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
}
