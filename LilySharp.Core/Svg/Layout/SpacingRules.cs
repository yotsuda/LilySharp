using System.Collections.Immutable;
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
    /// Calculates the minimum width for a measure using the Spring-Rod model.
    /// </summary>
    /// <remarks>
    /// The minimum width is the sum of all spring MinDistances plus barline widths.
    /// This ensures no visual collisions when the measure is at its minimum size.
    /// </remarks>
    public static double CalculateMeasureMinWidth(Measure measure)
    {
        double width = 0;
        
        // Barline widths
        width += GetBarlineWidth(measure.StartBarline);
        width += GetBarlineWidth(measure.EndBarline);
        
        // Spring minimum distances (content area)
        if (measure.Items.Length > 0)
        {
            var springs = CreateSpringsForMeasure(measure);
            foreach (var spring in springs)
            {
                width += spring.MinDistance;
            }
        }
        
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

    // ========================================
    // Spring-Rod Model Support
    // ========================================
    
    /// <summary>Notehead width (reference point is center).</summary>
    public const double NoteheadWidth = 12;
    
    /// <summary>Rest width.</summary>
    public const double RestWidth = 10;
    
    /// <summary>Minimum gap between items.</summary>
    public const double MinItemGap = 4;
    
    /// <summary>Padding between barline and first/last item.</summary>
    public const double BarlinePadding = 8;
    
    /// <summary>
    /// Calculates the left extent of an item from its reference point.
    /// This includes accidentals which are drawn to the left of the notehead.
    /// </summary>
    public static double CalculateLeftExtent(MusicItem item)
    {
        double extent = item switch
        {
            NoteItem => NoteheadWidth / 2,  // Half notehead to the left of center
            ChordItem => NoteheadWidth / 2,
            RestItem => RestWidth / 2,
            _ => NoteheadWidth / 2
        };
        
        // Add accidental width (accidentals are to the left of the notehead)
        double accidentalExtent = item switch
        {
            NoteItem note => note.Accidental != null ? AccidentalWidth + 2 : 0,
            ChordItem chord => chord.Notes.Any(n => n.Accidental != null) ? AccidentalWidth + 2 : 0,
            _ => 0
        };
        
        return extent + accidentalExtent;
    }
    
    /// <summary>
    /// Calculates the right extent of an item from its reference point.
    /// This includes the notehead and any dots.
    /// </summary>
    public static double CalculateRightExtent(MusicItem item)
    {
        double extent = item switch
        {
            NoteItem => NoteheadWidth / 2,  // Half notehead to the right of center
            ChordItem => NoteheadWidth / 2,
            RestItem => RestWidth / 2,
            _ => NoteheadWidth / 2
        };
        
        // Add dot width
        int dots = GetDots(item);
        if (dots > 0)
        {
            extent += dots * DotWidth;
        }
        
        return extent;
    }
    
    /// <summary>
    /// Creates a spring between two adjacent items.
    /// </summary>
    /// <param name="prevItem">The previous item (null for barline-to-first-item)</param>
    /// <param name="nextItem">The next item (null for last-item-to-barline)</param>
    /// <param name="prevDuration">Duration of previous item (for ideal distance calculation)</param>
    public static Spring CreateSpring(MusicItem? prevItem, MusicItem? nextItem, Fraction prevDuration)
    {
        // Calculate ideal distance based on duration (Gourlay algorithm)
        double idealDistance = CalculateDurationWidth(prevDuration);
        
        // Calculate minimum distance to avoid collision
        double prevRightExtent = prevItem != null ? CalculateRightExtent(prevItem) : BarlinePadding;
        double nextLeftExtent = nextItem != null ? CalculateLeftExtent(nextItem) : BarlinePadding;
        double minDistance = prevRightExtent + nextLeftExtent + MinItemGap;
        
        // Ensure ideal is at least min
        idealDistance = Math.Max(idealDistance, minDistance);
        
        // Calculate stiffness (inverse of duration - shorter notes are stiffer)
        double durationValue = prevDuration.ToDouble();
        double stiffness = durationValue > 0 ? 1.0 / durationValue : 10.0;
        
        return new Spring(idealDistance, minDistance, stiffness);
    }
    
    /// <summary>
    /// Creates all springs for a measure.
    /// </summary>
    /// <param name="measure">The measure to create springs for</param>
    /// <returns>Array of springs (one between each pair of adjacent reference points)</returns>
    public static ImmutableArray<Spring> CreateSpringsForMeasure(Measure measure)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<Spring>.Empty;
        
        var springs = new List<Spring>();
        
        // Spring from start barline to first item
        var firstItem = measure.Items[0];
        springs.Add(CreateSpring(null, firstItem, Fraction.Quarter)); // Use quarter note as default
        
        // Springs between items
        for (int i = 0; i < measure.Items.Length - 1; i++)
        {
            var prevItem = measure.Items[i];
            var nextItem = measure.Items[i + 1];
            springs.Add(CreateSpring(prevItem, nextItem, prevItem.Duration));
        }
        
        // Spring from last item to end barline
        var lastItem = measure.Items[^1];
        springs.Add(CreateSpring(lastItem, null, lastItem.Duration));
        
        return springs.ToImmutableArray();
    }
}