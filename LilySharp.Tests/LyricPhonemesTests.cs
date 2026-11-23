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
using System.Text.Json;
using LilySharp.Core.Audio;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Core.Vocaloid;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The voice's own symbols written on a syllable — <c>병[/pp.y.eo.NG/]</c> (Cantoria's request 1,
/// 2026-11-22; <see cref="LyricReading.Phonemes"/>): the sung voice hands them to the engine as
/// <c>"phonemes": "pp y eo NG"</c> on the syllable's note; the page and every file print the word.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LyricPhonemesTests
{
    private static string Book(string words, string music = "c'4 d e f |", string language = "korean") => $$"""
        tempo 4 = 120
        time 4/4
        part melody { clef treble  instrument soprano }
        section A {
          melody { {{music}} }
          lyrics verse language {{language}} { {{words}} }
        }
        singer sings verse on melody
        form { A }
        score { staff melody  lyrics verse on melody }
        """;

    private static SungVoice Voice(SyntaxTree tree)
    {
        var score = RenderSpecParser.FindAll(tree).First();
        var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
        return Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));
    }

    [Theory]
    [InlineData("병[/pp.y.eo.NG/]", "병", "병", "pp y eo NG")]
    [InlineData("漢[かん][/k.a.N/]", "漢", "かん", "k a N")]
    [InlineData("大[おお]空[ぞら][/o.o.z.o.r.a/]", "大空", "おおぞら", "o o z o r a")]
    [InlineData("love[/l.ah1.v/]", "love", "love", "l ah1 v")]
    [InlineData("병[/ko/pp.ko/y/]", "병", "병", "ko/pp ko/y")]        // a symbol may keep its language's prefix
    [InlineData("병[/NG/]", "병", "병", "NG")]
    public void APhonemeBracket_EndsTheSyllable_AndIsNoReading(string raw, string printed, string sung, string phonemes)
    {
        var (p, s, problem) = LyricReading.Split(raw);
        Assert.Null(problem);
        Assert.Equal(printed, p);
        Assert.Equal(sung, s);
        Assert.Equal(phonemes, LyricReading.Phonemes(raw));
        Assert.DoesNotContain(LyricReading.Readings(raw), r => r.Contains('/'));
    }

    [Theory]
    [InlineData("병", null)]
    [InlineData("漢[かん]", null)]
    [InlineData("병[/pp", null)]          // malformed: no phonemes at all
    public void ASyllableWithNone_HasNoPhonemes(string raw, string? phonemes)
        => Assert.Equal(phonemes, LyricReading.Phonemes(raw));

    [Theory]
    [InlineData("[/pp.y/]", "no word before them")]
    [InlineData("병[/pp.y", "not closed")]
    [InlineData("병[/pp/]다", "must end the syllable")]
    [InlineData("병[/pp/][かん]", "must end the syllable")]
    [InlineData("병[/pp/][/y/]", "must end the syllable")]
    [InlineData("병[//]", "empty phoneme")]
    [InlineData("병[/pp..y/]", "empty phoneme")]
    [InlineData("병[/.pp/]", "empty phoneme")]
    [InlineData("병[/p[p/]", "bracket inside")]
    [InlineData("병[너[/pp/]", "reading in '병[너[/pp/]' is not closed")]
    public void Split_NamesWhatIsWrong_AndLeavesTheWordAsWritten(string raw, string problemSays)
    {
        var (p, s, problem) = LyricReading.Split(raw);
        Assert.NotNull(problem);
        Assert.Contains(problemSays, problem);
        Assert.Equal(raw, p);
        Assert.Equal(raw, s);
        Assert.Null(LyricReading.Phonemes(raw));
    }

    [Fact]
    public void TheVoice_SingsTheSymbols_OnTheSyllablesNote_AndTheJsonCarriesThem()
    {
        var tree = SyntaxTree.Parse(Book("발 병[/pp.y.eo.NG/] ~ 다 |"));
        Assert.Empty(tree.Diagnostics);

        var page = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "verse" });
        Assert.Equal(new[] { "발", "병", "다" }, page.Lyrics.OrderBy(l => l.ItemIndex).Select(l => l.Text));

        var voice = Voice(tree);
        Assert.Equal(new[] { "발", "병", null, "다" }, voice.Notes.Select(n => n.Lyric));
        Assert.Equal(new[] { null, "pp y eo NG", null, null }, voice.Notes.Select(n => n.Phonemes));   // the melisma's note: none

        using var json = JsonDocument.Parse(SungScoreJson.Write(voice, 48000));
        var notes = json.RootElement.GetProperty("notes").EnumerateArray().ToList();
        Assert.Equal("pp y eo NG", notes[1].GetProperty("phonemes").GetString());
        Assert.Equal("병", notes[1].GetProperty("lyric").GetString());       // the lyric stays: the engine names the note by it
        Assert.All(notes.Where((_, i) => i != 1), n => Assert.False(n.TryGetProperty("phonemes", out _)));
    }

    [Fact]
    public void WithAReading_TheLyricIsTheReading_AndThePhonemesRideBesideIt()
    {
        var tree = SyntaxTree.Parse(Book("漢[かん][/k.a.N/] じ ゆ き |", language: "japanese"));
        Assert.Empty(tree.Diagnostics);
        Assert.DoesNotContain(LilySharp.Core.Semantics.SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.LyricReadingNotKana);
        var first = Voice(tree).Notes[0];
        Assert.Equal("かん", first.Lyric);
        Assert.Equal("k a N", first.Phonemes);
    }

    [Fact]
    public void AHeldNoteBeginningAPhrase_SingsTheVowelAgain_WithoutTheSyllablesSymbols()
    {
        // かん held over a breath: the note after it sings ん again, and k a N is not its sound.
        var tree = SyntaxTree.Parse(Book("漢[かん][/k.a.N/] _ じ ゆ |", music: "c'4@breath d e f |", language: "japanese"));
        Assert.Empty(tree.Diagnostics);
        var notes = Voice(tree).Notes;
        Assert.Equal(new[] { "かん", "ん", "じ", "ゆ" }, notes.Select(n => n.Lyric));
        Assert.Equal(new[] { "k a N", null, null, null }, notes.Select(n => n.Phonemes));
    }

    [Fact]
    public void AGraceLeadingTheNote_TakesItsSymbols_WithItsSyllable()
    {
        var tree = SyntaxTree.Parse(Book("발 병[/pp.y.eo.NG/] 다 라 |", music: "c'4 grace { e'16 } d4 e f |"));
        Assert.Empty(tree.Diagnostics);
        var notes = Voice(tree).Notes;
        int at = notes.ToList().FindIndex(n => n.Lyric == "병");
        Assert.Equal("pp y eo NG", notes[at].Phonemes);
        Assert.Null(notes[at + 1].Phonemes);
    }

    [Fact]
    public void ThePrintedFiles_WriteTheWord_AndNoSymbol()
    {
        var tree = SyntaxTree.Parse(Book("발- 병[/pp.y.eo.NG/] 다 라 |"));
        Assert.Empty(tree.Diagnostics);
        var xml = new MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.Contains("병", xml);
        Assert.DoesNotContain("pp", xml);
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("병", ly);
        Assert.DoesNotContain("eo", ly);
        var vsqx = new VsqxExporter().Export(tree).ToString();
        Assert.DoesNotContain("eo.NG", vsqx);
        Assert.DoesNotContain("[/", vsqx);
    }

    [Theory]
    [InlineData("발 병[/pp y eo NG/] 다 |", "not closed")]       // a space ends the syllable: the error says to use '.'
    [InlineData("발 [/pp/] 다 라 |", "no word before them")]
    [InlineData("발 병[/pp..y/] 다 라 |", "empty phoneme")]     // (an empty [//] is a line comment from its //)
    public void MalformedPhonemes_AreAnError_OnTheSyllable(string words, string says)
    {
        var tree = SyntaxTree.Parse(Book(words));
        var d = tree.Diagnostics.First(d => d.Code == DiagnosticCodes.LyricReadingMalformed);
        Assert.Contains(says, d.Message);
        if (says == "not closed")
            Assert.Contains("'.'", d.Message);
        string at = tree.GetRoot().ToFullString().Substring(d.Span.Start, d.Span.Length);
        Assert.Contains("[/", at);
    }

    [Fact]
    public void TheSourceRoundTrips_WithThePhonemesInPlace()
    {
        string source = Book("발- 병[/pp.y.eo.NG/] 漢[かん][/k.a.N/] 라 |");
        Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());
    }
}
