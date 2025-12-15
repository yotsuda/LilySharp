namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for beam quantization scoring.
/// LILYPOND-REF: lily/include/beam-scoring-problem.hh:1-203 Beam_quant_parameters struct
/// </summary>
public sealed record BeamQuantParameters
{
    /// <summary>Default parameters matching Lilypond defaults.</summary>
    public static BeamQuantParameters Default { get; } = new();
    
    // General
    /// <summary>Threshold to combat rounding errors.</summary>
    public double BeamEps { get; init; } = 1e-3;
    
    /// <summary>Region size for quant search.</summary>
    public double RegionSize { get; init; } = 2.0;
    
    // Forbidden quants
    /// <summary>Demerit for secondary beam on staff line.</summary>
    public double SecondaryBeamDemerit { get; init; } = 10.0;
    
    /// <summary>Demerit factor for stem length deviation.</summary>
    public double StemLengthDemeritFactor { get; init; } = 5.0;
    
    /// <summary>Penalty for stems that are too short.</summary>
    public double StemLengthLimitPenalty { get; init; } = 5000.0;
    
    // Slope penalties
    /// <summary>Penalty when beam direction differs from damping direction.</summary>
    public double DampingDirectionPenalty { get; init; } = 800.0;
    
    /// <summary>Factor for musical direction scoring.</summary>
    public double MusicalDirectionFactor { get; init; } = 400.0;
    
    /// <summary>Penalty for hint direction mismatch.</summary>
    public double HintDirectionPenalty { get; init; } = 20.0;
    
    /// <summary>Factor for ideal slope deviation.</summary>
    public double IdealSlopeFactor { get; init; } = 10.0;
    
    /// <summary>Slope threshold considered as zero.</summary>
    public double RoundToZeroSlope { get; init; } = 0.02;
    
    // Damping
    /// <summary>
    /// Slope damping factor. Higher values result in flatter beams.
    /// LILYPOND-REF: lily/beam-quanting.cc:754-755
    /// </summary>
    public double Damping { get; init; } = 1.0;
    
    // Collision penalties
    /// <summary>Penalty for collision with other objects.</summary>
    public double CollisionPenalty { get; init; } = 500.0;
    
    /// <summary>Padding for collision detection.</summary>
    public double CollisionPadding { get; init; } = 0.5;
    
    /// <summary>Penalty for horizontal inter-quant positioning.</summary>
    public double HorizontalInterQuantPenalty { get; init; } = 500.0;
    
    /// <summary>Factor for stem collision scoring.</summary>
    public double StemCollisionFactor { get; init; } = 1.0;
}