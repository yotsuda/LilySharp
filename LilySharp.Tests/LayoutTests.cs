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

}
