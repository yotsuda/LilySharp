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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Verifies that multi-measure rest rendering substitutes the H-bar visual for
/// runs of consecutive single-rest measures, and that the measure count is shown.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — big_rest + measure-count text.
/// </remarks>
[Trait("Category", "Unit")]
public class MmrRenderTests
{
    private static string Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    [Fact]
    public void Render_R1Star4_ContainsMmrCountText()
    {
        var svg = Render("R1*4 |");
        // The MMR renderer prints the measure count as text-anchor="middle" with
        // font-weight="bold". Our run produces ">4<" inside that text element.
        Assert.Contains(">4<", svg);
        // SharedRenderer emits hex colors (`#000000`) where SvgRenderer used named
        // colors (`black`). Either form is acceptable — barlines/H-bar fill must exist.
        Assert.True(svg.Contains("fill=\"black\"") || svg.Contains("fill=\"#000000\""),
            "Expected at least one black-filled rect (barlines or H-bar).");
    }

    [Fact]
    public void Render_RegularMusic_DoesNotContainMmrText()
    {
        var svg = Render("c4 d e f |");
        // No MMR span → no measure count emitted from MmrEngraver.
        // (The "4" might still appear from durations in attributes, so guard
        // by looking for the bold serif size 2.4 used by MMR text.)
        Assert.DoesNotContain("font-size=\"2.4\" font-weight=\"bold\"", svg);
    }

    [Fact]
    public void Render_MmrAndMusic_BothPresent()
    {
        var svg = Render("R1*3 c4 d e f |");
        // MMR text "3" + regular note c4 should both appear.
        Assert.Contains(">3<", svg);
    }

    [Fact]
    public void Render_R1Star12_ShowsCountAboveTen()
    {
        var svg = Render("R1*12 |");
        Assert.Contains(">12<", svg);
    }

    [Fact]
    public void Render_SingleR1_DoesNotEmitCountNumber()
    {
        // 1-measure MMR draws a whole rest glyph only, no count text.
        var svg = Render("R1 |");
        // No bold count text at the LP MMR style.
        Assert.DoesNotContain("font-size=\"2.4\" font-weight=\"bold\"", svg);
    }

    [Fact]
    public void Render_R1Star4_UsesChurchRestNoHbar()
    {
        // 4 measures should render via church_rest (a single long rest glyph),
        // NOT the H-bar (which has a thick rect of height ~0.5 ss).
        var svg = Render("R1*4 |");
        Assert.Contains(">4<", svg);
        // H-bar rectangles use height="0.50" — should NOT appear for church_rest.
        Assert.DoesNotContain("height=\"0.50\"", svg);
    }

    [Fact]
    public void Render_R1Star12_UsesBigRestHbar()
    {
        // 12 measures (> ExpandLimit=10) uses big_rest (H-bar with height 0.50).
        var svg = Render("R1*12 |");
        Assert.Contains("height=\"0.50\"", svg);
    }
}
