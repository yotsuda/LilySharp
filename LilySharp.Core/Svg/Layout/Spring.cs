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