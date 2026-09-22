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

namespace LilySharp.Tests;

/// <summary>
/// HorizontalSkyline keeps a building LIST (Merge concatenates); these tests
/// pin that all queries still return ENVELOPE values — a shadowed building
/// must never influence Distance or X. LILYPOND-REF: lily/skyline.cc
/// internal_distance / height semantics.
/// </summary>
[Trait("Category", "Unit")]
public class HorizontalSkylineEnvelopeTests
{
    [Fact]
    public void Distance_IsExactForBoxes()
    {
        // prev item: right edge at x=2 over y∈[-2,2]
        var right = HorizontalSkyline.FromBox(-2, 2, 0, 2, HorizontalDirection.Right);
        // next item: left edge at x=-1 (extends 1 left of its reference) over y∈[-1,1]
        var left = HorizontalSkyline.FromBox(-1, 1, -1, 3, HorizontalDirection.Left);

        // Required separation = rightExtent(2) - leftExtent(-1) = 3
        Assert.Equal(3.0, right.Distance(left), 6);
    }

    /// <summary>
    /// The column views are built padded in one step (session 490); the answer must be the
    /// two-step one — the same buildings in the same order, at every padding including none —
    /// because every later Merge and Distance reads the list as it stands.
    /// </summary>
    [Theory]
    [InlineData(HorizontalDirection.Right, 0.08)]
    [InlineData(HorizontalDirection.Left, 0.15)]
    [InlineData(HorizontalDirection.Right, 0.0)]
    public void FromBoxesPadded_IsFromBoxesThenPaddedCopy(HorizontalDirection direction, double padding)
    {
        var boxes = new[]
        {
            (-2.0, 2.0, -0.5, 1.3),
            (-0.25, 0.75, -1.2, 0.4),
            (1.5, 3.5, 0.1, 0.9),
        };

        var twoStep = HorizontalSkyline.FromBoxes(boxes, direction).PaddedCopy(padding);
        var oneStep = HorizontalSkyline.FromBoxesPadded(boxes, direction, padding);

        Assert.Equal(twoStep.Buildings, oneStep.Buildings);
    }

    /// <summary>
    /// The accidental placement's one-step spellings (session 491) answer the in-place
    /// sequence they replaced building for building: ShiftedRaisedOver is Clone, Shift, Raise,
    /// Merge; ShiftedScratch is Clone, Shift.
    /// </summary>
    [Fact]
    public void ShiftedRaisedOver_AndShiftedScratch_AreTheInPlaceSteps()
    {
        var glyph = HorizontalSkyline.FromBoxes(new[]
        {
            (-0.75, 0.5, -0.3, 0.6),
            (0.2, 1.25, -0.1, 0.9),
        }, HorizontalDirection.Left);
        var under = HorizontalSkyline.FromBoxes(new[] { (-1.0, 1.0, 0.0, 1.2) }, HorizontalDirection.Left);

        var inPlace = glyph.Clone();
        inPlace.Shift(1.5);
        inPlace.Raise(-0.85);
        inPlace.Merge(under);
        Assert.Equal(inPlace.Buildings,
            HorizontalSkyline.ShiftedRaisedOver(glyph, 1.5, -0.85, under).Buildings);

        var shifted = glyph.Clone();
        shifted.Shift(-2.5);
        Assert.Equal(shifted.Buildings, HorizontalSkyline.ShiftedScratch(glyph, -2.5).Buildings);
    }

    /// <summary>
    /// The spellings that write into a skyline the caller keeps (session 498 — the accidental
    /// placement's running reference, the line start's two skylines) REPLACE what it held: a
    /// kept skyline that still carries an old building and a pending padding answers as a
    /// fresh one would.
    /// </summary>
    [Fact]
    public void TheIntoSpellings_ReplaceWhatTheKeptSkylineHeld()
    {
        var glyph = HorizontalSkyline.FromBoxes(new[]
        {
            (-0.75, 0.5, -0.3, 0.6),
            (0.2, 1.25, -0.1, 0.9),
        }, HorizontalDirection.Left);
        var under = HorizontalSkyline.FromBoxes(new[] { (-1.0, 1.0, 0.0, 1.2) }, HorizontalDirection.Left);
        HorizontalSkyline Stale() => HorizontalSkyline.FromBoxesPadded(
            new[] { (-3.0, 3.0, -5.0, 5.0) }, HorizontalDirection.Left, 0.2);

        Assert.Equal(HorizontalSkyline.ShiftedRaisedOver(glyph, 1.5, -0.85, under).Buildings,
            HorizontalSkyline.ShiftedRaisedOverInto(Stale(), glyph, 1.5, -0.85, under).Buildings);

        var boxes = new[] { (-1.0, 0.5, 0.0, 1.0), (0.25, 2.0, -0.5, 0.75) };
        Assert.Equal(HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Left).Buildings,
            HorizontalSkyline.FromBoxesInto(Stale(), boxes).Buildings);

        var cleared = Stale();
        cleared.Clear();
        Assert.True(cleared.IsEmpty);
        Assert.Empty(cleared.Buildings);

        // Writing into the skyline being read would clear it before it is read.
        Assert.Throws<ArgumentException>(
            () => HorizontalSkyline.ShiftedRaisedOverInto(under, glyph, 1.5, -0.85, under));
    }

    /// <summary>
    /// A skyline built padded keeps its padding PENDING (session 497): every read must see the
    /// eager padded list, every write must pad first (a pad of a raised building is not the
    /// raised pad, bit for bit), and a pending skyline merged into another must bring its pads.
    /// The eager copy — FromBoxes then PaddedCopy — is the reference throughout.
    /// </summary>
    [Fact]
    public void APendingPadding_ReadsWritesAndMergesAsTheEagerCopy()
    {
        var boxes = new[]
        {
            (-2.0, 2.0, -0.5, 1.3),
            (-0.25, 0.75, -1.2, 0.4),
            (1.5, 3.5, 0.1, 0.9),
        };
        const double pad = 0.15;
        HorizontalSkyline Eager() => HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Right).PaddedCopy(pad);
        HorizontalSkyline Pending() => HorizontalSkyline.FromBoxesPadded(boxes, HorizontalDirection.Right, pad);
        var facing = HorizontalSkyline.FromBoxes(new[] { (-1.0, 3.8, 1.1, 2.0) }, HorizontalDirection.Left);

        // Reads, the padding still pending: at a y only a pad covers, and a distance.
        foreach (double y in new[] { -2.1, 0.0, 3.6, 3.75 })
            Assert.Equal(Eager().X(y), Pending().X(y));
        Assert.Equal(Eager().MaxHeight(), Pending().MaxHeight());
        Assert.Equal(Eager().Distance(facing), Pending().Distance(facing));
        Assert.Equal(Eager().Distance(facing, 0.1), Pending().Distance(facing, 0.1));
        Assert.Equal(facing.Distance(Eager(), 0.1), facing.Distance(Pending(), 0.1));

        // Writes: raise and shift a pending skyline, and a clone of one.
        var eager = Eager();
        eager.Raise(0.37);
        eager.Shift(-1.13);
        var pending = Pending();
        pending.Raise(0.37);
        pending.Shift(-1.13);
        Assert.Equal(eager.Buildings, pending.Buildings);
        var clone = Pending().Clone();
        clone.Raise(0.37);
        clone.Shift(-1.13);
        Assert.Equal(eager.Buildings, clone.Buildings);

        // A pending skyline merged INTO another brings its pads, in the eager order.
        var intoEager = HorizontalSkyline.FromBoxes(new[] { (0.0, 1.0, 0.0, 0.5) }, HorizontalDirection.Right);
        intoEager.Merge(Eager());
        var intoPending = HorizontalSkyline.FromBoxes(new[] { (0.0, 1.0, 0.0, 0.5) }, HorizontalDirection.Right);
        intoPending.Merge(Pending());
        Assert.Equal(intoEager.Buildings, intoPending.Buildings);
    }

    [Fact]
    public void Distance_IgnoresShadowedBuilding()
    {
        var right = HorizontalSkyline.FromBoxes(new[]
        {
            (-2.0, 2.0, 0.0, 2.0),   // envelope: right edge x=2
            (-1.0, 1.0, 0.0, 0.5),   // fully shadowed (smaller extent, inside y-range)
        }, HorizontalDirection.Right);
        var left = HorizontalSkyline.FromBox(-2, 2, 0, 1, HorizontalDirection.Left);

        var rightNoShadow = HorizontalSkyline.FromBox(-2, 2, 0, 2, HorizontalDirection.Right);

        Assert.Equal(rightNoShadow.Distance(left), right.Distance(left), 6);
    }

    [Fact]
    public void Distance_SlopedBuilding_MaxAtOverlapEndpoint()
    {
        // Sloped right edge: x=0 at y=-2 rising to x=4 at y=2 (e.g. a beam).
        var right = HorizontalSkyline.FromSlope(-2, 0, 2, 4, HorizontalDirection.Right);
        var left = HorizontalSkyline.FromBox(0, 2, 0, 1, HorizontalDirection.Left);

        // Overlap y∈[0,2]; sum is linear, max at y=2: x=4, left extent 0 → 4.
        Assert.Equal(4.0, right.Distance(left), 6);
    }

    [Fact]
    public void X_ReturnsOutermostCoveringBuilding()
    {
        var right = HorizontalSkyline.FromBoxes(new[]
        {
            (-2.0, 2.0, 0.0, 0.5),   // small
            (-1.0, 1.0, 0.0, 2.0),   // outer at y=0
        }, HorizontalDirection.Right);

        Assert.Equal(2.0, right.X(0), 6);   // envelope, not first-in-list
        Assert.Equal(0.5, right.X(1.5), 6); // only the tall-thin building covers
    }

    [Fact]
    public void X_LeftSkyline_ReturnsOutermostLeftEdge()
    {
        var left = HorizontalSkyline.FromBoxes(new[]
        {
            (-2.0, 2.0, -0.5, 3.0),  // left edge -0.5
            (-1.0, 1.0, -2.0, 3.0),  // outer (further left) at y=0
        }, HorizontalDirection.Left);

        Assert.Equal(-2.0, left.X(0), 6);
        Assert.Equal(-0.5, left.X(1.5), 6);
    }

    [Fact]
    public void Merge_ThenDistance_EqualsEnvelope()
    {
        var a = HorizontalSkyline.FromBox(-2, 0, 0, 1, HorizontalDirection.Right);
        var b = HorizontalSkyline.FromBox(0, 2, 0, 3, HorizontalDirection.Right);
        a.Merge(b);

        var left = HorizontalSkyline.FromBox(-2, 2, 0, 1, HorizontalDirection.Left);

        // Envelope: max(1, 3) over the overlapping band → 3.
        Assert.Equal(3.0, a.Distance(left), 6);
    }
}
