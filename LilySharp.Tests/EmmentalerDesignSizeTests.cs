// Lily# — a music notation language and engraver.
// Copyright (C) 2026 yotsuda
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
/// Which Emmentaler design a size lands on, and what LilyPond then scales it by.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size;
///   scm/lily-library.scm:1702-1710 feta-design-size-mapping.
/// The sizes are LilyPond's own arithmetic (<c>20 · 2^(step/6)</c>), and the design each one
/// lands on is what decides which LILC table a glyph's box comes from.
/// </remarks>
public sealed class EmmentalerDesignSizeTests
{
    /// <summary>
    /// A grace is font-size −3, which asks for 14.142 and lands on design 14 — the reading
    /// the whole <c>grace.column</c> island turns on.
    /// </summary>
    [Fact]
    public void GraceSize_LandsOnDesignFourteen()
    {
        Assert.Equal(14.142136, EmmentalerDesignSize.RequestedSize(-3), 6);
        Assert.Equal(14, EmmentalerDesignSize.ForFontSizeStep(-3).Rounded);
        // …and the file is then stretched by the sliver between 14.142 and 14.14.
        Assert.Equal(1.000151, EmmentalerDesignSize.Magnification(-3), 6);
    }

    /// <summary>Full size asks for exactly 20 and needs no magnification.</summary>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-6, 11)]    // one octave down: 10.0 → 11 is the nearest by ratio
    [InlineData(6, 26)]     // one octave up: 40.0 → 26 is as far as the designs go
    [InlineData(-1, 18)]    // 17.818 ≈ the 18 design's 17.82
    [InlineData(-2, 16)]    // 15.874 ≈ 15.87
    [InlineData(1, 23)]     // 22.449 ≈ 22.45
    public void FontSizeStep_LandsOnTheDesignLilyPondPicks(double step, int expectedDesign)
        => Assert.Equal(expectedDesign, EmmentalerDesignSize.ForFontSizeStep(step).Rounded);

    /// <summary>
    /// The choice is by RATIO, not by difference — the two disagree, and LilyPond asks for
    /// the ratio.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:58-60 best_rounded_design_size's own comparison —
    ///   <c>requested &gt; actual ? requested/actual : actual/requested</c>, minimised.
    /// A ratio rule switches designs at the GEOMETRIC mean of two design sizes, a difference
    /// rule at the ARITHMETIC one, and between 12.60 and 14.14 those are 13.3475 and 13.37.
    /// Anything asking for a size in that band gets a different FILE under the two rules, so
    /// this is the whole of why the port is a ratio.
    /// </remarks>
    [Fact]
    public void TheChoiceIsByRatio_NotByDifference()
    {
        double geometric = System.Math.Sqrt(12.60 * 14.14);   // 13.3475 — the ratio's split
        double arithmetic = (12.60 + 14.14) / 2;              // 13.37   — a difference's

        Assert.Equal(13, EmmentalerDesignSize.BestRounded(geometric - 0.01).Rounded);
        // In the band between the two means the ratio has already moved on and a difference
        // rule has not: this is the assertion a difference-based rewrite fails.
        Assert.Equal(14, EmmentalerDesignSize.BestRounded(geometric + 0.01).Rounded);
        Assert.Equal(14, EmmentalerDesignSize.BestRounded(arithmetic - 0.01).Rounded);
    }
}
