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
    /// <summary>Base width for a quarter note in staff spaces.</summary>
    public const double QuarterNoteWidth = 3.6;

    /// <summary>Minimum width for any note in staff spaces.</summary>
    public const double MinNoteWidth = 2.0;

    /// <summary>Width added for each accidental in staff spaces.</summary>
    public const double AccidentalWidth = 1.2;

    /// <summary>Width added for each dot in staff spaces.</summary>
    public const double DotWidth = 0.6;

    /// <summary>Width of a single barline in staff spaces.</summary>
    public const double BarlineWidth = 0.8;

    /// <summary>Width of a repeat barline in staff spaces.</summary>
    public const double RepeatBarlineWidth = 1.6;

    /// <summary>Width of a double or final barline in staff spaces.</summary>
    public const double DoubleBarlineWidth = 1.2;

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
    /// <remarks>
    /// Uses spacing constants from GlyphMetrics (see LILYPOND-REF comments there).
    /// </remarks>
    public static double CalculatePrefixWidth(int keySharps, bool includeTimeSignature,
        int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        // Clef width includes spacing to key signature
        double width = GlyphMetrics.ClefToKeySignatureSpace;

        int keyAccidentals = Math.Abs(keySharps);
        if (keyAccidentals > 0)
        {
            // Use actual Emmentaler glyph width: sharps=1.1ss, flats=0.8ss
            double accWidth = GlyphMetrics.GetKeySignatureAccidentalWidth(keySharps > 0);
            width += keyAccidentals * accWidth;
        }

        if (includeTimeSignature)
        {
            // Add spacing from key signature (or clef) to time signature
            if (keyAccidentals > 0)
                width += GlyphMetrics.KeySignatureToTimeSignatureSpace;
            double timeSigWidth = GlyphMetrics.GetTimeSigWidth(timeSigBeats, timeSigBeatType);
            width += timeSigWidth + GlyphMetrics.TimeSignatureToFirstNoteSpace;
        }

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

    /// <summary>Minimum gap between items in staff spaces.</summary>
    public static double MinItemGap => GlyphMetrics.MinItemGap;

    /// <summary>Padding between barline and first/last item in staff spaces.</summary>
    public static double BarlinePadding => GlyphMetrics.BarlinePadding;

    /// <summary>Gap between accidental and notehead in staff spaces.</summary>
    public static double AccidentalNoteGap => GlyphMetrics.AccidentalNoteGap;

    /// <summary>
    /// Calculates the left extent of an item from its reference point (notehead center).
    /// This includes accidentals which are drawn to the left of the notehead.
    /// </summary>
    /// <remarks>
    /// Reference point is at the horizontal center of the notehead.
    /// Left extent = half notehead + accidental width + gap (if accidental present)
    ///
    /// LILYPOND-REF: lily/accidental-placement.cc
    /// For chords with multiple accidentals, uses AccidentalPlacement to calculate
    /// the staggered/stacked positions, then returns the leftmost extent.
    /// </remarks>
    public static double CalculateLeftExtent(MusicItem item)
    {
        // Get notehead metrics (note value determines which notehead glyph)
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);

        // Base extent: from center to left edge of notehead
        double extent = noteheadBBox.CenterX;

        // For rests, use a simplified calculation
        if (item is RestItem)
        {
            return extent;
        }

        // Handle accidentals
        if (item is ChordItem chord)
        {
            // For chords, use AccidentalPlacement to calculate staggered positions
            var placement = new AccidentalPlacement();
            var layouts = placement.CalculatePositions(chord.Notes);

            if (layouts.Length > 0)
            {
                // Find the leftmost accidental position (most negative XOffset)
                // XOffset is negative, representing distance to the left of notehead
                double leftmostOffset = layouts.Min(l => l.XOffset);

                // The leftmost extent is the absolute value of the offset
                extent = Math.Abs(leftmostOffset);
            }
        }
        else if (item is NoteItem note && note.Accidental != null)
        {
            // Single note with accidental
            var accBBox = GlyphMetrics.GetAccidentalBBox(note.Accidental);
            extent += accBBox.Width + AccidentalNoteGap;
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
        double extent = noteheadBBox.Width - noteheadBBox.CenterX;

        // Add dot width
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            // Each dot plus a small gap
            extent += dots * dotBBox.Width + EngravingDefaults.DotGap;
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
    /// LILYPOND-REF: lily/note-spacing.cc:119-199 stem_dir_correction()
    /// - ideal_distance = get_duration_space(duration)
    /// - min_distance = max(increment, skyline_collision_distance)
    /// - inverse_stretch_strength = max(0.1, ideal - min)
    /// - stem direction optical correction applied to ideal
    /// </remarks>
    public static Spring CreateSpring(MusicItem? prevItem, MusicItem? nextItem, Fraction prevDuration,
                                      NoteSpacingParameters? noteParams = null)
    {
        var np = noteParams ?? NoteSpacingParameters.Default;

        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // Skyline-based collision distance (rod)
        double skylineDistance = CalculateSkylineDistance(prevItem, nextItem, staffY: 0);

        // min_distance = max(defaultMin, skylineDistance) - ensures no collision
        double minDistance = Math.Max(defaultMin, skylineDistance);

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(prevDuration);

        // --- Stem direction optical correction ---
        // LILYPOND-REF: lily/note-spacing.cc:119-199 stem_dir_correction
        idealDistance += CalculateStemCorrection(prevItem, nextItem, np);

        // LILYPOND-REF: lily/spacing-basic.cc:115 note_spacing() - inverse_stretch
        // This controls how much the spring can stretch
        double inverseStretchStrength = Math.Max(0.1, idealDistance - minDistance);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Calculates stem direction optical correction for spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:119-199 stem_dir_correction
    /// - Opposite stem directions: increase space to avoid visual collision
    /// - Same stem direction: decrease space for tighter appearance
    /// </remarks>
    private static double CalculateStemCorrection(MusicItem? prevItem, MusicItem? nextItem,
                                                   NoteSpacingParameters noteParams)
    {
        bool? prevStemUp = prevItem switch
        {
            NoteItem n => n.StemUp,
            ChordItem c => c.StemUp,
            _ => null
        };

        bool? nextStemUp = nextItem switch
        {
            NoteItem n => n.StemUp,
            ChordItem c => c.StemUp,
            _ => null
        };

        if (!prevStemUp.HasValue || !nextStemUp.HasValue)
            return 0;

        double increment = EngravingDefaults.SpacingIncrement;

        if (prevStemUp.Value != nextStemUp.Value)
        {
            // Different stem directions: stems may cross → increase space
            // LILYPOND-REF: note-spacing.cc:141-162 (different direction correction)
            // Simplified: apply stem_spacing_correction as fraction of increment
            return noteParams.StemSpacingCorrection * increment * 0.5;
        }
        else
        {
            // Same stem direction: can be tighter
            // LILYPOND-REF: note-spacing.cc:164-199 (same direction correction)
            return -noteParams.SameDirectionCorrection * increment * 0.5;
        }
    }

    /// <summary>
    /// Calculates the duration-based space.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-options.cc:68-104 get_duration_space()
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
            return EngravingDefaults.SpacingIncrement;

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

        // Result in staff spaces: spaceFactor * increment
        return spaceFactor * EngravingDefaults.SpacingIncrement;
    }

    /// <summary>
    /// Creates a spring for grace note spacing with tighter parameters.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:140-155 grace note spring
    /// LILYPOND-REF: scm/define-grobs.scm:1585-1598 GraceSpacing
    /// Grace notes use: spacing-increment=0.8, shortest-duration-space=1.6,
    /// inverse_stretch_strength = increment / 2.0
    /// </remarks>
    public static Spring CreateGraceSpring(Fraction graceDuration,
                                            GraceSpacingParameters? graceParams = null)
    {
        var gp = graceParams ?? GraceSpacingParameters.Default;

        double durationValue = graceDuration.ToDouble();
        if (durationValue <= 0)
            durationValue = gp.BaseShortestDuration;

        // Same Gourlay formula as regular notes, but with grace parameters
        double ratio = durationValue / gp.BaseShortestDuration;
        double spaceFactor = ratio < 1.0
            ? gp.ShortestDurationSpace + ratio - 1.0
            : gp.ShortestDurationSpace + Math.Log2(ratio);

        double idealDistance = spaceFactor * gp.SpacingIncrement;
        double minDistance = gp.SpacingIncrement;

        // LILYPOND-REF: spacing-basic.cc:153
        // inverse_stretch_strength = increment / 2.0 (more rigid than normal)
        double inverseStretchStrength = gp.SpacingIncrement / 2.0;

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Adjusts a spring's MinDistance to accommodate grace notes before the next item.
    /// </summary>
    /// <param name="spring">The original spring between items.</param>
    /// <param name="graceNoteCount">Number of grace notes before the next item.</param>
    /// <returns>Spring with adjusted MinDistance to reserve space for grace notes.</returns>
    public static Spring AdjustSpringForGraceNotes(Spring spring, int graceNoteCount)
    {
        if (graceNoteCount <= 0)
            return spring;

        double graceWidth = GraceNoteEngraver.GetGraceGroupWidth(graceNoteCount);
        double newMin = Math.Max(spring.MinDistance, spring.MinDistance + graceWidth);
        double newIdeal = Math.Max(spring.IdealDistance, newMin);

        return new Spring(newIdeal, newMin, spring.InverseStretchStrength);
    }

    /// <summary>
    /// Creates a spring for a timing column based on duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    /// Simplified spring creation for timing-based columns without skyline collision detection.
    /// Uses duration-based spacing for ideal distance.
    /// </remarks>
    public static Spring CreateTimingSpring(Fraction duration)
    {
        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(duration);

        // Ensure minimum distance
        idealDistance = Math.Max(idealDistance, defaultMin);

        // min_distance for timing springs (no skyline collision)
        double minDistance = defaultMin;

        // LILYPOND-REF: lily/spacing-basic.cc:115 note_spacing() - inverse_stretch
        double inverseStretchStrength = Math.Max(0.1, idealDistance - minDistance);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
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

    /// <summary>
    /// Creates all springs for a measure, considering lyrics width.
    /// </summary>
    /// <param name="measure">The measure to create springs for</param>
    /// <param name="measureIndex">Index of this measure in the score</param>
    /// <param name="lyrics">All lyrics in the score</param>
    /// <returns>Array of springs with lyrics-adjusted minimum distances</returns>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    /// LILYPOND-REF: lily/note-spacing.cc:80-85 skyline-based min_distance
    ///
    /// When lyrics are present under notes, their width contributes to the
    /// minimum distance between note columns. This prevents lyric text from
    /// overlapping with adjacent syllables.
    /// </remarks>
    public static ImmutableArray<Spring> CreateSpringsForMeasureWithLyrics(
        Measure measure,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<Spring>.Empty;

        // Build a lookup of lyrics by item index for this measure
        var lyricsByItem = new Dictionary<int, List<LyricItem>>();
        foreach (var lyric in lyrics)
        {
            if (lyric.MeasureIndex == measureIndex)
            {
                if (!lyricsByItem.TryGetValue(lyric.ItemIndex, out var list))
                {
                    list = new List<LyricItem>();
                    lyricsByItem[lyric.ItemIndex] = list;
                }
                list.Add(lyric);
            }
        }

        var springs = new List<Spring>();

        // Spring from start barline to first item
        var firstItem = measure.Items[0];
        var firstSpring = CreateSpring(null, firstItem, Fraction.Quarter);
        // Adjust for first item's lyric left extent
        if (lyricsByItem.TryGetValue(0, out var firstLyrics))
        {
            double lyricLeftExtent = GetLyricLeftExtent(firstLyrics);
            double adjustedMin = Math.Max(firstSpring.MinDistance, lyricLeftExtent + MinItemGap);
            firstSpring = new Spring(firstSpring.IdealDistance, adjustedMin, firstSpring.InverseStretchStrength);
        }
        springs.Add(firstSpring);

        // Springs between items
        for (int i = 0; i < measure.Items.Length - 1; i++)
        {
            var prevItem = measure.Items[i];
            var nextItem = measure.Items[i + 1];
            var spring = CreateSpring(prevItem, nextItem, prevItem.Duration);

            // LILYPOND-REF: lily/note-spacing.cc:80-85
            // Adjust minimum distance for lyrics
            double lyricDistance = CalculateLyricDistance(
                lyricsByItem.GetValueOrDefault(i),
                lyricsByItem.GetValueOrDefault(i + 1));

            if (lyricDistance > spring.MinDistance)
            {
                spring = new Spring(
                    Math.Max(spring.IdealDistance, lyricDistance),
                    lyricDistance,
                    spring.InverseStretchStrength);
            }

            springs.Add(spring);
        }

        // Spring from last item to end barline
        int lastIndex = measure.Items.Length - 1;
        var lastItem = measure.Items[^1];
        var lastSpring = CreateSpring(lastItem, null, lastItem.Duration);
        // Adjust for last item's lyric right extent
        if (lyricsByItem.TryGetValue(lastIndex, out var lastLyrics))
        {
            double lyricRightExtent = GetLyricRightExtent(lastLyrics);
            double adjustedMin = Math.Max(lastSpring.MinDistance, lyricRightExtent + MinItemGap);
            lastSpring = new Spring(lastSpring.IdealDistance, adjustedMin, lastSpring.InverseStretchStrength);
        }
        springs.Add(lastSpring);

        return springs.ToImmutableArray();
    }

    /// <summary>
    /// Calculates the minimum distance between two notes based on their lyrics.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    ///
    /// The distance is: prevLyricRightExtent + nextLyricLeftExtent + padding
    /// where each extent is half the lyric text width (centered under note).
    /// </remarks>
    private static double CalculateLyricDistance(List<LyricItem>? prevLyrics, List<LyricItem>? nextLyrics)
    {
        if (prevLyrics == null && nextLyrics == null)
            return 0;

        double prevRight = GetLyricRightExtent(prevLyrics);
        double nextLeft = GetLyricLeftExtent(nextLyrics);

        // Add minimum gap between syllables
        const double lyricPadding = 0.5;  // staff spaces

        return prevRight + nextLeft + lyricPadding;
    }

    /// <summary>
    /// Gets the right extent of lyrics (from note center to right edge of text).
    /// </summary>
    private static double GetLyricRightExtent(List<LyricItem>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(lyric.Text);
            // Right extent is half the width (text is centered under note)
            maxExtent = Math.Max(maxExtent, width / 2);
        }
        return maxExtent;
    }

    /// <summary>
    /// Gets the left extent of lyrics (from note center to left edge of text).
    /// </summary>
    private static double GetLyricLeftExtent(List<LyricItem>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(lyric.Text);
            // Left extent is half the width (text is centered under note)
            maxExtent = Math.Max(maxExtent, width / 2);
        }
        return maxExtent;
    }

    /// <summary>
    /// Estimates the width of lyric text in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-metric.cc:100-120 text extent calculation
    ///
    /// Uses the same estimation as LyricEngraver for consistency.
    /// The SVG renderer uses font-size = 4 * 0.8 = 3.2 staff spaces.
    /// </remarks>
    private static double EstimateLyricTextWidth(string text)
    {
        const double fontSize = 3.2;  // staff spaces (matches SvgRenderer)
        double width = 0;
        foreach (char c in text)
        {
            double ratio = c switch
            {
                'i' or 'l' or 'I' or '!' or '.' or '\'' or '-' => 0.3,
                'm' or 'w' or 'M' or 'W' => 0.7,
                _ => 0.5
            };
            width += fontSize * ratio;
        }
        return width;
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
    public static HorizontalSkyline CreateRightSkyline(MusicItem item, double referenceX, double staffY)
    {
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();

        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadCenterX = noteheadBBox.CenterX;
        double noteheadLeftX = referenceX - noteheadCenterX;
        double noteheadWidth = noteheadBBox.Width;

        // Get note Y position
        double noteY = item switch
        {
            NoteItem note => staffY - note.StaffPosition / 2.0,
            _ => staffY
        };

        // Add notehead box
        double noteheadYBottom = noteY - noteheadBBox.Top;
        double noteheadYTop = noteY - noteheadBBox.Bottom;
        boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));

        // Add flag if present (8th notes and shorter with stems)
        if (item is NoteItem note2 && noteValue >= 8)
        {
            var flagBBox = GlyphMetrics.GetFlagBBox(noteValue, note2.StemUp);
            if (flagBBox != default)
            {
                // Flag is attached to the stem end
                double stemHeight = EngravingDefaults.IdealStemLength;
                double stemEndY = note2.StemUp ? noteY - stemHeight : noteY + stemHeight;

                // Flag position (attached at stem)
                double stemX = note2.StemUp
                    ? noteheadLeftX + GlyphMetrics.StemUpSE.X
                    : noteheadLeftX + GlyphMetrics.StemDownNW.X;

                double flagYBottom, flagYTop;
                if (note2.StemUp)
                {
                    // Flag extends downward from stem end
                    flagYBottom = stemEndY;
                    flagYTop = stemEndY - flagBBox.Bottom - flagBBox.Top;
                }
                else
                {
                    // Flag extends upward from stem end
                    flagYTop = stemEndY;
                    flagYBottom = stemEndY + flagBBox.Top - flagBBox.Bottom;
                }

                double flagWidth = flagBBox.Width;
                boxes.Add((Math.Min(flagYBottom, flagYTop), Math.Max(flagYBottom, flagYTop),
                           stemX, stemX + flagWidth));
            }
        }

        // Add dots if present
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotWidth = dotBBox.Width;
            double dotGap = EngravingDefaults.DotGap;

            // Dots must avoid staff lines - if note is on a line, shift dot up
            int staffPosition = item switch
            {
                NoteItem note => note.StaffPosition,
                _ => 1  // Default to odd (not on line)
            };
            double dotYOffset = (staffPosition % 2 == 0) ? -0.5 : 0;

            for (int d = 0; d < dots; d++)
            {
                double dotX = noteheadLeftX + noteheadWidth + dotGap + d * (dotWidth + dotGap);
                double dotYCenter = noteY + dotYOffset;
                double dotRadius = dotBBox.Height / 2;
                boxes.Add((dotYCenter - dotRadius, dotYCenter + dotRadius, dotX, dotX + dotWidth));
            }
        }

        return HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Right);
    }

    /// <summary>
    /// Creates the left skyline for a music item.
    /// The left skyline represents the leftmost extent at each Y coordinate.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc, lily/accidental-placement.cc
    /// For chords, includes all noteheads and uses AccidentalPlacement for proper
    /// staggered accidental positions.
    /// </remarks>
    public static HorizontalSkyline CreateLeftSkyline(MusicItem item, double referenceX, double staffY)
    {
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();

        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadCenterX = noteheadBBox.CenterX;
        double noteheadLeftX = referenceX - noteheadCenterX;
        double noteheadWidth = noteheadBBox.Width;

        if (item is ChordItem chord)
        {
            // Add all noteheads from the chord
            foreach (var noteInfo in chord.Notes)
            {
                double noteY = staffY - noteInfo.StaffPosition / 2.0;
                double noteheadYBottom = noteY - noteheadBBox.Top;
                double noteheadYTop = noteY - noteheadBBox.Bottom;
                boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));
            }

            // Add accidentals using AccidentalPlacement for proper staggering
            var placement = new AccidentalPlacement();
            var layouts = placement.CalculatePositions(chord.Notes);

            foreach (var layout in layouts)
            {
                var accBBox = GlyphMetrics.GetAccidentalBBox(layout.Accidental);
                double accWidth = accBBox.Width;
                // XOffset is negative (left of notehead), relative to notehead left edge
                double accX = noteheadLeftX + layout.XOffset;

                double noteY = staffY - layout.StaffPosition / 2.0;
                double accYBottom = noteY - accBBox.Top;
                double accYTop = noteY - accBBox.Bottom;
                boxes.Add((accYBottom, accYTop, accX, accX + accWidth));
            }
        }
        else if (item is NoteItem note)
        {
            // Single note
            double noteY = staffY - note.StaffPosition / 2.0;
            double noteheadYBottom = noteY - noteheadBBox.Top;
            double noteheadYTop = noteY - noteheadBBox.Bottom;
            boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));

            // Add accidental if present
            if (note.Accidental != null)
            {
                var placement = new AccidentalPlacement();
                var layout = placement.CalculateSinglePosition(note);
                if (layout.HasValue)
                {
                    var accBBox = GlyphMetrics.GetAccidentalBBox(layout.Value.Accidental);
                    double accWidth = accBBox.Width;
                    double accX = noteheadLeftX + layout.Value.XOffset;

                    double accYBottom = noteY - accBBox.Top;
                    double accYTop = noteY - accBBox.Bottom;
                    boxes.Add((accYBottom, accYTop, accX, accX + accWidth));
                }
            }
        }
        else
        {
            // Rest or other items
            double noteY = staffY;
            double noteheadYBottom = noteY - noteheadBBox.Top;
            double noteheadYTop = noteY - noteheadBBox.Bottom;
            boxes.Add((noteheadYBottom, noteheadYTop, noteheadLeftX, noteheadLeftX + noteheadWidth));
        }

        return HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Left);
    }
    /// <summary>
    /// Calculates the minimum distance between two items using skyline collision detection.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:44-86
    /// Uses skylines to find the actual minimum distance where items don't overlap,
    /// considering the shape of noteheads and accidentals at each Y coordinate.
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

        // Create skylines for both items (at reference X = 0)
        var rightSkyline = CreateRightSkyline(prevItem, 0, staffY);
        var leftSkyline = CreateLeftSkyline(nextItem, 0, staffY);

        // Calculate minimum distance using skyline collision detection
        double skylineDistance = rightSkyline.Distance(leftSkyline);

        // If skylines don't overlap vertically, fall back to simple calculation
        if (double.IsNegativeInfinity(skylineDistance))
        {
            double prevRightExtent = CalculateNoteheadRightExtent(prevItem);
            double nextLeftExtent = CalculateLeftExtent(nextItem);
            return prevRightExtent + nextLeftExtent + MinItemGap;
        }

        // Add minimum gap padding
        return Math.Max(skylineDistance + MinItemGap, MinItemGap);
    }

    /// <summary>
    /// Calculates the right extent from notehead center, excluding stems and flags.
    /// </summary>
    private static double CalculateNoteheadRightExtent(MusicItem item)
    {
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);

        // Right extent from center = width - centerX
        double extent = noteheadBBox.Width - noteheadBBox.CenterX;

        // Add dots if present
        int dots = GetDots(item);
        if (dots > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotWidth = dotBBox.Width;
            double dotGap = EngravingDefaults.DotGap;
            extent += dotGap + dots * dotWidth + (dots - 1) * dotGap;
        }

        return extent;
    }
}
