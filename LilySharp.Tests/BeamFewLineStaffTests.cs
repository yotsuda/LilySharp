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

using LilySharp.Core.Svg;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A beam is quanted against ITS staff's radius — LilyPond's <c>staff_radius_</c>, half the
/// outer lines' span — and not against five lines whatever the staff draws. The line walk of
/// score_forbidden_quants and the inside-the-staff tests read it, so a one-line staff (radius
/// 0) and the timbales pair (radius 1.0) quant their beams elsewhere than a five-line staff.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p874/probe/beam, `b8 b d' d' | f'8 f' a' a' | g8 g e e
/// | c8 c a, a, |` under treble, the beam's centre line against the staff's middle):
/// <list type="bullet">
/// <item>one line: bar 1 (down) −3.19 → −2.50, bar 3 (up) +2.19 → +1.50;</item>
/// <item>the pair (±2): bar 1 −3.19 → −2.50, bar 3 +1.81 → +1.19;</item>
/// <item>bars 2 and 4 (−0.81 → −0.19, +0.19 → 0.00) the same on every staff.</item>
/// </list>
/// Before 2026-10-10 the notation callers never handed the quanter a line count, so every
/// staff was five lines to it: bar 1 came out −2.81 → −2.19 and bar 3 +1.81 → +1.19 on one
/// line. And the radius of a reduced staff is the DRAWN lines' span over four — the pair at
/// ±2 is 1.0 — where (N − 1)/2, LilyPond's own N-line default, would make it 0.5.
/// LILYPOND-REF: lily/beam-quanting.cc:335 staff_radius_, :1248, :1300, :1338, :1352;
///   lily/staff-symbol-referencer.cc:189-196 staff_radius.
/// </remarks>
[Trait("Category", "Unit")]
public class BeamFewLineStaffTests
{
    private static RenderedGeometry Render(int lines) => RenderedGeometry.RenderProduct($$"""
        octave absolute
        time 4/4
        part p { }
        section A { p { b8 b d' d' | f'8 f' a' a' | g8 g e e | c8 c a, a, | } }
        form { A }
        score { staff p as lines {{lines}} }
        """);

    [Theory]
    [InlineData(1, 0, -3.19, -2.50)]
    [InlineData(1, 2, 2.19, 1.50)]
    [InlineData(2, 0, -3.19, -2.50)]
    [InlineData(2, 2, 1.81, 1.19)]
    public void ABeam_IsQuantedOnItsOwnStaffsRadius(int lines, int beam, double left, double right)
    {
        var g = Render(lines);
        // Read against the staff's TOP drawn line (the pair's middle is not a line), then moved
        // to the middle: the top line stands its position over two above it.
        double topAboveMiddle = EngravingDefaults.StaffLinePositions(lines)[^1] / 2.0;
        Assert.Equal(left, g.BeamPositionAboveStaffLine(beam, rightEnd: false, 0) + topAboveMiddle, 2);
        Assert.Equal(right, g.BeamPositionAboveStaffLine(beam, rightEnd: true, 0) + topAboveMiddle, 2);
    }

    [Theory]
    [InlineData(5, 2.0)]
    [InlineData(4, 1.5)]
    [InlineData(3, 1.0)]
    [InlineData(2, 1.0)]
    [InlineData(1, 0.0)]
    public void TheRadius_IsTheDrawnLinesSpanOverFour(int lines, double radius)
        => Assert.Equal(radius, EngravingDefaults.NotationStaffRadius(lines), 6);
}
