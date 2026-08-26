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

internal static partial class SpacingRules
{
    /// <summary>
    /// Calculates the duration-based space using the global default base shortest duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-options.cc:72-107 get_duration_space()
    /// Uses EngravingDefaults.BaseShortestDuration (3/16). For score-specific spacing,
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
    /// LILYPOND-REF: lily/spacing-options.cc:72-107 get_duration_space()
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

        // LILYPOND-REF: lily/spacing-options.cc:72-107 get_duration_space()
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

    // ---------- Multi-measure rest: LilyPond's run-level spacing rod ----------

    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2375 MultiMeasureRest (space-increment . 2.0).</remarks>
    private const double MmrSpaceIncrement = 2.0;

    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2370 MultiMeasureRest (bound-padding . 0.5).</remarks>
    private const double MmrBoundPadding = 0.5;

    /// <summary>
    /// Width of the multi-measure rest symbol at zero available space — LilyPond's
    /// <c>symbol_stencil (me, 0.0)</c>, the value its spacing rod is built from.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest.cc:166-189 Multi_measure_rest::symbol_stencil
    /// LILYPOND-REF: lily/multi-measure-rest.cc:226-329 Multi_measure_rest::church_rest
    ///
    /// church_rest with <c>space == 0</c>: <c>inner_padding = (space - symbols_width) /
    /// (2*1.5 + (symbol_count-1))</c> goes negative, so the guard resets it to 1.0 (and
    /// min() against max-symbol-separation 8.0 leaves it at 1.0). The stencil is then
    /// <c>symbols_width + inner_padding * (symbol_count - 1)</c>; left_offset only
    /// translates. Verified against LP: measure-count 2 → one breve rest, 0.600.
    ///
    /// The decomposition mirrors <see cref="Rendering.SharedRenderer"/>'s church rest
    /// so rod and drawing agree. It walks maxima 8 / longa 4 / breve 2 / whole 1,
    /// which is church_rest's loop: <c>dl</c> starts at -3 and only ever increases,
    /// emitting <c>2^-dl</c> measures while the remainder still covers it. With
    /// expand-limit 10 the maxima can only appear at counts 8, 9 and 10 —
    /// 8 = maxima, 9 = maxima + whole, 10 = maxima + breve. Decomposing those into
    /// longas instead (4+4, 4+4+1, 4+4+2) spent one glyph too many and made the rod
    /// 0.4 ss too wide.
    /// </remarks>
    internal static double MmrSymbolWidth(int measureCount)
    {
        if (measureCount <= 0)
            return 0;

        if (measureCount > MultiMeasureRestEngraver.ExpandLimit)
        {
            // LILYPOND-REF: lily/multi-measure-rest.cc:194-215 big_rest (me, 0.0) —
            // the filled box collapses to zero width and only the two hair-thickness
            // end caps remain.
            return 2 * EngravingDefaults.MultiMeasureRestHairThickness;
        }

        double symbolsWidth = 0;
        int symbolCount = 0;
        int remaining = measureCount;
        foreach (var (span, width) in new[]
        {
            (8, GlyphMetrics.RestMaximaWidth),
            (4, GlyphMetrics.RestLonga.Width),
            (2, GlyphMetrics.RestDoubleWhole.Width),
            (1, GlyphMetrics.RestWhole.Width),
        })
        {
            while (remaining >= span)
            {
                symbolsWidth += width;
                symbolCount++;
                remaining -= span;
            }
        }

        // inner_padding == 1.0 at space == 0 (see remarks).
        return symbolsWidth + (symbolCount - 1);
    }

    // Staff-line Y-extent (positions -4..4 -> -2..2 ss). Every break-aligned grob
    // below carries extra-spacing-height that reaches the staff, so giving each box
    // this same Y makes them all overlap — see MmrRodMinimumDistance's remarks.
    private const double StaffYBottom = -2.0;
    private const double StaffYTop = 2.0;

    /// <summary>
    /// The bar line drawn at a multi-measure-rest run's LEFT bound. Lily# owns an internal
    /// boundary's bar line on the LEFT measure's <see cref="Measure.EndBarline"/> (the right
    /// measure's <see cref="Measure.StartBarline"/> is <see cref="BarlineType.None"/> to
    /// avoid double-drawing), so fall back to the previous measure's end when the run
    /// measure declares no start bar line of its own. This is the width LilyPond's left
    /// bounding <c>NonMusicalPaperColumn</c> reaches with (see <see cref="MmrRodMinimumDistance"/>).
    /// </summary>
    internal static BarlineType RunLeftBoundBarline(
        IReadOnlyList<Measure> measures, int runStart)
    {
        BarlineType start = measures[runStart].StartBarline;
        if (start != BarlineType.None)
            return start;
        return runStart > 0 ? measures[runStart - 1].EndBarline : BarlineType.None;
    }

    /// <summary>
    /// Room a clef change at the START of <paramref name="nextMeasure"/> needs to the
    /// LEFT of the bar line separating it from the measure before — zero when that
    /// measure opens with no clef change.
    /// </summary>
    /// <remarks>
    /// LilyPond engraves a mid-line clef change BEFORE the bar line: the unbroken
    /// break-align order is <c>… clef, cue-clef, staff-bar, key-cancellation,
    /// key-signature, time-signature …</c> (scm/define-grobs.scm:650-664). A key or time
    /// change therefore rides the spring AFTER the bar line, but a clef takes space
    /// BEFORE it — which is the preceding measure's last-item → bar line minimum.
    ///
    /// The amount is the boundary column's own geometry, so it is read off
    /// <see cref="BoundaryColumn.BarLineLeft"/> rather than recomputed: the clef's width
    /// plus its <c>Clef.space-alist (staff-bar . (extra-space . 0.7))</c>. Measured
    /// 2.84668 for a bass change clef on LilyPond 2.24.4.
    ///
    /// This is added to the EXISTING item → bar line minimum rather than replacing it.
    /// LilyPond's own minimum is <c>padding + skyline distance</c> (spacing-spanner.cc:315
    /// → separation-item.cc:48-68), which Lily# does not yet use for that pair; swapping
    /// it in moves every measure and is a separate step. Adding the clef's allowance
    /// leaves every clef-less boundary untouched.
    /// </remarks>
    /// <remarks>
    /// ⚠️ THE CLEF-LESS BOUNDARY IS ANSWERED WITHOUT BUILDING THE COLUMN, and
    /// <see cref="BoundaryColumn.OpensWithClefChange"/> carries the proof that 0 is the
    /// column's own answer there rather than an approximation of it. It matters because of
    /// how often this is asked: <c>MeasureContentKey.Compute</c> asks it once per measure
    /// per staff on every keystroke, and a 1000-bar book with no clef change anywhere was
    /// building 1000 columns — a candidate list, a placement list, a placed list and an
    /// immutable builder each — to read one nullable that was going to be 0.
    /// MEASURED (session 193): 0.93 MB of a 46.1 MB perf-plain1k keystroke.
    /// </remarks>
    internal static double BoundaryClefAllowance(BarlineType barline, Measure? nextMeasure)
        => nextMeasure == null || !BoundaryColumn.OpensWithClefChange(nextMeasure.Items)
            ? 0
            : BoundaryColumn.Build(barline, nextMeasure.Items).BarLineLeft ?? 0;

    /// <summary>
    /// <c>Paper_column::minimum_distance</c> between the two paper columns bounding a
    /// multi-measure-rest run: a genuine <see cref="HorizontalSkyline"/> distance over
    /// the break-aligned grobs on each bounding column, so a key / time change sitting
    /// at the bound reserves its own width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc:144-164 Paper_column::minimum_distance —
    /// <c>max (0.0, skys[LEFT].distance (skys[RIGHT]))</c>, where <c>skys[LEFT]</c> is the
    /// LEFT column's RIGHT skyline and <c>skys[RIGHT]</c> the RIGHT column's LEFT skyline.
    /// Each skyline is built by lily/separation-item.cc:120-190 boxes(): every grob adds a
    /// Box whose X is <c>extent + extra-spacing-width</c> and whose Y is
    /// <c>pure_y_extent + extra-spacing-height</c> (defaults <c>(-0.1 . 0.1)</c> /
    /// <c>(0 . 0)</c>, separation-item.cc:166-169).
    ///
    /// Every break-aligned grob here carries an extra-spacing-height that INCLUDES the
    /// staff (pure-from-neighbor-interface::extra-spacing-height-including-staff,
    /// scm/define-grobs.scm) and the bar line spans the staff, so every box on one column
    /// overlaps every box on the other in Y. The distance therefore equals the horizontal
    /// reach difference; it is still expressed as boxes + a real
    /// <c>HorizontalSkyline.Distance</c> so the mechanism — and any future
    /// non-overlapping case — is exactly LilyPond's. For the same reason the box Y is set
    /// to the staff extent (the exact esh magnitude never changes which pairs overlap).
    ///
    /// Column-internal geometry, measured on LilyPond 2.24.4 (bar line left edge at the
    /// column origin, drawn width bw; break-alignment places changes AFTER it,
    /// lily/break-alignment-interface.cc: placed-left = prev.right + space):
    ///   KeySignature: left = bw + 1.0 (space-alist key←staff-bar 1.1, observed edge gap
    ///     1.0), extra-spacing-width (0.0 . 1.0). A `\key g \major` R1*5 run then reaches
    ///     0.19 + 1.0 + 1.1 + 1.0 = 3.29, min_dist 3.29 − (−0.1) = 3.390 — matching LilyPond,
    ///     where the old bw + 0.2 closed form returned 0.390 (the run came out ~3.0 ss narrow).
    ///   TimeSignature: left = bw + 0.75 (space-alist 1.0, observed 0.75), or, when a key
    ///     change precedes it on the same column, keysig.right + 1.15; esw (0.0 . 0.8).
    /// The bar line itself reaches bw + 0.1 (its default esw right, separation-item.cc:167).
    /// A leading key change folds any cancellation into <see cref="GetKeySignatureChangeWidth"/>,
    /// which matches LilyPond's KeyCancellation+KeySignature pair for the common cases (a
    /// pure new key, or a pure cancellation to C); a key TYPE change (flats↔sharps) at the
    /// bound is slightly under-reserved by the inter-grob gap LilyPond puts between the
    /// cancellation and the new signature — rare enough to leave documented.
    /// A leading CLEF change contributes NOTHING here, and that is LilyPond's own answer,
    /// not an omission. LilyPond orders an unbroken break-align group
    /// <c>clef, cue-clef, staff-bar, key-cancellation, key-signature, time-signature</c>
    /// (scm/define-grobs.scm:650-664), so the clef is the only one of the three that sits
    /// BEFORE the bar line. LilyPond's <c>minimum_distance</c> is measured column ORIGIN to
    /// column origin, and the origin is the leftmost break-aligned grob — so a clef moves
    /// the ORIGIN left without moving the bar line. This rod is expressed in Lily#'s frame,
    /// where the bar line sits at the origin (see the box built for it below), i.e. bar line
    /// to bar line. Measured on 2.24.4, bar line to bar line across `R1*5` is 14.133856 both
    /// with and without a leading `\clef bass` (and with a sparse or a dense preceding bar);
    /// only the column origin moves, by the clef's width + its
    /// <c>Clef.space-alist (staff-bar . (extra-space . 0.7))</c>. Adding a clef box here
    /// would therefore widen the run by ~2.847 ss that LilyPond does not spend.
    /// </remarks>
    internal static double MmrRodMinimumDistance(BarlineType leftBound, IEnumerable<MusicItem>? runStartItems)
    {
        HorizontalSkyline leftColumnRight =
            BoundaryColumn.Build(leftBound, runStartItems).RightSkylineFromBarLine();
        // The right bounding column carries only its bar line: whatever sits there, the
        // column origin coincides with the leftmost grob's left edge and that grob's
        // default extra-spacing-width left is −0.1, so the column's left reach is −0.1.
        // LILYPOND-REF: lily/separation-item.cc:167.
        HorizontalSkyline rightColumnLeft = HorizontalSkyline.FromBox(
            StaffYBottom, StaffYTop, xLeft: -0.1, xRight: 0.1, HorizontalDirection.Left);

        return Math.Max(0.0, leftColumnRight.Distance(rightColumnLeft));
    }

    /// <summary>
    /// LilyPond's minimum distance between the bar lines bounding a multi-measure
    /// rest run — the rod that replaces per-measure springs for the whole run.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest.cc:341-391
    /// Multi_measure_rest::calculate_spacing_rods, transcribed:
    /// <code>
    ///   length += full-measure-extra-space
    ///           + options.get_duration_space (mlen.main_part_)
    ///           + space-increment * log2 (measure-count);
    ///   length += 2 * bound-padding;
    ///   rod.distance_ = max (Paper_column::minimum_distance (li, ri) + length, minlen);
    /// </code>
    /// The local <c>length</c> enters as the symbol width (set_spacing_rods passes
    /// <c>symbol_stencil (me, 0.0)</c>). MultiMeasureRest leaves <c>minimum-length</c>
    /// unset, so LilyPond's <c>minlen</c> is 0 and the max() is inert; it is kept here
    /// to match the source line for line.
    ///
    /// The <c>options.get_duration_space</c> above is NOT the score's note spacing.
    /// <c>calculate_spacing_rods</c> does <c>options.init_from_grob (me)</c> with
    /// <c>me</c> = the MULTI-MEASURE REST grob, and init_from_grob reads
    /// <c>spacing-increment</c>, <c>shortest-duration-space</c> and
    /// <c>common-shortest-duration</c> off that grob — none of which MultiMeasureRest
    /// carries. So all three fall back to init_from_grob's OWN defaults, which are not
    /// the Spacing_options constructor's 1.2 / 2.0 / (1/8) but
    /// <c>1</c>, <c>1</c> and <c>Moment (1/8, 1/16)</c>:
    ///   increment = 1, shortest-duration-space = 1, global-shortest = 1/8.
    /// The rod's duration space is therefore SCORE-INDEPENDENT — a 4/4 bar always
    /// contributes <c>(1 + log2 ((1/1) / (1/8))) * 1 = 4.0</c>, whatever the music's
    /// own shortest note is. Feeding it the score's base shortest duration (which gave
    /// 5.298 for a 4/4 bar) made every run 1.298 ss too wide.
    /// Verified on LilyPond 2.24.4: overriding SpacingSpanner's shortest-duration-space
    /// (2.0 -> 4.0) or spacing-increment (1.2 -> 2.4) moves the run width by exactly
    /// 0.000, because the rod never reads them.
    /// LILYPOND-REF: lily/spacing-options.cc:31-53 Spacing_options::init_from_grob,
    ///               lily/spacing-options.cc:72-107 get_duration_space.
    /// </remarks>
    internal static double MmrRodDistance(
        int measureCount,
        Fraction measureLength,
        double minimumDistance,
        double runBarlineWidth)
    {
        double length = MmrSymbolWidth(measureCount);
        length += FullMeasureExtraSpace
                  + MmrRodDurationSpace(measureLength)
                  + MmrSpaceIncrement * Math.Log2(measureCount);
        length += 2 * MmrBoundPadding;

        const double minlen = 0.0;
        // LilyPond's rod is the whole li->ri COLUMN distance, with the bounding bar
        // lines living INSIDE those columns (bar-line extent runs from the column
        // origin). Lily#'s layout instead prices each measure as CONTENT + its own
        // bar-line glyph widths (GetBarlineWidth(start)+(end), added by the layouter
        // and the break gate alike), so the run measure would otherwise draw its
        // bounding bar lines twice: once folded into minimum_distance, once as measure
        // width. Subtract that run bar-line width here so the rod is the run's CONTENT
        // span; the layout then re-adds the bar lines to reach LilyPond's column
        // distance. (This is exactly what the old bw+0.2 form did implicitly by feeding
        // a None start bar line — now made explicit, since minimum_distance carries the
        // real left bar line and any break-aligned change.)
        return Math.Max(minimumDistance + length - runBarlineWidth, minlen);
    }

    /// <summary>
    /// <c>get_duration_space</c> as the multi-measure-rest rod sees it: with the
    /// Spacing_options that init_from_grob leaves behind for a grob carrying no
    /// spacing properties. See the note on <see cref="MmrRodDistance"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spacing-options.cc:72-107.</remarks>
    private static double MmrRodDurationSpace(Fraction measureLength)
    {
        // init_from_grob's fallbacks, NOT the Spacing_options constructor's values.
        const double increment = 1.0;
        const double shortestDurationSpace = 1.0;
        const double globalShortest = 0.125; // Moment (1/8, 1/16).main_part_

        double ratio = measureLength.ToDouble() / globalShortest;
        return ratio < 1.0
            ? (shortestDurationSpace + ratio - 1) * increment
            : (shortestDurationSpace + Math.Log2(ratio)) * increment;
    }

    /// <summary>
    /// Calculates the common shortest duration across all voices in a multi-staff score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:92-173 calc_common_shortest_duration —
    /// per MEASURE, find the shortest sounding duration; the spacing basis is the
    /// MODE of those per-measure shortests across the piece (ties prefer the
    /// shorter duration), capped at base-shortest-duration (3/16). This keeps one
    /// ornamental 32nd-note run from loosening the whole piece, and keeps
    /// long-note pieces from collapsing to minimal spacing — unlike the absolute
    /// global minimum this method used previously.
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.MultiStaffScore score)
        => CommonShortestDuration(score.AllVoices.Select(v => v.Measures),
            score.TimeSignature.MeasureDuration);

    /// <summary>
    /// Calculates the common shortest duration across all voices in a single-staff score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:92-173 calc_common_shortest_duration
    /// </remarks>
    public static double CalculateCommonShortestDuration(Model.Score score)
        => CommonShortestDuration(score.Voices.Select(v => v.Measures),
            score.TimeSignature.MeasureDuration);

    private static double CommonShortestDuration(
        IEnumerable<ImmutableArray<Model.Measure>> voiceMeasures,
        Fraction initialMeasureDuration)
    {
        var voices = voiceMeasures.ToList();
        int measureCount = voices.Count == 0 ? 0 : voices.Max(m => m.Length);

        // A full-measure rest is measured against the PREVAILING meter, so a 2/4 bar's
        // half rest is dropped from the vote just like a 4/4 bar's whole rest.
        var meters = MultiMeasureRestEngraver.PrevailingMeters(
            voices, measureCount, initialMeasureDuration);

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
                if (MultiMeasureRestEngraver.IsFullMeasureRest(measures[m], meters[m]))
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

}
