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
/// Unit tests for the staff-affinity → vertical-spacing-spec selection logic.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:240-252
/// LILYPOND-REF: scm/define-grob-properties.scm:819-841
/// </remarks>
[Trait("Category", "Unit")]
public class StaffAffinityTests
{
    private static readonly StaffSpacingParameters Sp = StaffSpacingParameters.Default;
    private static readonly VerticalSpacingSpec Spaceable = Sp.StaffGroupStaff;

    [Fact]
    public void IsSpaceable_NullAffinity_True()
    {
        Assert.True(StaffAffinity.IsSpaceable(null));
    }

    [Theory]
    [InlineData(StaffAffinityDirection.Up)]
    [InlineData(StaffAffinityDirection.Down)]
    [InlineData(StaffAffinityDirection.Center)]
    public void IsSpaceable_AffinitySet_False(int affinity)
    {
        Assert.False(StaffAffinity.IsSpaceable(affinity));
    }

    [Fact]
    public void Select_BothSpaceable_ReturnsSpaceableSpec()
    {
        var spec = StaffAffinity.Select(null, null, Spaceable, Sp);
        Assert.Same(Spaceable, spec);
    }

    [Fact]
    public void Select_BothNonSpaceable_ReturnsNonStaffNonStaff()
    {
        var spec = StaffAffinity.Select(
            StaffAffinityDirection.Up, StaffAffinityDirection.Down, Spaceable, Sp);
        Assert.Same(Sp.NonStaffNonStaff, spec);
    }

    [Fact]
    public void Select_LowerNonSpaceableAffinityUp_PointsAtUpperSpaceable_Related()
    {
        // Lyrics (lower) with affinity UP attaches to the spaceable staff above ⇒ related.
        var spec = StaffAffinity.Select(null, StaffAffinityDirection.Up, Spaceable, Sp);
        Assert.Same(Sp.NonStaffRelatedStaff, spec);
    }

    [Fact]
    public void Select_LowerNonSpaceableAffinityDown_PointsAway_Unrelated()
    {
        // Non-staff (lower) with affinity DOWN points away from the upper spaceable ⇒ unrelated.
        var spec = StaffAffinity.Select(null, StaffAffinityDirection.Down, Spaceable, Sp);
        Assert.Same(Sp.NonStaffUnrelatedStaff, spec);
    }

    [Fact]
    public void Select_UpperNonSpaceableAffinityDown_PointsAtLowerSpaceable_Related()
    {
        // Non-staff (upper) with affinity DOWN attaches to the spaceable staff below ⇒ related.
        var spec = StaffAffinity.Select(StaffAffinityDirection.Down, null, Spaceable, Sp);
        Assert.Same(Sp.NonStaffRelatedStaff, spec);
    }

    [Fact]
    public void Select_UpperNonSpaceableAffinityUp_PointsAway_Unrelated()
    {
        // Non-staff (upper) with affinity UP points away from the lower spaceable ⇒ unrelated.
        var spec = StaffAffinity.Select(StaffAffinityDirection.Up, null, Spaceable, Sp);
        Assert.Same(Sp.NonStaffUnrelatedStaff, spec);
    }

    [Theory]
    [InlineData(StaffAffinityDirection.Center, null)]   // upper non-spaceable CENTER + lower spaceable
    [InlineData(null, StaffAffinityDirection.Center)]   // upper spaceable + lower non-spaceable CENTER
    public void Select_CenterAffinity_TreatedAsRelated(int? upper, int? lower)
    {
        var spec = StaffAffinity.Select(upper, lower, Spaceable, Sp);
        Assert.Same(Sp.NonStaffRelatedStaff, spec);
    }

    [Fact]
    public void Spec_NonStaffRelatedStaff_HasLpDefaults()
    {
        // LILYPOND-REF: ly/engraver-init.ly:649-652 (basic-distance . 5.5) (padding . 0.5) (stretchability . 1)
        Assert.Equal(5.5, Sp.NonStaffRelatedStaff.BasicDistance);
        Assert.Equal(0.5, Sp.NonStaffRelatedStaff.Padding);
        Assert.Equal(1.0, Sp.NonStaffRelatedStaff.Stretchability);
    }

    [Fact]
    public void Spec_NonStaffNonStaff_HasLpDefaults()
    {
        // LILYPOND-REF: ly/engraver-init.ly:653-657 (basic-distance . 0) (minimum-distance . 2.8) (padding . 0.2)
        Assert.Equal(0, Sp.NonStaffNonStaff.BasicDistance);
        Assert.Equal(2.8, Sp.NonStaffNonStaff.MinimumDistance);
        Assert.Equal(0.2, Sp.NonStaffNonStaff.Padding);
    }

    [Fact]
    public void Spec_NonStaffUnrelatedStaff_HasLpDefaults()
    {
        // LILYPOND-REF: scm/define-grobs.scm:4239 (padding . 0.5)
        // LILYPOND-REF: ly/engraver-init.ly:658 Lyrics override → 1.5
        // We default to the Lyrics override since most non-staff use cases match.
        Assert.Equal(0, Sp.NonStaffUnrelatedStaff.BasicDistance);
        Assert.Equal(1.5, Sp.NonStaffUnrelatedStaff.Padding);
    }
}
