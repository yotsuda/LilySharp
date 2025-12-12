namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Default engraving parameters based on Lilypond defaults.
/// All values are in staff spaces unless otherwise noted.
/// See docs/COORDINATE_SYSTEM.md for unit conversion guidelines.
/// </summary>
public static class EngravingDefaults
{
    // ========================================
    // Beam parameters (from Lilypond beam.cc)
    // ========================================
    
    /// <summary>Beam thickness in staff spaces.</summary>
    public const double BeamThickness = 0.48;
    
    /// <summary>Staff line thickness in staff spaces (approximate).</summary>
    public const double LineThickness = 0.1;
    
    /// <summary>
    /// Distance between beam centers for multiple beams.
    /// Formula: (2 * staff_space + line - beam_thickness) / 2
    /// </summary>
    public const double BeamTranslation = (2.0 + LineThickness - BeamThickness) / 2.0;
    
    // ========================================
    // Stem parameters
    // ========================================
    
    /// <summary>Ideal stem length in staff spaces.</summary>
    public const double IdealStemLength = 3.5;
    
    /// <summary>Minimum stem length in staff spaces.</summary>
    public const double MinStemLength = 2.5;
    
    /// <summary>Default stem length for non-beamed notes in staff spaces.</summary>
    public const double DefaultStemLength = 3.5;
    
    // ========================================
    // Beamlet parameters
    // ========================================
    
    /// <summary>Length of a beamlet (partial beam) in staff spaces.</summary>
    public const double BeamletLength = 1.0;
    
    // ========================================
    // Conversion helpers
    // ========================================
    
    /// <summary>
    /// Converts staff spaces to staff positions.
    /// 1 staff space = 2 staff positions.
    /// </summary>
    public static double ToStaffPositions(double staffSpaces) => staffSpaces * 2;
    
    /// <summary>
    /// Converts staff positions to staff spaces.
    /// 1 staff position = 0.5 staff spaces.
    /// </summary>
    public static double ToStaffSpaces(double staffPositions) => staffPositions / 2;
}