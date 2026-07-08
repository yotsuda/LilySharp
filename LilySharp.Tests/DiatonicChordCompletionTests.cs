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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The '@chord(…)' completion offers the current key's diatonic chords — each
/// degree's triad, seventh, and suspended chords — labelled as chord symbols
/// (C, Cmaj7, Csus4, …) but inserting the shared chords{}-style form (c, c:maj7).
/// </summary>
[Trait("Category", "Unit")]
public class DiatonicChordCompletionTests
{
    private static Microsoft.VisualStudio.LanguageServer.Protocol.CompletionItem[] Items(string text)
    {
        int offset = text.IndexOf("()") + 1;   // caret between the parens
        Assert.True(LilySharpLanguageServer.IsInsideChordAnnotation(text, offset));
        return LilySharpLanguageServer.GetDiatonicChordCompletions(text, offset).Items;
    }

    private static string[] Labels(string text) => Items(text).Select(i => i.Label).ToArray();

    [Fact]
    public void CMajor_OffersTriadsSeventhsAndSus()
    {
        var labels = Labels("key c major\nc'4@chord()");
        foreach (var triad in new[] { "C", "Dm", "Em", "F", "G", "Am", "Bdim" })
            Assert.Contains(triad, labels);
        foreach (var seventh in new[] { "Cmaj7", "Dm7", "Em7", "Fmaj7", "G7", "Am7", "Bm7b5" })
            Assert.Contains(seventh, labels);
        Assert.Contains("Csus4", labels);
        Assert.Contains("Gsus2", labels);
    }

    [Fact]
    public void InsertsSharedChordsBlockForm()
    {
        var items = Items("key c major\nc'4@chord()");
        string Insert(string label) => items.First(i => i.Label == label).InsertText;
        Assert.Equal("c", Insert("C"));          // major triad: bare root
        Assert.Equal("d:m", Insert("Dm"));       // minor triad
        Assert.Equal("g:7", Insert("G7"));       // dominant seventh
        Assert.Equal("d:m7", Insert("Dm7"));
        Assert.Equal("b:m7b5", Insert("Bm7b5")); // half-diminished vii
        Assert.Equal("c:sus4", Insert("Csus4"));
    }

    [Fact]
    public void DMajor_SpellsSharps()
    {
        var labels = Labels("key d major\nc'4@chord()");
        foreach (var triad in new[] { "D", "Em", "F#m", "G", "A", "Bm", "C#dim" })
            Assert.Contains(triad, labels);
        Assert.Contains("C#m7b5", labels);   // vii of D major
    }

    [Fact]
    public void BbMajor_SpellsFlats()
    {
        var labels = Labels("key bes major\nc'4@chord()");
        foreach (var triad in new[] { "Bb", "Cm", "Dm", "Eb", "F", "Gm", "Adim" })
            Assert.Contains(triad, labels);
        Assert.Contains("Bbmaj7", labels);
    }

    [Fact]
    public void AMinor()
    {
        var labels = Labels("key a minor\nc'4@chord()");
        foreach (var triad in new[] { "Am", "Bdim", "C", "Dm", "Em", "F", "G" })
            Assert.Contains(triad, labels);
    }

    [Fact]
    public void NoKey_DefaultsToCMajor()
        => Assert.Contains("C", Labels("c'4@chord()"));

    [Fact]
    public void IsInsideChordAnnotation_TracksPosition()
    {
        var t = "c'4@chord(c:m) d'4@fig(6)";
        Assert.True(LilySharpLanguageServer.IsInsideChordAnnotation(t, t.IndexOf("c:m") + 1));   // inside @chord
        Assert.False(LilySharpLanguageServer.IsInsideChordAnnotation(t, t.IndexOf(") d") + 2)); // after ')'
        Assert.False(LilySharpLanguageServer.IsInsideChordAnnotation(t, t.IndexOf("6)")));       // inside @fig, not @chord
    }
}
