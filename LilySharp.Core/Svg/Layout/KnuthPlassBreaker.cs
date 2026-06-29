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
    BreakPermission BreakPermission = BreakPermission.Allow);

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
public sealed class KnuthPlassBreaker
{
    private readonly double _lineWidth;
    private readonly double _firstPrefixWidth;
    private readonly double _continuationPrefixWidth;
    private readonly double _tolerance;
    private readonly double _looseness;
    private readonly bool _raggedRight;

    /// <summary>
    /// Penalty for infinite badness (line cannot be set).
    /// </summary>
    private const double Infinity = 1e18;

    /// <summary>
    /// Penalty multiplier for overfull lines.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/simple-spacer.cc:281</remarks>
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
    public KnuthPlassBreaker(
        double lineWidth,
        double firstPrefixWidth,
        double continuationPrefixWidth,
        double tolerance = 1.1,
        double looseness = 0,
        bool raggedRight = false)
    {
        _lineWidth = lineWidth;
        _firstPrefixWidth = firstPrefixWidth;
        _continuationPrefixWidth = continuationPrefixWidth;
        _tolerance = tolerance;
        _looseness = looseness;
        _raggedRight = raggedRight;
    }

    /// <summary>
    /// Finds optimal line breaks for measures.
    /// </summary>
    /// <param name="measures">Measures to break into lines.</param>
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
    /// (LSP_F3_QUERY_GRAPH_DESIGN.md §4 — measure_natural_width is the cutoff).
    /// </remarks>
    internal static MeasureSpringData[] ComputeMeasureSpringData(IReadOnlyList<Measure> measures,
                                                                double? baseShortestDuration = null)
    {
        var data = new MeasureSpringData[measures.Count];
        for (int i = 0; i < measures.Count; i++)
        {
            var m = measures[i];
            double idealWidth = SpacingRules.CalculateMeasureIdealWidth(m, baseShortestDuration);

            // Sum inverse stretch strengths from the measure's springs
            double inverseStretch = 0;
            double minWidth = 0;
            var springs = SpacingRules.CreateSpringsForMeasure(m, baseShortestDuration);
            foreach (var spring in springs)
            {
                inverseStretch += spring.InverseStretchStrength;
                minWidth += spring.MinDistance;
            }

            // Add barline widths to min
            minWidth += SpacingRules.GetBarlineWidth(m.StartBarline);
            minWidth += SpacingRules.GetBarlineWidth(m.EndBarline);

            // LILYPOND-REF: lily/constrained-breaking.cc:112-113 — break_penalty_ propagation
            data[i] = new MeasureSpringData(idealWidth, minWidth, inverseStretch,
                m.BreakPenalty, m.LineBreakPermission);
        }
        return data;
    }

    /// <summary>
    /// Calculates the force for a line containing the given spring data.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:267-300 solve()
    /// force = (available_width - ideal_sum) / inverse_stretch_sum
    /// Positive force = stretch, negative = compress.
    /// </remarks>
    internal static double CalculateLineForce(
        double availableWidth, double idealSum, double inverseStretchSum)
    {
        if (inverseStretchSum < 1e-6)
            return availableWidth >= idealSum ? 0 : double.NegativeInfinity;

        return (availableWidth - idealSum) / inverseStretchSum;
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
        var cumMin = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            cumIdeal[i + 1] = cumIdeal[i] + springData[i].IdealWidth;
            cumInvStretch[i + 1] = cumInvStretch[i] + springData[i].InverseStretchStrength;
            cumMin[i + 1] = cumMin[i] + springData[i].MinWidth;
        }

        // D[j] = minimum penalty to break measures 0..j-1
        // prev[j] = previous break point for optimal solution ending at j
        // lineForce[j] = force of the last line in optimal solution ending at j
        // lineCount[j] = number of lines in optimal solution ending at j
        var d = new double[n + 1];
        var prev = new int[n + 1];
        var lineForce = new double[n + 1];
        var lineCount = new int[n + 1];

        d[0] = 0;
        lineForce[0] = 0; // No previous line
        lineCount[0] = 0;
        for (int j = 1; j <= n; j++)
        {
            d[j] = Infinity;
            prev[j] = -1;

            for (int i = 0; i < j; i++)
            {
                // 1-4: Check break permission — skip if Forbid at break point i (except start)
                // LILYPOND-REF: lily/include/constrained-breaking.hh:74 break_permission_
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
                double minSum = cumMin[j] - cumMin[i];

                // LILYPOND-REF: lily/constrained-breaking.cc — no hard reject;
                // all transitions are evaluated via penalty. This ensures the DP
                // always finds a valid solution (possibly overfull) rather than
                // falling back to putting everything on one system.

                // Check severely underfull (but allow last line and lines adjacent to forced/forbid breaks)
                // LILYPOND-REF: lily/constrained-breaking.cc — \break is an absolute constraint
                // LILYPOND-REF: lily/page-spacing.cc — last system is allowed to be underfull (ragged-last)
                bool isLastLine = (j == n);
                bool hasForceAtJ = j <= n && j > 0 && springData[j - 1].BreakPermission == BreakPermission.Force;
                bool hasForceAtI = i > 0 && springData[i - 1].BreakPermission == BreakPermission.Force;
                if (!isLastLine && idealSum < availableWidth / (_tolerance * 2) && idealSum > 0
                    && !hasForceAtJ && !hasForceAtI)
                    continue;

                // LILYPOND-REF: lily/simple-spacer.cc:267-300
                // Use max(idealSum, minSum) as effective width for force calculation.
                double effectiveWidth = Math.Max(idealSum, minSum);
                double force = CalculateLineForce(availableWidth, effectiveWidth, invStretchSum);

                // Handle degenerate case: springs have zero flexibility
                if (double.IsNegativeInfinity(force))
                    force = -(effectiveWidth - availableWidth) * 1000;

                // LILYPOND-REF: lily/constrained-breaking.cc:224-232
                // demerits = force² + Δforce² + break_penalty_
                double penalty;
                if (force < 0)
                {
                    // Compressed/overfull line: use force² + overfull penalty
                    penalty = force * force + OverfullPenalty * Math.Abs(force);
                }
                else
                {
                    penalty = force * force;
                }

                // 1-1: LILYPOND-REF: lily/constrained-breaking.cc:568-573
                // ragged_right: only force², no Δforce² (consecutive line uniformity
                // doesn't matter when lines aren't justified)
                if (!_raggedRight)
                {
                    double prevF = lineForce[i];
                    double deltaForce = force - prevF;
                    penalty += deltaForce * deltaForce;
                }

                // 1-2: LILYPOND-REF: lily/constrained-breaking.cc:112-113
                // Add break_penalty_ from the measure at the break point
                if (j < n)
                {
                    penalty += springData[j - 1].BreakPenalty;
                }

                if (penalty < Infinity)
                {
                    double totalPenalty = d[i] + penalty;
                    if (totalPenalty < d[j])
                    {
                        d[j] = totalPenalty;
                        prev[j] = i;
                        lineForce[j] = force;
                        lineCount[j] = lineCount[i] + 1;
                    }
                }
            }
        }

        // Backtrack to find break points
        var breaks = BacktrackBreaks(n, prev, d);
        if (breaks == null)
        {
            // DP failed — fall back to greedy
            return GreedyBreak(springData, cumMin);
        }

        // 1-3: LILYPOND-REF: lily/constrained-breaking.cc looseness parameter
        // If looseness != 0, find the solution with line count closest to
        // optimal + looseness that has the minimum demerits among candidates.
        if (_looseness != 0)
        {
            int optimalLines = lineCount[n];
            int targetLines = optimalLines + (int)_looseness;
            if (targetLines < 1) targetLines = 1;

            // Re-run DP tracking all solutions by line count
            var bestByLineCount = FindBreaksByLineCount(springData, cumIdeal, cumInvStretch, cumMin, targetLines);
            if (bestByLineCount != null)
                return bestByLineCount;
        }

        return breaks;
    }

    /// <summary>
    /// Backtracks through the DP prev[] array to extract break points.
    /// Returns null if the path is invalid.
    /// </summary>
    private static List<int>? BacktrackBreaks(int n, int[] prev, double[] d)
    {
        if (d[n] >= Infinity)
            return null;

        var breaks = new List<int>();
        int current = n;
        while (current > 0)
        {
            breaks.Add(current);
            current = prev[current];
            if (current < 0)
                return null;
        }

        breaks.Reverse();
        return breaks;
    }

    /// <summary>
    /// Finds optimal breaks for a specific target line count (for looseness).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc — looseness biases line count
    /// Uses 2D DP: dp[j, k] = min demerits for measures 0..j-1 in exactly k lines.
    /// </remarks>
    private List<int>? FindBreaksByLineCount(
        MeasureSpringData[] springData,
        double[] cumIdeal, double[] cumInvStretch, double[] cumMin,
        int targetLines)
    {
        int n = springData.Length;
        if (targetLines > n || targetLines < 1)
            return null;

        // dp[j, k] = min demerits for measures 0..j-1 in exactly k lines
        // Store as flat array: dp[j * (targetLines+1) + k]
        int cols = targetLines + 1;
        var dp = new double[(n + 1) * cols];
        var prev = new int[(n + 1) * cols];
        Array.Fill(dp, Infinity);
        Array.Fill(prev, -1);
        dp[0] = 0; // 0 measures in 0 lines

        for (int j = 1; j <= n; j++)
        {
            for (int i = 0; i < j; i++)
            {
                // Check break permission
                if (i > 0 && springData[i - 1].BreakPermission == BreakPermission.Forbid)
                    continue;

                bool hasForcedInMiddle = false;
                for (int m = i; m < j - 1; m++)
                {
                    if (springData[m].BreakPermission == BreakPermission.Force)
                    { hasForcedInMiddle = true; break; }
                }
                if (hasForcedInMiddle) continue;

                bool isFirstLine = i == 0;
                double prefixWidth = isFirstLine ? _firstPrefixWidth : _continuationPrefixWidth;
                double availableWidth = _lineWidth - prefixWidth;

                double idealSum = cumIdeal[j] - cumIdeal[i];
                double invStretchSum = cumInvStretch[j] - cumInvStretch[i];
                double minSum = cumMin[j] - cumMin[i];

                double effectiveWidth = Math.Max(idealSum, minSum);
                double force = CalculateLineForce(availableWidth, effectiveWidth, invStretchSum);
                if (double.IsNegativeInfinity(force))
                    force = -(effectiveWidth - availableWidth) * 1000;

                double penalty;
                if (force < 0)
                    penalty = force * force + OverfullPenalty * Math.Abs(force);
                else
                    penalty = force * force;

                if (j < n)
                    penalty += springData[j - 1].BreakPenalty;

                if (penalty >= Infinity) continue;

                for (int k = 1; k <= targetLines; k++)
                {
                    int prevIdx = i * cols + (k - 1);
                    if (dp[prevIdx] >= Infinity) continue;

                    double total = dp[prevIdx] + penalty;
                    int curIdx = j * cols + k;
                    if (total < dp[curIdx])
                    {
                        dp[curIdx] = total;
                        prev[curIdx] = i;
                    }
                }
            }
        }

        // Check if target line count is achievable
        int finalIdx = n * cols + targetLines;
        if (dp[finalIdx] >= Infinity)
            return null;

        // Backtrack
        var breaks = new List<int>();
        int cur = n;
        int curK = targetLines;
        while (cur > 0 && curK > 0)
        {
            breaks.Add(cur);
            cur = prev[cur * cols + curK];
            curK--;
            if (cur < 0) return null;
        }

        breaks.Reverse();
        return breaks;
    }

    /// <summary>
    /// Greedy fallback when DP fails to find a valid path.
    /// Fills each system until the next measure would exceed available width.
    /// </summary>
    private List<int> GreedyBreak(MeasureSpringData[] springData, double[] cumMin)
    {
        int n = springData.Length;
        var breaks = new List<int>();
        int lineStart = 0;

        while (lineStart < n)
        {
            bool isFirstLine = lineStart == 0;
            double prefixWidth = isFirstLine ? _firstPrefixWidth : _continuationPrefixWidth;
            double availableWidth = _lineWidth - prefixWidth;

            // Find how many measures fit on this line
            int lineEnd = lineStart + 1; // At least one measure per line
            while (lineEnd < n)
            {
                double minSum = cumMin[lineEnd + 1] - cumMin[lineStart];
                if (minSum > availableWidth)
                    break;
                lineEnd++;
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