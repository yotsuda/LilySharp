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
/// A beamlet must not hang out of the tuplet its stem belongs to, and a stem inside a
/// tuplet is ranked in WRITTEN proportions.
/// </summary>
/// <remarks>
/// <para>
/// The <c>beam.beamlet.*</c> ledger points hold the six shapes of the flag-direction rule
/// against real LilyPond, and every one of their books is plain 4/4 with no tuplet in it.
/// Two branches of <see cref="BeamingPattern"/> they cannot reach are watched here: a stem
/// standing at the START or the STOP of a tuplet span keeps its flag CENTER — so the chip
/// never fires for it — and is then clamped to its neighbour's count on the outward side
/// (lily/beaming-pattern.cc:190-200 at_span_start / at_span_stop); and a stem INSIDE a
/// tuplet is ranked by the span stack in written proportions
/// (lily/beaming-pattern.cc:291-404), whose rendered observer is the LP regression book
/// beamlet-test.ly (audit/lp-regression).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BeamletTupletSpanTests
{
    /// <summary>Counts 2 3 1 1: sixteenth, thirty-second, eighth, eighth, filling 7/32.
    /// The tuplet variant hangs a span over just the thirty-second — its num/den are never
    /// read, because the span opens only for stems strictly PAST its start and none is —
    /// so only the boundary tests see it.</summary>
    private static BeamingPattern.Element[] Pattern(bool secondStemStartsATuplet) =>
    [
        new(new Fraction(0), new Fraction(1, 16), 2),
        new(new Fraction(1, 16), new Fraction(1, 32), 3,
            Tuplet: secondStemStartsATuplet
                ? new BeamingPattern.TupletDescription(
                    new Fraction(1, 16), new Fraction(3, 32), 2, 3, null)
                : null),
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

    /// <summary>The last bar of LP regression beamlet-test.ly:
    /// <c>\tuplet 5/4 { a8 a32 a8 a16. a8 a8 }</c> — moments are actual (factor 4/5),
    /// counts 1 3 1 2 1 1, and the whole sextet is one span from 0 to 1/2.</summary>
    private static BeamingPattern.Element[] BeamletTestT8(bool insideTheTuplet)
    {
        var t = insideTheTuplet
            ? new BeamingPattern.TupletDescription(
                Fraction.Zero, new Fraction(1, 2), 4, 5, null)
            : null;
        return
        [
            new(new Fraction(0), new Fraction(1, 10), 1, t),
            new(new Fraction(1, 10), new Fraction(1, 40), 3, t),
            new(new Fraction(1, 8), new Fraction(1, 10), 1, t),
            new(new Fraction(9, 40), new Fraction(3, 40), 2, t),
            new(new Fraction(3, 10), new Fraction(1, 10), 1, t),
            new(new Fraction(2, 5), new Fraction(1, 10), 1, t),
        ];
    }

    [Fact]
    public void TheThirtySecondOfBeamletTestsLastTuplet_PointsItsBeamletsRight()
    {
        // The a32 stands ON a span moment: read through the span's grid and the 4/5 factor,
        // its written length ranks it 1 against the following eighth's 3, so its two spare
        // beamlets point RIGHT — LilyPond's rendering of the book's own claim
        // (lily/beaming-pattern.cc:291-404, the span stack; texidoc: "beamlets should point
        // away from complete beat units … in tuplets as well").
        var counts = BeamingPattern.Beamify(BeamletTestT8(insideTheTuplet: true), CommonTime);

        Assert.Equal(1, counts[1].Left);
        Assert.Equal(3, counts[1].Right);
    }

    [Fact]
    public void WithoutTheTupletSpan_TheSameThirtySecondPointedLeft()
    {
        // The PERTURBATION, and the shape of the defect this port closed: with the root span
        // alone the tie-break read 1 against 1 — the actual moments 1/10 and 1/8 rank equal
        // under the whole-measure grid — and the beamlets pointed LEFT.
        var counts = BeamingPattern.Beamify(BeamletTestT8(insideTheTuplet: false), CommonTime);

        Assert.Equal(3, counts[1].Left);
        Assert.Equal(1, counts[1].Right);
    }

    [Fact]
    public void TheDottedSixteenth_PointsLeftWithAndWithoutTheSpan()
    {
        // The book's OTHER interior peak (a16. at 9/40) does not move: both grids rank it 3
        // against the following on-moment eighth's 1, so the sixteenth beamlet points LEFT
        // either way — which is what confined the book's divergence to the a32 alone.
        foreach (bool inside in new[] { true, false })
        {
            var counts = BeamingPattern.Beamify(BeamletTestT8(inside), CommonTime);
            Assert.Equal(2, counts[3].Left);
            Assert.Equal(1, counts[3].Right);
        }
    }
}
