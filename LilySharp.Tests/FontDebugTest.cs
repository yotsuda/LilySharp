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

    // ---- the words' faces, cut to the characters drawn (2026-11-19) -----------------------

    private const string Song = """
        title "Morning"
        time 4/4
        part m { clef treble }
        section A {
          m { c'4 d' e' f' | }
          lyrics words { Write a line of | }
        }
        form { A }
        score { staff m  lyrics words on m }
        """;

    private static (string Style, byte[] Font)[] TextFaces(string svg)
        => [.. System.Text.RegularExpressions.Regex.Matches(svg,
                @"@font-face \{ font-family: 'TeX Gyre Schola'; font-weight: (\w+); font-style: (\w+); src: url\('data:font/otf;base64,([A-Za-z0-9+/=]+)'\) format\('opentype'\); \}")
            .Select(m => (m.Groups[1].Value + "/" + m.Groups[2].Value, System.Convert.FromBase64String(m.Groups[3].Value)))];

    /// <summary>
    /// An exported SVG carries the bundled text face its words are set in, cut down to the
    /// characters drawn: the layout spaced the words by that face's widths, and a viewer's own
    /// serif ran two lyric words together (owner's report 2026-11-19, the site's home page).
    /// </summary>
    [Fact]
    public void Embedded_CarriesTheWordsFace_CutToTheCharactersDrawn()
    {
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(Song), new SvgRenderOptions { EmbedFont = true });
        var faces = TextFaces(svg);
        var regular = Assert.Single(faces, f => f.Style == "normal/normal").Font;
        Assert.Contains(faces, f => f.Style == "bold/normal");   // the title
        // An OpenType font still, and a small one: the whole file is 140,596 bytes.
        Assert.Equal("OTTO", System.Text.Encoding.ASCII.GetString(regular, 0, 4));
        Assert.InRange(regular.Length, 1000, 12000);
        // The same score, the same bytes.
        Assert.Equal(svg, SvgGenerator.Generate(SyntaxTree.Parse(Song), new SvgRenderOptions { EmbedFont = true }));
    }

    [Fact]
    public void NotEmbedded_AndPreview_CarryNoTextFace()
    {
        Assert.Empty(TextFaces(SvgGenerator.Generate(SyntaxTree.Parse(Song), new SvgRenderOptions { EmbedFont = false })));
        Assert.Empty(TextFaces(SvgGenerator.Generate(SyntaxTree.Parse(Song), SvgRenderOptions.Preview())));
    }
}
