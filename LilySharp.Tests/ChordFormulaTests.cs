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

using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The open chord entry (HANDOFF §2 E ⑹⑺, the owner's approval 2026-10-06): a quality
/// assembled from its parts, and named from its tones.
/// </summary>
/// <remarks>
/// ⚠️ THE NAMES ARE THE COMMON JAZZ SPELLING SINCE 2026-11-13 (grammar evaluation 4) — the
/// owner's decision, in the owner's words 「コード記号は、ジャズの一般的な綴りにして」, which
/// OVERRIDES the one this class recorded until then ("named by LilyPond's Ignatzek rules
/// from its tones. Every name below is LilyPond 2.26.0's for the same tones — MEASURED, Lab
/// sessions/p846/ch, 16 of 16 alike"). The big band writer's chart (eval4 bigband/r-08)
/// printed <c>C7(#9, b13)</c> as "C7♯9add♭13", <c>Bbmaj7(#11)</c> as "B♭△add♯11",
/// <c>F6(9)</c> as "F6add9" and <c>C13(b9)</c> as "C7♭9add13" — LilyPond's picture exactly
/// (eval4 fixB/lp/r08-crop.png), and no chart's. The rules are
/// <see cref="ChordFormula.NameOf"/>'s; the names pinned before that day are kept in the
/// trailing comments, and the whole before/after table (236 chords) is Lab
/// sessions/p1226/md/eval4/fixB2/chord-names-before-after.md. The TONES and the twin's
/// <c>c:1.…</c> spelling did not move: LilyPond still names the twin by its own rules.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ChordFormulaTests
{
    private static ChordStructure Parse(string symbol)
    {
        Assert.True(ChordStructure.TryParseChordEntry(symbol, out var chord), symbol + " should parse");
        return chord;
    }

    private static ChordSymbolText Symbols(string symbol) => Parse(symbol).PrintedSymbol(ChordSpelling.Default);

    [Theory]
    [InlineData("C9sus4", "C9sus4", 1, "c1:1.4.5.7.9", "0 5 7 10 14")]
    [InlineData("C7alt", "Calt", 1, "c1:1.3.5.7.9-.10-.11+.13-", "0 4 7 10 13 15 18 20")]
    [InlineData("C5", "C5", 1, "c1:1.5", "0 7")]
    [InlineData("C69", "C6/9", 1, "c1:1.3.5.6.9", "0 4 7 9 14")]                      // was C6add9
    [InlineData("Cmaj7+11", "C△7(♯11)", 1, "c1:1.3.5.7+.11+", "0 4 7 11 18")]         // was C△add♯11
    [InlineData("Cm7-5-9", "Cm7(♭5♭9)", 2, "c1:1.3-.5-.7.9-", "0 3 6 10 13")]         // was Cm7♭5♭9
    [InlineData("C7omit3", "C7", 1, "c1:1.5.7", "0 7 10")]
    [InlineData("Cadd2", "Cadd2", 1, "c1:1.2.3.5", "0 2 4 7")]                        // was Csus2add3
    [InlineData("Cmmaj9", "Cm△9", 2, "c1:1.3-.5.7+.9", "0 3 7 11 14")]
    [InlineData("C13sus4", "C13sus4", 1, "c1:1.4.5.7.9.13", "0 5 7 10 14 21")]        // was C9sus4add13
    [InlineData("C7+5+9", "C7(♯5♯9)", 1, "c1:1.3.5+.7.9+", "0 4 8 10 15")]            // was C7♯5♯9
    [InlineData("Cm69", "Cm6/9", 2, "c1:1.3-.5.6.9", "0 3 7 9 14")]                   // was Cm6add9
    [InlineData("C7sus2", "C7sus2", 1, "c1:1.2.5.7", "0 2 7 10")]
    [InlineData("Cm7add11", "Cm11", 2, "c1:1.3-.5.7.11", "0 3 7 10 17")]              // was Cm7add11
    // The big band writer's four (eval4 bigband/r-08), and the rules' other arms.
    [InlineData("C7(#9, b13)", "C7(♯9♭13)", 1, "c1:1.3.5.7.9+.13-", "0 4 7 10 15 20")]   // was C7♯9add♭13
    [InlineData("Cmaj7(#11)", "C△7(♯11)", 1, "c1:1.3.5.7+.11+", "0 4 7 11 18")]
    [InlineData("C6(9)", "C6/9", 1, "c1:1.3.5.6.9", "0 4 7 9 14")]
    [InlineData("C13(b9)", "C13(♭9)", 1, "c1:1.3.5.7.9-.13", "0 4 7 10 13 21")]          // was C7♭9add13
    [InlineData("C13(#11)", "C13(♯11)", 1, "c1:1.3.5.7.9.11+.13", "0 4 7 10 14 18 21")]  // was C7♯1113
    [InlineData("C7(b9, #11, b13)", "C7(♭9♯11♭13)", 1, "c1:1.3.5.7.9-.11+.13-", "0 4 7 10 13 18 20")]
    [InlineData("C9(#11)", "C9(♯11)", 1, "c1:1.3.5.7.9.11+", "0 4 7 10 14 18")]          // was C7♯11
    [InlineData("Cm(add9)", "Cm(add9)", 2, "c1:1.3-.5.9", "0 3 7 14")]                   // was Cmadd9
    [InlineData("Cadd11", "Cadd11", 1, "c1:1.3.5.11", "0 4 7 17")]
    [InlineData("C7add13", "C13", 1, "c1:1.3.5.7.13", "0 4 7 10 21")]                    // was C7add13
    [InlineData("Cmmaj13", "Cm△13", 2, "c1:1.3-.5.7+.9.13", "0 3 7 11 14 21")]           // was Cm△9add13
    [InlineData("C7sus4(b9)", "C7sus4(♭9)", 1, "c1:1.4.5.7.9-", "0 5 7 10 13")]          // was C7♭9sus4
    [InlineData("C(b5)", "C(♭5)", 1, "c1:1.3.5-", "0 4 6")]                              // was C♭5
    public void AnAssembledQuality_IsNamedAndWrittenFromItsTones(
        string entry, string printed, int superFrom, string chordMode, string semitones)
    {
        var chord = Parse(entry);
        Assert.Equal(ChordQuality.Composed, chord.Quality);
        var symbol = chord.PrintedSymbol(ChordSpelling.Default);
        Assert.Equal(printed, symbol.Text);
        Assert.Equal(superFrom, symbol.SuperFrom);
        Assert.Equal(chordMode, chord.ToChordMode("1"));
        Assert.Equal(semitones, string.Join(" ", chord.Intervals));
    }

    /// <summary>The power chord and the altered chord keep their roots' accidentals: the
    /// <c>b</c> after a root is its flat, which is why every tension is spelled + / -.</summary>
    [Theory]
    [InlineData("Bb5", "B♭5")]
    [InlineData("F#7alt", "F♯alt")]
    public void TheRootKeepsItsAccidental(string entry, string printed)
        => Assert.Equal(printed, Symbols(entry).Text);

    /// <summary>A spelling whose tones a registered quality has IS that quality — its table
    /// name, its twin spelling — however it was assembled.</summary>
    [Theory]
    [InlineData("C+5", ChordQuality.Augmented)]
    [InlineData("C7add9", ChordQuality.Dominant9)]
    [InlineData("Cmaj", ChordQuality.Major7)]
    [InlineData("Cm7-5", ChordQuality.HalfDiminished7)]
    [InlineData("C9sus", ChordQuality.Composed)]
    [InlineData("Csus4add9", ChordQuality.Composed)]
    public void ASpellingOfARegisteredToneSet_IsThatQuality(string entry, ChordQuality expected)
    {
        var chord = Parse(entry);
        Assert.Equal(expected, chord.Quality);
        Assert.Equal(expected == ChordQuality.Composed, chord.Formula != null);
    }

    [Theory]
    [InlineData("Cm5")]         // a power chord has no third to make minor
    [InlineData("C7-9+9")]      // both ninths are alt's: LilyPond holds one pitch a step
    [InlineData("C9alt")]       // alt is the seventh's
    [InlineData("Cmsus4")]      // a sus chord has no third to make minor
    [InlineData("Cdimmaj7")]
    [InlineData("C7sus4sus4")]  // each modifier once
    [InlineData("C7#9")]        // '#' belongs to the root and the bass alone
    [InlineData("Cadd9add9")]
    [InlineData("C9add9")]      // the step is already there
    public void NotAQuality(string entry)
        => Assert.False(ChordStructure.TryParseChordEntry(entry, out _), entry + " should not parse");

    [Fact]
    public void UnderWords_TheTriangleIsTheWord()
    {
        var spelling = new ChordSpelling(ChordQualityStyle.Words, false);
        Assert.Equal("Cmaj7(♯11)", Parse("Cmaj7+11").PrintedSymbol(spelling).Text);
        // After the minor `m`, a space and the word — the registered Cm maj7's spelling, which
        // an assembled minor-major chord now shares (it printed Cmmaj9 until 2026-11-13).
        Assert.Equal("Cm maj9", Parse("Cmmaj9").PrintedSymbol(spelling).Text);
        Assert.Equal("Cm maj7", Parse("Cmmaj7").PrintedSymbol(spelling).Text);
    }

    /// <summary>The registered qualities are named by the same rules as the assembled ones —
    /// one builder since 2026-11-13, where two tables stood: the major seventh carries its
    /// number after the triangle, a registered alteration its parentheses.</summary>
    [Theory]
    [InlineData("Cmaj7", "C△7", "Cmaj7")]        // was C△
    [InlineData("Cmaj9", "C△9", "Cmaj9")]
    [InlineData("Cmaj13", "C△13", "Cmaj13")]
    [InlineData("Cmmaj7", "Cm△7", "Cm maj7")]    // was Cm△
    [InlineData("C7-5", "C7(♭5)", "C7(♭5)")]     // was C7♭5
    [InlineData("C7+5", "C7(♯5)", "C7(♯5)")]
    [InlineData("C7-9", "C7(♭9)", "C7(♭9)")]
    [InlineData("C7+9", "C7(♯9)", "C7(♯9)")]
    [InlineData("C7+11", "C7(♯11)", "C7(♯11)")]
    [InlineData("Cm7-5", "Cø", "Cm7(♭5)")]       // words: was Cm7♭5
    [InlineData("Cdim", "C°", "Cdim")]
    [InlineData("Cdim7", "C°7", "Cdim7")]
    [InlineData("Caug", "C+", "Caug")]
    [InlineData("C6", "C6", "C6")]
    [InlineData("Cadd9", "Cadd9", "Cadd9")]
    [InlineData("C7sus4", "C7sus4", "C7sus4")]
    [InlineData("Csus2", "Csus2", "Csus2")]
    [InlineData("Cm11", "Cm11", "Cm11")]
    [InlineData("C13", "C13", "C13")]
    public void ARegisteredQuality_IsNamedByTheSameRules(string entry, string symbols, string words)
    {
        var chord = Parse(entry);
        Assert.Null(chord.Formula);
        Assert.Equal(symbols, chord.PrintedSymbol(ChordSpelling.Default).Text);
        Assert.Equal(words, chord.PrintedSymbol(ChordSpelling.Canonical).Text);
    }

    /// <summary>The 6/9 chord is written with a slash and is no slash chord: the entry reads
    /// it back as it prints, a bass comes after it, and the bass's slash is the one a reader
    /// of the printed name finds (<see cref="ChordSymbolText.BassSlash"/>).</summary>
    [Fact]
    public void TheSixNine_IsNotASlashChord()
    {
        Assert.Equal(Parse("C69"), Parse("C6/9"));
        var overBass = Parse("F6/9/A");
        Assert.Equal(Parse("F69/A"), overBass);
        Assert.Equal(5, overBass.BassStep);
        string printed = overBass.PrintedSymbol(ChordSpelling.Default).Text;
        Assert.Equal("F6/9/A", printed);
        Assert.Equal(4, ChordSymbolText.BassSlash(printed));
        Assert.Equal(-1, ChordSymbolText.BassSlash("F6/9"));
        Assert.Equal(2, ChordSymbolText.BassSlash("C6/E"));
    }

    /// <summary>Everything between the root and the bass is ONE raised run — the 6/9 with its
    /// slash, the alteration list with its parentheses — and under a capo's <c>both</c> the
    /// bracketed name's run ends at the bracket's own <c>)</c>, not at the list's.</summary>
    [Fact]
    public void TheSixNineAndTheParentheses_StandInTheRaisedRun()
    {
        static (string Text, (string Text, bool Up)[] Pieces) Drawn(string head, string entry)
        {
            string book = head + "part m { clef treble }\nsection A { m { c'4 d e f | } chords prog { " + entry
                + " | } }\nform { A }\nscore { chords prog  staff m }\n";
            var tree = SyntaxTree.Parse(book);
            Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            var item = Assert.Single(score.ChordNames);
            double up = ChordNameGlyphRun.SuperRaise(score.TextMetrics);
            return (item.ChordText, ChordNameGlyphRun
                .Pieces(score.TextMetrics, item.ChordText, item.SuperFrom, item.BracketSuperFrom)
                .Select(p => (p.Text, p.Raise > up / 2)).ToArray());
        }

        var sixNine = Drawn("", "F6(9)/A");
        Assert.Equal("F6/9/A", sixNine.Text);
        Assert.Equal(new[] { ("F", false), ("6/9", true), ("/A", false) }, sixNine.Pieces);

        var altered = Drawn("", "C7(b9, #11)");
        Assert.Equal("C7(♭9♯11)", altered.Text);
        Assert.Equal(new[] { ("C", false), ("7(", true), ("", true), ("9", true), ("", true), ("11)", true) }, altered.Pieces);

        var both = Drawn("capo 3\nlayout { chordNames both }\n", "Eb7(b9)");
        Assert.Equal("E♭7(♭9) (C7(♭9))", both.Text);
        Assert.Equal(
            new[] { ("E", false), ("", false), ("7(", true), ("", true), ("9)", true), (" (C", false), ("7(", true), ("", true), ("9)", true), (")", false) },
            both.Pieces);
    }

    [Fact]
    public void LowercaseMinor_TakesTheModifierWithTheRoot()
    {
        var spelling = new ChordSpelling(ChordQualityStyle.Symbols, true);
        var symbol = Parse("Cm7-5-9").PrintedSymbol(spelling);
        Assert.Equal("c7(♭5♭9)", symbol.Text);
        Assert.Equal(1, symbol.SuperFrom);
    }

    [Fact]
    public void ASlashBassAndARomanDegreeTakeTheSameQualities()
    {
        Assert.True(ChordStructure.TryParseRomanEntry("I6/9", 0, 0, out var sixNine));
        Assert.Equal("I6/9", sixNine.ToRomanNumeral(0, 0));
        Assert.True(ChordStructure.TryParseRomanEntry("V7(b9)", 0, 0, out var altered));
        Assert.Equal("V7(♭9)", altered.ToRomanNumeral(0, 0));
        Assert.Equal("C9sus4/B♭", Symbols("C9sus4/Bb").Text);
        Assert.True(ChordStructure.TryParseRomanEntry("V9sus4", 0, 0, out var roman));
        Assert.Equal(ChordQuality.Composed, roman.Quality);
        Assert.Equal("V9sus4", roman.ToRomanNumeral(0, 0));
    }

    /// <summary>Two spellings of one tone set are one chord — what a chords row compares to
    /// leave a repeated chord unprinted.</summary>
    [Fact]
    public void TwoSpellingsOfOneToneSetAreEqual()
    {
        Assert.Equal(Parse("C9sus4"), Parse("C9sus"));
        Assert.NotEqual(Parse("C9sus4"), Parse("C9sus2"));
    }

    [Fact]
    public void TheSpelledTonesAreTheChordsNotes()
        => Assert.Equal("<c e g bes des ees fis aes>", Parse("C7alt").ToNoteChord());
}
