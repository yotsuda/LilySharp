namespace LilySharp.Core.Svg;

/// <summary>
/// Music engraving rules based on standard practices.
/// Reference: "Behind Bars" by Elaine Gould.
/// </summary>
public static class EngravingRules
{
    // === Stem lengths (in staff spaces) ===
    
    /// <summary>Standard stem length is 3.5 staff spaces (one octave).</summary>
    public const double StandardStemLength = 3.5;
    
    /// <summary>Minimum stem length is 2.5 staff spaces.</summary>
    public const double MinimumStemLength = 2.5;
    
    // === Note Spacing ===
    // Based on LilyPond's Gourlay algorithm
    // Reference: John S. Gourlay, "Spacing a Line of Music"
    // OSU-CISRC-10/87-TR35, 1987
    
    private const double ShortestDurationSpace = 2.0;
    private const double SpacingIncrement = 1.2;
    private const int ReferenceDuration = 8;
    
    /// <summary>
    /// Calculate note spacing using logarithmic spacing algorithm.
    /// </summary>
    /// <param name="noteValue">Note value (1=whole, 2=half, 4=quarter, 8=eighth, etc.)</param>
    /// <returns>Spacing in staff spaces</returns>
    public static double GetNoteSpacing(int noteValue)
    {
        double ratio = (double)ReferenceDuration / noteValue;
        
        if (ratio < 1.0)
        {
            // Short notes: linear spacing
            return (ShortestDurationSpace + ratio - 1) * SpacingIncrement;
        }
        else
        {
            // Longer notes: logarithmic spacing
            return (ShortestDurationSpace + Math.Log2(ratio)) * SpacingIncrement;
        }
    }
    
    /// <summary>Minimum spacing between notes (in staff spaces).</summary>
    public const double MinimumNoteSpacing = 1.5;
    
    // === Break-aligned spacing ===
    // Based on LilyPond's space-alist
    
    /// <summary>Space after barline to first note.</summary>
    public const double SpaceAfterBarline = 1.3;
    
    /// <summary>Space after clef to next element.</summary>
    public const double SpaceAfterClef = 1.0;
    
    /// <summary>Space after time signature to first note.</summary>
    public const double SpaceAfterTimeSignature = 2.0;
    
    /// <summary>Space after key signature to first note.</summary>
    public const double SpaceAfterKeySignature = 2.5;
}