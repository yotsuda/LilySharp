using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Solves spring-based spacing to achieve a target width.
/// Based on Lilypond's simple-spacer.cc.
/// </summary>
/// <remarks>
/// The solver finds a force that, when applied uniformly to all springs,
/// achieves the target width while respecting minimum distance constraints.
/// 
/// Algorithm:
/// 1. Calculate the minimum possible width (all springs at MinDistance)
/// 2. Calculate the ideal width (all springs at IdealDistance)
/// 3. Binary search for the force that achieves target width
/// </remarks>
public sealed class SpringSolver
{
    private readonly ImmutableArray<Spring> _springs;
    
    public SpringSolver(ImmutableArray<Spring> springs)
    {
        _springs = springs;
    }
    
    /// <summary>
    /// Gets the total length of all springs at the given force.
    /// </summary>
    public double TotalLength(double force)
    {
        double total = 0;
        foreach (var spring in _springs)
        {
            total += spring.Length(force);
        }
        return total;
    }
    
    /// <summary>
    /// Gets the minimum possible total length (all springs compressed to MinDistance).
    /// </summary>
    public double MinTotalLength => TotalLength(double.NegativeInfinity);
    
    /// <summary>
    /// Gets the ideal total length (all springs at IdealDistance with zero force).
    /// </summary>
    public double IdealTotalLength => TotalLength(0);
    
    /// <summary>
    /// Finds the force needed to achieve the target total width.
    /// </summary>
    /// <param name="targetWidth">The desired total width</param>
    /// <returns>The force to apply to all springs</returns>
    public double SolveForWidth(double targetWidth)
    {
        if (_springs.Length == 0)
            return 0;
        
        double minLength = MinTotalLength;
        double idealLength = IdealTotalLength;
        
        // If target is less than minimum, we can't compress further
        if (targetWidth <= minLength)
            return double.NegativeInfinity;
        
        // If target equals ideal, no force needed
        if (Math.Abs(targetWidth - idealLength) < 0.001)
            return 0;
        
        // Binary search for the correct force
        // Force range: large negative (compress) to large positive (stretch)
        double forceLow = -1000;
        double forceHigh = 1000;
        
        // Expand range if needed
        while (TotalLength(forceLow) > targetWidth && forceLow > -1e6)
            forceLow *= 2;
        while (TotalLength(forceHigh) < targetWidth && forceHigh < 1e6)
            forceHigh *= 2;
        
        // Binary search
        const int maxIterations = 50;
        const double tolerance = 0.1;
        
        for (int i = 0; i < maxIterations; i++)
        {
            double forceMid = (forceLow + forceHigh) / 2;
            double length = TotalLength(forceMid);
            
            if (Math.Abs(length - targetWidth) < tolerance)
                return forceMid;
            
            if (length < targetWidth)
                forceLow = forceMid;
            else
                forceHigh = forceMid;
        }
        
        return (forceLow + forceHigh) / 2;
    }
    
    /// <summary>
    /// Gets the positions of all items given the solved force.
    /// </summary>
    /// <param name="force">The force from SolveForWidth</param>
    /// <param name="startX">The starting X position</param>
    /// <returns>Array of X positions for each item (N+1 positions for N springs)</returns>
    public ImmutableArray<double> GetPositions(double force, double startX = 0)
    {
        var positions = new List<double> { startX };
        double currentX = startX;
        
        foreach (var spring in _springs)
        {
            currentX += spring.Length(force);
            positions.Add(currentX);
        }
        
        return positions.ToImmutableArray();
    }
}