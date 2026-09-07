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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The beam face (<see cref="BeamLayout.OuterEdgeStaffSpaceAtX"/>) is read in ONE frame —
/// the stems' — and every producer of a "where is this member's stem" answer hands that
/// frame in.
/// </summary>
/// <remarks>
/// <para>
/// The quanter's two Y are the primary line AT THE OUTER MEMBER STEMS
/// (<c>BeamScoringProblem.AtOuterStems</c>). Until 2026-09-07 the face interpolated them
/// between the COLUMN ANCHORS, one stem attachment to the left: a reader that handed in an
/// anchor got its member's tip exactly (the shift cancelled), and a reader that handed in a
/// real x — the slur's stem attachment, a beamed rest's ink centre — got the line one attach
/// further right, off by slope × attach. The tuplet engraver then "corrected" its note tips
/// for a shift they had never suffered, and the follow-beam bracket landed slope × half a
/// stem too deep (ledger <c>staff.staff.tuplet-bracket-follow-beam</c>, +0.007811 =
/// 0.119904 × 0.065). These pin the frame on a beam sloped enough that the two frames are
/// a full staff space apart at the anchors.
/// </para>
/// <para>
/// A SLOPED beam is the whole point: on a flat one the two frames agree everywhere, which is
/// how the anchor frame stayed green through every flat-beam ledger point.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BeamFaceFrameTests
{
    private static NoteItem Note(int staffPosition, Fraction duration)
        => new(staffPosition, duration, 0, null, false, 0);

    /// <summary>Two beamed eighths, up stems, the line RISING one staff space per staff
    /// space of x (positions 4 → 12 over a 4-ss run between the stems). Anchors at 0 and
    /// 4; the drawn stems one black-head up attachment to the right of each.</summary>
    private static BeamLayout SteepUpBeam()
    {
        var e1 = Note(0, Fraction.Eighth);
        var e2 = Note(0, Fraction.Eighth);
        var members = ImmutableArray.Create(
            new BeamMember(e1, 1, 0, 1, 0, 0, memberStemUp: true),
            new BeamMember(e2, 1, 1, 0, 0, 1, memberStemUp: true));
        var group = new BeamGroup(members, 0, 0, stemUp: true);
        double attach = LayoutUtilities.StemAttachX(true, 4, NoteheadStyle.Default);
        return new BeamLayout(group, leftY: 4, rightY: 12, leftX: 0, rightX: 4,
            leftStemX: 0 + attach, rightStemX: 4 + attach,
            ImmutableArray.Create(0.0, 4.0), staffIndex: 0, systemIndex: 0);
    }

    /// <summary>
    /// The face at a member's DRAWN STEM is that member's quanted Y plus half the beam's
    /// thickness — the quantity <c>AtOuterStems</c> answered, read back where it is true.
    /// </summary>
    [Fact]
    public void TheFaceAtTheOuterStems_IsTheQuantersOwnAnswer()
    {
        var beam = SteepUpBeam();
        double half = EngravingDefaults.BeamThickness / 2.0;
        Assert.Equal(4 / 2.0 + half, beam.OuterEdgeStaffSpaceAtX(beam.LeftStemX, stemUp: true), 12);
        Assert.Equal(12 / 2.0 + half, beam.OuterEdgeStaffSpaceAtX(beam.RightStemX, stemUp: true), 12);
        // Its slope is over the STEMS' run, which is the anchors' run for a same-direction
        // beam — so a read a little LEFT of the first stem is that much slope lower, which
        // is what the anchor frame silently did to every real-x reader (by a whole attach).
        Assert.Equal(
            4 / 2.0 + half - 0.05 * beam.Slope / 2.0,
            beam.OuterEdgeStaffSpaceAtX(beam.LeftStemX - 0.05, stemUp: true), 12);
        // The column ANCHOR of an up stem is not on the beam at all: it lies one attach
        // (1.2392) left of the stem, past the drawn end, so the face clamps there. This is
        // the x every producer handed in until 2026-09-07.
        double halfStem = EngravingDefaults.StemThickness / 2.0;
        Assert.Equal(
            4 / 2.0 + half - halfStem * beam.Slope / 2.0,
            beam.OuterEdgeStaffSpaceAtX(beam.LeftX, stemUp: true), 12);
        // ⚠️ POSITIVE CONTROL: the two frames must actually differ on this beam, or the
        // assertions above pin nothing (they agree on any flat beam).
        Assert.NotEqual(
            beam.OuterEdgeStaffSpaceAtX(beam.LeftX, true),
            beam.OuterEdgeStaffSpaceAtX(beam.LeftStemX, true), 6);
    }

    /// <summary>
    /// <see cref="BeamLayout.MemberStemX"/> is the x the producers hand to
    /// <see cref="NoteColumnLayout.BeamStemX"/>, and reading a member's tip there gives the
    /// quanter's answer for that member; reading it at the anchor (what every producer
    /// handed in until 2026-09-07) no longer does.
    /// </summary>
    [Fact]
    public void AMembersTip_IsReadAtItsDrawnStem_NotAtItsAnchor()
    {
        var beam = SteepUpBeam();
        double half = EngravingDefaults.BeamThickness / 2.0;
        double attach = LayoutUtilities.StemAttachX(true, 4, NoteheadStyle.Default);
        Assert.Equal(beam.MemberXPositions[1] + attach, beam.MemberStemX(1), 12);

        var atStem = NoteColumnLayout.Of(
            Note(0, Fraction.Eighth), forcedStemUp: true, beam, beamStemX: beam.MemberStemX(1))!.Value;
        Assert.Equal(EngravingDefaults.StaffMiddle - (12 / 2.0 + half),
            atStem.OutwardTipDeviceY(towardUp: true), 12);

        var atAnchor = NoteColumnLayout.Of(
            Note(0, Fraction.Eighth), forcedStemUp: true, beam, beamStemX: beam.MemberXPositions[1])!.Value;
        Assert.NotEqual(atStem.OutwardTipDeviceY(true), atAnchor.OutwardTipDeviceY(true), 6);
    }

    /// <summary>
    /// A real x between the stems is interpolated on the line THROUGH the stems, and past
    /// the beam's drawn ends (half a stem thickness beyond each outer stem) the face clamps —
    /// there is no beam there. LILYPOND-REF: lily/beam.cc:631 calc_beam_segments.
    /// </summary>
    [Fact]
    public void ARealX_IsInterpolatedBetweenTheStems_AndClampedAtTheDrawnEnds()
    {
        var beam = SteepUpBeam();
        double half = EngravingDefaults.BeamThickness / 2.0;
        double midX = (beam.LeftStemX + beam.RightStemX) / 2.0;
        Assert.Equal(8 / 2.0 + half, beam.OuterEdgeStaffSpaceAtX(midX, stemUp: true), 12);

        double halfStem = EngravingDefaults.StemThickness / 2.0;
        Assert.Equal(beam.LeftStemX - halfStem, beam.DrawnLeftX, 12);
        Assert.Equal(beam.RightStemX + halfStem, beam.DrawnRightX, 12);
        Assert.Equal(
            beam.OuterEdgeStaffSpaceAtX(beam.DrawnRightX, true),
            beam.OuterEdgeStaffSpaceAtX(beam.DrawnRightX + 100, true), 12);
    }
}
