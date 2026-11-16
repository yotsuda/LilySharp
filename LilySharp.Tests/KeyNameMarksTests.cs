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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The key in words over a score whose staves cannot show a signature (owner's request
/// 2026-11-16): every staff one to four lines — the chord chart's one-line staff, and two-,
/// three- and four-line staves — prints "Key: D" over the first bar and again where the key
/// changes. A five-line, tab or drum staff in the score, and it prints none.
/// </summary>
[Trait("Category", "Unit")]
public class KeyNameMarksTests
{
    private static string Book(string scoreLine, string home = "key d major", string later = "key f major") => $$"""
        title "K"
        time 4/4
        {{home}}
        part l { }
        section A {
          chords h {
            D | G | D | A |
          }
        }
        section B {
          {{later}}
          chords h {
            F | C | F | C |
          }
        }
        form { A B }
        score { {{scoreLine}} }
        """;

    private static string Svg(string book) => SvgGenerator.Generate(SyntaxTree.Parse(book), SvgRenderOptions.Default);

    private static (string Text, double X)[] KeyWords(string svg) =>
        Regex.Matches(svg, @"<text x=""([^""]+)""[^>]*>(Key: [^<]*)<").Cast<Match>()
            .Select(m => (m.Groups[2].Value, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))).ToArray();

    private static double ChordX(string svg, string name, int nth) =>
        Regex.Matches(svg, $@"<text x=""([^""]+)""[^>]*TeX Gyre Heros[^>]*>{name}<").Cast<Match>()
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ElementAt(nth);

    [Theory]
    [InlineData("chords h")]                          // the automatic one-line staff
    [InlineData("chords h  staff l as lines 1")]
    [InlineData("chords h  staff l as lines 2")]
    [InlineData("chords h  staff l as lines 3")]
    [InlineData("chords h  staff l as lines 4")]
    public void AStaffOfOneToFourLines_PrintsTheKeyInWords_AtTheStartAndTheChange(string scoreLine)
    {
        string svg = Svg(Book(scoreLine));
        var words = KeyWords(svg);
        Assert.Equal(new[] { "Key: D", "Key: F" }, words.Select(w => w.Text));
        // The change stands over its own bar: the column of section B's first chord.
        Assert.Equal(ChordX(svg, "F", 0), words[1].X, 2);
    }

    [Theory]
    [InlineData("chords h  staff l")]                 // five lines: the signature says it
    [InlineData("chords h  staff l as lines 1  staff l as lines 5")]
    public void AScoreWithAFiveLineStaff_PrintsNone(string scoreLine)
        => Assert.Empty(KeyWords(Svg(Book(scoreLine))));

    [Fact]
    public void AMinorKey_IsNamedAsAChordSymbol()
    {
        var words = KeyWords(Svg(Book("chords h", "key b minor", "key g minor")));
        Assert.Equal(new[] { "Key: Bm", "Key: Gm" }, words.Select(w => w.Text));
    }

    [Theory]
    [InlineData("d major", "D")]
    [InlineData("b minor", "Bm")]
    [InlineData("bes major", "Bb")]
    [InlineData("fis major", "F#")]
    [InlineData("ees minor", "Ebm")]
    [InlineData("c major", "C")]
    [InlineData("a minor", "Am")]
    [InlineData("custom fis cis", null)]
    [InlineData("none", null)]
    public void TheName_IsTheTonicAsAChordSymbolsRoot(string key, string? name)
    {
        var syntax = SyntaxTree.Parse($"key {key}\n").GetRoot().DescendantNodes().OfType<KeySignatureSyntax>().First();
        Assert.Equal(name, KeyNameMarks.NameOf(syntax));
    }

    /// <summary>A section without a key of its own after one in another key is back in the
    /// opening key, and says so; a section in the key already in force says nothing.</summary>
    [Fact]
    public void TheReturnToTheOpeningKey_IsNamed_AndNoChangeIsNot()
    {
        string book = """
            time 4/4
            key d major
            section A { chords h { D | D | } }
            section B { key f major  chords h { F | F | } }
            section C { key d major  chords h { D | D | } }
            form { A B A C }
            score { chords h }
            """;
        Assert.Equal(new[] { "Key: D", "Key: F", "Key: D" }, KeyWords(Svg(book)).Select(w => w.Text));
    }

    /// <summary>A one-line staff draws no key signature at the start — LilyPond's RhythmicStaff
    /// removes the Key_engraver — and, since the key's words came, no change either: it drew the
    /// change's naturals and flat on its line.</summary>
    [Fact]
    public void AOneLineStaff_DrawsNoKeyChange()
    {
        // The change's glyphs carry its tonic's place (the f of `key f major`).
        string book1 = Book("chords h  staff l as lines 1"), book2 = Book("chords h  staff l as lines 2");
        string Glyphs(string book) => $@"<text class=""music""[^>]*data-pos=""{book.IndexOf("key f major", System.StringComparison.Ordinal) + 4}""";
        Assert.DoesNotMatch(Glyphs(book1), Svg(book1));
        Assert.Matches(Glyphs(book2), Svg(book2));
    }
}
