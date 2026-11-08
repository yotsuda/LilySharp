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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// The sung voices (docs/singing-design.md §3–§4): a part's words on its notes in seconds, read
/// off the MIDI export's lyric events and clock — the page's placement and the playback's time,
/// nothing counted again. Session 1125.
/// </summary>
[Trait("Category", "Unit")]
public class SungVoicesTests
{
    private static List<SungVoice> Voices(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var score = RenderSpecParser.FindAll(tree).FirstOrDefault();
        var midi = new MidiExporter { Form = score?.Form, Score = score }.Export(tree);
        return SungVoices.Read(midi, MidiClock.SecondsOf(midi));
    }

    private static string Book(string music, string words, string parts = "part melody { clef treble }",
        string rows = "staff melody  lyrics verse sings melody", string head = "") => $$"""
        {{head}}
        tempo 4 = 120
        time 2/4
        {{parts}}
        section A {
          melody { {{music}} }
          lyrics verse { {{words}} }
        }
        form { A }
        score { {{rows}} }
        """;

    // The owner's 雪やこんこ, two bars: ゆ on c', its vowel held on the d (`_`), き, や | こん, こ and a rest.
    private const string YukiMusic = "c'8. d16 c8. d16 | c4 a8 r |";
    private const string YukiWords = "ゆ _ き や | こん こ |";

    [Fact]
    public void TheWords_StandOnTheirNotes_InSeconds_AndAHeldNoteIsAMelisma()
    {
        var voice = Assert.Single(Voices(Book(YukiMusic, YukiWords)));
        Assert.Equal("melody", voice.Part);
        Assert.Equal("ja", voice.Language);
        Assert.Empty(voice.Warnings);
        Assert.Equal(new[] { "ゆ", null, "き", "や", "こん", "こ" }, voice.Notes.Select(n => n.Lyric));
        Assert.Equal(new[] { false, true, false, false, false, false }, voice.Notes.Select(n => n.Melisma));
        // 120 to the quarter: a quarter is 0.5 s, a dotted eighth 0.375 s, a sixteenth 0.125 s.
        Assert.Equal(new[] { 0.0, 0.375, 0.5, 0.875, 1.0, 1.5 }, voice.Notes.Select(n => System.Math.Round(n.Start, 6)));
        Assert.Equal(new[] { 0.375, 0.125, 0.375, 0.125, 0.5, 0.25 }, voice.Notes.Select(n => System.Math.Round(n.Duration, 6)));
        Assert.Equal(new double[] { 60, 62, 60, 62, 60, 57 }, voice.Notes.Select(n => n.Pitch));
    }

    [Fact]
    public void ABreath_IsAfterTheLastNoteBeforeARest_AndAfterTheLast()
    {
        var voice = Assert.Single(Voices(Book(YukiMusic, YukiWords)));
        // や runs straight into こん across the bar line: no breath there; こ is followed by a rest.
        Assert.Equal(new[] { false, false, false, false, false, true }, voice.Notes.Select(n => n.Breath));
        var two = Assert.Single(Voices(Book("c'4 r | d4 e |", "a | b c |")));
        Assert.Equal(new[] { true, false, true }, two.Notes.Select(n => n.Breath));
    }

    [Fact]
    public void ASlurredNote_TakesNoSyllable_AndIsAMelisma()
    {
        // LilyPond's \lyricsto: the notes inside the slur take none (the words are per bar).
        var voice = Assert.Single(Voices(Book("c'4( d) | e f |", "la | la la |")));
        Assert.Equal(new[] { "la", null, "la", "la" }, voice.Notes.Select(n => n.Lyric));
        Assert.Equal(new[] { false, true, false, false }, voice.Notes.Select(n => n.Melisma));
    }

    [Fact]
    public void AChord_SingsItsTopNote()
    {
        var voice = Assert.Single(Voices(Book("<c' e' g'>4 r | <d' f'>2 |", "a | b |", head: "octave absolute")));
        Assert.Equal(new double[] { 67, 65 }, voice.Notes.Select(n => n.Pitch));
        Assert.Equal(new[] { "a", "b" }, voice.Notes.Select(n => n.Lyric));
    }

    [Fact]
    public void NotesAfterTheWordsRanOut_AreNotSung_PastARest()
    {
        var voice = Assert.Single(Voices(Book("c'4 d | r e | f g |", "a b |")));
        Assert.Equal(new[] { "a", "b" }, voice.Notes.Select(n => n.Lyric));
    }

    [Fact]
    public void SyllablesThatAreNotKana_AreSung_AndSaidInAWarning()
    {
        var voice = Assert.Single(Voices(Book("c'4 d | e2 |", "雪 や | こ |")));
        Assert.Equal(new[] { "雪", "や", "こ" }, voice.Notes.Select(n => n.Lyric));
        var warning = Assert.Single(voice.Warnings);
        Assert.Contains("雪", warning);
        Assert.DoesNotContain("や", warning);
    }

    [Fact]
    public void APartWithNoWords_IsNoVoice_AndEachPartWithWordsIsOne()
    {
        var book = """
            time 2/4
            section A {
              sax { c'4 d | e2 | }
              sop { g'4 a | b2 | }
              alt { e'4 f | g2 | }
              lyrics s sings sop { ラ ラ | ラ | }
              lyrics a sings alt { ル ル | ル | }
            }
            form { A }
            score { staff sax  staff sop  lyrics s  staff alt  lyrics a }
            """;
        var voices = Voices(book);
        Assert.Equal(new[] { "sop", "alt" }, voices.Select(v => v.Part));
        Assert.All(voices, v => Assert.Equal(3, v.Notes.Count));
    }

    [Fact]
    public void TwoLyricRowsOnOnePart_AreOneVoice_TheFirstWords()
    {
        // The owner's file prints the verse under the melody twice (two rows): one voice, sung once.
        var voices = Voices(Book(YukiMusic, YukiWords,
            rows: "staff melody  lyrics verse sings melody  staff melody  lyrics verse sings melody"));
        var voice = Assert.Single(voices);
        Assert.Equal(6, voice.Notes.Count);
    }

    /// <summary>A part a lyrics row sings is a singer (owner's decision 2026-11-08): its track's
    /// program — the stand-in the preview plays while there is no voice, and what the .mid plays —
    /// is its named instrument, else its preset's sound, else the General MIDI choir; a part with
    /// no words keeps LilyPond's acoustic grand.</summary>
    [Theory]
    [InlineData("part melody { clef treble }", true, 52)]
    [InlineData("part melody { clef treble }", false, 0)]
    [InlineData("part melody { midiInstrument \"flute\" }", true, 73)]
    [InlineData("part melody { instrument soprano }", true, 52)]
    [InlineData("part melody { instrument piano }", true, 0)]
    public void ASungPartsStandIn_IsItsInstrument_ElseTheChoir(string part, bool sung, int program)
    {
        var tree = SyntaxTree.Parse(Book(YukiMusic, YukiWords, parts: part, rows: sung ? "staff melody  lyrics verse sings melody" : "staff melody"));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var score = RenderSpecParser.FindAll(tree).First();
        var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
        var track = midi.Tracks.Single(t => t.Name == "melody");
        Assert.Equal(program, track.Program);
    }

    [Fact]
    public void Kana_IsHiraganaKatakanaAndTheLongVowelMark()
    {
        Assert.True(SungVoices.IsKana("ゆ"));
        Assert.True(SungVoices.IsKana("こん"));
        Assert.True(SungVoices.IsKana("ラー"));
        Assert.True(SungVoices.IsKana("ｱ"));
        Assert.False(SungVoices.IsKana("雪"));
        Assert.False(SungVoices.IsKana("la"));
        Assert.False(SungVoices.IsKana(""));
    }
}
