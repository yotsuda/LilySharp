namespace LilySharp.Core.Svg;

/// <summary>
/// SMuFL engraving defaults from Bravura font metadata.
/// All values are in staff spaces (multiply by SpaceHeight for pixels).
/// Reference: https://w3c.github.io/smufl/latest/specification/engravingdefaults.html
/// </summary>
public static class SmuflDefaults
{
    // Staff and lines
    public const double StaffLineThickness = 0.13;
    public const double LegerLineThickness = 0.16;
    public const double LegerLineExtension = 0.4;
    
    // Stems and beams
    public const double StemThickness = 0.12;
    public const double BeamThickness = 0.5;
    public const double BeamSpacing = 0.25;
    
    // Barlines
    public const double ThinBarlineThickness = 0.16;
    public const double ThickBarlineThickness = 0.5;
    public const double BarlineSeparation = 0.4;
    public const double RepeatBarlineDotSeparation = 0.16;
    
    // Slurs and ties
    public const double SlurEndpointThickness = 0.1;
    public const double SlurMidpointThickness = 0.22;
    public const double TieEndpointThickness = 0.1;
    public const double TieMidpointThickness = 0.22;
    
    // Other elements
    public const double HairpinThickness = 0.16;
    public const double TupletBracketThickness = 0.16;
    public const double BracketThickness = 0.5;
    
    // Notehead widths (from glyphAdvanceWidths)
    public const double NoteheadWholeWidth = 1.688;
    public const double NoteheadHalfWidth = 1.18;
    public const double NoteheadBlackWidth = 1.18;
    public const double NoteheadDoubleWholeWidth = 2.296;
    
    // Stem attachment points (from glyphsWithAnchors)
    // stemUpSE: right side of notehead for stem-up
    public const double StemUpAttachX = 1.18;
    public const double StemUpAttachY = 0.168;
    // stemDownNW: left side of notehead for stem-down
    public const double StemDownAttachX = 0.0;
    public const double StemDownAttachY = -0.168;
}

/// <summary>
/// Music engraving rules based on "Behind Bars" by Elaine Gould.
/// </summary>
public static class EngravingRules
{
    // Stem lengths (in staff spaces)
    // Standard stem length is 3.5 staff spaces (one octave)
    public const double StandardStemLength = 3.5;
    // Minimum stem length is 2.5 staff spaces
    public const double MinimumStemLength = 2.5;
    
    // =======================================================
    // Note Spacing - Based on LilyPond's Gourlay algorithm
    // Reference: John S. Gourlay, "Spacing a Line of Music"
    // OSU-CISRC-10/87-TR35, 1987
    // =======================================================
    
    // Base spacing constant (in staff spaces)
    private const double ShortestDurationSpace = 2.0;
    
    // Spacing increment (approximately notehead width, in staff spaces)
    private const double SpacingIncrement = 1.2;
    
    // Reference duration: 8th note (noteValue = 8)
    private const int ReferenceDuration = 8;
    
    /// <summary>
    /// Calculate note spacing using logarithmic spacing algorithm.
    /// Based on LilyPond's implementation of Gourlay's algorithm.
    /// </summary>
    /// <param name="noteValue">Note value (1=whole, 2=half, 4=quarter, 8=eighth, etc.)</param>
    /// <returns>Spacing in staff spaces</returns>
    public static double GetNoteSpacing(int noteValue)
    {
        // ratio = reference / noteValue (because larger noteValue = shorter duration)
        // e.g., quarter (4) vs eighth (8): ratio = 8/4 = 2
        double ratio = (double)ReferenceDuration / noteValue;
        
        if (ratio < 1.0)
        {
            // Short notes (faster than 8th): linear spacing
            // Prevents disproportionate stretching
            return (ShortestDurationSpace + ratio - 1) * SpacingIncrement;
        }
        else
        {
            // Longer notes: logarithmic spacing
            // space = (base + log2(ratio)) * increment
            return (ShortestDurationSpace + Math.Log2(ratio)) * SpacingIncrement;
        }
    }
    
    // Minimum spacing between notes (in staff spaces)
    public const double MinimumNoteSpacing = 1.5;
    
    // =======================================================
    // Break-aligned spacing - Based on LilyPond's space-alist
    // These values are for spacing to the first note
    // =======================================================
    
    // Space after barline to first note (semi-shrink-space)
    public const double SpaceAfterBarline = 1.3;
    
    // Space after clef to next element
    // Clef → key-signature: 0.82, Clef → first-note: 5.0
    public const double SpaceAfterClef = 1.0;
    
    // Space after time signature to first note (semi-shrink-space)
    public const double SpaceAfterTimeSignature = 2.0;
    
    // Space after key signature to first note (shrink-space)
    public const double SpaceAfterKeySignature = 2.5;
}