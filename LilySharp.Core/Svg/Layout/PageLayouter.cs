using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Creates pages from systems using optimal page breaking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc Page_spacer class
/// LILYPOND-REF: lily/page-layout-problem.cc vertical justification
/// </remarks>
public sealed class PageLayouter
{
    private readonly LayoutOptions _options;

    public PageLayouter(LayoutOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Creates pages using optimal page breaking algorithm.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc Page_spacer class
    /// Uses dynamic programming to find optimal page breaks,
    /// then applies force-based vertical spacing within each page.
    /// </remarks>
    public ImmutableArray<PageLayout> CreatePagesWithOptimalBreaking(
        ImmutableArray<SystemLayout> systems,
        double headerHeight,
        ImmutableArray<(double upExtent, double downExtent)> systemExtents)
    {
        if (systems.Length == 0)
        {
            return ImmutableArray<PageLayout>.Empty;
        }

        var vs = _options.VerticalSpacing;

        // Create SystemDetails for each system using per-system skyline extents
        // and context-dependent spacing specs
        var systemDetails = new List<SystemDetails>();
        for (int i = 0; i < systems.Length; i++)
        {
            double staffHeight = _options.StaffHeight;
            double topExtent = systemExtents[i].upExtent;
            double bottomExtent = systemExtents[i].downExtent;

            // Select spacing spec based on context
            // (for page breaker, we use system-system as default;
            //  actual per-pair spacing is applied during positioning)
            var spec = vs.SystemSystem;

            // Skyline-based minimum distance
            double skylineDistance = staffHeight + bottomExtent;
            double minDistance = Math.Max(skylineDistance, spec.MinimumDistance);

            systemDetails.Add(new SystemDetails
            {
                Height = topExtent + staffHeight + bottomExtent,
                TopExtent = topExtent,
                BottomExtent = bottomExtent,
                StaffHeight = staffHeight,
                Padding = spec.Padding,
                SpringLength = Math.Max(0, spec.BasicDistance - minDistance),
                InverseHooke = Math.Max(0.1, spec.Stretchability > 0 ? spec.Stretchability / 60.0 : 0.1),
            });
        }

        // Run page breaker
        var breaker = new PageBreaker(
            pageHeight: _options.PageHeight,
            topMargin: _options.MarginTop,
            bottomMargin: _options.MarginBottom,
            headerHeight: headerHeight,
            parameters: _options.PageBreaking);

        var breakPoints = breaker.BreakIntoPages(systemDetails);

        // Create pages from break points with context-aware Y positioning
        var pages = new List<PageLayout>();
        int systemStart = 0;

        for (int pageIdx = 0; pageIdx < breakPoints.Count; pageIdx++)
        {
            int systemEnd = breakPoints[pageIdx];
            bool isFirstPage = pageIdx == 0;
            bool isLastPage = pageIdx == breakPoints.Count - 1;

            // Reconstruct PageSpacing for this page to get the force
            double topMargin = isFirstPage
                ? _options.MarginTop + headerHeight
                : _options.MarginTop;
            var pageSpacing = new PageSpacing(_options.PageHeight, topMargin, _options.MarginBottom);
            for (int sysIdx = systemStart; sysIdx < systemEnd; sysIdx++)
                pageSpacing.AppendSystem(systemDetails[sysIdx]);

            // Determine if this page uses ragged spacing
            bool isRagged = _options.PageBreaking.RaggedBottom
                || (isLastPage && _options.PageBreaking.RaggedLastBottom);

            // Clamp force
            double force = pageSpacing.Force;
            if (double.IsNegativeInfinity(force) || double.IsNaN(force))
                force = 0;
            else if (isRagged)
                force = Math.Max(0, Math.Min(force, 0)); // No stretching for ragged
            else
                force = Math.Max(0, force); // No compression

            // Position systems using context-aware spacing specs
            var pageSystems = PositionSystemsOnPage(
                systems, systemExtents, systemDetails, systemStart, systemEnd,
                isFirstPage, headerHeight, force, vs);

            pages.Add(new PageLayout(
                PageIndex: pageIdx,
                Width: _options.PageWidth,
                Height: _options.PageHeight,
                HeaderHeight: isFirstPage ? headerHeight : 0,
                Systems: pageSystems));

            systemStart = systemEnd;
        }

        return pages.ToImmutableArray();
    }

    /// <summary>
    /// Positions systems on a page using context-aware vertical spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:488-535 spacing spec selection
    /// LILYPOND-REF: lily/page-layout-problem.cc:622-646 minimum distance from skylines
    /// </remarks>
    private ImmutableArray<SystemLayout> PositionSystemsOnPage(
        ImmutableArray<SystemLayout> allSystems,
        ImmutableArray<(double upExtent, double downExtent)> systemExtents,
        List<SystemDetails> systemDetails,
        int startIdx, int endIdx,
        bool isFirstPage, double headerHeight,
        double force, VerticalSpacingParameters vs)
    {
        var pageSystems = new List<SystemLayout>();

        // First system Y position
        var topSpec = vs.TopSystem;
        double currentY = isFirstPage
            ? _options.MarginTop + headerHeight + systemExtents[startIdx].upExtent + topSpec.Padding
            : _options.MarginTop + systemExtents[startIdx].upExtent + topSpec.Padding;

        for (int sysIdx = startIdx; sysIdx < endIdx; sysIdx++)
        {
            pageSystems.Add(allSystems[sysIdx] with { Y = currentY });

            if (sysIdx < endIdx - 1)
            {
                // Select spacing spec for this pair
                bool isFirst = sysIdx == startIdx;
                var spec = vs.SelectSpec(
                    isFirstOnPage: false, // Not first — we already placed first
                    isLastOnPage: false,
                    prevIsTitle: systemDetails[sysIdx].IsTitle,
                    currentIsTitle: systemDetails[sysIdx + 1].IsTitle,
                    currentIsNewScore: false);

                var d = systemDetails[sysIdx];

                // Skyline-based minimum distance
                double skylineDistance = _options.StaffHeight + d.BottomExtent
                    + systemExtents[sysIdx + 1].upExtent;
                double minDistance = Math.Max(skylineDistance, spec.MinimumDistance) + spec.Padding;

                // Spring-based ideal distance
                double springDistance = Math.Max(spec.BasicDistance, minDistance);

                // Apply force-based stretching
                double inverseHooke = spec.Stretchability > 0 ? spec.Stretchability / 60.0 : 0.1;
                double distance = springDistance + force * inverseHooke;

                // Never go below minimum
                currentY += Math.Max(distance, minDistance);
            }
        }

        return pageSystems.ToImmutableArray();
    }
}
