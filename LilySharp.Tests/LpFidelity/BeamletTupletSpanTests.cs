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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// A beamlet must not hang out of the tuplet its stem belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The <c>beam.beamlet.*</c> ledger points hold the six shapes of the flag-direction rule
/// against real LilyPond, and every one of their books is plain 4/4 with no tuplet in it.
/// This is the one branch of <see cref="BeamingPattern"/> they cannot reach: a stem standing
/// at the START or the STOP of a tuplet span keeps its flag CENTER — so the chip never fires
/// for it — and is then clamped to its neighbour's count on the outward side
/// (lily/beaming-pattern.cc:190-200 at_span_start / at_span_stop).
/// </para>
/// <para>
/// ⚠️ It is asserted here rather than through a rendered book because no fixture reaches it:
/// Lily# ends an automatic beam at every tuplet boundary, so only a MANUAL bracket written
/// across one gets there. Without this the branch would have no observer at all — a rule with
/// nothing watching it is a rule that quietly stops being true.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BeamletTupletSpanTests
{
    /// <summary>Counts 2 3 1 1: sixteenth, thirty-second, eighth, eighth, filling 7/32.</summary>
    private static BeamingPattern.Element[] Pattern(bool secondStemStartsATuplet) =>
    [
        new(new Fraction(0), new Fraction(1, 16), 2),
        new(new Fraction(1, 16), new Fraction(1, 32), 3, AtSpanStart: secondStemStartsATuplet),
        new(new Fraction(3, 32), new Fraction(1, 8), 1),
        new(new Fraction(7, 32), new Fraction(1, 8), 1),
    ];

    private static readonly BeamingPattern.Options CommonTime =
        BeamingPattern.Options.For(new TimeSignature(4, 4));

    [Fact]
    public void AStemAtATupletsStart_IsClampedToItsLeftNeighbourAndKeepsItsOwnRight()
    {
        var counts = BeamingPattern.Beamify(Pattern(secondStemStartsATuplet: true), CommonTime);

        // The thirty-second's flag stays CENTER, so it keeps all three beams on the right…
        Assert.Equal(3, counts[1].Right);
        // …and the left is cut to what the sixteenth before it offers, rather than poking two
        // beamlets back across the tuplet's opening.
        Assert.Equal(2, counts[1].Left);
    }

    [Fact]
    public void WithoutTheTupletBoundary_TheSameStemIsFlaggedAndChippedInstead()
    {
        // The PERTURBATION: the identical rhythm with no span boundary on it. The flag rule
        // now runs — the right neighbour carries fewer beams than the left, so the flag points
        // LEFT, the left side keeps its own three, and the RIGHT is chipped by
        // max(3 - 1, 1) = 2. Both readings differ from the clamped ones above, which is what
        // makes the assertions there load-bearing rather than a restatement of the general
        // rule.
        var counts = BeamingPattern.Beamify(Pattern(secondStemStartsATuplet: false), CommonTime);

        Assert.Equal(3, counts[1].Left);
        Assert.Equal(1, counts[1].Right);
    }
}
