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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The open chord entry (HANDOFF §2 E ⑹⑺, the owner's approval 2026-10-06): a quality
/// assembled from its parts, named by LilyPond's Ignatzek rules from its tones. Every name
/// below is LilyPond 2.26.0's for the same tones (MEASURED, Lab sessions/p846/ch: the twin's
/// <c>c:1.…</c> spelling engraved by LilyPond beside the page, 16 of 16 alike).
/// </summary>
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
    [InlineData("C69", "C6add9", 1, "c1:1.3.5.6.9", "0 4 7 9 14")]
    [InlineData("Cmaj7+11", "C△add♯11", 1, "c1:1.3.5.7+.11+", "0 4 7 11 18")]
    [InlineData("Cm7-5-9", "Cm7♭5♭9", 2, "c1:1.3-.5-.7.9-", "0 3 6 10 13")]
    [InlineData("C7omit3", "C7", 1, "c1:1.5.7", "0 7 10")]
    [InlineData("Cadd2", "Csus2add3", 1, "c1:1.2.3.5", "0 2 4 7")]
    [InlineData("Cmmaj9", "Cm△9", 2, "c1:1.3-.5.7+.9", "0 3 7 11 14")]
    [InlineData("C13sus4", "C9sus4add13", 1, "c1:1.4.5.7.9.13", "0 5 7 10 14 21")]
    [InlineData("C7+5+9", "C7♯5♯9", 1, "c1:1.3.5+.7.9+", "0 4 8 10 15")]
    [InlineData("Cm69", "Cm6add9", 2, "c1:1.3-.5.6.9", "0 3 7 9 14")]
    [InlineData("C7sus2", "C7sus2", 1, "c1:1.2.5.7", "0 2 7 10")]
    [InlineData("Cm7add11", "Cm7add11", 2, "c1:1.3-.5.7.11", "0 3 7 10 17")]
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
        Assert.Equal("Cmaj7add♯11", Parse("Cmaj7+11").PrintedSymbol(spelling).Text);
        Assert.Equal("Cmmaj9", Parse("Cmmaj9").PrintedSymbol(spelling).Text);
    }

    [Fact]
    public void LowercaseMinor_TakesTheModifierWithTheRoot()
    {
        var spelling = new ChordSpelling(ChordQualityStyle.Symbols, true);
        var symbol = Parse("Cm7-5-9").PrintedSymbol(spelling);
        Assert.Equal("c7♭5♭9", symbol.Text);
        Assert.Equal(1, symbol.SuperFrom);
    }

    [Fact]
    public void ASlashBassAndARomanDegreeTakeTheSameQualities()
    {
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
