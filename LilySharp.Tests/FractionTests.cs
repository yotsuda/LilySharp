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
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

public sealed class FractionTests
{
    [Theory]
    [InlineData(2, 4, 1, 2)]
    [InlineData(3, 9, 1, 3)]
    [InlineData(6, 3, 2, 1)]
    [InlineData(0, 5, 0, 1)]
    [InlineData(1, -2, -1, 2)]   // sign normalized to numerator
    [InlineData(-2, -4, 1, 2)]   // double-negative
    public void Constructor_ReducesAndNormalizes(int n, int d, int en, int ed)
    {
        var f = new Fraction(n, d);
        Assert.Equal(en, f.Numerator);
        Assert.Equal(ed, f.Denominator);
    }

    [Fact]
    public void Constructor_ZeroDenominator_Throws()
        => Assert.Throws<ArgumentException>(() => new Fraction(1, 0));

    [Fact]
    public void Constructor_IntMinValue_DoesNotThrow()
    {
        // Pre-fix this threw OverflowException via Math.Abs(int.MinValue).
        var f = new Fraction(int.MinValue, 1);
        Assert.Equal(int.MinValue, f.Numerator);
        Assert.Equal(1, f.Denominator);
    }

    [Theory]
    [InlineData(1, 4, 1, 4, 1, 2)]
    [InlineData(1, 3, 1, 6, 1, 2)]
    [InlineData(1, 2, 1, 3, 5, 6)]
    public void Addition(int an, int ad, int bn, int bd, int en, int ed)
    {
        var r = new Fraction(an, ad) + new Fraction(bn, bd);
        Assert.Equal(new Fraction(en, ed), r);
    }

    [Theory]
    [InlineData(1, 2, 1, 4, 1, 4)]
    [InlineData(3, 4, 1, 4, 1, 2)]
    public void Subtraction(int an, int ad, int bn, int bd, int en, int ed)
    {
        var r = new Fraction(an, ad) - new Fraction(bn, bd);
        Assert.Equal(new Fraction(en, ed), r);
    }

    [Theory]
    [InlineData(2, 3, 3, 4, 1, 2)]
    [InlineData(1, 2, 1, 2, 1, 4)]
    public void Multiplication(int an, int ad, int bn, int bd, int en, int ed)
    {
        var r = new Fraction(an, ad) * new Fraction(bn, bd);
        Assert.Equal(new Fraction(en, ed), r);
    }

    [Theory]
    [InlineData(1, 2, 1, 4, 2, 1)]
    [InlineData(1, 3, 2, 3, 1, 2)]
    public void Division(int an, int ad, int bn, int bd, int en, int ed)
    {
        var r = new Fraction(an, ad) / new Fraction(bn, bd);
        Assert.Equal(new Fraction(en, ed), r);
    }

    [Theory]
    [InlineData(4, 1, 3, 8)]    // quarter, 1 dot -> 3/8
    [InlineData(4, 2, 7, 16)]   // quarter, 2 dots -> 7/16
    [InlineData(2, 1, 3, 4)]    // half, 1 dot -> 3/4
    public void Dotted(int noteValue, int dots, int en, int ed)
    {
        var r = Fraction.FromNoteValue(noteValue).Dotted(dots);
        Assert.Equal(new Fraction(en, ed), r);
    }

    [Fact]
    public void Comparison_And_Equality()
    {
        Assert.True(new Fraction(1, 4) < new Fraction(1, 2));
        Assert.True(new Fraction(1, 2) > new Fraction(1, 3));
        Assert.True(new Fraction(2, 4) == new Fraction(1, 2));
        Assert.True(new Fraction(1, 3) <= new Fraction(1, 3));
        Assert.Equal(new Fraction(1, 2).GetHashCode(), new Fraction(2, 4).GetHashCode());
    }

    [Fact]
    public void Accumulation_OfManyTuplets_IsExactAndDoesNotThrow()
    {
        // 12 eighth-note triplets (each 1/8 * 2/3 = 1/12) should sum to exactly 1.
        var triplet8 = Fraction.Eighth * new Fraction(2, 3);
        var sum = Fraction.Zero;
        for (int i = 0; i < 12; i++) sum += triplet8;
        Assert.Equal(new Fraction(1, 1), sum);
    }

    [Fact]
    public void GenuineOverflow_Throws_RatherThanWrap()
    {
        // 1/1_000_000 * 1/1_000_000 = 1/10^12; the denominator cannot fit in int,
        // and the value is already reduced, so this must throw rather than wrap to
        // a wrong (possibly negative) duration.
        var tiny = new Fraction(1, 1_000_000);
        Assert.Throws<OverflowException>(() => tiny * tiny);
    }
}
