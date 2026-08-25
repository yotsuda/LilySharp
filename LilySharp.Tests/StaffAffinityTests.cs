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

    /// <summary>The Lyrics context's specs, which is what every case below reads unless it
    /// says otherwise.</summary>
    private static StaffSpacingParameters.NonStaffSpacing Lyrics => Sp.Lyrics;

    private static VerticalSpacingSpec Spec(int? before, int? after)
        => StaffAffinity.GetSpacingSpec(before, Lyrics, after, Lyrics, Spaceable);

    [Fact]
    public void GetSpacingSpec_BothSpaceable_ReturnsSpaceableSpec()
    {
        Assert.Same(Spaceable, Spec(null, null));
    }

    /// <summary>
    /// Two non-spaceable lines whose affinities do NOT point away from each other take the
    /// UPPER line's <c>nonstaff-nonstaff-spacing</c> — verse to verse, and any pair whose
    /// upper line is not UP.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-layout-problem.cc:1327-1332.</remarks>
    [Theory]
    [InlineData(StaffAffinityDirection.Up, StaffAffinityDirection.Up)]
    [InlineData(StaffAffinityDirection.Down, StaffAffinityDirection.Down)]
    [InlineData(StaffAffinityDirection.Down, StaffAffinityDirection.Up)]
    [InlineData(StaffAffinityDirection.Center, StaffAffinityDirection.Down)]
    public void GetSpacingSpec_BothNonSpaceable_ReturnsNonStaffNonStaff(int before, int after)
    {
        Assert.Same(Sp.NonStaffNonStaff, Spec(before, after));
    }

    /// <summary>
    /// THE THIRD BRANCH, which Lily# did not have: two non-spaceable lines pointing AWAY
    /// from each other — the upper one UP, the lower one DOWN — belong to different staves,
    /// so LilyPond puts the upper line's <c>nonstaff-unrelatedstaff-spacing</c> and a
    /// LARGE_STRETCH between them instead of the verse-to-verse spec.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-layout-problem.cc:1333-1336.</remarks>
    [Fact]
    public void GetSpacingSpec_NonSpaceablePairPointingApart_IsUnrelatedWithLargeStretch()
    {
        var spec = Spec(StaffAffinityDirection.Up, StaffAffinityDirection.Down);

        Assert.Equal(Sp.NonStaffUnrelatedStaff.Padding, spec.Padding);
        Assert.Equal(StaffAffinity.LargeStretch, spec.Stretchability);
    }

    [Fact]
    public void GetSpacingSpec_LowerNonSpaceableAffinityUp_PointsAtUpperSpaceable_Related()
    {
        // Lyrics (lower) with affinity UP attaches to the spaceable staff above ⇒ related.
        Assert.Same(Sp.NonStaffRelatedStaff, Spec(null, StaffAffinityDirection.Up));
    }

    [Fact]
    public void GetSpacingSpec_LowerNonSpaceableAffinityDown_PointsAway_Unrelated()
    {
        // Non-staff (lower) with affinity DOWN points away from the upper spaceable ⇒ unrelated.
        var spec = Spec(null, StaffAffinityDirection.Down);

        Assert.Equal(Sp.NonStaffUnrelatedStaff.Padding, spec.Padding);
        Assert.Equal(StaffAffinity.LargeStretch, spec.Stretchability);
    }

    [Fact]
    public void GetSpacingSpec_UpperNonSpaceableAffinityDown_PointsAtLowerSpaceable_Related()
    {
        // Non-staff (upper) with affinity DOWN attaches to the spaceable staff below ⇒ related.
        Assert.Same(Sp.NonStaffRelatedStaff, Spec(StaffAffinityDirection.Down, null));
    }

    [Fact]
    public void GetSpacingSpec_UpperNonSpaceableAffinityUp_PointsAway_Unrelated()
    {
        // Non-staff (upper) with affinity UP points away from the lower spaceable ⇒ unrelated.
        var spec = Spec(StaffAffinityDirection.Up, null);

        Assert.Equal(Sp.NonStaffUnrelatedStaff.Padding, spec.Padding);
        Assert.Equal(StaffAffinity.LargeStretch, spec.Stretchability);
    }

    [Theory]
    [InlineData(StaffAffinityDirection.Center, null)]   // upper non-spaceable CENTER + lower spaceable
    [InlineData(null, StaffAffinityDirection.Center)]   // upper spaceable + lower non-spaceable CENTER
    public void GetSpacingSpec_CenterAffinity_TreatedAsRelated(int? upper, int? lower)
    {
        Assert.Same(Sp.NonStaffRelatedStaff, Spec(upper, lower));
    }

    /// <summary>
    /// THE SPEC IS READ OFF THE GROB, so a ChordNames line and a Lyrics line under the same
    /// staff take DIFFERENT springs from the same property name.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:649-652 against :722 — Lyrics declares
    /// <c>(basic-distance . 5.5) (padding . 0.5) (stretchability . 1)</c>, ChordNames
    /// declares <c>(padding . 0.5)</c> and nothing else. Reusing one table for both is the
    /// mistake this pair exists to catch: it builds the Lyrics' 5.5-ideal spring under a
    /// chord row.
    /// </remarks>
    [Fact]
    public void GetSpacingSpec_RelatedStaff_ComesFromTheLinesOwnContext()
    {
        var lyric = StaffAffinity.GetSpacingSpec(
            null, Sp.Lyrics, StaffAffinityDirection.Up, Sp.Lyrics, Spaceable);
        var chords = StaffAffinity.GetSpacingSpec(
            StaffAffinityDirection.Down, Sp.ChordNames, null, Sp.Lyrics, Spaceable);

        Assert.Equal(5.5, lyric.BasicDistance);
        Assert.Equal(1.0, lyric.Stretchability);
        Assert.Equal(0.5, lyric.Padding);

        // ChordNames declares only the padding: the ideal and both strengths are the
        // caller's Spring (1.0, 0.0) (page-layout-problem.cc:1035).
        Assert.Equal(1.0, chords.BasicDistance);
        Assert.Null(chords.Stretchability);
        Assert.Equal(0.5, chords.Padding);
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
        Assert.Equal(1.5, Sp.NonStaffUnrelatedStaff.Padding);

        // ★ THE IDEAL IS 1.0, NOT 0 (2026-08-26). The padding is ALL this spec declares — no
        // basic-distance — so read_spacing_spec leaves the caller's own
        // `Spring spring (1.0, 0.0)` standing (page-layout-problem.cc:1035). This asserted 0
        // while the SECOND copy of the same spec, the one the loose chain actually read, held
        // 1.0; the two were never compared, because SystemSpacingTests' two-homes pin
        // enumerated the other two specs and not this one. The copy is gone — see
        // StaffSpacingParameters.NonStaffUnrelatedStaff — and this is the surviving number.
        Assert.Equal(1.0, Sp.NonStaffUnrelatedStaff.BasicDistance);
        Assert.Null(Sp.NonStaffUnrelatedStaff.Stretchability);
    }
}
