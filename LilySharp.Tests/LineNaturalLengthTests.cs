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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A line's length at force 0 — what the ragged line breaker measures its whitespace from —
/// is LilyPond's <c>configuration_length (0.0)</c>: every spring at <c>max(min, ideal)</c>
/// (HANDOFF §2 R7⒝, session 570).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spring.cc:218-228 Spring::length — at force 0 a spring stands at
///   <c>max (0, blocking_force_)</c>, i.e. its minimum when that exceeds its ideal.
/// The breaker used <c>max(Σideal, Σmin)</c>, which is short whenever a spring whose minimum
/// exceeds its ideal shares a measure with ordinary ones.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LineNaturalLengthTests
{
    [Fact]
    public void ABlockingSpringStandsAtItsMinimum_AnOrdinaryOneAtItsIdeal()
    {
        // One blocking spring (min 5 > ideal 3) and one ordinary (min 1 < ideal 3), plus a
        // bar line: Σideal = Σmin = 6, so the sums' max says 6 + 0.19; LilyPond says 5 + 3.
        var springs = ImmutableArray.Create(new Spring(3, 5, 1), new Spring(3, 1, 1));
        var d = new MeasureSpringData(IdealWidth: 6.19, MinWidth: 6.19, InverseStretchStrength: 2,
            Springs: springs, RigidWidth: 0.19);
        Assert.Equal(0.19 + 5 + 3, KnuthPlassBreaker.NaturalWidthOf(d), 9);
    }

    [Fact]
    public void AMeasureWithoutSprings_TakesTheLargerSum()
    {
        var d = new MeasureSpringData(IdealWidth: 4, MinWidth: 5, InverseStretchStrength: 1);
        Assert.Equal(5, KnuthPlassBreaker.NaturalWidthOf(d), 9);
    }
}
