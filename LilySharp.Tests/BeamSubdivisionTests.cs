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

using System.Linq;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Verifies the faithful port of LilyPond's beam subdivision (rank assignment and
/// segment collection). A rank is measured in beam-translation units from the
/// primary line (rank 0); a positive rank sits above it, negative below, so the
/// SECONDARY of a down-stem group is +1 (toward the heads above) and of an up-stem
/// group −1 (heads below). A beam through a knee keeps every level a single span
/// (two straight parallel lines), while a fractional beam is a short/stub span.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:294 calc_beaming, :457 calc_beam_segments.
/// </remarks>
[Trait("Category", "Unit")]
public class BeamSubdivisionTests
{
    private static BeamSubdivision.StemBeaming S(int l, int r, int dir, double x) => new(l, r, dir, x);

    [Fact]
    public void NormalUp_SecondaryBelow()
    {
        // 16-16, both up: secondary rank −1 (below, toward the heads).
        var ranks = BeamSubdivision.CalcBeaming(new[] { S(0, 2, +1, 0), S(2, 0, +1, 2) });
        Assert.Equal(new[] { 0, -1 }, ranks[0].Right);
        Assert.Equal(new[] { 0, -1 }, ranks[1].Left);
        Assert.Equal(0, ranks[0].Multiplicity(+1)); // up-stem reaches the primary
    }

    [Fact]
    public void NormalDown_SecondaryAbove()
    {
        // 16-16, both down: secondary rank +1 (above, toward the heads).
        var ranks = BeamSubdivision.CalcBeaming(new[] { S(0, 2, -1, 0), S(2, 0, -1, 2) });
        Assert.Equal(new[] { 0, 1 }, ranks[0].Right);
        Assert.Equal(0, ranks[0].Multiplicity(-1)); // down-stem reaches the primary
    }

    [Fact]
    public void Knee_16_16_8_FractionalSecondaryOnTheTwoSixteenths()
    {
        // Two down 16ths + an up 8th: the 16th (rank 1) is a fractional beam over the
        // two down-stems only, above the primary; the primary spans all three.
        var stems = new[] { S(0, 2, -1, 0), S(2, 1, -1, 2), S(1, 0, +1, 4) };
        var ranks = BeamSubdivision.CalcBeaming(stems);
        Assert.Contains(1, ranks[0].Right);       // 16th present on stem 0
        Assert.Contains(1, ranks[1].Left);        // and stem 1's left
        Assert.DoesNotContain(1, ranks[1].Right); // but NOT continuing to the 8th
        Assert.DoesNotContain(1, ranks[2].Left);

        var segs = BeamSubdivision.CalcBeamSegments(stems, ranks, 1.1, 0.75, 0.05);
        var primary = segs.Single(s => s.Rank == 0);
        var secondary = segs.Single(s => s.Rank == 1);
        Assert.True(primary.XRight - primary.XLeft > secondary.XRight - secondary.XLeft,
            "primary spans further than the fractional 16th");
    }

    [Fact]
    public void Knee_16_16_16_16_TwoStraightParallelBeams()
    {
        // Two down then two up 16ths: BOTH ranks span the whole group — two straight
        // parallel lines, no twist, no break. Down-stems reach rank 0, up-stems rank 1.
        var stems = new[] { S(0, 2, -1, 0), S(2, 2, -1, 2), S(2, 2, +1, 4), S(2, 0, +1, 6) };
        var ranks = BeamSubdivision.CalcBeaming(stems);
        var segs = BeamSubdivision.CalcBeamSegments(stems, ranks, 1.1, 0.75, 0.05);
        Assert.Equal(2, segs.Count); // exactly two spans
        foreach (var seg in segs)
        {
            Assert.True(seg.XLeft <= 0.0 && seg.XRight >= 6.0,
                $"rank {seg.Rank} should span the whole group");
        }
        Assert.Equal(0, ranks[0].Multiplicity(-1)); // down-stem reaches the lower line
        Assert.Equal(1, ranks[3].Multiplicity(+1)); // up-stem reaches the upper line
    }

    [Fact]
    public void DottedEighthSixteenth_LoneBeamlet()
    {
        // 8. 16 (up): the 16th's second beam is a lone left-pointing beamlet.
        var stems = new[] { S(0, 1, +1, 0), S(2, 0, +1, 2) };
        var ranks = BeamSubdivision.CalcBeaming(stems);
        var segs = BeamSubdivision.CalcBeamSegments(stems, ranks, 1.1, 0.75, 0.05);
        var beamlet = segs.Single(s => s.Rank == -1);
        Assert.True(beamlet.XRight <= 2.1, "beamlet ends at the 16th's stem");
        Assert.True(beamlet.XLeft > 0.0, "and points back toward the dotted eighth");
    }

    /// <summary>
    /// A RUN's free end is a beamlet too, not just a lone rank: 8 16 16 16 puts the 16th
    /// beam over the last three stems AND sticks it out ahead of the first of them.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:589-624 calc_beam_segments — the edge with
    ///   <c>seg.dir_ == event_dir</c> is the one furthest from its stem and takes the
    ///   beamlet length; LilyPond builds one segment per stem-SIDE and merges by rank, so a
    ///   left beamlet and the run it touches are one drawn segment.
    /// <para>
    /// ⚠️ NO FIXTURE AND NO SNAPSHOT COVERS THIS. When the run's ends were given the plain
    /// half-stem overhang instead, the whole suite stayed green — this test is the only
    /// thing that would notice, which is why it states the number rather than a shape.
    /// MEASURED on LilyPond 2.26.0 via audit/lp-regression's autobeam-tuplet-recheck, whose
    /// second beam group is exactly c8 c16 c16 c16: stems at 22.350 / 26.054 / 28.558 /
    /// 31.764, and the 16th beam drawn over 24.954..31.829 — 1.100 (beamlet-default-length)
    /// ahead of the second stem, and half a stem past the last.
    /// </para>
    /// </remarks>
    [Fact]
    public void EighthThenThreeSixteenths_TheRunsFreeEndSticksOutAhead()
    {
        // Stems 2.0 apart so the 0.75 proportion cap (1.5) cannot bind before 1.1 does.
        // ⚠️ The counts are the STEMS' OWN beam counts — a 16th carries 2 on both sides —
        // and CalcBeaming decides what actually connects. Handing it the already-clamped
        // "1" on the second stem's left is a different question and answers it differently.
        var stems = new[] { S(0, 1, +1, 0), S(2, 2, +1, 2), S(2, 2, +1, 4), S(2, 0, +1, 6) };
        var ranks = BeamSubdivision.CalcBeaming(stems);
        var segs = BeamSubdivision.CalcBeamSegments(stems, ranks, 1.1, 0.75, 0.05);

        var primary = segs.Single(s => s.Rank == 0);
        Assert.Equal(-0.05, primary.XLeft, precision: 9);   // the beam's own end: half a stem
        Assert.Equal(6.05, primary.XRight, precision: 9);

        // Up stems put the secondary BELOW the primary, toward the heads: rank −1
        // (the same sign DottedEighthSixteenth_LoneBeamlet reads).
        var sixteenth = segs.Single(s => s.Rank == -1);
        Assert.Equal(2.0 - 1.1, sixteenth.XLeft, precision: 9);  // beamlet ahead of stem 1
        Assert.Equal(6.05, sixteenth.XRight, precision: 9);      // and flush past the last
    }
}
