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
/// Represents a beam configuration candidate with position and score.
/// LILYPOND-REF: lily/include/beam-scoring-problem.hh:1-203 Beam_configuration struct
/// </summary>
internal sealed class BeamConfiguration : IScorableConfig
{
    /// <summary>Y position at left end, in staff-spaces (LilyPond's quanting frame).</summary>
    public double LeftY { get; set; }

    /// <summary>Y position at right end, in staff-spaces (LilyPond's quanting frame).</summary>
    public double RightY { get; set; }

    /// <summary>Total demerit score (lower is better).</summary>
    public double Demerits { get; set; }

    // (LP's score_card_ string is compiled out except under DEBUG_BEAM_SCORING
    // — beam-scoring-problem.hh. The port accumulated it unconditionally, one
    // string allocation per demerit per candidate on the quanting hot path,
    // with zero readers anywhere in the repo. Removed 2026-08-26; AddDemerit
    // keeps its reason parameter so the call sites still name their demerits
    // and a debug build can trivially re-grow the card.)

    /// <summary>Next scorer to evaluate.</summary>
    internal int NextScorerTodo { get; set; }

    /// <summary>Creates a new beam configuration.</summary>
    public BeamConfiguration(double leftY, double rightY)
    {
        LeftY = leftY;
        RightY = rightY;
        Demerits = 0.0;
        NextScorerTodo = 0;
    }

    /// <summary>Checks if all scorers have been applied.</summary>
    public bool IsDone => NextScorerTodo >= (int)BeamScorer.NumScorers;

    /// <summary>Adds a demerit; <paramref name="reason"/> names it at the call site
    /// (see the score-card note above).</summary>
    public void AddDemerit(double demerit, string reason)
    {
        Demerits += demerit;
    }

    /// <summary>Gets the slope of this beam configuration.</summary>
    public double GetSlope(double xSpan)
    {
        if (xSpan < 1e-6)
            return 0.0;
        return (RightY - LeftY) / xSpan;
    }

    /// <summary>Gets Y position at a given X coordinate.</summary>
    public double GetYAt(double x, double leftX, double xSpan)
    {
        if (xSpan < 1e-6)
            return LeftY;
        return LeftY + (x - leftX) * (RightY - LeftY) / xSpan;
    }
}

/// <summary>
/// Scorer types for beam quantization.
/// Ordered by increasing expensiveness.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam-quanting.cc:624-651 — scorer ordering
/// NOTE: LilyPond evaluates scorers in a different order than listed here.
/// LP order: STEM_LENGTHS, ORIGINAL, SLOPE_IDEAL, SLOPE_MUSICAL, SLOPE_DIRECTION,
///           HORIZONTAL_INTER, FORBIDDEN, COLLISIONS
/// IMPLEMENTED — forbidden quants constants (FIXED_DEMERIT=0.39, FUDGE=2.2) in ScoreForbiddenQuants
/// IMPLEMENTED — cross-staff 10× penalty multiplier
/// </remarks>
internal enum BeamScorer
{
    OriginalDistance,
    SlopeIdeal,
    SlopeMusical,
    SlopeDirection,
    HorizontalInter,
    Forbidden,
    StemLengths,
    Collisions,
    NumScorers
}

/// <summary>
/// Represents a potential collision object for beam scoring.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/beam-scoring-problem.hh:101-109 Beam_collision struct
/// </remarks>
public readonly record struct BeamCollision(
    // X position relative to the beam's LEFT STEM (in staff spaces).
    double X,
    // Y range of the collision object (in STAFF SPACES, minY to maxY) — the frame
    // the whole quanter speaks. LilyPond books a covered grob's y extent in the same
    // unit: add_collision divides by staff_space_ (beam-quanting.cc:205) before storing.
    double MinY,
    double MaxY,
    // Base penalty factor for this collision.
    double BasePenalty = 1.0
);