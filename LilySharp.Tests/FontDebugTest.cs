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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Tests;

/// <summary>
/// Font-face emission modes of the live SVG path (SvgGenerator →
/// SvgDocumentContext.GetFontFaceRule): Preview omits @font-face entirely
/// (the host page injects Emmentaler); Default references the local font.
/// </summary>
[Trait("Category", "Unit")]
public class FontDebugTest
{
    private readonly ITestOutputHelper _output;
    public FontDebugTest(ITestOutputHelper output) => _output = output;

    private static string Render(SvgRenderOptions options)
        => SvgGenerator.Generate(SyntaxTree.Parse("c4 |"), options);

    [Fact]
    public void Preview_OmitsFontFace()
    {
        var svg = Render(SvgRenderOptions.Preview());

        // Preview mode should NOT contain @font-face
        Assert.DoesNotContain("@font-face", svg);
        // But should still reference the font family
        Assert.Contains("font-family: 'Emmentaler'", svg);
    }

    [Fact]
    public void Default_UsesLocalFont()
    {
        var svg = Render(new SvgRenderOptions { EmbedFont = false });

        // Without embedding, the font-face rule falls back to the local font
        Assert.Contains("src: local('Emmentaler')", svg);
    }

    [Fact]
    public void SvgContainsGlyphCharacters()
    {
        var svg = Render(SvgRenderOptions.Preview());

        // Should contain music glyph characters
        Assert.True(svg.Contains(EmmentalerGlyphs.NoteheadBlack) ||
                   svg.Contains(EmmentalerGlyphs.GClef),
                   "SVG should contain music glyph characters");
    }
}
