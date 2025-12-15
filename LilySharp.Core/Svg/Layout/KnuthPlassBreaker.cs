using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

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
/// The penalty function considers:
/// - Line stretch/compression (badness)
/// - Break penalties at specific measure boundaries
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
    public List<List<Measure>> BreakIntoLines(IReadOnlyList<Measure> measures)
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

        // Calculate ideal widths for each measure
        var widths = measures.Select(m => SpacingRules.CalculateMeasureIdealWidth(m)).ToArray();

        // Find optimal number of lines and break points
        var breakPoints = FindOptimalBreaks(widths, forcedBreaks);

        // Convert break points to measure groups
        return CreateMeasureGroups(measures, breakPoints);
    }

    /// <summary>
    /// Finds optimal break points using dynamic programming.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:83-126
    /// Forced breaks are handled by only allowing transitions through forced break points.
    /// </remarks>
    private List<int> FindOptimalBreaks(double[] widths, HashSet<int> forcedBreaks)
    {
        int n = widths.Length;

        // Precompute line widths for all (i,j) pairs
        var lineWidths = PrecomputeLineWidths(widths);

        // D[j] = minimum penalty to break measures 0..j-1
        // prev[j] = previous break point for optimal solution ending at j
        var d = new double[n + 1];
        var prev = new int[n + 1];

        d[0] = 0;
        for (int j = 1; j <= n; j++)
        {
            d[j] = Infinity;
            prev[j] = -1;

            for (int i = 0; i < j; i++)
            {
                // Skip if there's a forced break between i and j-1 (exclusive)
                // We can only transition from i if there are no forced breaks in (i, j)
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
                double penalty = CalculateLinePenalty(lineWidths[i, j], isFirstLine);

                if (penalty < Infinity)
                {
                    double totalPenalty = d[i] + penalty;
                    if (totalPenalty < d[j])
                    {
                        d[j] = totalPenalty;
                        prev[j] = i;
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
    /// Precomputes cumulative line widths for all (start, end) pairs.
    /// </summary>
    private double[,] PrecomputeLineWidths(double[] widths)
    {
        int n = widths.Length;
        var result = new double[n + 1, n + 1];

        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = i; j < n; j++)
            {
                sum += widths[j];
                result[i, j + 1] = sum;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculates the penalty (badness) for a line with given content width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/simple-spacer.cc:267-300
    /// LILYPOND-REF: lily/constrained-breaking.cc:114-115
    ///
    /// The penalty is based on how much the line needs to stretch or compress.
    /// Badness = 100 * |ratio - 1|^3 where ratio = actual/ideal
    /// </remarks>
    private double CalculateLinePenalty(double contentWidth, bool isFirstLine)
    {
        double prefixWidth = isFirstLine ? _firstPrefixWidth : _continuationPrefixWidth;
        double availableWidth = _lineWidth - prefixWidth;

        if (contentWidth <= 0)
            return Infinity;

        double ratio = availableWidth / contentWidth;

        // Overfull line (content wider than available)
        if (ratio < 1.0 / _tolerance)
            return Infinity;

        // Severely stretched line
        if (ratio > _tolerance * 2)
            return Infinity;

        // Calculate badness using cubic formula
        // LILYPOND-REF: lily/simple-spacer.cc:291
        double deviation = Math.Abs(ratio - 1.0);
        double badness = 100 * Math.Pow(deviation, 3);

        // Add overfull penalty for lines that need compression
        if (ratio < 1.0)
            badness += OverfullPenalty * (1.0 - ratio);

        return badness;
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