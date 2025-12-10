using LilySharp.Core.Syntax;

namespace LilySharp.Core.Tablature;

/// <summary>
/// Provides predefined tunings and fret calculation for tablature.
/// MIDI note numbers for standard tunings (index 0 = lowest string).
/// String numbers in tablature: 1 = highest pitch, 6 = lowest pitch (for guitar).
/// </summary>
public static class Tunings
{
    // Guitar: E2=40, A2=45, D3=50, G3=55, B3=59, E4=64 (6弦→1弦)
    public static readonly int[] Guitar = [40, 45, 50, 55, 59, 64];
    
    // Bass (4-string): E1=28, A1=33, D2=38, G2=43 (4弦→1弦)
    public static readonly int[] Bass = [28, 33, 38, 43];
    
    // Bass (5-string): B0=23, E1=28, A1=33, D2=38, G2=43 (5弦→1弦)
    public static readonly int[] Bass5 = [23, 28, 33, 38, 43];
    
    // Ukulele: G4=67, C4=60, E4=64, A4=69 (4弦→1弦, re-entrant tuning)
    public static readonly int[] Ukulele = [67, 60, 64, 69];
    
    public static int[] GetTuning(TuningType type) => type switch
    {
        TuningType.Guitar => Guitar,
        TuningType.Bass => Bass,
        TuningType.Bass5 => Bass5,
        TuningType.Ukulele => Ukulele,
        _ => Guitar
    };
    
    public static int GetStringCount(TuningType type) => type switch
    {
        TuningType.Guitar => 6,
        TuningType.Bass => 4,
        TuningType.Bass5 => 5,
        TuningType.Ukulele => 4,
        _ => 6
    };
    
    /// <summary>
    /// Calculates the best string and fret for a given MIDI pitch.
    /// </summary>
    /// <param name="midiPitch">The MIDI note number to place.</param>
    /// <param name="tuning">The tuning array (index 0 = lowest string).</param>
    /// <param name="preferredString">Preferred string (1 = highest, 0 = auto).</param>
    /// <returns>A tuple of (stringNumber, fret) where stringNumber 1 = highest pitch string.</returns>
    public static (int stringNum, int fret) CalculateFret(int midiPitch, int[] tuning, int preferredString = 0)
    {
        int stringCount = tuning.Length;
        
        // Convert 1-based string number to array index
        // String 1 (highest) = index stringCount-1
        // String N (lowest) = index 0
        int ToIndex(int str) => stringCount - str;
        int ToStringNum(int idx) => stringCount - idx;
        
        // If preferred string is specified, use it
        if (preferredString >= 1 && preferredString <= stringCount)
        {
            int idx = ToIndex(preferredString);
            int openPitch = tuning[idx];
            int fret = midiPitch - openPitch;
            if (fret >= 0 && fret <= 24)
            {
                return (preferredString, fret);
            }
        }
        
        // Auto: find the best string (prefer lower fret positions)
        int bestString = stringCount; // lowest string as fallback
        int bestFret = 99;
        
        // Search from highest to lowest string
        for (int idx = stringCount - 1; idx >= 0; idx--)
        {
            int openPitch = tuning[idx];
            int fret = midiPitch - openPitch;
            
            if (fret >= 0 && fret <= 24)
            {
                if (fret < bestFret)
                {
                    bestString = ToStringNum(idx);
                    bestFret = fret;
                }
            }
        }
        
        // If no valid position found, return lowest string with calculated fret
        if (bestFret == 99)
        {
            bestFret = midiPitch - tuning[0];
            if (bestFret < 0) bestFret = 0;
        }
        
        return (bestString, bestFret);
    }
}