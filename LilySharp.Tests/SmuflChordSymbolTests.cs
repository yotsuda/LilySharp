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
using System.Linq;
using System.Text;
using LilySharp.Core.Pdf;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord symbol in a SMuFL font is set the way the font means it to be (docs/smufl-design.md
/// §6 ④, Lab sessions/p869): its sharps, flats, △, °, ø and + are the font's own chord-symbol
/// glyphs (<c>csym*</c>) where it has them, and in Petaluma its letters are Petaluma Script, the
/// handwritten face the font is paired with — unless the score names a face for them.
/// </summary>
public class SmuflChordSymbolTests
{
    private const string Book =
        "part m { clef treble }\n" +
        "section A { m { c'2@chord(Bb) d'2@chord(Ebmaj7) | e'2@chord(C#dim) f'2@chord(Caug) | } }\n" +
        "form { A }\n" +
        "score { staff m }\n";

    private static string Svg(string fonts) => LiveRender.SvgFromRenderSpec(fonts + "\n" + Book);

    private static BoxSymbol[] Boxes(string fonts)
    {
        var tree = SyntaxTree.Parse(fonts + "\n" + Book);
        return BoxesGenerator.GenerateDocument(tree, RenderSpecParser.FindFirst(tree))
            .Pages.SelectMany(p => p.Symbols).ToArray();
    }

    [Fact]
    public void APetalumaChordSymbol_IsWrittenInPetalumaScript_WithPetalumasOwnSymbols()
    {
        string svg = Svg("fonts { music \"Petaluma\" }");
        Assert.Contains("font-family=\"Petaluma Script", svg, StringComparison.Ordinal);
        // csymAccidentalFlat / Sharp, csymMajorSeventh, csymDiminished, csymAugmented.
        foreach (char c in new[] { '', '', '', '', '' })
            Assert.Contains($">{c}</text>", svg, StringComparison.Ordinal);
        // …and not the staff's flat (accidentalFlat), which the notes here never need.
        Assert.DoesNotContain("></text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void AFaceTheScoreNames_WinsOverTheCompanion()
    {
        string svg = Svg("fonts { music \"Petaluma\" chord \"TeX Gyre Schola\" }");
        Assert.Contains("font-family=\"TeX Gyre Schola", svg, StringComparison.Ordinal);
        // The binding is the chord symbols' alone: the rehearsal mark keeps the companion
        // (2026-10-09), and nothing else is set in it.
        var companion = System.Text.RegularExpressions.Regex.Matches(svg,
            "<text[^>]*font-family=\"Petaluma Script[^>]*>([^<]*)</text>");
        Assert.All(companion, m => Assert.Equal("A", m.Groups[1].Value));
    }

    /// <summary>The header is set in the companion too unless the score names its face — the
    /// role, its group (<c>header</c>) or the generic family the role follows (owner,
    /// 2026-10-09: "title/poet/composer … music font で描いて").</summary>
    [Fact]
    public void APetalumaHeader_IsWrittenInPetalumaScript_UnlessTheScoreNamesItsFace()
    {
        const string header = "title \"Tune\"\ncomposer \"Someone\"\npoet \"Words\"\nsubtitle \"Sub\"\n";
        static string HeaderSvg(string fonts) => LiveRender.SvgFromRenderSpec(header + fonts + "\n" + Book);
        static bool InCompanion(string svg, string text) => System.Text.RegularExpressions.Regex.IsMatch(
            svg, $"<text[^>]*font-family=\"Petaluma Script[^>]*>{text}</text>");

        string petaluma = HeaderSvg("fonts { music \"Petaluma\" }");
        foreach (var text in new[] { "Tune", "Someone", "Words", "Sub" })
            Assert.True(InCompanion(petaluma, text), text);

        Assert.False(InCompanion(HeaderSvg("fonts { music \"Petaluma\" title \"TeX Gyre Heros\" }"), "Tune"));
        Assert.False(InCompanion(HeaderSvg("fonts { music \"Petaluma\" header \"TeX Gyre Heros\" }"), "Someone"));
        Assert.False(InCompanion(HeaderSvg("fonts { music \"Petaluma\" serif \"TeX Gyre Heros\" }"), "Words"));
        Assert.False(InCompanion(HeaderSvg(""), "Tune"));
    }

    /// <summary>The rehearsal mark is set in the companion too — the serif box was the one
    /// typeset letter left on a handwritten page (owner, 2026-10-09).</summary>
    [Fact]
    public void APetalumaRehearsalMark_IsWrittenInPetalumaScript()
    {
        string svg = Svg("fonts { music \"Petaluma\" }");
        Assert.Matches("<text[^>]*font-family=\"Petaluma Script[^>]*>A</text>", svg);
        Assert.DoesNotMatch("<text[^>]*font-family=\"Petaluma Script[^>]*>A</text>", Svg(""));
    }

    [Fact]
    public void Bravura_KeepsTheEngravingsLetters_AndSetsItsOwnSymbols()
    {
        string svg = Svg("fonts { music \"Bravura\" }");
        Assert.DoesNotContain("Petaluma Script", svg, StringComparison.Ordinal);
        Assert.Contains("TeX Gyre Heros", svg, StringComparison.Ordinal);
        Assert.Contains("></text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Leland_HasNoChordAccidentals_SoItsFlatIsItsAccidental_AndItsTriangleIsItsOwn()
    {
        var symbols = Boxes("fonts { music \"Leland\" }");
        Assert.Contains(symbols, s => s.Glyph == "AccidentalFlat");
        Assert.Contains(symbols, s => s.Glyph == "CsymMajorSeventh" && s.Kind == "chordName");
        Assert.DoesNotContain(symbols, s => s.Glyph == "CsymAccidentalFlat");
    }

    [Fact]
    public void Emmentaler_DrawsNoChordSymbolGlyph()
    {
        var symbols = Boxes("");
        Assert.DoesNotContain(symbols, s => s.Glyph?.StartsWith("Csym", StringComparison.Ordinal) == true);
        Assert.Contains(symbols, s => s.Glyph == "AccidentalFlat");
    }

    [Fact]
    public void TheCompanionFace_TravelsWithThePage()
    {
        // The SVG embeds it (no viewer has it) and the PDF carries its program, `embedded` or not.
        string embedded = SvgGenerator.Generate(SyntaxTree.Parse("fonts { music \"Petaluma\" }\n" + Book),
            new SvgRenderOptions { EmbedFont = true });
        Assert.Contains("@font-face { font-family: 'Petaluma Script'; src: url('data:font/woff2;base64,", embedded, StringComparison.Ordinal);
        byte[] pdf = PdfGenerator.Generate(SyntaxTree.Parse("fonts { music \"Petaluma\" }\n" + Book));
        // The subset's BaseFont, its space written #20 (PDF name syntax).
        Assert.Contains("+Petaluma#20Script", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }
}
