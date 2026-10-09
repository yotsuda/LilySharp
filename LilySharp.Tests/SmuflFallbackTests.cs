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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LilySharp.Core.Pdf;
using LilySharp.Core.Png;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A glyph the score's music font lacks is drawn from the next font it names, and from
/// Emmentaler when none has it — said once per glyph (docs/smufl-design.md §1, §6 ② ⒟).
/// Leland has no figured-bass digits (nor the styled heads, heel/toe and thumb); Bravura has
/// them.
/// </summary>
public class SmuflFallbackTests
{
    private const string Figures =
        "part m { clef bass }\n" +
        "section A { m { c4@figuredBass(6 4) d4@figuredBass(5) e2 } }\n" +
        "form { A }\n" +
        "score { staff m }\n";

    private static MusicFont Chain(params string[] names)
    {
        var source = "fonts { music " + string.Join(" ", names.Select(n => $"\"{n}\"")) + " }\n" + Figures;
        var tree = SyntaxTree.Parse(source);
        return MusicFonts.Of(SvgGenerator.CollectScore(tree, null).Fonts);
    }

    private static (string Svg, List<string> Warnings) Render(string fonts, bool embed = false)
    {
        var warnings = new List<string>();
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(fonts + "\n" + Figures),
            new SvgRenderOptions { EmbedFont = embed, LayoutWarning = warnings.Add });
        return (svg, warnings);
    }

    [Fact]
    public void TheChain_GivesEachGlyphToTheFirstFontThatHasIt_AndEmmentalerLast()
    {
        var font = Assert.IsType<MusicFontChain>(Chain("Leland"));
        Assert.Equal("Leland", font.Name);
        Assert.Equal(["Leland", "Emmentaler"], font.Members.Select(m => m.Name));
        Assert.Equal("Leland", font.OwnerOf(MusicGlyph.NoteheadBlack).Name);
        Assert.Same(EmmentalerMusicFont.Instance, font.OwnerOf(MusicGlyph.Figbass5));
        // Leland's own glyphs keep Leland's own characters: a score that needs no fallback
        // draws what it drew before the chain.
        Assert.Equal((char) 0xE0A4, font.Codepoint(MusicGlyph.NoteheadBlack));
        // A later font's glyph is a stand-in no font holds, and comes back from it.
        char figure = font.Codepoint(MusicGlyph.Figbass5);
        Assert.InRange(figure, '\uDC00', '\uDFFF');
        Assert.Equal(MusicGlyph.Figbass5, font.GlyphOf(figure));
        var (code, face) = font.Drawn(figure, font.DefaultDesign);
        Assert.Equal(EmmentalerMusicFont.Instance.Codepoint(MusicGlyph.Figbass5), code);
        Assert.Equal("Emmentaler", font.FaceFamily(face));
        Assert.True(font.TryParseFamily("Emmentaler", out int parsed));
        Assert.Equal(face, parsed);
        // Its dimensions are Emmentaler's, at the face it is drawn from.
        Assert.Equal(EmmentalerMusicFont.Instance.FullSize.Advance(MusicGlyph.Figbass5),
            font.FullSize.Advance(MusicGlyph.Figbass5));
    }

    [Fact]
    public void ALelandScore_DrawsItsFiguresInEmmentaler_AndSaysSoOncePerGlyph()
    {
        var (svg, warnings) = Render("fonts { music \"Leland\" }");
        Assert.Contains(".music { font-family: 'Leland', serif; }", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Emmentaler, Leland, serif\"", svg, StringComparison.Ordinal);
        // No stand-in reaches the page.
        Assert.DoesNotContain(svg, c => c is >= '\uD800' and <= '\uDFFF');
        // 6, 4 and 5 — each said once although the score has three figures.
        var said = warnings.Where(w => w.Contains("Leland", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, said.Count);
        Assert.Contains(said, w => w.Contains("figbass6", StringComparison.Ordinal) && w.Contains("Emmentaler", StringComparison.Ordinal));
        Assert.Contains(said, w => w.Contains("figbass4", StringComparison.Ordinal));
        Assert.Contains(said, w => w.Contains("figbass5", StringComparison.Ordinal));
    }

    [Fact]
    public void ANextNamedFont_DrawsWhatTheFirstLacks_AndIsNoFallbackToWarnAbout()
    {
        var (svg, warnings) = Render("fonts { music \"Leland\" \"Bravura\" }");
        Assert.Contains("font-family=\"Bravura, Leland, serif\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Emmentaler,", svg, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AScoreThatNeedsNoFallback_IsDrawnAsBefore_AndSaysNothing()
    {
        const string plain = "part m { clef treble }\nsection A { m { c'4 d' e' f' } }\nform { A }\nscore { staff m }\n";
        var warnings = new List<string>();
        string svg = SvgGenerator.Generate(SyntaxTree.Parse("fonts { music \"Leland\" }\n" + plain),
            new SvgRenderOptions { EmbedFont = false, LayoutWarning = warnings.Add });
        Assert.DoesNotContain("Emmentaler", svg, StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    [Fact]
    public void TheEmbeddedSvg_CarriesTheFallbackFace()
    {
        var (svg, _) = Render("fonts { music \"Leland\" }", embed: true);
        Assert.Contains("@font-face { font-family: 'Leland';", svg, StringComparison.Ordinal);
        Assert.Contains("@font-face { font-family: 'Emmentaler'; src: url('data:font/woff2;base64,", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOtherBackends_DrawTheFallback()
    {
        var tree = SyntaxTree.Parse("fonts { music \"Leland\" }\n" + Figures);
        var warnings = new List<string>();
        byte[] pdf = PdfGenerator.GenerateScore(tree, null, new PdfRenderOptions { LayoutWarning = warnings.Add });
        string text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("Leland", text, StringComparison.Ordinal);
        Assert.Contains("Emmentaler", text, StringComparison.Ordinal);
        Assert.Equal(3, warnings.Count);
        byte[] png = PngGenerator.Generate(tree);
        Assert.True(png.Length > 1000);
        // The boxes name the glyph by the character the face draws, not the stand-in.
        var boxes = BoxesGenerator.GeneratePages(tree, null);
        var glyphs = boxes.SelectMany(p => p.Symbols).Where(b => b.Codepoint is not null).ToList();
        Assert.NotEmpty(glyphs);
        Assert.DoesNotContain(glyphs, b => b.Codepoint is >= 0xD800 and <= 0xDFFF);
        Assert.Contains(glyphs, b => b.Kind == "figuredBass");
    }
}
