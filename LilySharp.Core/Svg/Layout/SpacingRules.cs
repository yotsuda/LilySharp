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

    /// <summary>The clearance a bar line reserves to its neighbour OVER its own drawn
    /// stencil. A plain barline reserves <see cref="BarlineWidth"/> (0.8) for a
    /// stencil that is only <see cref="EngravingDefaults.ThinBarlineThickness"/>
    /// (0.19) wide, i.e. ~0.61 of breathing room. Repeat barlines reuse the SAME
    /// clearance, measured from their actual (wider, leftward-dotted) stencil — so
    /// the reservation tracks the glyph instead of a hand-tuned constant, and a
    /// whole rest before a `:|` always clears the dots.
    /// LILYPOND-REF: scm/define-grobs.scm BarLine space-alist — the padding to a
    /// neighbouring note is applied uniformly over the bar line's own X-extent.</summary>
    public const double BarlineClearance = BarlineWidth - EngravingDefaults.ThinBarlineThickness;

    /// <summary>Width of a double or final barline in staff spaces.</summary>
    public const double DoubleBarlineWidth = 1.2;

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
    /// <summary>
    /// Line-start prefix-to-first-note spring (ideal, min) — see
    /// BreakAlignSpacing.FirstNoteSpring.
    /// </summary>
    public static (double Ideal, double Min) FirstNoteSpring(int keySharps, bool includeTimeSignature)
        => BreakAlignSpacing.FirstNoteSpring(Math.Abs(keySharps), includeTimeSignature);

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
        // Repeat barlines reserve their actual drawn stencil plus the same
        // clearance a plain barline gets — the reservation tracks the glyph.
        BarlineType.RepeatStart => EngravingDefaults.BarlineDrawnWidth(type) + BarlineClearance,
        BarlineType.RepeatEnd => EngravingDefaults.BarlineDrawnWidth(type) + BarlineClearance,
        BarlineType.RepeatBoth => EngravingDefaults.BarlineDrawnWidth(type) + BarlineClearance,
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
    internal static double GetClefChangeWidth(ClefType clef) => clef switch
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
    internal static double GetKeySignatureChangeWidth(KeySignatureChangeItem keyChange)
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
    /// Gets the width of a mid-measure time signature change.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/time-signature-engraver.cc — width is the wider of the
    /// numerator / denominator digit stacks.
    /// </remarks>
    internal static double GetTimeSignatureChangeWidth(TimeSignatureChangeItem timeChange) =>
        GlyphMetrics.GetTimeSigWidth(timeChange.NewTime.Beats, timeChange.NewTime.BeatType);

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

        // Time signature change items
        if (item is TimeSignatureChangeItem timeChange)
        {
            double timeWidth = GetTimeSignatureChangeWidth(timeChange);
            return timeWidth / 2.0 + GlyphMetrics.ClefChangePadding;
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
            // Within-chord seconds: a head reversed to the LEFT of the stem
            // (stem down) extends the column's left ink even without
            // accidentals. LILYPOND-REF: lily/stem.cc:606-760.
            double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);
            double minHeadOffset = headOffsets.Min();
            if (minHeadOffset < 0)
                extent = Math.Max(extent, noteheadBBox.CenterX - minHeadOffset);

            // For chords, use AccidentalPlacement to calculate staggered positions
            var placement = new AccidentalPlacement();
            var layouts = placement.CalculatePositions(chord.Notes, headOffsets);

            if (layouts.Length > 0)
            {
                // Find the leftmost accidental position (most negative XOffset)
                // XOffset is negative, representing distance to the left of notehead
                double leftmostOffset = layouts.Min(l => l.XOffset);

                // The leftmost extent is the absolute value of the offset
                extent = Math.Max(extent, Math.Abs(leftmostOffset));
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

        // Time signature change items
        if (item is TimeSignatureChangeItem timeChange)
        {
            double timeWidth = GetTimeSignatureChangeWidth(timeChange);
            return timeWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

        // Get notehead metrics
        int noteValue = GetNoteValue(item);
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);

        // Base extent: from center to right edge of notehead
        double extent = noteheadBBox.Width - noteheadBBox.CenterX;

        // Within-chord seconds: a head reversed to the RIGHT of the stem
        // (stem up) extends the column's right ink by its shift amount.
        // LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
        if (item is ChordItem chord)
        {
            double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);
            double maxHeadOffset = headOffsets.Length > 0 ? headOffsets.Max() : 0;
            if (maxHeadOffset > 0)
                extent += maxHeadOffset;
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
    /// Gets the note value (1=whole, 2=half, 4=quarter, etc.) for a music item.
    /// </summary>
    private static int GetNoteValue(MusicItem item)
    {
        // Clef/key/time change items have zero duration — treat as quarter note for glyph lookup
        if (item is ClefChangeItem or KeySignatureChangeItem or TimeSignatureChangeItem)
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
    /// Calculates stem direction optical correction for spacing ([Wanske] p.138:
    /// up-stem→down-stem needs extra space, down-stem→up-stem less).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction:
    /// - opposite directions → correction scales with the stems' vertical
    ///   OVERLAP: min(|overlap|/7, 1) · leftDir · stem-spacing-correction
    ///   (different_directions_correction, :140-160)
    /// - same direction → only when the head ranges do NOT overlap and the gap
    ///   exceeds one staff position: ±same-direction-correction depending on
    ///   which side is lower (same_direction_correction, :162-197); skipped
    ///   when an accidental sticks out of the right side (:305-308)
    /// Simplifications vs LilyPond (beam membership is not visible at spacing
    /// time): the flagged-unbeamed-left gate (:264-266) and the knee special
    /// case (:289-292) are not applied. Stem directions ARE beam-resolved —
    /// the collector bakes the beam's direction into the items.
    /// </remarks>
    internal static double CalculateStemCorrection(MusicItem? prevItem, MusicItem? nextItem,
                                                   NoteSpacingParameters noteParams)
    {
        if (StemSpacingInfo(prevItem) is not { } l || StemSpacingInfo(nextItem) is not { } r)
            return 0;

        int leftDir = l.StemUp ? 1 : -1;
        int rightDir = r.StemUp ? 1 : -1;

        if (leftDir != rightDir)
        {
            // LILYPOND-REF: note-spacing.cc:140-160 different_directions_correction
            double lo = Math.Max(l.StemMin, r.StemMin);
            double hi = Math.Min(l.StemMax, r.StemMax);
            if (hi <= lo)
                return 0;
            // Overlap in staff positions (half-spaces); 7 is LilyPond's hardcoded scale.
            return Math.Min((hi - lo) / 7.0, 1.0) * leftDir * noteParams.StemSpacingCorrection;
        }

        // LILYPOND-REF: note-spacing.cc:305-308 — same-direction correction only
        // without accidentals sticking out of the right hand side.
        if (HasAccidental(nextItem))
            return 0;

        // LILYPOND-REF: note-spacing.cc:162-197 same_direction_correction —
        // applies only when the two head ranges are disjoint by more than one
        // staff position; sign depends on which side is lower.
        bool headsOverlap = Math.Max(l.HeadMin, r.HeadMin) <= Math.Min(l.HeadMax, r.HeadMax);
        if (headsOverlap)
            return 0;

        int lowest = l.HeadMin > r.HeadMax ? 1 : -1; // +1 = RIGHT side is lower
        double delta = lowest > 0 ? l.HeadMin - r.HeadMax : r.HeadMin - l.HeadMax;
        return delta > 1 ? -lowest * noteParams.SameDirectionCorrection : 0;
    }

    /// <summary>
    /// Merges the per-voice stem-direction spacing wishes for the column pair
    /// (<paramref name="tLeft"/> → <paramref name="tRight"/>) into a single spring.
    /// Each voice with a note/chord column at BOTH moments contributes one wish:
    /// the duration-proportional <paramref name="baseSpring"/> refined by that
    /// voice's stem-direction correction. The wishes are combined with
    /// <see cref="Spring.Merge"/>, exactly as LilyPond merges the simultaneous
    /// voices' spacing wishes for a musical column pair.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:322-393 Spacing_spanner::musical_column_spacing
    ///   — collect each voice's Note_spacing wish, then <c>spring = merge_springs (springs)</c>.
    /// LILYPOND-REF: lily/spring.cc:101-131 merge_springs.
    /// For monophonic music exactly one voice contributes, so the result equals
    /// that single wish (base + its own correction) — identical to applying the
    /// correction directly, which keeps all single-voice spacing unchanged.
    /// </remarks>
    internal static Spring MergeVoiceStemWishes(
        Spring baseSpring, IReadOnlyList<Measure> voices,
        Fraction tLeft, Fraction tRight, NoteSpacingParameters noteParams)
    {
        Spring? merged = null;
        foreach (var voice in voices)
        {
            var left = NoteColumnAt(voice, tLeft);
            var right = NoteColumnAt(voice, tRight);
            if (left is null || right is null)
                continue;

            double corr = CalculateStemCorrection(left, right, noteParams);
            Spring wish = corr != 0
                ? new Spring(
                    Math.Max(baseSpring.MinDistance, baseSpring.IdealDistance + corr),
                    baseSpring.MinDistance,
                    baseSpring.InverseStretchStrength)
                : baseSpring;

            merged = merged is null ? wish : Spring.Merge(merged, wish);
        }
        return merged ?? baseSpring;
    }

    /// <summary>
    /// The note or chord column starting exactly at moment <paramref name="t"/> in
    /// <paramref name="measure"/>, or null if that voice rests (or has no column)
    /// there. Zero-duration change items sharing the moment are skipped.
    /// </summary>
    private static MusicItem? NoteColumnAt(Measure measure, Fraction t)
    {
        var cur = Fraction.Zero;
        foreach (var item in measure.Items)
        {
            if (cur == t && item is NoteItem or ChordItem)
                return item;
            if (cur > t)
                return null;
            cur += item.Duration;
        }
        return null;
    }

    /// <summary>
    /// Stem and head vertical ranges (staff positions, +up) used by the stem
    /// direction correction. Null for stemless items (rests, whole notes).
    /// </summary>
    private static (bool StemUp, double StemMin, double StemMax, double HeadMin, double HeadMax)?
        StemSpacingInfo(MusicItem? item)
    {
        switch (item)
        {
            case NoteItem n:
            {
                int noteValue = n.BaseDuration.Denominator;
                if (n.BaseDuration.Numerator != 1) noteValue = 1;
                if (noteValue < 2)
                    return null; // whole notes have no stem (Stem::is_invisible)
                double endPos = StemEndPosition(n.StaffPosition, n.StemUp, noteValue, n.StaffPosition);
                return (n.StemUp,
                    Math.Min(n.StaffPosition, endPos), Math.Max(n.StaffPosition, endPos),
                    n.StaffPosition, n.StaffPosition);
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                int noteValue = c.BaseDuration.Denominator;
                if (c.BaseDuration.Numerator != 1) noteValue = 1;
                if (noteValue < 2)
                    return null;
                int minPos = c.Notes.Min(x => x.StaffPosition);
                int maxPos = c.Notes.Max(x => x.StaffPosition);
                int tipPos = c.StemUp ? maxPos : minPos;
                double endPos = StemEndPosition(tipPos, c.StemUp, noteValue, tipPos);
                return (c.StemUp,
                    Math.Min(minPos, endPos), Math.Max(maxPos, endPos),
                    minPos, maxPos);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Unbeamed stem end in staff positions (+up), via the LilyPond stem-length
    /// rules (stem.cc internal_calc_stem_end_position).
    /// </summary>
    private static double StemEndPosition(int attachPos, bool stemUp, int noteValue, int staffPosition)
    {
        // StemCalculator works in the renderer's Y-down staff-space frame with
        // the staff middle at staffTopY + 2; use middle = 0 → staffTopY = −2.
        double attachY = -attachPos * 0.5;
        double endY = StemCalculator.CalculateStemEndY(
            attachY, stemUp, staffTopY: -2.0,
            StemCalculator.GetDurationLog(noteValue), staffPosition);
        return -endY * 2.0;
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
    /// LILYPOND-REF: lily/spacing-spanner.cc
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
    /// LILYPOND-REF: lily/spacing-spanner.cc:92-173 calc_common_shortest_duration —
    /// per MEASURE, find the shortest sounding duration; the spacing basis is the
    /// MODE of those per-measure shortests across the piece (ties prefer the
    /// shorter duration), capped at base-shortest-duration (1/8). This keeps one
    /// ornamental 32nd-note run from loosening the whole piece, and keeps
    /// long-note pieces from collapsing to minimal spacing — unlike the absolute
    /// global minimum this method used previously.
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.MultiStaffScore score)
        => CommonShortestDuration(score.AllVoices.Select(v => v.Measures));

    /// <summary>
    /// Calculates the common shortest duration across all voices in a single-staff score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:92-173 calc_common_shortest_duration
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.Score score)
        => CommonShortestDuration(score.Voices.Select(v => v.Measures));

    private static double CommonShortestDuration(IEnumerable<ImmutableArray<Model.Measure>> voiceMeasures)
    {
        var voices = voiceMeasures.ToList();
        int measureCount = voices.Count == 0 ? 0 : voices.Max(m => m.Length);

        // Per-measure shortest across all voices, then count occurrences.
        var counts = new Dictionary<double, int>();
        for (int m = 0; m < measureCount; m++)
        {
            double shortest = double.MaxValue;
            foreach (var measures in voices)
            {
                if (m >= measures.Length)
                    continue;

                // Full-measure rests create no musical columns in LilyPond and
                // therefore never contribute to the common shortest duration.
                if (MultiMeasureRestEngraver.IsFullMeasureRest(measures[m]))
                    continue;

                foreach (var item in measures[m].Items)
                {
                    double dur = item.Duration.ToDouble();
                    // Skip zero-duration items (grace notes, clef changes, etc.)
                    if (dur > 0 && dur < shortest)
                        shortest = dur;
                }
            }

            if (shortest < double.MaxValue)
                counts[shortest] = counts.GetValueOrDefault(shortest) + 1;
        }

        if (counts.Count == 0)
            return EngravingDefaults.BaseShortestDuration;

        // Mode; on equal counts LilyPond prefers the SHORTER duration
        // (spacing-spanner.cc:156-164 — descending scan with >=).
        double mode = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .First().Key;

        // d = min(base-shortest-duration, mode) — spacing-spanner.cc:166-171.
        return Math.Min(EngravingDefaults.BaseShortestDuration, mode);
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

        // LILYPOND-REF: lily/grace-spacing-engraver.cc — use per-group common shortest duration
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
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc — common-shortest-duration per grace sequence
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
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
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

        // LILYPOND-REF: lily/grace-spacing-engraver.cc — per-group common shortest duration
        double bsd = CalculateGraceGroupShortestDuration(notes);

        double totalIdealDistance = 0;

        // Create a spring for each grace note and sum ideal distances
        // LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
        // Grace columns are positioned at ideal distances (not compressed to min)
        for (int i = 0; i < notes.Length; i++)
        {
            var spring = CreateGraceSpring(notes[i].BaseDuration, gp, bsd);
            totalIdealDistance += spring.IdealDistance;
        }

        // LILYPOND-REF: lily/grace-spacing-engraver.cc:65-80
        // Add rod from grace group to main note (junction padding)
        totalIdealDistance += GraceToMainRod;

        // The leftmost grace note's accidental hangs further left of the group's
        // first head, so it sets how far the group protrudes before the main note;
        // reserve it (scaled with the grace head). Without this a grace accidental
        // (e.g. \grace { fis16 }) could overrun the barline or the previous note.
        // LILYPOND-REF: lily/accidental-placement.cc — accidentals reserve left extent.
        if (notes[0].Accidental is { } acc0)
        {
            double accW = GlyphMetrics.GetAccidentalBBox(acc0).Width
                        + GlyphMetrics.AccidentalNoteGap;
            totalIdealDistance += accW * GraceNoteItem.ScaleFactor;
        }

        return totalIdealDistance;
    }

    /// <summary>
    /// Rod distance from last grace note to the main note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc — distance from grace column to main column
    /// </remarks>
    public const double GraceToMainRod = 0.4;

    /// <summary>
    /// Adjusts a spring's MinDistance to accommodate grace notes before the next item.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
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
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
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

    /// <summary>The leading grace notes hanging left of an item's column, if any.</summary>
    private static ImmutableArray<GraceNoteInfo> GraceNotesOf(MusicItem item) => item switch
    {
        NoteItem n => n.LeadingGrace,
        ChordItem c => c.LeadingGrace,
        _ => ImmutableArray<GraceNoteInfo>.Empty
    };

    /// <summary>
    /// If <paramref name="prevItem"/> is a mid-measure clef/key/time change, widens
    /// the following spring by its glyph width so the width ESTIMATE matches the
    /// timing-column layout (which reserves the same via
    /// MeasureLayouter.ChangeItemPrefixWidth). Otherwise returns the spring unchanged.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/paper-column.cc — the breakable change column
    /// precedes the musical column of the same moment.</remarks>
    private static Spring WidenForChangeItem(Spring spring, MusicItem prevItem)
    {
        double w = prevItem switch
        {
            ClefChangeItem cc => GetClefChangeWidth(cc.NewClef) + 2 * GlyphMetrics.ClefChangePadding,
            KeySignatureChangeItem kc => GetKeySignatureChangeWidth(kc) + 2 * GlyphMetrics.ClefChangePadding,
            TimeSignatureChangeItem tc => GetTimeSignatureChangeWidth(tc) + 2 * GlyphMetrics.ClefChangePadding,
            _ => 0
        };
        return w > 0
            ? new Spring(spring.IdealDistance + w, spring.MinDistance + w, spring.InverseStretchStrength)
            : spring;
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
    /// Creates a spring scaled by the shortest currently-playing note duration across all voices.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:107-162 Spacing_spanner::note_spacing
    /// LILYPOND-REF: lily/spacing-engraver.cc:200-253 stop_translation_timestep
    ///
    /// LP's per-column spring formula:
    ///   <c>fraction = delta_t / shortest_playing</c>
    ///   <c>len = options-&gt;get_duration_space(shortest_playing)</c>
    ///   <c>spring = Spring(fraction * len, fraction * min)</c>
    /// where <c>shortest_playing</c> is the min duration over all voices' notes that are
    /// playing at the left column of the spring (NOT just the time delta to the next column).
    /// In monophonic music <c>shortest_playing == delta_t</c> and this collapses to the
    /// existing <see cref="CreateTimingSpring(Fraction, double?, NoteSpacingParameters?)"/>;
    /// in polyphonic music it produces tighter springs when a faster voice is sounding
    /// underneath a slower voice.
    /// </remarks>
    public static Spring CreateTimingSpringMultiVoice(
        Fraction segmentDuration,
        Fraction shortestPlayingDuration,
        double? baseShortestDuration = null,
        NoteSpacingParameters? noteParams = null)
    {
        // LILYPOND-REF: lily/spacing-basic.cc:113-119 — fall back to delta_t when no playing duration is known.
        if (shortestPlayingDuration <= Fraction.Zero)
            shortestPlayingDuration = segmentDuration;
        if (shortestPlayingDuration <= Fraction.Zero)
            return CreateTimingSpring(segmentDuration, baseShortestDuration, noteParams);

        // LILYPOND-REF: lily/spacing-basic.cc:144 — clamp shortest_playing to the segment's
        // actual delta when the latter is shorter (avoids over-shrinking on rests).
        Fraction effectivePlaying = shortestPlayingDuration;
        if (segmentDuration > Fraction.Zero && segmentDuration < effectivePlaying)
            effectivePlaying = segmentDuration;

        double defaultMin = EngravingDefaults.SpacingIncrement;
        double bsd = baseShortestDuration ?? EngravingDefaults.BaseShortestDuration;

        // LILYPOND-REF: lily/spacing-basic.cc:151 — len = get_duration_space(shortest_playing)
        double len = CalculateDurationSpace(effectivePlaying, bsd);
        // LILYPOND-REF: lily/spacing-basic.cc:155-156 — fraction = delta_t / shortest_playing
        double fraction = segmentDuration.ToDouble() / effectivePlaying.ToDouble();

        // LILYPOND-REF: lily/spacing-basic.cc:157 — Spring(fraction * len, fraction * min)
        double idealDistance = Math.Max(fraction * len, defaultMin);
        double minDistance = defaultMin;

        var np = noteParams ?? NoteSpacingParameters.Default;
        if (np.StrictNoteSpacing)
            minDistance = Math.Max(minDistance, idealDistance);

        // LILYPOND-REF: lily/spacing-basic.cc:160-161 — inverse_stretch_strength = fraction * max(0.1, len - min)
        double inverseStretchStrength = Math.Max(0.1, fraction * Math.Max(0.1, len - defaultMin));

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Computes the shortest playing duration across all voices at a given musical timing,
    /// matching LP's <c>shortest-playing-duration</c> column property.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-engraver.cc:200-253 stop_translation_timestep
    /// "playing" = a note that started at or before <paramref name="timing"/> and ends strictly after it.
    /// Returns <c>Fraction.Zero</c> if no voice has a note playing at <paramref name="timing"/>.
    /// </remarks>
    public static Fraction ComputeShortestPlayingAt(Fraction timing, IEnumerable<Measure> allMeasures)
    {
        Fraction shortest = Fraction.Zero;
        bool found = false;

        foreach (var m in allMeasures)
        {
            Fraction t = Fraction.Zero;
            foreach (var item in m.Items)
            {
                Fraction end = t + item.Duration;
                // The note "plays" at `timing` iff t <= timing < end.
                if (t <= timing && timing < end && item.Duration > Fraction.Zero)
                {
                    if (!found || item.Duration < shortest)
                    {
                        shortest = item.Duration;
                        found = true;
                    }
                }
                t = end;
            }
        }

        return shortest;
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

        // LILYPOND-REF: lily/spacing-spanner.cc:200-280
        // Filter out loose items (tuplet brackets, fermata marks, etc.)
        // that don't participate in horizontal spacing
        var spacingItems = new List<MusicItem>();
        foreach (var item in measure.Items)
        {
            if (!item.IsLoose)
                spacingItems.Add(item);
        }

        if (spacingItems.Count == 0)
            return ImmutableArray<Spring>.Empty;

        // LILYPOND-REF: lily/multi-measure-rest.cc:340-391 set_spacing_rods /
        // calculate_spacing_rods — full-measure rests are spaced by a compact
        // rod for the whole run (symbol width + ONE duration space +
        // space-increment·log2(count)), NOT proportionally to the notated
        // whole notes. Per-measure approximation: canonical column widths on
        // both sides of the rest, independent of the global shortest duration.
        if (MultiMeasureRestEngraver.IsFullMeasureRest(measure))
        {
            var rest = spacingItems[0];
            double inc = EngravingDefaults.SpacingIncrement;
            double startMin = Math.Max(inc, CalculateSkylineDistance(null, rest, staffY: 0));
            double endMin = Math.Max(inc, CalculateSkylineDistance(rest, null, staffY: 0));
            return ImmutableArray.Create(
                new Spring(Math.Max(1.25 * inc, startMin), startMin, Math.Max(0.1, 0.25 * inc)),
                new Spring(Math.Max(2.0 * inc, endMin), endMin, Math.Max(0.1, inc)));
        }

        var springs = new List<Spring>();

        // Spring from start barline to first item. Leading grace on the first item
        // hangs left of its column (after the barline), so reserve its width here
        // too — otherwise this width estimate disagrees with the timing-column
        // layout (which reserves it in MeasureLayouter), and line breaking would
        // under-estimate grace measures.
        // LILYPOND-REF: lily/grace-spacing-engraver.cc — grace columns precede the note.
        var firstItem = spacingItems[0];
        var firstSpring = CreateSpring(null, firstItem, Fraction.Quarter,
            baseShortestDuration: baseShortestDuration);
        springs.Add(AdjustSpringForGraceNotes(firstSpring, GraceNotesOf(firstItem)));

        // Springs between items (the spring into a grace-bearing note reserves its
        // grace; the spring after a mid-measure clef/key/time change reserves the
        // change glyph, so this estimate agrees with the timing-column layout —
        // which reserves it via MeasureLayouter.ChangeItemPrefixWidth — and line
        // breaking does not under-estimate change measures).
        for (int i = 0; i < spacingItems.Count - 1; i++)
        {
            var prevItem = spacingItems[i];
            var nextItem = spacingItems[i + 1];
            var spring = CreateSpring(prevItem, nextItem, prevItem.Duration,
                baseShortestDuration: baseShortestDuration);
            spring = AdjustSpringForGraceNotes(spring, GraceNotesOf(nextItem));
            spring = WidenForChangeItem(spring, prevItem);
            springs.Add(spring);
        }

        // Spring from last item to end barline
        var lastItem = spacingItems[^1];
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
        // Reserve any leading grace on the first item (same as the non-lyric
        // estimate path), so a grace + lyric measure is not under-estimated.
        firstSpring = AdjustSpringForGraceNotes(firstSpring, GraceNotesOf(firstItem));
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
            // Reserve grace hanging left of the next item, and the glyph of a
            // mid-measure clef/key/time change (both match the timing path).
            spring = AdjustSpringForGraceNotes(spring, GraceNotesOf(nextItem));
            spring = WidenForChangeItem(spring, prevItem);

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
    /// Widens an EXISTING spring chain so adjacent syllables don't collide.
    /// Unlike <see cref="CreateSpringsForMeasureWithLyrics"/> (which builds item
    /// springs from scratch for the single-staff path), this post-processes the
    /// timing-column springs used by the multi-staff layouter, so a promoted
    /// single-staff score gets the same lyric-driven spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:80-85 skyline-based min_distance.
    /// The spring chain is [start→col0, col0→col1, …, colLast→end]; for a
    /// single-voice measure the timing columns coincide with the note items, so
    /// spring i+1 spans item i → item i+1. When the column count does not match
    /// the item count (extra voices), the mapping breaks down and the chain is
    /// returned unchanged — lyrics are only engraved on single-voice staves.
    /// </remarks>
    public static ImmutableArray<Spring> ApplyLyricSpacing(
        ImmutableArray<Spring> springs,
        Measure measure,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics)
    {
        if (measure.Items.Length == 0 || springs.Length != measure.Items.Length + 1)
            return springs;

        var lyricsByItem = new Dictionary<int, List<LyricItem>>();
        foreach (var lyric in lyrics)
        {
            if (lyric.MeasureIndex != measureIndex)
                continue;
            if (!lyricsByItem.TryGetValue(lyric.ItemIndex, out var list))
                lyricsByItem[lyric.ItemIndex] = list = new List<LyricItem>();
            list.Add(lyric);
        }
        if (lyricsByItem.Count == 0)
            return springs;

        var result = springs.ToBuilder();

        // First spring (start barline → item 0): reserve item 0's left extent.
        if (lyricsByItem.TryGetValue(0, out var firstLyrics))
        {
            var s0 = result[0];
            double adjustedMin = Math.Max(s0.MinDistance, GetLyricLeftExtent(firstLyrics) + MinItemGap);
            result[0] = new Spring(Math.Max(s0.IdealDistance, adjustedMin), adjustedMin, s0.InverseStretchStrength);
        }

        // Between items: spring i+1 spans item i → item i+1.
        for (int i = 0; i < measure.Items.Length - 1; i++)
        {
            double lyricDistance = CalculateLyricDistance(
                lyricsByItem.GetValueOrDefault(i),
                lyricsByItem.GetValueOrDefault(i + 1));
            var spring = result[i + 1];
            if (lyricDistance > spring.MinDistance)
                result[i + 1] = new Spring(
                    Math.Max(spring.IdealDistance, lyricDistance),
                    lyricDistance, spring.InverseStretchStrength);
        }

        // Last spring (item last → end barline): reserve last item's right extent.
        int lastIndex = measure.Items.Length - 1;
        if (lyricsByItem.TryGetValue(lastIndex, out var lastLyrics))
        {
            var sl = result[^1];
            double adjustedMin = Math.Max(sl.MinDistance, GetLyricRightExtent(lastLyrics) + MinItemGap);
            result[^1] = new Spring(Math.Max(sl.IdealDistance, adjustedMin), adjustedMin, sl.InverseStretchStrength);
        }

        return result.ToImmutable();
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
        double maxNoteheadRightX = noteheadLeftX + noteheadWidth;

        if (item is ChordItem chord)
        {
            // LILYPOND-REF: lily/note-column.cc — notehead side assignment for seconds
            // When two notes are a second apart (staff position diff = 1), one notehead
            // shifts right. The right skyline must include these displaced noteheads.
            var displacements = CalculateChordDisplacements(chord.Notes, chord.StemUp, noteheadWidth);

            foreach (var noteInfo in chord.Notes)
            {
                double noteY = staffY - noteInfo.StaffPosition / 2.0;
                double noteheadYBottom = noteY - noteheadBBox.Top;
                double noteheadYTop = noteY - noteheadBBox.Bottom;

                double xOffset = displacements.GetValueOrDefault(noteInfo.StaffPosition, 0);
                double thisLeftX = noteheadLeftX + xOffset;
                double thisRightX = thisLeftX + noteheadWidth;

                boxes.Add((noteheadYBottom, noteheadYTop, thisLeftX, thisRightX));
                maxNoteheadRightX = Math.Max(maxNoteheadRightX, thisRightX);
            }

            // Add dots for chord notes (placed after rightmost notehead)
            int chordDots = GetDots(item);
            if (chordDots > 0)
            {
                var dotBBox = GlyphMetrics.AugmentationDot;
                double dotWidth = dotBBox.Width;
                double dotGap = EngravingDefaults.DotGap;

                foreach (var noteInfo in chord.Notes)
                {
                    double dotYOffset = (noteInfo.StaffPosition % 2 == 0) ? -0.5 : 0;
                    double noteY = staffY - noteInfo.StaffPosition / 2.0;
                    for (int d = 0; d < chordDots; d++)
                    {
                        double dotX = maxNoteheadRightX + dotGap + d * (dotWidth + dotGap);
                        double dotYCenter = noteY + dotYOffset;
                        double dotRadius = dotBBox.Height / 2;
                        boxes.Add((dotYCenter - dotRadius, dotYCenter + dotRadius, dotX, dotX + dotWidth));
                    }
                }
            }
        }
        else
        {
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
                    double dotX = maxNoteheadRightX + dotGap + d * (dotWidth + dotGap);
                    double dotYCenter = noteY + dotYOffset;
                    double dotRadius = dotBBox.Height / 2;
                    boxes.Add((dotYCenter - dotRadius, dotYCenter + dotRadius, dotX, dotX + dotWidth));
                }
            }
        }

        return HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Right);
    }

    /// <summary>
    /// Calculates horizontal displacement offsets for chord noteheads with seconds.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-column.cc — notehead side assignment
    /// When two notes are a second apart (adjacent staff positions), one notehead
    /// shifts to the opposite side of the stem:
    /// - Stem up: lower note of the pair shifts right by noteheadWidth
    /// - Stem down: upper note of the pair shifts right by noteheadWidth
    /// </remarks>
    internal static Dictionary<int, double> CalculateChordDisplacements(
        ImmutableArray<ChordNoteInfo> notes, bool stemUp, double noteheadWidth)
    {
        var offsets = new Dictionary<int, double>();
        if (notes.Length < 2)
            return offsets;

        var sorted = notes.OrderBy(n => n.StaffPosition).Select(n => n.StaffPosition).ToList();
        var shifted = new HashSet<int>();

        if (stemUp)
        {
            // Stem up: lower note of adjacent pair shifts right
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (sorted[i + 1] - sorted[i] == 1)
                {
                    if (!shifted.Contains(sorted[i]))
                    {
                        offsets[sorted[i]] = noteheadWidth;
                        shifted.Add(sorted[i]);
                    }
                    i++; // Skip next to avoid double-shifting in clusters
                }
            }
        }
        else
        {
            // Stem down: upper note of adjacent pair shifts right
            for (int i = sorted.Count - 1; i > 0; i--)
            {
                if (sorted[i] - sorted[i - 1] == 1)
                {
                    if (!shifted.Contains(sorted[i]))
                    {
                        offsets[sorted[i]] = noteheadWidth;
                        shifted.Add(sorted[i]);
                    }
                    i--; // Skip next to avoid double-shifting in clusters
                }
            }
        }

        return offsets;
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
            // Within-chord seconds: reversed heads shift sideways.
            // LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done.
            double[] headOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue);

            // Add all noteheads from the chord (at their real, shifted X)
            for (int i = 0; i < chord.Notes.Length; i++)
            {
                double noteY = staffY - chord.Notes[i].StaffPosition / 2.0;
                double noteheadYBottom = noteY - noteheadBBox.Top;
                double noteheadYTop = noteY - noteheadBBox.Bottom;
                double hx = noteheadLeftX + headOffsets[i];
                boxes.Add((noteheadYBottom, noteheadYTop, hx, hx + noteheadWidth));
            }

            // Add accidentals using AccidentalPlacement for proper staggering
            var placement = new AccidentalPlacement();
            var layouts = placement.CalculatePositions(chord.Notes, headOffsets);

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

            // Arpeggio wavy line hangs to the LEFT of the chord. Reserve its extent
            // (using the SAME constants ArpeggioEngraver places it with) so it does
            // not collide with the barline or the previous note — without this the
            // arpeggio was drawn but never spaced for, like grace notes were.
            // LILYPOND-REF: scm/define-grobs.scm Arpeggio (direction . LEFT),
            //   (X-extent . ly:arpeggio::width) — the grob participates in spacing.
            if (chord.HasArpeggio && chord.Notes.Length > 0)
            {
                double arpRight = noteheadLeftX - ArpeggioEngraver.Padding;
                double arpLeft = arpRight - 2 * ArpeggioEngraver.WaveAmplitude;
                int maxPos = chord.Notes.Max(n => n.StaffPosition);
                int minPos = chord.Notes.Min(n => n.StaffPosition);
                double arpYBottom = (staffY - maxPos / 2.0) - ArpeggioEngraver.Protrusion;
                double arpYTop = (staffY - minPos / 2.0) + ArpeggioEngraver.Protrusion;
                boxes.Add((arpYBottom, arpYTop, arpLeft, arpRight));
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
            ClefChangeItem => 1.0,             // (clef . (extra-space . 1.0))
            KeySignatureChangeItem => 1.0,     // (key-signature . (extra-space . 1.0))
            TimeSignatureChangeItem => 0.75,   // (time-signature . (extra-space . 0.75))
            _ when isFirstInMeasure => 1.3,    // (first-note . (semi-shrink-space . 1.3))
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
            TimeSignatureChangeItem => 1.0,
            _ => BarlinePadding
        };
    }

    public static double CalculateSkylineDistance(MusicItem? prevItem, MusicItem? nextItem,
                                                   double staffY,
                                                   NoteSpacingParameters? noteParams = null)
    {
        // LILYPOND-REF: scm/define-grobs.scm — skyline-horizontal-padding (LP default 0.1).
        // LilySharp historically used GlyphMetrics.MinItemGap (0.4) as the static
        // constant; the parameter override path lets callers tune it down for
        // tighter LP-style proportional spacing.
        double minItemGap = noteParams?.MinItemGap ?? MinItemGap;

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
                return BarlinePadding * 2 + minItemGap;
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
            return prevRightExtent + nextLeftExtent + minItemGap;
        }

        // Add minimum gap padding
        return Math.Max(skylineDistance + minItemGap, minItemGap);
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

        if (item is TimeSignatureChangeItem timeChange)
        {
            double timeWidth = GetTimeSignatureChangeWidth(timeChange);
            return timeWidth / 2.0 + GlyphMetrics.ClefChangePadding;
        }

        int noteValue = GetNoteValue(item);

        // A rest is drawn glyph-left-aligned at its column X (DrawRest: DrawGlyph at x),
        // so its right reach from the column origin is the rest glyph's right edge —
        // wide for a whole/half rest. Using the (smaller) notehead box here let a whole
        // rest's glyph collide with the following barline. LILYPOND-REF: lily/rest.cc
        // Rest::width / generic_extent_callback — the rest stencil's own X-extent feeds
        // the column skyline / separation.
        double extent;
        if (item is RestItem)
        {
            extent = GlyphMetrics.GetRestBBox(noteValue).Right;
        }
        else
        {
            var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
            // Right extent from center = width - centerX
            extent = noteheadBBox.Width - noteheadBBox.CenterX;
        }

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
