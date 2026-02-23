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
