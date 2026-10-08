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
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The glyphs a SMuFL font designs to a different convention from Emmentaler's are placed as
/// LilyPond places Emmentaler's (docs/smufl-design.md §6 ④, Lab sessions/p869): a script is
/// centred on its head by its BOX, wherever the font puts its origin; a wiggle repeats every
/// <c>repeatOffset</c>; the arpeggio wiggle, which SMuFL lays down, is stood up.
/// </summary>
public class SmuflPlacementTests
{
    public static TheoryData<string> Fonts => new() { "Emmentaler", "Bravura", "Leland", "Petaluma" };

    private static IReadOnlyList<BoxSymbol> Symbols(string font, string music)
    {
        var tree = SyntaxTree.Parse(
            $"fonts {{ music \"{font}\" }}\n" +
            "part m { clef treble }\n" +
            $"section A {{ m {{ {music} }} }}\n" +
            "form main { A }\n" +
            "score main { staff m }\n");
        return BoxesGenerator.GenerateDocument(tree, RenderSpecParser.FindFirst(tree))
            .Pages.SelectMany(p => p.Symbols).ToList();
    }

    private static double CentreX(BoxSymbol s) => (s.Box[0] + s.Box[2]) / 2;

    [Theory]
    [MemberData(nameof(Fonts))]
    public void AScript_IsCentredOnItsHead_InEveryFont(string font)
    {
        // Half notes, so no stem stands beside the head to pull a script off it.
        foreach (string script in new[] { "fermata", "accent", "marcato", "turn", "prall", "mordent", "upBow" })
        {
            var symbols = Symbols(font, $"b'2@{script} r2");
            var head = Assert.Single(symbols, s => s.Kind == "notehead");
            var mark = Assert.Single(symbols, s => s.Kind is "fermata" or "articulation" or "ornament");
            // The ink's centre is the box's: the metadata box is the outline's (bbox-audit,
            // Lab sessions/p869) — and Emmentaler's scripts are drawn about their origin.
            Assert.True(Math.Abs(CentreX(mark) - CentreX(head)) < 0.02,
                $"{font} @{script}: centre {CentreX(mark):F3}, head {CentreX(head):F3}");
            // …and it stands clear above the head, not beside it.
            Assert.True(mark.Box[3] <= head.Box[1] + 1e-6, $"{font} @{script} reaches into the head");
        }
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void TheArpeggio_StandsBesideTheChord_AsOneUnbrokenWiggle(string font)
    {
        var symbols = Symbols(font, "<c' e' g' c''>2@arpeggio r2");
        var wiggles = symbols.Where(s => s.Kind == "arpeggio").OrderBy(s => s.Box[1]).ToList();
        Assert.True(wiggles.Count >= 2, $"{font}: {wiggles.Count} copies");
        double headLeft = symbols.Where(s => s.Kind == "notehead").Min(s => s.Box[0]);
        foreach (var w in wiggles)
        {
            // Upright: each copy taller than wide, and left of the heads.
            Assert.True(w.Box[3] - w.Box[1] > w.Box[2] - w.Box[0], $"{font}: a copy lies on its side");
            Assert.True(w.Box[2] < headLeft, $"{font}: the wiggle reaches into the chord");
        }
        // One column, and every copy meets the next (the outlines overhang the step).
        Assert.True(wiggles.Max(w => w.Box[0]) - wiggles.Min(w => w.Box[0]) < 0.02, $"{font}: the copies are not stacked");
        for (int i = 1; i < wiggles.Count; i++)
            Assert.True(wiggles[i].Box[1] <= wiggles[i - 1].Box[3] + 1e-3, $"{font}: a gap between copies {i - 1} and {i}");
    }

    [Theory]
    [InlineData("Bravura", 0.948)]
    [InlineData("Leland", 0.88)]
    [InlineData("Petaluma", 1.36)]
    public void TheTrillLine_RepeatsEveryRepeatOffset(string font, double repeatOffset)
    {
        var symbols = Symbols(font, "c'4@startTrillSpan d' e' f' | g'1@stopTrillSpan");
        var elements = symbols.Where(s => s.Kind == "ornament").OrderBy(s => s.Box[0]).Skip(1).ToList();  // past the "tr"
        Assert.True(elements.Count >= 3, $"{font}: {elements.Count} elements");
        for (int i = 1; i < elements.Count; i++)
            Assert.Equal(repeatOffset, elements[i].Box[0] - elements[i - 1].Box[0], 2);
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void FingeringAndFigures_AreReadingSize_InEveryFont(string font)
    {
        // LilyPond's −5 brings Emmentaler's 2-space cut to ~1.12; a SMuFL font's digits are
        // ~1.03 at its own size, and took the −5 a second time (0.58) until p869.
        var symbols = Symbols(font, "c'4@finger(5) d'@figuredBass(5) e'2");
        foreach (string kind in new[] { "fingering", "figuredBass" })
        {
            var digit = Assert.Single(symbols, s => s.Kind == kind);
            double height = digit.Box[3] - digit.Box[1];
            Assert.True(height is > 0.9 and < 1.3, $"{font} {kind}: {height:F3} ss tall");
        }
    }

    [Fact]
    public void AFingering_IsTheScoresFontsDigit_AfterAnotherFontsScore()
    {
        // The one-digit runs are memoised: per music font, or a Bravura score drawn after an
        // Emmentaler one (the preview's whole life) is handed Emmentaler's character.
        Symbols("Emmentaler", "c'4@finger(1) r2.");
        var one = Assert.Single(Symbols("Bravura", "c'4@finger(1) r2."), s => s.Kind == "fingering");
        Assert.Equal(0xED11, one.Codepoint);   // SMuFL fingering1
    }

    [Fact]
    public void TheMultiMeasureRestsCount_ClearsTheStaffByItsInk_InEveryFont()
    {
        // LilyPond places the count by its EXTENT; a SMuFL timeSig digit is centred on its
        // baseline, so placed by the baseline it stood a staff space low (on the rest, in Petaluma).
        double GapOverRest(string font)
        {
            var symbols = Symbols(font, "R1*3");
            var digit = Assert.Single(symbols, s => s.Glyph == "TimeSig3");
            double restTop = symbols.Where(s => s.Kind == "rest").Min(s => s.Box[1]);
            return restTop - digit.Box[3];
        }
        double emmentaler = GapOverRest("Emmentaler");
        foreach (string font in new[] { "Bravura", "Leland", "Petaluma" })
            Assert.True(Math.Abs(GapOverRest(font) - emmentaler) < 0.15,
                $"{font}: the count clears the rest by {GapOverRest(font):F3}, Emmentaler's by {emmentaler:F3}");
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void ToCoda_SetsTheSignBesideTheWord_CentredOnIt(string font)
    {
        var tree = SyntaxTree.Parse(
            $"fonts {{ music \"{font}\" }}\n" +
            "part m { clef treble }\n" +
            "section A { m { c'1 | } }\nsection B { m { d'1 | } }\n" +
            "form main { segno A to coda B ds al coda coda B }\n" +
            "score main { staff m }\n");
        var symbols = BoxesGenerator.GenerateDocument(tree, RenderSpecParser.FindFirst(tree))
            .Pages.SelectMany(p => p.Symbols).ToList();
        var word = Assert.Single(symbols, s => s.Text == "To");
        var sign = symbols.Where(s => s.Glyph == "MarkCoda").OrderBy(s => Math.Abs(s.Box[0] - word.Box[2])).First();
        double gap = sign.Box[0] - word.Box[2];
        Assert.True(gap is > 0.0 and < 0.6, $"{font}: the sign stands {gap:F3} past the word");
        double signMiddle = (sign.Box[1] + sign.Box[3]) / 2, wordMiddle = (word.Box[1] + word.Box[3]) / 2;
        Assert.True(Math.Abs(signMiddle - wordMiddle) < 0.1, $"{font}: the sign's middle is {signMiddle - wordMiddle:F3} off the word's");
    }

    [Fact]
    public void Emmentalers_Wiggles_KeepTheirLilcBoxes()
    {
        // The step is LILC's 1.0 / 0.8 and the arpeggio stands from its origin, so the turned
        // reading is the identity there (the page does not move — the sweep's 0).
        Assert.False(EmmentalerMusicFont.Instance.LiesDown(MusicGlyph.WiggleArpeggiatoUp));
        var trill = EmmentalerMusicFont.Instance.FullSize.Box(MusicGlyph.WiggleTrill);
        Assert.Equal(1.0, trill.Right - trill.Left, 9);
        using (MusicFont.Use(EmmentalerMusicFont.Instance))
            Assert.Equal(EmmentalerMusicFont.Instance.FullSize.Box(MusicGlyph.WiggleArpeggiatoUp), ArpeggioEngraver.WiggleBox);
    }
}
