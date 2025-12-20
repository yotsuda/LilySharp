using Xunit;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

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
    
    private static SystemDetails CreateSystem(
        double height = 20,
        double staffHeight = 10,
        double topExtent = 5,
        double bottomExtent = 5,
        double padding = 2,
        double springLength = 3,
        bool forceBreakAfter = false)
    {
        return new SystemDetails
        {
            Height = height,
            TopExtent = topExtent,
            BottomExtent = bottomExtent,
            StaffHeight = staffHeight,
            Padding = padding,
            SpringLength = springLength,
            ForceBreakAfter = forceBreakAfter
        };
    }
}