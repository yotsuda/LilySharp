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
public sealed class BeamConfiguration
{
    /// <summary>Y position at left end (in staff spaces).</summary>
    public double LeftY { get; set; }

    /// <summary>Y position at right end (in staff spaces).</summary>
    public double RightY { get; set; }

    /// <summary>Total demerit score (lower is better).</summary>
    public double Demerits { get; set; }

    /// <summary>Score breakdown for debugging.</summary>
    public string ScoreCard { get; set; } = string.Empty;

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

    /// <summary>Adds a demerit with reason for debugging.</summary>
    public void AddDemerit(double demerit, string reason)
    {
        Demerits += demerit;
        if (!string.IsNullOrEmpty(ScoreCard))
            ScoreCard += "; ";
        ScoreCard += $"{reason}: {demerit:F2}";
    }

    /// <summary>Creates a new configuration with offset.</summary>
    public static BeamConfiguration CreateWithOffset(double leftY, double rightY, double leftOffset, double rightOffset)
    {
        return new BeamConfiguration(leftY + leftOffset, rightY + rightOffset);
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
public enum BeamScorer
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
/// Based on Lilypond's Beam_collision.
/// </summary>
public readonly record struct BeamCollision(
    /// <summary>X position relative to beam start (in staff spaces).</summary>
    double X,
    /// <summary>Y range of the collision object (in staff positions, minY to maxY).</summary>
    double MinY,
    double MaxY,
    /// <summary>Base penalty factor for this collision.</summary>
    double BasePenalty = 1.0
);