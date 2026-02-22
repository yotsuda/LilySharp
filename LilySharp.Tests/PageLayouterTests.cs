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
        // Disable ragged-last-bottom to allow vertical justification
        var options = CreateOptions(pageHeight: 200, staffHeight: 4, systemSpacing: 8) with
        {
            PageBreaking = new PageBreakingParameters { RaggedLastBottom = false }
        };
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 0, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        double gap = pageSystems[1].Y - pageSystems[0].Y;

        // Minimum distance: skyline + padding
        // skylineDist = staffHeight + bottomExtent + nextTopExtent = 4 + 1 + 1 = 6
        // minDist = max(6, 8) + 1 = 9 (min-distance=8 from system-system default)
        // basicDist = max(12, 9) = 12
        // With positive force the gap should be larger than basicDist
        var spec = options.VerticalSpacing.SystemSystem;
        double skylineDist = options.StaffHeight + 1.0 + 1.0;
        double minGap = Math.Max(Math.Max(skylineDist, spec.MinimumDistance) + spec.Padding, spec.BasicDistance);
        Assert.True(gap > minGap,
            $"Expected gap ({gap:F2}) > minimum ({minGap:F2}) due to force stretching");
    }

    [Fact]
    public void OverfullPage_ClampsForce_MaintainsMinSpacing()
    {
        // Systems barely fit on page — force should be clamped to 0 (no compression)
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

        // Minimum gap: skylineDistance + padding
        // skylineDistance = staffHeight + bottomExtent + nextTopExtent = 10 + 6 + 3 = 19
        // minDistance = max(skylineDistance, spec.MinimumDistance) + spec.Padding = max(19, 8) + 1 = 20
        double skylineDist = options.StaffHeight + 6.0 + 3.0;
        double vs = options.VerticalSpacing.SystemSystem.MinimumDistance;
        double expectedMinGap = Math.Max(skylineDist, vs) + options.VerticalSpacing.SystemSystem.Padding;
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

    // --- VerticalSpacingParameters tests ---

    [Fact]
    public void VerticalSpacingParameters_Default_MatchesLilyPond()
    {
        var p = VerticalSpacingParameters.Default;

        // LILYPOND-REF: ly/paper-defaults-init.ly:64-89
        Assert.Equal(12, p.SystemSystem.BasicDistance);
        Assert.Equal(8, p.SystemSystem.MinimumDistance);
        Assert.Equal(1, p.SystemSystem.Padding);
        Assert.Equal(60, p.SystemSystem.Stretchability);

        Assert.Equal(14, p.ScoreSystem.BasicDistance);
        Assert.Equal(120, p.ScoreSystem.Stretchability);

        Assert.Equal(5, p.MarkupSystem.BasicDistance);
        Assert.Equal(0.5, p.MarkupSystem.Padding);

        Assert.Equal(1, p.TopSystem.BasicDistance);
        Assert.Equal(1, p.TopSystem.Padding);

        Assert.Equal(1, p.LastBottom.BasicDistance);
        Assert.Equal(30, p.LastBottom.Stretchability);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_FirstOnPage_ReturnsTopSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: true, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.TopSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_SystemAfterTitle_ReturnsMarkupSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: true, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.MarkupSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_NewScore_ReturnsScoreSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: true);

        Assert.Equal(p.ScoreSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_NormalSystems_ReturnsSystemSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.SystemSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_TitleAfterSystem_ReturnsScoreMarkup()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: true,
            currentIsNewScore: false);

        Assert.Equal(p.ScoreMarkup.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_TitleAfterTitle_ReturnsMarkupMarkup()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: true, currentIsTitle: true,
            currentIsNewScore: false);

        Assert.Equal(p.MarkupMarkup.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_CustomParameters_AffectSpacing()
    {
        // Tighter system-system spacing
        var customVs = new VerticalSpacingParameters
        {
            SystemSystem = new VerticalSpacingSpec
            {
                BasicDistance = 8,
                MinimumDistance = 6,
                Padding = 0.5,
                Stretchability = 30
            }
        };
        var options = CreateOptions(pageHeight: 200) with
        {
            VerticalSpacing = customVs,
            PageBreaking = new PageBreakingParameters { RaggedLastBottom = false }
        };
        var defaultOptions = CreateOptions(pageHeight: 200) with
        {
            PageBreaking = new PageBreakingParameters { RaggedLastBottom = false }
        };

        var layouterCustom = new PageLayouter(options);
        var layouterDefault = new PageLayouter(defaultOptions);
        var systems = CreateDummySystems(3);
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var customPages = layouterCustom.CreatePagesWithOptimalBreaking(systems, 0, extents);
        var defaultPages = layouterDefault.CreatePagesWithOptimalBreaking(systems, 0, extents);

        double customGap = customPages[0].Systems[1].Y - customPages[0].Systems[0].Y;
        double defaultGap = defaultPages[0].Systems[1].Y - defaultPages[0].Systems[0].Y;

        // Custom has smaller basic-distance (8 vs 12), so gap should be smaller
        Assert.True(customGap < defaultGap,
            $"Custom gap ({customGap:F2}) should be < default ({defaultGap:F2})");
    }
}
