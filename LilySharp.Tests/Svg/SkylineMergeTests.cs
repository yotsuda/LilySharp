using LilySharp.Core.Svg.Layout;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests.Svg;

/// <summary>
/// Tests for skyline merge algorithm - the core functionality.
/// Verifies that intersection points are correctly computed when merging.
/// </summary>
[Trait("Category", "Unit")]
public class SkylineMergeTests
{
    private readonly ITestOutputHelper _output;
    private const double Epsilon = 1e-6;

    public SkylineMergeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Two non-overlapping buildings should remain separate.
    /// </summary>
    [Fact]
    public void Merge_NonOverlapping_RemainsSeparate()
    {
        var skyline = VerticalSkyline.FromBox(0, 50, 0, 10, VerticalDirection.Up);
        var other = VerticalSkyline.FromBox(60, 100, 0, 15, VerticalDirection.Up);

        skyline.Merge(other);

        // Should have 2 non-empty buildings (plus empty fillers)
        int nonEmptyCount = skyline.Buildings.Count(b =>
            !double.IsNegativeInfinity(b.Height(b.XLeft)));

        _output.WriteLine($"Non-empty buildings: {nonEmptyCount}");
        foreach (var b in skyline.Buildings)
            _output.WriteLine($"  {b}");

        Assert.Equal(2, nonEmptyCount);
    }

    /// <summary>
    /// Two overlapping buildings at same height should merge into one.
    /// </summary>
    [Fact]
    public void Merge_SameHeight_MergesIntoOne()
    {
        var skyline = VerticalSkyline.FromBox(0, 60, 0, 10, VerticalDirection.Up);
        var other = VerticalSkyline.FromBox(40, 100, 0, 10, VerticalDirection.Up);

        skyline.Merge(other);

        // At x=50, height should be 10
        Assert.Equal(10, skyline.Height(50), Epsilon);

        // Should span the full range
        Assert.Equal(10, skyline.Height(10), Epsilon);
        Assert.Equal(10, skyline.Height(90), Epsilon);
    }

    /// <summary>
    /// Overlapping buildings at different heights - UP skyline keeps minimum Y.
    /// </summary>
    [Fact]
    public void Merge_DifferentHeights_UpKeepsMinimumY()
    {
        // UP skyline: keeps the topmost point (smallest Y in real coordinates)
        var skyline = VerticalSkyline.FromBox(0, 100, 0, 20, VerticalDirection.Up);  // yTop=20
        var other = VerticalSkyline.FromBox(30, 70, 0, 10, VerticalDirection.Up);    // yTop=10 (higher)

        skyline.Merge(other);

        // In overlap region [30,70], should keep yTop=10 (smaller Y = topmost)
        double heightAt50 = skyline.Height(50);
        _output.WriteLine($"Height at x=50: {heightAt50}");

        Assert.Equal(10, heightAt50, Epsilon);

        // Outside overlap, should be 20
        Assert.Equal(20, skyline.Height(10), Epsilon);
        Assert.Equal(20, skyline.Height(90), Epsilon);
    }

    /// <summary>
    /// DOWN skyline keeps maximum Y (bottommost point).
    /// </summary>
    [Fact]
    public void Merge_DifferentHeights_DownKeepsMaximumY()
    {
        // DOWN skyline: keeps the bottommost point (largest Y in real coordinates)
        var skyline = VerticalSkyline.FromBox(0, 100, 50, 60, VerticalDirection.Down);  // yBottom=50
        var other = VerticalSkyline.FromBox(30, 70, 70, 80, VerticalDirection.Down);    // yBottom=70 (lower)

        skyline.Merge(other);

        // In overlap region [30,70], should keep yBottom=70 (larger Y = bottommost)
        double heightAt50 = skyline.Height(50);
        _output.WriteLine($"Height at x=50: {heightAt50}");

        Assert.Equal(70, heightAt50, Epsilon);

        // Outside overlap, should be 50
        Assert.Equal(50, skyline.Height(10), Epsilon);
        Assert.Equal(50, skyline.Height(90), Epsilon);
    }

    /// <summary>
    /// Distance between UP and DOWN skylines.
    /// </summary>
    [Fact]
    public void Distance_UpAndDown_ReturnsGap()
    {
        // UP skyline at yTop=20
        var up = VerticalSkyline.FromBox(0, 100, 0, 20, VerticalDirection.Up);

        // DOWN skyline at yBottom=50
        var down = VerticalSkyline.FromBox(0, 100, 50, 60, VerticalDirection.Down);

        // Gap = 50 - 20 = 30
        double distance = up.Distance(down);

        _output.WriteLine($"UP maxHeight: {up.MaxHeight()}");
        _output.WriteLine($"DOWN maxHeight: {down.MaxHeight()}");
        _output.WriteLine($"Distance: {distance}");

        Assert.Equal(30, distance, Epsilon);
    }

    /// <summary>
    /// Multiple merges build up correct skyline.
    /// </summary>
    [Fact]
    public void MultipleMerges_BuildsCorrectSkyline()
    {
        var skyline = new VerticalSkyline(VerticalDirection.Up);

        // Add three buildings at different positions and heights
        skyline.Merge(VerticalSkyline.FromBox(0, 40, 0, 30, VerticalDirection.Up));    // yTop=30
        skyline.Merge(VerticalSkyline.FromBox(20, 60, 0, 10, VerticalDirection.Up));   // yTop=10 (highest)
        skyline.Merge(VerticalSkyline.FromBox(50, 100, 0, 25, VerticalDirection.Up));  // yTop=25

        // Check heights at various points
        Assert.Equal(30, skyline.Height(10), Epsilon);  // Only first
        Assert.Equal(10, skyline.Height(30), Epsilon);  // First and second overlap, keep 10
        Assert.Equal(10, skyline.Height(55), Epsilon);  // Second and third overlap, keep 10
        Assert.Equal(25, skyline.Height(80), Epsilon);  // Only third
    }
}
