using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Rules for calculating spacing in music notation.
/// </summary>
/// <remarks>
/// Based on Gourlay (1987): "Spacing a Line of Music"
/// The spacing is approximately logarithmic with respect to duration.
/// </remarks>
public static class SpacingRules
{
    /// <summary>Base width for a quarter note in pixels.</summary>
    public const double QuarterNoteWidth = 36;
    
    /// <summary>Minimum width for any note in pixels.</summary>
    public const double MinNoteWidth = 20;
    
    /// <summary>Width added for each accidental.</summary>
    public const double AccidentalWidth = 12;
    
    /// <summary>Width added for each dot.</summary>
    public const double DotWidth = 6;
    
    /// <summary>Width of a single barline.</summary>
    public const double BarlineWidth = 8;
    
    /// <summary>Width of a repeat barline.</summary>
    public const double RepeatBarlineWidth = 16;
    
    /// <summary>Width of a double or final barline.</summary>
    public const double DoubleBarlineWidth = 12;
    
    /// <summary>Width of clef.</summary>
    public const double ClefWidth = 30;
    
    /// <summary>Width per accidental in key signature.</summary>
    public const double KeySignatureAccidentalWidth = 10;
    
    /// <summary>Width of time signature.</summary>
    public const double TimeSignatureWidth = 25;
    
    /// <summary>
    /// Calculates the minimum width for a music item based on its duration.
    /// </summary>
    public static double CalculateItemWidth(MusicItem item)
    {
        double baseWidth = CalculateDurationWidth(item.Duration);
        double accidentalWidth = GetAccidentalWidth(item);
        int dots = GetDots(item);
        
        return baseWidth + accidentalWidth + (dots * DotWidth);
    }
    
    /// <summary>
    /// Calculates width based on duration using logarithmic scaling.
    /// </summary>
    /// <remarks>
    /// Formula: width = baseWidth * (1 + log2(duration / quarterNote))
    /// This gives:
    /// - Whole note:      ~72px (2x quarter)
    /// - Half note:       ~54px (1.5x quarter)
    /// - Quarter note:    ~36px (base)
    /// - Eighth note:     ~27px (0.75x quarter)
    /// - Sixteenth note:  ~22px (0.6x quarter)
    /// </remarks>
    public static double CalculateDurationWidth(Fraction duration)
    {
        double quarterValue = Fraction.Quarter.ToDouble();
        double durationValue = duration.ToDouble();
        
        if (durationValue <= 0)
            return MinNoteWidth;
        
        // Logarithmic scaling: longer notes get more space, but not linearly
        double ratio = durationValue / quarterValue;
        double logFactor = 1.0 + Math.Log2(Math.Max(ratio, 0.0625)); // Clamp to 1/16
        
        return Math.Max(MinNoteWidth, QuarterNoteWidth * logFactor * 0.7);
    }
    
    /// <summary>
    /// Calculates the stretch weight for a music item.
    /// Longer notes have more weight and receive more extra space during justification.
    /// </summary>
    public static double CalculateStretchWeight(MusicItem item)
    {
        // Weight is proportional to duration
        return item.Duration.ToDouble();
    }
    
    /// <summary>
    /// Calculates the minimum width for a measure.
    /// </summary>
    public static double CalculateMeasureMinWidth(Measure measure)
    {
        double width = 0;
        
        // Start barline
        width += GetBarlineWidth(measure.StartBarline);
        
        // Items
        foreach (var item in measure.Items)
            width += CalculateItemWidth(item);
        
        // End barline
        width += GetBarlineWidth(measure.EndBarline);
        
        return width;
    }
    
    /// <summary>
    /// Calculates the width of system prefix (clef + key + optional time signature).
    /// </summary>
    public static double CalculatePrefixWidth(int keySharps, bool includeTimeSignature)
    {
        double width = ClefWidth;
        width += Math.Abs(keySharps) * KeySignatureAccidentalWidth;
        if (includeTimeSignature)
            width += TimeSignatureWidth;
        return width;
    }
    
    /// <summary>
    /// Gets the width of a barline type.
    /// </summary>
    public static double GetBarlineWidth(BarlineType type) => type switch
    {
        BarlineType.None => 0,
        BarlineType.Single => BarlineWidth,
        BarlineType.Double => DoubleBarlineWidth,
        BarlineType.Final => DoubleBarlineWidth,
        BarlineType.RepeatStart => RepeatBarlineWidth,
        BarlineType.RepeatEnd => RepeatBarlineWidth,
        BarlineType.RepeatBoth => RepeatBarlineWidth * 1.5,
        _ => BarlineWidth
    };
    
    private static double GetAccidentalWidth(MusicItem item)
    {
        return item switch
        {
            NoteItem note => note.Accidental != null ? AccidentalWidth : 0,
            ChordItem chord => chord.Notes.Count(n => n.Accidental != null) * AccidentalWidth,
            _ => 0
        };
    }
    
    private static int GetDots(MusicItem item)
    {
        return item switch
        {
            NoteItem note => note.Dots,
            RestItem rest => rest.Dots,
            ChordItem chord => chord.Dots,
            _ => 0
        };
    }
}