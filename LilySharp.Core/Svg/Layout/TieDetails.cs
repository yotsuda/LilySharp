namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for tie formatting.
/// Based on Lilypond's Tie_details from tie-details.hh
/// </summary>
public sealed record TieDetails
{
    /// <summary>Default parameters matching Lilypond defaults.</summary>
    public static TieDetails Default { get; } = new();
    
    /// <summary>Maximum height of tie arc (staff spaces).</summary>
    public double HeightLimit { get; init; } = 1.0;
    
    /// <summary>Ratio for determining tie height based on length.</summary>
    public double Ratio { get; init; } = 0.333;
    
    /// <summary>Gap between tie endpoint and notehead (staff spaces).</summary>
    public double XGap { get; init; } = 0.2;
    
    /// <summary>Gap between tie and stem (staff spaces).</summary>
    public double StemGap { get; init; } = 0.3;
    
    /// <summary>Minimum tie length (staff spaces).</summary>
    public double MinLength { get; init; } = 1.0;
    
    /// <summary>Clearance from staff lines at tip (staff spaces).</summary>
    public double TipStaffLineClearance { get; init; } = 0.15;
    
    /// <summary>Clearance from staff lines at center (staff spaces).</summary>
    public double CenterStaffLineClearance { get; init; } = 0.35;
    
    /// <summary>Penalty for collision with staff line.</summary>
    public double StaffLineCollisionPenalty { get; init; } = 10.0;
    
    /// <summary>Clearance from dots (staff spaces).</summary>
    public double DotCollisionClearance { get; init; } = 0.3;
    
    /// <summary>Penalty for collision with dots.</summary>
    public double DotCollisionPenalty { get; init; } = 20.0;
    
    /// <summary>Penalty for tie going in wrong direction.</summary>
    public double WrongDirectionOffsetPenalty { get; init; } = 10.0;
    
    /// <summary>Penalty for tie direction same as stem.</summary>
    public double SameDirAsStemPenalty { get; init; } = 8.0;
    
    /// <summary>Factor for min length penalty.</summary>
    public double MinLengthPenaltyFactor { get; init; } = 2.0;
    
    /// <summary>Padding for skyline collision detection.</summary>
    public double SkylinePadding { get; init; } = 0.05;
    
    /// <summary>Penalty for tie-tie collision.</summary>
    public double TieTieCollisionPenalty { get; init; } = 30.0;
    
    /// <summary>Distance threshold for tie-tie collision.</summary>
    public double TieTieCollisionDistance { get; init; } = 0.5;
    
    /// <summary>Penalty factor for horizontal distance.</summary>
    public double HorizontalDistancePenaltyFactor { get; init; } = 5.0;
    
    /// <summary>Penalty factor for vertical distance.</summary>
    public double VerticalDistancePenaltyFactor { get; init; } = 5.0;
    
    /// <summary>Threshold for intra-space positioning.</summary>
    public double IntraSpaceThreshold { get; init; } = 1.0;
    
    /// <summary>Search region size for single ties.</summary>
    public int SingleTieRegionSize { get; init; } = 3;
    
    /// <summary>Search region size for multiple ties.</summary>
    public int MultiTieRegionSize { get; init; } = 1;
}