namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Represents a beam configuration candidate with position and score.
/// Based on Lilypond's Beam_configuration from beam-scoring-problem.hh
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