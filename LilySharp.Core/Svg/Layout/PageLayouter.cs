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

        // Create SystemDetails for each system using per-system skyline extents
        var systemDetails = new List<SystemDetails>();
        for (int i = 0; i < systems.Length; i++)
        {
            double staffHeight = _options.StaffHeight;
            double topExtent = systemExtents[i].upExtent;
            double bottomExtent = systemExtents[i].downExtent;

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

        // Create pages from break points with force-based Y positioning
        var pages = new List<PageLayout>();
        int systemStart = 0;

        for (int pageIdx = 0; pageIdx < breakPoints.Count; pageIdx++)
        {
            int systemEnd = breakPoints[pageIdx];
            bool isFirstPage = pageIdx == 0;

            // Reconstruct PageSpacing for this page to get the force
            double topMargin = isFirstPage
                ? _options.MarginTop + headerHeight
                : _options.MarginTop;
            var pageSpacing = new PageSpacing(_options.PageHeight, topMargin, _options.MarginBottom);
            for (int sysIdx = systemStart; sysIdx < systemEnd; sysIdx++)
                pageSpacing.AppendSystem(systemDetails[sysIdx]);

            // Clamp force: don't compress below minimum spacing on overfull pages
            double force = Math.Max(0, pageSpacing.Force);
            if (double.IsNegativeInfinity(pageSpacing.Force) || double.IsNaN(pageSpacing.Force))
                force = 0;

            // Position systems using force-based spacing
            var pageSystems = new List<SystemLayout>();
            double currentY = _options.MarginTop
                + (isFirstPage ? headerHeight + systemExtents[systemStart].upExtent + _options.TopSystemPadding
                               : systemExtents[systemStart].upExtent + _options.TopSystemPadding);

            for (int sysIdx = systemStart; sysIdx < systemEnd; sysIdx++)
            {
                pageSystems.Add(systems[sysIdx] with { Y = currentY });

                if (sysIdx < systemEnd - 1)
                {
                    var d = systemDetails[sysIdx];
                    // Distance = staffHeight + bottomExtent + padding + springLength + force*flexibility + nextTopExtent
                    currentY += _options.StaffHeight + d.BottomExtent
                              + d.Padding + d.SpringLength + force * d.InverseHooke
                              + systemExtents[sysIdx + 1].upExtent;
                }
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
