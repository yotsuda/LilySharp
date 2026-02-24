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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for alignment-distances manual override via StaffGrouper properties.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:656-717 alignment_distances
/// LILYPOND-REF: lily/staff-grouper-interface.cc — staff-staff-spacing, staffgroup-staff-spacing
/// </remarks>
[Trait("Category", "Unit")]
public class AlignmentDistanceTests
{
    [Fact]
    public void ApplyOverrides_StaffStaffBasicDistance_Changes()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffGrouper", "staff-staff-spacing.basic-distance", "15", 0, 0));

        var result = StaffSpacingParameters.Default.ApplyOverrides(overrides);

        Assert.Equal(15, result.StaffStaff.BasicDistance);
        // Other properties unchanged
        Assert.Equal(StaffSpacingParameters.Default.StaffStaff.MinimumDistance, result.StaffStaff.MinimumDistance);
        Assert.Equal(StaffSpacingParameters.Default.StaffStaff.Padding, result.StaffStaff.Padding);
        Assert.Equal(StaffSpacingParameters.Default.StaffStaff.Stretchability, result.StaffStaff.Stretchability);
    }

    [Fact]
    public void ApplyOverrides_StaffGroupStaffPadding_Changes()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffGrouper", "staffgroup-staff-spacing.padding", "3.5", 0, 0));

        var result = StaffSpacingParameters.Default.ApplyOverrides(overrides);

        Assert.Equal(3.5, result.StaffGroupStaff.Padding);
        // StaffStaff unchanged
        Assert.Equal(StaffSpacingParameters.Default.StaffStaff.Padding, result.StaffStaff.Padding);
    }

    [Fact]
    public void ApplyOverrides_AllFourSubProperties()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffGrouper", "staff-staff-spacing.basic-distance", "12", 0, 0),
            new GrobOverride("StaffGrouper", "staff-staff-spacing.minimum-distance", "10", 0, 0),
            new GrobOverride("StaffGrouper", "staff-staff-spacing.padding", "2", 0, 0),
            new GrobOverride("StaffGrouper", "staff-staff-spacing.stretchability", "100", 0, 0));

        var result = StaffSpacingParameters.Default.ApplyOverrides(overrides);

        Assert.Equal(12, result.StaffStaff.BasicDistance);
        Assert.Equal(10, result.StaffStaff.MinimumDistance);
        Assert.Equal(2, result.StaffStaff.Padding);
        Assert.Equal(100, result.StaffStaff.Stretchability);
    }

    [Fact]
    public void ApplyOverrides_NoStaffGrouperOverrides_ReturnsIdentical()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("NoteHead", "color", "red", 0, 0));

        var result = StaffSpacingParameters.Default.ApplyOverrides(overrides);

        Assert.Same(StaffSpacingParameters.Default, result);
    }

    [Fact]
    public void ApplyOverrides_EmptyArray_ReturnsIdentical()
    {
        var result = StaffSpacingParameters.Default.ApplyOverrides(ImmutableArray<GrobOverride>.Empty);

        Assert.Same(StaffSpacingParameters.Default, result);
    }

    [Fact]
    public void ApplyOverrides_InvalidPropertyName_Ignored()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffGrouper", "unknown-spacing.basic-distance", "10", 0, 0));

        var result = StaffSpacingParameters.Default.ApplyOverrides(overrides);

        Assert.Same(StaffSpacingParameters.Default, result);
    }

    [Fact]
    public void ApplyOverrides_InvalidValue_Ignored()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffGrouper", "staff-staff-spacing.basic-distance", "not-a-number", 0, 0));

        var result = StaffSpacingParameters.Default.ApplyOverrides(overrides);

        Assert.Same(StaffSpacingParameters.Default, result);
    }

    [Fact]
    public void ApplyOverrides_BothSpacingTypes()
    {
        // Override both staff-staff and staffgroup-staff
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffGrouper", "staff-staff-spacing.basic-distance", "6", 0, 0),
            new GrobOverride("StaffGrouper", "staffgroup-staff-spacing.basic-distance", "14", 0, 0));

        var result = StaffSpacingParameters.Default.ApplyOverrides(overrides);

        Assert.Equal(6, result.StaffStaff.BasicDistance);
        Assert.Equal(14, result.StaffGroupStaff.BasicDistance);
    }
}
