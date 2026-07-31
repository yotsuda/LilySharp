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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for beam quantization scoring.
/// LILYPOND-REF: lily/include/beam-scoring-problem.hh:1-203 Beam_quant_parameters struct
/// </summary>
public sealed record BeamQuantParameters
{
    /// <summary>Default parameters matching Lilypond defaults.</summary>
    public static BeamQuantParameters Default { get; } = new();

    // General
    /// <summary>Threshold to combat rounding errors.</summary>
    public double BeamEps { get; init; } = 1e-3;

    /// <summary>Region size for quant search.</summary>
    public double RegionSize { get; init; } = 2.0;

    // Forbidden quants
    /// <summary>Demerit for secondary beam on staff line.</summary>
    public double SecondaryBeamDemerit { get; init; } = 10.0;

    /// <summary>Demerit factor for stem length deviation.</summary>
    public double StemLengthDemeritFactor { get; init; } = 5.0;

    /// <summary>Penalty for stems that are too short.</summary>
    public double StemLengthLimitPenalty { get; init; } = 5000.0;

    // Slope penalties
    /// <summary>Penalty when beam direction differs from damping direction.</summary>
    public double DampingDirectionPenalty { get; init; } = 800.0;

    /// <summary>Factor for musical direction scoring.</summary>
    public double MusicalDirectionFactor { get; init; } = 400.0;

    /// <summary>Penalty for hint direction mismatch.</summary>
    public double HintDirectionPenalty { get; init; } = 20.0;

    /// <summary>Factor for ideal slope deviation.</summary>
    public double IdealSlopeFactor { get; init; } = 10.0;

    /// <summary>Slope threshold considered as zero.</summary>
    public double RoundToZeroSlope { get; init; } = 0.02;

    // Damping
    /// <summary>
    /// Slope damping factor. Higher values result in flatter beams.
    /// LILYPOND-REF: lily/beam-quanting.cc:754-755
    /// </summary>
    public double Damping { get; init; } = 1.0;

    // Collision penalties
    /// <summary>Penalty for collision with other objects.</summary>
    public double CollisionPenalty { get; init; } = 500.0;

    /// <summary>Padding for collision detection (staff spaces).</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:508 Beam details — the (collision-padding . 0.35)
    ///   that get_detail actually finds, and so the value that is used.
    /// LILYPOND-REF: lily/beam-quanting.cc:115-117 get_detail (details, collision-padding,
    ///   0.5) — scaled by length-fraction² for grace beams (which Lily# does not have yet).
    /// ⚠️ THE 0.5 THERE IS THE FALLBACK FOR A GROB THAT DOES NOT DECLARE THE DETAIL, and
    /// Beam always declares it — so 0.5 is a number LilyPond never uses. Reading it was
    /// worth a whole quant step: with 0.35 a beam clears a covered head at 4.19 and with
    /// 0.5 it pays 4.80 demerits there and buys the next quant up instead. MEASURED
    /// (audit/lp-geometry/probes/beam-over-other-voice.ly through
    /// <c>\override Beam.inspect-quants</c>, which puts the winning score card in
    /// Beam.annotation): LilyPond's cards are C 700.36 at 2.81, C 560.75 at 3.81,
    /// C 40.19 at 4.00 and NO C term at 4.19 — the three numbers solve for a padding of
    /// exactly 0.350 and for nothing else.
    /// </remarks>
    public double CollisionPadding { get; init; } = 0.35;

    /// <summary>Penalty for horizontal inter-quant positioning.</summary>
    public double HorizontalInterQuantPenalty { get; init; } = 500.0;

}