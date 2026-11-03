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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// One pitch twice in a chord (owner's report and decision 2026-11-03, a triad's note dragged onto
/// another): <c>&lt;a b b&gt;</c> took LilyPond's left, right, left, and laid the second b on the a.
/// </summary>
[Trait("Category", "Unit")]
public class ChordUnisonTests
{
    private static ChordNoteInfo Note(int position, int midi, string? accidental = null)
        => new(position, accidental, Midi: midi);

    private static double[] Offsets(params ChordNoteInfo[] notes) => ChordHeadPositioning.CalculateOffsets(notes, stemUp: true, noteValue: 2);

    [Fact]
    public void AUnisonBesideASecond_IsOneHead_NotAHeadOnTheSecond()
    {
        var o = Offsets(Note(0, 71), Note(-1, 69), Note(0, 71));
        Assert.Equal(0, o[1]);          // the a, on the stem's side
        Assert.True(o[0] > 0);          // a b across the stem
        Assert.Equal(o[0], o[2]);       // and the other b on it
    }

    // Only the first prints the flat: <a bes bes>.
    [Fact]
    public void TheAccidentalOnTheFirstAlone_IsStillOnePitch()
    {
        var o = Offsets(Note(-1, 69), Note(0, 70, "accidentals.flat"), Note(0, 70));
        Assert.Equal(0, o[0]);
        Assert.True(o[1] > 0);
        Assert.Equal(o[1], o[2]);
    }

    // What NoteCollision asks, by position and no pitch: told by the accidental.
    [Fact]
    public void WithNoPitchToRead_TheAccidentalTells()
    {
        var one = Offsets(new ChordNoteInfo(-1, null), new ChordNoteInfo(0, null), new ChordNoteInfo(0, null));
        Assert.Equal(one[1], one[2]);
        var two = Offsets(new ChordNoteInfo(-1, null), new ChordNoteInfo(0, "accidentals.flat"), new ChordNoteInfo(0, null));
        Assert.NotEqual(two[1], two[2]);
    }

    [Fact]
    public void AnyOrderOfWriting_DrawsTheSame()
    {
        var o = Offsets(Note(-1, 69), Note(0, 71), Note(0, 71));
        Assert.Equal(0, o[0]);
        Assert.True(o[1] > 0);
        Assert.Equal(o[1], o[2]);
    }

    [Fact]
    public void TheNoteAboveAUnison_Counts_AsTheNoteBelow()
    {
        var o = Offsets(Note(0, 71), Note(0, 71), Note(1, 72));
        Assert.Equal(o[0], o[1]);
        Assert.NotEqual(o[0], o[2]);
    }

    [Fact]
    public void AUnisonAlone_IsLilyPondsTwoHeads()
    {
        var o = Offsets(Note(0, 71), Note(0, 71));
        Assert.NotEqual(o[0], o[1]);
    }

    [Fact]
    public void AUnisonWithNoSecond_IsLilyPondsTwoHeads()
    {
        var o = Offsets(Note(0, 71), Note(0, 71), Note(4, 76));
        Assert.NotEqual(o[0], o[1]);
    }

    // B and B flat stand on one line and are two notes: neither is drawn away.
    [Fact]
    public void TwoPitchesOnOneLine_AreNotOne()
    {
        var o = Offsets(Note(0, 71), Note(0, 70, "accidentals.flat"), Note(-1, 69));
        Assert.NotEqual(o[0], o[1]);
    }

    [Fact]
    public void AChordWithNoSameNote_IsAsItWas()
    {
        var o = Offsets(Note(-1, 69), Note(0, 71), Note(1, 72));
        Assert.Equal(0, o[0]);
        Assert.True(o[1] > 0);
        Assert.Equal(0, o[2]);
    }

    private static string Book(string music) =>
        "part m { clef treble }\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }\n";

    private static Diagnostic[] Warned(string text)
        => [.. SemanticValidation.Run(SyntaxTree.Parse(text)).Where(d => d.Code == DiagnosticCodes.ChordSamePitchTwice)];

    [Theory]
    [InlineData("<b' a, b>2 r |", "b>")]            // the owner's: the marks hide it
    [InlineData("<c' c>4 d e f |", "c>")]
    [InlineData("<c' e c>4 d e f |", "c>")]
    [InlineData("<e' g e>4 d e f |", "e>")]
    public void ThePitchWrittenTwice_IsSaidOnTheSecond(string music, string at)
    {
        string text = Book(music);
        var found = Assert.Single(Warned(text));
        Assert.Equal(text.IndexOf(at, System.StringComparison.Ordinal), found.Span.Start);
    }

    [Theory]
    [InlineData("<c' e g>4 d e f |")]
    [InlineData("<c' e' g>4 d e f |")]
    [InlineData("<c' c'>4 d e f |")]                // an octave
    [InlineData("<b' bes>2 r |")]                   // one line, two notes
    [InlineData("<cis' des>2 r |")]                 // another spelling
    [InlineData("<e'\\1 e\\2>2 r |")]               // two strings: a unison a player means
    public void AnythingElse_PassesInSilence(string music)
        => Assert.Empty(Warned(Book(music)));

    [Fact]
    public void OnOneString_IsSaid()
        => Assert.Single(Warned(Book("<e'\\1 e\\1>2 r |")));
}
