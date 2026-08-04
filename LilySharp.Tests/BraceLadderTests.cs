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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The grand-staff brace is chosen from the fetaBraces ladder and drawn UNSCALED, so the
/// only thing that can be wrong is which rung is picked and at what size it is emitted.
/// </summary>
/// <remarks>
/// ⚠️ WHAT NO TEST HERE CAN SEE: nothing rasterises a glyph, so the "one em is four staff
/// spaces" the drawing rests on is invisible to the suite — which is how the previous
/// spelling (an em read as ONE staff space, a power-law index guess and a 0.76 correction)
/// survived while drawing a 31.0 span 39.45 tall. That assumption is confirmed against
/// LilyPond by hand instead, in audit/lp-geometry/probes/brace-name-clear.ly, and named as
/// an assumption where it is made.
/// </remarks>
[Trait("Category", "Unit")]
public class BraceLadderTests
{
    /// <summary>
    /// The ladder must be strictly increasing — that is what makes the search a binary one,
    /// and LilyPond's own <c>binary-search</c> rests on the same property.
    /// </summary>
    [Fact]
    public void TheLadderIsStrictlyIncreasing()
    {
        var h = BraceLadder.Heights;
        Assert.Equal(576, h.Length);
        for (int i = 1; i < h.Length; i++)
            Assert.True(h[i] > h[i - 1],
                $"rung {i} ({h[i]}) does not exceed rung {i - 1} ({h[i - 1]})");
    }

    /// <summary>
    /// The WIDTH ladder is the same 576 rungs, also strictly increasing, and it agrees with
    /// what LilyPond was measured drawing.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE THIRD ASSERTION IS THE POINT, and it is the only thing here a height-only
    /// ladder could not say. <c>audit/lp-geometry/probes/brace-name-clear.ly</c> measures
    /// LilyPond's own four-staff brace at 8.175827 - 6.802427 = 1.373400 wide, which is rung
    /// 346. That book's note used to name rung 345 — plausible, because the two are only
    /// 0.1368 apart in HEIGHT and the picture cannot tell them apart. The width can, so the
    /// pair (height, width) pins WHICH rung LilyPond chose rather than merely a near one.
    /// </remarks>
    [Fact]
    public void TheWidthLadderIsStrictlyIncreasing_AndAgreesWithTheMeasuredBrace()
    {
        var w = BraceLadder.Widths;
        Assert.Equal(BraceLadder.Heights.Length, w.Length);
        for (int i = 1; i < w.Length; i++)
            Assert.True(w[i] > w[i - 1],
                $"width rung {i} ({w[i]}) does not exceed rung {i - 1} ({w[i - 1]})");

        // LilyPond 2.26.0, brace-name-clear.ly: the drawn brace's X extent.
        const double measuredWidth = 8.175826771653544 - 6.8024267716535425;
        Assert.Equal(1.3734, w[346], 4);
        Assert.Equal(measuredWidth, w[346], 6);
    }

    /// <summary>
    /// The selection is NEAREST, not next-below or next-above: no other rung may be closer
    /// to the wanted span than the one returned. Asserted over the whole ladder and the
    /// midpoints between rungs, which is where a next-below/next-above bug hides.
    /// </summary>
    [Fact]
    public void NearestIndex_ReturnsTheClosestRung()
    {
        var h = BraceLadder.Heights;
        var wanted = h.SelectMany((v, i) => i + 1 < h.Length
                ? new[] { v, v + (h[i + 1] - v) * 0.25, v + (h[i + 1] - v) * 0.75 }
                : new[] { v })
            .ToList();

        foreach (double w in wanted)
        {
            int got = BraceLadder.NearestIndex(w);
            double gotErr = Math.Abs(h[got] - w);
            for (int i = 0; i < h.Length; i++)
                Assert.True(Math.Abs(h[i] - w) >= gotErr - 1e-12,
                    $"span {w}: rung {i} ({h[i]}) is closer than the returned rung {got} ({h[got]})");
        }
    }

    /// <summary>A span outside the ladder clamps to an end rather than throwing.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm:5072-5099 <c>get-y-from-brace</c> — the
    /// guard after that search warns and returns the end glyph anyway, so a span off either
    /// end of the ladder is the same picture in both engines.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 0)]
    [InlineData(1000.0, 575)]
    public void SpansOutsideTheLadderClampToAnEnd(double span, int expected)
        => Assert.Equal(expected, BraceLadder.NearestIndex(span));

    /// <summary>
    /// The drawn brace: the rung nearest the group's span, emitted at the font's natural
    /// size. Both halves are read off the SVG, because both were wrong together before —
    /// the index was clamped to the top of the ladder AND the size was fitted to make it fit.
    /// </summary>
    [Fact]
    public void GrandStaffBrace_IsTheNearestRungAtNaturalSize()
    {
        string svg = LiveRender.SvgFromRenderSpec(
            "part sop { section A { c4 d e f } }\n" +
            "part alt { section A { e4 f g a } }\n" +
            "part ten { section A { g4 a b c } }\n" +
            "part bas { clef bass section A { c4 d e f } }\n" +
            "form main { A }\n" +
            "score main {\n  grandStaff {\n    staff sop\n    staff alt\n" +
            "    staff ten\n    staff bas\n  }\n}\n");

        // ⚠️ THE COORDINATES CAN BE NEGATIVE — a score with no instrument names indents by
        // nothing and the brace is drawn at x = -0.30, which a [\d.]+ for x silently misses.
        var brace = Regex.Match(svg,
            @"<text x=""-?[\d.]+"" y=""-?[\d.]+"" font-size=""(?<size>[\d.]+)""[^>]*"
            + @"font-family=""Emmentaler-Brace""[^>]*>(?<ch>.)</text>");
        Assert.True(brace.Success, "no Emmentaler-Brace glyph was drawn:\n" + svg);

        // The span the brace has to cover: the group's top staff line to its bottom one.
        // ⚠️ A STAFF LINE IS THE FULL-WIDTH ONE, said as a fraction of the widest rule
        // rather than as an absolute reach: this score is one measure and its system is
        // 21.9 wide, so any fixed threshold tuned on a longer book selects nothing.
        // A ledger line reaches a notehead's width and drops out either way.
        var horizontals = Regex.Matches(svg,
                @"<line[^>]*x1=""(?<x1>-?[\d.]+)""[^>]*y1=""(?<y>-?[\d.]+)""[^>]*x2=""(?<x2>-?[\d.]+)""[^>]*y2=""\k<y>""")
            .Select(m => (Span: double.Parse(m.Groups["x2"].Value) - double.Parse(m.Groups["x1"].Value),
                          Y: double.Parse(m.Groups["y"].Value)))
            .ToList();
        Assert.NotEmpty(horizontals);
        double widest = horizontals.Max(h => h.Span);
        var lineYs = horizontals.Where(h => h.Span >= widest * 0.9)
            .Select(h => h.Y).Distinct().OrderBy(v => v).ToList();
        Assert.Equal(20, lineYs.Count);          // four five-line staves
        double span = lineYs[^1] - lineYs[0];

        int expected = BraceLadder.NearestIndex(span);
        int drawn = brace.Groups["ch"].Value[0] - 0xE000;
        Assert.Equal(expected, drawn);

        // ...and NOT resized to the span: the glyph is emitted at the em, so its own height
        // is what appears. Reading the size back is the only place the suite can see it.
        Assert.Equal(4.0, double.Parse(brace.Groups["size"].Value), 6);

        // The rung's own height is what gets drawn, and it is within a ladder step of the
        // span — the error LilyPond accepts, and the net that catches a 4x em mistake.
        double drawnHeight = BraceLadder.Heights[drawn];
        Assert.True(Math.Abs(drawnHeight - span) < 0.30,
            $"span {span:F4} drew rung {drawn} ({drawnHeight:F4}) — off by "
            + $"{Math.Abs(drawnHeight - span):F4}, more than one step of the ladder");
    }
}
