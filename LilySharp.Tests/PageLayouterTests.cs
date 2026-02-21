using System.Collections.Immutable;
using Xunit;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

public class PageLayouterTests
{
    private static LayoutOptions CreateOptions(
        double pageHeight = 100,
        double pageWidth = 80,
        double marginTop = 5,
        double marginBottom = 5,
        double staffHeight = 4,
        double systemSpacing = 8,
        double topSystemPadding = 1)
    {
        return new LayoutOptions
        {
            PageHeight = pageHeight,
            PageWidth = pageWidth,
            MarginTop = marginTop,
            MarginBottom = marginBottom,
            StaffHeight = staffHeight,
            SystemSpacing = systemSpacing,
            TopSystemPadding = topSystemPadding,
            UseOptimalPageBreaking = true
        };
    }

    private static ImmutableArray<SystemLayout> CreateDummySystems(int count)
    {
        var builder = ImmutableArray.CreateBuilder<SystemLayout>(count);
        for (int i = 0; i < count; i++)
        {
            builder.Add(new SystemLayout(
                SystemIndex: i,
                Y: 0, // will be overwritten by PageLayouter
                Width: 76,
                PrefixWidth: 5,
                Measures: ImmutableArray<MeasureLayout>.Empty));
        }
        return builder.ToImmutable();
    }

    [Fact]
    public void PerSystemExtents_ProduceDifferentYPositions()
    {
        // Three systems with different skyline extents should get different spacing
        var options = CreateOptions(pageHeight: 200);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(3);

        // System 0: small extents, System 1: large down, System 2: large up
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 5.0),
            (upExtent: 4.0, downExtent: 1.0));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 3, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        Assert.Equal(3, pageSystems.Length);

        // Gap between system 0 and 1 should differ from gap between system 1 and 2
        // because extents differ
        double gap01 = pageSystems[1].Y - pageSystems[0].Y;
        double gap12 = pageSystems[2].Y - pageSystems[1].Y;

        // System 0 has small downExtent (1), system 1 has small upExtent (1)
        // System 1 has large downExtent (5), system 2 has large upExtent (4)
        // So gap12 should be larger than gap01
        Assert.True(gap12 > gap01,
            $"Expected gap12 ({gap12:F2}) > gap01 ({gap01:F2}) due to larger extents");
    }

    [Fact]
    public void ForceDistribution_StretchesSpacing()
    {
        // Two small systems on a large page should have spacing larger than minimum
        var options = CreateOptions(pageHeight: 200, staffHeight: 4, systemSpacing: 8);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 0, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        double gap = pageSystems[1].Y - pageSystems[0].Y;

        // Minimum distance: staffHeight + bottomExtent + padding + springLength + nextTopExtent
        // = 4 + 1 + 4 + 4 + 1 = 14 (at force=0)
        // With positive force the gap should be larger
        double minGap = options.StaffHeight + 1.0 + options.SystemSpacing * 0.5 + options.SystemSpacing * 0.5 + 1.0;
        Assert.True(gap > minGap,
            $"Expected gap ({gap:F2}) > minimum ({minGap:F2}) due to force stretching");
    }

    [Fact]
    public void OverfullPage_ClampsForce_MaintainsMinSpacing()
    {
        // Systems barely fit on page — force should be clamped to 0 (no compression)
        // Available: 50 - 5 - 5 = 40. Two systems need at least ~14 each ≈ 28 total, fits
        // But make it tighter: staffHeight=10, extents large
        var options = CreateOptions(pageHeight: 60, staffHeight: 10, systemSpacing: 4, marginTop: 3, marginBottom: 3);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        // Large extents to fill the page
        var extents = ImmutableArray.Create(
            (upExtent: 3.0, downExtent: 6.0),
            (upExtent: 3.0, downExtent: 6.0));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 0, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        double gap = pageSystems[1].Y - pageSystems[0].Y;

        // Minimum gap at force=0: staffHeight + bottomExtent + padding + springLength + nextTopExtent
        // = 10 + 6 + 2 + 2 + 3 = 23
        double expectedMinGap = options.StaffHeight + 6.0 + options.SystemSpacing * 0.5 + options.SystemSpacing * 0.5 + 3.0;
        Assert.True(gap >= expectedMinGap - 0.01,
            $"Expected gap ({gap:F2}) >= minimum ({expectedMinGap:F2}), systems should not overlap");
    }

    [Fact]
    public void EmptySystems_ReturnsEmpty()
    {
        var options = CreateOptions();
        var layouter = new PageLayouter(options);
        var systems = ImmutableArray<SystemLayout>.Empty;
        var extents = ImmutableArray<(double, double)>.Empty;

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 3, extents);

        Assert.Empty(pages);
    }
}
