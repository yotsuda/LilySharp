namespace LilySharp.Core.Svg;

/// <summary>
/// Default metrics for music engraving.
/// All values are in staff spaces (multiply by staff space height for pixels).
/// </summary>
public static class EngravingDefaults
{
    // === Staff and lines ===
    public const double StaffLineThickness = 0.13;
    public const double LegerLineThickness = 0.16;
    public const double LegerLineExtension = 0.4;
    public const double LineThickness = 0.1;
    
    // === Stems ===
    public const double StemThickness = 0.12;
    public const double IdealStemLength = 3.5;
    public const double MinStemLength = 2.5;
    public const double DefaultStemLength = 3.5;
    
    // === Beams ===
    public const double BeamThickness = 0.48;
    public const double BeamSpacing = 0.25;
    /// <summary>Distance between beam centers for multiple beams.</summary>
    public const double BeamTranslation = (2.0 + LineThickness - BeamThickness) / 2.0;
    /// <summary>Length of a beamlet (partial beam).</summary>
    public const double BeamletLength = 1.0;
    
    // === Barlines ===
    public const double ThinBarlineThickness = 0.16;
    public const double ThickBarlineThickness = 0.5;
    public const double BarlineSeparation = 0.4;
    public const double RepeatBarlineDotSeparation = 0.16;
    
    // === Slurs and ties ===
    public const double SlurEndpointThickness = 0.1;
    public const double SlurMidpointThickness = 0.22;
    public const double TieEndpointThickness = 0.1;
    public const double TieMidpointThickness = 0.22;
    
    // === Other elements ===
    public const double HairpinThickness = 0.16;
    public const double TupletBracketThickness = 0.16;
    public const double BracketThickness = 0.5;
    
    // === Notehead dimensions ===
    public const double NoteheadWholeWidth = 1.688;
    public const double NoteheadHalfWidth = 1.18;
    public const double NoteheadBlackWidth = 1.18;
    public const double NoteheadDoubleWholeWidth = 2.296;
    
    // === Stem attachment points ===
    public const double StemUpAttachX = 1.18;
    public const double StemUpAttachY = 0.168;
    public const double StemDownAttachX = 0.0;
    public const double StemDownAttachY = -0.168;
    
    // === Conversion helpers ===
    
    /// <summary>Converts staff spaces to staff positions (1 space = 2 positions).</summary>
    public static double ToStaffPositions(double staffSpaces) => staffSpaces * 2;
    
    /// <summary>Converts staff positions to staff spaces (1 position = 0.5 spaces).</summary>
    public static double ToStaffSpaces(double staffPositions) => staffPositions / 2;
}