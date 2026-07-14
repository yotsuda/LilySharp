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

    /// <summary>
    /// The SVG fragment for a single MMR count digit. LilyPond's
    /// MultiMeasureRestNumber uses the music-font number glyphs (font-encoding
    /// fetaText), so the count is one or more <c>&lt;text class="music"&gt;</c>
    /// glyph elements — NOT a serif <c>&gt;N&lt;</c> text run. Each digit is its
    /// own glyph element.
    /// </summary>
    private static string CountDigit(int d)
        => $">{EmmentalerGlyphs.GetTimeSigDigit(d)}<";

    [Fact]
    public void Render_R1Star4_ContainsMmrCountText()
    {
        var svg = Render("R1*4 |");
        // The measure count is drawn with the music-font number glyph for '4'.
        Assert.Contains(CountDigit(4), svg);
        // The H-bar (and barlines) draw as filled rects. A black fill is now the SVG
        // default (omitted to shrink the document), so assert the rect renders rather
        // than a literal fill colour.
        Assert.Contains("<rect", svg);
    }

    [Fact]
    public void Render_RegularMusic_DoesNotContainMmrText()
    {
        var svg = Render("c4 d e f |");
        // No MMR span → no MMR count number glyph. (Default 4/4 renders as the
        // Common-time glyph, not digit glyphs, and durations aren't drawn as
        // glyphs, so a count digit could only come from an MMR.)
        Assert.DoesNotContain(CountDigit(4), svg);
    }

    [Fact]
    public void Render_MmrAndMusic_BothPresent()
    {
        var svg = Render("R1*3 c4 d e f |");
        // MMR count "3" (music-font digit) + regular note c4 should both appear.
        Assert.Contains(CountDigit(3), svg);
    }

    [Fact]
    public void Render_R1Star12_ShowsCountAboveTen()
    {
        var svg = Render("R1*12 |");
        // Two-digit count: each digit is its own music-font glyph element.
        Assert.Contains(CountDigit(1), svg);
        Assert.Contains(CountDigit(2), svg);
    }

    [Fact]
    public void Render_SingleR1_DoesNotEmitCountNumber()
    {
        // 1-measure MMR draws a whole rest glyph only, no count number.
        var svg = Render("R1 |");
        Assert.DoesNotContain(CountDigit(1), svg);
    }

    [Fact]
    public void Render_R1Star4_UsesChurchRestNoHbar()
    {
        // 4 measures should render via church_rest (a single long rest glyph),
        // NOT the H-bar (which has a thick rect of height ~0.5 ss).
        var svg = Render("R1*4 |");
        Assert.Contains(CountDigit(4), svg);
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
