// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
    /// MaxProtrusionInRange returns the peak rise above Y=0 within the window only —
    /// the basis for raising a chord-name row over the notes its symbols overhang.
    /// </summary>
    [Fact]
    public void MaxProtrusionInRange_PeakWithinWindowOnly()
    {
        // A 3-sp protrusion over [20,30] and a taller 5-sp one over [40,50]
        // (Y-up above the top line).
        var sky = VerticalSkyline.FromBox(20, 30, 3, 3, VerticalDirection.Up);
        sky.Merge(VerticalSkyline.FromBox(40, 50, 5, 5, VerticalDirection.Up));

        Assert.Equal(3.0, sky.MaxProtrusionInRange(15, 35), 3);  // first box only
        Assert.Equal(5.0, sky.MaxProtrusionInRange(15, 55), 3);  // both → taller wins
        Assert.Equal(0.0, sky.MaxProtrusionInRange(-10, 5), 3);  // left of everything
    }

    /// <summary>
    /// Two systems' facing skylines are exactly as far apart as their ink, and no
    /// further — the contract everything that seeds a box into a system skyline relies on.
    /// </summary>
    /// <remarks>
    /// Written to settle a specific question rather than for coverage. Seeding the opening
    /// CLEF into the system skylines (it is the extreme ink on a plain score, and LilyPond
    /// carries it) moved system.natural-distance from exact to +1.110000, which works back
    /// to an inter-system distance of 9.110000 where the boxes say 7.350000. Either the
    /// skyline arithmetic disagrees with the boxes or its consumer does, and only one of
    /// those is worth debugging at a time.
    ///
    /// The geometry is the real one, in the system frame the builder uses (Y-up, origin at
    /// the system's top staff line): a staff whose bottom line is 4 below the origin, and a
    /// treble clef anchored one staff-space below the middle line, its ink spanning
    /// GlyphMetrics.ClefG = (-2.550 .. +4.800) about that anchor, i.e. -5.550 .. +1.800
    /// here. Facing systems are that far apart: 5.550 of hanging ink plus 1.800 of rising.
    /// </remarks>
    [Fact]
    public void Distance_BetweenFacingSystems_IsTheirInkAndNoMore()
    {
        const double clefLeft = 0.3, clefRight = 2.865;
        const double clefBottomUp = -5.55, clefTopUp = 1.8;

        // The system ABOVE: its floor is the staff's bottom line, plus the clef's ink.
        var down = VerticalSkyline.FromBox(0, 100, -4, -4, VerticalDirection.Down);
        down.Merge(VerticalSkyline.FromBox(
            clefLeft, clefRight, clefBottomUp, clefTopUp, VerticalDirection.Down));

        // The system BELOW: its roof is its top staff line, plus the same clef.
        var up = VerticalSkyline.FromBox(0, 100, 0, 0, VerticalDirection.Up);
        up.Merge(VerticalSkyline.FromBox(
            clefLeft, clefRight, clefBottomUp, clefTopUp, VerticalDirection.Up));

        _output.WriteLine($"down.MaxHeight = {down.MaxHeight():F6}  (expect -5.550000)");
        _output.WriteLine($"up.MaxHeight   = {up.MaxHeight():F6}  (expect  1.800000)");
        _output.WriteLine($"distance       = {up.Distance(down, 1.0):F6}  (expect  7.350000)");

        Assert.Equal(-5.55, down.MaxHeight(), 6);
        Assert.Equal(1.8, up.MaxHeight(), 6);
        Assert.Equal(7.35, up.Distance(down, 1.0), 6);
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
            !double.IsNegativeInfinity(b.ValueAt(b.Start)));

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
    /// Overlapping buildings at different heights - UP skyline keeps the topmost
    /// point (largest Y-up).
    /// </summary>
    [Fact]
    public void Merge_DifferentHeights_UpKeepsTopmostYUp()
    {
        // UP skyline: keeps the topmost point (largest Y-up)
        var skyline = VerticalSkyline.FromBox(0, 100, 0, 20, VerticalDirection.Up);  // top y_up=20
        var other = VerticalSkyline.FromBox(30, 70, 0, 30, VerticalDirection.Up);    // top y_up=30 (higher)

        skyline.Merge(other);

        // In overlap region [30,70], should keep y_up=30 (larger Y-up = topmost)
        double heightAt50 = skyline.Height(50);
        _output.WriteLine($"Height at x=50: {heightAt50}");

        Assert.Equal(30, heightAt50, Epsilon);

        // Outside overlap, should be 20
        Assert.Equal(20, skyline.Height(10), Epsilon);
        Assert.Equal(20, skyline.Height(90), Epsilon);
    }

    /// <summary>
    /// DOWN skyline keeps the bottommost point (smallest Y-up).
    /// </summary>
    [Fact]
    public void Merge_DifferentHeights_DownKeepsBottommostYUp()
    {
        // DOWN skyline: keeps the bottommost point (smallest Y-up)
        var skyline = VerticalSkyline.FromBox(0, 100, -50, -40, VerticalDirection.Down);  // bottom y_up=-50
        var other = VerticalSkyline.FromBox(30, 70, -70, -60, VerticalDirection.Down);    // bottom y_up=-70 (lower)

        skyline.Merge(other);

        // In overlap region [30,70], should keep y_up=-70 (smaller Y-up = bottommost)
        double heightAt50 = skyline.Height(50);
        _output.WriteLine($"Height at x=50: {heightAt50}");

        Assert.Equal(-70, heightAt50, Epsilon);

        // Outside overlap, should be -50
        Assert.Equal(-50, skyline.Height(10), Epsilon);
        Assert.Equal(-50, skyline.Height(90), Epsilon);
    }

    /// <summary>
    /// Distance between UP and DOWN skylines.
    /// </summary>
    [Fact]
    public void Distance_UpAndDown_ReturnsGap()
    {
        // UP skyline with its top edge at y_up=-20
        var up = VerticalSkyline.FromBox(0, 100, -40, -20, VerticalDirection.Up);

        // DOWN skyline with its bottom edge at y_up=-50 (below the UP skyline)
        var down = VerticalSkyline.FromBox(0, 100, -50, -40, VerticalDirection.Down);

        // Gap = -20 - (-50) = 30
        double distance = up.Distance(down);

        _output.WriteLine($"UP maxHeight: {up.MaxHeight()}");
        _output.WriteLine($"DOWN maxHeight: {down.MaxHeight()}");
        _output.WriteLine($"Distance: {distance}");

        Assert.Equal(30, distance, Epsilon);
    }

    /// <summary>
    /// Padded skyline extends buildings with 45° slopes.
    /// </summary>
    [Fact]
    public void Padded_ExtendsBuildingsWithSlopes()
    {
        // A single building at x=[20,80], top y_up=10 (UP skyline, internal height = +10)
        var skyline = VerticalSkyline.FromBox(20, 80, 0, 10, VerticalDirection.Up);

        double padding = 5.0;
        var padded = skyline.Padded(padding);

        // Original region should keep same height
        Assert.Equal(10, padded.Height(50), Epsilon);

        // Flat padding region: [20-P, 20] = [15, 20] and [80, 80+P] = [80, 85]
        // Should have same height as building edge
        Assert.Equal(10, padded.Height(17), Epsilon);  // Left flat padding
        Assert.Equal(10, padded.Height(82), Epsilon);  // Right flat padding

        // Sloped region: [20-2P, 20-P] = [10, 15] and [80+P, 80+2P] = [85, 90]
        // At outer tip (x=10 for left, x=90 for right), the Y-up height DROPS by P
        // For UP skyline: real height at tip = 10 - 5 = 5 (less protrusion, roof lowered)
        double leftTipHeight = padded.Height(10);
        double rightTipHeight = padded.Height(90);
        _output.WriteLine($"Left tip height: {leftTipHeight}, Right tip height: {rightTipHeight}");

        // The 45° slope lowers the roof by 1 per unit of X from the flat zone edge
        // (less "extreme" = worse for collision detection)
        Assert.True(leftTipHeight < 10, $"Left tip {leftTipHeight} should be < 10 (slope lowers the roof)");
        Assert.True(rightTipHeight < 10, $"Right tip {rightTipHeight} should be < 10 (slope lowers the roof)");
    }

    /// <summary>
    /// Distance with horizon_padding is larger than without.
    /// </summary>
    [Fact]
    public void Distance_WithHorizonPadding_IsLargerOrEqual()
    {
        // Two skylines that barely overlap in X
        var up = VerticalSkyline.FromBox(0, 50, 0, 20, VerticalDirection.Up);
        var down = VerticalSkyline.FromBox(45, 100, 50, 60, VerticalDirection.Down);

        double distNopad = up.Distance(down);
        double distPadded = up.Distance(down, 10.0);

        _output.WriteLine($"Distance without padding: {distNopad}");
        _output.WriteLine($"Distance with padding 10: {distPadded}");

        // Padded skyline covers more X range, so distance should be >= unpadded
        Assert.True(distPadded >= distNopad - Epsilon,
            $"Padded distance {distPadded} should be >= unpadded {distNopad}");
    }

    /// <summary>
    /// Distance with horizon_padding detects proximity for non-overlapping skylines.
    /// </summary>
    [Fact]
    public void Distance_HorizonPadding_DetectsNearbyNonOverlapping()
    {
        // Two skylines that DON'T overlap in X (gap of 5)
        var up = VerticalSkyline.FromBox(0, 40, 0, 20, VerticalDirection.Up);
        var down = VerticalSkyline.FromBox(45, 100, 50, 60, VerticalDirection.Down);

        double distNopad = up.Distance(down);
        double distPadded = up.Distance(down, 10.0);

        _output.WriteLine($"Distance without padding: {distNopad}");
        _output.WriteLine($"Distance with padding 10: {distPadded}");

        // Without padding, no overlap so distance = -inf
        Assert.Equal(double.NegativeInfinity, distNopad, Epsilon);

        // With padding 10, the padded UP skyline extends to x=40+2*10=60,
        // which overlaps with DOWN starting at x=45. Distance should be finite.
        Assert.True(!double.IsNegativeInfinity(distPadded),
            $"Padded distance should be finite, but got {distPadded}");
    }

    /// <summary>
    /// Zero horizon_padding returns same result as no-padding overload.
    /// </summary>
    [Fact]
    public void Distance_ZeroPadding_SameAsNoPadding()
    {
        var up = VerticalSkyline.FromBox(0, 100, 0, 20, VerticalDirection.Up);
        var down = VerticalSkyline.FromBox(0, 100, 50, 60, VerticalDirection.Down);

        double distNone = up.Distance(down);
        double distZero = up.Distance(down, 0.0);

        Assert.Equal(distNone, distZero, Epsilon);
    }

    /// <summary>
    /// Multiple merges build up correct skyline.
    /// </summary>
    [Fact]
    public void MultipleMerges_BuildsCorrectSkyline()
    {
        var skyline = new VerticalSkyline(VerticalDirection.Up);

        // Add three buildings at different positions and heights
        skyline.Merge(VerticalSkyline.FromBox(0, 40, 0, 10, VerticalDirection.Up));    // top y_up=10
        skyline.Merge(VerticalSkyline.FromBox(20, 60, 0, 30, VerticalDirection.Up));   // top y_up=30 (highest)
        skyline.Merge(VerticalSkyline.FromBox(50, 100, 0, 25, VerticalDirection.Up));  // top y_up=25

        // Check heights at various points (UP keeps the largest Y-up)
        Assert.Equal(10, skyline.Height(10), Epsilon);  // Only first
        Assert.Equal(30, skyline.Height(30), Epsilon);  // First and second overlap, keep 30
        Assert.Equal(30, skyline.Height(55), Epsilon);  // Second and third overlap, keep 30
        Assert.Equal(25, skyline.Height(80), Epsilon);  // Only third
    }

    /// <summary>
    /// Simplified Skyline: Distance with horizon_padding extends Y ranges.
    /// </summary>
    [Fact]
    public void SimplifiedSkyline_Distance_WithHorizonPadding()
    {
        // Right skyline: segment at Y=[10,20], X=30
        var right = Skyline.FromBox(10, 20, 0, 30, Skyline.Direction.Right);

        // Left skyline: segment at Y=[25,35], X=50 (no Y overlap with right)
        var left = Skyline.FromBox(25, 35, 50, 100, Skyline.Direction.Left);

        // Without padding: no Y overlap → PositiveInfinity
        double distNopad = right.Distance(left, 0.0);
        Assert.Equal(double.PositiveInfinity, distNopad);

        // With horizon_padding 6: Y ranges become [10-6,20+6]=[4,26] and [25-6,35+6]=[19,41]
        // Overlap: [19,26], distance = 50 - 30 = 20
        double distPadded = right.Distance(left, 6.0);
        Assert.Equal(20.0, distPadded, Epsilon);
    }
}
