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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A lead sheet's chord and lyric rows subdivide a bar differently (chords on the half note,
/// three syllables on thirds), so the union of timing columns does NOT match the syllable count.
/// The item-index lyric spacing bailed out on that mismatch, so wide syllables crowded and the
/// first one overran the barline. The column-based reservation must widen the springs regardless,
/// even across a chord-only column that sits between two syllables.
/// </summary>
[Trait("Category", "Unit")]
public class LeadSheetLyricSpacingTests
{
    private static LyricItem Ly(string text, Fraction timing)
        => new(Text: text, MeasureIndex: 0, ItemIndex: 0, Timing: timing, IsLyricsRow: true);

    [Fact]
    public void ReservesLyricWidthAcrossAChordOnlyColumn()
    {
        // Columns = union of chords {0, 1/2} and lyrics {0, 1/3, 2/3} → four columns, five springs.
        var columns = new[] { Fraction.Zero, new Fraction(1, 3), new Fraction(1, 2), new Fraction(2, 3) };
        var springs = Enumerable.Repeat(new Spring(0.5, 0.5, 1.0), columns.Length + 1).ToImmutableArray();
        var won = Ly("won", Fraction.Zero);
        var der = Ly("der", new Fraction(1, 3));
        var what = Ly("what", new Fraction(2, 3));

        var result = LyricSpacing.ApplyLeadSheetLyricSpacing(springs, columns, 0, new[] { won, der, what });

        // won → der (adjacent columns 0→1): that single spring reserves their combined ink.
        double wonDer = LyricSpacing.CalculateLyricDistance(new List<LyricItem> { won }, new List<LyricItem> { der });
        Assert.True(result[1].MinDistance >= wonDer - 1e-6,
            $"won→der spring {result[1].MinDistance} < needed {wonDer}");

        // der (col 1) → what (col 3) SPANS the chord-only column 1/2: the two springs together clear it.
        double derWhat = LyricSpacing.CalculateLyricDistance(new List<LyricItem> { der }, new List<LyricItem> { what });
        Assert.True(result[2].MinDistance + result[3].MinDistance >= derWhat - 1e-6,
            $"der→what span {result[2].MinDistance + result[3].MinDistance} < needed {derWhat}");

        // The leading syllable clears the start barline; the trailing one the end barline.
        Assert.True(result[0].MinDistance >= LyricSpacing.GetLyricLeftExtent(new List<LyricItem> { won }) + GlyphMetrics.MinItemGap - 1e-6);
        Assert.True(result[^1].MinDistance >= LyricSpacing.GetLyricRightExtent(new List<LyricItem> { what }) + GlyphMetrics.MinItemGap - 1e-6);
    }

    [Fact]
    public void NoLyricsInMeasure_LeavesSpringsUnchanged()
    {
        var columns = new[] { Fraction.Zero, new Fraction(1, 2) };
        var springs = Enumerable.Repeat(new Spring(0.5, 0.5, 1.0), columns.Length + 1).ToImmutableArray();
        var result = LyricSpacing.ApplyLeadSheetLyricSpacing(springs, columns, 0, new[] { Ly("x", Fraction.Zero) with { MeasureIndex = 9 } });
        Assert.Equal(springs, result);
    }
}
