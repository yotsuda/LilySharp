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
/// The trill line's glyph-run geometry: <c>make_trill_line</c>'s element fit, and the
/// rule that keeps it correct — it is applied ONCE, to the allotted span.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/line-interface.cc:48-108 make_trill_line.
/// </remarks>
[Trait("Category", "Unit")]
public class TrillWaveOutlineTests
{
    // The two boxes of scripts.trill_element, from the font: the LILC stencil width is the
    // repetition STEP, the outline width is the first copy's own LENGTH.
    private const double Step = 1.0;
    private const double TrueLength = 1.448;

    [Fact]
    public void DrawnLength_IsLilyPondsWholeElementFit()
    {
        // TXW's span: bound at the stop column, line starting at the tr glyph's true right
        // — allotted 7.496600, and LilyPond's dump ends the line 0.0486 short of the bound
        // because only six extra whole elements fit.
        double allotted = 7.4966;
        Assert.Equal(TrueLength + 6 * Step, TrillWaveOutline.DrawnLength(allotted), 9);

        // The remainder is always inside one step, and the run never exceeds the span
        // once a single element fits.
        double drawn = TrillWaveOutline.DrawnLength(allotted);
        Assert.True(drawn <= allotted);
        Assert.True(allotted - drawn < Step);
    }

    [Fact]
    public void DrawnLength_KeepsOneElement_EvenWhenItDoesNotFit()
    {
        // "Always have at least one trill element, even if the space allotted technically
        // doesn't allow it" — LilyPond's own words at line-interface.cc:84-85. The line
        // then reaches PAST its bound, on purpose.
        Assert.Equal(TrueLength, TrillWaveOutline.DrawnLength(0.5), 9);
        Assert.Equal(TrueLength, TrillWaveOutline.DrawnLength(TrueLength), 9);
    }

    [Fact]
    public void DrawnLength_ReadsTheAllottedSpan_AndTheFitIsAppliedOnce()
    {
        // The fit is a pure function of the ALLOTTED span, and every consumer (the
        // engraver's aligned_side, the stacker's mover profile, the renderer's polyline)
        // calls it with that span — TrillSpannerLayout.LineEndX keeps the BOUND, never a
        // fitted end. LilyPond likewise fits once, inside make_trill_line, from the
        // spanner's span points (line-interface.cc:88-98).
        double fitted = TrillWaveOutline.DrawnLength(7.4966);
        Assert.Equal(TrueLength + 6 * Step, fitted, 9);
        // Re-fitting a fitted length is a no-op HERE, at exactly this value — which is
        // not a licence to store one: the layout carried the fitted end for one build and
        // ledger trill.x.wave-zone fell from 4.720541 to its quiet 3.550000, a regression
        // whose mechanism was NOT isolated (it is not this arithmetic: measured, both
        // orders return the same length for every span the probe books use). One
        // spelling, applied once, is what LilyPond does and what the code does.
        Assert.Equal(fitted, TrillWaveOutline.DrawnLength(fitted), 9);
    }
}
