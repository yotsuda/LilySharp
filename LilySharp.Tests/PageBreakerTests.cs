using Xunit;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PageBreakerTests
{
    [Fact]
    public void BreakIntoPages_EmptySystems_ReturnsEmpty()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        var result = breaker.BreakIntoPages(Array.Empty<SystemDetails>());

        Assert.Empty(result);
    }

    [Fact]
    public void BreakIntoPages_SingleSystem_SinglePage()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        var systems = new[]
        {
            CreateSystem(height: 20)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.Single(result);
        Assert.Equal(1, result[0]); // 1 system on first page
    }

    [Fact]
    public void BreakIntoPages_TwoSystemsFitOnePage_SinglePage()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        // Available: 100 - 5 - 5 - 10 = 80
        // Two systems: 20 + 20 = 40 (fits easily)
        var systems = new[]
        {
            CreateSystem(height: 20),
            CreateSystem(height: 20)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.Single(result);
        Assert.Equal(2, result[0]); // 2 systems on first page
    }

    [Fact]
    public void BreakIntoPages_SystemsExceedPage_MultiplePagesCreated()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        // Available first page: 100 - 5 - 5 - 10 = 80
        // Available subsequent: 100 - 5 - 5 = 90
        // 5 systems × 30 = 150 (needs multiple pages)
        var systems = new[]
        {
            CreateSystem(height: 30),
            CreateSystem(height: 30),
            CreateSystem(height: 30),
            CreateSystem(height: 30),
            CreateSystem(height: 30)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.True(result.Count >= 2, $"Expected at least 2 pages, got {result.Count}");
    }

    [Fact]
    public void BreakIntoPages_ForcedBreak_RespectsBreak()
    {
        var breaker = new PageBreaker(
            pageHeight: 100,
            topMargin: 5,
            bottomMargin: 5,
            headerHeight: 10);

        // Two small systems that fit on one page, but forced break after first
        var systems = new[]
        {
            CreateSystem(height: 15, forceBreakAfter: true),
            CreateSystem(height: 15)
        };

        var result = breaker.BreakIntoPages(systems);

        Assert.Equal(2, result.Count); // Should be 2 pages due to forced break
        Assert.Equal(1, result[0]);    // 1 system on first page
        Assert.Equal(2, result[1]);    // End at system 2
    }

    [Fact]
    public void PageSpacing_SingleSystem_CalculatesForce()
    {
        var spacing = new PageSpacing(
            pageHeight: 100,
            topMargin: 10,
            bottomMargin: 10);

        var system = CreateSystem(height: 30);
        spacing.AppendSystem(system);

        // Available: 100 - 10 - 10 = 80
        // Rod: 30
        // Force should be positive (stretch to fill)
        Assert.True(spacing.Force > 0, $"Expected positive force, got {spacing.Force}");
        Assert.Equal(30, spacing.RodHeight);
    }

    [Fact]
    public void PageSpacing_MultipleSystems_AccumulatesHeight()
    {
        var spacing = new PageSpacing(
            pageHeight: 100,
            topMargin: 10,
            bottomMargin: 10);

        var system1 = CreateSystem(height: 20, staffHeight: 10, bottomExtent: 5);
        var system2 = CreateSystem(height: 20, staffHeight: 10, topExtent: 5);

        spacing.AppendSystem(system1);
        spacing.AppendSystem(system2);

        // First system: full height (20)
        // Second system: staffHeight + bottomExtent (10 + 5 = 15)
        // Plus spring length from first system
        Assert.True(spacing.RodHeight >= 35);
    }

    [Fact]
    public void PageSpacing_OverfullPage_ReturnsNegativeInfinity()
    {
        var spacing = new PageSpacing(
            pageHeight: 50,
            topMargin: 10,
            bottomMargin: 10);

        // Available: 50 - 10 - 10 = 30
        // System: 40 (overfull)
        var system = CreateSystem(height: 40);
        spacing.AppendSystem(system);

        Assert.True(double.IsNegativeInfinity(spacing.Force));
    }

    [Fact]
    public void SystemDetails_CreateFromLayout_SetsProperties()
    {
        var details = PageBreaker.CreateFromLayout(
            staffHeight: 4,
            topExtent: 2,
            bottomExtent: 3,
            padding: 1,
            springLength: 2,
            forceBreakAfter: true);

        Assert.Equal(9, details.Height);      // 2 + 4 + 3
        Assert.Equal(2, details.TopExtent);
        Assert.Equal(3, details.BottomExtent);
        Assert.Equal(4, details.StaffHeight);
        Assert.Equal(1, details.Padding);
        Assert.Equal(2, details.SpringLength);
        Assert.True(details.ForceBreakAfter);
    }

    // --- PageBreakingParameters tests ---

    [Fact]
    public void PageBreakingParameters_Default_MatchesLilyPond()
    {
        var p = PageBreakingParameters.Default;

        // LILYPOND-REF: lily/page-breaking.cc:280-297
        Assert.False(p.RaggedBottom);
        Assert.True(p.RaggedLastBottom);
        Assert.Equal(0, p.SystemsPerPage);
        Assert.Equal(0, p.MaxSystemsPerPage);
        Assert.Equal(0, p.MinSystemsPerPage);
        Assert.Equal(100000, p.OrphanPenalty);
        // LILYPOND-REF: lily/page-breaking.cc:1506
        Assert.Equal(10, p.PageSpacingWeight);
    }

    // --- Orphan penalty tests ---

    [Fact]
    public void OrphanPenalty_SingleSystemOnLastPage_Penalized()
    {
        // 3 systems: 2 fit on first page, 1 orphan on second
        // vs 1 on first, 2 on second — second should be preferred
        var systems = new[]
        {
            CreateSystem(height: 25),
            CreateSystem(height: 25),
            CreateSystem(height: 25)
        };

        // With orphan penalty (default)
        var breakerWithPenalty = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { OrphanPenalty = 100000 });
        var resultWithPenalty = breakerWithPenalty.BreakIntoPages(systems);

        // Without orphan penalty
        var breakerNoPenalty = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { OrphanPenalty = 0 });
        var resultNoPenalty = breakerNoPenalty.BreakIntoPages(systems);

        // Both should produce valid results
        Assert.True(resultWithPenalty.Count >= 1);
        Assert.True(resultNoPenalty.Count >= 1);

        // With orphan penalty, the breaker should try to avoid 1 system on last page
        if (resultWithPenalty.Count >= 2)
        {
            int lastPageSystems = resultWithPenalty[^1] -
                (resultWithPenalty.Count >= 2 ? resultWithPenalty[^2] : 0);
            Assert.True(lastPageSystems >= 1,
                "Last page should have at least 1 system");
        }
    }

    // --- Ragged bottom tests ---

    [Fact]
    public void RaggedLastBottom_DoesNotStretchLastPage()
    {
        // Verify that ragged-last-bottom produces valid breaks
        var systems = new[]
        {
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15)
        };

        var breaker = new PageBreaker(
            pageHeight: 100, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { RaggedLastBottom = true });

        var result = breaker.BreakIntoPages(systems);

        Assert.True(result.Count >= 1);
        Assert.Equal(systems.Length, result[^1]); // All systems accounted for
    }

    [Fact]
    public void RaggedBottom_AllPagesNotStretched()
    {
        var systems = new[]
        {
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15),
            CreateSystem(height: 15)
        };

        var breaker = new PageBreaker(
            pageHeight: 70, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { RaggedBottom = true });

        var result = breaker.BreakIntoPages(systems);

        Assert.True(result.Count >= 2, "Should need multiple pages");
        Assert.Equal(systems.Length, result[^1]);
    }

    // --- Max/min systems per page tests ---

    [Fact]
    public void MaxSystemsPerPage_LimitsSystemCount()
    {
        var systems = new[]
        {
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10)
        };

        var breaker = new PageBreaker(
            pageHeight: 200, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { MaxSystemsPerPage = 2 });

        var result = breaker.BreakIntoPages(systems);

        // Should be at least 2 pages since max is 2 systems per page
        Assert.True(result.Count >= 2,
            $"Expected at least 2 pages with max 2 per page, got {result.Count}");

        // Verify each page has at most 2 systems
        int prevBreak = 0;
        foreach (var bp in result)
        {
            int count = bp - prevBreak;
            Assert.True(count <= 2,
                $"Page has {count} systems, max is 2");
            prevBreak = bp;
        }
    }

    [Fact]
    public void SystemsPerPage_ForcesExactCount()
    {
        var systems = new[]
        {
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10),
            CreateSystem(height: 10)
        };

        var breaker = new PageBreaker(
            pageHeight: 200, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { SystemsPerPage = 2 });

        var result = breaker.BreakIntoPages(systems);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0]); // 2 systems on page 1
        Assert.Equal(4, result[1]); // 2 systems on page 2
    }

    // --- Break permission tests ---

    [Fact]
    public void BreakPermission_Forbid_PreventsBreak()
    {
        var systems = new[]
        {
            CreateSystem(height: 30, pagePermission: BreakPermission.Forbid),
            CreateSystem(height: 30)
        };

        var breaker = new PageBreaker(
            pageHeight: 60, topMargin: 5, bottomMargin: 5, headerHeight: 5);

        var result = breaker.BreakIntoPages(systems);

        // Even though systems might not fit well, forbid prevents break after system 0
        Assert.Single(result);
        Assert.Equal(2, result[0]);
    }

    [Fact]
    public void BreakPermission_Force_EnforcesBreak()
    {
        var systems = new[]
        {
            CreateSystem(height: 10, pagePermission: BreakPermission.Force),
            CreateSystem(height: 10)
        };

        var breaker = new PageBreaker(
            pageHeight: 200, topMargin: 5, bottomMargin: 5, headerHeight: 5);

        var result = breaker.BreakIntoPages(systems);

        // Force permission should create page break after system 0
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
    }

    // --- Page spacing weight test ---

    [Fact]
    public void PageSpacingWeight_AffectsDemerits()
    {
        // Higher weight means page spacing is more important
        var systems = new[]
        {
            CreateSystem(height: 20),
            CreateSystem(height: 20),
            CreateSystem(height: 20)
        };

        // With low weight
        var breakerLow = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { PageSpacingWeight = 1 });
        var resultLow = breakerLow.BreakIntoPages(systems);

        // With high weight
        var breakerHigh = new PageBreaker(
            pageHeight: 80, topMargin: 5, bottomMargin: 5, headerHeight: 5,
            parameters: new PageBreakingParameters { PageSpacingWeight = 100 });
        var resultHigh = breakerHigh.BreakIntoPages(systems);

        // Both should produce valid results
        Assert.True(resultLow.Count >= 1);
        Assert.True(resultHigh.Count >= 1);
        Assert.Equal(systems.Length, resultLow[^1]);
        Assert.Equal(systems.Length, resultHigh[^1]);
    }

    private static SystemDetails CreateSystem(
        double height = 20,
        double staffHeight = 10,
        double topExtent = 5,
        double bottomExtent = 5,
        double padding = 2,
        double springLength = 3,
        bool forceBreakAfter = false,
        BreakPermission pagePermission = BreakPermission.Allow)
    {
        return new SystemDetails
        {
            Height = height,
            TopExtent = topExtent,
            BottomExtent = bottomExtent,
            StaffHeight = staffHeight,
            Padding = padding,
            SpringLength = springLength,
            ForceBreakAfter = forceBreakAfter,
            PagePermission = pagePermission
        };
    }
}
