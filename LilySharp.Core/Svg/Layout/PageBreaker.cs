using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Vertical spacing details for a single system (line of music).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/constrained-breaking.hh:45-119 Line_details struct
/// </remarks>
public sealed record SystemDetails
{
    /// <summary>
    /// Full height of the system including top and bottom extents.
    /// </summary>
    public required double Height { get; init; }

    /// <summary>
    /// Height above the staff top (negative skyline extent).
    /// </summary>
    public required double TopExtent { get; init; }

    /// <summary>
    /// Height below the staff bottom (positive skyline extent).
    /// </summary>
    public required double BottomExtent { get; init; }

    /// <summary>
    /// Staff height (fixed, typically 4 staff spaces).
    /// </summary>
    public required double StaffHeight { get; init; }

    /// <summary>
    /// Compulsory space after this system (padding).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:63 padding_</remarks>
    public double Padding { get; init; }

    /// <summary>
    /// Spring length (natural distance from bottom of this system to top of next).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:69 space_</remarks>
    public double SpringLength { get; init; }

    /// <summary>
    /// Inverse of spring stiffness (higher = more flexible).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:71 inverse_hooke_</remarks>
    public double InverseHooke { get; init; } = 1.0;

    /// <summary>
    /// Penalty for breaking page after this system.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:77 page_penalty_</remarks>
    public double PagePenalty { get; init; }

    /// <summary>
    /// Whether a page break is forced after this system.
    /// </summary>
    public bool ForceBreakAfter { get; init; }

    /// <summary>
    /// Gets the natural distance to the next system.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/constrained-breaking.cc:657-667 spring_length()</remarks>
    public double GetSpringLength(SystemDetails next)
    {
        // Natural distance is padding plus spring length
        return Padding + SpringLength;
    }
}

/// <summary>
/// Calculates vertical force for a page of systems.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc:31-132 Page_spacing class
/// Similar to horizontal Spring-Rod problem, but simpler because
/// each system only interacts with adjacent systems.
/// </remarks>
public sealed class PageSpacing
{
    private readonly double _pageHeight;
    private readonly double _topMargin;
    private readonly double _bottomMargin;

    private double _rodHeight;
    private double _springLength;
    private double _inverseSpringK;
    private SystemDetails? _firstSystem;
    private SystemDetails? _lastSystem;

    /// <summary>
    /// Current force (positive = stretch, negative = compress).
    /// </summary>
    public double Force { get; private set; }

    /// <summary>
    /// Total rod height (minimum height).
    /// </summary>
    public double RodHeight => _rodHeight;

    /// <summary>
    /// Total spring length.
    /// </summary>
    public double SpringLength => _springLength;

    public PageSpacing(double pageHeight, double topMargin, double bottomMargin)
    {
        _pageHeight = pageHeight;
        _topMargin = topMargin;
        _bottomMargin = bottomMargin;
        Clear();
    }

    /// <summary>
    /// Resets the spacing calculation.
    /// </summary>
    public void Clear()
    {
        _rodHeight = 0;
        _springLength = 0;
        _inverseSpringK = 0;
        _firstSystem = null;
        _lastSystem = null;
        Force = 0;
    }

    /// <summary>
    /// Appends a system to this page.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-spacing.cc:53-72 append_system()</remarks>
    public void AppendSystem(SystemDetails system)
    {
        if (_firstSystem == null)
        {
            // First system on page
            _rodHeight = system.Height;
            _firstSystem = system;
        }
        else
        {
            // Add spring between previous and current system
            _rodHeight += system.StaffHeight + system.BottomExtent;
            _springLength += _lastSystem!.GetSpringLength(system);
        }

        _inverseSpringK += system.InverseHooke;
        _lastSystem = system;

        CalcForce();
    }

    /// <summary>
    /// Calculates the force needed to fit systems on page.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-spacing.cc:32-43 calc_force()</remarks>
    private void CalcForce()
    {
        double availableHeight = _pageHeight - _topMargin - _bottomMargin;
        double lastPadding = _lastSystem?.Padding ?? 0;

        if (_rodHeight + lastPadding >= availableHeight)
        {
            // Overfull page
            Force = double.NegativeInfinity;
        }
        else
        {
            // Force = (available - rod - spring) / flexibility
            Force = (availableHeight - _rodHeight - lastPadding - _springLength)
                    / Math.Max(0.1, _inverseSpringK);
        }
    }
}

/// <summary>
/// Result of page breaking optimization.
/// </summary>
public sealed record PageBreakResult
{
    /// <summary>
    /// Total penalty (demerits) of this solution.
    /// </summary>
    public double Penalty { get; init; }

    /// <summary>
    /// Force values for each page.
    /// </summary>
    public ImmutableArray<double> Forces { get; init; }

    /// <summary>
    /// Number of systems on each page.
    /// </summary>
    public ImmutableArray<int> SystemsPerPage { get; init; }
}

/// <summary>
/// Optimizes page breaking using dynamic programming.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc:134-402 Page_spacer class
/// Uses same DP approach as KnuthPlassBreaker but for vertical (page) dimension.
///
/// Algorithm:
/// Let D(n) = minimum penalty to put systems 0..n on some number of pages
/// Let D(n,k) = minimum penalty to put systems 0..n on exactly k pages
/// Then: D(n,k) = min over j { D(j,k-1) + penalty(j+1..n on one page) }
/// </remarks>
public sealed class PageBreaker
{
    private readonly double _pageHeight;
    private readonly double _topMargin;
    private readonly double _bottomMargin;
    private readonly double _headerHeight;

    /// <summary>
    /// Penalty for bad spacing (overflow or extreme stretch).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/page-spacing.hh:45</remarks>
    private const double BadSpacingPenalty = 1e6;

    /// <summary>
    /// Penalty for terrible spacing (ignoring user constraints).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/page-spacing.hh:46</remarks>
    private const double TerribleSpacingPenalty = 1e8;

    public PageBreaker(double pageHeight, double topMargin, double bottomMargin, double headerHeight)
    {
        _pageHeight = pageHeight;
        _topMargin = topMargin;
        _bottomMargin = bottomMargin;
        _headerHeight = headerHeight;
    }

    /// <summary>
    /// Breaks systems into pages optimally.
    /// </summary>
    /// <param name="systems">Details for each system.</param>
    /// <returns>Indices where page breaks occur.</returns>
    public List<int> BreakIntoPages(IReadOnlyList<SystemDetails> systems)
    {
        if (systems.Count == 0)
            return new List<int>();

        // Single system always fits on one page
        if (systems.Count == 1)
            return new List<int> { 1 };

        // Use dynamic programming to find optimal breaks
        return FindOptimalBreaks(systems);
    }

    /// <summary>
    /// Finds optimal page breaks using dynamic programming.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:146-179 solve()
    /// Uses simple DP where we don't constrain the number of pages.
    /// </remarks>
    private List<int> FindOptimalBreaks(IReadOnlyList<SystemDetails> systems)
    {
        int n = systems.Count;

        // dp[j] = minimum penalty to lay out systems 0..j-1
        // prev[j] = previous break point for optimal solution ending at j
        var dp = new double[n + 1];
        var prev = new int[n + 1];

        dp[0] = 0;
        for (int j = 1; j <= n; j++)
        {
            dp[j] = double.MaxValue;
            prev[j] = -1;

            for (int i = 0; i < j; i++)
            {
                // Check for forced breaks
                bool hasForcedBreakInMiddle = false;
                for (int k = i; k < j - 1; k++)
                {
                    if (systems[k].ForceBreakAfter)
                    {
                        hasForcedBreakInMiddle = true;
                        break;
                    }
                }
                if (hasForcedBreakInMiddle)
                    continue;

                // Calculate penalty for putting systems i..j-1 on one page
                double penalty = CalculatePagePenalty(systems, i, j, i == 0);

                if (penalty < double.MaxValue)
                {
                    double totalPenalty = dp[i] + penalty;
                    if (totalPenalty < dp[j])
                    {
                        dp[j] = totalPenalty;
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
    /// Calculates penalty for putting systems startIdx..endIdx-1 on one page.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:296-402 calc_subproblem()
    /// </remarks>
    private double CalculatePagePenalty(
        IReadOnlyList<SystemDetails> systems,
        int startIdx,
        int endIdx,
        bool isFirstPage)
    {
        // Calculate available height
        double topMargin = isFirstPage ? _topMargin + _headerHeight : _topMargin;
        var spacing = new PageSpacing(_pageHeight, topMargin, _bottomMargin);

        // Add systems to page
        for (int i = startIdx; i < endIdx; i++)
        {
            spacing.AppendSystem(systems[i]);
        }

        double force = spacing.Force;

        // Check for overfull page
        if (double.IsNegativeInfinity(force))
        {
            return double.MaxValue;
        }

        // LILYPOND-REF: lily/page-spacing.cc:358
        // demerits = force^2
        double demerits = force * force;

        // Clamp at BAD_SPACING_PENALTY
        demerits = Math.Min(demerits, BadSpacingPenalty);

        // Add page penalty for breaking after this page
        if (endIdx < systems.Count && endIdx > startIdx)
        {
            demerits += systems[endIdx - 1].PagePenalty;
        }

        return demerits;
    }

    /// <summary>
    /// Creates system details from layout information.
    /// </summary>
    public static SystemDetails CreateFromLayout(
        double staffHeight,
        double topExtent,
        double bottomExtent,
        double padding,
        double springLength,
        bool forceBreakAfter = false)
    {
        return new SystemDetails
        {
            Height = topExtent + staffHeight + bottomExtent,
            TopExtent = topExtent,
            BottomExtent = bottomExtent,
            StaffHeight = staffHeight,
            Padding = padding,
            SpringLength = springLength,
            ForceBreakAfter = forceBreakAfter
        };
    }
}