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
using System.Text;
using LilySharp.Core.Pdf;
using LilySharp.Core.Png;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A score that names a music font is engraved in it (docs/smufl-design.md §6 ② ⒝) — and a
/// score that names none is engraved in Emmentaler exactly as before, whatever was drawn in
/// between (⒞: the character-keyed caches carry the font).
/// </summary>
public class SmuflRenderTests
{
    private const string Book =
        "part m { clef treble }\n" +
        "section A { m { c'4 d'8 e'8 fis'2 | g'1 } }\n" +
        "form { A }\n" +
        "score { staff m }\n";

    private const string GrandStaff =
        "part rh { clef treble }\npart lh { clef bass }\n" +
        "section A { rh { c''4 d'' e'' f'' } lh { c4 d e f } }\n" +
        "form { A }\n" +
        "score { grandStaff { staff rh staff lh } }\n";

    private static string Svg(string source) => LiveRender.SvgFromRenderSpec(source);

    [Fact]
    public void ABravuraScore_IsDrawnInBravura()
    {
        string svg = Svg("fonts { music \"Bravura\" }\n" + Book);
        Assert.Contains(".music { font-family: 'Bravura', serif; }", svg, StringComparison.Ordinal);
        // The G clef is SMuFL's gClef, not Emmentaler's clefs.G; the sharp is accidentalSharp.
        Assert.Contains("", svg, StringComparison.Ordinal);
        Assert.Contains("", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Emmentaler", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmmentalerScore_IsUnchanged_ByABravuraRenderBetweenTwoOfIts()
    {
        string before = Svg(Book);
        Assert.Contains("'Emmentaler'", before, StringComparison.Ordinal);
        string bravura = Svg("fonts { music \"Bravura\" }\n" + Book);
        Assert.NotEqual(before, bravura);
        string after = Svg(Book);
        Assert.Equal(before, after);
        // …and the other way round: Bravura twice, with Emmentaler between, is one picture.
        Assert.Equal(bravura, Svg("fonts { music \"Bravura\" }\n" + Book));
    }

    [Fact]
    public void TheSetting_DrawsInTheNamedFont_OverTheFiles()
    {
        var tree = SyntaxTree.Parse("fonts { music \"Bravura\" }\n" + Book);
        var leland = PaperOverrides.Parse(["music=Leland"], out var error);
        Assert.Null(error);
        string svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false, PaperOverrides = leland });
        Assert.Contains("'Leland'", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("'Bravura'", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameFoundNowhere_IsEngravedInEmmentaler()
    {
        string svg = Svg("fonts { music \"NoSuchFont\" \"Petaluma\" }\n" + Book);
        // The chain's next name is used — Petaluma — not Emmentaler, since one name is found.
        Assert.Contains("'Petaluma'", svg, StringComparison.Ordinal);
        string none = Svg("fonts { music \"NoSuchFont\" }\n" + Book);
        Assert.Contains("'Emmentaler'", none, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmbeddedSvg_CarriesTheFontsOwnFile()
    {
        string bravura = SvgGenerator.Generate(SyntaxTree.Parse("fonts { music \"Bravura\" }\n" + Book),
            new SvgRenderOptions { EmbedFont = true });
        Assert.Contains("@font-face { font-family: 'Bravura'; src: url('data:font/woff2;base64,", bravura, StringComparison.Ordinal);
        // Leland publishes no WOFF2: the OTF is embedded, declared as what it is.
        string leland = SvgGenerator.Generate(SyntaxTree.Parse("fonts { music \"Leland\" }\n" + Book),
            new SvgRenderOptions { EmbedFont = true });
        Assert.Contains("@font-face { font-family: 'Leland'; src: url('data:font/opentype;base64,", leland, StringComparison.Ordinal);
        Assert.Contains("format('opentype')", leland, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOtherBackends_DrawABravuraScore()
    {
        var tree = SyntaxTree.Parse("fonts { music \"Bravura\" }\n" + Book);
        byte[] png = PngGenerator.Generate(tree);
        Assert.True(png.Length > 1000);
        byte[] pdf = PdfGenerator.Generate(tree);
        Assert.True(pdf.Length > 1000);
        // The subset font the PDF carries is Bravura's, named as such.
        Assert.Contains("Bravura", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Assert.DoesNotContain("Emmentaler-", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void AGrandStaff_DrawsTheSmuflBrace_ScaledToTheSpan()
    {
        string svg = Svg("fonts { music \"Bravura\" }\n" + GrandStaff);
        Assert.DoesNotContain("Emmentaler-Brace", svg, StringComparison.Ordinal);
        // SMuFL's one brace (U+E000), drawn in the music face at a size that spans the staves —
        // many times the glyphs' 4.0.
        var brace = System.Text.RegularExpressions.Regex.Match(svg,
            "<text class=\"music\"[^>]*font-size=\"(?<size>[\\d.]+)\"[^>]*></text>");
        Assert.True(brace.Success, "no SMuFL brace was drawn:\n" + svg);
        Assert.True(double.Parse(brace.Groups["size"].Value, System.Globalization.CultureInfo.InvariantCulture) > 8.0);
        // …while Emmentaler's grand staff keeps its ladder rung in the brace face.
        Assert.Contains("Emmentaler-Brace", Svg(GrandStaff), StringComparison.Ordinal);
    }
}
