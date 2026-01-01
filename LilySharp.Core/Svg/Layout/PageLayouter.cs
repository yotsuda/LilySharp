using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Creates pages from systems using optimal page breaking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc Page_spacer class
/// LILYPOND-REF: lily/page-layout-problem.cc
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
    /// Uses dynamic programming to find optimal page breaks.
    /// </remarks>
    public ImmutableArray<PageLayout> CreatePagesWithOptimalBreaking(
        ImmutableArray<SystemLayout> systems,
        double headerHeight,
        double systemUpExtent,
        double systemDownExtent)
    {
        if (systems.Length == 0)
        {
            return ImmutableArray<PageLayout>.Empty;
        }

        // Create SystemDetails for each system
        var systemDetails = new List<SystemDetails>();
        foreach (var system in systems)
        {
            // Calculate system height (staff + extents)
            double staffHeight = _options.StaffHeight;

            // For now, use simple estimates for extents
            // TODO: Calculate actual skyline extents per system
            double topExtent = systemUpExtent;
            double bottomExtent = systemDownExtent;

            systemDetails.Add(PageBreaker.CreateFromLayout(
                staffHeight: staffHeight,
                topExtent: topExtent,
                bottomExtent: bottomExtent,
                padding: _options.SystemSpacing * 0.5,
                springLength: _options.SystemSpacing * 0.5));
        }

        // Run page breaker
        var breaker = new PageBreaker(
            pageHeight: _options.PageHeight,
            topMargin: _options.MarginTop,
            bottomMargin: _options.MarginBottom,
            headerHeight: headerHeight);

        var breakPoints = breaker.BreakIntoPages(systemDetails);

        // Create pages from break points
        var pages = new List<PageLayout>();
        int systemStart = 0;

        for (int pageIdx = 0; pageIdx < breakPoints.Count; pageIdx++)
        {
            int systemEnd = breakPoints[pageIdx];
            bool isFirstPage = pageIdx == 0;

            // Collect systems for this page
            var pageSystems = new List<SystemLayout>();
            double currentY = _options.MarginTop + (isFirstPage ? headerHeight + systemUpExtent + _options.TopSystemPadding : _options.TopSystemPadding);

            for (int sysIdx = systemStart; sysIdx < systemEnd; sysIdx++)
            {
                // Create new SystemLayout with updated Y position
                var original = systems[sysIdx];
                var updated = original with { Y = currentY };
                pageSystems.Add(updated);
                currentY += _options.StaffHeight + _options.SystemSpacing;
            }

            pages.Add(new PageLayout(
                PageIndex: pageIdx,
                Width: _options.PageWidth,
                Height: _options.PageHeight,
                HeaderHeight: isFirstPage ? headerHeight : 0,
                Systems: pageSystems.ToImmutableArray()));

            systemStart = systemEnd;
        }

        return pages.ToImmutableArray();
    }
}
