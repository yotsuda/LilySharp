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
using Xunit.Abstractions;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Svg;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
/// <summary>
/// LEGACY-PATH TESTS. Exercises SvgRenderer's per-glyph text metrics, which
/// the live SharedRenderer has not ported yet (it uses length-based estimates).
/// Repoint when real text metrics land in SharedRenderer.
/// </summary>
public class FontDebugTest
{
    private readonly ITestOutputHelper _output;
    public FontDebugTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Preview_OmitsFontFace()
    {
        var tree = SyntaxTree.Parse("c4 |");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var layout = new LayoutEngine().Layout(score);

        var renderer = new SvgRenderer(renderOptions: SvgRenderOptions.Preview());
        var svg = renderer.Render(score, layout);

        // Preview mode should NOT contain @font-face
        Assert.DoesNotContain("@font-face", svg);
        // But should still reference the font family
        Assert.Contains("font-family: 'Emmentaler'", svg);
    }

    [Fact]
    public void Default_UsesLocalFont()
    {
        var tree = SyntaxTree.Parse("c4 |");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var layout = new LayoutEngine().Layout(score);

        var renderer = new SvgRenderer(renderOptions: SvgRenderOptions.Default);
        var svg = renderer.Render(score, layout);

        // Default mode should reference local font
        Assert.Contains("src: local('Emmentaler')", svg);
    }

    [Fact]
    public void SvgContainsGlyphCharacters()
    {
        var tree = SyntaxTree.Parse("c4 |");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var layout = new LayoutEngine().Layout(score);

        var renderer = new SvgRenderer(renderOptions: SvgRenderOptions.Preview());
        var svg = renderer.Render(score, layout);

        // Should contain music glyph characters
        Assert.True(svg.Contains(EmmentalerGlyphs.NoteheadBlack) ||
                   svg.Contains(EmmentalerGlyphs.GClef),
                   "SVG should contain music glyph characters");
    }
}
