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
/// A spring connecting two adjacent items in a measure.
/// LILYPOND-REF: lily/spring.cc:1-250 Spring class
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spring.cc:220-240 Spring::length()
///   length = max(ideal_distance + force * inverse_stretch_strength, min_distance)
///
/// Where:
/// - IdealDistance: The preferred distance based on duration
/// - MinDistance: The minimum distance (rod constraint)
/// - InverseStretchStrength: Controls how much the spring stretches per unit force
/// - Force: Applied uniformly to all springs to achieve target width
/// </remarks>
public sealed record Spring
{
    /// <summary>
    /// The ideal distance between reference points.
    /// Calculated from duration using logarithmic scaling (Gourlay algorithm).
    /// </summary>
    public double IdealDistance { get; init; }

    /// <summary>
    /// The minimum distance (rod constraint).
    /// Calculated from collision avoidance or canonical notehead width.
    /// </summary>
    public double MinDistance { get; init; }

    /// <summary>
    /// The inverse stretch strength (flexibility).
    /// Higher values mean more stretching per unit force.
    /// Lilypond default: ideal - min (at least 0.1).
    /// </summary>
    public double InverseStretchStrength { get; init; }

    /// <summary>
    /// The inverse compress strength.
    /// Used when force is negative (compression).
    /// Lilypond default: ideal - min (at least 0).
    /// </summary>
    public double InverseCompressStrength { get; init; }

    /// <summary>
    /// The force at which the spring transitions from compressed to stretched.
    /// Below this force, the spring is at MinDistance.
    /// </summary>
    public double BlockingForce { get; }

    public Spring(double idealDistance, double minDistance, double inverseStretchStrength)
    {
        IdealDistance = idealDistance;
        MinDistance = minDistance;
        InverseStretchStrength = inverseStretchStrength;

        // Lilypond default: inverse_compress_strength = ideal - min (at least 0)
        InverseCompressStrength = Math.Max(0, idealDistance - minDistance);

        // Calculate blocking force (from Lilypond spring.cc update_blocking_force)
        if (minDistance > idealDistance)
        {
            if (inverseStretchStrength > 0)
                BlockingForce = (minDistance - idealDistance) / inverseStretchStrength;
            else
                BlockingForce = 0;
        }
        else
        {
            if (InverseCompressStrength > 0)
                BlockingForce = (minDistance - idealDistance) / InverseCompressStrength;
            else
                BlockingForce = 0;
        }
    }

    /// <summary>
    /// Full constructor with explicit compress strength.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spring.cc:29-35</remarks>
    public Spring(double idealDistance, double minDistance,
                  double inverseStretchStrength, double inverseCompressStrength)
    {
        IdealDistance = idealDistance;
        MinDistance = minDistance;
        InverseStretchStrength = inverseStretchStrength;
        InverseCompressStrength = inverseCompressStrength;

        if (minDistance > idealDistance)
        {
            BlockingForce = inverseStretchStrength > 0
                ? (minDistance - idealDistance) / inverseStretchStrength
                : 0;
        }
        else
        {
            BlockingForce = inverseCompressStrength > 0
                ? (minDistance - idealDistance) / inverseCompressStrength
                : 0;
        }
    }

    /// <summary>
    /// Merges two springs by averaging their properties.
    /// Used when multiple spacing wishes exist for the same column pair.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:105-131 Spring::merge()
    /// - Ideal distances and stretch strengths are averaged
    /// - Compress strengths use harmonic mean (1/avg(1/k))
    /// - Headroom: avg_distance = max(min_distance + 0.3, avg_distance)
    /// </remarks>
    public static Spring Merge(Spring a, Spring b)
    {
        double avgIdeal = (a.IdealDistance + b.IdealDistance) / 2;
        double maxMin = Math.Max(a.MinDistance, b.MinDistance);

        // Headroom: ensure some stretch room above min_distance
        // LILYPOND-REF: spring.cc:122
        avgIdeal = Math.Max(maxMin + 0.3, avgIdeal);

        // Average stretch strength
        double avgStretch = (a.InverseStretchStrength + b.InverseStretchStrength) / 2;

        // Harmonic mean of compress strength
        // LILYPOND-REF: spring.cc:126-128
        double avgCompress;
        if (a.InverseCompressStrength > 0 && b.InverseCompressStrength > 0)
        {
            avgCompress = 2.0 / (1.0 / a.InverseCompressStrength + 1.0 / b.InverseCompressStrength);
        }
        else
        {
            avgCompress = Math.Max(0, avgIdeal - maxMin);
        }

        return new Spring(avgIdeal, maxMin, avgStretch, avgCompress);
    }

    /// <summary>
    /// Scales the spring by a factor (e.g., for grace notes).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:88-95 Spring::operator*=()
    /// </remarks>
    public Spring Scale(double factor)
    {
        double newIdeal = Math.Max(MinDistance, IdealDistance * factor);
        double newCompress = Math.Max(0, newIdeal - MinDistance);
        double newStretch = InverseStretchStrength * factor;
        return new Spring(newIdeal, MinDistance, newStretch, newCompress);
    }

    /// <summary>
    /// Calculates the spring length for a given force.
    /// </summary>
    /// <param name="force">The force applied (positive = stretch, negative = compress)</param>
    /// <returns>The resulting length, never less than MinDistance</returns>
    public double Length(double force)
    {
        // LILYPOND-REF: lily/spring.cc:220-240 Spring::length()
        double effectiveForce = Math.Max(force, BlockingForce);
        double invK = effectiveForce < 0 ? InverseCompressStrength : InverseStretchStrength;

        // LILYPOND-REF: lily/spring.cc:228-234 - handle +Inf case
        // +Inf can happen; -Inf is impossible as BlockingForce is finite
        if (double.IsPositiveInfinity(effectiveForce))
        {
            effectiveForce = 0.0;
        }

        // Corner case: if min_distance > ideal_distance but spring is fixed (inv_k = 0),
        // we must return min_distance
        return Math.Max(MinDistance, IdealDistance + effectiveForce * invK);
    }
}