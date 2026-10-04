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
/// Rods on the parts of a SERIES spring — the springs LilyPond has between columns that Lily#
/// carries inside one timing slot. A rod is LilyPond's <c>Simple_spacer::add_rod</c>: it raises
/// the blocking force of every spring in its range (lily/simple-spacer.cc:89-127), and
/// <c>set_blocking_force</c> raises the MINIMUM to the length at that force, never the ideal
/// (lily/spring.cc:183-195) — so at force 0 the part stands at the rod, and stretched it stands
/// at <c>max (rod, ideal + force × k)</c>, not at <c>rod + force × k</c>.
/// </summary>
public class SeriesSpringRodTests
{
    // Two parts: an approach (ideal 2, min 1) and a second spring (ideal 1, min 0.5), both
    // stretching at 1 a unit of force.
    private static Spring TwoParts() => Spring.InSeries(ImmutableArray.Create(
        new Spring(2.0, 1.0, 1.0, 1.0),
        new Spring(1.0, 0.5, 1.0, 0.5)), 1.5);

    [Fact]
    public void ARodOnOnePart_HoldsThatPartAtTheRod_AtForceZero()
    {
        var s = TwoParts().WithPartRod(1, 1.5);

        Assert.Equal(1.5, s.Series[1].Length(0.0), 9);
        Assert.Equal(2.0, s.Series[0].Length(0.0), 9);   // the other part is untouched
        Assert.Equal(3.5, s.Length(0.0), 9);
    }

    [Fact]
    public void ARodOnOnePart_RaisesItsMinimum_NotItsIdeal()
    {
        var s = TwoParts().WithPartRod(1, 1.5);

        // force 1: ideal + f k = 2 > the rod 1.5, so the part is 2 — a rod folded into the
        // ideal would answer 1.5 + 1 = 2.5.
        Assert.Equal(2.0, s.Series[1].Length(1.0), 9);
        Assert.Equal(1.0, s.Series[1].IdealDistance, 9);
    }

    [Fact]
    public void ARodBelowThePartsMinimum_IsDropped()
    {
        var before = TwoParts();
        var after = before.WithPartRod(1, 0.4);

        Assert.Same(before, after);
    }

    [Fact]
    public void ARodOverASeriesSpring_RaisesEveryPart()
    {
        // One rod over the whole series spring (ideal 3, stretching at 2): 3 + 2 f = 4 gives
        // f = 0.5, and EACH part takes that blocking force — the approach 2.5, the second 1.5.
        var rodded = SpringSolver.ApplyRods(ImmutableArray.Create(TwoParts()),
            new[] { (0, 1, 4.0) })[0];

        Assert.Equal(2.5, rodded.Series[0].Length(0.0), 9);
        Assert.Equal(1.5, rodded.Series[1].Length(0.0), 9);
        Assert.Equal(4.0, rodded.Length(0.0), 9);
    }
}
