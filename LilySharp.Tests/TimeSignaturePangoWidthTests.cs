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

using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Pins the time signature's width to the value LilyPond's markup path produces, which is
/// the raw digit advance snapped to Pango's device-pixel grid.
/// </summary>
/// <remarks>
/// LilyPond sets a default time signature as text — <c>\number</c> markup through Pango —
/// not as a music glyph, so the width is hinted to a whole device pixel. The expected
/// numbers here were measured on the TimeSignature grob itself on 2.26.0 via
/// <c>ly:text-interface::interpret-markup</c>, and they double as the check that Lily#'s
/// port of that quantum (derived from PANGO_RESOLUTION, not fitted) is right.
/// </remarks>
[Trait("Category", "Unit")]
public class TimeSignaturePangoWidthTests
{
    /// <summary>
    /// LilyPond's own markup widths for every digit, measured on the grob. Lily#'s
    /// unquantised advances are the fattened-digit metrics, which match the ASCII digit
    /// LilyPond sets only for '4'; the others are pinned to the fattened advance snapped to
    /// the same grid, which is what Lily# computes today.
    /// </summary>
    [Theory]
    // digit 4 is the one the ledger measures, and the one whose fattened and ASCII
    // advances coincide, so it is pinned to LilyPond's own markup width to the digit.
    [InlineData(4, 1.604735)]
    // The remaining digits are pinned to the quantised FATTENED advance (what Lily# has),
    // which for these happens to equal LilyPond's ASCII markup width — 0/2/8 share 1.468162
    // and 6/9 share 1.365732, a grouping only quantisation produces.
    [InlineData(0, 1.468162)]
    [InlineData(2, 1.468162)]
    [InlineData(8, 1.468162)]
    [InlineData(6, 1.365732)]
    [InlineData(9, 1.365732)]
    public void Digit_WidthIsSnappedToPangosGrid(int digit, double expected)
        => Assert.Equal(expected, GlyphMetrics.GetTimeSigDigitWidth(digit), 6);

    /// <summary>
    /// The default 4/4 and 2/2 print the <c>timesig.C44</c>/<c>timesig.C22</c> GLYPHS —
    /// LilyPond's default style takes the glyph (LILC ink) path for exactly those two
    /// fractions (make-c-time-signature-markup) — so their width is the C glyphs' LILC
    /// ink 1.7, NOT a quantised digit (the former pin here asserted the digit width and
    /// left every 4/4 first note 0.0953 short of LilyPond — ledger
    /// line-start.time-to-first-note.{standard-key,cut-common}). Every OTHER fraction
    /// stays on the Pango digit path: 3/4 rides its quantised '4' denominator, which is
    /// what barline.next.time-change-to-notehead is exact on.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/time-signature-settings.scm:954-964,981-982.</remarks>
    [Fact]
    public void FourFourAndTwoTwo_AreTheCGlyphInk_NotQuantisedDigits()
    {
        Assert.Equal(1.700000, GlyphMetrics.GetTimeSigWidth(4, 4), 6); // timesig.C44 LILC
        Assert.Equal(1.700000, GlyphMetrics.GetTimeSigWidth(2, 2), 6); // timesig.C22 LILC
        Assert.Equal(1.604735, GlyphMetrics.GetTimeSigWidth(3, 4), 6); // digit path, unchanged
    }

    /// <summary>
    /// Every quantised width is an exact integer multiple of the quantum — the property
    /// that makes this a snap to a grid rather than a fudge toward one measured number.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void EveryWidthIsAWholeNumberOfPixels(int digit)
    {
        const double q = 72.0 * 72.27 / (1200.0 * 5.0 * 25.4);
        double steps = GlyphMetrics.GetTimeSigDigitWidth(digit) / q;
        Assert.Equal(System.Math.Round(steps), steps, 6);
    }
}
