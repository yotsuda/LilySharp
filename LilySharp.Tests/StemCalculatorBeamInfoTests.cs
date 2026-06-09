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
/// Per-stem beamed stem length must grow with beam count (16th/32nd beams need
/// longer stems so the extra beams clear the notehead) — LilyPond's Stem_info,
/// driven by the beamed-lengths table. BeamScoringProblem now consumes this
/// instead of a flat constant.
/// </summary>
/// <remarks>LILYPOND-REF: lily/stem.cc:1137 calc_stem_info.</remarks>
public sealed class StemCalculatorBeamInfoTests
{
    [Fact]
    public void IdealStemLength_GrowsWithBeamCount()
    {
        // Head on the middle line, stem up: IdealY is the stem length itself.
        var oneBeam = StemCalculator.CalculateBeamedStemInfo(0, stemUp: true, beamCount: 1);
        var twoBeams = StemCalculator.CalculateBeamedStemInfo(0, stemUp: true, beamCount: 2);
        var threeBeams = StemCalculator.CalculateBeamedStemInfo(0, stemUp: true, beamCount: 3);

        Assert.True(oneBeam.IdealY < twoBeams.IdealY,
            $"16th beam stem ({twoBeams.IdealY:F3}) should exceed 8th ({oneBeam.IdealY:F3})");
        Assert.True(twoBeams.IdealY < threeBeams.IdealY,
            $"32nd beam stem ({threeBeams.IdealY:F3}) should exceed 16th ({twoBeams.IdealY:F3})");
    }

    [Fact]
    public void StemDirection_IsSignedByDir()
    {
        // Up stems extend to positive Y (higher), down stems to negative.
        var up = StemCalculator.CalculateBeamedStemInfo(0, stemUp: true, beamCount: 1);
        var down = StemCalculator.CalculateBeamedStemInfo(0, stemUp: false, beamCount: 1);
        Assert.True(up.IdealY > 0);
        Assert.True(down.IdealY < 0);
    }
}
