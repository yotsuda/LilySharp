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
    /// <c>default-staff-staff-spacing</c> declares NO <c>stretchability</c>, and that has to
    /// stay spelled as the absence rather than as the 9 it works out to.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:213-216 <c>set_default_strength</c> — with no
    /// stretchability in the spec the inverse stretch strength IS the ideal, so the two
    /// spellings agree at today's basic-distance and diverge the moment anything overrides
    /// it: LilyPond's spring follows the new ideal, a literal 9 would not.
    /// <para>
    /// ⚠️ This exists because the port was first written with the literal. Nothing would
    /// have caught it — every output is identical, and <c>LpProvenanceTests</c> only asks
    /// that a constant carry a source, which the literal did (HANDOFF 5.2.1 (1): a REF can
    /// sit next to a different expression). The test asserts the RULE instead: perturb the
    /// basic-distance and the stretch strength must follow it.
    /// </para>
    /// </remarks>
    [Fact]
    public void DefaultStaffStaff_TakesItsStretchStrengthFromTheIdeal_NotFromALiteral()
    {
        var spec = StaffSpacingParameters.Default.DefaultStaffStaff;
        Assert.Equal(0, spec.Stretchability);

        // Spring.InverseStretchStrength is the quantity set_default_strength decides.
        Assert.Equal(
            spec.BasicDistance,
            LayoutUtilities.CreateSpring(spec, 0).InverseStretchStrength, 9);

        var widened = spec with { BasicDistance = 14 };
        Assert.Equal(
            14, LayoutUtilities.CreateSpring(widened, 0).InverseStretchStrength, 9);
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
