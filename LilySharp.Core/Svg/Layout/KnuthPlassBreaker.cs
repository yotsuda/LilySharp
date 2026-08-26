// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/constrained-breaking.cc
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
//   lily/simple-spacer.cc
//     Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Per-measure spring data for line-breaking force calculations.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/simple-spacer.cc — each column's spring contributes to line force
/// </remarks>
internal readonly record struct MeasureSpringData(
    double IdealWidth,
    double MinWidth,
    double InverseStretchStrength,
    double BreakPenalty = 0,
    BreakPermission BreakPermission = BreakPermission.Allow,
    double InverseCompressStrength = 0,
    double Spring0Ideal = 0,
    double Spring0Min = 0,
    double Spring0Stretch = 0,
    double Spring0Compress = 0,
    // The prefix→first-note spring this measure gets WHEN it opens a line (built by
    // MultiStaffLayouter.LineStartSpringForLine — the same one the layout uses), or null
    // when unknown (the single-staff gate path). ApplyLineStartSpring swaps it for the
    // Spring0* mid-line spring above when this measure is a line's first.
    Spring? LineStartSpring = null,
    // The cross-bar lyric rod's two gate faces (LyricSpacing, HANDOFF 2H, 2026-08-20) —
    // both MINIMUM-only, because a rod raises blocking forces and never ideals:
    // this measure's HALF of the pair pricing (its lyric line edges and the spring
    // minima toward each bar), combined with the NEXT measure's half at break time
    // (LyricSpacing.CrossBarPairMinExcess) into the extra line minimum the rod across
    // their shared bar demands. Carried split so each entry reads only its own measure's
    // springs — see the class's remarks and IncrementalCompiler.SpringReusable...
    LyricSpacing.LyricBarPricing? CrossBarLyricPricing = null,
    // ...and when this measure ENDS the line while a lyric line continues past it (the
    // re-supplied trailing reservation, less the suffix springs' minima). Zero on every
    // unsung measure, so both are inert wherever they were before the port.
    double LineEndLyricMinExcess = 0);

/// <summary>
/// Knuth-Plass optimal line breaking algorithm for music scores.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/constrained-breaking.cc:36-75
/// Uses dynamic programming to find optimal line breaks that minimize total penalty.
///
/// Algorithm overview:
/// Let W(i,j) be the penalty for putting measures i..j on one line.
/// Let D(n,k) be the minimum total penalty for breaking the first n measures into k lines.
/// Then: D(n,k) = min over j { D(j,k-1) + W(j+1,n) }
///
/// LILYPOND-REF: lily/constrained-breaking.cc:224-232
/// Demerits = force² + Δforce² where:
/// - force = (available - ideal_sum) / inverse_stretch_sum
/// - Δforce = force_current - force_previous
/// </remarks>
internal sealed class KnuthPlassBreaker
{
    private readonly double _lineWidth;
    private readonly double _firstPrefixWidth;
    private readonly double _continuationPrefixWidth;
    private readonly double _looseness;
    private readonly bool _raggedRight;

    /// <summary>
    /// Penalty for infinite badness (line cannot be set).
    /// </summary>
    private const double Infinity = 1e18;

    /// <summary>
    /// Creates a new Knuth-Plass line breaker.
    /// </summary>
    /// <param name="lineWidth">Available width for content.</param>
    /// <param name="firstPrefixWidth">Width of prefix on first line (clef, key, time).</param>
    /// <param name="continuationPrefixWidth">Width of prefix on continuation lines (clef, key).</param>
    /// <param name="looseness">Prefer more lines (positive) or fewer (negative).</param>
    /// <param name="raggedRight">If true, exclude Δforce² from demerits (ragged-right mode).</param>
    /// <remarks>
    /// The line-start substitution the gate must make — replacing a line's first measure's
    /// bar-line spring 0 with the prefix→first-note spring the layout gives it — is carried
    /// PER MEASURE on <see cref="MeasureSpringData.LineStartSpring"/> (built by
    /// MultiStaffLayouter.LineStartSpringForLine, the one implementation the system layout
    /// also uses), not passed here. That is section 5.4's "the two spring systems and the
    /// break gate must agree", honoured at the one place the gate exists to predict.
    /// LILYPOND-REF: lily/spacing-spanner.cc:219-224 — LilyPond generates the spacing for the
    /// PREBROKEN pieces of a column, so a candidate line start is priced with the spring it
    /// would really get.
    /// </remarks>
    public KnuthPlassBreaker(
        double lineWidth,
        double firstPrefixWidth,
        double continuationPrefixWidth,
        double looseness = 0,
        bool raggedRight = false)
    {
        _lineWidth = lineWidth;
        _firstPrefixWidth = firstPrefixWidth;
        _continuationPrefixWidth = continuationPrefixWidth;
        _looseness = looseness;
        _raggedRight = raggedRight;
    }

    /// <summary>
    /// Finds optimal line breaks for measures.
    /// </summary>
    /// <param name="measures">Measures to break into lines.</param>
    /// <param name="baseShortestDuration">Optional spacing base-shortest-duration override
    /// (staff-space scaling of note spacing); null uses the score default.</param>
    /// <returns>List of measure groups, each representing one line.</returns>
    public List<List<Measure>> BreakIntoLines(IReadOnlyList<Measure> measures,
                                               double? baseShortestDuration = null)
    {
        if (measures.Count == 0)
            return new List<List<Measure>>();

        // Calculate spring data for each measure (includes break permission/penalty)
        var springData = ComputeMeasureSpringData(measures, baseShortestDuration);

        // Find optimal number of lines and break points
        var breakPoints = FindOptimalBreaks(springData);

        // Convert break points to measure groups
        return CreateMeasureGroups(measures, breakPoints);
    }

    /// <summary>
    /// Breaks with PRECOMPUTED per-measure spring data. Multi-staff scores
    /// must price each measure by the COMBINED springs of all staves —
    /// pricing by the primary staff alone packs lines wherever that staff
    /// happens to rest while another staff is dense.
    /// LILYPOND-REF: lily/paper-column.cc — columns aggregate all staves,
    /// so constrained breaking sees the combined springs.
    /// </summary>
    internal List<List<Measure>> BreakIntoLines(IReadOnlyList<Measure> measures,
                                                MeasureSpringData[] springData)
    {
        if (measures.Count == 0)
            return new List<List<Measure>>();
        var breakPoints = FindOptimalBreaks(springData);
        return CreateMeasureGroups(measures, breakPoints);
    }

    /// <summary>
    /// Computes spring data for each measure from its internal springs — the F3
    /// design's <c>measure_natural_width</c> vector, i.e. the SOLE input (with
    /// paper width) to the global line-break DP <see cref="FindOptimalBreaks"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc — spring parameters for force calculation
    /// Each measure's springs are summed to produce aggregate spring data.
    ///
    /// internal (not private) so it can be exercised as the line-break gate:
    /// because <c>FindOptimalBreaks</c> depends only on this vector, an edit that
    /// leaves every measure's <see cref="MeasureSpringData"/> unchanged cannot
    /// change the break solution, so line-breaking can be skipped on that edit
    /// (the F3 incremental design notes §4 — measure_natural_width is the cutoff).
    /// </remarks>
    internal static MeasureSpringData[] ComputeMeasureSpringData(IReadOnlyList<Measure> measures,
                                                                double? baseShortestDuration = null)
    {
        var data = new MeasureSpringData[measures.Count];
        for (int i = 0; i < measures.Count; i++)
        {
            var m = measures[i];

            // Build the measure's springs ONCE and derive all three sums from them.
            // (Previously CalculateMeasureIdealWidth built the springs internally and
            // then CreateSpringsForMeasure built them again — the same work twice.)
            double startBar = SpacingRules.GetBarlineWidth(m.StartBarline);
            double endBar = SpacingRules.GetBarlineWidth(m.EndBarline);
            var springs = SpacingRules.CreateSpringsForMeasure(m, baseShortestDuration);

            double idealWidth = startBar + endBar;  // matches CalculateMeasureIdealWidth
            double inverseStretch = 0;
            double inverseCompress = 0;
            double minWidth = startBar + endBar;
            foreach (var spring in springs)
            {
                idealWidth += spring.IdealDistance;
                inverseStretch += spring.InverseStretchStrength;
                inverseCompress += spring.InverseCompressStrength;
                minWidth += spring.MinDistance;
            }
            // An empty placeholder has no springs; give it its full-bar space (a
            // LilyPond MMR-style ROD, so it holds under compression too) — matching
            // CalculateMeasureIdealWidth. Absent-voice filler measures stay at zero:
            // they inherit their width from the sibling staves' same measure.
            if (m.Items.Length == 0 && m.IsEmptyPlaceholder)
            {
                double slot = SpacingRules.EmptyPlaceholderContentWidth();
                idealWidth += slot;
                minWidth += slot;
            }

            // LILYPOND-REF: lily/constrained-breaking.cc:112-113 — break_penalty_ propagation
            var s0 = springs.Length > 0 ? springs[0] : null;
            data[i] = new MeasureSpringData(idealWidth, minWidth, inverseStretch,
                m.BreakPenalty, m.LineBreakPermission, inverseCompress,
                s0?.IdealDistance ?? 0, s0?.MinDistance ?? 0,
                s0?.InverseStretchStrength ?? 0, s0?.InverseCompressStrength ?? 0);
        }
        return data;
    }

    /// <summary>
    /// Calculates the force for a line containing the given spring data.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:175-198 solve — the line is expanded or
    /// compressed depending on which side of the natural length it falls, and the two
    /// sides use DIFFERENT Hooke sums: expand_line (:207-223) sums
    /// <c>inverse_stretch_strength</c>, compress_line (:232-287) sums
    /// <c>inverse_compress_strength</c>.
    /// <para>
    /// A spring's two strengths are not the same number — LilyPond's defaults are
    /// <c>ideal</c> for stretch and <c>ideal - min</c> for compress (spring.cc:205-216),
    /// and Lily#'s note springs likewise carry <c>max(0.1, ideal - increment)</c> against
    /// <c>ideal - min</c>. Dividing a compression by the STRETCH sum, as this used to,
    /// understates |force| by however much the two differ and makes an overfull line look
    /// cheaper than it is.
    /// </para>
    /// <para>
    /// ⚠️ Still an approximation of compress_line, and knowingly: LilyPond walks the
    /// individual springs in blocking-force order and drops each out of the Hooke sum as it
    /// reaches its minimum, which needs per-spring data. The break gate deliberately carries
    /// only per-measure SUMS (see <see cref="MeasureSpringData"/> — the F3 incremental gate
    /// compares that vector), so this is the linear part of compress_line, exact until the
    /// first spring blocks.
    /// </para>
    /// </remarks>
    internal static double CalculateLineForce(
        double availableWidth, double idealSum, double inverseStretchSum,
        double inverseCompressSum)
    {
        bool compressing = availableWidth < idealSum;
        double invHooke = compressing ? inverseCompressSum : inverseStretchSum;

        if (invHooke < 1e-6)
            return compressing ? double.NegativeInfinity : 0;

        return (availableWidth - idealSum) / invHooke;
    }

    /// <summary>
    /// Finds optimal break points using dynamic programming with force-based demerits.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:83-126, 224-232
    /// Demerits = force² + Δforce² + break_penalty_
    ///
    /// 1-1: ragged_right excludes Δforce² (lily/constrained-breaking.cc:568-573)
    /// 1-2: break_penalty_ from Measure added to demerits (lily/constrained-breaking.cc:112-113)
    /// 1-3: looseness selects solution with line_count closest to optimal+looseness
    /// 1-4: break permission forbid/force (lily/include/constrained-breaking.hh:74)
    /// </remarks>
    private List<int> FindOptimalBreaks(MeasureSpringData[] springData)
    {
        int n = springData.Length;

        // Precompute cumulative sums for fast range queries
        var cumIdeal = new double[n + 1];
        var cumInvStretch = new double[n + 1];
        var cumInvCompress = new double[n + 1];
        var cumMin = new double[n + 1];
        // ...and the same idiom for the FORCED-BREAK question below, which is a range query
        // like the others and was being answered by re-scanning the range: "is there a Force
        // permission strictly inside this candidate line". That scan costs O(j - i) per pair
        // and so O(n³) over the DP — MEASURED at exactly n³/6 iterations (166,666,500 for a
        // 1000-bar book, 4,499,950 for a 300-bar one). A prefix count answers it in O(1) and
        // is the same predicate: the scan covered k in [i, j-2], which is nonempty exactly
        // when the count over that range is positive.
        var cumForce = new int[n + 1];
        // Cross-bar lyric pair minima: entry i is the extra MINIMUM paid when measures i
        // and i+1 share a line — the two halves each entry carries, joined here (this is
        // the only place the halves meet, so no stored entry ever reads a neighbour's
        // springs). A line i..j−1 collects the pairs strictly inside it:
        // cumPairMin[j−1] − cumPairMin[i]. All zero on an unsung score.
        var cumPairMin = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            cumIdeal[i + 1] = cumIdeal[i] + springData[i].IdealWidth;
            cumInvStretch[i + 1] = cumInvStretch[i] + springData[i].InverseStretchStrength;
            cumInvCompress[i + 1] = cumInvCompress[i] + springData[i].InverseCompressStrength;
            cumMin[i + 1] = cumMin[i] + springData[i].MinWidth;
            cumPairMin[i + 1] = cumPairMin[i] + (i + 1 < n
                ? LyricSpacing.CrossBarPairMinExcess(
                    springData[i].CrossBarLyricPricing, springData[i + 1].CrossBarLyricPricing)
                : 0);
            cumForce[i + 1] = cumForce[i]
                + (springData[i].BreakPermission == BreakPermission.Force ? 1 : 0);
        }

        // The DP state is (break index, LINE COUNT), as LilyPond's is (break index, system
        // index): dp[j, k] is the least demerits for measures 0..j-1 in exactly k lines,
        // and lineForce[j, k] is that solution's LAST line force, which is what the next
        // line charges its (prev - force)² against.
        // LILYPOND-REF: lily/constrained-breaking.cc:80-124 calc_subproblem — st.at (j, sys)
        //   carries demerits_, details_ (the line, force included) and prev_.
        int cols = n + 1;
        var dp = new double[(n + 1) * cols];
        var prev = new int[(n + 1) * cols];
        var lineForce = new double[(n + 1) * cols];
        Array.Fill(dp, Infinity);
        Array.Fill(prev, -1);
        dp[0] = 0;          // no measures in no lines
        lineForce[0] = 0;   // no previous line

        // The REACHABLE line-count band per break index: minLines[i]..maxLines[i] brackets
        // every k with dp[i, k] < Infinity (holes inside the band keep the guard below).
        // A break's states are all written while the outer loop stands at j == that break,
        // and only read at j > it, so the band is stable by the time it is read. Iterating
        // k outside the band skips only iterations whose sole act was the dp[from] >=
        // Infinity `continue` — the k loop has no other effect for an unreachable state —
        // which is what makes this bounded walk output-identical by construction rather
        // than by measurement. MEASURED (session 135, before this): 6,979,000 k-iterations
        // per 1000-bar break, 3,995,965 of them (57%) that empty `continue`.
        var minLines = new int[n + 1];
        var maxLines = new int[n + 1];
        Array.Fill(minLines, int.MaxValue);
        Array.Fill(maxLines, int.MinValue);
        minLines[0] = 0;
        maxLines[0] = 0;

        for (int j = 1; j <= n; j++)
        {
            for (int i = 0; i < j; i++)
            {
                // A start no line count reaches prices to nothing: every k below would hit
                // the dp[from] >= Infinity `continue`, so the whole line pricing for this
                // (i, j) is dead work. Same construction argument as the band itself.
                if (minLines[i] > maxLines[i])
                    continue;

                // 1-4: Check break permission — skip if Forbid at break point i (except start)
                // LILYPOND-REF: lily/include/constrained-breaking.hh:73 break_permission_
                if (i > 0 && springData[i - 1].BreakPermission == BreakPermission.Forbid)
                    continue;

                // Skip if there's a forced break between i and j-1 (exclusive)
                // 1-4: Also skip if there's a Force permission in the middle
                if (cumForce[j - 1] - cumForce[i] > 0)
                    continue;

                bool isFirstLine = i == 0;
                double prefixWidth = isFirstLine ? _firstPrefixWidth : _continuationPrefixWidth;
                double availableWidth = _lineWidth - prefixWidth;

                // Compute line spring totals via cumulative sums. The minimum also pays
                // the cross-bar lyric rods this line's interior bars carry, plus the
                // re-supplied trailing reservation when a lyric line runs past its end —
                // the same two quantities the layout's ApplyRods will enforce, so a sung
                // bar is priced for breaking exactly as it will be laid out.
                double idealSum = cumIdeal[j] - cumIdeal[i];
                double invStretchSum = cumInvStretch[j] - cumInvStretch[i];
                double invCompressSum = cumInvCompress[j] - cumInvCompress[i];
                double minSum = cumMin[j] - cumMin[i]
                    + (cumPairMin[j - 1] - cumPairMin[i])
                    + springData[j - 1].LineEndLyricMinExcess;

                // The line's FIRST measure is priced with the prefix→first-note spring the
                // layout will actually give it, not with the bar-line spring it carries as
                // a mid-line measure. See the constructor's remarks.
                ApplyLineStartSpring(springData[i],
                    ref idealSum, ref minSum, ref invStretchSum, ref invCompressSum);

                // ⚠️ There is deliberately NO "this line is too underfull to consider" rule
                // here. LilyPond has none: an underfull line is PRICED — a big positive
                // force, squared into the demerits like any other, or the leftover
                // whitespace when ragged (Simple_spacer::force_penalty,
                // lily/simple-spacer.cc:308-320) — and a line is dropped only when it cannot
                // be SET at all, which is the impossible-line rule below.
                // One used to stand here, excluding any non-last line whose ideal was under
                // availableWidth / (tolerance * 2). It was removed 2026-07-25 with no
                // observable effect at all: 3286 tests and 87 ledger points unmoved, no
                // snapshot touched.
                double effectiveWidth = Math.Max(idealSum, minSum);
                double force = CalculateLineForce(
                    availableWidth, effectiveWidth, invStretchSum, invCompressSum);

                // Simple_spacer::range_solve's fits_: compress_line runs out of springs to
                // block while the line is still longer than the target — that is, the total
                // MINIMUM does not fit. Ragged additionally refuses any compression at all.
                // A -inf force is the same condition reached through zero flexibility.
                // LILYPOND-REF: lily/simple-spacer.cc:285 (compress_line's fits_ = false),
                //   :200-201 (ragged && force < 0).
                bool fits = minSum <= availableWidth
                            && !double.IsNegativeInfinity(force)
                            && !(_raggedRight && force < 0);

                // An unsettable line is IMPOSSIBLE, not expensive: it is dropped, and so is
                // every LONGER line from the same start (their minima only grow — LilyPond
                // stops extending outright). The one exception is a line spanning a SINGLE
                // breakpoint: there is nothing left to split, so it is kept at -200000.
                // LILYPOND-REF: lily/simple-spacer.cc:509-516; the drop is
                //   lily/constrained-breaking.cc:98-99 (isinf force → stop lengthening).
                double penalizedForce;
                if (!fits)
                {
                    if (j != i + 1)
                        continue;
                    penalizedForce = -200000;
                }
                else
                {
                    penalizedForce = ForcePenalty(availableWidth, force, effectiveWidth);
                }

                // What LilyPond stores per line, and what combine_demerits squares, is the
                // PENALISED force, not the raw one.
                // LILYPOND-REF: lily/simple-spacer.cc:506-507 → Line_details::force_.
                double storedForce = penalizedForce;
                double penalty = penalizedForce * penalizedForce;

                // 1-2: LILYPOND-REF: lily/constrained-breaking.cc:112-113
                // Add break_penalty_ from the measure at the break point
                if (j < n)
                {
                    penalty += springData[j - 1].BreakPenalty;
                }

                if (penalty >= Infinity)
                    continue;

                // Predecessor states live at (i, k - 1), so k walks the band shifted by
                // one. The guard stays: the band brackets the reachable set, it does not
                // promise it is gapless.
                for (int k = minLines[i] + 1; k <= maxLines[i] + 1; k++)
                {
                    int from = i * cols + (k - 1);
                    if (dp[from] >= Infinity)
                        continue;

                    // 1-1: LILYPOND-REF: lily/constrained-breaking.cc:568-573
                    // combine_demerits: force² + (prev - force)², or force² alone when
                    // ragged (consecutive line uniformity does not matter unjustified).
                    // prev is the force of THIS predecessor state's own last line, which
                    // is why the force has to be carried per (break, line count).
                    double linePenalty = penalty;
                    if (!_raggedRight)
                    {
                        double deltaForce = storedForce - lineForce[from];
                        linePenalty += deltaForce * deltaForce;
                    }

                    double total = dp[from] + linePenalty;
                    int to = j * cols + k;
                    if (total < dp[to])
                    {
                        dp[to] = total;
                        prev[to] = i;
                        lineForce[to] = storedForce;
                        if (k < minLines[j]) minLines[j] = k;
                        if (k > maxLines[j]) maxLines[j] = k;
                    }
                }
            }
        }

        // LilyPond's best_solution: walk the line count UPWARD and stop as soon as another
        // line fails to improve AND nothing in that solution is compressed.
        // LILYPOND-REF: lily/constrained-breaking.cc:224-260 — the `too_many_lines` early
        //   return at :245-254 is the load-bearing part.
        // ⚠️ Minimising total demerits alone does NOT reproduce this: every line
        // contributes force², so splitting into more lines shrinks every force and with it
        // the total, and the search drifts to more systems than LilyPond uses. Measured
        // before this was ported — 4 systems where LilyPond takes 3, ledger point
        // justified.first-system.heads.
        List<int>? best = null;
        double bestDemerits = Infinity;
        int bestLines = 0;
        for (int k = 1; k <= n; k++)
        {
            double demerits = dp[n * cols + k];
            if (demerits >= Infinity)
                continue;

            if (demerits < bestDemerits)
            {
                bestDemerits = demerits;
                bestLines = k;
                best = BacktrackByLineCount(n, cols, prev, k);
            }
            else if (!HasCompressedLine(n, cols, prev, lineForce, k))
            {
                break;
            }
        }

        if (best == null)
            return GreedyBreak(springData, cumMin);

        // 1-3: LILYPOND-REF: lily/constrained-breaking.cc looseness — bias the chosen line
        // count, taking the solution for that count when it is reachable.
        if (_looseness != 0)
        {
            int target = Math.Max(1, bestLines + (int)_looseness);
            if (target <= n && dp[n * cols + target] < Infinity)
                return BacktrackByLineCount(n, cols, prev, target) ?? best;
        }

        return best;
    }

    /// <summary>
    /// Turns a settled line's force into the value the break search actually squares.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:308-320 Simple_spacer::force_penalty.
    /// Ragged is not priced by force at all but by the whitespace left after the line's
    /// natural (force 0) configuration; justified uses the convex compression penalty
    /// <c>f - 2f⁴</c>, which leaves a stretched line at <c>f</c>.
    /// </remarks>
    /// <param name="lineLength">The line's available width (LilyPond's <c>line_len</c>).</param>
    /// <param name="force">The solved force.</param>
    /// <param name="naturalLength">The line's length at force 0
    /// (<c>configuration_length (0.0)</c>).</param>
    private double ForcePenalty(double lineLength, double force, double naturalLength)
        // The formula's one home is SpringSolver.ForcePenaltyOf (2026-08-26; this body
        // was the second spelling, and the pair had already drifted — the solver-side
        // copy had its ragged arm reversed).
        => SpringSolver.ForcePenaltyOf(lineLength, force, naturalLength, _raggedRight);

    /// <summary>
    /// Swaps a line's first measure from its mid-line spring 0 to the line-start spring the
    /// layout gives it, floored at LilyPond's 0.3 + min_dist. No-op when the spring data
    /// carries none — the single-staff gate path leaves it null, keeping those callers on
    /// their previous behaviour.
    /// </summary>
    /// <remarks>
    /// The spring is PRE-BUILT per measure (MultiStaffLayouter.LineStartSpringForLine) and
    /// stored on <see cref="MeasureSpringData"/> by the multi-staff gate, so the substitution
    /// here is a straight swap for the same spring the system layout puts at spring 0 — which
    /// is exactly what section 5.4 requires the gate to price. The <c>Spring0*</c> fields are
    /// the mid-line spring 0 this replaces.
    /// </remarks>
    private static void ApplyLineStartSpring(
        in MeasureSpringData first,
        ref double idealSum, ref double minSum,
        ref double invStretchSum, ref double invCompressSum)
    {
        if (first.LineStartSpring is not { } lineStart)
            return;

        idealSum += lineStart.IdealDistance - first.Spring0Ideal;
        minSum += lineStart.MinDistance - first.Spring0Min;
        invStretchSum += lineStart.InverseStretchStrength - first.Spring0Stretch;
        invCompressSum += lineStart.InverseCompressStrength - first.Spring0Compress;
    }

    /// <summary>Break points of the best solution that uses exactly
    /// <paramref name="lines"/> lines, or null if the path is broken.</summary>
    private static List<int>? BacktrackByLineCount(int n, int cols, int[] prev, int lines)
    {
        var breaks = new List<int>();
        int cur = n;
        for (int k = lines; k > 0; k--)
        {
            breaks.Add(cur);
            cur = prev[cur * cols + k];
            if (cur < 0)
                return null;
        }
        breaks.Reverse();
        return breaks;
    }

    /// <summary>
    /// Whether any line of the exactly-<paramref name="lines"/> solution is COMPRESSED —
    /// LilyPond's <c>too_many_lines</c> test, which is what lets the walk keep adding lines
    /// past the first non-improvement while something still does not fit.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/constrained-breaking.cc:245-254 — the loop over the
    /// solution's lines looking for <c>force_ &lt; 0</c>.</remarks>
    private static bool HasCompressedLine(
        int n, int cols, int[] prev, double[] lineForce, int lines)
    {
        int cur = n;
        for (int k = lines; k > 0; k--)
        {
            if (lineForce[cur * cols + k] < 0)
                return true;
            cur = prev[cur * cols + k];
            if (cur < 0)
                return false;
        }
        return false;
    }

    /// <summary>
    /// Greedy fallback when DP fails to find a valid path.
    /// Fills each system until the next measure would exceed available width,
    /// honoring break permissions the same way the DP does: a Force break ends
    /// the line right there, and a width-driven break may not land on a Forbid
    /// boundary — it backs up to the nearest allowed one in the line, or
    /// extends past the forbidden run (overfull) when the whole line is one
    /// noBreak chain. The fallback must not violate what the primary algorithm
    /// treats as an absolute constraint.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc — break_permission_ is a hard
    /// constraint, not a preference weight.
    /// </remarks>
    internal List<int> GreedyBreak(MeasureSpringData[] springData, double[] cumMin)
    {
        int n = springData.Length;
        var breaks = new List<int>();
        int lineStart = 0;

        while (lineStart < n)
        {
            bool isFirstLine = lineStart == 0;
            double prefixWidth = isFirstLine ? _firstPrefixWidth : _continuationPrefixWidth;
            double availableWidth = _lineWidth - prefixWidth;

            // Fill while width allows, stopping hard at a forced break.
            int lineEnd = lineStart + 1; // At least one measure per line
            while (lineEnd < n
                   && springData[lineEnd - 1].BreakPermission != BreakPermission.Force
                   && cumMin[lineEnd + 1] - cumMin[lineStart] <= availableWidth)
            {
                lineEnd++;
            }

            // A width-driven stop may not break at a Forbid boundary.
            if (lineEnd < n && springData[lineEnd - 1].BreakPermission == BreakPermission.Forbid)
            {
                int back = lineEnd;
                while (back > lineStart + 1 && springData[back - 1].BreakPermission == BreakPermission.Forbid)
                    back--;
                if (springData[back - 1].BreakPermission != BreakPermission.Forbid)
                {
                    lineEnd = back;
                }
                else
                {
                    // The whole line is one noBreak chain: extend past it
                    // (overfull) rather than split it.
                    while (lineEnd < n && springData[lineEnd - 1].BreakPermission == BreakPermission.Forbid)
                        lineEnd++;
                }
            }

            breaks.Add(lineEnd);
            lineStart = lineEnd;
        }

        return breaks;
    }

    /// <summary>
    /// Converts break points to measure groups. Internal static so
    /// <see cref="SystemBreaker.BreakIntoSystemsGreedy"/>, which delegates its walk
    /// to <see cref="GreedyBreak"/>, regroups with the same one implementation.
    /// </summary>
    internal static List<List<Measure>> CreateMeasureGroups(IReadOnlyList<Measure> measures, List<int> breakPoints)
    {
        var result = new List<List<Measure>>();
        int start = 0;

        foreach (int end in breakPoints)
        {
            var group = new List<Measure>();
            for (int i = start; i < end; i++)
            {
                group.Add(measures[i]);
            }
            result.Add(group);
            start = end;
        }

        return result;
    }
}
