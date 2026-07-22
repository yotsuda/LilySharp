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
    [Fact]
    public void EachStyleResolvesToItsOwnFace()
    {
        double regular = TextFontMetrics.Advance("Allegro", 1.0);
        double bold = TextFontMetrics.Advance("Allegro", 1.0, sans: false, FontStyle.Bold);
        double italic = TextFontMetrics.Advance("Allegro", 1.0, sans: false, FontStyle.Italic);
        double sans = TextFontMetrics.Advance("Allegro", 1.0, sans: true);

        Assert.True(bold > regular, $"bold {bold} should exceed regular {regular}");
        Assert.NotEqual(regular, italic, 6);
        Assert.NotEqual(regular, sans, 6);
    }

    /// <summary>Advances scale linearly with the font size, and an empty string is zero.</summary>
    [Fact]
    public void AdvanceScalesWithSizeAndHandlesEmpty()
    {
        double one = TextFontMetrics.Advance("Andante", 1.0);
        Assert.Equal(one * 2.4, TextFontMetrics.Advance("Andante", 2.4), 9);
        Assert.Equal(0.0, TextFontMetrics.Advance("", 2.4));
        Assert.Equal(0.0, TextFontMetrics.InkHeight("", 2.4));
    }

    /// <summary>
    /// The bundled serif is LilyPond's own text face by metrics. C059 (which LilyPond
    /// actually resolves to) and TeX Gyre Schola agree on every advance measured, so these
    /// values double as a check that the right family got bundled.
    /// </summary>
    [Theory]
    [InlineData("Allegro", 3.3330)]
    [InlineData("Andante con moto", 8.3520)]
    [InlineData("Fine", 2.0930)]
    [InlineData("3", 0.5560)]
    public void SerifAdvances_AreTheLilyPondTextFaces(string text, double expectedPerEm)
        => Assert.Equal(expectedPerEm, TextFontMetrics.Advance(text, 1.0), 4);
}
