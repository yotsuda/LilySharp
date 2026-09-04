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
        // A spacer (`s`) never outranks a REAL playing note: LilyPond's spacing engraver
        // reads playing durations off the rhythmic grobs it acknowledges, and a skip
        // engraves none — so an `s8` under a sustained c'4 must not drag shortest-playing
        // down to an eighth (LP regression beam-skip.ly: the quarter keeps its plain
        // quarter spring). But when ONLY spacers play — a skip-only stretch, or a
        // lead-sheet row whose every item is a slot spacer — the spacers' durations stay
        // the answer. ⚠️ THAT KEEP IS LILY#'S OWN, not LilyPond's: LP never faces the
        // case (a column with no playing grobs is pruned before note_spacing runs, and
        // if one slipped through, spacing-basic.cc:113-120 would programming_error and
        // charge a WHOLE note — not delta_t). Lily#'s surviving skip-only columns ride
        // the delta_t fallback in CreateTimingSpringMultiVoice, and on the slot grids
        // the rows build, a slot's duration and its delta_t are the same number — the
        // measured lead-sheet recipe (ApplyRowCommandColumnSprings) is calibrated on
        // top of exactly that, which is why the spacer durations must keep flowing here.
        Fraction shortest = Fraction.Zero;
        Fraction shortestSpacer = Fraction.Zero;
        bool found = false;
        bool foundSpacer = false;

        foreach (var m in allMeasures)
        {
            Fraction t = Fraction.Zero;
            foreach (var item in m.Items)
            {
                Fraction end = t + item.Duration;
                // The note "plays" at `timing` iff t <= timing < end.
                if (t <= timing && timing < end && item.Duration > Fraction.Zero)
                {
                    if (item is RestItem { IsSpacer: true })
                    {
                        if (!foundSpacer || item.Duration < shortestSpacer)
                        {
                            shortestSpacer = item.Duration;
                            foundSpacer = true;
                        }
                    }
                    else if (!found || item.Duration < shortest)
                    {
                        shortest = item.Duration;
                        found = true;
                    }
                }
                t = end;
            }
        }

        return found ? shortest : shortestSpacer;
    }


    /// <summary>
    /// Creates all springs for a measure.
    /// </summary>
    /// <param name="measure">The measure to create springs for</param>
    /// <param name="baseShortestDuration">Optional spacing base-shortest-duration override;
    /// null uses the score default.</param>
    /// <param name="nextMeasure">The measure FOLLOWING this one, when known — a clef change
    /// opening it is drawn before the shared bar line, so its width is charged to this
    /// measure's closing spring (<see cref="BoundaryClefAllowance"/>). Must mirror
    /// MeasureLayouter.CreateTimingSprings, which does the same on the column side.</param>
    /// <returns>Array of springs (one between each pair of adjacent reference points)</returns>
    public static ImmutableArray<Spring> CreateSpringsForMeasure(Measure measure,
                                                                 double? baseShortestDuration = null,
                                                                 Measure? nextMeasure = null)
    {
        if (measure.Items.Length == 0)
            return ImmutableArray<Spring>.Empty;

        // LILYPOND-REF: lily/spacing-spanner.cc:200-280
        // Filter out loose items (tuplet brackets, fermata marks, etc.)
        // that don't participate in horizontal spacing
        var spacingItems = new List<MusicItem>();
        foreach (var item in measure.Items)
        {
            // A grace column is spaced by the group's own reservation on the spring INTO the
            // main note (AdjustSpringForGraceNotes below, read off NoteItem.LeadingGrace) and
            // never as a column of its own on this chain — the same division of labour
            // IsMusicalColumn states for the timing-column system. Letting one in gave the
            // group a spring HERE as well as the reservation THERE: measured, a grace opening
            // a lower voice's bar shoved the whole first column 1.04 to the right.
            if (!item.IsLoose && !item.GraceTime)
                spacingItems.Add(item);
        }

        if (spacingItems.Count == 0)
            return ImmutableArray<Spring>.Empty;

        // AN EMPTY BAR — skips and nothing else — has no column LilyPond keeps, so its two
        // bar lines are spaced as one breakable pair (SpacingRules.EmptyBarSprings; the
        // timing-column system decides the same in MultiStaffLayouter.ApplySharedColumnReservations,
        // where the score's side tables can also say whether a chord symbol or a syllable
        // stands over the skip — this single-measure estimate sees only the measure).
        // ⚠️ DERIVED, NOT TRANSCRIBED, on four counts this estimate cannot help (the fourth:
        // a double percent sign on a bounding bar line is a score-table fact, unseen here,
        // so no sign reaches into this estimate's bar): LilyPond's
        // measure-length is the METER's bar (the column system reads the prevailing meter);
        // a lone measure carries only its own duration, so a pickup bar of skips is priced
        // by its length here and by the meter there. The left bounding bar line of a
        // measure declaring none is the PREVIOUS bar's end line, which this estimate cannot
        // see, so it assumes the single line every such boundary draws by default. And the
        // left bar line's break permission is the previous measure's, unseen too, so the
        // pair is read as breakable on that side and this measure's own permission decides.
        // LILYPOND-REF: lily/paper-column.cc:115-136 Paper_column::is_used.
        if (BarHoldsOnlySkips(measure.Items))
            return EmptyBarSprings(
                spacingItems.Count,
                measure.StartBarline == BarlineType.None ? BarlineType.Single : measure.StartBarline,
                measure.Items,
                measure.LineBreakPermission != BreakPermission.Forbid,
                measure.TotalDuration,
                measure.TotalDuration,
                baseShortestDuration ?? EngravingDefaults.BaseShortestDuration);

        // NOTE: a full-measure rest gets ORDINARY springs here. LilyPond does the
        // same — a rested bar is spaced like any other bar, and the compaction of a
        // multi-measure rest comes from the run-level ROD
        // (Multi_measure_rest::calculate_spacing_rods, ported as MmrRodDistance)
        // applied across the collapsed run, NOT from shrinking each measure. The
        // earlier per-measure approximation here was wrong in BOTH directions:
        // measured against LP 2.24.4 it made an `R1*9` run ~108% too wide (the
        // approximation is linear in the count where LP's rod grows ~2·log2(count))
        // and a lowercase `r1` bar ~25% too narrow (LP spaces it as a normal bar:
        // `r1`×3 spans 31.214 ss with or without \compressMMRests, vs `R1*3` 20.810).

        // A SKIP HAS NO COLUMN: its onset is LilyPond's unused column and leaves the
        // chain, so the items on either side become neighbours and the leg between them
        // spans the skip's time (MultiStaffLayouter.CollectAllTimingsForMeasure is the
        // column system's spelling of the same drop). Each kept item carries its onset, so
        // a leg's delta_t is the distance to the NEXT KEPT item or to the bar.
        // LILYPOND-REF: lily/paper-column.cc:115-136 Paper_column::is_used.
        var kept = new List<(MusicItem Item, Fraction Onset)>(spacingItems.Count);
        {
            var onset = Fraction.Zero;
            var keep = new HashSet<MusicItem>(ReferenceEqualityComparer.Instance);
            foreach (var item in spacingItems)
                keep.Add(item);
            foreach (var item in measure.Items)
            {
                if (keep.Contains(item) && item is not RestItem { IsSpacer: true })
                    kept.Add((item, onset));
                onset += item.Duration;
            }
        }
        if (kept.Count == 0)
            return ImmutableArray<Spring>.Empty;
        var keptItems = kept.Select(k => k.Item).ToList();
        var totalDuration = measure.TotalDuration;

        var springs = new List<Spring>();

        // Spring from start barline to first item — the SAME builder the timing-column
        // system uses, so the leading grace / change-glyph / skyline reservations and
        // the BarLine space-alist value cannot drift between the two. This used to
        // price the gap as the first note's duration space (3.6 for a quarter against
        // the correct 0.9): LilyPond reaches a bar line → note pair through
        // Staff_spacing, where duration never enters.
        // A measure filled by a single note/chord gets LP's full-measure-extra-space
        // on THIS spring (barline → first column), not on the note → barline spring:
        // LP passes it as `situational_space` to Staff_spacing::get_spacing, keyed on
        // the measure that FOLLOWS the barline.
        // LILYPOND-REF: lily/spacing-spanner.cc:484-489 breakable_column_spacing.
        // A bar that OPENS with a skip has its first kept item at a later moment, and the
        // bar line → item pair takes the duration-space branch instead (the column system's
        // CreateBarlineToFirstSpring makes the same fork). The left bounding line is the
        // measure's own start line, a single line standing in where it declares none — the
        // previous bar's end line is not in this estimate's sight.
        var (firstItem, firstOnset) = kept[0];
        var firstSpring = firstOnset > Fraction.Zero
            ? SkipOpenedBarFirstSpring(
                measure.StartBarline == BarlineType.None ? BarlineType.Single : measure.StartBarline,
                measure.Items, new[] { firstItem }, firstOnset,
                baseShortestDuration ?? EngravingDefaults.BaseShortestDuration)
            : BarlineToFirstColumnSpring(new[] { firstItem }, FillsMeasure(measure));
        springs.Add(firstSpring);

        // Springs between items (the spring into a grace-bearing note reserves its grace;
        // a pair touching a mid-measure clef/key/time change is priced by that change's
        // column, so this estimate totals what the timing-column layout will produce and
        // line breaking does not mis-measure change measures — pinned by
        // SpacingInvariantTests.BothSpringSystems_AgreeAcrossAMidMeasureChangeColumn).
        for (int i = 0; i < kept.Count - 1; i++)
        {
            var (prevItem, prevOnset) = kept[i];
            var (nextItem, nextOnset) = kept[i + 1];
            var spring = CreateSpring(prevItem, nextItem, nextOnset - prevOnset,
                baseShortestDuration: baseShortestDuration,
                shortestPlaying: prevItem.Duration);
            // Swap the generic spacing-increment for the LEFT column's real head
            // width, exactly as the timing-column system does (MeasureLayouter) —
            // this is LilyPond's ideal, and leaving it out made every spring here
            // ~0.104 ss narrow for a black head.
            spring = ApplyLeftHeadWidth(spring, One(prevItem), One(nextItem));
            spring = AdjustSpringForGraceNotes(
                spring, GraceNotesOf(nextItem), graceParams: null, mainItem: nextItem);
            // A pair touching a mid-measure change column is priced by the change column,
            // not by duration — and NOT by merge_springs' headroom afterwards, which would
            // add 0.3 to a gap LilyPond has already fixed.
            if (ChangeColumnItemSpring(keptItems, i, spring.IdealDistance) is { } changeSpring)
            {
                springs.Add(changeSpring);
                continue;
            }
            // Mirror of MeasureLayouter.CreateInterColumnSpring.
            // LILYPOND-REF: lily/spacing-spanner.cc:380-393 -> lily/spring.cc:122.
            spring = ApplyMergeSpringsHeadroom(spring);
            springs.Add(spring);
        }

        // Spring from last item to end barline. full-measure-extra-space is charged to
        // the LEADING spring above, mirroring LilyPond's attribution. The leg runs from
        // the last KEPT item to the bar over any skip that follows it.
        var (lastItem, lastOnset) = kept[^1];
        var lastSpring = CreateSpring(lastItem, null, totalDuration - lastOnset,
            baseShortestDuration: baseShortestDuration,
            shortestPlaying: lastItem.Duration);
        // The column's skyline against the bar line's box, the bar line's box grown toward
        // BOTH its neighbours — the same pair the timing-column system prices
        // (MeasureLayouter.CreateLastToBarlineSpring). CreateSpring saw the left neighbour
        // only; the rod is applied after the headroom below.
        var barPair = NoteColumnToBarlineFloorPair(lastItem, LeadingMusicalItems(nextMeasure));
        lastSpring = lastSpring.EnsureMinDistance(barPair.SkyMin);
        lastSpring = ApplyLeftHeadWidth(lastSpring, One(lastItem));

        // The bar line stands in for the right-hand stem, so LilyPond runs
        // stem_dir_correction on THIS spring too. CreateSpring's own
        // CalculateStemCorrection sees no RIGHT item here and contributes nothing,
        // so without this the two spring systems disagreed on every stemmed measure:
        // the timing-column system (MeasureLayouter.CreateLastToBarlineSpring) has
        // carried the correction since the bar-line spring was ported, this one never
        // did. A measure has one voice, so this is the single-wish case of
        // MergeVoiceStemWishesToBarline — merging one wish returns it unchanged.
        // LILYPOND-REF: lily/note-spacing.cc:111 + :243-264; :113 clamps at 0.0.
        lastSpring = lastSpring.WithIdealDistance(
            Math.Max(0, lastSpring.IdealDistance
                + CalculateStemCorrectionToBarline(lastItem, NoteSpacingParameters.Default)));

        // Mirror of MeasureLayouter.CreateLastToBarlineSpring: a clef change opening the
        // NEXT measure is drawn before this bar line, so it widens the MINIMUM here. The
        // duration-based ideal is already bar-line framed and stays put.
        double clefAllowance = BoundaryClefAllowance(measure.EndBarline, nextMeasure);
        if (clefAllowance > 0)
            // LILYPOND-REF: lily/spring.cc:143-153 set_min_distance — the minimum moves,
            // the strengths do not.
            lastSpring = lastSpring.WithMinDistance(lastSpring.MinDistance + clefAllowance);

        // ...and merge_springs' headroom then lifts the ideal off that minimum, which is
        // what places the bar line when a clef precedes it. Mirror of
        // MeasureLayouter.CreateLastToBarlineSpring — the two systems must agree
        // (SpacingInvariantTests.BothSpringSystems_AgreeOnEveryMusicalSpring).
        // LILYPOND-REF: lily/spacing-spanner.cc:380-393 -> lily/spring.cc:122;
        //   lily/note-spacing.cc:78-83 for the padding-free minimum it floors from.
        lastSpring = ApplyMergeSpringsHeadroom(lastSpring);

        // …and the column rod last, a floor on the compressed length only — mirror of
        // MeasureLayouter.CreateLastToBarlineSpring.
        // LILYPOND-REF: lily/spacing-spanner.cc:228-297 set_column_rods.
        lastSpring = lastSpring.EnsureMinDistance(barPair.Rod + clefAllowance);

        springs.Add(lastSpring);

        return springs.ToImmutableArray();
    }

    /// <summary>
    /// The musical item(s) a measure OPENS with — its first column's note, chord or drawn
    /// rest, zero-duration changes and grace time stepped over — or null when it opens with
    /// none (a spacer, an empty placeholder, no measure at all). The bar line closing the
    /// previous measure has these as its right-hand neighbours.
    /// </summary>
    internal static IReadOnlyList<MusicItem>? LeadingMusicalItems(Measure? measure)
    {
        if (measure == null)
            return null;
        List<MusicItem>? items = null;
        foreach (var item in measure.Items)
        {
            if (item.GraceTime || IsChangeItem(item))
                continue;
            if (IsMusicalColumn(item))
                (items ??= new List<MusicItem>()).Add(item);
            if (item.Duration > Fraction.Zero)
                break;
        }
        return items;
    }

    /// <summary>
    /// Reserves chord symbols' real text widths on the timing columns, the
    /// way LilyPond's ChordName item joins its paper column's horizontal
    /// extent expanded by (-0.5 . 0.5) — so neighbouring symbols keep ≥1.0
    /// space and a chords-only grid gets real bar widths (sixteen R1-thin
    /// bars otherwise "fit" one line and the symbols overprint). Widths use
    /// the sans face the symbols render in.
    /// LILYPOND-REF: scm/define-grobs.scm ChordName extra-spacing-width.
    /// </summary>
    /// <remarks>
    /// The reservation is ASYMMETRIC because the symbol is: ChordName has no X-offset and no
    /// self-alignment-interface (scm/define-grobs.scm:837-855), so its ink runs <c>(0 . w)</c>
    /// from its column and the spacing extent runs <c>(-0.5 . w + 0.5)</c>. A column therefore
    /// owes 0.5 to its LEFT neighbour and <c>w + 0.5</c> to its right one, whichever side the
    /// neighbour is — not <c>w/2 + 0.5</c> to each, which is what a centred symbol would owe.
    /// <para>
    /// ⚠️ NAMED DIVERGENCE (measured 2026-09-05, scratch/p334/bench-cn.ly with cn-settings.ly):
    /// LilyPond's rod is between the ChordName and what shares its HEIGHT — the next
    /// ChordName (<c>w + 0.5 + 0.5 + 0.1</c>) and the line end — because Separation_item
    /// distances are horizontal-skyline distances. A bar line and a note column do not
    /// reach the chord line, so a name wider than its bar OVERHANGS the bar line and even the
    /// next note column (`F♯sus4' ext 6.90 at 16.34, next bar at 21.88, `Emaj7/D♯' at 25.42:
    /// the names keep 2.17, the bar line stands under the first). Lily# prices the last
    /// symbol's reach to the bar EDGE instead (the bar line clears it), which is stronger:
    /// the springs are per measure and no rod spans the bar column. Same answer where the
    /// natural spacing already clears; wider bars where it does not.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Spring> ApplyChordRowSpacing(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        int measureIndex,
        ImmutableArray<ChordNameItem> chordNames,
        bool includeAttached = false)
    {
        if (chordNames.IsDefaultOrEmpty || springs.Length != timings.Count + 1)
            return springs;

        var width = new double[timings.Count];
        bool any = false;
        foreach (var cn in chordNames)
        {
            if (cn.MeasureIndex != measureIndex)
                continue;
            // Row symbols always price; STAFF-ATTACHED symbols only when the
            // caller opts in (an all-rest measure has no other width source).
            // ⚠️ A note-attached @chord (UseTiming false) is attached too: it carries its
            // note's onset in Timing (MeasureCollector.CollectChordNames), so it stands on
            // that column here exactly as a chords-track symbol does. Until 2026-09-05 the
            // filter also required UseTiming, and an inline symbol reserved NOTHING — two
            // whole-note chords with wide names (`F♯sus4' `Emaj7/D♯') printed 0.78 ss apart,
            // reading as one word (scratch/ベースタブLy/bench.lys). LilyPond has one grob for
            // both spellings, so one price.
            if (!cn.IsChordRow && !includeAttached)
                continue;
            for (int t = 0; t < timings.Count; t++)
            {
                if (timings[t] == cn.Timing)
                {
                    width[t] = Math.Max(width[t],
                        ChordNameEngraver.SymbolInkWidth(fonts, cn.ChordText));
                    any = true;
                    break;
                }
            }
        }
        if (!any)
            return springs;

        // LILYPOND-REF: scm/define-grobs.scm ChordName extra-spacing-width
        // (-0.5 . 0.5): the symbol's spacing extent is its ink (0 . w) grown by
        // 0.5 on each side, so it clears a bar line on its left by 0.5, reaches
        // w + 0.5 to its right, and two adjacent symbols keep 1.0 between them.
        const double edgeGap = 0.5;
        // LILYPOND-REF: lily/spacing-spanner.cc:315-316 generate_springs — the rod between
        // two columns is the Separation_item distance PLUS the column's `padding`, default
        // 0.1 (`set_column_rods (cols, padding)`), the same 0.1 the note-to-note rods carry.
        // MEASURED (audit/lp-geometry/probes/chord-symbol-width.ly, score CWA): two adjacent
        // "Am" quarters sit at w + 0.5 + 0.5 + 0.1 to six digits; before this term the
        // chord.symbol-width.minor-pair-gap point read exactly 0.100000 of its residual here.
        const double rodPadding = 0.1;
        var result = springs.ToBuilder();
        void Widen(int springIndex, double needed)
        {
            var s = result[springIndex];
            // A rod: the minimum moves, the strengths do not (see ApplyTabChordSpacing's
            // Widen for the measured consequence of resetting the compress strength).
            // LILYPOND-REF: lily/simple-spacer.cc:90-127 Simple_spacer::add_rod.
            if (needed > s.MinDistance)
                result[springIndex] = new Spring(
                    Math.Max(s.IdealDistance, needed), needed,
                    s.InverseStretchStrength,
                    needed >= s.IdealDistance ? 0.0 : s.InverseCompressStrength);
        }
        // How far a column's symbol reaches on each side, extra-spacing-width included.
        // A column with no symbol reaches nowhere: LilyPond has no grob there to grow.
        double LeftReach(int t) => width[t] > 0 ? edgeGap : 0;
        double RightReach(int t) => width[t] > 0 ? width[t] + edgeGap : 0;
        // A rod exists only where the symbol contributed a box; a zero reach means no box,
        // so no padding either (the other content's rods are made elsewhere).
        double Rod(double reach) => reach > 0 ? reach + rodPadding : 0;

        // Left edge: only the -0.5 of the extent stands left of the column, never a
        // half width — the ink itself starts ON the column.
        Widen(0, Rod(LeftReach(0)));
        for (int t = 0; t < timings.Count - 1; t++)
        {
            // A STAFF-ATTACHED symbol OVERHANGS a bare-note column (LP ChordName
            // extra-spacing-width -0.5 . 0.5) rather than pushing the note right,
            // so where a symbol borders a column with no symbol, reserve nothing
            // and let the note keep its natural, even spacing. A chords ROW/grid
            // (includeAttached == false) has no notes to overhang — its symbols
            // ARE the content — so it keeps the full reservation on every cell.
            // Two adjacent symbols always price so they never overprint, and the
            // bar EDGES below price the full width so an all-rest (R1) attached
            // bar, whose only column is the rest, still clears the barlines.
            if (includeAttached && (width[t] <= 0 || width[t + 1] <= 0))
                continue;
            // The LEFT symbol's whole width lies between the two columns; the right
            // one's lies beyond them. So the gap owes (w[t] + 0.5) + 0.5, plus the
            // one rod padding — it is a per-rod term, not a per-box one.
            Widen(t + 1, Rod(RightReach(t) + LeftReach(t + 1)));
        }
        Widen(timings.Count, Rod(RightReach(timings.Count - 1)));
        return result.ToImmutable();
    }

    /// <summary>
    /// How far the chord-symbol ink on each of a measure's columns reaches RIGHT of that column
    /// — the chord side of LilyPond's keep-inside-line rod, one entry per column. Mirrors
    /// <see cref="ApplyChordRowSpacing"/>'s own <c>width</c> array exactly (same filter, same
    /// metric), so the quantity rodded is the one that method reserves.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:837-855 — ChordName declares no <c>X-offset</c> and
    /// no <c>self-alignment-interface</c> at all, so its reference point IS its ink left and
    /// the symbol stands ON its column: its extent is <c>(0 . w)</c>. There is therefore NO
    /// left reach to rod, and the right reach is the symbol's whole width.
    /// MEASURED (audit/lp-geometry/probes/staffless-system.ly): the ChordName anchor equals
    /// its column's X to 6 digits in every score of that probe (CO, COW, CL, CLW, CS), and
    /// widening the name by 13.5 ss does not move the first column by a thousandth.
    /// No padding is added — LilyPond's rod carries none (lily/simple-spacer.cc:559) — unlike
    /// <see cref="ApplyChordRowSpacing"/>'s neighbour gaps.
    /// </remarks>
    internal static double[] ChordInkRightReachPerColumn(
        Rendering.ScoreTextMetrics fonts,
        IReadOnlyList<Fraction> timings,
        int measureIndex,
        ImmutableArray<ChordNameItem> chordNames,
        bool includeAttached)
    {
        var width = new double[timings.Count];
        if (chordNames.IsDefaultOrEmpty || timings.Count == 0)
            return width;

        foreach (var cn in chordNames)
        {
            if (cn.MeasureIndex != measureIndex)
                continue;
            // The same filter as ApplyChordRowSpacing: an inline @chord is attached too.
            if (!cn.IsChordRow && !includeAttached)
                continue;
            for (int t = 0; t < timings.Count; t++)
                if (timings[t] == cn.Timing)
                {
                    width[t] = Math.Max(width[t],
                        ChordNameEngraver.SymbolInkWidth(fonts, cn.ChordText));
                    break;
                }
        }
        return width;
    }

    /// <summary>
    /// How far the MUSICAL ink on each timing column reaches past that column, on each side —
    /// the note half of LilyPond's <c>keep_inside_line_</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:431-432 — <c>keep_inside_line_ =
    /// col-&gt;extent (col, X_AXIS)</c>, the column's own INK. Not the spacing box: an
    /// <c>extra-spacing-width</c> is read by <c>Separation_item</c>, it is not part of a
    /// grob's X-extent, so <see cref="CalculateLeftExtent"/> and
    /// <see cref="CalculateNoteheadRightExtent"/> are used bare here where
    /// <see cref="MusicalColumnLeftReach"/> (which serves <c>Paper_column::minimum_distance</c>)
    /// adds it.
    /// <para>
    /// The column reference point coincides with a note head's LEFT edge, so a plain head
    /// reaches its full width RIGHT and nothing left; what reaches LEFT is an accidental
    /// (probe TKT read a note carrying one at 1.234272 against a plain note's 0.100000, both
    /// including the 0.1 / 0.2 <c>extra-spacing-width</c> that this function excludes).
    /// </para>
    /// <para>
    /// Every measure at the index is walked — a paper column is shared by all staves and
    /// voices — and items are matched to columns by ONSET, the same walk
    /// <see cref="ApplyTabChordSpacing"/> makes.
    /// </para>
    /// </remarks>
    internal static (double[] Left, double[] Right) MusicalInkOverhangsPerColumn(
        IReadOnlyList<Model.Measure> measures, IReadOnlyList<Fraction> timings)
    {
        var left = new double[timings.Count];
        var right = new double[timings.Count];
        foreach (var measure in measures)
        {
            var onset = Fraction.Zero;
            foreach (var item in measure.Items)
            {
                if (IsMusicalColumn(item))
                    for (int t = 0; t < timings.Count; t++)
                        if (timings[t] == onset)
                        {
                            left[t] = Math.Max(left[t], CalculateLeftExtent(item));
                            right[t] = Math.Max(right[t], CalculateNoteheadRightExtent(item));
                            break;
                        }
                onset += item.Duration;
            }
        }
        return (left, right);
    }

    /// <summary>
    /// The point a grob whose <c>parent-alignment-X</c> is CENTER aligns to on each timing
    /// column — LilyPond's <c>he.linear_combination (CENTER)</c>, i.e. the centre of the
    /// column's note-column extent, or of the placeholder when the column holds no rhythmic
    /// grob at all. One entry per column, measured from the column's own reference point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:117-141 <c>aligned_on_parent</c> —
    /// <c>he = Paper_column::get_interface_extent (him, note-column-interface, a)</c>, and when
    /// that is empty on X it falls back to the column's <c>X-alignment-extent</c>
    /// (<see cref="EngravingDefaults.PaperColumnXAlignmentExtentWidth"/>). The extent here is
    /// unioned over EVERY note column on the paper column, the same walk
    /// <see cref="MusicalInkOverhangsPerColumn"/> makes.
    /// ⚠️ FOR A LYRIC THE UNION IS THE WRONG SET — MEASURED (probe
    /// lyric-bound-voice-mapping.ly, 2.26.0): a syllable centres on its OWN voice's head
    /// (LBIP's on the primary quarter's 0.6521, LBI/LBIC's on the bound half's 0.6887 —
    /// per-voice, not the union of both), because a LyricText's X parent is the note head
    /// of its ASSOCIATED voice, not the shared paper column. The per-voice reading is
    /// <see cref="OwnVoiceAlignmentEdgeAt"/>; lyric consumers take it first and fall back
    /// here only where no own-voice item exists (a row's finer grid, the placeholder).
    /// Until 2026-08-20 the reservations read this union (the +0.0366 sliver
    /// lyrics.column.bound-voice.primary-control priced) while the engraver read a
    /// per-staff PRIMARY-only walk (the same sliver on the drawn side of a BOUND voice) —
    /// two spellings, both voice-blind a different way.
    /// <para>
    /// ⚠️ WHAT IS IN THAT EXTENT WAS MEASURED, NOT ASSUMED (audit/lp-geometry/probes/
    /// staffless-system.ly, scores LSH / LSA / LSD / LSR). A NoteColumn's X-extent is its whole
    /// axis group (define-grobs.scm NoteColumn <c>X-extent = ly:axis-group-interface::width</c>),
    /// so the question is which grobs are IN the group, and the answer is not the one a reading
    /// of "the column's ink" would give: note heads are (LSH, 0.688700 = half of a 1.377400
    /// head) and rests are (LSR, 0.750000 = half a half-rest), but an ACCIDENTAL is not (LSA,
    /// unchanged at 0.688700) and neither is a DOT (LSD, unchanged). Both predictions to the
    /// contrary were written down first and both were wrong. That is consistent with LilyPond's
    /// structure — a Dots grob hangs off its note head and the accidentals off an
    /// Accidental_placement, so neither is among the note column's <c>elements</c> — and it is
    /// why this does NOT reuse <see cref="MusicalInkOverhangsPerColumn"/>, which deliberately
    /// includes an accidental's leftward reach because the keep-inside-line rod does take it.
    /// </para>
    /// <para>
    /// A stem is in the group but never widens it: it stands at a head's own edge.
    /// </para>
    /// </remarks>
    internal static (double Left, double Centre)[] ParentAlignmentEdgesPerColumn(
        IReadOnlyList<Model.Measure> measures, IReadOnlyList<Fraction> timings)
    {
        var left = new double[timings.Count];
        var right = new double[timings.Count];
        var seen = new bool[timings.Count];

        foreach (var measure in measures)
        {
            var onset = Fraction.Zero;
            foreach (var item in measure.Items)
            {
                if (RhythmicHeadExtent(item) is { } ext)
                    for (int t = 0; t < timings.Count; t++)
                        if (timings[t] == onset)
                        {
                            left[t] = seen[t] ? Math.Min(left[t], ext.Left) : ext.Left;
                            right[t] = seen[t] ? Math.Max(right[t], ext.Right) : ext.Right;
                            seen[t] = true;
                            break;
                        }
                onset += item.Duration;
            }
        }

        // Both alignment points a self-aligned grob can take on the extent: its LEFT
        // edge (a melisma syllable, lyricMelismaAlignment) and its CENTER (everything
        // else). The placeholder extent is (0 . 1.35): left 0, centre half the width.
        var edges = new (double Left, double Centre)[timings.Count];
        for (int t = 0; t < timings.Count; t++)
            edges[t] = seen[t]
                ? (left[t], (left[t] + right[t]) / 2)
                : (0.0, EngravingDefaults.PaperColumnXAlignmentExtentWidth / 2);
        return edges;
    }

    /// <summary>
    /// The alignment edge pair on ONE VOICE's bar at a moment — the per-voice reading a
    /// LYRIC aligns on (its X parent is its own voice's note head; see the union
    /// function's remark for the probe that measured it). Null when the voice has no
    /// rhythmic item at that moment — the caller falls back to the union/placeholder.
    /// </summary>
    internal static (double Left, double Centre)? OwnVoiceAlignmentEdgeAt(
        Model.Measure measure, Fraction timing)
    {
        var onset = Fraction.Zero;
        foreach (var item in measure.Items)
        {
            if (onset == timing && RhythmicHeadExtent(item) is { } ext)
                return (ext.Left, (ext.Left + ext.Right) / 2);
            onset += item.Duration;
        }
        return null;
    }

    /// <summary>
    /// One item's contribution to the note-column extent above: its MAIN note head (the
    /// unreversed one at the column origin), or the rest, measured from the column's
    /// reference point. Null for anything that is not a rhythmic grob (a note column holds
    /// no clef or bar line).
    /// </summary>
    private static (double Left, double Right)? RhythmicHeadExtent(MusicItem? item)
    {
        if (!IsMusicalColumn(item) || item is null)
            return null;

        int noteValue = GetNoteValue(item);

        // A rest is drawn glyph-left-aligned at its column, so its own box IS its extent.
        if (item is RestItem)
        {
            var restBox = GlyphMetrics.GetRestBBox(noteValue);
            return (restBox.Left, restBox.Right);
        }

        var head = GlyphMetrics.GetNoteheadBBox(noteValue);
        // A chord contributes only its MAIN notehead, not the union with reversed
        // (suspended) heads: the aligning grobs (LyricText, TextScript) declare
        // X-align-on-main-noteheads #t, which swaps the note column's extent for
        // its main-extent — the extent of first_head, the head the stem walk
        // starts from, which the positioning leaves at offset 0 (reversed heads
        // are the ones shifted, stem-up right / stem-down left). So the main
        // head's box IS the plain head box at the column origin, for NoteItem
        // and ChordItem alike. input-order-alignment.ly measured the union
        // mis-centring a suspended chord's syllable by half a head (10.245 vs
        // LP's main-head centre 9.7861).
        // LILYPOND-REF: lily/note-column.cc:179-204 calc_main_extent
        // LILYPOND-REF: lily/self-alignment-interface.cc:143-145 aligned_on_parent
        // LILYPOND-REF: scm/define-grobs.scm:2228 LyricText X-align-on-main-noteheads
        return (head.Left, head.Right);
    }

    /// <summary>
    /// The surviving empty COMMAND columns of a staff-less row: between two of a lead
    /// sheet's timing columns LilyPond has TWO springs, not one — musical column →
    /// (empty) command column at the next beat, then command column → that beat's
    /// musical column — and the second is the breakable dt==0 spring, a flat 0.5.
    /// This composes that pair into each inter-column spring.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:71-77 standard_breakable_column_spacing —
    /// <c>ideal = min_dist + 0.5</c> for a dt == 0 pair, and <c>min_dist</c> is 0 here
    /// because an empty command column has no box. The command columns SURVIVE only on a
    /// staff-less row: lily/spacing-determine-loose-columns.cc:82-90
    /// <c>is_loose_column</c> wants a <c>left-neighbor</c>/<c>right-neighbor</c> to
    /// attach a loose column to, those are set off NOTE columns, and a
    /// ChordNames/Lyrics-only score has none — so the empty columns are never pruned and
    /// every beat costs its duration space PLUS this 0.5. On a staff-backed score they
    /// are pruned and no such term exists, which is why this is applied on the lead-sheet
    /// path only.
    /// <para>
    /// MEASURED (audit/lp-geometry/probes/chord-symbol-width.ly, CAL2 ALLCOL dump): the
    /// system of a chords-only score holds a starter-less column 0.5 left of EVERY
    /// musical column, and each measured gap decomposes to six digits as
    /// duration-space + 0.500000 across four regimes (quarters 2.898045 + 0.5, halves
    /// 4.098045 + 0.5, eighths 2.4 + 0.5, and the mixed book's quarter 3.6 + 0.5).
    /// The last musical column's spring runs to the bar line's own command column, so
    /// the closing spring carries NO extra term (whole → bar measured 5.298045, the bare
    /// duration space).
    /// </para>
    /// <para>
    /// Composing the pair into one spring is exact, not an approximation: springs in
    /// series add their ideals, their minima and their inverse strengths, and the dt==0
    /// spring is Spring(0.5, 0) with the default strength — its inverse stretch is its
    /// own ideal (lily/spring.cc set_default_strength), 0.5.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Spring> ApplyRowCommandColumnSprings(
        ImmutableArray<Spring> springs)
    {
        // The dt == 0 breakable spring of one surviving empty command column.
        const double commandIdeal = 0.5;
        if (springs.Length <= 2)
            return springs;
        var result = springs.ToBuilder();
        // Inter-column springs only: spring 0 (bar line → first column) is already the
        // breakable pair, and the last spring runs INTO a command column (the bar
        // line's), so LilyPond adds nothing there.
        for (int i = 1; i < result.Count - 1; i++)
        {
            var s = result[i];
            result[i] = new Spring(
                s.IdealDistance + commandIdeal, s.MinDistance,
                s.InverseStretchStrength + commandIdeal);
        }
        return result.ToImmutable();
    }

    /// <summary>
    /// Floors a LEAD-SHEET bar at a readable grid-cell width. Row bars carry
    /// no notation ink, so without a floor a long chart packs every bar onto
    /// one line; with it the chart wraps like a song-book grid.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: LilyPond has no such floor — a chords-only chart's bar width is
    /// whatever its duration springs add up to. Both the 10.0 and the distribution are
    /// Lily#'s.
    /// <para>
    /// ⚠️ The whole deficit goes into the LAST spring — the trailing room after the bar's
    /// final chord — and nowhere else. It used to be shared equally across every spring,
    /// and in a bar with one chord (a whole-note cell: two springs) that put half the
    /// artificial width IN FRONT of beat 1 — the symbol and its syllable sat ~3.5 ss deep
    /// into the bar while every multi-chord bar opened at ~0.6 (reported by the user on
    /// test/lead-sheet, 2026-07-29: a beat-1 note belongs by its bar line). Inner springs
    /// must not take it either: they are the bar's DURATION springs, the quantity the
    /// <c>chord.symbol-width.*spring-control</c> ledger points measure against LilyPond,
    /// and a floor share folded into them is invisible fitting. Trailing room is also
    /// where LilyPond's own duration springs put a whole note's width.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Spring> EnsureLeadSheetBarWidth(ImmutableArray<Spring> springs)
    {
        const double gridBarMinWidth = 10.0;
        if (springs.Length == 0)
            return springs;
        double minSum = 0;
        foreach (var s in springs)
            minSum += s.MinDistance;
        if (minSum >= gridBarMinWidth)
            return springs;
        double extra = gridBarMinWidth - minSum;
        var result = springs.ToBuilder();
        var last = result[^1];
        result[^1] = new Spring(
            Math.Max(last.IdealDistance, last.MinDistance + extra),
            last.MinDistance + extra, last.InverseStretchStrength);
        return result.ToImmutable();
    }

    /// <summary>
    /// Reserves the horizontal room a TAB staff's fret digits need in the SHARED
    /// note columns, so adjacent digits (or a chord's zigzagged columns) do not
    /// overprint. Tab fret numbers are a Lily# enlargement of LilyPond's tiny,
    /// unspaced digits, so their width has no LilyPond analogue and is priced in
    /// here on the "digits must not overlap" principle — the same one that drives
    /// the chord zigzag. Widens each inter-column spring to hold the right extent
    /// of the left column plus the left extent of the right column.
    /// </summary>
    public static ImmutableArray<Spring> ApplyTabChordSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        Model.Measure tabMeasure,
        int[] tuning,
        int octaveShift)
    {
        if (springs.Length != timings.Count + 1)
            return springs;

        var left = new double[timings.Count];
        var right = new double[timings.Count];
        bool any = false;
        Fraction onset = Fraction.Zero;
        foreach (var item in tabMeasure.Items)
        {
            // A grace's fret digit is drawn small and inside the grace group's own reserved
            // run (GraceNoteLayout.Tuning), so it asks nothing of the shared note column —
            // measured, pricing one here widened a bass tab's first spring by 0.66, a whole
            // digit, and shoved the bar along with it.
            if (item is (Model.NoteItem or Model.ChordItem) and not { GraceTime: true })
                for (int t = 0; t < timings.Count; t++)
                    if (timings[t] == onset)
                    {
                        var (l, r) = LilySharp.Core.Rendering.SharedRenderer.TabItemHalfExtent(
                            item, tuning, octaveShift);
                        left[t] = Math.Max(left[t], l);
                        right[t] = Math.Max(right[t], r);
                        any = true;
                        break;
                    }
            onset += item.Duration;
        }
        if (!any)
            return springs;

        double tabGap = TabConstants.FretColumnGap; // clearance between adjacent digit columns
        var result = springs.ToBuilder();
        void Widen(int idx, double needed)
        {
            var s = result[idx];
            // A reservation is a ROD: it moves the minimum (and the ideal up to it) and
            // neither strength — LilyPond's add_rod raises blocking forces only. The
            // 3-argument constructor here used to reset the compress strength to
            // ideal − min, so a rest → note spring under a chord symbol blocked at
            // −1.0 where LilyPond's blocks at −0.62 (Freedom bars 69-76, session 323).
            // LILYPOND-REF: lily/simple-spacer.cc:90-127 Simple_spacer::add_rod.
            // ⚠️ A rod that reaches the ideal leaves min == ideal, and LilyPond's blocking
            // force is then 0 ONLY because the compress strength is 0 (spring.cc:78-82):
            // compress_line takes a blocking-0 spring as already blocked, never adds its
            // flexibility, and subtracts it anyway — a positive strength there breaks the
            // solve (Simple_spacer::compress_line, 想い人 bars 84-87 read "does not fit").
            // So the strength survives only while the rod stays under the ideal.
            if (needed > s.MinDistance)
                result[idx] = new Spring(
                    Math.Max(s.IdealDistance, needed), needed,
                    s.InverseStretchStrength,
                    needed >= s.IdealDistance ? 0.0 : s.InverseCompressStrength);
        }
        Widen(0, left[0]);
        for (int t = 0; t < timings.Count - 1; t++)
            Widen(t + 1, right[t] + left[t + 1] + tabGap);
        Widen(timings.Count, right[^1]);
        return result.ToImmutable();
    }

    /// <summary>
    /// Prices the CROSS-VOICE column pairs of one staff — the pairs the per-voice rod
    /// loop in <see cref="MeasureLayouter"/> cannot see — with each item's ink taken at
    /// the X the renderer will draw it, its note-collision voice shift included. The
    /// case that found it: a dotted half in voice three (shifted right past the
    /// down-voice head) reaches its DOT into the next eighth column, which belongs to
    /// voice two alone; no single voice occupies both columns, so no floor was raised
    /// and the dot printed straight through the next head
    /// (scratch/ベースタブLy/Untitled-4.lys, 起票 2026-08-05).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:120-190 Separation_item::boxes — a staff's
    ///   separation item boxes every grob of its paper column across ALL the staff's
    ///   voices, as extents in the COLUMN's frame, i.e. with calc_positioning_done's
    ///   shifts already applied. LilyPond resolves collisions BEFORE spacing reads the
    ///   boxes; Lily# applies shifts at render time, so this pass asks the same
    ///   computation the renderer's offsets come from
    ///   (<see cref="ElementCoordinator.ComputeVoiceOffsets"/>).
    /// LILYPOND-REF: lily/note-spacing.cc:78-83 — the spring minimum is the skyline
    ///   distance over those boxes; lily/spring.cc:122 merge_springs then floors the
    ///   ideal at min + 0.3, which is why the headroom is re-applied after the raise.
    /// <para>
    /// MEASURED (2.26.0, the book above): with the dot, LilyPond's first eighth gap is
    /// 3.33 against the measure's plain 2.50; remove the dot (<c>cis2</c> for
    /// <c>cis2.</c>) and it collapses to 2.51 even though the shifted head stays. So the
    /// push is the DOT's skyline, not the head's, and a dot-blind gate cannot price it.
    /// (The absolute gap will differ from LilyPond's while the voice-three cascade shift
    /// differs — Lily# draws this cis at +1.30, LilyPond at +0.65 — a separate,
    /// pre-existing note-collision question; this pass prices the geometry Lily#
    /// actually draws.)
    /// </para>
    /// <para>
    /// ⚠️ Same-voice pairs with no shift are SKIPPED: the per-voice loop has already
    /// priced them through these same two functions, and a same-voice pair is
    /// shift-invariant anyway (both ends carry the voice's own dx, which cancels in the
    /// distance). Cross-STAFF pairs are never formed — that is the false collision the
    /// per-voice loop was narrowed to avoid (a triplet in one staff clashing with
    /// straight eighths in another, see its comment); the SAME-staff frame is LilyPond's
    /// own, one separation item per staff.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One collision-offsets computation per STAFF object, not per measure:
    /// <see cref="ElementCoordinator.ComputeVoiceOffsets"/> walks every measure of
    /// every voice, and this decoration runs once per measure in BOTH the break gate
    /// and the system layout — calling it inline made the pass O(measures²) per staff
    /// (MEASURED: +26% end-to-end on a 120-bar two-voice book, 1991→2514 ms). The
    /// offsets derive purely from the staff's immutable Voices, so one computation per
    /// Staff instance is exact; a model rebuild makes new Staff objects and refills.
    /// </summary>
    /// <remarks>
    /// Keyed on the VOICES, not on the <see cref="Model.Staff"/> that holds them, so that the
    /// BEAM frame can read this same answer: <c>ElementCoordinator.LayoutBeams</c> is handed a
    /// per-staff <c>Score</c> (<c>MultiStaffLayouter.StaffBeamScoreOf</c>) and never sees the
    /// Staff, and a fourth computation of one quantity is how the third one started
    /// (HANDOFF §5.2.1②). Every producer passes <c>staff.Voices</c> through unchanged, so the
    /// underlying array is one object per staff and the memo is exactly as sharp as before.
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Model.Voice[], ImmutableDictionary<VoiceItemKey, double>> s_staffVoiceOffsets = new();

    /// <summary>
    /// The staff's note-collision X shifts, settled once per staff — THE answer every
    /// consumer reads (this spacing pass, the skyline seed that must reserve a
    /// shifted voice where it is DRAWN, and the beam frame whose stems stand on those
    /// shifted heads). Keys are 1-based VoiceId, matching
    /// VoiceCollector / the renderer's VoiceItemKey.
    /// </summary>
    internal static ImmutableDictionary<VoiceItemKey, double> VoiceCollisionShiftsOf(
        Model.Staff staff)
        => VoiceCollisionShiftsOf(staff.Voices);

    /// <inheritdoc cref="VoiceCollisionShiftsOf(Model.Staff)"/>
    internal static ImmutableDictionary<VoiceItemKey, double> VoiceCollisionShiftsOf(
        ImmutableArray<Model.Voice> voices)
    {
        // A single voice collides with nothing: ComputeVoiceOffsets returns Empty for it
        // anyway, and answering here keeps the one-voice book — every book, mostly — off the
        // table entirely.
        if (voices.Length < 2)
            return ImmutableDictionary<VoiceItemKey, double>.Empty;

        var key = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(voices)!;
        if (s_staffVoiceOffsets.TryGetValue(key, out var cached))
            return cached;

        var computed = ElementCoordinator.ComputeVoiceOffsets(voices).VoiceOffsets;
        return s_staffVoiceOffsets.GetValue(key, _ => computed);
    }

    internal static ImmutableArray<Spring> ApplyCrossVoiceColumnSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        Model.Staff staff,
        int measureIndex)
    {
        var staffVoices = staff.Voices;
        if (staffVoices.Length < 2 || springs.Length != timings.Count + 1)
            return springs;

        var offsets = VoiceCollisionShiftsOf(staff);

        // The items STARTING at each timing column, each with the shift it is drawn at.
        // VoiceId is 1-based, matching VoiceCollector / the renderer's VoiceItemKey.
        var columns = new List<(MusicItem Item, double Shift, int Voice)>?[timings.Count];
        for (int v = 0; v < staffVoices.Length; v++)
        {
            if (measureIndex >= staffVoices[v].Measures.Length)
                continue;
            var items = staffVoices[v].Measures[measureIndex].Items;
            var onset = Fraction.Zero;
            for (int oi = 0; oi < items.Length; oi++)
            {
                var item = items[oi];
                // Only what LilyPond boxes: a note, a chord, a DRAWN rest. A spacer
                // (`s`) engraves no grob at all, so it has no separation box — pairing
                // it here floored a real note against a phantom head at the middle
                // line. Same gate as IsMusicalColumn.
                if (IsMusicalColumn(item))
                    for (int t = 0; t < timings.Count; t++)
                        if (timings[t] == onset)
                        {
                            (columns[t] ??= new()).Add((item,
                                offsets.GetValueOrDefault(
                                    new VoiceItemKey(measureIndex, v + 1, oi)),
                                v));
                            break;
                        }
                onset += item.Duration;
            }
        }

        var result = springs.ToBuilder();
        bool widened = false;
        // Each entry's facing skyline is built ONCE per pair side it joins — an entry
        // faces at most one pair as the LEFT item and one as the RIGHT, and skyline
        // construction is the expensive part of a pair (MEASURED: building them
        // per-pair was most of this pass's ~100 ms on a 120-bar two-voice book).
        // SkylineFloorPair keeps both clamps in the one home the item-pair helpers use.
        var rightSkyOf = new Dictionary<(int Col, int Entry), HorizontalSkyline>();
        var leftSkyOf = new Dictionary<(int Col, int Entry), HorizontalSkyline>();
        for (int t = 1; t < timings.Count; t++)
        {
            if (columns[t - 1] is not { } left || columns[t] is not { } right)
                continue;
            double maxSky = 0, maxRod = 0;
            for (int li = 0; li < left.Count; li++)
                for (int ri = 0; ri < right.Count; ri++)
                {
                    var l = left[li];
                    var r = right[ri];
                    if (l.Voice == r.Voice && l.Shift == 0 && r.Shift == 0)
                        continue;
                    // Column-origin frame, each item's head-left at its collision shift —
                    // the same frame CalculateSkylineDistance / SeparationRodDistance use.
                    if (!rightSkyOf.TryGetValue((t - 1, li), out var rs))
                        rightSkyOf[(t - 1, li)] = rs =
                            ItemSkylineFactory.CreateRightSkylineAtColumn(l.Item, l.Shift, 0);
                    if (!leftSkyOf.TryGetValue((t, ri), out var ls))
                        leftSkyOf[(t, ri)] = ls =
                            ItemSkylineFactory.CreateLeftSkylineAtColumn(r.Item, r.Shift, 0);
                    var (sky, rod) = SkylineFloorPair(rs, ls);
                    maxSky = Math.Max(maxSky, sky);
                    maxRod = Math.Max(maxRod, rod);
                }
            if (maxSky <= result[t].MinDistance && maxRod <= result[t].MinDistance)
                continue;
            // The same three steps the per-voice loop takes, in the same order: raise
            // the spring minimum, re-floor the ideal at min + 0.3 (merge_springs'
            // headroom), then the rod, which binds only under compression.
            var s = result[t].EnsureMinDistance(maxSky);
            s = ApplyMergeSpringsHeadroom(s);
            result[t] = s.EnsureMinDistance(maxRod);
            widened = true;
        }
        return widened ? result.ToImmutable() : springs;
    }

    /// <summary>
    /// Reserves the sideways reach of a wide, always-outside script (a fermata or
    /// ornament) in the shared note columns, so a fermata over one note does not
    /// crowd the next note's accidental or head. The reservation is a SKYLINE
    /// distance, so it only widens where the script's glyph and the neighbour's
    /// ink overlap VERTICALLY — a fermata high above the staff leaves a low
    /// following note's spacing untouched, exactly as LilyPond's Script grob
    /// joins the note column's horizontal skyline only at its own Y band. Scripts
    /// live in a separate collection keyed by (staff, measure, item); this aligns
    /// them to columns by onset, like <see cref="ApplyTabChordSpacing"/>. Narrow
    /// scripts contribute no box (see <see cref="ArticulationEngraver.SpacingInkBox"/>),
    /// so most articulation fixtures are left exactly as before.
    /// LILYPOND-REF: lily/separation-item.cc set_distance() — every grob in the
    ///   note column (Script included) feeds the column's horizontal skyline.
    /// </summary>
    public static ImmutableArray<Spring> ApplyArticulationSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> timings,
        Model.Measure measure,
        ImmutableArray<ArticulationItem> articulations,
        int measureIndex,
        int staffIndex)
    {
        if (articulations.IsDefaultOrEmpty || springs.Length != timings.Count + 1)
            return springs;

        // Per column: the note/chord starting at that onset, and any wide-script
        // ink boxes it carries (skyline frame: column at X=0, middle line Y=0).
        var colItem = new MusicItem?[timings.Count];
        var colBoxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>?[timings.Count];
        bool any = false;
        Fraction onset = Fraction.Zero;
        for (int oi = 0; oi < measure.Items.Length; oi++)
        {
            var item = measure.Items[oi];
            // A grace column carries no script of its own (LYS4020 drops them) and is not
            // the column a script on the MAIN note reaches from — and standing first at the
            // shared moment, it would be the one `??=` kept.
            if (item is (Model.NoteItem or Model.ChordItem) and not { GraceTime: true })
                for (int t = 0; t < timings.Count; t++)
                {
                    if (timings[t] != onset)
                        continue;
                    colItem[t] ??= item;
                    foreach (var art in articulations)
                    {
                        if (art.StaffIndex != staffIndex || art.MeasureIndex != measureIndex
                            || art.ItemIndex != oi)
                            continue;
                        if (ArticulationEngraver.SpacingInkBox(art, item, staffY: 0) is { } box)
                        {
                            (colBoxes[t] ??= new()).Add(box);
                            any = true;
                        }
                    }
                    break;
                }
            onset += item.Duration;
        }
        if (!any)
            return springs;

        // Clear the script from the neighbouring column by LilyPond's script-to-grob
        // gap (each side's extra-spacing-width), not the wider generic item gap — so a
        // fermata sits the LP distance from the next note's accidental, not further.
        double gap = ArticulationSpacing.ScriptToNeighbourGap;
        var result = springs.ToBuilder();
        void Widen(int idx, double needed)
        {
            var s = result[idx];
            // A reservation is a ROD: it moves the minimum (and the ideal up to it) and
            // neither strength — LilyPond's add_rod raises blocking forces only. The
            // 3-argument constructor here used to reset the compress strength to
            // ideal − min, so a rest → note spring under a chord symbol blocked at
            // −1.0 where LilyPond's blocks at −0.62 (Freedom bars 69-76, session 323).
            // LILYPOND-REF: lily/simple-spacer.cc:90-127 Simple_spacer::add_rod.
            // ⚠️ A rod that reaches the ideal leaves min == ideal, and LilyPond's blocking
            // force is then 0 ONLY because the compress strength is 0 (spring.cc:78-82):
            // compress_line takes a blocking-0 spring as already blocked, never adds its
            // flexibility, and subtracts it anyway — a positive strength there breaks the
            // solve (Simple_spacer::compress_line, 想い人 bars 84-87 read "does not fit").
            // So the strength survives only while the rod stays under the ideal.
            if (needed > s.MinDistance)
                result[idx] = new Spring(
                    Math.Max(s.IdealDistance, needed), needed,
                    s.InverseStretchStrength,
                    needed >= s.IdealDistance ? 0.0 : s.InverseCompressStrength);
        }

        // The between-column spring t+1 spans colItem[t] → colItem[t+1]. A script
        // on the LEFT column reaches RIGHT into the right column's left ink; a
        // script on the RIGHT column reaches LEFT over the left column's right ink.
        for (int t = 0; t + 1 < timings.Count; t++)
        {
            var left = colItem[t];
            var right = colItem[t + 1];
            if (left is null || right is null)
                continue;
            double needed = 0;
            if (colBoxes[t] is { } lb)
            {
                double d = HorizontalSkyline.FromBoxes(lb, HorizontalDirection.Right)
                    .Distance(ItemSkylineFactory.CreateLeftSkyline(right, 0, 0));
                if (!double.IsNegativeInfinity(d))
                    needed = Math.Max(needed, d + gap);
            }
            if (colBoxes[t + 1] is { } rb)
            {
                double d = ItemSkylineFactory.CreateRightSkyline(left, 0, 0)
                    .Distance(HorizontalSkyline.FromBoxes(rb, HorizontalDirection.Left));
                if (!double.IsNegativeInfinity(d))
                    needed = Math.Max(needed, d + gap);
            }
            if (needed > 0)
                Widen(t + 1, needed);
        }
        return result.ToImmutable();
    }


    // ========================================
    // Skyline Generation
    // ========================================

}
