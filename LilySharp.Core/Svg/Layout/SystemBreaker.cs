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
/// Breaks measures into systems (lines) using optimal or greedy algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/constrained-breaking.cc
/// LILYPOND-REF: lily/page-breaking.cc (break decisions)
/// </remarks>
internal sealed class SystemBreaker
{
    private readonly LayoutOptions _options;

    public SystemBreaker(LayoutOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// The prefix width the gate charges a FIRST line — indent excluded, since that is the
    /// caller's own option rather than anything the score engraves.
    /// </summary>
    /// <remarks>
    /// Exposed so the gate's model can be asserted against the layout's instead of being
    /// re-derived beside it (SpacingInvariantTests.BreakGateAndLayout_PriceTheSameLineStart)
    /// and so IncrementalCompiler's skip fingerprint reads the same inputs.
    /// </remarks>
    internal static double GateFirstPrefixWidth(MultiStaffScore score, double maxClefWidth) =>
        SpacingRules.CalculatePrefixWidth(
            maxClefWidth, SpacingRules.WidestActiveKeyInk(score, 0),
            includeTimeSignature: SpacingRules.AnyStaffEngravesTime(score),
            score.TimeSignature.NumeratorText, score.TimeSignature.DenominatorText);

    /// <summary>The same for a CONTINUATION line, which carries clef and key but no meter.</summary>
    internal static double GateContinuationPrefixWidth(MultiStaffScore score, double maxClefWidth) =>
        SpacingRules.CalculatePrefixWidth(
            maxClefWidth, SpacingRules.WidestActiveKeyInk(score, 0),
            includeTimeSignature: false);

    /// <summary>
    /// Breaks measures into systems for a multi-staff score.
    /// Uses the primary voice of the first staff group for measure widths.
    /// </summary>
    public List<List<Measure>> BreakIntoSystems(MultiStaffScore score,
                                                double? baseShortestDuration = null,
                                                IReadOnlyList<int>? precomputedLineSizes = null,
                                                MeasureSpringData[]? precomputedSprings = null,
                                                LineBreakDpSession? dpSession = null)
    {
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;

        // F3 incremental cutoff (the F3 incremental design notes §4): when the caller
        // has verified the line-break gate (per-measure spring vector + prefix
        // widths) is unchanged, the break solution cannot change, so regroup the
        // new measures by the cached line sizes and skip the spring computation
        // and the DP entirely. The default path (precomputedLineSizes == null) is
        // byte-identical to before.
        if (precomputedLineSizes != null)
            return RegroupBySizes(measures, precomputedLineSizes);

        // Fold each system's indent into the prefix width so the break decision
        // matches the rendered fit (the layout subtracts the same indent from the
        // available width). Default indent 0 → no-op.
        // LILYPOND-REF: scm/output-lib.scm system-start-text indent.
        // The key column is the UNION of the signatures the staves actually ENGRAVE
        // (SpacingRules.WidestActiveKeyInk — the one model the system layout reserves from,
        // LILYPOND-REF: lily/break-alignment-interface.cc:141-142,242). Reading the SCORE key
        // here instead made the gate a second model: on a grand staff whose upper part is
        // transposed, the score key is C major and books nothing while the staff engraves
        // D major, so the gate under-booked the line start by 2.650000 — the key's 2.2 plus
        // the Clef->Key and Key->Time gaps, which only open when a signature is engraved.
        // A gate that books less than the layout packs a line the layout then cannot set.
        // The all-tab case needs no special key handling any more: a TabStaff engraves no
        // signature, so the union is 0 for it by construction, exactly as LilyPond books
        // nothing for a staff with no Key_engraver (ly/engraver-init.ly:1214).
        // ⚠️ Measure 0, both here and for continuation lines, because the breaker prices ONE
        // continuation prefix for the whole score and does not yet know where lines start.
        // A mid-piece key change therefore still books its OLD width on the lines after it —
        // a separate, pre-existing narrowing of the same shape, and the structural fix is a
        // per-line prefix (MeasureSpringData already carries a per-measure LineStartSpring).
        double maxClefWidth = SpacingRules.MaxClefWidth(score);
        double firstPrefixWidth = GateFirstPrefixWidth(score, maxClefWidth) + _options.Indent;
        double continuationPrefixWidth =
            GateContinuationPrefixWidth(score, maxClefWidth) + _options.ShortIndent;

        if (_options.UseOptimalLineBreaking)
        {
            // The gate prices each line start with the prefix→first-note spring the layout
            // will actually give it — carried per measure on MeasureSpringData.LineStartSpring
            // (ComputeMultiStaffSpringData below, via MultiStaffLayouter.LineStartSpringForLine)
            // and floored at LilyPond's 0.3 + min_dist. That per-line MINIMUM is what the old
            // OverfullPenalty stood in for; with it wired, the penalty is gone and a compressed
            // line LilyPond would keep (e.g. test/ties-slurs' eight bars in ONE system) is no
            // longer forbidden. Section 5.4 / docs/HANDOFF.md §1.
            // LILYPOND-REF: lily/staff-spacing.cc:210-220; lily/spacing-spanner.cc:219-224.
            var breaker = new KnuthPlassBreaker(
                _options.ContentWidth,
                firstPrefixWidth,
                continuationPrefixWidth,
                raggedRight: _options.RaggedRight);

            // The incremental driver has ALREADY built this vector — it is the line-break
            // gate it just compared — so when it hands it over the breaker does not build a
            // second one. Both come from ComputeMultiStaffSpringData(score, shortest) with
            // the same score instance and the same shortest duration (both sides call
            // SpacingRules.CalculateCommonShortestDuration on that score), so the vector is
            // the same object's worth of data either way; passing it is not a new model.
            // MEASURED: 385-780 ms per edit on a 1000-bar book, paid twice.
            var springData = precomputedSprings
                ?? ComputeMultiStaffSpringData(score, baseShortestDuration);
            return breaker.BreakIntoLines(measures, springData, dpSession);
        }

        return BreakIntoSystemsGreedy(measures, firstPrefixWidth, continuationPrefixWidth, baseShortestDuration);
    }

    /// <summary>
    /// The multi-staff line-break gate: each measure priced by the COMBINED
    /// springs across all staves (the same springs the actual system layout
    /// solves with). This vector — with the paper width and prefix widths — is
    /// the sole determinant of the break solution, so the incremental driver
    /// compares it to decide whether line-breaking can be skipped.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc — columns aggregate staves, so pricing
    /// by the primary staff alone would pack lines wherever that staff rests
    /// while another staff is dense.
    /// </remarks>
    /// <param name="memo">Per-measure reuse hook (⒟⁗, HANDOFF §1 ▶): returns the PREVIOUS
    /// edit's <see cref="MeasureSpringData"/> for index <c>i</c> when the incremental
    /// driver has proven it unchanged, or null to compute it here. The proof lives with
    /// the caller (<c>IncrementalCompiler.SpringReusable</c>): a measure's springs read
    /// only measure <c>i</c> (all staves/voices, side-tables, entry context — all folded
    /// into its content key), plus the NEIGHBOURHOOD keys i−1/i+1 (the previous measure's
    /// end bar line via <see cref="SpacingRules.RunLeftBoundBarline"/>, the next measure's
    /// run membership via <see cref="MmrRunMap.ForbidsBreakAfter"/>, and — since the
    /// cross-bar lyric rod port, 2026-08-20 — both neighbours' lyric content: the halves
    /// ReserveLyricLine drops read whether a line continues into i±1, and the pair excess
    /// below reads measure i+1's springs outright), and the score-global common shortest
    /// duration. There is deliberately NO second per-measure implementation: the memo
    /// short-circuits THIS loop body, so a reused and a computed entry come from the one
    /// implementation (§2 A — no second spelling of the same quantity). The cross-bar
    /// PAIR quantity is deliberately carried SPLIT (each measure its own
    /// <see cref="LyricSpacing.LyricBarPricing"/> half, combined only at break time by
    /// KnuthPlassBreaker) so that no stored entry ever reads a neighbour's SPRINGS —
    /// a combined excess would read i+1's springs and through their halves a
    /// neighbour-of-neighbour's lyrics, outside this 3-key window.</param>
    internal static MeasureSpringData[] ComputeMultiStaffSpringData(MultiStaffScore score,
                                                                    double? baseShortestDuration,
                                                                    Func<int, MeasureSpringData?>? memo = null)
    {
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        var layouter = new MeasureLayouter();
        var springData = new MeasureSpringData[measures.Length];

        // Mirror the system layout: a multi-measure rest run is ONE bar, so the
        // measures it swallows are priced at zero and the run-opening measure carries
        // the run rod. A run must also not be split by a line break — LilyPond's
        // compressed MMR is a single spanner between two bar-line columns and cannot
        // span systems — so the interior forbids breaking.
        var runMap = MmrRunMap.ForScore(score);

        bool sung = !score.Lyrics.IsDefaultOrEmpty;

        for (int i = 0; i < measures.Length; i++)
        {
            if (memo?.Invoke(i) is { } reused)
            {
                springData[i] = reused;
                continue;
            }

            var primaryMeasure = measures[i];

            if (runMap.IsInterior(i))
            {
                springData[i] = new MeasureSpringData(0, 0, 0,
                    primaryMeasure.BreakPenalty,
                    runMap.ForbidsBreakAfter(i)
                        ? BreakPermission.Forbid : primaryMeasure.LineBreakPermission);
                continue;
            }

            var allTimings = MultiStaffLayouter.CollectAllTimingsForMeasure(score, i);
            var allMeasures = MultiStaffLayouter.CollectAllMeasuresAtIndex(score, i);
            // Mirror of MultiStaffLayouter: a clef change opening the next measure is
            // drawn before this bar line, so the break gate must price it here too.
            var springs = layouter.CreateTimingSprings(
                primaryMeasure, allTimings, baseShortestDuration, allMeasures,
                i + 1 < measures.Length ? measures[i + 1] : null,
                MultiStaffLayouter.CollectStaffIndicesAtIndex(score, i));

            // The shared-column reservations (lyrics, chords, tab digits, wide
            // scripts) — the SAME list the system layout applies, from the one home
            // (MultiStaffLayouter.ApplySharedColumnReservations). The gate used to
            // carry a hand-mirrored copy of that list and it drifted: tab fret
            // digits were never priced here, so an all-tab book packed onto one
            // system and ran past the page edge.
            springs = MultiStaffLayouter.ApplySharedColumnReservations(
                score, i, springs, primaryMeasure, allTimings, allMeasures);

            // The measure's lyric line edges — the same function the layout reads, off the
            // same reserved springs — for three prices: its half of the cross-bar PAIR
            // pricing (combined with the neighbour's half at break time), the line-start
            // lyric floor, and the line-END excess: when a continuing line's trailing half
            // was dropped, the layout re-supplies it as a rod at a system's end
            // (LineEndLyricReservation), so a candidate line ending here must be priced
            // for the part of that rod the suffix springs' minima do not already cover.
            var edges = ImmutableArray<LyricSpacing.LyricLineEdge>.Empty;
            LyricSpacing.LyricBarPricing? barPricing = null;
            double lineEndLyricMinExcess = 0;
            if (sung)
            {
                edges = LyricSpacing.MeasureLineEdges(
                    score.TextMetrics, springs, allTimings, i, ScoreSideTables.Lyrics(score),
                    score.IsLeadSheet,
                    SpacingRules.ParentAlignmentEdgesPerColumn(allMeasures, allTimings),
                    // The same per-voice edges the layout reads (one list, section 5.4).
                    LyricSpacing.OwnVoiceEdgeProvider(score));
                barPricing = LyricSpacing.BuildBarPricing(edges, springs,
                    SpacingRules.GetBarlineWidth(primaryMeasure.StartBarline),
                    SpacingRules.GetBarlineWidth(primaryMeasure.EndBarline));
                if (barPricing != null)
                {
                    // Lines[k] is built from edges[k] (BuildBarPricing preserves order).
                    for (int k = 0; k < edges.Length; k++)
                        if (edges[k].ContinuesIntoNext)
                            lineEndLyricMinExcess = Math.Max(lineEndLyricMinExcess,
                                LyricSpacing.LineEndLyricReservation(edges[k])
                                - barPricing.Lines[k].SuffixMin);
                    lineEndLyricMinExcess = Math.Max(0, lineEndLyricMinExcess);
                }
            }

            double ideal = 0, min = 0, invStretch = 0, invCompress = 0;
            foreach (var s in springs)
            {
                ideal += s.IdealDistance;
                min += s.MinDistance;
                invStretch += s.InverseStretchStrength;
                // The compression Hooke sum is its own number, not the stretch one:
                // LilyPond's compress_line sums inverse_compress_strength where
                // expand_line sums inverse_stretch_strength (simple-spacer.cc:207-287).
                invCompress += s.InverseCompressStrength;
            }
            // The run rod, priced exactly as the layouter prices it, so the break gate
            // and the layout agree on how wide a multi-measure rest bar really is.
            if (runMap.TryGetRunStartingAt(i, out var run))
            {
                var measureLength = Fraction.Zero;
                foreach (var item in primaryMeasure.Items)
                    measureLength += item.Duration;
                // Same minimum-distance quantity the layouter uses: LP's
                // Paper_column::minimum_distance is the LEFT bounding column's skyline reach
                // over its break-aligned grobs, NOT the accumulated spring minima this used
                // to pass. The run measure's own bar-line widths (re-added at `barlines`
                // below) are subtracted from the rod so the rod is the run's content span.
                double runBarlineWidth =
                    SpacingRules.GetBarlineWidth(primaryMeasure.StartBarline)
                    + SpacingRules.GetBarlineWidth(primaryMeasure.EndBarline);
                double rod = SpacingRules.MmrRodDistance(
                    run.Count, measureLength,
                    SpacingRules.MmrRodMinimumDistance(
                        SpacingRules.RunLeftBoundBarline(measures, i),
                        primaryMeasure.Items),
                    runBarlineWidth);
                ideal = Math.Max(ideal, rod);
                min = Math.Max(min, rod);
            }

            double barlines = SpacingRules.GetBarlineWidth(primaryMeasure.StartBarline)
                            + SpacingRules.GetBarlineWidth(primaryMeasure.EndBarline);
            // The measure's own spring 0 — the one the layout REPLACES when this measure
            // opens a line (MultiStaffLayouter). Carried so the gate can make the same
            // substitution instead of pricing a line start with a spring the layout will
            // not use. See KnuthPlassBreaker's constructor remarks.
            var s0 = springs.Length > 0 ? springs[0] : null;
            // The prefix→first-note spring this measure would get if it OPENED a line —
            // measure 0 on the first system (which carries the meter), any later measure on a
            // continuation — built by the SAME implementation the layout uses, so the gate
            // prices a candidate line start exactly as it will be laid out (section 5.4).
            var lineStartSpring = s0 is { } spring0
                ? MultiStaffLayouter.LineStartSpringForLine(score, i, isFirstSystem: i == 0, spring0,
                    LyricSpacing.LineStartLyricFloor(edges))
                : null;
            springData[i] = new MeasureSpringData(ideal + barlines, min + barlines, invStretch,
                primaryMeasure.BreakPenalty,
                runMap.ForbidsBreakAfter(i)
                    ? BreakPermission.Forbid : primaryMeasure.LineBreakPermission,
                invCompress,
                s0?.IdealDistance ?? 0, s0?.MinDistance ?? 0,
                s0?.InverseStretchStrength ?? 0, s0?.InverseCompressStrength ?? 0,
                lineStartSpring,
                barPricing,
                lineEndLyricMinExcess);
        }
        return springData;
    }

    /// <summary>
    /// Regroups measures into systems by cached per-line measure counts — the
    /// incremental reuse of a prior break solution when the gate is unchanged.
    /// </summary>
    private static List<List<Measure>> RegroupBySizes(ImmutableArray<Measure> measures, IReadOnlyList<int> sizes)
    {
        var groups = new List<List<Measure>>(sizes.Count);
        int idx = 0;
        foreach (int size in sizes)
        {
            var group = new List<Measure>(size);
            for (int k = 0; k < size && idx < measures.Length; k++)
                group.Add(measures[idx++]);
            groups.Add(group);
        }
        // Defensive: a correct gate guarantees the sizes cover every measure, but
        // never drop measures if a caller misuses the hook — append the remainder.
        if (idx < measures.Length)
        {
            var tail = new List<Measure>(measures.Length - idx);
            while (idx < measures.Length)
                tail.Add(measures[idx++]);
            groups.Add(tail);
        }
        return groups;
    }

    /// <summary>
    /// Breaks measures into systems using a greedy first-fit algorithm.
    /// Break permissions are honored the same way the optimal path does:
    /// <c>break</c> (Force) always ends the system, and a width-driven break
    /// never lands on a Forbid (noBreak) boundary — the Forbid-joined tail
    /// moves to the next system instead, unless the whole system is one chain
    /// (then it stays and goes overfull; permissions are absolute).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc — break_permission_ is a hard
    /// constraint, not a preference weight.
    /// </remarks>
    internal List<List<Measure>> BreakIntoSystemsGreedy(
        ImmutableArray<Measure> measures,
        double firstPrefixWidth,
        double continuationPrefixWidth,
        double? baseShortestDuration = null)
    {
        if (measures.Length == 0)
            return new List<List<Measure>>();

        // The WALK is KnuthPlassBreaker.GreedyBreak — the one implementation of the
        // permission rules, shared with the DP-failure fallback (before the fold this
        // was a second first-fit loop, and the pair had already drifted once: the
        // GreedyBreakPermissionTests remarks record both ignoring Forbid). What stays
        // this mode's own is the WIDTH MODEL: per-measure IDEAL widths, exactly what
        // the replaced loop priced with. Equivalence of the two walks under that model
        // is not just read off the code — the carried-tail edge agrees BECAUSE a
        // carried tail is always an all-Forbid suffix, so the re-checked new line
        // extends past it as one chain to the same boundary the old loop reached —
        // and the four Greedy_* pins below hold it.
        // ⚠️ Known, pre-existing, and deliberately preserved: this mode prices a
        // multi-measure-rest run at its full per-measure widths (no MMR compression,
        // no run-interior Forbid) — HANDOFF names the limitation; the springs built
        // here reproduce it, they do not fix it silently.
        int n = measures.Length;
        var springData = new MeasureSpringData[n];
        var cumIdeal = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            double w = SpacingRules.CalculateMeasureIdealWidth(measures[i], baseShortestDuration);
            springData[i] = new MeasureSpringData(w, w, 0, 0, measures[i].LineBreakPermission);
            cumIdeal[i + 1] = cumIdeal[i] + w;
        }

        var breaker = new KnuthPlassBreaker(
            _options.ContentWidth, firstPrefixWidth, continuationPrefixWidth);
        var breakPoints = breaker.GreedyBreak(springData, cumIdeal);
        return KnuthPlassBreaker.CreateMeasureGroups(measures, breakPoints);
    }
}
