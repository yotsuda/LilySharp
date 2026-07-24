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
    double Spring0Compress = 0);

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
    private readonly double _tolerance;
    private readonly double _looseness;
    private readonly bool _raggedRight;
    private readonly Spring? _firstLineStartSpring;
    private readonly Spring? _continuationLineStartSpring;

    /// <summary>
    /// Penalty for infinite badness (line cannot be set).
    /// </summary>
    private const double Infinity = 1e18;

    /// <summary>
    /// Extra surcharge on a compressed line, ON TOP of LilyPond's own force penalty.
    /// ⚠️ NOT LilyPond's, and known not to be.
    /// </summary>
    /// <remarks>
    /// ⚠️ UNSOURCED. Its former <c>LILYPOND-REF: lily/simple-spacer.cc:281</c> pointed at
    /// <c>: sp-&gt;inverse_stretch_strength ();</c>, an unrelated line of compress_line's
    /// bookkeeping, and the value 50000 appears nowhere in LilyPond.
    /// <para>
    /// LilyPond's counterpart — <see cref="ForcePenalty"/> plus the impossible-line rule in
    /// <see cref="FindOptimalBreaks"/> — is now PORTED, and this constant survives only as
    /// an additive term beside it. That is the whole of what is left unsourced in a line's
    /// cost, and it is load-bearing: zeroing it moves six ledger points that are exact today
    /// (page.tight.page-count and system.{tuplet-bracket,slur,tie}-*) AWAY from LilyPond.
    /// </para>
    /// <para>
    /// ⚠️ Measured, so as not to be re-guessed: it is NOT standing in for the line-start
    /// spring the gate used to be missing. Wiring that in (<see cref="ApplyLineStartSpring"/>)
    /// and zeroing this constant regresses exactly the same six points by exactly the same
    /// amounts as zeroing it alone did. The missing quantity is a per-line MINIMUM that is
    /// larger still — the candidate being LilyPond's <c>0.3 + min_dist</c> floor on the
    /// line-start spring's fixed distance (staff-spacing.cc:210-215), whose min_dist
    /// <see cref="LineStartColumn.MinimumDistance"/> computes and nothing yet wires into
    /// either spring system. docs/HANDOFF.md §1 carries the account.
    /// </para>
    /// <para>
    /// What it costs meanwhile, measured on test/ties-slurs (2026-07-25): LilyPond sets
    /// those eight bars as ONE COMPRESSED system, and Lily# reaches the same natural
    /// geometry — every column agrees to 2e-5 — but cannot choose that line, because this
    /// term prices it at 50000 × |force|. So the constant does not merely stand in for
    /// something; it forbids a regime LilyPond uses.
    /// </para>
    /// </remarks>
    private const double OverfullPenalty = 50000;

    /// <summary>
    /// Creates a new Knuth-Plass line breaker.
    /// </summary>
    /// <param name="lineWidth">Available width for content.</param>
    /// <param name="firstPrefixWidth">Width of prefix on first line (clef, key, time).</param>
    /// <param name="continuationPrefixWidth">Width of prefix on continuation lines (clef, key).</param>
    /// <param name="tolerance">Acceptable ratio deviation from 1.0 (default 1.1).</param>
    /// <param name="looseness">Prefer more lines (positive) or fewer (negative).</param>
    /// <param name="raggedRight">If true, exclude Δforce² from demerits (ragged-right mode).</param>
    /// <param name="firstLineStartSpring">The prefix→first-note spring on the FIRST line,
    /// where the prefix carries the meter; null keeps the measure's own spring 0.</param>
    /// <param name="continuationLineStartSpring">The same on a continuation line, whose
    /// prefix is the clef (and key) only.</param>
    /// <remarks>
    /// The two line-start springs are score-level, not per measure — they come from the
    /// prefix's space-alist (<see cref="SpacingRules.FirstNoteSpring"/>) — so they are
    /// passed once, exactly as the prefix widths are. What IS per measure is the spring
    /// they REPLACE, which each <see cref="MeasureSpringData"/> carries as its
    /// <c>Spring0*</c> fields.
    /// <para>
    /// ⚠️ The gate must have both because it is the thing deciding where lines start. The
    /// layout substitutes spring 0 on a line's first measure (MultiStaffLayouter) and the
    /// gate used not to follow, so it priced every line start with a spring the layout
    /// would never use — 0.900000/0.200000 against 2.000000/1.000000 on a metered line.
    /// That is section 5.4's "the two spring systems and the break gate must agree",
    /// broken at the one place the gate exists to predict.
    /// </para>
    /// <para>LILYPOND-REF: lily/spacing-spanner.cc:219-224 — LilyPond generates the
    /// spacing for the PREBROKEN pieces of a column alongside the unbroken one, so a
    /// candidate line start is priced with the spring it would really get.</para>
    /// </remarks>
    public KnuthPlassBreaker(
        double lineWidth,
        double firstPrefixWidth,
        double continuationPrefixWidth,
        double tolerance = 1.1,
        double looseness = 0,
        bool raggedRight = false,
        Spring? firstLineStartSpring = null,
        Spring? continuationLineStartSpring = null)
    {
        _lineWidth = lineWidth;
        _firstPrefixWidth = firstPrefixWidth;
        _continuationPrefixWidth = continuationPrefixWidth;
        _tolerance = tolerance;
        _looseness = looseness;
        _raggedRight = raggedRight;
        _firstLineStartSpring = firstLineStartSpring;
        _continuationLineStartSpring = continuationLineStartSpring;
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
        for (int i = 0; i < n; i++)
        {
            cumIdeal[i + 1] = cumIdeal[i] + springData[i].IdealWidth;
            cumInvStretch[i + 1] = cumInvStretch[i] + springData[i].InverseStretchStrength;
            cumInvCompress[i + 1] = cumInvCompress[i] + springData[i].InverseCompressStrength;
            cumMin[i + 1] = cumMin[i] + springData[i].MinWidth;
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

        for (int j = 1; j <= n; j++)
        {
            for (int i = 0; i < j; i++)
            {
                // 1-4: Check break permission — skip if Forbid at break point i (except start)
                // LILYPOND-REF: lily/include/constrained-breaking.hh:73 break_permission_
                if (i > 0 && springData[i - 1].BreakPermission == BreakPermission.Forbid)
                    continue;

                // Skip if there's a forced break between i and j-1 (exclusive)
                // 1-4: Also skip if there's a Force permission in the middle
                bool hasForcedBreakInMiddle = false;
                for (int k = i; k < j - 1; k++)
                {
                    if (springData[k].BreakPermission == BreakPermission.Force)
                    {
                        hasForcedBreakInMiddle = true;
                        break;
                    }
                }
                if (hasForcedBreakInMiddle)
                    continue;

                bool isFirstLine = i == 0;
                double prefixWidth = isFirstLine ? _firstPrefixWidth : _continuationPrefixWidth;
                double availableWidth = _lineWidth - prefixWidth;

                // Compute line spring totals via cumulative sums
                double idealSum = cumIdeal[j] - cumIdeal[i];
                double invStretchSum = cumInvStretch[j] - cumInvStretch[i];
                double invCompressSum = cumInvCompress[j] - cumInvCompress[i];
                double minSum = cumMin[j] - cumMin[i];

                // The line's FIRST measure is priced with the prefix→first-note spring the
                // layout will actually give it, not with the bar-line spring it carries as
                // a mid-line measure. See the constructor's remarks.
                ApplyLineStartSpring(springData[i], isFirstLine,
                    ref idealSum, ref minSum, ref invStretchSum, ref invCompressSum);

                // ⚠️ This rule is NOT LilyPond's: LilyPond PRICES an underfull line (a big
                // positive force, or the leftover whitespace when ragged) rather than
                // forbidding it. It is still here because removing it makes Lily# worse —
                // see the note on OverfullPenalty, which is the same open question.
                bool isLastLine = (j == n);
                bool hasForceAtJ = j <= n && j > 0 && springData[j - 1].BreakPermission == BreakPermission.Force;
                bool hasForceAtI = i > 0 && springData[i - 1].BreakPermission == BreakPermission.Force;
                if (!isLastLine && idealSum < availableWidth / (_tolerance * 2) && idealSum > 0
                    && !hasForceAtJ && !hasForceAtI)
                    continue;

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
                double penalty = penalizedForce * penalizedForce
                    + (force < 0 ? OverfullPenalty * Math.Abs(force) : 0);

                // 1-2: LILYPOND-REF: lily/constrained-breaking.cc:112-113
                // Add break_penalty_ from the measure at the break point
                if (j < n)
                {
                    penalty += springData[j - 1].BreakPenalty;
                }

                if (penalty >= Infinity)
                    continue;

                for (int k = 1; k <= n; k++)
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
    {
        if (_raggedRight)
            return Math.Max(0.0, lineLength - naturalLength);

        double f = force;
        return f - (f < 0 ? f * f * f * f * 2 : 0);
    }

    /// <summary>
    /// Swaps a line's first measure from its mid-line spring 0 to the prefix→first-note
    /// spring, mirroring the substitution the layout makes.
    /// </summary>
    /// <remarks>
    /// The layout's rule (MultiStaffLayouter): the replacement keeps whichever minimum is
    /// larger — the space-alist's or the measure's own spring 0 — and is rigid
    /// (<c>inverseStretchStrength: 0</c>), with its ideal floored at that minimum.
    /// No-op when the caller supplied no line-start spring, which keeps every existing
    /// caller on its previous behaviour.
    /// </remarks>
    private void ApplyLineStartSpring(
        in MeasureSpringData first, bool isFirstLine,
        ref double idealSum, ref double minSum,
        ref double invStretchSum, ref double invCompressSum)
    {
        var spring = isFirstLine ? _firstLineStartSpring : _continuationLineStartSpring;
        if (spring is not { } lineStart)
            return;

        double newMin = Math.Max(lineStart.MinDistance, first.Spring0Min);
        var replaced = new Spring(
            Math.Max(lineStart.IdealDistance, newMin), newMin, inverseStretchStrength: 0);

        idealSum += replaced.IdealDistance - first.Spring0Ideal;
        minSum += replaced.MinDistance - first.Spring0Min;
        invStretchSum += replaced.InverseStretchStrength - first.Spring0Stretch;
        invCompressSum += replaced.InverseCompressStrength - first.Spring0Compress;
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
    /// Converts break points to measure groups.
    /// </summary>
    private List<List<Measure>> CreateMeasureGroups(IReadOnlyList<Measure> measures, List<int> breakPoints)
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