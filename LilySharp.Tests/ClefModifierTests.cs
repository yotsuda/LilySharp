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

using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The octavation digit of a <c>treble_8</c> / <c>bass_8</c> / <c>treble^8</c> clef is
/// LilyPond's ClefModifier: italic text at the paper's size stepped by −4 (em 1.386), its
/// centre on the clef's <c>clef-alignments</c> point, its near edge on the clef's ink or 0.7
/// outside the staff, whichever is further. Until session 563 it was drawn at em 3.2 (2.3×)
/// at fixed offsets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:944-975 ClefModifier clef-modifier-interface;
/// lily/clef-modifier.cc:27-57 Clef_modifier::calc_parent_alignment;
/// scm/output-lib.scm:3983-4000 clef-modifier::print (make-fontsize-markup at 0.6 × the clef's step).
/// MEASURED on 2.26.0 (Lab sessions/p563/treble8-lp.log): the "8" 0.683 × 0.953 at fs −4,
/// centre 1.026 from the G clef's left, top 3.526 below the staff middle (the clef's box
/// bottom is 3.55 below). Poison: the old em (FontSize × 0.8) or the old fixed offsets
/// redden the first fact; the F clef's alignment (−0.3, staff-padding winning) the second.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ClefModifierTests
{
    private static readonly ScoreTextMetrics Fonts = ScoreTextMetrics.Bundled;

    private static string Book(string clef)
        => "part m { clef " + clef + " }\nsection A { m { c4 d e f | } }\n"
           + "form main { ~A }\nscore main { staff m }\n";

    private static string Svg(string book)
        => SvgGenerator.Generate(TestPaper.ParseAtIndentZero(book), new SvgRenderOptions { EmbedFont = false });

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The digit's (x, baseline y, font-size), page Y-down.</summary>
    private static (double X, double Y, double Em) Digit(string svg)
    {
        var m = Regex.Match(svg, @"<text x=""([\d.]+)"" y=""([\d.]+)"" font-size=""([\d.]+)"" font-style=""italic"" text-anchor=""middle""[^>]*>8</text>");
        Assert.True(m.Success, "no octavation digit on the page");
        return (D(m.Groups[1].Value), D(m.Groups[2].Value), D(m.Groups[3].Value));
    }

    /// <summary>The clef glyph's (x, y) at the full size, page Y-down.</summary>
    private static (double X, double Y) Clef(string svg, char glyph)
    {
        var m = Regex.Match(svg, @"<text class=""music"" x=""([\d.]+)"" y=""([\d.]+)"" font-size=""4\.00""[^>]*>" + glyph + "</text>");
        Assert.True(m.Success, "no clef glyph on the page");
        return (D(m.Groups[1].Value), D(m.Groups[2].Value));
    }

    private static double StaffTop(string svg)
    {
        var m = Regex.Match(svg, @"<line x1=""[\d.]+"" y1=""([\d.]+)"" x2=""[\d.]+"" y2=""\1"" stroke=""#000000"" stroke-width=""0\.100""");
        Assert.True(m.Success, "no staff line on the page");
        return D(m.Groups[1].Value);
    }

    [Fact]
    public void UnderATrebleClef_TheDigitIsSmall_OffCentre_AndOnTheClefsInk()
    {
        string svg = Svg(Book("treble_8"));
        var (x, y, em) = Digit(svg);
        var clef = Clef(svg, EmmentalerGlyphs.GClef);

        // em 2.2 × magstep(−4) = 1.386.
        Assert.Equal(2.2 * EmmentalerDesignSize.Magstep(-4), em, 2);
        // Centre: the clef's centre − 0.2 × its half-width = 1.026 from its left.
        Assert.Equal(clef.X + GlyphMetrics.ClefG.Width / 2 * 0.8, x, 2);
        // Top of the ink on the clef's box bottom (2.55 below the clef's origin, further than
        // the staff-padding 0.7 below the bottom line).
        var ink = Fonts.Ink("8", em, TextRole.ClefOctave, FontStyle.Italic);
        double inkTop = y - ink.Top;                       // page Y-down
        // ±0.011: the SVG prints two decimals, so two rounded numbers meet here.
        Assert.InRange(inkTop, clef.Y - GlyphMetrics.ClefG.Bottom - 0.011, clef.Y - GlyphMetrics.ClefG.Bottom + 0.011);
        Assert.True(inkTop > StaffTop(svg) + 4 + 0.7, "the digit must clear the staff by 0.7");
    }

    /// <summary>The F clef is shallow, so the staff-padding wins the drop, and its alignment
    /// is −0.3.</summary>
    [Fact]
    public void UnderABassClef_TheStaffPaddingWins_AndTheAlignmentIsTheFClefs()
    {
        string svg = Svg(Book("bass_8"));
        var (x, y, em) = Digit(svg);
        var clef = Clef(svg, EmmentalerGlyphs.FClef);
        Assert.Equal(clef.X + GlyphMetrics.ClefF.Width / 2 * 0.7, x, 2);
        var ink = Fonts.Ink("8", em, TextRole.ClefOctave, FontStyle.Italic);
        double want = StaffTop(svg) + 4 + 0.7;
        Assert.InRange(y - ink.Top, want - 0.011, want + 0.011);
    }

    /// <summary>Above the G clef: the ink bottom on the clef's box top (4.8 above its
    /// origin), centred +0.1 of the half-width right of the clef's centre.</summary>
    [Fact]
    public void AboveATrebleClef_TheDigitSitsOnTheClefsTop()
    {
        string svg = Svg(Book("treble^8"));
        var (x, y, em) = Digit(svg);
        var clef = Clef(svg, EmmentalerGlyphs.GClef);
        Assert.Equal(clef.X + GlyphMetrics.ClefG.Width / 2 * 1.1, x, 2);
        var ink = Fonts.Ink("8", em, TextRole.ClefOctave, FontStyle.Italic);
        double want = clef.Y - GlyphMetrics.ClefG.Top;
        Assert.InRange(y - ink.Bottom, want - 0.011, want + 0.011);
    }
}
