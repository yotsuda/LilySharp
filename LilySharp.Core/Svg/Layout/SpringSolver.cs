using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Solves spring-based spacing to achieve a target width.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/simple-spacer.cc:1-300 Simple_spacer class
/// 
/// The solver finds a force that, when applied uniformly to all springs,
/// achieves the target width while respecting minimum distance constraints.
/// 
/// Algorithm (analytical solution, same as LilyPond):
/// 1. Calculate max_block_force (maximum blocking force among all springs)
/// 2. Calculate length at max_block_force
/// 3. If target > length: expand_line (simple linear calculation)
/// 4. If target &lt; length: compress_line (iterative blocking)
/// </remarks>
public sealed class SpringSolver
{
    private readonly ImmutableArray<Spring> _springs;
    
    public SpringSolver(ImmutableArray<Spring> springs)
    {
        _springs = springs;
    }
    
    // LILYPOND-REF: lily/simple-spacer.cc:159-162 Simple_spacer::configuration_length()
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
    
    // LILYPOND-REF: lily/simple-spacer.cc:165-172 Simple_spacer::range_max_block_force()
    /// <summary>
    /// Gets the maximum blocking force among all springs.
    /// </summary>
    private double MaxBlockingForce()
    {
        double result = 0.0;
        foreach (var spring in _springs)
        {
            result = Math.Max(result, spring.BlockingForce);
        }
        return result;
    }
    
    // LILYPOND-REF: lily/simple-spacer.cc:175-205 Simple_spacer::solve() + range_solve()
    /// <summary>
    /// Finds the force needed to achieve the target total width.
    /// </summary>
    /// <param name="targetWidth">The desired total width</param>
    /// <param name="ragged">If true, negative force means the line doesn't fit</param>
    /// <returns>Solution containing force and whether the line fits</returns>
    public (double Force, bool Fits) Solve(double targetWidth, bool ragged = false)
    {
        if (_springs.Length == 0)
            return (0, true);
        
        double maxBlockForce = MaxBlockingForce();
        double maxBlockForceLen = TotalLength(maxBlockForce);
        
        double force;
        bool fits;
        
        if (maxBlockForceLen < targetWidth)
        {
            // Need to expand
            (force, fits) = ExpandLine(targetWidth, maxBlockForceLen, maxBlockForce);
        }
        else if (maxBlockForceLen > targetWidth)
        {
            // Need to compress
            (force, fits) = CompressLine(targetWidth, maxBlockForceLen, maxBlockForce);
        }
        else
        {
            force = maxBlockForce;
            fits = true;
        }
        
        // LILYPOND-REF: lily/simple-spacer.cc:201-202
        if (ragged && force < 0)
            fits = false;
        
        return (force, fits);
    }
    
    /// <summary>
    /// Finds the force needed to achieve the target total width.
    /// </summary>
    /// <param name="targetWidth">The desired total width</param>
    /// <returns>The force to apply to all springs</returns>
    public double SolveForWidth(double targetWidth)
    {
        return Solve(targetWidth).Force;
    }
    
    // LILYPOND-REF: lily/simple-spacer.cc:207-225 Simple_spacer::expand_line()
    /// <summary>
    /// Calculates force when expanding the line (target > max_block_force_len).
    /// </summary>
    private (double Force, bool Fits) ExpandLine(double targetLen, double maxBlockForceLen, double maxBlockForce)
    {
        // Sum of all inverse stretch strengths
        double invHooke = 0;
        foreach (var spring in _springs)
        {
            invHooke += spring.InverseStretchStrength;
        }
        
        // Avoid division by zero - if springs are infinitely stiff, report very large force
        if (invHooke == 0.0)
            invHooke = 1e-6;
        
        // Linear calculation: force = (targetLen - currentLen) / totalFlexibility + currentForce
        double force = (targetLen - maxBlockForceLen) / invHooke + maxBlockForce;
        return (force, true);
    }
    
    // LILYPOND-REF: lily/simple-spacer.cc:233-288 Simple_spacer::compress_line()
    /// <summary>
    /// Calculates force when compressing the line (target &lt; max_block_force_len).
    /// </summary>
    private (double Force, bool Fits) CompressLine(double targetLen, double maxBlockForceLen, double maxBlockForce)
    {
        // Check whether we will actually be compressed (negative force) or just less stretched
        double neutralLength = TotalLength(0.0);
        bool compressed = (neutralLength > targetLen);
        
        double curForce = compressed ? 0.0 : maxBlockForce;
        double curLen = compressed ? neutralLength : maxBlockForceLen;
        
        // Sort springs by blocking force (descending)
        var sortedSprings = _springs.OrderByDescending(s => s.BlockingForce).ToList();
        
        // inv_hooke is the total flexibility of currently-active springs
        double invHooke = 0;
        int i = sortedSprings.Count;
        
        // Add springs that are already active (blocking_force < current_force)
        for (; i > 0 && sortedSprings[i - 1].BlockingForce < curForce; i--)
        {
            invHooke += compressed 
                ? sortedSprings[i - 1].InverseCompressStrength 
                : sortedSprings[i - 1].InverseStretchStrength;
        }
        
        // Process remaining springs in order
        for (; i < sortedSprings.Count; i++)
        {
            var sp = sortedSprings[i];
            
            if (double.IsPositiveInfinity(sp.BlockingForce))
                break;
            
            // Distance the line would shrink before this spring blocks
            double blockDist = (curForce - sp.BlockingForce) * invHooke;
            
            // Check if we reach target before this spring blocks
            if (curLen - blockDist < targetLen)
            {
                curForce += (targetLen - curLen) / invHooke;
                return (curForce, true);
            }
            
            // This spring blocks - update state
            curLen -= blockDist;
            invHooke -= compressed ? sp.InverseCompressStrength : sp.InverseStretchStrength;
            curForce = sp.BlockingForce;
        }
        
        // Couldn't fit
        return (curForce, false);
    }
    
    // LILYPOND-REF: lily/simple-spacer.cc:295-305 Simple_spacer::spring_positions()
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