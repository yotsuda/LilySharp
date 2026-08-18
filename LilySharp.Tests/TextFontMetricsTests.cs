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

using LilySharp.Core.Rendering;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Pins <see cref="TextFontMetrics"/> against numbers measured from LilyPond itself, so a
/// font swap or an API change cannot move the engine's text reservations unnoticed.
/// </summary>
[Trait("Category", "Unit")]
public class TextFontMetricsTests
{
    /// <summary>
    /// The quantity the four tuplet ledger entries are left with. LilyPond's TupletNumber
    /// is an italic digit at font-size -2 on text-font-size 11pt, i.e.
    /// 11 * 2^(-2/6) = 8.730706 pt, and one staff space is 5 pt — so its measured ink of
    /// 1.255434 ss is 0.718976 of an em.
    /// </summary>
    /// <remarks>
    /// Measured 1.2554756 against LilyPond's 1.255434 — 0.000042 ss apart, which is
    /// LilyPond's own Pango quantisation, the same order and the same explanation as the
    /// -0.000076 that staff.staff.dynamic-under-whole-note carries. The bound is on the
    /// ABSOLUTE difference rather than on decimal places, because 1e-4 of rounding puts
    /// these two either side of a boundary and would read as a failure of the port rather
    /// than as the named residual it is.
    /// </remarks>
    [Fact]
    public void ItalicDigit_MatchesLilyPondsOwnInk()
    {
        const double lilyPond = 1.255434;
        const double sizeInStaffSpaces = 8.730706 / 5.0;
        double ink = TextFontMetrics.InkHeight("3", sizeInStaffSpaces, sans: false, FontStyle.Italic);
        Assert.True(Math.Abs(ink - lilyPond) < 1e-4,
            $"ink {ink:F7} against LilyPond's {lilyPond:F6} — {Math.Abs(ink - lilyPond):E2} apart");
    }

    /// <summary>
    /// A digit has no descender, so all of its ink is above the baseline. Pins the SIGN
    /// convention: Skia's path is Y-down and this class reflects it, so a mistake here
    /// would flip every text reservation that uses the ink rather than the height.
    /// </summary>
    [Fact]
    public void Ink_IsYUpAboutTheBaseline()
    {
        var (bottom, top) = TextFontMetrics.Ink("3", 1.0, sans: false, FontStyle.Italic);
        Assert.True(top > 0, $"a digit's ink must rise above the baseline, got top={top}");
        Assert.True(Math.Abs(bottom) < 0.05, $"a digit has no descender, got bottom={bottom}");

        var (pBottom, _) = TextFontMetrics.Ink("p", 1.0);
        Assert.True(pBottom < -0.1, $"'p' descends below the baseline, got bottom={pBottom}");
    }

    /// <summary>
    /// Bold is a different face, not a synthesised emphasis of the regular one — so it must
    /// measure wider. Catches a FileName() mapping that silently serves one face for all.
    /// </summary>
    /// <remarks>At LilyPond's own text size rather than at 1.0: advances are snapped to a
    /// device pixel per glyph, and at size 1.0 that pixel is 3.4% of the em — coarse enough
    /// for two faces to land on the same count and read as one face.</remarks>
    [Fact]
    public void EachStyleResolvesToItsOwnFace()
    {
        const double textSize = 11.0 / 5.0;
        double regular = TextFontMetrics.Advance("Allegro", textSize);
        double bold = TextFontMetrics.Advance("Allegro", textSize, sans: false, FontStyle.Bold);
        double italic = TextFontMetrics.Advance("Allegro", textSize, sans: false, FontStyle.Italic);
        double sans = TextFontMetrics.Advance("Allegro", textSize, sans: true);

        Assert.True(bold > regular, $"bold {bold} should exceed regular {regular}");
        Assert.NotEqual(regular, italic, 6);
        Assert.NotEqual(regular, sans, 6);
    }

    /// <summary>
    /// Every advance is a whole number of Pango device pixels, and the count adds up PER
    /// GLYPH — the two halves of what <see cref="TextFontMetrics.Advance"/> reproduces.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/text-advance.ly, ledger text.width.*): LilyPond
    /// reads "n" as 39 pixels, "nn" as 78 and "nnnn" as 156 — exactly additive, which is
    /// what says the snap is per glyph and not on the string's total.
    /// </remarks>
    [Fact]
    public void Advances_AreWholePixelsAndAddPerGlyph()
    {
        double n = TextFontMetrics.Advance("n", 2.2, sans: false, FontStyle.Italic);
        double nn = TextFontMetrics.Advance("nn", 2.2, sans: false, FontStyle.Italic);
        double nnnn = TextFontMetrics.Advance("nnnn", 2.2, sans: false, FontStyle.Italic);

        Assert.Equal(2 * n, nn, 9);
        Assert.Equal(4 * n, nnnn, 9);
        foreach (double width in new[] { n, nn, nnnn })
        {
            double pixels = width / TextFontMetrics.PangoPixelStaffSpaces;
            Assert.Equal(Math.Round(pixels), pixels, 9);
        }
    }

    /// <summary>
    /// An advance is NOT linear in the font size, deliberately: the snap happens after the
    /// size multiply, so the same string at two sizes is two pixel counts rather than one
    /// number scaled. Empty stays zero.
    /// </summary>
    /// <remarks>
    /// The italic "A" advances 0.704 em: 0.704 ss is 20.618 pixels → 21 → 0.717009, while
    /// 0.704 × 2.4 = 1.6896 ss is 49.485 → 49 → 1.673022. Scaling the first by 2.4 gives
    /// 1.720823, two thirds of a pixel out. This test exists because the OPPOSITE was
    /// asserted here until 2026-08-02, and it was asserting the defect.
    /// </remarks>
    [Fact]
    public void Advance_IsNotLinearInTheSize()
    {
        double atOne = TextFontMetrics.Advance("A", 1.0, sans: false, FontStyle.Italic);
        double atSize = TextFontMetrics.Advance("A", 2.4, sans: false, FontStyle.Italic);
        Assert.NotEqual(atOne * 2.4, atSize, 6);

        Assert.Equal(0.0, TextFontMetrics.Advance("", 2.4));
        Assert.Equal(0.0, TextFontMetrics.InkHeight("", 2.4));
    }

    /// <summary>
    /// The bundled serif is LilyPond's own text face by metrics, and the widths are
    /// LilyPond's OWN — measured per string at its own text size, not derived from a table.
    /// </summary>
    /// <remarks>
    /// The strings are the kern-free rungs of audit/lp-geometry/probes/text-advance.ly
    /// (ledger text.width.{n1,n4,o1,a1}, all EXACT), so this is the unit-level mirror of
    /// those entries: it catches a font swap or a snap regression without rendering a page.
    /// A string with a kerning pair would NOT belong here — Lily# cannot kern, and the
    /// ledger carries those as the named whole-pixel residuals they are.
    /// </remarks>
    [Theory]
    [InlineData("n", 1.331588976378)]
    [InlineData("nnnn", 5.326355905512)]
    [InlineData("o", 1.092585826772)]
    [InlineData("A", 1.536448818898)]
    public void ItalicAdvances_AreLilyPondsOwnWidths(string text, double lilyPond)
        => Assert.Equal(lilyPond, TextFontMetrics.Advance(text, 2.2, sans: false, FontStyle.Italic), 9);

    /// <summary>
    /// A bundled face's ink extent is the DESIGN'S OWN COORDINATE, a whole font unit — not
    /// a rasteriser's rendering of it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE OBSERVER THAT WAS MISSING FOR 214 SESSIONS. Until 2026-08-19 the ink
    /// came from <c>SKPaint.GetTextPath</c>, and that call is not the same function on two
    /// machines: Windows hands back the design's integers, Linux (SkiaSharp's FreeType
    /// build) hands back the same outline quantised to 1/512 of a unit. The suite was green
    /// on Windows and 53 red on Linux the whole time, and NOTHING in it asked this question
    /// — the ledger and the snapshots only saw the consequence, one platform at a time.
    /// <para>
    /// So the assertion is INTEGRALITY rather than a number: it is the property that makes
    /// the two platforms agree, it holds for every glyph of every bundled face, and it goes
    /// red on whichever machine stops reading the font's tables. The one pinned pair below
    /// is the separate question of WHICH face — a font swap moves that, integrality not.
    /// </para>
    /// ⚠️ Asked at font size 1000 because the bundled faces are 1000 units per em, so the
    /// returned staff-space number IS the font unit and no scaling hides the quantisation.
    /// </remarks>
    [Theory]
    [InlineData("3", false, FontStyle.Bold)]
    [InlineData("2", false, FontStyle.Bold)]
    [InlineData("g", false, FontStyle.Regular)]
    [InlineData("H", false, FontStyle.Regular)]
    [InlineData("n", false, FontStyle.Italic)]
    [InlineData("A", true, FontStyle.Bold)]
    [InlineData("dolce", false, FontStyle.Italic)]
    public void Ink_IsTheDesignsOwnFontUnits_NotARasterisersRenderingOfThem(
        string text, bool sans, FontStyle style)
    {
        var (bottom, top) = TextFontMetrics.Ink(text, 1000.0, sans, style);
        Assert.Equal(Math.Round(bottom), bottom, 6);
        Assert.Equal(Math.Round(top), top, 6);
    }

    /// <summary>The bundled bold serif digit, so a font swap is caught as well as a
    /// rasteriser creeping back in.</summary>
    [Fact]
    public void Ink_OfTheBoldSerifDigit_IsTeXGyreScholasOwnNumbers()
    {
        var (bottom, top) = TextFontMetrics.Ink("3", 1000.0, sans: false, FontStyle.Bold);
        Assert.Equal(-14.0, bottom, 6);
        Assert.Equal(708.0, top, 6);
    }

    /// <summary>The music face is read the same way, and had the same exposure.</summary>
    /// <remarks>
    /// MEASURED 2026-08-19: emmentaler-20's U+E0A4 reads top <c>-782</c> through Skia on
    /// Windows and <c>-781.982421875</c> on Linux, and the identical <c>-782</c> on both
    /// through the font's tables. The skyline walk over a music glyph reads this path, so
    /// leaving it on the rasteriser would have kept 18 of the 53 Linux failures alive.
    /// </remarks>
    [Theory]
    [InlineData('')]
    [InlineData('')]
    [InlineData('')]
    public void MusicGlyphOutline_IsTheDesignsOwnFontUnits(char glyph)
    {
        var path = TextFontMetrics.MusicGlyphPath(glyph, EmmentalerFaces.DefaultDesign);
        Assert.NotNull(path);
        Assert.False(path!.IsEmpty);
        var bounds = path.Bounds;
        Assert.Equal(Math.Round(bounds.Top), bounds.Top, 3);
        Assert.Equal(Math.Round(bounds.Bottom), bounds.Bottom, 3);
        Assert.Equal(Math.Round(bounds.Left), bounds.Left, 3);
        Assert.Equal(Math.Round(bounds.Right), bounds.Right, 3);
    }
}
