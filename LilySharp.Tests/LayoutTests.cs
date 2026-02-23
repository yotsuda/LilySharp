using Xunit;
using LilySharp.Core.Svg;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class LayoutTests
{
    [Fact]
    public void PaperSettings_Default_IsA4()
    {
        var paper = PaperSettings.Default;

        Assert.Equal(210.0, paper.PaperWidth);
        Assert.Equal(297.0, paper.PaperHeight);
        Assert.Equal(10.0, paper.TopMargin);
        Assert.Equal(10.0, paper.BottomMargin);
        Assert.Equal(15.0, paper.LeftMargin);
        Assert.Equal(15.0, paper.RightMargin);
    }

    [Fact]
    public void PaperSettings_LineWidth_CalculatedCorrectly()
    {
        var paper = PaperSettings.Default;

        // line-width = paper-width - left-margin - right-margin
        // 210 - 15 - 15 = 180
        Assert.Equal(180.0, paper.LineWidth);
    }

    [Fact]
    public void PaperSettings_StaffSpace_IsQuarterOfStaffSize()
    {
        var paper = PaperSettings.Default;

        // staff-space = staff-size / 4
        // 20 / 4 = 5
        Assert.Equal(5.0, paper.StaffSpace);
    }

    [Fact]
    public void PaperSettings_PrintableHeight_ExcludesMargins()
    {
        var paper = PaperSettings.Default;

        // printable = paper-height - top-margin - bottom-margin
        // 297 - 10 - 10 = 277
        Assert.Equal(277.0, paper.GetPrintableHeight());
    }

    [Fact]
    public void PaperSettings_PrintableHeight_ExcludesHeaderFooter()
    {
        var paper = PaperSettings.Default;

        // printable = paper-height - top-margin - bottom-margin - header - footer
        // 297 - 10 - 10 - 20 - 15 = 242
        Assert.Equal(242.0, paper.GetPrintableHeight(headerHeight: 20, footerHeight: 15));
    }

    [Theory]
    [InlineData(PaperSize.A4, 210.0, 297.0)]
    [InlineData(PaperSize.A5, 148.0, 210.0)]
    [InlineData(PaperSize.Letter, 215.9, 279.4)]
    public void PaperSettings_ForPaperSize_SetsDimensions(PaperSize size, double expectedWidth, double expectedHeight)
    {
        var paper = PaperSettings.ForPaperSize(size);

        Assert.Equal(expectedWidth, paper.PaperWidth);
        Assert.Equal(expectedHeight, paper.PaperHeight);
    }

    [Fact]
    public void PaperSettings_TwoSided_OddPageHasOuterMarginLeft()
    {
        var paper = new PaperSettings
        {
            TwoSided = true,
            InnerMargin = 20,
            OuterMargin = 15,
            BindingOffset = 5
        };

        // Odd page (1): outer margin on left
        Assert.Equal(15.0, paper.GetLeftMargin(1));
        // Even page (2): inner margin + binding offset on left
        Assert.Equal(25.0, paper.GetLeftMargin(2)); // 20 + 5
    }

    [Fact]
    public void SpacingSettings_Default_HasCorrectValues()
    {
        var spacing = SpacingSettings.Default;

        // system-system-spacing defaults
        Assert.Equal(12, spacing.SystemSystem.BasicDistance);
        Assert.Equal(8, spacing.SystemSystem.MinimumDistance);
        Assert.Equal(1, spacing.SystemSystem.Padding);
        Assert.Equal(60, spacing.SystemSystem.Stretchability);
    }

    [Fact]
    public void Spring_Length_WithZeroForce_ReturnsIdeal()
    {
        var spring = new Spring(idealDistance: 10, minDistance: 5);

        Assert.Equal(10.0, spring.Length(0));
    }

    [Fact]
    public void Spring_Length_WithPositiveForce_Stretches()
    {
        var spring = new Spring(idealDistance: 10, minDistance: 5);

        // length = ideal + force * inverse_strength
        // with default strength, inverse_strength = ideal - min = 5
        // length = 10 + 1 * 5 = 15
        Assert.Equal(15.0, spring.Length(1));
    }

    [Fact]
    public void Spring_Length_WithNegativeForce_Compresses()
    {
        var spring = new Spring(idealDistance: 10, minDistance: 5);

        // length = ideal + force * inverse_strength
        // length = 10 + (-1) * 5 = 5
        Assert.Equal(5.0, spring.Length(-1));
    }

    [Fact]
    public void Spring_Length_DoesNotGoBelowMinimum()
    {
        var spring = new Spring(idealDistance: 10, minDistance: 5);

        // Even with large negative force, should not go below minimum
        Assert.Equal(5.0, spring.Length(-100));
    }
}
