// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
    public static double CalculateMeasureIdealWidth(Measure measure,
                                                    double? baseShortestDuration = null)
    {
        double width = 0;

        // Barline widths
        width += GetBarlineWidth(measure.StartBarline);
        width += GetBarlineWidth(measure.EndBarline);

        // Spring ideal distances (content area) - includes duration space
        if (measure.Items.Length > 0)
        {
            var springs = CreateSpringsForMeasure(measure, baseShortestDuration);
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
    /// LILYPOND-REF: lily/break-alignment-interface.cc
    /// LILYPOND-REF: scm/define-grobs.scm break-align-orders, Clef/KeySignature/TimeSignature space-alist
    ///
    /// Delegates to BreakAlignSpacing which implements LP's break-alignment-interface
    /// with space-alist lookups and break-align-orders for correct element ordering.
    /// Uses G-clef width as default; for other clefs, use the overload with clefWidth.
    /// </remarks>
    public static double CalculatePrefixWidth(int keySharps, bool includeTimeSignature,
        int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth,
            Math.Abs(keySharps), keySharps > 0,
            includeTimeSignature, timeSigBeats, timeSigBeatType);
    }

    /// <summary>
    /// Calculates the width of system prefix with explicit clef width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc
    /// Use this overload when the clef type is known for accurate spacing.
    /// </remarks>
    public static double CalculatePrefixWidth(double clefWidth, int keySharps,
        bool includeTimeSignature, int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        return BreakAlignSpacing.CalculatePrefixWidth(
            clefWidth,
            Math.Abs(keySharps), keySharps > 0,
            includeTimeSignature, timeSigBeats, timeSigBeatType);
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

    private static bool HasAccidental(MusicItem? item)
    {
        return item switch
        {
            NoteItem note => note.Accidental != null,
            ChordItem chord => chord.Notes.Any(n => n.Accidental != null),
            _ => false
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

    /// <summary>
    /// Gets the width of a change (mid-measure) clef glyph.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/clef.cc:29-52 — "_change" suffix glyphs are smaller variants.
    /// </remarks>
    private static double GetClefChangeWidth(ClefType clef) => clef switch
    {
        ClefType.Bass => GlyphMetrics.FClefChangeWidth,
        ClefType.Alto or ClefType.Tenor => GlyphMetrics.CClefChangeWidth,
        _ => GlyphMetrics.GClefChangeWidth
    };

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
    /// Gets the width of a mid-measure key signature change.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc — key signature width depends on accidental count.
    /// Includes cancellation naturals from previous key.
    /// </remarks>
    private static double GetKeySignatureChangeWidth(KeySignatureChangeItem keyChange)
    {
        double width = 0;

        // Cancellation naturals (from previous key)
        int prevCount = keyChange.PreviousKey.Count;
        int newCount = keyChange.NewKey.Count;
        bool sameType = (keyChange.PreviousKey.IsSharps == keyChange.NewKey.IsSharps) ||
                        keyChange.PreviousKey.Sharps == 0 || keyChange.NewKey.Sharps == 0;

        // LILYPOND-REF: lily/key-engraver.cc:67-125 — cancellation logic
        if (!sameType && prevCount > 0)
        {
            // Different type (sharps→flats or flats→sharps): cancel all previous
            width += prevCount * GlyphMetrics.KeySignatureNaturalWidth;
        }
        else if (sameType && prevCount > newCount && keyChange.PreviousKey.Sharps != 0)
        {
            // Same type but fewer: cancel the difference
            width += (prevCount - newCount) * GlyphMetrics.KeySignatureNaturalWidth;
        }

        // New key accidentals
        if (newCount > 0)
        {
            width += newCount * GlyphMetrics.GetKeySignatureAccidentalWidth(keyChange.NewKey.IsSharps);
        }

        return Math.Max(width, GlyphMetrics.KeySignatureNaturalWidth); // minimum width
    }

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
        // Clef change items use their own width calculation
        // LILYPOND-REF: lily/clef.cc — change clefs are smaller variants
        if (item is ClefChangeItem clefChange)
        {
            double clefWidth = GetClefChangeWidth(clefChange.NewClef);
            return clefWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

        // Key signature change items
        if (item is KeySignatureChangeItem keyChange)
        {
            double keyWidth = GetKeySignatureChangeWidth(keyChange);
            return keyWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

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
        // Clef change items use their own width calculation
        if (item is ClefChangeItem clefChange)
        {
            double clefWidth = GetClefChangeWidth(clefChange.NewClef);
            return clefWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

        // Key signature change items
        if (item is KeySignatureChangeItem keyChange)
        {
            double keyWidth = GetKeySignatureChangeWidth(keyChange);
            return keyWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

        // Get notehead metrics
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);

        // Base extent: from center to right edge of notehead
        double extent = noteheadBBox.Width - noteheadBBox.CenterX;

        // LILYPOND-REF: lily/note-column.cc:169-220 calc_main_extent
        // For stem-up chords with seconds, a suspended head extends right of stem.
        // The main extent excludes this suspended head.
        if (item is ChordItem chord && HasSuspendedHead(chord))
        {
            bool stemUp = chord.StemUp;
            if (stemUp)
            {
                // Stem-up: suspended head is right of stem, main extent is just the normal side
                // Don't add the suspended head width to right extent
                // (the base extent already accounts for one notehead)
            }
            else
            {
                // Stem-down: suspended head is left of stem, right extent needs the extra notehead
                extent += noteheadBBox.Width;
            }
        }

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
    /// Determines if a chord has a suspended notehead (shifted to the opposite side of the stem
    /// due to a second interval).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-column.cc:169-220 calc_main_extent
    /// A chord has a suspended head when two adjacent notes are a second apart
    /// (staff position difference of 1). The note on the opposite side of the stem is "suspended".
    /// </remarks>
    internal static bool HasSuspendedHead(ChordItem chord)
    {
        if (chord.Notes.Length < 2)
            return false;

        var positions = chord.Notes.Select(n => n.StaffPosition).OrderBy(p => p).ToArray();
        for (int i = 0; i < positions.Length - 1; i++)
        {
            if (positions[i + 1] - positions[i] == 1)
                return true;  // Second interval found
        }
        return false;
    }

    /// <summary>
    /// Gets the note value (1=whole, 2=half, 4=quarter, etc.) for a music item.
    /// </summary>
    private static int GetNoteValue(MusicItem item)
    {
        // Clef/key change items have zero duration — treat as quarter note for glyph lookup
        if (item is ClefChangeItem or KeySignatureChangeItem)
            return 4;
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
                                      NoteSpacingParameters? noteParams = null,
                                      double? baseShortestDuration = null)
    {
        var np = noteParams ?? NoteSpacingParameters.Default;

        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // Skyline-based collision distance (rod)
        double skylineDistance = CalculateSkylineDistance(prevItem, nextItem, staffY: 0);

        // min_distance = max(defaultMin, skylineDistance) - ensures no collision
        double minDistance = Math.Max(defaultMin, skylineDistance);

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(prevDuration,
            baseShortestDuration ?? EngravingDefaults.BaseShortestDuration);

        // --- Stem direction optical correction ---
        // LILYPOND-REF: lily/note-spacing.cc:119-199 stem_dir_correction
        idealDistance += CalculateStemCorrection(prevItem, nextItem, np);

        // LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
        // In strict mode, enforce minimum distance = duration-based ideal distance.
        // This prevents compression below proportional spacing.
        if (np.StrictNoteSpacing)
        {
            minDistance = Math.Max(minDistance, idealDistance);
        }

        // LILYPOND-REF: lily/spacing-basic.cc note_spacing()
        //   ret.set_inverse_stretch_strength(fraction * std::max(0.1, (len - min)));
        // where min = increment_ (NOT skyline min_distance).
        // Skyline min_distance is set later via set_min_distance() but does NOT
        // affect inverse_stretch_strength. This ensures accidentals (which increase
        // skyline min_distance) don't make springs stiffer — they stretch equally.
        double inverseStretchStrength = Math.Max(0.1, idealDistance - defaultMin);

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
            // LILYPOND-REF: note-spacing.cc:305-310
            // Only apply same direction correction if there are no
            // accidentals sticking out of the right hand side.
            if (HasAccidental(nextItem))
                return 0;
            return -noteParams.SameDirectionCorrection * increment * 0.5;
        }
    }

    /// <summary>
    /// Calculates the duration-based space using the global default base shortest duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-options.cc:68-104 get_duration_space()
    /// Uses EngravingDefaults.BaseShortestDuration (1/8). For score-specific spacing,
    /// use the overload that accepts a baseShortestDuration parameter from
    /// CalculateCommonShortestDuration().
    /// </remarks>
    public static double CalculateDurationSpace(Fraction duration)
    {
        return CalculateDurationSpace(duration, EngravingDefaults.BaseShortestDuration);
    }

    /// <summary>
    /// Calculates the duration-based space with a specific base shortest duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-options.cc:68-104 get_duration_space()
    /// LILYPOND-REF: lily/spacing-determine-shortest-duration-op.cc
    /// - ratio = duration / base_shortest_duration
    /// - if ratio less than 1: space = (shortest_duration_space + ratio - 1) * increment
    /// - if ratio >= 1: space = (shortest_duration_space + log2(ratio)) * increment
    ///
    /// The baseShortestDuration should come from CalculateCommonShortestDuration()
    /// which scans all voices to find the actual shortest note in the score.
    /// </remarks>
    public static double CalculateDurationSpace(Fraction duration, double baseShortestDuration)
    {
        double durationValue = duration.ToDouble();

        if (durationValue <= 0)
            return EngravingDefaults.SpacingIncrement;

        // Ratio of this duration to base shortest
        double ratio = durationValue / baseShortestDuration;

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
    /// Calculates the common shortest duration across all voices in a multi-staff score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-determine-shortest-duration-op.cc
    ///
    /// LilyPond determines the base shortest duration dynamically by scanning all
    /// musical columns in the score. The common shortest duration is used as the
    /// reference point for the Gourlay spacing algorithm: durations shorter than
    /// this get linear spacing, durations longer get logarithmic spacing.
    ///
    /// This ensures that a score with only quarter and half notes spaces differently
    /// from a score that also contains sixteenth notes.
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.MultiStaffScore score)
    {
        double shortest = double.MaxValue;

        foreach (var voice in score.AllVoices)
        {
            double voiceShortest = FindShortestDurationInMeasures(voice.Measures);
            if (voiceShortest < shortest)
                shortest = voiceShortest;
        }

        // Fall back to default if no notes found
        return shortest < double.MaxValue ? shortest : EngravingDefaults.BaseShortestDuration;
    }

    /// <summary>
    /// Calculates the common shortest duration across all voices in a single-staff score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-determine-shortest-duration-op.cc
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.Score score)
    {
        double shortest = double.MaxValue;

        foreach (var voice in score.Voices)
        {
            double voiceShortest = FindShortestDurationInMeasures(voice.Measures);
            if (voiceShortest < shortest)
                shortest = voiceShortest;
        }

        return shortest < double.MaxValue ? shortest : EngravingDefaults.BaseShortestDuration;
    }

    /// <summary>
    /// Finds the shortest note/rest duration in a sequence of measures.
    /// </summary>
    private static double FindShortestDurationInMeasures(ImmutableArray<Model.Measure> measures)
    {
        double shortest = double.MaxValue;

        foreach (var measure in measures)
        {
            foreach (var item in measure.Items)
            {
                double dur = item.Duration.ToDouble();
                // Skip zero-duration items (grace notes, clef changes, etc.)
                if (dur > 0 && dur < shortest)
                    shortest = dur;
            }
        }

        return shortest;
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
                                            GraceSpacingParameters? graceParams = null,
                                            double? baseShortestDuration = null)
    {
        var gp = graceParams ?? GraceSpacingParameters.Default;

        double durationValue = graceDuration.ToDouble();
        if (durationValue <= 0)
            durationValue = gp.BaseShortestDuration;

        // LILYPOND-REF: lily/grace-spacing.cc — use per-group common shortest duration
        double bsd = baseShortestDuration ?? gp.BaseShortestDuration;

        // Same Gourlay formula as regular notes, but with grace parameters
        double ratio = durationValue / bsd;
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
    /// Calculates the common shortest duration within a grace note group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing.cc — common-shortest-duration per grace sequence
    /// Each grace group independently determines its base shortest duration,
    /// rather than using a global default. This ensures that a group of sixteenth
    /// grace notes spaces differently from a group of eighth grace notes.
    /// </remarks>
    public static double CalculateGraceGroupShortestDuration(
        ImmutableArray<GraceNoteInfo> notes)
    {
        double shortest = double.MaxValue;

        foreach (var note in notes)
        {
            double dur = note.BaseDuration.ToDouble();
            if (dur > 0 && dur < shortest)
                shortest = dur;
        }

        // Fall back to default grace duration (eighth note)
        return shortest < double.MaxValue
            ? shortest
            : GraceSpacingParameters.Default.BaseShortestDuration;
    }

    /// <summary>
    /// Calculates the total width of a grace note group using spring-based spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing.cc:36-80 Grace_spacing::calc_springs
    /// Creates individual springs for each grace note using per-group common shortest
    /// duration, then sums the minimum distances to get the rod (minimum width).
    /// The grace→main junction adds GraceToMainRod padding.
    ///
    /// This replaces the fixed-width calculation in GetGraceGroupWidth with a
    /// LP-compliant spring-based approach.
    /// </remarks>
    public static double CalculateGraceGroupSpringWidth(
        ImmutableArray<GraceNoteInfo> notes,
        GraceSpacingParameters? graceParams = null)
    {
        if (notes.IsDefaultOrEmpty)
            return 0;

        var gp = graceParams ?? GraceSpacingParameters.Default;

        // LILYPOND-REF: lily/grace-spacing.cc — per-group common shortest duration
        double bsd = CalculateGraceGroupShortestDuration(notes);

        double totalIdealDistance = 0;

        // Create a spring for each grace note and sum ideal distances
        // LILYPOND-REF: lily/grace-spacing.cc:36-80 Grace_spacing::calc_springs
        // Grace columns are positioned at ideal distances (not compressed to min)
        for (int i = 0; i < notes.Length; i++)
        {
            var spring = CreateGraceSpring(notes[i].BaseDuration, gp, bsd);
            totalIdealDistance += spring.IdealDistance;
        }

        // LILYPOND-REF: lily/grace-spacing.cc:65-80
        // Add rod from grace group to main note (junction padding)
        totalIdealDistance += GraceToMainRod;

        return totalIdealDistance;
    }

    /// <summary>
    /// Rod distance from last grace note to the main note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing.cc — distance from grace column to main column
    /// </remarks>
    public const double GraceToMainRod = 0.4;

    /// <summary>
    /// Adjusts a spring's MinDistance to accommodate grace notes before the next item.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing.cc:36-80 Grace_spacing::calc_springs
    /// Uses spring-based grace group width when note info is available,
    /// falls back to fixed-width calculation for backward compatibility.
    /// </remarks>
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
    /// Adjusts a spring's MinDistance using spring-based grace note width calculation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing.cc:36-80 Grace_spacing::calc_springs
    /// Uses per-group common shortest duration and individual grace springs
    /// to calculate the rod (minimum distance) more accurately than fixed widths.
    /// </remarks>
    public static Spring AdjustSpringForGraceNotes(Spring spring,
        ImmutableArray<GraceNoteInfo> graceNotes,
        GraceSpacingParameters? graceParams = null)
    {
        if (graceNotes.IsDefaultOrEmpty)
            return spring;

        double graceWidth = CalculateGraceGroupSpringWidth(graceNotes, graceParams);
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
    public static Spring CreateTimingSpring(Fraction duration,
                                            double? baseShortestDuration = null,
                                            NoteSpacingParameters? noteParams = null)
    {
        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(duration,
            baseShortestDuration ?? EngravingDefaults.BaseShortestDuration);

        // Ensure minimum distance
        idealDistance = Math.Max(idealDistance, defaultMin);

        // min_distance for timing springs (no skyline collision)
        double minDistance = defaultMin;

        // LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
        // In strict mode, enforce minimum distance = ideal distance for proportional spacing
        var np = noteParams ?? NoteSpacingParameters.Default;
        if (np.StrictNoteSpacing)
        {
            minDistance = Math.Max(minDistance, idealDistance);
        }

        // LILYPOND-REF: lily/spacing-basic.cc:115 note_spacing() - inverse_stretch
        double inverseStretchStrength = Math.Max(0.1, idealDistance - defaultMin);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }


    /// <summary>
    /// Creates all springs for a measure.
    /// </summary>
    /// <param name="measure">The measure to create springs for</param>
    /// <returns>Array of springs (one between each pair of adjacent reference points)</returns>
    public static ImmutableArray<Spring> CreateSpringsForMeasure(Measure measure,
                                                                 double? baseShortestDuration = null)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<Spring>.Empty;

        var springs = new List<Spring>();

        // Spring from start barline to first item
        var firstItem = measure.Items[0];
        springs.Add(CreateSpring(null, firstItem, Fraction.Quarter,
            baseShortestDuration: baseShortestDuration));

        // Springs between items
        for (int i = 0; i < measure.Items.Length - 1; i++)
        {
            var prevItem = measure.Items[i];
            var nextItem = measure.Items[i + 1];
            springs.Add(CreateSpring(prevItem, nextItem, prevItem.Duration,
                baseShortestDuration: baseShortestDuration));
        }

        // Spring from last item to end barline
        var lastItem = measure.Items[^1];
        springs.Add(CreateSpring(lastItem, null, lastItem.Duration,
            baseShortestDuration: baseShortestDuration));

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
        IReadOnlyList<LyricItem> lyrics,
        double? baseShortestDuration = null)
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
        var firstSpring = CreateSpring(null, firstItem, Fraction.Quarter,
            baseShortestDuration: baseShortestDuration);
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
            var spring = CreateSpring(prevItem, nextItem, prevItem.Duration,
                baseShortestDuration: baseShortestDuration);

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
        var lastSpring = CreateSpring(lastItem, null, lastItem.Duration,
            baseShortestDuration: baseShortestDuration);
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
    /// <summary>
    /// Gets the space from a barline to the next item, based on item type.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarLine.space-alist
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    ///
    /// Different item types get different amounts of space after a barline:
    ///   first-note:     semi-shrink-space 1.3 (can shrink slightly)
    ///   next-note:      semi-fixed-space  0.9 (mostly fixed)
    ///   clef:           extra-space       1.0
    ///   key-signature:  extra-space       1.0
    ///   time-signature: extra-space       0.75
    /// </remarks>
    public static double GetBarlineToItemSpace(MusicItem? nextItem, bool isFirstInMeasure = true)
    {
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist
        return nextItem switch
        {
            ClefChangeItem => 1.0,           // (clef . (extra-space . 1.0))
            KeySignatureChangeItem => 1.0,   // (key-signature . (extra-space . 1.0))
            _ when isFirstInMeasure => 1.3,  // (first-note . (semi-shrink-space . 1.3))
            _ => 0.9                         // (next-note . (semi-fixed-space . 0.9))
        };
    }

    /// <summary>
    /// Gets the space from the last item in a measure to the barline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70
    /// The distance from item to barline uses BarlinePadding for normal notes,
    /// with extra space for non-musical items.
    /// </remarks>
    public static double GetItemToBarlineSpace(MusicItem? prevItem)
    {
        return prevItem switch
        {
            ClefChangeItem => 1.0,
            KeySignatureChangeItem => 1.0,
            _ => BarlinePadding
        };
    }

    public static double CalculateSkylineDistance(MusicItem? prevItem, MusicItem? nextItem,
                                                   double staffY)
    {
        // For barline-to-item or item-to-barline, use LP space-alist based calculation
        // LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
        if (prevItem == null || nextItem == null)
        {
            if (prevItem == null && nextItem != null)
            {
                // Barline → item: use space-alist padding based on item type
                double barlinePad = GetBarlineToItemSpace(nextItem);
                double itemExtent = CalculateLeftExtent(nextItem);
                return barlinePad + itemExtent;
            }
            else if (prevItem != null && nextItem == null)
            {
                // Item → barline: use type-aware barline padding
                double itemExtent = CalculateNoteheadRightExtent(prevItem);
                double barlinePad = GetItemToBarlineSpace(prevItem);
                return itemExtent + barlinePad;
            }
            else
            {
                // Both null (shouldn't happen): return default
                return BarlinePadding * 2 + MinItemGap;
            }
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
        if (item is ClefChangeItem clefChange)
        {
            double clefWidth = GetClefChangeWidth(clefChange.NewClef);
            return clefWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

        if (item is KeySignatureChangeItem keyChange)
        {
            double keyWidth = GetKeySignatureChangeWidth(keyChange);
            return keyWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

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
