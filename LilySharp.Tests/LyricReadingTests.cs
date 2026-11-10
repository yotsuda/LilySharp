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
using System.Xml.Linq;
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
/// A reading glued to a lyric word — <c>漢[かん]</c> (owner's decision 2026-11-08, session 1138;
/// <see cref="LyricReading"/>): the page, the MusicXML, the LilyPond twin and the .mid print the
/// kanji; the sung voice and the VSQX sing the kana. Malformed brackets are LYS4036, a reading
/// that is not kana LYS4037.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LyricReadingTests
{
    private static string Book(string words, string music = "c'4 d e f |") => $$"""
        tempo 4 = 120
        time 4/4
        part melody { clef treble  instrument soprano }
        section A {
          melody { {{music}} }
          lyrics verse { {{words}} }
        }
        singer sings verse on melody
        form { A }
        score { staff melody  lyrics verse on melody }
        """;

    [Theory]
    [InlineData("漢[かん]", "漢", "かん")]
    [InlineData("大[おお]空[ぞら]", "大空", "おおぞら")]
    [InlineData("漢[かん]じ", "漢じ", "かんじ")]
    [InlineData("ゆ", "ゆ", "ゆ")]
    [InlineData("はる[ハル]", "はる", "ハル")]
    public void Split_PrintsTheWord_AndSingsTheReading(string raw, string printed, string sung)
    {
        var (p, s, problem) = LyricReading.Split(raw);
        Assert.Null(problem);
        Assert.Equal(printed, p);
        Assert.Equal(sung, s);
    }

    [Theory]
    [InlineData("[かん]", "no word before it")]
    [InlineData("漢[かん", "not closed")]
    [InlineData("漢[]", "empty reading")]
    [InlineData("漢[か[ん]]", "inside the reading")]
    [InlineData("漢]", "no '['")]
    [InlineData("漢[かん][じ]", "no word before it")]
    public void Split_NamesWhatIsWrong_AndLeavesTheWordAsWritten(string raw, string problemSays)
    {
        var (p, s, problem) = LyricReading.Split(raw);
        Assert.NotNull(problem);
        Assert.Contains(problemSays, problem);
        Assert.Equal(raw, p);
        Assert.Equal(raw, s);
    }

    [Fact]
    public void ThePage_PrintsTheKanji_AndTheVoice_SingsTheKana_WithNoKanaWarning()
    {
        var tree = SyntaxTree.Parse(Book("漢[かん] 字[じ] 大[おお]空[ぞら] ゆ |"));
        Assert.Empty(tree.Diagnostics);

        var page = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "verse" });
        Assert.Equal(new[] { "漢", "字", "大空", "ゆ" }, page.Lyrics.OrderBy(l => l.ItemIndex).Select(l => l.Text));

        var score = RenderSpecParser.FindAll(tree).First();
        var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
        var events = midi.Tracks.SelectMany(t => t.Lyrics).OrderBy(l => l.Tick).ToList();
        Assert.Equal(new[] { "漢", "字", "大空", "ゆ" }, events.Select(l => l.Text));          // the .mid: as printed
        Assert.Equal(new[] { "かん", "じ", "おおぞら", null }, events.Select(l => l.Sung));

        var voice = Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));
        Assert.Equal(new[] { "かん", "じ", "おおぞら", "ゆ" }, voice.Notes.Select(n => n.Lyric));
        Assert.Empty(voice.Warnings);
    }

    [Fact]
    public void AKanjiWithNoReading_StillWarns_ThatTheVoiceNeedsKana()
    {
        var tree = SyntaxTree.Parse(Book("漢 字[じ] ゆ き |"));
        var score = RenderSpecParser.FindAll(tree).First();
        var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
        var voice = Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));
        var warning = Assert.Single(voice.Warnings);
        Assert.Contains("漢", warning);
        Assert.DoesNotContain("字", warning);
    }

    [Fact]
    public void TheMusicXml_AndTheLilyPondTwin_WriteTheKanji_TheVsqx_SingsTheKana()
    {
        var tree = SyntaxTree.Parse(Book("漢[かん]- 字[じ] ゆ き |"));
        Assert.Empty(tree.Diagnostics);

        var xml = new MusicXmlExporter().Export(tree).ToXml();
        var texts = xml.Descendants("lyric").Select(l => l.Element("text")!.Value).ToList();
        Assert.Equal(new[] { "漢", "字", "ゆ", "き" }, texts);
        Assert.Equal("begin", xml.Descendants("lyric").First().Element("syllabic")!.Value);   // the hyphen survives the reading
        Assert.DoesNotContain("かん", xml.ToString());

        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("漢", ly);
        Assert.DoesNotContain("かん", ly);
        Assert.DoesNotContain("[", ly.Substring(ly.IndexOf("lyricmode", System.StringComparison.Ordinal)).Split('}')[0]);

        var vsqx = new VsqxExporter().Export(tree).ToString();
        Assert.Contains("かん", vsqx);
        Assert.Contains("<y><![CDATA[じ]]></y>", vsqx);
    }

    [Theory]
    [InlineData("漢 [かん] ゆ き |", "no word before it")]        // a '[' after a space is its own syllable
    [InlineData("漢[かん ゆ き |", "not closed")]
    [InlineData("漢[] ゆ き ら |", "empty reading")]
    [InlineData("漢 ] ゆ き |", "no '['")]
    public void MalformedBrackets_AreAnError_OnTheSyllable(string words, string says)
    {
        var tree = SyntaxTree.Parse(Book(words));
        var d = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.LyricReadingMalformed);
        Assert.Contains(says, d.Message);
        string source = tree.GetRoot().ToFullString();
        string at = source.Substring(d.Span.Start, d.Span.Length);
        Assert.True(at.Contains('[') || at.Contains(']'), $"the error points at the bracketed syllable, not '{at}'");
    }

    [Fact]
    public void AReadingThatIsNotKana_IsAWarning_AndIsStillHandedToTheVoice()
    {
        var tree = SyntaxTree.Parse(Book("漢[kan] 字[じ] ゆ き |"));
        var d = Assert.Single(tree.Diagnostics);
        Assert.Equal(DiagnosticCodes.LyricReadingNotKana, d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("[kan]", d.Message);
        var score = RenderSpecParser.FindAll(tree).First();
        var midi = new MidiExporter { Form = score.Form, Score = score }.Export(tree);
        var voice = Assert.Single(SungVoices.Read(midi, MidiClock.SecondsOf(midi)));
        Assert.Equal("kan", voice.Notes[0].Lyric);
    }

    [Fact]
    public void AQuotedSyllable_KeepsItsBrackets_AsEveryOtherCharacter()
    {
        var tree = SyntaxTree.Parse(Book("\"漢[かん]\" 字 ゆ き |"));
        Assert.Empty(tree.Diagnostics);
        var page = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "verse" });
        Assert.Equal("漢[かん]", page.Lyrics.OrderBy(l => l.ItemIndex).First().Text);
    }

    [Fact]
    public void TheSourceRoundTrips_WithTheReadingInPlace()
    {
        string source = Book("漢[かん]- 字[じ] 大[おお]空[ぞら] ゆ |");
        Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());
    }
}
