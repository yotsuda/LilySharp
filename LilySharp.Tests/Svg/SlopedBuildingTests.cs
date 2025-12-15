using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests.Svg;

/// <summary>
/// Tests for sloped Building support in VerticalSkyline.
/// LILYPOND-REF: lily/skyline.cc Building struct
/// </summary>
public class SlopedBuildingTests
{
    private const double Epsilon = 1e-6;
    
    [Fact]
    public void Building_HorizontalHeight_ReturnsConstant()
    {
        // Horizontal building: height = 5 everywhere
        var b = new Building(0, 10, 5);
        
        Assert.Equal(5, b.Height(0), Epsilon);
        Assert.Equal(5, b.Height(5), Epsilon);
        Assert.Equal(5, b.Height(10), Epsilon);
    }
    
    [Fact]
    public void Building_SlopedHeight_ReturnsLinearInterpolation()
    {
        // Sloped building: from (0, 0) to (10, 10)
        var b = new Building(0, 0, 10, 10);
        
        Assert.Equal(0, b.Height(0), Epsilon);
        Assert.Equal(5, b.Height(5), Epsilon);
        Assert.Equal(10, b.Height(10), Epsilon);
    }
    
    [Fact]
    public void Building_IntersectionX_ParallelLines()
    {
        // Parallel buildings - should return max of left X
        var b1 = new Building(0, 5, 5, 10);
        var b2 = new Building(5, 10, 10, 15);
        
        double ix = b1.IntersectionX(b2);
        Assert.Equal(5, ix, Epsilon);
    }
    
    [Fact]
    public void Building_IntersectionX_CrossingLines()
    {
        // b1: from (0, 0) to (10, 10) -> y = x
        // b2: from (0, 10) to (10, 0) -> y = -x + 10
        // Intersection: x = -x + 10 -> 2x = 10 -> x = 5
        var b1 = new Building(0, 0, 10, 10);
        var b2 = new Building(0, 10, 0, 10);
        
        double ix = b1.IntersectionX(b2);
        Assert.Equal(5, ix, Epsilon);
    }
    
    [Fact]
    public void Building_Above_HorizontalVsSloped()
    {
        // Horizontal at y=5
        var horizontal = new Building(0, 10, 5);
        // Sloped from y=0 to y=10
        var sloped = new Building(0, 0, 10, 10);
        
        // At x=3: sloped=3, horizontal=5 -> horizontal above
        Assert.True(horizontal.Above(sloped, 3));
        Assert.False(sloped.Above(horizontal, 3));
        
        // At x=7: sloped=7, horizontal=5 -> sloped above
        Assert.True(sloped.Above(horizontal, 7));
        Assert.False(horizontal.Above(sloped, 7));
    }
    
    [Fact]
    public void VerticalSkyline_FromSlope_CreatesCorrectBuilding()
    {
        // Create a sloped skyline (like a beam)
        var skyline = VerticalSkyline.FromSlope(0, 10, 100, 20, 2, VerticalDirection.Up);
        
        Assert.False(skyline.IsEmpty);
        Assert.Single(skyline.Buildings);
        
        var b = skyline.Buildings[0];
        Assert.Equal(0, b.XLeft, Epsilon);
        Assert.Equal(100, b.XRight, Epsilon);
        
        // Building.Height() returns internal representation (negative for UP skyline)
        // VerticalSkyline.Height() returns real Y coordinate
        Assert.Equal(10, skyline.Height(0), Epsilon);
        Assert.Equal(20, skyline.Height(100), Epsilon);
    }
    
    [Fact]
    public void VerticalSkyline_Merge_HorizontalBuildings()
    {
        // Two horizontal UP skylines at different yTop values
        // s1: yTop=10 (internal height = -10)
        // s2: yTop=15 (internal height = -15)
        var s1 = VerticalSkyline.FromBox(0, 50, 0, 10, VerticalDirection.Up);
        var s2 = VerticalSkyline.FromBox(25, 75, 0, 15, VerticalDirection.Up);
        
        s1.Merge(s2);
        
        // For UP skyline, we keep the minimum Y (topmost, most negative internal)
        // s1 has yTop=10 (more negative = -10), s2 has yTop=15 (-15)
        // -15 < -10, so s2 is "higher" in UP skyline terms
        
        // At x=30 (overlap): keep yTop=10 (smaller Y = topmost)
        Assert.Equal(10, s1.Height(30), Epsilon);
        
        // At x=10: only s1, yTop=10
        Assert.Equal(10, s1.Height(10), Epsilon);
        
        // At x=60: only s2, yTop=15
        Assert.Equal(15, s1.Height(60), Epsilon);
    }
    
    [Fact]
    public void VerticalSkyline_Merge_SlopedBuildings()
    {
        // Create two crossing sloped UP skylines
        // s1: rises from y=0 to y=10 (internal: -0 to -10)
        // s2: falls from y=10 to y=0 (internal: -10 to -0)
        var s1 = VerticalSkyline.FromSlope(0, 0, 10, 10, 0, VerticalDirection.Up);
        var s2 = VerticalSkyline.FromSlope(0, 10, 10, 0, 0, VerticalDirection.Up);
        
        s1.Merge(s2);
        
        // At x=5 (intersection), both have real height 5
        // For UP skyline, "higher" means smaller Y (more negative internal)
        // At x=2: s1=2, s2=8 -> s2 is higher (smaller Y) -> result should be 2
        // At x=8: s1=8, s2=2 -> s1 is higher (smaller Y) -> result should be 2
        // Wait, for UP skyline we keep the MINIMUM Y (topmost)
        Assert.True(s1.Height(2) <= 2.1, $"At x=2, expected ~2 (min of 2,8), got {s1.Height(2)}");
        Assert.True(s1.Height(8) <= 2.1, $"At x=8, expected ~2 (min of 8,2), got {s1.Height(8)}");
    }
    
    [Fact]
    public void VerticalSkyline_Distance_WithSlopedBuildings()
    {
        // UP skyline with sloped roof at y=10 to y=20
        var up = VerticalSkyline.FromSlope(0, 10, 100, 20, 0, VerticalDirection.Up);
        
        // DOWN skyline at y=50 to y=60
        var down = VerticalSkyline.FromBox(0, 100, 50, 60, VerticalDirection.Down);
        
        // Distance = gap between the two skylines
        // UP skyline top edge: y=10 to y=20
        // DOWN skyline bottom edge: y=50
        // Gap at x=0: 50 - 10 = 40
        // Gap at x=100: 50 - 20 = 30
        // LilyPond distance = UP.internalHeight + DOWN.internalHeight
        //   = (-10 to -20) + 50 = 40 to 30
        double dist = up.Distance(down);
        
        // Maximum distance is 40 (at x=0)
        Assert.True(dist >= 38 && dist <= 42, $"Expected ~40, got {dist}");
    }
    
    [Fact]
    public void Building_WithXRange_PreservesSlope()
    {
        // Original: from (0, 0) to (10, 10)
        var original = new Building(0, 0, 10, 10);
        
        // Extract middle portion
        var middle = original.WithXRange(3, 7);
        
        Assert.Equal(3, middle.XLeft, Epsilon);
        Assert.Equal(7, middle.XRight, Epsilon);
        Assert.Equal(3, middle.Height(3), Epsilon);
        Assert.Equal(7, middle.Height(7), Epsilon);
        Assert.Equal(original.Slope, middle.Slope, Epsilon);
    }
}