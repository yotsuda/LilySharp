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
    /// Calculates width based on duration using Lilypond's spacing algorithm.
    /// </summary>
    /// <remarks>
    /// Uses CalculateDurationSpace internally for consistency.
    /// This is a convenience method for situations where only the width is needed.
    /// </remarks>
    public static double CalculateDurationWidth(Fraction duration)
    {
        return Math.Max(MinNoteWidth, CalculateDurationSpace(duration));
    }
    /// <summary>
    /// Calculates the minimum width needed for a measure (collision avoidance only).
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
    /// Calculates the ideal width for a measure (includes duration-based spacing).
    /// </summary>
    /// <remarks>
    /// The ideal width follows Lilypond's spacing algorithm where each duration
    /// gets space proportional to its length (logarithmic scaling).
    /// This is the width that produces visually pleasing spacing.
    /// </remarks>
    public static double CalculateMeasureIdealWidth(Measure measure)
    {
        double width = 0;
        
        // Barline widths
        width += GetBarlineWidth(measure.StartBarline);
        width += GetBarlineWidth(measure.EndBarline);
        
        // Spring ideal distances (content area) - includes duration space
        if (measure.Items.Length > 0)
        {
            var springs = CreateSpringsForMeasure(measure);
            foreach (var spring in springs)
            {
                width += spring.IdealDistance;
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
    // Uses SMuFL metrics from GlyphMetrics for accurate spacing
    
    /// <summary>Minimum gap between items in pixels.</summary>
    public static double MinItemGap => GlyphMetrics.ToPixels(GlyphMetrics.MinItemGap);
    
    /// <summary>Padding between barline and first/last item in pixels.</summary>
    public static double BarlinePadding => GlyphMetrics.ToPixels(GlyphMetrics.BarlinePadding);
    
    /// <summary>Gap between accidental and notehead in pixels.</summary>
    public static double AccidentalNoteGap => GlyphMetrics.ToPixels(GlyphMetrics.AccidentalNoteGap);
    
    /// <summary>
    /// Calculates the left extent of an item from its reference point (notehead center).
    /// This includes accidentals which are drawn to the left of the notehead.
    /// </summary>
    /// <remarks>
    /// Reference point is at the horizontal center of the notehead.
    /// Left extent = half notehead + accidental width + gap (if accidental present)
    /// </remarks>
    public static double CalculateLeftExtent(MusicItem item)
    {
        // Get notehead metrics (note value determines which notehead glyph)
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        
        // Base extent: from center to left edge of notehead
        double extent = GlyphMetrics.ToPixels(noteheadBBox.CenterX);
        
        // For rests, use a simplified calculation
        if (item is RestItem)
        {
            return extent;
        }
        
        // Add accidental width if present
        string? accidental = item switch
        {
            NoteItem note => note.Accidental,
            ChordItem chord => chord.Notes.Select(n => n.Accidental).FirstOrDefault(a => a != null),
            _ => null
        };
        
        if (accidental != null)
        {
            var accBBox = GlyphMetrics.GetAccidentalBBox(accidental);
            extent += GlyphMetrics.ToPixels(accBBox.Width) + AccidentalNoteGap;
        }
        
        return extent;
    }
    
    /// <summary>
    /// Calculates the right extent of an item from its reference point (notehead center).
    /// This includes the notehead and any dots.
    /// </summary>
    /// <remarks>
    /// Reference point is at the horizontal center of the notehead.
    /// Right extent = half notehead + dots (if present)
    /// </remarks>
    public static double CalculateRightExtent(MusicItem item)
    {
        // Get notehead metrics
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        
        // Base extent: from center to right edge of notehead
        double extent = GlyphMetrics.ToPixels(noteheadBBox.Width - noteheadBBox.CenterX);
        
        // Add dot width
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            // Each dot plus a small gap
            extent += dots * GlyphMetrics.ToPixels(dotBBox.Width + EngravingDefaults.DotGap);
        }
        
        return extent;
    }
    
    /// <summary>
    /// Gets the note value (1=whole, 2=half, 4=quarter, etc.) for a music item.
    /// </summary>
    private static int GetNoteValue(MusicItem item)
    {
        var duration = item.Duration;
        return (int)duration.Denominator;
    }
    /// <summary>
    /// Creates a spring between two music items.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:100-130 note_spacing()
    /// - ideal_distance = get_duration_space(duration)
    /// - min_distance = max(increment, skyline_collision_distance)
    /// - inverse_stretch_strength = max(0.1, ideal - min)
    /// </remarks>
    public static Spring CreateSpring(MusicItem? prevItem, MusicItem? nextItem, Fraction prevDuration)
    {
        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement * GlyphMetrics.SpaceHeight;
        
        // Skyline-based collision distance (rod)
        double skylineDistance = CalculateSkylineDistance(prevItem, nextItem, staffY: 0);
        
        // min_distance = max(defaultMin, skylineDistance) - ensures no collision
        double minDistance = Math.Max(defaultMin, skylineDistance);
        
        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(prevDuration);
        
        // LILYPOND-REF: lily/spacing-basic.cc:115 note_spacing() - inverse_stretch
        // This controls how much the spring can stretch
        double inverseStretchStrength = Math.Max(0.1 * GlyphMetrics.SpaceHeight, idealDistance - minDistance);
        
        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }
    
    /// <summary>
    /// Calculates the duration-based space.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-options.cc:58-73 get_duration_space()
    /// - ratio = duration / base_shortest_duration
    /// - if ratio less than 1: space = (shortest_duration_space + ratio - 1) * increment
    /// - if ratio >= 1: space = (shortest_duration_space + log2(ratio)) * increment
    /// 
    /// This gives consistent, proportional spacing that looks right visually.
    /// </remarks>
    public static double CalculateDurationSpace(Fraction duration)
    {
        double durationValue = duration.ToDouble();
        
        if (durationValue <= 0)
            return EngravingDefaults.SpacingIncrement * GlyphMetrics.SpaceHeight;
        
        // Ratio of this duration to base shortest (typically 1/8)
        double ratio = durationValue / EngravingDefaults.BaseShortestDuration;
        
        // LILYPOND-REF: lily/spacing-options.cc:65-70 get_duration_space()
        double spaceFactor;
        if (ratio < 1.0)
        {
            // Linear scaling for very short notes
            spaceFactor = EngravingDefaults.ShortestDurationSpace + ratio - 1.0;
        }
        else
        {
            // Logarithmic scaling (Gourlay algorithm)
            spaceFactor = EngravingDefaults.ShortestDurationSpace + Math.Log2(ratio);
        }
        
        // Convert to pixels: spaceFactor * increment * staff_space
        return spaceFactor * EngravingDefaults.SpacingIncrement * GlyphMetrics.SpaceHeight;
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

    // ========================================
    // Skyline Generation
    // ========================================
    
    /// <summary>
    /// Creates the right skyline for a music item.
    /// The right skyline represents the rightmost extent at each Y coordinate.
    /// </summary>
    /// <param name="item">The music item</param>
    /// <param name="referenceX">X coordinate of the reference point (notehead center)</param>
    /// <param name="staffY">Y coordinate of the staff's middle line</param>
    public static Skyline CreateRightSkyline(MusicItem item, double referenceX, double staffY)
    {
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadCenterX = GlyphMetrics.ToPixels(noteheadBBox.CenterX);
        double noteheadLeftX = referenceX - noteheadCenterX;
        double noteheadWidth = GlyphMetrics.ToPixels(noteheadBBox.Width);
        
        // Get note Y position
        double noteY = item switch
        {
            NoteItem note => staffY - note.StaffPosition * GlyphMetrics.SpaceHeight / 2,
            _ => staffY
        };
        
        // Add notehead box
        double noteheadYBottom = noteY - GlyphMetrics.ToPixels(noteheadBBox.Top);
        double noteheadYTop = noteY - GlyphMetrics.ToPixels(noteheadBBox.Bottom);
        boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));
        
        // Add flag if present (8th notes and shorter with stems)
        if (item is NoteItem note2 && noteValue >= 8)
        {
            var flagBBox = GlyphMetrics.GetFlagBBox(noteValue, note2.StemUp);
            if (flagBBox != default)
            {
                // Flag is attached to the stem end
                double stemHeight = EngravingDefaults.IdealStemLength * GlyphMetrics.SpaceHeight;
                double stemEndY = note2.StemUp ? noteY - stemHeight : noteY + stemHeight;
                
                // Flag position (attached at stem)
                double stemX = note2.StemUp 
                    ? noteheadLeftX + GlyphMetrics.ToPixels(GlyphMetrics.StemUpSE.X)
                    : noteheadLeftX + GlyphMetrics.ToPixels(GlyphMetrics.StemDownNW.X);
                
                double flagYBottom, flagYTop;
                if (note2.StemUp)
                {
                    // Flag extends downward from stem end
                    flagYBottom = stemEndY;
                    flagYTop = stemEndY - GlyphMetrics.ToPixels(flagBBox.Bottom - flagBBox.Top);
                }
                else
                {
                    // Flag extends upward from stem end  
                    flagYTop = stemEndY;
                    flagYBottom = stemEndY + GlyphMetrics.ToPixels(flagBBox.Top - flagBBox.Bottom);
                }
                
                double flagWidth = GlyphMetrics.ToPixels(flagBBox.Width);
                boxes.Add((Math.Min(flagYBottom, flagYTop), Math.Max(flagYBottom, flagYTop), 
                           stemX, stemX + flagWidth));
            }
        }
        
        // Add dots if present
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotWidth = GlyphMetrics.ToPixels(dotBBox.Width);
            double dotGap = GlyphMetrics.ToPixels(EngravingDefaults.DotGap);
            
            // Dots must avoid staff lines - if note is on a line, shift dot up
            int staffPosition = item switch
            {
                NoteItem note => note.StaffPosition,
                _ => 1  // Default to odd (not on line)
            };
            double dotYOffset = (staffPosition % 2 == 0) ? -GlyphMetrics.SpaceHeight / 2 : 0;
            
            for (int d = 0; d < dots; d++)
            {
                double dotX = noteheadLeftX + noteheadWidth + dotGap + d * (dotWidth + dotGap);
                double dotYCenter = noteY + dotYOffset;
                double dotRadius = GlyphMetrics.ToPixels(dotBBox.Height / 2);
                boxes.Add((dotYCenter - dotRadius, dotYCenter + dotRadius, dotX, dotX + dotWidth));
            }
        }
        
        return Skyline.FromBoxes(boxes, Skyline.Direction.Right);
    }
    
    /// <summary>
    /// Creates the left skyline for a music item.
    /// The left skyline represents the leftmost extent at each Y coordinate.
    /// </summary>
    public static Skyline CreateLeftSkyline(MusicItem item, double referenceX, double staffY)
    {
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadCenterX = GlyphMetrics.ToPixels(noteheadBBox.CenterX);
        double noteheadLeftX = referenceX - noteheadCenterX;
        double noteheadWidth = GlyphMetrics.ToPixels(noteheadBBox.Width);
        
        // Get note Y position
        double noteY = item switch
        {
            NoteItem note => staffY - note.StaffPosition * GlyphMetrics.SpaceHeight / 2,
            _ => staffY
        };
        
        // Add notehead box
        double noteheadYBottom = noteY - GlyphMetrics.ToPixels(noteheadBBox.Top);
        double noteheadYTop = noteY - GlyphMetrics.ToPixels(noteheadBBox.Bottom);
        boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));
        
        // Add accidental if present (to the left of notehead)
        string? accidental = item switch
        {
            NoteItem note => note.Accidental,
            ChordItem chord => chord.Notes.Select(n => n.Accidental).FirstOrDefault(a => a != null),
            _ => null
        };
        
        if (accidental != null)
        {
            var accBBox = GlyphMetrics.GetAccidentalBBox(accidental);
            double accWidth = GlyphMetrics.ToPixels(accBBox.Width);
            double accNoteGap = GlyphMetrics.ToPixels(GlyphMetrics.AccidentalNoteGap);
            double accX = noteheadLeftX - accWidth - accNoteGap;
            
            double accYBottom = noteY - GlyphMetrics.ToPixels(accBBox.Top);
            double accYTop = noteY - GlyphMetrics.ToPixels(accBBox.Bottom);
            boxes.Add((accYBottom, accYTop, accX, accX + accWidth));
        }
        
        return Skyline.FromBoxes(boxes, Skyline.Direction.Left);
    }
    /// <summary>
    /// Calculates the minimum distance between two items.
    /// </summary>
    /// <remarks>
    /// For horizontal spacing between notes, we use notehead-based calculation
    /// (not including stems/flags which extend vertically and rarely collide horizontally).
    /// This follows Lilypond's approach where note spacing is primarily based on noteheads.
    /// </remarks>
    public static double CalculateSkylineDistance(MusicItem? prevItem, MusicItem? nextItem, 
                                                   double staffY)
    {
        // For barline-to-item or item-to-barline, use simple calculation
        if (prevItem == null || nextItem == null)
        {
            double prevExtent = prevItem != null ? CalculateNoteheadRightExtent(prevItem) : BarlinePadding;
            double nextExtent = nextItem != null ? CalculateLeftExtent(nextItem) : BarlinePadding;
            return prevExtent + nextExtent + MinItemGap;
        }
        
        // Use notehead-based calculation (excluding stems/flags)
        double prevRightExtent = CalculateNoteheadRightExtent(prevItem);
        double nextLeftExtent = CalculateLeftExtent(nextItem);
        
        return prevRightExtent + nextLeftExtent + MinItemGap;
    }
    
    /// <summary>
    /// Calculates the right extent from notehead center, excluding stems and flags.
    /// </summary>
    private static double CalculateNoteheadRightExtent(MusicItem item)
    {
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        
        // Right extent from center = width - centerX
        double extent = GlyphMetrics.ToPixels(noteheadBBox.Width - noteheadBBox.CenterX);
        
        // Add dots if present
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotWidth = GlyphMetrics.ToPixels(dotBBox.Width);
            double dotGap = GlyphMetrics.ToPixels(EngravingDefaults.DotGap);
            extent += dotGap + dots * dotWidth + (dots - 1) * dotGap;
        }
        
        return extent;
    }
}
