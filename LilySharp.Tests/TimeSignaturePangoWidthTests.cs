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
    /// LilyPond's own markup width for EVERY digit, measured on the grob.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS THEORY USED TO COVER SIX DIGITS, AND THE FOUR IT LEFT OUT WERE THE POINT.
    /// Until 2026-08-14 it pinned only 4, 0, 2, 8, 6 and 9 — with a comment saying in so many
    /// words that Lily# read the FATTENED cut and that the fattened advance "matches the ASCII
    /// digit LilyPond sets only for '4'". Every digit it skipped is one where the two cuts
    /// could disagree, so the test was written to pass under either answer and could not fail
    /// when the cut was wrong. It was: <c>\number</c> declares no font-features, so LilyPond
    /// sets the PLAIN digits, and 1 (37 device pixels, not 38) and 7 (39, not 38) were both
    /// off by a pixel, in opposite directions (ledger
    /// <c>line-start.time-to-first-note.digit-one</c> / <c>.digit-seven</c>,
    /// <c>MeterGlyphRun</c>).
    /// ⇒ All ten are pinned now, each to the width LilyPond laid the digit out at — read off
    /// its own stencil expressions, where a row's width is twice the centring translation
    /// (audit/lp-geometry/probes/barline-spacing.ly scores TD1 / TD7 / TDK carry the three
    /// that a ledger point watches).
    /// </remarks>
    [Theory]
    [InlineData(0, 1.468162)]   // 43 px
    [InlineData(1, 1.263302)]   // 37 px — the narrowest, and one of the two the cut moved
    [InlineData(2, 1.468162)]   // 43 px
    [InlineData(3, 1.331589)]   // 39 px
    [InlineData(4, 1.604735)]   // 47 px — the widest, and the digit both cuts agree on
    [InlineData(5, 1.331589)]   // 39 px
    [InlineData(6, 1.365732)]   // 40 px
    [InlineData(7, 1.331589)]   // 39 px — the other digit the cut moved, the other way
    [InlineData(8, 1.468162)]   // 43 px
    [InlineData(9, 1.365732)]   // 40 px
    public void Digit_WidthIsSnappedToPangosGrid(int digit, double expected)
        => Assert.Equal(expected, GlyphMetrics.GetTimeSigDigitWidth(digit), 6);

    /// <summary>
    /// A two-glyph row is not the sum of its digits: the pair kerns, and the kern is applied
    /// INSIDE the per-glyph pixel snap.
    /// </summary>
    /// <remarks>
    /// LilyPond's own numerator rows, measured on the grob (scratch sighting, session 164):
    /// "10" is 77 device pixels, "11" 74, "12" 80 and "16" 74. The 11/16 pair is the control
    /// that the kern is real — they reach the same total by different routes (37+37 with no
    /// kern against 34+40 with one), and an implementation that dropped the kern would print
    /// 74 and 77.
    /// </remarks>
    [Theory]
    [InlineData("10", 2.629035)]
    [InlineData("11", 2.526605)]
    [InlineData("12", 2.731465)]
    [InlineData("16", 2.526605)]
    public void TwoDigitRow_KernsInsideTheSnap(string row, double expected)
        => Assert.Equal(expected, LilySharp.Core.Svg.Layout.MeterGlyphRun.Width(row), 6);

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
