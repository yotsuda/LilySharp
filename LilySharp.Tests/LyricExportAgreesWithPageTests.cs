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
using System.Xml.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The files say what the page says: a syllable stands on the same bar and moment in the
/// MusicXML and the MIDI as on the page, however the words are spelled.
/// </summary>
[Trait("Category", "Unit")]
public class LyricExportAgreesWithPageTests
{
    private const string Head = "octave absolute\ntime 4/4\npart m { clef treble }\n";
    private const string Staff = "form { A }\nscore { staff m  lyrics w }\n";

    public static IEnumerable<object[]> Books()
    {
        static object[] Sec(string name, string music, string words)
            => new object[] { name, Head + "section A { m { " + music + " } lyrics w sings m { " + words + " } }\n" + Staff };

        yield return Sec("plain", "c''4 d'' e'' f'' | g''1 |", "a b c d | e |");
        yield return Sec("more syllables than the bar's notes", "c''1 | d''1 | e''1 | f''1 |", "one two three | four | five | six |");
        yield return Sec("a bar left short", "c''4 d'' e'' f'' | g''4 a'' b'' c''' |", "a b | c d |");
        yield return Sec("a bar with no words", "c''1 | d''1 | e''1 |", "a | | b |");
        yield return Sec("words past the last bar wrap", "c''1 | d''1 |", "a | b | c | d |");
        yield return Sec("verses", "c''2 d'' |", "verse 1 { aa bb | } verse 2 { cc dd | }");
        yield return Sec("a slur opened on a rest", "r4( c'' d'') e'' |", "aa bb cc |");
        yield return Sec("a plain slur", "c''4( d'' e'') f'' |", "aa bb cc |");
        yield return Sec("a slur over the bar", "c''2 d''( | e'') f'' |", "aa bb | cc |");
        yield return Sec("a tie", "c''2~ c''4 d'' | e''1 |", "aa bb | cc |");
        yield return Sec("a tie over the bar", "c''1~ | c''2 d'' |", "aa | bb |");
        yield return Sec("a tie to a rest", "c''4~ r c'' d'' |", "aa bb cc |");
        yield return Sec("a grace note", "grace { b'8 } c''4 d'' e'' f'' |", "aa bb cc dd |");
        yield return Sec("a chord", "<c'' e''>4 d'' <e'' g''>2 |", "aa bb cc |");
        yield return Sec("melisma marks", "c''4 d'' e'' f'' |", "aa _ bb ~ |");
        yield return Sec("an extender line", "c''4( d'') e'' f'' |", "aa __ bb cc |");
        yield return Sec("a tuplet", "tuplet 3/2 { c''8 d'' e'' } f''4 g''2 |", "a b c d e |");
        yield return Sec("two voices", "voice { c''2 d'' | } { e'1 | }", "aa bb |");
        yield return Sec("a rest", "c''4 r d'' r | e''1 |", "aa bb | cc |");
        yield return Sec("a pickup", "partial 4 c''4 | d''1 |", "aa | bb |");

        yield return new object[]
        {
            "a by-part track, a section sung twice",
            "octave absolute\ntime 4/4\npart m { clef treble\n section A { c''2 d'' | }\n section B { e''1 | }\n}\n"
            + "lyrics w sings m { section A { aa bb | } section B { cc | } }\nform { A B A }\nscore { staff m  lyrics w }\n",
        };
        yield return new object[]
        {
            "verses on a section written out twice",
            Head + "section A { m { c''2 d'' | } lyrics w sings m { verse 1 { aa bb | } verse 2 { cc dd | } } }\n"
            + "form { A A }\nscore { staff m  lyrics w }\n",
        };
        yield return new object[]
        {
            "two tracks on one part",
            Head + "section A { m { c''2 d'' | } lyrics w sings m { aa bb | } lyrics x sings m { cc dd | } }\n"
            + "form { A }\nscore { staff m  lyrics w  lyrics x }\n",
        };
        yield return new object[]
        {
            "a track sings the second part",
            "octave absolute\ntime 4/4\npart m { clef treble }\npart n { clef treble }\n"
            + "section A { m { c''2 d'' | } n { e'4 f' g' a' | } lyrics w sings n { aa bb cc dd | } }\n"
            + "form { A }\nscore { staff m\n staff n  lyrics w }\n",
        };
    }

    /// <summary>"bar/moment in 1920ths: text", sorted, on the page.</summary>
    private static string[] OnThePage(SyntaxTree tree)
    {
        var page = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.PlayedSpec(tree, null, null)!);
        return page.Lyrics
            .Select(l => $"{l.MeasureIndex}/{(int)System.Math.Round(l.Timing.ToDouble() * 1920)}: {l.Text}")
            .Order(System.StringComparer.Ordinal).ToArray();
    }

    /// <summary>The same reading of the MusicXML: every &lt;lyric&gt; at its note's bar and moment.</summary>
    private static string[] InTheXml(SyntaxTree tree)
    {
        var doc = new MusicXmlExporter().Export(tree).ToXml();
        var rows = new List<string>();
        foreach (var part in doc.Descendants("part"))
        {
            int divisions = 1, bar = 0;
            foreach (var measure in part.Elements("measure"))
            {
                int at = 0, last = 0;
                foreach (var el in measure.Elements())
                {
                    if (el.Name == "attributes" && el.Element("divisions") is { } d)
                        divisions = int.Parse(d.Value);
                    else if (el.Name == "backup")
                        at -= int.Parse(el.Element("duration")!.Value);
                    else if (el.Name == "forward")
                        at += int.Parse(el.Element("duration")!.Value);
                    else if (el.Name == "note")
                    {
                        if (el.Element("grace") != null)
                            continue;
                        int start = el.Element("chord") != null ? last : at;
                        foreach (var l in el.Elements("lyric"))
                            rows.Add($"{bar}/{start * 480 / divisions}: {l.Element("text")!.Value}");
                        last = start;
                        if (el.Element("chord") == null)
                            at += int.Parse(el.Element("duration")!.Value);
                    }
                }
                bar++;
            }
        }
        return rows.Order(System.StringComparer.Ordinal).ToArray();
    }

    /// <summary>Every lyric event, with the tick as a bar and moment of 4/4 (a book sung once).</summary>
    private static string[] InTheMidi(SyntaxTree tree)
    {
        var midi = new MidiExporter().Export(tree);
        return midi.Tracks.SelectMany(t => t.Lyrics)
            .Select(l => $"{l.Tick / 1920}/{l.Tick % 1920}: {l.Text}")
            .Order(System.StringComparer.Ordinal).ToArray();
    }

    private static SyntaxTree Parse(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return tree;
    }

    [Theory]
    [MemberData(nameof(Books))]
    public void TheXmlAgrees(string name, string source)
    {
        _ = name;
        var tree = Parse(source);
        Assert.Equal(OnThePage(tree), InTheXml(tree));
    }

    [Theory]
    [MemberData(nameof(Books))]
    public void TheMidiAgrees(string name, string source)
    {
        var tree = Parse(source);
        // A pickup's bar is short, and this reads ticks as whole bars; a grace note before the
        // beat moves its main note's sounding onset (the next test).
        if (name is not ("a pickup" or "a grace note"))
            Assert.Equal(OnThePage(tree), InTheMidi(tree));
    }

    [Fact]
    public void TheMidi_SingsTheMainNote_WhereItSounds_AfterAGraceNote()
    {
        var source = Head + "section A { m { grace { b'8 } c''4 d'' e''2 | } lyrics w sings m { aa bb | } }\n" + Staff;
        var midi = new MidiExporter().Export(Parse(source));
        var main = midi.Tracks.SelectMany(t => t.Notes).Where(n => !n.IsGrace).OrderBy(n => n.StartTick).First();
        Assert.Equal(new[] { (main.StartTick, "aa"), (480, "bb") }, Events(source));
    }

    private static (int Tick, string Text)[] Events(string source)
        => new MidiExporter().Export(Parse(source)).Tracks.SelectMany(t => t.Lyrics)
            .Select(l => (l.Tick, l.Text)).OrderBy(e => e.Tick).ThenBy(e => e.Text, System.StringComparer.Ordinal).ToArray();

    private const string RepeatedForm = "form { |: A :| }\nscore { staff m  lyrics w }\n";

    [Fact]
    public void TheMidi_SingsPlainWordsEveryPass()
        => Assert.Equal(new[] { (0, "aa"), (960, "bb"), (1920, "aa"), (2880, "bb") }, Events(
            Head + "section A { m { c''2 d'' | } lyrics w sings m { aa bb | } }\n" + RepeatedForm));

    [Fact]
    public void TheMidi_SingsEachVerseOnItsOwnPass()
        => Assert.Equal(new[] { (0, "aa"), (960, "bb"), (1920, "cc"), (2880, "dd") }, Events(
            Head + "section A { m { c''2 d'' | } lyrics w sings m { verse 1 { aa bb | } verse 2 { cc dd | } } }\n" + RepeatedForm));

    [Theory]
    [InlineData("1,3")]
    [InlineData("1-3")]
    public void TheMidi_SingsAVerseOnEveryPlayItCovers(string plays)
    {
        var form = "form { |: A :|*3 }\nscore { staff m  lyrics w }\n";
        var ticks = Events(Head + "section A { m { c''1 | } lyrics w sings m { verse " + plays + " { aa | } } }\n" + form)
            .Select(e => e.Tick).ToArray();
        Assert.Equal(plays == "1,3" ? new[] { 0, 3840 } : new[] { 0, 1920, 3840 }, ticks);
    }

    [Fact]
    public void TheMidi_SingsABlankNot_AndTheNextWordOnTheNextNote()
        => Assert.Equal(new[] { (0, "aa"), (960, "cc") }, Events(
            Head + "section A { m { c''4 d'' e'' f'' | } lyrics w sings m { aa \"\" cc | } }\n" + Staff));

    [Fact]
    public void TheMidi_SingsAWrappedRowOnThePassOfItsRow()
        => Assert.Equal(new[] { (0, "aa"), (1920, "bb") }, Events(
            Head + "section A { m { c''1 | } lyrics w sings m { aa | bb | } }\n" + RepeatedForm));

    [Fact]
    public void TheMidi_SingsATopLevelTrack()
        => Assert.Equal(new[] { (0, "aa"), (960, "bb") }, Events(
            Head + "section A { m { c''2 d'' | } }\nlyrics w sings m { aa bb | }\n" + Staff));

    [Fact]
    public void TheMidi_SingsAnUnboundRowOnTheStaffAbove()
        => Assert.Equal(new[] { (0, "aa"), (960, "bb") }, Events(
            Head + "section A { m { c''2 d'' | } lyrics w { aa bb | } }\n" + Staff));

    [Fact]
    public void TheMidi_TakesAPhraseTwoPartsPlay_OnThePartSung_Once()
        => Assert.Equal(new[] { (0, "aa"), (960, "bb") }, Events(
            "octave absolute\ntime 4/4\nphrase riff { c''2 d'' | }\npart m { clef treble }\npart n { clef treble }\n"
            + "section A { m { riff } n { riff } lyrics w sings n { aa bb | } }\n"
            + "form { A }\nscore { staff m\n staff n  lyrics w }\n"));

    private static string[] PageWords(string music, string words)
    {
        var tree = Parse(Head + "section A { m { " + music + " } lyrics w sings m { " + words + " } }\n" + Staff);
        return OnThePage(tree);
    }

    [Fact]
    public void AGraceNote_TakesNoSyllable()
        => Assert.Equal(new[] { "0/0: aa", "0/480: bb" }, PageWords("grace { b'8 } c''4 d'' e''2 |", "aa bb |"));

    [Fact]
    public void ASlurOpenedOnAGraceNote_DoesNotHoldTheMainNote()
        => Assert.Equal(new[] { "0/0: aa", "0/960: bb" }, PageWords("grace { b'8( } c''4 d'') e''2 |", "aa bb |"));

    [Fact]
    public void ATieOpenedOnAGraceNote_DoesNotHoldTheMainNote()
        => Assert.Equal(new[] { "0/0: aa", "0/480: bb" }, PageWords("grace { c''8~ } c''4 d'' e''2 |", "aa bb |"));

    [Fact]
    public void ASlurRunningThroughAGraceNote_StillHoldsTheNoteAfterIt()
        => Assert.Equal(new[] { "0/0: aa", "0/960: bb" }, PageWords("c''4( grace { d''8 } e''4) f''2 |", "aa bb |"));

    [Fact]
    public void ATieIntoAGraceNote_DoesNotReachTheNoteAfterIt()
        => Assert.Equal(new[] { "0/0: aa", "0/480: bb", "0/960: cc" }, PageWords("c''4~ grace { c''8 } d''4 e''2 |", "aa bb cc |"));

    [Fact]
    public void ANamedVoicesSyllable_StandsOnItsNote_NotTheGraceNoteBeforeIt()
    {
        var tree = Parse(Head + "section A { m { voice { e'1 | } alt { grace { b'8 } c''2 d'' | } } lyrics w sings alt { aa bb | } }\n"
            + "form { A }\nscore { staff m  lyrics w }\n");
        var doc = new MusicXmlExporter().Export(tree).ToXml();
        Assert.Equal(new[] { "C5 aa", "D5 bb" }, doc.Descendants("note").Where(n => n.Element("lyric") != null)
            .Select(n => n.Element("pitch")!.Element("step")!.Value + n.Element("pitch")!.Element("octave")!.Value
                + " " + n.Element("lyric")!.Element("text")!.Value));
    }

    [Fact]
    public void AVersesOwnNumberIsWritten_WithNoVerseOneBeside()
        => Assert.Equal(new[] { "2:aa", "2:bb" }, Numbers(
            Head + "section A { m { c''2 d'' | } lyrics w sings m { verse 2 { aa bb | } } }\n" + Staff));

    [Fact]
    public void AWrappedRowsNumber_IsCountedFromTheWrap_NotFromANoteTaken()
        => Assert.Equal(new[] { "1:a", "2:b" }, Numbers(
            Head + "section A { m { c''2 d'' | } lyrics w sings m { a | ~ b | } }\n" + Staff));

    [Fact]
    public void TwoNumbersFallingOnOneNote_BothStay()
        => Assert.Equal(new[] { "2:aa", "3:bb" }, Numbers(
            Head + "section A { m { c''1 | } lyrics w sings m { verse 2 { aa | } } lyrics x sings m { bb | } }\n"
            + "form { A }\nscore { staff m  lyrics w  lyrics x }\n"));

    [Fact]
    public void ADrumNote_TakesASyllable()
    {
        var doc = new MusicXmlExporter().Export(Parse(
            "part kit { clef percussion }\nsection A { kit { bd4 sn hh hh | } lyrics w sings kit { a b c d | } }\n"
            + "form { A }\nscore { staff kit  lyrics w }\n")).ToXml();
        Assert.Equal(4, doc.Descendants("lyric").Count());
    }

    [Fact]
    public void TheMidi_SingsThePartSung_WhereAPhraseIsPlayedAtTwoMoments()
        => Assert.Equal(new[] { (1920, "aa"), (2880, "bb") }, Events(
            "octave absolute\ntime 4/4\nphrase riff { c''2 d'' | }\npart m { clef treble }\npart n { clef treble }\n"
            + "section A { m { riff s1 | } n { s1 | riff } lyrics w sings n { | aa bb | } }\n"
            + "form { A }\nscore { staff m\n staff n  lyrics w }\n"));

    private static string[] Numbers(string source)
        => new MusicXmlExporter().Export(Parse(source)).ToXml().Descendants("lyric")
            .Select(l => l.Attribute("number")!.Value + ":" + l.Element("text")!.Value).ToArray();

    [Fact]
    public void AVersesNumberIsWritten()
        => Assert.Equal(new[] { "1:aa", "2:cc", "1:bb", "2:dd" }, Numbers(
            Head + "section A { m { c''2 d'' | } lyrics w sings m { verse 1 { aa bb | } verse 2 { cc dd | } } }\n" + Staff));

    [Fact]
    public void WordsPastTheLastBar_AreTheNextVerse()
        => Assert.Equal(new[] { "1:a", "2:c", "1:b", "2:d" }, Numbers(
            Head + "section A { m { c''1 | d''1 | } lyrics w sings m { a | b | c | d | } }\n" + Staff));

    [Fact]
    public void ASectionSingingOnlyTheSecondTrack_KeepsItsNumber()
        => Assert.Equal(new[] { "1:aa", "2:bb", "2:cc" }, Numbers(
            Head + "section A { m { c''1 | } lyrics w sings m { aa | } lyrics x sings m { bb | } }\n"
            + "section B { m { d''1 | } lyrics x sings m { cc | } }\n"
            + "form { A B }\nscore { staff m  lyrics w  lyrics x }\n"));

    [Fact]
    public void AnUnboundRow_KeepsItsWords_OnTheStaffAbove()
    {
        var doc = new MusicXmlExporter().Export(Parse(
            "octave absolute\ntime 4/4\npart m { clef treble }\npart n { clef treble }\n"
            + "section A { m { c''1 | } n { e'2 f' | } lyrics w { aa bb | } }\n"
            + "form { A }\nscore { staff m\n staff n  lyrics w }\n")).ToXml();
        var parts = doc.Descendants("part").ToList();
        Assert.Empty(parts[0].Descendants("lyric"));
        Assert.Equal(new[] { "aa", "bb" }, parts[1].Descendants("lyric").Select(l => l.Element("text")!.Value));
    }

    [Fact]
    public void AQuotedSyllable_IsWrittenAsItsWords()
    {
        var doc = new MusicXmlExporter().Export(Parse(
            Head + "section A { m { c''4 d'' e'' f'' | } lyrics w sings m { \"a\"- b \"-\" \"la-\" | } }\n" + Staff)).ToXml();
        Assert.Equal(new[] { "a/begin", "b/end", "-/single", "la-/single" },
            doc.Descendants("lyric").Select(l => l.Element("text")!.Value + "/" + l.Element("syllabic")!.Value));
    }

    /// <summary>A phrase two parts play stands at one source position; the words go on the part sung.</summary>
    [Fact]
    public void APhraseTwoPartsPlay_TakesTheWordsOnThePartSung()
    {
        var doc = new MusicXmlExporter().Export(Parse(
            "octave absolute\ntime 4/4\nphrase riff { c''2 d'' | }\npart m { clef treble }\npart n { clef treble }\n"
            + "section A { m { riff } n { riff } lyrics w sings n { aa bb | } }\n"
            + "form { A }\nscore { staff m\n staff n  lyrics w }\n")).ToXml();
        var parts = doc.Descendants("part").ToList();
        Assert.Empty(parts[0].Descendants("lyric"));
        Assert.Equal(2, parts[1].Descendants("lyric").Count());
    }
}
