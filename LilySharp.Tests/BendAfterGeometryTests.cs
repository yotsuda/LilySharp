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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A fall (<c>@fall</c>) or doit (<c>@doit</c>) is LilyPond's BendAfter, printed by
/// bend::print: ONE stroked cubic that leaves the note head's ink right — or its dot's, when
/// the dot sits on the head's own row — by the padding 0.5, ends 0.5 short of the next
/// column's ink but at least minimum-length 0.5 on, drops (rises) 0.5 × 4 = 2 staff spaces,
/// and is 2 line-thicknesses (0.2) thick. Until session 566 it was eight straight segments
/// along an invented quadratic 1.25 long, 1.7 deep and 0.13 thick, leaving the head by 0.15.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/output-lib.scm:1343-1397 bend::print — left-x, right-x against each bound's generic-bound-extent, delta-y, minimum-length
/// LILYPOND-REF: scm/define-grobs.scm:551-559 BendAfter — minimum-length 0.5, thickness 2.0, bend-after-interface
/// MEASURED on 2.26.0 (Lab sessions/p566/bend-lp.log, the fixture's twin): `e\bendAfter #-4 g`
/// draws 13.107 … 13.892 = the e's ink right 12.607 + 0.5 … the g's ink left 14.392 − 0.5;
/// `e\bendAfter #+4 |` draws 18.784 … 19.284 = minimum-length, the bar line's 19.717 − 0.5
/// falling short of it; a dotted d in a space (bend-dots-lp.log) starts at 18.318 = its dot's
/// right 17.818 + 0.5, while a dotted c on a ledger line leaves its head's right 10.008 + 0.5,
/// not its lifted dot's; the stencil is 2.0 tall and the path 0.2 thick.
/// Poison: the old 0.15 / 1.25 / 1.7 / 0.13 reddens every fact here.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BendAfterGeometryTests
{
    private const double Padding = 0.5;
    private const double MinimumLength = 0.5;
    private const double Tolerance = 0.006;   // two decimals in the SVG, either side

    // octave 5: c d e f stand in the treble staff's upper half, a and b just under middle C5
    // — a in the second space (position −1), b on the middle line (0).
    private static string Render(string sectionA) => LiveRender.SvgFromRenderSpec($$"""
        part v { octave 5 }
        section A { {{sectionA}} }
        form main { A }
        score main { staff v }
        """);

    private readonly record struct Bend(int Pos, double X0, double Y0, double X1, double Y1, double Width);

    private static List<Bend> Bends(string svg) => Regex.Matches(svg,
            "<path d=\"M ([-\\d.]+),([-\\d.]+) C [-\\d.,]+ [-\\d.,]+ ([-\\d.]+),([-\\d.]+)\" fill=\"none\" "
            + "stroke=\"#000000\" stroke-width=\"([\\d.]+)\"[^>]*data-pos=\"(\\d+)\"")
        .Select(m => new Bend(int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture),
            D(m.Groups[1]), D(m.Groups[2]), D(m.Groups[3]), D(m.Groups[4]), D(m.Groups[5])))
        .OrderBy(b => b.X0).ToList();

    private static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    /// <summary>Every music glyph: heads, rests and the clef carry a source position; a dot
    /// carries none (Pos −1) and is found by its glyph and its X.</summary>
    private static List<(int Pos, double X, char Glyph)> Glyphs(string svg) => Regex.Matches(svg,
            "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"[\\d.]+\"(?: data-pos=\"(\\d+)\")?>(.)</text>")
        .Select(m => (m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : -1,
            D(m.Groups[1]), m.Groups[3].Value[0]))
        .ToList();

    /// <summary>The bend's own note: the head of the greatest source position before the
    /// mark's.</summary>
    private static double HeadLeftOf(List<(int Pos, double X, char Glyph)> glyphs, Bend bend)
    {
        int pos = glyphs.Where(g => g.Pos >= 0 && g.Pos < bend.Pos).Max(g => g.Pos);
        return glyphs.Where(g => g.Pos == pos).Min(g => g.X);
    }

    /// <summary>The bend's note's dot: the first dot glyph right of the head.</summary>
    private static double DotLeftOf(List<(int Pos, double X, char Glyph)> glyphs, Bend bend)
    {
        double headLeft = HeadLeftOf(glyphs, bend);
        return glyphs.Where(g => g.Glyph == EmmentalerGlyphs.AugmentationDot && g.X > headLeft).Min(g => g.X);
    }

    /// <summary>The next column's ink left: the leftmost glyph of the next source position.</summary>
    private static double NextInkLeftOf(List<(int Pos, double X, char Glyph)> glyphs, Bend bend)
    {
        int pos = glyphs.Where(g => g.Pos > bend.Pos).Min(g => g.Pos);
        return glyphs.Where(g => g.Pos == pos).Min(g => g.X);
    }

    [Fact]
    public void LeavesTheHeadsInkRight_ByThePadding()
    {
        var svg = Render("c4 e@fall g a |");
        var bend = Assert.Single(Bends(svg));
        var glyphs = Glyphs(svg);
        double headRight = HeadLeftOf(glyphs, bend) + GlyphMetrics.GetNoteheadBBox(4).Right;
        Assert.InRange(bend.X0, headRight + Padding - Tolerance, headRight + Padding + Tolerance);
    }

    [Fact]
    public void EndsThePaddingShortOfTheNextHead()
    {
        var svg = Render("c4 e@fall g a |");
        var bend = Assert.Single(Bends(svg));
        var glyphs = Glyphs(svg);
        double nextLeft = NextInkLeftOf(glyphs, bend) + GlyphMetrics.GetNoteheadBBox(4).Left;
        Assert.InRange(bend.X1, nextLeft - Padding - Tolerance, nextLeft - Padding + Tolerance);
        // The next head is well clear here, so the padding and not the minimum decided.
        Assert.True(bend.X1 - bend.X0 > MinimumLength + Tolerance, $"reach {bend.X1 - bend.X0}");
    }

    [Fact]
    public void ReachesAtLeastMinimumLength_AgainstTheBarLine()
    {
        // The doit on the measure's last note: its right bound is the bar line. The end is
        // the bar's ink left less the padding, or the minimum length on, whichever is further.
        var svg = Render("c2 e4 g@doit | c1 |");
        var bend = Assert.Single(Bends(svg));
        double barLeft = Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"[-\\d.]+\" width=\"0.19\"")
            .Select(m => D(m.Groups[1])).Where(x => x > bend.X0).Min();
        double expected = Math.Max(barLeft - Padding, bend.X0 + MinimumLength);
        Assert.InRange(bend.X1, expected - Tolerance, expected + Tolerance);
        Assert.True(bend.X1 - bend.X0 >= MinimumLength - Tolerance, $"reach {bend.X1 - bend.X0}");
    }

    [Fact]
    public void TheDotCounts_WhenItSitsOnTheHeadsRow_NotWhenLifted()
    {
        // a (a space: the dot on the head's row) and b (a line: the dot lifted a row).
        var svg = Render("a4.@fall b8 b4.@fall c8 |");
        var bends = Bends(svg);
        Assert.Equal(2, bends.Count);
        var glyphs = Glyphs(svg);

        double aDotRight = DotLeftOf(glyphs, bends[0]) + GlyphMetrics.AugmentationDot.Width;
        Assert.InRange(bends[0].X0, aDotRight + Padding - Tolerance, aDotRight + Padding + Tolerance);

        double bHeadRight = HeadLeftOf(glyphs, bends[1]) + GlyphMetrics.GetNoteheadBBox(4).Right;
        Assert.InRange(bends[1].X0, bHeadRight + Padding - Tolerance, bHeadRight + Padding + Tolerance);
        double bDotRight = DotLeftOf(glyphs, bends[1]) + GlyphMetrics.AugmentationDot.Width;
        Assert.True(bends[1].X0 < bDotRight + Padding - Tolerance, "the lifted dot must not be read");
    }

    [Fact]
    public void DropsOrRisesTwoSpaces_TwoLineThicknessesThick()
    {
        var svg = Render("c4 e@fall g@doit a |");
        var bends = Bends(svg);
        Assert.Equal(2, bends.Count);
        // Device Y grows downward: a fall ends 2 below its start, a doit 2 above.
        Assert.InRange(bends[0].Y1 - bends[0].Y0, 2.0 - Tolerance, 2.0 + Tolerance);
        Assert.InRange(bends[1].Y1 - bends[1].Y0, -2.0 - Tolerance, -2.0 + Tolerance);
        Assert.Equal(2.0 * EngravingDefaults.LineThickness, bends[0].Width, 3);
        Assert.Equal(2.0 * EngravingDefaults.LineThickness, bends[1].Width, 3);
    }
}
