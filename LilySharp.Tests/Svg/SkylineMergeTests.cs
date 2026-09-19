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

using System.Collections.Generic;
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

    // A test for the SIMPLIFIED Skyline's padded Distance stood here until session 315, when
    // the class it exercised was deleted — see the commit that removed
    // Svg/Layout/Skyline.cs. Nothing in the engine had called that class since the dot
    // column stopped, and its merge was not LilyPond's: it folded two overlapping boxes
    // into one spanning their union, which is the defect that commit repaired.

    /// <summary>
    /// A building that reaches ±∞ still reaches ±∞ after a FINITE one is merged over it —
    /// the invariant every floor in the engine stands on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/skyline.cc:259-282 empty_skyline / single_skyline — the two
    /// builders that pad every gap with a -infinity building, which is how LilyPond keeps
    /// the invariant its own file header states ("the start of the first building is at
    /// -infinity, the end of the last building is at infinity"). Lily# stores only the
    /// non-empty stretches, so the same invariant has to be kept by the merge's interval
    /// walk instead.
    /// </para>
    /// <para>
    /// ⚠️ THIS FAILED FOR AS LONG AS THE MERGE COLLECTED ONLY FINITE BOUNDARIES: the walk
    /// covered the finite hull and everything outside it came back EMPTY, so one note-column
    /// box deleted the staff floor either side of itself. The ledger reading that caught it
    /// is dynamic.page.quiet.last-staff-to-foot (-0.020774041 → -0.000075985): an `f` whose
    /// ink overhangs its notehead's advance binds OUTSIDE the column's box, on floor that had
    /// been punched out. The hairpin span that first exposed it (session 48) had to pass a
    /// bounded horizon to work around it; that workaround is gone with this.
    /// </para>
    /// </remarks>
    [Fact]
    public void Merge_KeepsAnUnboundedBuildingsTails()
    {
        // The staff floor: -2.05 over the whole horizon, as side-position's
        // set_minimum_height raises it (side-position-interface.cc:323-330).
        var floor = VerticalSkyline.FromBox(
            double.NegativeInfinity, double.PositiveInfinity, -2.05, -2.05,
            VerticalDirection.Down);
        // One note column's head, a finite box well above that floor.
        floor.Merge(VerticalSkyline.FromBox(0, 1.3, -0.545, 0.545, VerticalDirection.Down));

        // Inside the box and either side of it, the floor is the floor: the head is higher,
        // so it never wins on a DOWN skyline.
        Assert.Equal(-2.05, floor.Height(-50), Epsilon);
        Assert.Equal(-2.05, floor.Height(0.65), Epsilon);
        Assert.Equal(-2.05, floor.Height(50), Epsilon);

        // ...and a reading taken OUTSIDE the box still sees it. A 1-wide roof at Y-up 1.296
        // sitting to the right of the column is 3.346 above the floor, wherever it is.
        var mine = VerticalSkyline.FromBox(2.0, 3.0, 1.296, 1.296, VerticalDirection.Up);
        Assert.Equal(3.346, mine.Distance(floor), Epsilon);

        // TWO unbounded buildings and no finite boundary at all: the walk had nothing to
        // walk between and returned an EMPTY skyline. The outer one wins, everywhere.
        var deeper = VerticalSkyline.FromBox(
            double.NegativeInfinity, double.PositiveInfinity, -3.0, -3.0,
            VerticalDirection.Down);
        deeper.Merge(VerticalSkyline.FromBox(
            double.NegativeInfinity, double.PositiveInfinity, -2.05, -2.05,
            VerticalDirection.Down));
        Assert.False(deeper.IsEmpty);
        Assert.Equal(-3.0, deeper.Height(0), Epsilon);
        Assert.Equal(-3.0, deeper.Height(1000), Epsilon);
    }

    /// <summary>Twelve boxes, each overlapping the next, so ONE resolve walk merges eleven
    /// overlaps in a row — and the profile that comes out is the one merging them one at a
    /// time gives.</summary>
    /// <remarks>
    /// The resolve lends each overlap the same three scratch buffers for the whole walk
    /// (VerticalSkyline.ResolveScratch), which no earlier test could see: every one of them
    /// merges one or two overlaps, and a buffer is only observable once it is used TWICE.
    /// A stale leftover — a missing Clear, or two roles sharing one list — changes what the
    /// second overlap merges, and it does not throw: it deletes or invents silhouette.
    /// <para>
    /// The batched path and the one-at-a-time path are compared POINTWISE, not building by
    /// building, because only the batch coalesces colinear neighbours (EndBatch) — the two
    /// are the same profile written with different seams, and it is the profile that is the
    /// claim.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABatchedResolveEqualsMergingOneAtATime_AcrossElevenOverlapsInOneWalk()
    {
        var batched = new VerticalSkyline(VerticalDirection.Up);
        batched.BeginBatch();
        var oneAtATime = new VerticalSkyline(VerticalDirection.Up);
        foreach (var (left, right, top) in OverlappingBoxes())
        {
            batched.Merge(VerticalSkyline.FromBox(left, right, 0, top, VerticalDirection.Up));
            oneAtATime.Merge(VerticalSkyline.FromBox(left, right, 0, top, VerticalDirection.Up));
        }
        batched.EndBatch();

        for (double x = 0.11; x < 37.9; x += 0.37)
            Assert.Equal(oneAtATime.Height(x), batched.Height(x), Epsilon);
    }

    /// <summary>The same eleven-overlap walk read against the ARITHMETIC: at every x the
    /// profile is the tallest box covering it.</summary>
    /// <remarks>
    /// The companion to the test above, and not a duplicate of it: that one compares two
    /// code paths, and a corrupted scratch corrupts both alike. This one has no skyline in
    /// its oracle at all, so it fails even when the two paths agree with each other.
    /// </remarks>
    [Fact]
    public void AResolvedProfileIsTheTallestBoxCoveringEachPoint()
    {
        var sky = new VerticalSkyline(VerticalDirection.Up);
        sky.BeginBatch();
        foreach (var (left, right, top) in OverlappingBoxes())
            sky.Merge(VerticalSkyline.FromBox(left, right, 0, top, VerticalDirection.Up));
        sky.EndBatch();

        for (double x = 0.11; x < 37.9; x += 0.37)
        {
            double tallest = double.NegativeInfinity;
            foreach (var (left, right, top) in OverlappingBoxes())
                if (left <= x && x <= right)
                    tallest = System.Math.Max(tallest, top);
            Assert.Equal(tallest, sky.Height(x), Epsilon);
        }
    }

    /// <summary>One resolve walk allocates its scratch ONCE, not once per overlap.</summary>
    /// <remarks>
    /// The other two nets cannot see this, and that is why it is here. MEASURED (session 416,
    /// the owner's corpus, eight forward keystrokes a book): the walk entered the overlap
    /// merge 1,691,308 times and allocated three Lists on each of them — 398 B an overlap,
    /// 3.97% of a keystroke — and NONE of it was visible to the suite, because the profile
    /// that came out was right. Only <see cref="ResolveScratch"/>'s output buffer needs its
    /// Clear for correctness; the input buffers' Clears are idempotent under a max, so a
    /// poison in either one leaves every profile test green and only the cost moves. A gate
    /// on the cost is the only observer those two have.
    /// <para>
    /// One-sided and roomy, in the shape <c>KeystrokeFloorGateTests</c> uses: eleven overlaps
    /// at ~400 B of throwaway List apiece is ~4.4 kB the old walk spent and this one does
    /// not, so a ceiling well under that separates them while leaving the measured figure
    /// (printed on failure) free to drift with List's growth policy.
    /// </para>
    /// </remarks>
    [Fact]
    public void OneResolveWalkAllocatesItsScratchOnce_NotOncePerOverlap()
    {
        // The boxes are built OUTSIDE the measurement: a FromBox is a skyline of its own and
        // would price twelve constructions into a reading about one walk.
        var boxes = new List<VerticalSkyline>();
        foreach (var (left, right, top) in OverlappingBoxes())
            boxes.Add(VerticalSkyline.FromBox(left, right, 0, top, VerticalDirection.Up));

        long Walk()
        {
            var sky = new VerticalSkyline(VerticalDirection.Up);
            sky.BeginBatch();
            foreach (var box in boxes)
                sky.Merge(box);
            sky.EndBatch();
            return sky.Buildings.Count;
        }

        Walk();                       // JIT and first-touch, so the measured round is steady
        Walk();
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        Walk();
        long spent = System.GC.GetAllocatedBytesForCurrentThread() - before;

        // MEASURED 2,184 B on the dev machine today; the ceiling clears that by ~37% and
        // still sits 2.2x under the ~6,600 B the per-overlap walk spent.
        Assert.True(spent < 3000,
            $"one twelve-box resolve allocated {spent} B; eleven overlaps' worth of throwaway "
            + "Lists is about 4,400 B on top of the profile, so this is the scratch coming "
            + "back per overlap");
    }

    /// <summary>The buffer a resolve walk reads is lent by the thread, not by the walk — so
    /// the SECOND walk must not find the first one's buildings still in it.</summary>
    /// <remarks>
    /// The walk's input list moved out of the call and up to the thread in session 421 (the
    /// copy it used to allocate was 2.06% of a keystroke), and that put a Clear on the
    /// correctness path: a buffer handed over still holding the last skyline's buildings
    /// resolves them into this one — silhouette from grobs this skyline never saw. It is not
    /// the idempotent kind of stale that <see cref="OneResolveWalkAllocatesItsScratchOnce_NotOncePerOverlap"/>
    /// describes; it invents ink.
    /// <para>
    /// The two walks are deliberately far apart on the horizon and far apart in height, so a
    /// leak shows up as an answer rather than as a rounding: the tall one is 50 units high
    /// over [0, 150], the low one 1 and 2 units high over [0, 2].
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondResolveOnTheSameThread_DoesNotInheritTheFirstsBuildings()
    {
        var tall = new VerticalSkyline(VerticalDirection.Up);
        tall.BeginBatch();
        tall.Merge(VerticalSkyline.FromBox(0, 100, 0, 50, VerticalDirection.Up));
        tall.Merge(VerticalSkyline.FromBox(50, 150, 0, 40, VerticalDirection.Up));
        tall.EndBatch();
        Assert.Equal(50.0, tall.Height(10), Epsilon);

        // The next walk on this thread is handed the very buffer that one filled.
        var low = new VerticalSkyline(VerticalDirection.Up);
        low.BeginBatch();
        low.Merge(VerticalSkyline.FromBox(0, 1, 0, 1, VerticalDirection.Up));
        low.Merge(VerticalSkyline.FromBox(1, 2, 0, 2, VerticalDirection.Up));
        low.EndBatch();

        Assert.Equal(1.0, low.Height(0.5), Epsilon);
        Assert.Equal(2.0, low.Height(1.5), Epsilon);
        // And nothing at all out where only the first walk had ink.
        Assert.Equal(double.NegativeInfinity, low.Height(50));
    }

    /// <summary>The scratch a resolve walk cuts its overlaps with is lent by the thread too —
    /// so the SECOND walk must not find the first one's tail or segments still in it.</summary>
    /// <remarks>
    /// The three lists moved out of the call and up to the thread in session 429, which
    /// measured them as the WHOLE of what a walk still allocated (0.270% of a keystroke, and
    /// the split came to 100.0% of the walk). Before that, a fresh
    /// <c>ResolveScratch</c> per walk made this true by construction; now it is true because
    /// every one of the three is cleared by the code that fills it, and this is the gate that
    /// says so.
    /// <para>
    /// ⚠️ THE SECOND WALK HAS EXACTLY ONE OVERLAP, deliberately, and that is what makes this
    /// net the ONE the pooling needs. Of the three lists, only <c>Overlapping</c> goes stale
    /// in a way no existing net could see: within a walk the tail it carries over is this
    /// same skyline's own earlier buildings, so re-reading them cannot change a maximum — the
    /// idempotent stale again — while ACROSS walks it is another skyline's ink. With one
    /// overlap there is no within-walk reuse to hide behind. VERIFIED BY POISON, all four
    /// ways: dropping <c>overlapping.Clear()</c> turns this red (1,090 reads 60 instead of
    /// -∞) and leaves the pre-429 shape — a fresh scratch every walk — green.
    /// </para>
    /// <para>
    /// ⚠️ THE OTHER TWO CLEARS ARE PINNED ALREADY, and were before this change: dropping the
    /// <c>Clear</c> on <c>scratch.Merged</c> duplicates segments within one walk (this net's
    /// own first assertion goes red, pooled or not), and dropping
    /// <c>boundaryList.Clear()</c> turns <see cref="MergeSlope_LeavesWhatTheFromSlopePairLeaves"/>
    /// and <see cref="ABatchsResultList_IsSizedByWhatTheResolveKeeps_NotByWhatItWasHanded"/>
    /// red. That last one is the reason this remark says "verified" rather than "by
    /// construction": the first draft of it argued a stale boundary could not reach a page —
    /// an extra cut fuses straight back — and the poison said otherwise in four seconds.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondWalkOnTheSameThread_DoesNotInheritTheFirstsScratch()
    {
        // A walk with MANY overlaps, far off on the horizon and far up: it leaves the widest
        // tail and the most segments this thread's scratch will have held.
        var far = new VerticalSkyline(VerticalDirection.Up);
        far.BeginBatch();
        for (int i = 0; i < 12; i++)
            far.Merge(VerticalSkyline.FromBox(1000 + i * 5, 1000 + i * 5 + 40, 0, 50 + i,
                VerticalDirection.Up));
        far.EndBatch();
        Assert.Equal(61.0, far.Height(1090), Epsilon);

        // And now a walk with ONE overlap, close in and low.
        var near = new VerticalSkyline(VerticalDirection.Up);
        near.BeginBatch();
        near.Merge(VerticalSkyline.FromBox(0, 10, 0, 1, VerticalDirection.Up));
        near.Merge(VerticalSkyline.FromBox(5, 15, 0, 2, VerticalDirection.Up));
        near.EndBatch();

        Assert.Equal(1.0, near.Height(2.5), Epsilon);   // its own ink, left of the overlap
        Assert.Equal(2.0, near.Height(7.5), Epsilon);   // the overlap resolved to the higher
        Assert.Equal(2.0, near.Height(12.5), Epsilon);  // and right of it
        // Nothing at all out where only the first walk had ink.
        Assert.Equal(double.NegativeInfinity, near.Height(1090));
        Assert.Equal(double.NegativeInfinity, near.Height(1005));
    }

    /// <summary>The list <c>Padded</c> builds its padding buildings in is lent by the thread
    /// as well — so the SECOND padding must not find the first one's buildings still in
    /// it.</summary>
    /// <remarks>
    /// The padding list moved out of the call and up to the thread in session 430, together
    /// with the intermediate skyline that used to be built around it — 0.382% of a keystroke
    /// between them, of which two thirds was that intermediate's list climbing 4-8-16-… while
    /// being handed buildings that were already in a right-sized list.
    /// <para>
    /// ⚠️ THIS IS THE INVENTING KIND OF STALE, like the merge input's and unlike the scratch's:
    /// the whole padding list is merged into the answer, so buildings left over from the
    /// previous padding raise this skyline's silhouette with another grob's.
    /// </para>
    /// <para>
    /// ⚠️ WHAT THE POISON SAID, which is not what the first draft of this remark predicted.
    /// Dropping the <c>Clear</c> in <c>RentPadding</c> turns this red — the near skyline's
    /// padded profile reads 61 out at 1,090, where it has no ink at all — and it also turns
    /// <see cref="Distance_BetweenFacingSystems_IsTheirInkAndNoMore"/> red. So the claim this
    /// remark was about to make, that nothing here could see the leak, was wrong. What is
    /// true is narrower and worth the distinction: run ALONE, that one is GREEN under the same
    /// poison. It pads twice and catches the leak only when some earlier test in the class has
    /// already padded on this thread — it is a victim of the pollution, not an observer of it,
    /// and which tests are victims depends on the order they run in. This one pads twice
    /// itself, so it says the same thing whatever else ran.
    /// </para>
    /// <para>
    /// The two paddings are deliberately far apart on the horizon: the first is twelve
    /// overlapping boxes around x = 1,000 and some 50 units up, the second one box over
    /// [0, 10] one unit up. The padding is 2, so the second's own answer is flat at 1 from -2
    /// to 12 and slopes to nothing by 14 — everything asserted here is inside that, except the
    /// last two lines, which are where only the FIRST padding had buildings.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondPaddingOnTheSameThread_DoesNotInheritTheFirstsBuildings()
    {
        var far = new VerticalSkyline(VerticalDirection.Up);
        far.BeginBatch();
        for (int i = 0; i < 12; i++)
            far.Merge(VerticalSkyline.FromBox(1000 + i * 5, 1000 + i * 5 + 40, 0, 50 + i,
                VerticalDirection.Up));
        far.EndBatch();
        var farPadded = far.Padded(2.0);
        Assert.Equal(61.0, farPadded.Height(1090), Epsilon);

        // The next padding on this thread is handed the very buffer that one filled.
        var near = new VerticalSkyline(VerticalDirection.Up);
        near.Merge(VerticalSkyline.FromBox(0, 10, 0, 1, VerticalDirection.Up));
        var nearPadded = near.Padded(2.0);

        Assert.Equal(1.0, nearPadded.Height(5), Epsilon);    // its own ink
        Assert.Equal(1.0, nearPadded.Height(-1), Epsilon);   // the flat pad on the left
        Assert.Equal(1.0, nearPadded.Height(11), Epsilon);   // and on the right
        Assert.Equal(0.0, nearPadded.Height(13), Epsilon);   // the 45° slope, halfway down
        // And nothing at all out where only the first padding had buildings.
        Assert.Equal(double.NegativeInfinity, nearPadded.Height(1090));
        Assert.Equal(double.NegativeInfinity, nearPadded.Height(1005));
    }

    /// <summary>A cached profile merged into an ALREADY-RESOLVED skyline keeps both
    /// silhouettes: this skyline's own ink, and the profile's at the offset it was placed
    /// at.</summary>
    /// <remarks>
    /// ⚠️ THIS ARM HAD NO OBSERVER AT ALL until session 421 wrote this. <c>Merge(resolved,
    /// dx, dy)</c> has two arms — the batch one, which appends and defers, and this one, for
    /// a skyline that is already resolved — and the product reaches this one 2,380 times a
    /// corpus keystroke sweep (measured), yet a poison that dropped this skyline's own
    /// buildings from the walk left all 8,739 tests green. The batch arm is what the lyric
    /// and system builders use, and it is covered many times over; this one is the pedal and
    /// annotation path, and it was covered by nothing.
    /// </remarks>
    [Fact]
    public void AResolvedProfileMergedIntoAResolvedSkyline_KeepsBothSilhouettes()
    {
        var sky = new VerticalSkyline(VerticalDirection.Up);
        sky.Merge(VerticalSkyline.FromBox(0, 10, 0, 3, VerticalDirection.Up));
        Assert.Equal(3.0, sky.Height(5), Epsilon);

        // A resolved profile of its own, placed twenty units along the horizon and two up.
        var profile = VerticalSkyline.FromBox(0, 4, 0, 7, VerticalDirection.Up).Buildings;
        sky.Merge(profile, 20.0, 2.0);

        Assert.Equal(3.0, sky.Height(5), Epsilon);    // its own ink survived the merge
        Assert.Equal(9.0, sky.Height(22), Epsilon);   // the profile arrived, shifted AND raised
    }

    /// <summary>A merge into a large skyline does not copy that skyline to read it.</summary>
    /// <remarks>
    /// The companion gate to <see cref="OneResolveWalkAllocatesItsScratchOnce_NotOncePerOverlap"/>,
    /// one level out: that one pins the scratch INSIDE a walk, this one pins the walk's INPUT.
    /// MEASURED (session 421, Release, the owner's corpus, eight forward keystrokes a book):
    /// the input list was rebuilt on every one of 73,254 merges and 10,284 batch ends —
    /// 320,748,776 B, 2.06% of a keystroke — and the profile that came out was right every
    /// time, so nothing in the suite could see it. A gate on the cost is the only observer it
    /// has, exactly as session 416 found for the scratch.
    /// <para>
    /// One-sided and roomy: three hundred buildings copied twice (the exact-size copy, then
    /// the regrowth behind AddRange) is about 29 kB a merge, so a ceiling an order of
    /// magnitude under that separates a rented buffer from a rebuilt one while leaving the
    /// measured figure free to drift with List's growth policy.
    /// </para>
    /// </remarks>
    [Fact]
    public void AMergeIntoALargeSkyline_DoesNotCopyItToReadIt()
    {
        var sky = new VerticalSkyline(VerticalDirection.Up);
        sky.BeginBatch();
        for (int i = 0; i < 300; i++)
            sky.Merge(VerticalSkyline.FromBox(2 * i, 2 * i + 1, 0, 1 + i % 7, VerticalDirection.Up));
        sky.EndBatch();
        Assert.True(sky.Buildings.Count >= 300,
            $"the fixture resolved to {sky.Buildings.Count} buildings; this gate needs a big one");

        // Built outside the measurement, and merged to warm: the first merge after a
        // 300-building batch is the one that grows the thread's buffer to fit.
        // ⚠️ THREE OF THEM, NOT ONE, SINCE SESSION 426 — and the reason is this fixture rather
        // than this gate. A batch now reserves its result list at the batch's own count
        // (EndBatch), which in the product leaves room to spare because the resolve keeps far
        // fewer buildings than the seeds append; here the boxes are DISJOINT, so it keeps one
        // per box and the reservation lands exactly on the count. The next merges then grow the
        // list once, and the measured round has to be past that — the steady state is what the
        // ceiling below is about. Distinct x each time, so every warm merge really does add a
        // building rather than resolving to the same profile.
        var measured = VerticalSkyline.FromBox(3.5, 4.5, 0, 9, VerticalDirection.Up);
        foreach (double x in new[] { 1.5, 701.5, 703.5 })
            sky.Merge(VerticalSkyline.FromBox(x, x + 1.0, 0, 9, VerticalDirection.Up));

        long before = System.GC.GetAllocatedBytesForCurrentThread();
        sky.Merge(measured);
        long spent = System.GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(spent < 3000,
            $"one merge into a {sky.Buildings.Count}-building skyline allocated {spent} B; "
            + "rebuilding the walk's input would be about 29,000 B, so this is the skyline "
            + "being copied to be read");
    }

    /// <summary>Box i spans [3i, 3i+5] — two units of overlap with its neighbour — at a
    /// height that rises and falls, so the winner changes hands along the horizon rather
    /// than one box shadowing the rest.</summary>
    private static IEnumerable<(double Left, double Right, double Top)> OverlappingBoxes()
    {
        for (int i = 0; i < 12; i++)
            yield return (3.0 * i, 3.0 * i + 5.0, 1.0 + (i * 7) % 5);
    }

    /// <summary>
    /// <c>MergeBox</c> leaves the SAME BUILDINGS, IN THE SAME ORDER, as building a
    /// <c>FromBox</c> skyline and merging it — batched and unbatched, up and down.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE ONLY THING THAT KEEPS THEM ONE QUANTITY. There are now two roads to a
    /// seeded box: the pair, which every caller outside this repository's hot paths still
    /// takes, and the direct append, which the seeds take. They agree today because the
    /// direct one re-reads the pair's own two lines — the sign convention and the ±inf
    /// filter — and nothing in the compiler says they have to keep agreeing.
    /// <para>
    /// ⚠️ WHAT IT CATCHES, MEASURED BY POISON (HANDOFF §5.4), IS THE SIGN CONVENTION AND NOT
    /// THE FILTER. Swapping the edge the direction picks turns the two BATCHED cases red (the
    /// unbatched ones go through <c>FromBox</c> itself, so they cannot). Deleting the ±inf
    /// filter turns NOTHING red — and that is a fact about the filter, not a hole here: the
    /// resolve drops those buildings anyway, which is what <c>Merge</c>'s own remark says, so
    /// its absence cannot reach a page. It is kept because it keeps the batch small.
    /// </para>
    /// HANDOFF §7.7 — the same quantity spelt twice, guarded rather than trusted.
    /// </remarks>
    [Theory]
    [InlineData(VerticalDirection.Up, true)]
    [InlineData(VerticalDirection.Up, false)]
    [InlineData(VerticalDirection.Down, true)]
    [InlineData(VerticalDirection.Down, false)]
    public void MergeBox_LeavesWhatTheFromBoxPairLeaves(VerticalDirection dir, bool batched)
    {
        var pair = new VerticalSkyline(dir);
        var direct = new VerticalSkyline(dir);
        if (batched)
        {
            pair.BeginBatch();
            direct.BeginBatch();
        }
        foreach (var (left, right, top) in OverlappingBoxes())
        {
            pair.Merge(VerticalSkyline.FromBox(left, right, top - 2.0, top, dir));
            direct.MergeBox(left, right, top - 2.0, top);
        }
        // ...and a box whose own edge is -infinity, which is what makes the ±inf FILTER
        // load-bearing rather than incidental: a batch drops such a building on both roads,
        // an unbatched merge keeps all three. Nothing in OverlappingBoxes reaches it.
        double empty = dir == VerticalDirection.Up
            ? double.NegativeInfinity : double.PositiveInfinity;
        pair.Merge(VerticalSkyline.FromBox(1.0, 4.0, empty, empty, dir));
        direct.MergeBox(1.0, 4.0, empty, empty);
        if (batched)
        {
            pair.EndBatch();
            direct.EndBatch();
        }

        Assert.Equal(pair.Buildings.Count, direct.Buildings.Count);
        for (int i = 0; i < pair.Buildings.Count; i++)
        {
            var (w, g) = (pair.Buildings[i], direct.Buildings[i]);
            Assert.Equal(w.Start, g.Start);
            Assert.Equal(w.End, g.End);
            Assert.Equal(w.Slope, g.Slope);
            Assert.Equal(w.Intercept, g.Intercept);
        }
    }

    /// <summary>
    /// <c>MergeSlope</c> is <c>MergeBox</c>'s twin and carries the same obligation — see
    /// that test's remark. A slope carries no padders, so what a poisoned sign would change
    /// here is the building itself.
    /// </summary>
    [Theory]
    [InlineData(VerticalDirection.Up, true)]
    [InlineData(VerticalDirection.Up, false)]
    [InlineData(VerticalDirection.Down, true)]
    [InlineData(VerticalDirection.Down, false)]
    public void MergeSlope_LeavesWhatTheFromSlopePairLeaves(VerticalDirection dir, bool batched)
    {
        var pair = new VerticalSkyline(dir);
        var direct = new VerticalSkyline(dir);
        if (batched)
        {
            pair.BeginBatch();
            direct.BeginBatch();
        }
        foreach (var (left, right, top) in OverlappingBoxes())
        {
            pair.Merge(VerticalSkyline.FromSlope(left, top, right, top - 1.0, 0.25, dir));
            direct.MergeSlope(left, top, right, top - 1.0, 0.25);
        }
        if (batched)
        {
            pair.EndBatch();
            direct.EndBatch();
        }

        Assert.Equal(pair.Buildings.Count, direct.Buildings.Count);
        for (int i = 0; i < pair.Buildings.Count; i++)
        {
            var (w, g) = (pair.Buildings[i], direct.Buildings[i]);
            Assert.Equal(w.Start, g.Start);
            Assert.Equal(w.End, g.End);
            Assert.Equal(w.Slope, g.Slope);
            Assert.Equal(w.Intercept, g.Intercept);
        }
    }

    /// <summary>
    /// A batch OPENED ON A SKYLINE THAT ALREADY HAS INK resolves the ink that was there
    /// together with what the batch appends.
    /// </summary>
    /// <remarks>
    /// ⚠️ NO PRODUCT CALLER DOES THIS TODAY — all six <c>BeginBatch</c> sites batch a skyline
    /// they have just made — and that is exactly why the arm needs an observer of its own: the
    /// batch accumulates into a buffer borrowed from the thread (session 426), so what used to
    /// be "append beside the buildings already in the list" is now "start the buffer as those
    /// buildings". A poison that drops the seeding leaves every other test green, because every
    /// other test's batch starts empty; here the low ink at x=5 disappears.
    /// <para>
    /// AND IN THAT ORDER, which is not decoration: the resolve sorts by Start with
    /// <c>List.Sort</c>, which is NOT stable, so where two buildings share a Start their input
    /// order is part of the answer. Old-then-new is the order the appends made when the batch
    /// was the list itself.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABatchOpenedOnANonEmptySkyline_ResolvesTheInkThatWasAlreadyThere()
    {
        var sky = new VerticalSkyline(VerticalDirection.Up);
        sky.MergeBox(0, 10, 0, 3);            // unbatched: resolved straight away
        Assert.Equal(3.0, sky.Height(5), Epsilon);

        sky.BeginBatch();
        sky.MergeBox(20, 30, 0, 7);
        sky.EndBatch();

        Assert.Equal(3.0, sky.Height(5), Epsilon);    // the ink that was here
        Assert.Equal(7.0, sky.Height(25), Epsilon);   // and the ink the batch brought
    }

    /// <summary>
    /// TWO BATCHES OPEN AT ONCE — a system's up and down skylines are batched together —
    /// keep their buildings apart.
    /// </summary>
    /// <remarks>
    /// The batch buffers come from one pool on the thread (session 426), so this is the net on
    /// the pool handing out a DIFFERENT list to each open batch: a single shared slot would let
    /// the second <c>BeginBatch</c> take the list the first is still filling, and both profiles
    /// would come out as the union of the two. Both directions are Up here on purpose — a
    /// direction mismatch would throw, which is a different failure and would hide this one.
    /// </remarks>
    [Fact]
    public void TwoBatchesOpenAtOnce_DoNotShareABuffer()
    {
        var first = new VerticalSkyline(VerticalDirection.Up);
        var second = new VerticalSkyline(VerticalDirection.Up);
        first.BeginBatch();
        second.BeginBatch();
        first.MergeBox(0, 10, 0, 3);
        second.MergeBox(100, 110, 0, 9);
        first.MergeBox(5, 15, 0, 4);
        second.MergeBox(105, 115, 0, 8);
        first.EndBatch();
        second.EndBatch();

        Assert.Equal(4.0, first.Height(12), Epsilon);
        Assert.Equal(double.NegativeInfinity, first.Height(105));
        Assert.Equal(9.0, second.Height(102), Epsilon);
        Assert.Equal(double.NegativeInfinity, second.Height(12));
    }

    /// <summary>A batch of ONE building leaves that building in the skyline.</summary>
    /// <remarks>
    /// The arm the resolve skips: one building is already resolved, so <c>EndBatch</c> moves it
    /// out of the buffer rather than sorting it. Without that move the buffer goes back to the
    /// pool with the only ink the skyline had in it. A staff with a single seeded box is the
    /// product shape (a one-note system's edge), and it would go silently empty.
    /// </remarks>
    [Fact]
    public void ABatchOfOneBuilding_LeavesItInTheSkyline()
    {
        var sky = new VerticalSkyline(VerticalDirection.Up);
        sky.BeginBatch();
        sky.MergeBox(0, 10, 0, 3);
        sky.EndBatch();

        Assert.False(sky.IsEmpty);
        Assert.Equal(3.0, sky.Height(5), Epsilon);
    }

    /// <summary>A batch does not GROW an array: the buffer it appends into already has the
    /// capacity the last batch on this thread reached.</summary>
    /// <remarks>
    /// The only observer of what session 426 was for, and the shape
    /// <see cref="OneResolveWalkAllocatesItsScratchOnce_NotOncePerOverlap"/> already uses: the
    /// profile is identical either way, so nothing but a cost gate can see the difference.
    /// MEASURED (the owner's corpus, 231 books × eight forward keystrokes, allocated bytes,
    /// counted per call site): the four batches <c>SkylineBuilder</c> opens appended 897,518
    /// buildings a sweep into lists that started EMPTY, and List's doubling allocated 2.90
    /// slots of array for every building delivered — 83,353,208 B, 0.556% of a keystroke,
    /// against ZERO for the one site that counted its appends and reserved for them
    /// (<c>LyricEngraver</c>, session 224). With the buffer pooled the same sweep grew an array
    /// ONCE, for the thread's first batch.
    /// <para>
    /// What a warm batch may still allocate is ONE array for the resolve's output, reserved at
    /// the batch's own count (<c>EndBatch</c>) — 240 × 32 B ≈ 7.7 kB here. Growing to the same
    /// place by rungs costs about twice that, and it is measured: this gate read 16,496 B while
    /// the reservation was missing. The ceiling sits between the two, one-sided, and leaves
    /// List's growth policy free to drift.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABatchAppendsIntoAPooledBuffer_NotIntoAGrowingOne()
    {
        long Batch()
        {
            var sky = new VerticalSkyline(VerticalDirection.Up);
            sky.BeginBatch();
            for (int i = 0; i < 240; i++)
                sky.MergeBox(i, i + 1, 0, 1.0 + (i % 7));
            sky.EndBatch();
            return sky.Buildings.Count;
        }

        Batch();                      // the thread's first batch pays for the buffer
        Batch();                      // JIT and first-touch, so the measured round is steady
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        Batch();
        long spent = System.GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(spent < 11000,
            $"a warm 240-box batch allocated {spent} B; one reserved array for 240 buildings is "
            + "about 7,700 B and arriving at the same capacity by doubling costs about twice "
            + "that (measured 16,496 B), so this is a list being grown per batch instead of "
            + "lent by the thread and reserved once");
    }

    /// <summary>A batch's result list is sized by what the resolve KEEPS, not by what the
    /// batch was handed.</summary>
    /// <remarks>
    /// The other half of <see cref="ABatchAppendsIntoAPooledBuffer_NotIntoAGrowingOne"/>, and
    /// it needs its own fixture because the two cannot both be seen in one. That gate's boxes
    /// are DISJOINT, so the resolve keeps one building per box and the count it was handed IS
    /// the answer — reserving the bound and sizing exactly are the same array there, and only
    /// a rung climb stands out. Here every box covers the same span, so 600 appends resolve to
    /// a single building, and the two policies are 19,224 B apart.
    /// <para>
    /// ⚠️ WHY IT MATTERS IN THE PRODUCT, where the boxes are neither all disjoint nor all
    /// stacked: MEASURED (session 427, the owner's corpus, 231 books × eight forward
    /// keystrokes, counted by construction) the resolve keeps <b>0.363</b> of what the batch
    /// appends — 486.5 buildings in a keystroke, 176.8 out. Session 426 reserved
    /// <c>batch.Count</c> because the count the walk comes to is not known until it is over;
    /// the walk now writes to a buffer the thread lends and copies that count out, so the
    /// array is exactly R. Keystroke allocation over that corpus fell 0.134%, which is the
    /// whole of what this gate is about.
    /// </para>
    /// <para>
    /// ⚠️ AND THE POLICY IS NOT "exact-size everywhere" — see <c>sizeResultExactly</c>'s
    /// remark. A first version of the change sized every resolve exactly and
    /// <see cref="AMergeIntoALargeSkyline_DoesNotCopyItToReadIt"/> went red, because a skyline
    /// that is merged into again has something to amortise and an array sized at R has to be
    /// replaced by the next merge that adds a building.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABatchsResultList_IsSizedByWhatTheResolveKeeps_NotByWhatItWasHanded()
    {
        long Batch()
        {
            var sky = new VerticalSkyline(VerticalDirection.Up);
            sky.BeginBatch();
            for (int i = 0; i < 600; i++)
                sky.MergeBox(0, 100, 0, 1.0 + (i % 7));   // every box over the SAME span
            sky.EndBatch();
            return sky.Buildings.Count;
        }

        Assert.True(Batch() <= 4,
            "the fixture is meant to collapse 600 appends to a handful of buildings; if it "
            + "stopped doing that, this gate is measuring the disjoint case and cannot see "
            + "the difference it exists for");
        Batch();                      // JIT and first-touch, so the measured round is steady
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        Batch();
        long spent = System.GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(spent < 3000,
            $"a warm 600-box batch that resolves to a handful of buildings allocated {spent} B; "
            + "an array reserved at the batch's own count would be 24 + 32 × 600 = 19,224 B, so "
            + "this is the result list being sized by the appends instead of by the answer");
    }

    /// <summary>A skyline's FIRST merge gets its list in one allocation, at the size of the
    /// answer, instead of climbing to that size a rung at a time.</summary>
    /// <remarks>
    /// The other half of <see cref="ABatchsResultList_IsSizedByWhatTheResolveKeeps_NotByWhatItWasHanded"/>,
    /// and the half nobody had priced: that gate watches a skyline being BUILT, this one watches
    /// one being MERGED INTO for the first time. Until session 428 the merge walk rebuilt
    /// straight into <c>_buildings</c>, which a <c>Clear</c> leaves at whatever capacity it had
    /// — nothing at all, the first time — so the walk climbed 4-8-16-…-R and allocated about
    /// twice R getting there.
    /// <para>
    /// ⚠️ WHY IT MATTERS IN THE PRODUCT: MEASURED (session 428, the owner's corpus, 231 books ×
    /// eight forward keystrokes, counted by construction) the non-batch walks climbed
    /// 29,946,160 B a sweep, <b>0.201% of a keystroke</b>, and <b>92.4% of it was each
    /// skyline's FIRST merge</b> — 12,769 walks of 14,061. The walk now writes to the buffer
    /// the thread lends, as the batch arm has since session 427, and asks
    /// <c>EnsureCapacity(R)</c> for the answer.
    /// </para>
    /// <para>
    /// ⚠️ AND IT IS NOT THE SAME POLICY AS THE BATCH ARM'S, which is why both gates are here.
    /// <c>Capacity = R</c> fits the array to a skyline that is finished;
    /// <see cref="AMergeIntoALargeSkyline_DoesNotCopyItToReadIt"/> is red under that rule
    /// because a skyline that is merged into again must keep its headroom, and the simulation
    /// agrees — exact-sizing every walk would have cost <b>0.372%</b> of a keystroke against
    /// the 0.192% of doing nothing, all of it in the 17,474 walks that pay nothing today.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFirstMergeIntoASkyline_DoesNotClimbToTheSizeOfTheAnswer()
    {
        // Three hundred disjoint boxes: the resolve keeps one building each, so the answer is
        // large and its size is known. Built once, outside every measurement, and merged FROM
        // rather than into, so the measured round allocates nothing on its behalf.
        var wide = new VerticalSkyline(VerticalDirection.Up);
        wide.BeginBatch();
        for (int i = 0; i < 300; i++)
            wide.MergeBox(10 + 2 * i, 11 + 2 * i, 0, 1.0 + (i % 7));
        wide.EndBatch();
        Assert.True(wide.Buildings.Count >= 300,
            $"the fixture resolved to {wide.Buildings.Count} buildings; this gate needs a "
            + "profile whose size is worth climbing to");

        // The thread's two lent buffers — the walk's input and its result — grow to fit on
        // first use, and that growth is a per-thread cost, not a per-merge one. Pay it here.
        var warm = VerticalSkyline.FromBox(0, 1, 0, 2, VerticalDirection.Up);
        warm.Merge(wide);

        var cold = VerticalSkyline.FromBox(0, 1, 0, 2, VerticalDirection.Up);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        cold.Merge(wide);
        long spent = System.GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(warm.Buildings.Count, cold.Buildings.Count);
        Assert.True(spent < 15000,
            $"a first merge that resolves to {cold.Buildings.Count} buildings allocated "
            + $"{spent} B; one array for that answer is about 24 + 32 × 300 = 9,624 B, while "
            + "climbing to it from a list that starts empty allocates 8 + 16 + … + 512 slots "
            + "= 32,680 B — so this is the walk growing the skyline's list a rung at a time");
    }
}
