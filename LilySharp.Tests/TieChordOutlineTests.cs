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
/// The tie's per-column CHORD OUTLINE — the skyline a tie reads its attachment X off.
/// </summary>
/// <remarks>
/// <para>
/// These assert the outline DIRECTLY, at a Y of the test's choosing, because the thing the
/// port is about is that the answer DEPENDS on that Y. A corpus book only ever shows the one
/// Y its own tie happened to land on, and for as long as every short tie landed inside its
/// head box, the difference between "the head's edge" and "the outline at this Y" was
/// invisible in 210 snapshots.
/// </para>
/// <para>
/// The numbers are LilyPond's own, measured on <c>&lt;c d&gt;2 ~ &lt;c d&gt;2</c> (score TWSEC of
/// audit/lp-geometry/probes/tie-direction.ly) in the system's X frame, and quoted here so a
/// reader can check the fixture against the engraver rather than against this file:
/// </para>
/// <code>
/// left column   lower head (8.585000 . 9.962400)   upper head (9.897400 . 11.274800)
/// right column  lower head (12.860445 . 14.237845) upper head (14.172845 . 15.550245)
/// stem origin   9.897400 (left) / 14.172845 (right), each widened by staff_space/20
/// lower tie     L=9.473700  R=13.349145   = head CENTRE +- note-head-gap
/// upper tie     L=10.786100 R=13.772845   = 14.122845 - stem-gap on the right
/// </code>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:96-287 set_column_chord_outline.
/// </remarks>
[Trait("Category", "Unit")]
public class TieChordOutlineTests
{
    // The left column of TWSEC: a stem-up chord of seconds, both heads tied.
    private static TieColumnParts LeftColumnOfSeconds() => new()
    {
        TiedHeads =
        [
            new TieOutlineHead(-6, 8.585000, 9.962400),
            new TieOutlineHead(-5, 9.897400, 11.274800),
        ],
        // Stem up: origin at the lower head's right edge less half the stem thickness, running
        // from the FOOT head's position (-6) to the tip.
        Stem = new TieOutlineStem(
            IsNormal: true, CentreX: 9.897400, TipY: 1.0,
            NearHeadPosition: -6, SupportHeadCentreX: 9.273700),
        HeadPositions = [-6, -5],
    };

    private static TieColumnParts RightColumnOfSeconds() => new()
    {
        TiedHeads =
        [
            new TieOutlineHead(-6, 12.860445, 14.237845),
            new TieOutlineHead(-5, 14.172845, 15.550245),
        ],
        Stem = new TieOutlineStem(
            IsNormal: true, CentreX: 14.172845, TipY: 1.0,
            NearHeadPosition: -6, SupportHeadCentreX: 13.549145),
        HeadPositions = [-6, -5],
    };

    /// <summary>
    /// ABOVE the topmost head the outline recedes to that head's CENTRE — the recession box —
    /// and that is the whole reason a tie which clears its heads comes out one head wider than
    /// one running alongside them.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tie-formatting-problem.cc:243-258 set_column_chord_outline;
    /// the centre (rather than the three-quarter point) is
    /// flower/include/interval.hh:303-316 linear_combination with an integer-divided argument.</remarks>
    [Fact]
    public void AboveTheHeads_TheOutlineRecedesToTheTopHeadsCentre()
    {
        var left = TieChordOutline.Build(LeftColumnOfSeconds(), isLeftBound: true, 0.05);

        // The upper head spans positions -6..-4, i.e. -3.0 .. -2.0 staff spaces.
        Assert.Equal((9.897400 + 11.274800) / 2, left.Attachment(-1.5), 9);
    }

    /// <summary>
    /// THE RECESSION DOES NOT START AT THE HEAD'S EDGE, it starts a skyline-padding beyond it,
    /// on a 45° ramp — and the tie of TWSEC clears that ramp by 0.19 rather than by nothing.
    /// </summary>
    /// <remarks>
    /// This is the one number in the outline that could be read as slack and is not:
    /// LilyPond pads the whole thing (<c>Skyline (boxes, …).padded (skyline_padding)</c>), so a
    /// candidate sitting a hair above a head still meets that head.
    /// LILYPOND-REF: lily/skyline.cc:558-615 Skyline::padded (horizon_padding);
    /// lily/tie-formatting-problem.cc:260-261 set_column_chord_outline.
    /// </remarks>
    [Fact]
    public void JustAboveTheHeads_ThePaddingKeepsTheHeadInTheOutline()
    {
        var left = TieChordOutline.Build(LeftColumnOfSeconds(), isLeftBound: true, 0.05);

        // The head box ends at -2.0. The pad runs flat to -1.95 and ramps to -1.90, where it
        // has given up exactly one padding of reach...
        Assert.Equal(11.274800 - 0.05, left.Attachment(-1.90), 9);
        // ...and past that it is gone and the recession box has the Y.
        Assert.Equal((9.897400 + 11.274800) / 2, left.Attachment(-1.85), 9);
    }

    /// <summary>
    /// WITHIN a head's own box the outline stands at that head's EDGE, not its centre.
    /// </summary>
    [Fact]
    public void InsideTheHeadBox_TheOutlineStandsAtTheHeadsEdge()
    {
        var left = TieChordOutline.Build(LeftColumnOfSeconds(), isLeftBound: true, 0.05);

        // Mid-way up the upper head's box.
        Assert.Equal(11.274800, left.Attachment(-2.5), 9);
    }

    /// <summary>
    /// The STEM is in the outline, and on the side it faces it is what the tie meets first.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE BOX IS 0.1 WIDE AND NOT THE STEM'S OWN 0.13: LilyPond adds the stem's origin as a
    /// POINT and widens it by staff_space/20 (:150-151). Reading the drawing instead gives
    /// 0.015 too much on each side.
    /// </remarks>
    [Fact]
    public void TheStemIsInTheOutline_AndFacesTheArrivingTie()
    {
        var right = TieChordOutline.Build(RightColumnOfSeconds(), isLeftBound: false, 0.05);

        // Above the heads the recession box would give the upper head's centre (14.861545),
        // but the stem's box reaches further left and wins.
        Assert.Equal(14.172845 - 0.05, right.Attachment(-1.76), 9);
        Assert.Equal(14.172845 - 0.05, right.StemBox!.Value.Left, 9);
        Assert.Equal(14.172845 + 0.05, right.StemBox!.Value.Right, 9);
    }

    /// <summary>
    /// BELOW the bottommost head the recession box takes over on that side too — which is what
    /// makes the LOWER tie of a seconds chord the wide one.
    /// </summary>
    [Fact]
    public void BelowTheHeads_BothColumnsRecedeToTheBottomHeadsCentre()
    {
        var left = TieChordOutline.Build(LeftColumnOfSeconds(), isLeftBound: true, 0.05);
        var right = TieChordOutline.Build(RightColumnOfSeconds(), isLeftBound: false, 0.05);

        // The lower tie of TWSEC sits at -3.75; LilyPond draws it 9.473700 -> 13.349145,
        // i.e. these two centres inset by note-head-gap 0.2 at each end.
        Assert.Equal(9.273700, left.Attachment(-3.75), 9);
        Assert.Equal(13.549145, right.Attachment(-3.75), 9);
        Assert.Equal(3.875445, (13.549145 - 0.2) - (9.273700 + 0.2), 9);
    }

    /// <summary>
    /// <c>head_extents_</c> is the union of ALL the column's TIED heads and carries no stem —
    /// the horizontal-distance term measures against a head, the attachment against the
    /// outline, and the head-edge hug against this UNION (which is why a middle tie of a chord
    /// never hugs).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tie-formatting-problem.cc:282-286 set_column_chord_outline.</remarks>
    [Fact]
    public void HeadExtentIsTheUnionOfTheTiedHeadsAndCarriesNoStem()
    {
        var left = TieChordOutline.Build(LeftColumnOfSeconds(), isLeftBound: true, 0.05);

        Assert.Equal(8.585000, left.HeadX.Left, 9);
        Assert.Equal(11.274800, left.HeadX.Right, 9);
        // The lower head's box bottom and the upper head's box top — both heads, not one.
        Assert.Equal(-3.5, left.HeadY.Down, 9);
        Assert.Equal(-2.0, left.HeadY.Up, 9);
    }
}
