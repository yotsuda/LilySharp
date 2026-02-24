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
    double InverseStretchStrength);

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
    public KnuthPlassBreaker(
        double lineWidth,
        double firstPrefixWidth,
        double continuationPrefixWidth,
        double tolerance = 1.1,
        double looseness = 0)
    {
        _lineWidth = lineWidth;
        _firstPrefixWidth = firstPrefixWidth;
        _continuationPrefixWidth = continuationPrefixWidth;
        _tolerance = tolerance;
        _looseness = looseness;
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

        // Collect forced break points (measures with HasBreakAfter = true)
        var forcedBreaks = new HashSet<int>();
        for (int i = 0; i < measures.Count; i++)
        {
            if (measures[i].HasBreakAfter)
                forcedBreaks.Add(i + 1); // Break AFTER measure i means break point at i+1
        }

        // Calculate spring data for each measure
        var springData = ComputeMeasureSpringData(measures, baseShortestDuration);

        // Find optimal number of lines and break points
        var breakPoints = FindOptimalBreaks(springData, forcedBreaks);

        // Convert break points to measure groups
        return CreateMeasureGroups(measures, breakPoints);
    }

    /// <summary>
    /// Computes spring data for each measure from its internal springs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc — spring parameters for force calculation
    /// Each measure's springs are summed to produce aggregate spring data.
    /// </remarks>
    private static MeasureSpringData[] ComputeMeasureSpringData(IReadOnlyList<Measure> measures,
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

            data[i] = new MeasureSpringData(idealWidth, minWidth, inverseStretch);
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
    /// Demerits = force² + (force - prevForce)²
    /// Forced breaks are handled by only allowing transitions through forced break points.
    /// </remarks>
    private List<int> FindOptimalBreaks(MeasureSpringData[] springData, HashSet<int> forcedBreaks)
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
        var d = new double[n + 1];
        var prev = new int[n + 1];
        var lineForce = new double[n + 1];

        d[0] = 0;
        lineForce[0] = 0; // No previous line
        for (int j = 1; j <= n; j++)
        {
            d[j] = Infinity;
            prev[j] = -1;

            for (int i = 0; i < j; i++)
            {
                // Skip if there's a forced break between i and j-1 (exclusive)
                bool hasForcedBreakInMiddle = false;
                for (int k = i + 1; k < j; k++)
                {
                    if (forcedBreaks.Contains(k))
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

                // Check overfull: if minimum width exceeds available
                if (minSum > availableWidth * _tolerance)
                    continue;

                // Check severely underfull (but allow lines adjacent to forced breaks)
                // LILYPOND-REF: lily/constrained-breaking.cc — \break is an absolute constraint
                // A line ending at or starting from a forced break must be allowed
                if (idealSum < availableWidth / (_tolerance * 2) && idealSum > 0
                    && !forcedBreaks.Contains(j) && !forcedBreaks.Contains(i))
                    continue;

                // LILYPOND-REF: lily/simple-spacer.cc:267-300
                double force = CalculateLineForce(availableWidth, idealSum, invStretchSum);

                // Overfull line that can't be compressed
                if (double.IsNegativeInfinity(force))
                    continue;

                // LILYPOND-REF: lily/constrained-breaking.cc:224-232
                // demerits = force² + Δforce²
                double penalty;
                if (force < 0)
                {
                    // Compressed line: use force² + overfull penalty
                    penalty = force * force + OverfullPenalty * Math.Abs(force);
                }
                else
                {
                    penalty = force * force;
                }

                // Δforce² : penalize force difference between consecutive lines
                double prevF = lineForce[i];
                double deltaForce = force - prevF;
                penalty += deltaForce * deltaForce;

                if (penalty < Infinity)
                {
                    double totalPenalty = d[i] + penalty;
                    if (totalPenalty < d[j])
                    {
                        d[j] = totalPenalty;
                        prev[j] = i;
                        lineForce[j] = force;
                    }
                }
            }
        }

        // Backtrack to find break points
        var breaks = new List<int>();
        int current = n;
        while (current > 0)
        {
            breaks.Add(current);
            current = prev[current];
        }

        breaks.Reverse();
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