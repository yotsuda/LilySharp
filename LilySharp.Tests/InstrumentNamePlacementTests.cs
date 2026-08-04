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
/// Where an instrument name's right edge goes — LilyPond's
/// <c>system-start-text::calc-x-offset</c>, checked against LilyPond's own output.
/// </summary>
/// <remarks>
/// Every row is a measurement from <c>audit/lp-geometry/probes/instrument-name-x.ly</c> run
/// through LilyPond 2.26.0: the name's own width and the leftmost delimiter's left edge go in,
/// the X extent LilyPond gave the grob comes out. The widths are LILYPOND's, not this build's,
/// which is what makes this a test of the RULE and not of our font.
/// <para>
/// ⚠️ THREE BOOKS BECAUSE THE RULE HAS THREE REGIMES, and one book can only ever show one:
/// a name narrower than the indent (centred in an indent-wide box), a name wider than it
/// (pinned to the delimiter and overflowing left), and a system with no delimiter at all.
/// </para>
/// </remarks>
public class InstrumentNamePlacementTests
{
    /// <summary>LilyPond's own `indent` in staff spaces, from the same dump.</summary>
    private const double Indent = 8.535826771653543;

    /// <summary>Book 1: a GrandStaff, so the delimiter is the printed brace.</summary>
    private const double BraceLeft = 6.8024267716535425;

    /// <summary>Book 2: two plain staves, so the delimiter is the SystemStartBar.</summary>
    private const double BarLeft = 8.475826771653542;

    [Theory]
    // Book 1 — braced group. The last row is the wide-name regime: "Contrabassoon" is wider
    // than the indent, so its right edge pins to (delimiter - padding) and it runs off to the
    // left, ending 6.54 staff spaces LEFT of the system origin. LilyPond really does that.
    [InlineData("I", 0.7170094488188976, BraceLeft, 2.59301811023622)]
    [InlineData("Alto", 3.9264803149606298, BraceLeft, 4.197753543307085)]
    [InlineData("Soprano", 7.306667716535433, BraceLeft, 5.887847244094488)]
    [InlineData("Contrabassoon", 13.042743307086614, BraceLeft, 6.502426771653543)]
    // Book 2 — two plain staves. Same rule, a different delimiter, so the whole block shifts.
    [InlineData("Soprano/bar", 7.306667716535433, BarLeft, 7.561247244094487)]
    [InlineData("Bass/bar", 4.165483464566929, BarLeft, 5.990655118110236)]
    public void MatchesLilyPond(string name, double width, double delimiterLeft, double expected)
        => Assert.Equal(expected,
            SharedRenderer.InstrumentNameRightEdge(width, Indent, delimiterLeft), 9);

    /// <summary>
    /// A system with NO delimiter answers as if the delimiter's left edge were the indent.
    /// </summary>
    /// <remarks>
    /// ⚠️ MEASURED, NOT ASSUMED, and the source reads the other way at first glance:
    /// <c>calc-x-offset</c> seeds <c>total-left</c> with <c>+inf.0</c> and subtracts
    /// <c>(interval-length (cons total-left indent))</c>, which looks like an infinity. It is
    /// not — that interval is EMPTY, and an empty interval's length is 0, so the correction
    /// term vanishes. Book 3 of the probe is a lone staff and lands exactly here.
    /// </remarks>
    [Fact]
    public void WithNoDelimiter_TheIndentStandsInForIt()
        => Assert.Equal(7.621247244094487,
            SharedRenderer.InstrumentNameRightEdge(7.306667716535433, Indent, null), 9);

    /// <summary>
    /// The name clears the delimiter for every name, which is the property the old placement
    /// did not have.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE RULE, NOT A ROW (HANDOFF 5.4). The defect was not one bad name: centring at
    /// <c>Indent / 2</c> put Alto 1.048 from the brace's right edge, Bass 0.638, Piano 0.205,
    /// Tenor 0.154 and Soprano -0.019, against a brace whose ink is 1.3734 wide — so ordinary
    /// names overlapped and only the longest crossed the right edge outright. What is asserted
    /// here is that no width can produce an overlap, sampled across the range that matters
    /// including well past the indent.
    /// </remarks>
    [Theory]
    [InlineData(0.5)]
    [InlineData(3.0)]
    [InlineData(7.3)]
    [InlineData(8.5)]
    [InlineData(8.6)]
    [InlineData(13.0)]
    [InlineData(25.0)]
    public void NoWidthOverlapsTheDelimiter(double width)
    {
        double right = SharedRenderer.InstrumentNameRightEdge(width, Indent, BraceLeft);
        Assert.True(right <= BraceLeft - 0.3 + 1e-12,
            $"a name {width} wide reaches {right}, past the delimiter's left edge {BraceLeft} "
            + "less its 0.3 padding");
    }
}
