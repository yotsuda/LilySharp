namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for slur scoring.
/// LILYPOND-REF: lily/include/slur-score-parameters.hh:1-57 Slur_score_parameters struct
/// </summary>
public sealed record SlurScoreParameters
{
    /// <summary>Default parameters matching Lilypond defaults.</summary>
    public static SlurScoreParameters Default { get; } = new();

    /// <summary>Search region size.</summary>
    public int RegionSize { get; init; } = 5;

    /// <summary>Penalty for encompassing note heads.</summary>
    public double HeadEncompassPenalty { get; init; } = 20.0;

    /// <summary>Penalty for encompassing stems.</summary>
    public double StemEncompassPenalty { get; init; } = 10.0;

    /// <summary>Factor for edge attraction.</summary>
    public double EdgeAttractionFactor { get; init; } = 4.0;

    /// <summary>Penalty for same slope as staff.</summary>
    public double SameSlopePenalty { get; init; } = 20.0;

    /// <summary>Factor for steeper slopes.</summary>
    public double SteeperSlopeFactor { get; init; } = 10.0;

    /// <summary>Penalty for non-horizontal slurs.</summary>
    public double NonHorizontalPenalty { get; init; } = 15.0;

    /// <summary>Maximum allowed slope.</summary>
    public double MaxSlope { get; init; } = 1.1;

    /// <summary>Factor for max slope penalty.</summary>
    public double MaxSlopeFactor { get; init; } = 10.0;

    /// <summary>Penalty for collision with extra objects.</summary>
    public double ExtraObjectCollisionPenalty { get; init; } = 50.0;

    /// <summary>Penalty for accidental collision.</summary>
    public double AccidentalCollision { get; init; } = 3.0;

    /// <summary>Free distance for slur positioning.</summary>
    public double FreeSlurDistance { get; init; } = 0.8;

    /// <summary>Free distance from note heads.</summary>
    public double FreeHeadDistance { get; init; } = 0.3;

    /// <summary>Gap to staff line (inside).</summary>
    public double GapToStafflineInside { get; init; } = 0.2;

    /// <summary>Gap to staff line (outside).</summary>
    public double GapToStafflineOutside { get; init; } = 0.1;

    /// <summary>Maximum height limit (staff spaces).</summary>
    public double HeightLimit { get; init; } = 2.0;

    /// <summary>Height ratio for calculation.</summary>
    public double Ratio { get; init; } = 0.333;
}