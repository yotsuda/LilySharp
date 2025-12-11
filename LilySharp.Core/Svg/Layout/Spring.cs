namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A spring connecting two adjacent items in a measure.
/// Based on Lilypond's spacing model (spring.cc).
/// </summary>
/// <remarks>
/// The spring length formula:
///   length = max(IdealDistance + Force / Stiffness, MinDistance)
/// 
/// Where:
/// - IdealDistance: The preferred distance based on duration (time-proportional)
/// - MinDistance: The minimum distance to avoid visual collision
/// - Stiffness: How resistant the spring is to stretching (shorter notes = stiffer)
/// - Force: Applied uniformly to all springs to achieve target width
/// </remarks>
public sealed record Spring
{
    /// <summary>
    /// The ideal distance between the reference points of two adjacent items.
    /// Calculated from duration using logarithmic scaling (Gourlay algorithm).
    /// </summary>
    public double IdealDistance { get; init; }
    
    /// <summary>
    /// The minimum distance to avoid visual collision.
    /// Calculated as: PreviousItem.RightExtent + NextItem.LeftExtent + MinGap
    /// </summary>
    public double MinDistance { get; init; }
    
    /// <summary>
    /// The stiffness of the spring. Higher values mean less stretching.
    /// Calculated as inverse of duration: shorter notes are stiffer.
    /// </summary>
    public double Stiffness { get; init; }
    
    /// <summary>
    /// The force at which the spring transitions from compressed to stretched.
    /// Below this force, the spring is at MinDistance.
    /// </summary>
    public double BlockingForce { get; }
    
    public Spring(double idealDistance, double minDistance, double stiffness)
    {
        IdealDistance = idealDistance;
        MinDistance = minDistance;
        Stiffness = stiffness;
        
        // Calculate blocking force (from Lilypond spring.cc)
        // This is the force at which length equals MinDistance
        if (MinDistance > IdealDistance && Stiffness > 0)
        {
            // Need positive force to reach MinDistance
            BlockingForce = (MinDistance - IdealDistance) * Stiffness;
        }
        else if (Stiffness > 0)
        {
            // Need negative force (compression) to reach MinDistance
            BlockingForce = (MinDistance - IdealDistance) * Stiffness;
        }
        else
        {
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
        if (Stiffness <= 0)
            return Math.Max(IdealDistance, MinDistance);
        
        double length = IdealDistance + force / Stiffness;
        return Math.Max(length, MinDistance);
    }
}
