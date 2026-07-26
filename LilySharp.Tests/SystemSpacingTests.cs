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

[Trait("Category", "Unit")]
public class SystemSpacingTests
{
    // --- StaffSpacingParameters ---

    [Fact]
    public void StaffSpacingParameters_Default_MatchesLilyPond()
    {
        var p = StaffSpacingParameters.Default;

        // LILYPOND-REF: scm/define-grobs.scm:3042-3045
        Assert.Equal(9, p.StaffStaff.BasicDistance);
        Assert.Equal(7, p.StaffStaff.MinimumDistance);
        Assert.Equal(1, p.StaffStaff.Padding);
        Assert.Equal(5, p.StaffStaff.Stretchability);

        // LILYPOND-REF: scm/define-grobs.scm:3046-3049
        Assert.Equal(10.5, p.StaffGroupStaff.BasicDistance);
        Assert.Equal(8, p.StaffGroupStaff.MinimumDistance);
        Assert.Equal(1, p.StaffGroupStaff.Padding);
        Assert.Equal(9, p.StaffGroupStaff.Stretchability);

        // LILYPOND-REF: scm/define-grobs.scm:4237-4239 — the branch for a staff that has no
        // staff-grouper at all (axis-group-interface.cc:1008-1027).
        Assert.Equal(9, p.DefaultStaffStaff.BasicDistance);
        Assert.Equal(8, p.DefaultStaffStaff.MinimumDistance);
        Assert.Equal(1, p.DefaultStaffStaff.Padding);
    }

    /// <summary>
    /// Every spec whose LilyPond source declares NO <c>stretchability</c> has to stay
    /// spelled as the ABSENCE (0), never as the number the rule works out to.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1350-1357
    /// <c>alter_spring_from_spacing_spec</c> — <c>set_default_strength()</c> runs
    /// UNCONDITIONALLY (:1354) and only then does a declared <c>stretchability</c> override
    /// it (:1356-1357); <c>set_default_stretch_strength</c> is
    /// <c>inverse_stretch_strength_ = ideal_distance_</c> (spring.cc:213-216). So an absent
    /// stretchability tracks the ideal and a declared one does not.
    /// <para>
    /// ⚠️ WHY THIS IS A TEST AND NOT A COMMENT. Both members below were first written as the
    /// number instead of the absence — <c>DefaultStaffStaff</c> as 9 when the port was
    /// added, <c>MarkupMarkup</c> as 1 long before that — and NOTHING could have caught
    /// either: every output is byte-identical (the ideal equals the literal in both), and
    /// <c>LpProvenanceTests</c> only asks that a constant carry a source, which both did
    /// (HANDOFF 5.2.1 (1): a REF can sit beside a different expression). So the RULE is
    /// asserted instead of the number — perturb the basic-distance and the stretch strength
    /// must follow it. Written as a Theory so the next spec of this shape is one line.
    /// </para>
    /// </remarks>
    [Theory]
    // LILYPOND-REF: scm/define-grobs.scm:4237-4239 default-staff-staff-spacing.
    [InlineData("DefaultStaffStaff", 9.0)]
    // LILYPOND-REF: ly/paper-defaults-init.ly:76-77 markup-markup-spacing.
    [InlineData("MarkupMarkup", 1.0)]
    public void SpecsLilyPondLeavesUnstretched_TrackTheIdeal_NotALiteral(
        string member, double expectedIdeal)
    {
        var spec = member switch
        {
            "DefaultStaffStaff" => StaffSpacingParameters.Default.DefaultStaffStaff,
            "MarkupMarkup" => VerticalSpacingParameters.Default.MarkupMarkup,
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
        };

        // 0 IS LilyPond's absent — CreateSpring's convention.
        Assert.Equal(0, spec.Stretchability);
        Assert.Equal(expectedIdeal, spec.BasicDistance);

        Assert.Equal(
            expectedIdeal,
            LayoutUtilities.CreateSpring(spec, 0).InverseStretchStrength, 9);

        // The rule, not the number: move the ideal and the strength must move with it.
        Assert.Equal(
            expectedIdeal + 5,
            LayoutUtilities.CreateSpring(spec with { BasicDistance = expectedIdeal + 5 }, 0)
                .InverseStretchStrength, 9);
    }

    /// <summary>
    /// <c>nonstaff-relatedstaff-spacing</c> and <c>nonstaff-nonstaff-spacing</c> each have
    /// TWO homes in Lily#, and they have to agree.
    /// </summary>
    /// <remarks>
    /// One LilyPond property, two transcriptions: <see cref="StaffSpacingParameters"/> holds
    /// the pair the STAFF layout reaches through <c>StaffAffinity.Select</c>, and
    /// <c>LooseLineSpacer</c> holds the pair the loose-line chain builds its springs from.
    /// HANDOFF 5.2.1 (2) is about exactly this: a duplicated quantity is where a port lands
    /// on one copy and not the other, and the padding-times-four defect lived in the copy.
    /// Unifying them is the real fix and is not free (the loose chain does not take
    /// <c>\override</c> plumbing today), so until then the invariant is asserted.
    /// <para>
    /// LILYPOND-REF: ly/engraver-init.ly:649-652 and :653-657 — the Lyrics context's two
    /// overrides, which is what BOTH copies transcribe.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoHomesOfTheLyricSpacingSpecs_Agree()
    {
        var sp = StaffSpacingParameters.Default;

        Assert.Equal(sp.NonStaffRelatedStaff, LooseLineSpacer.NonStaffRelatedStaff);
        Assert.Equal(sp.NonStaffNonStaff, LooseLineSpacer.NonStaffNonStaff);
    }

    [Fact]
    public void StaffStaff_BasicDistance_ControlsIntraGroupSpacing()
    {
        // Default basic-distance = 9 staff spaces (center to center)
        // With staffHeight = 4, gap = 9 - 4 = 5 staff spaces between staves
        var p = StaffSpacingParameters.Default;
        double staffHeight = 4;
        double expectedGap = p.StaffStaff.BasicDistance - staffHeight;

        Assert.Equal(5, expectedGap);
    }

    [Fact]
    public void StaffGroupStaff_LargerThanStaffStaff()
    {
        // Inter-group spacing should be larger to visually separate groups
        var p = StaffSpacingParameters.Default;

        Assert.True(p.StaffGroupStaff.BasicDistance > p.StaffStaff.BasicDistance,
            $"Inter-group ({p.StaffGroupStaff.BasicDistance}) should be > intra-group ({p.StaffStaff.BasicDistance})");
        Assert.True(p.StaffGroupStaff.Stretchability > p.StaffStaff.Stretchability,
            "Inter-group should be more elastic than intra-group");
    }

    [Fact]
    public void CustomStaffSpacing_OverridesDefaults()
    {
        var custom = new StaffSpacingParameters
        {
            StaffStaff = new VerticalSpacingSpec
            {
                BasicDistance = 7,
                MinimumDistance = 5,
                Padding = 0.5,
                Stretchability = 3
            }
        };

        Assert.Equal(7, custom.StaffStaff.BasicDistance);
        Assert.Equal(5, custom.StaffStaff.MinimumDistance);
        // StaffGroupStaff should still have default values
        Assert.Equal(10.5, custom.StaffGroupStaff.BasicDistance);
    }

    // --- LayoutOptions integration ---

    [Fact]
    public void LayoutOptions_HasStaffSpacing()
    {
        var options = LayoutOptions.Default;

        Assert.NotNull(options.StaffSpacing);
        Assert.Equal(9, options.StaffSpacing.StaffStaff.BasicDistance);
    }

    // --- VerticalSpacingSpec ---

    [Fact]
    public void VerticalSpacingSpec_AllPropertiesAccessible()
    {
        var spec = new VerticalSpacingSpec
        {
            BasicDistance = 12,
            MinimumDistance = 8,
            Padding = 1,
            Stretchability = 60
        };

        Assert.Equal(12, spec.BasicDistance);
        Assert.Equal(8, spec.MinimumDistance);
        Assert.Equal(1, spec.Padding);
        Assert.Equal(60, spec.Stretchability);
    }

    // --- BreakPermission enum ---

    [Fact]
    public void BreakPermission_HasExpectedValues()
    {
        Assert.Equal(0, (int)BreakPermission.Allow);
        Assert.Equal(1, (int)BreakPermission.Forbid);
        Assert.Equal(2, (int)BreakPermission.Force);
    }
}
