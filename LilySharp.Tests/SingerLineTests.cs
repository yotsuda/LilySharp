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

using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>One voice a <c>singer</c> line; a line in a score over the top level's for its part
/// (docs/lyrics-singer-design.md §2, §3, §8).</summary>
public class SingerLineTests
{
    private const string Music = """
        time 4/4
        part m { clef treble }
        section A { m { c'2 d | } }
        lyrics ja { あ い | }
        lyrics en { la la | }
        form { A }

        """;

    private static IReadOnlyList<Diagnostic> Validate(string src) => SemanticValidation.Run(SyntaxTree.Parse(src));

    private static List<SungVoice> Sung(string src, string? score = null)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), score);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        return SungVoices.Read(midi, MidiClock.SecondsOf(midi));
    }

    [Fact]
    public void TwoLinesOnTheSameWords_AreTwoVoicesOfThePart()
    {
        var src = Music + "singer \"n/A\" sings ja on m\nsinger \"n/B\" sings ja on m\nscore { staff m  lyrics ja }\n";
        Assert.DoesNotContain(Validate(src), d => d.Severity == DiagnosticSeverity.Error);

        var voices = Sung(src);
        Assert.Equal(["m", "m#2"], voices.Select(v => v.Key));
        Assert.Equal(["n/A", "n/B"], voices.Select(v => v.Singer));
        Assert.All(voices, v => Assert.Equal("m", v.Part));
        Assert.Equal(voices[0].Notes.Count, voices[1].Notes.Count);
    }

    [Fact]
    public void TheDefaultVoiceAndANamedOne_AreTwoVoicesToo()
    {
        var voices = Sung(Music + "singer sings ja on m\nsinger \"n/B\" sings ja on m\nscore { staff m  lyrics ja }\n");
        Assert.Equal([null, "n/B"], voices.Select(v => v.Singer));
    }

    [Fact]
    public void AGenderOnASecondLine_IsThatVoicesGender()
    {
        var voices = Sung(Music + "singer male sings ja on m\nsinger female sings ja on m\nscore { staff m  lyrics ja }\n");
        Assert.Equal(["male", "female"], voices.Select(v => v.Gender));
    }

    [Theory]
    [InlineData("singer \"n/A\" sings ja on m\nsinger \"n/A\" sings ja on m\n")]
    [InlineData("singer female sings ja on m\nsinger female sings ja on m\n")]
    public void TheSameVoiceTwice_IsWarnedAbout(string lines)
    {
        var d = Assert.Single(Validate(Music + lines + "score { staff m  lyrics ja }\n"), d => d.Code == DiagnosticCodes.SingerRepeated);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void OtherWordsOnThePart_AtTheTopLevel_AreRefused_AndSayWhere()
    {
        var d = Assert.Single(Validate(Music + "singer \"n/A\" sings ja on m\nsinger \"n/B\" sings en on m\nscore { staff m  lyrics ja }\n"),
            d => d.Code == DiagnosticCodes.SingerDuplicatePart);
        Assert.Contains("inside that score", d.Message);
    }

    [Fact]
    public void OtherWordsOnThePart_InOneScore_AreRefused()
    {
        var d = Assert.Single(Validate(Music + "score { singer \"n/A\" sings ja on m\nsinger \"n/B\" sings en on m\nstaff m  lyrics ja }\n"),
            d => d.Code == DiagnosticCodes.SingerDuplicatePart);
        Assert.Contains("in this score", d.Message);
    }

    [Fact]
    public void TheCommaIsGone()
        => Assert.Contains(SyntaxTree.Parse(Music + "singer \"n/A\",\"n/B\" sings ja on m\nscore { staff m }\n").Diagnostics,
            d => d.Code == DiagnosticCodes.SingerNeedsSings);

    private const string TwoScores = Music + """
        singer "n/A" sings ja on m
        score jp { staff m  lyrics ja }
        score us {
          singer "n/B" sings en on m
          staff m  lyrics en
        }

        """;

    [Fact]
    public void AScoresLine_ReplacesTheTopLevelsForItsPart()
    {
        Assert.DoesNotContain(Validate(TwoScores), d => d.Severity == DiagnosticSeverity.Error);

        var jp = Assert.Single(Sung(TwoScores, "jp"));
        Assert.Equal(("n/A", "ja"), (jp.Singer, jp.Track));
        Assert.Equal("あ", jp.Notes[0].Lyric);

        var us = Assert.Single(Sung(TwoScores, "us"));
        Assert.Equal(("n/B", "en"), (us.Singer, us.Track));
        Assert.Equal("la", us.Notes[0].Lyric);
    }

    [Fact]
    public void AScoreWithNoLineOfItsOwn_KeepsTheTopLevels()
    {
        var root = SyntaxTree.Parse(TwoScores).GetRoot();
        var jp = RenderSpecParser.FindAll(root).Single(s => s.Name == "jp");
        Assert.Equal(["n/A"], LyricSinger.LinesFor(root, jp).Select(l => l.Singer));
    }

    [Fact]
    public void AnotherPartKeepsTheTopLevelsLine()
    {
        const string src = """
            time 4/4
            part sop { clef treble }
            part alt { clef treble }
            section A { sop { c'2 d | } alt { e'2 f | } }
            lyrics w { la la | }
            form { A }
            singer "n/S" sings w on sop
            singer "n/T" sings w on alt
            score { singer "n/X" sings w on alt
            staff sop  lyrics w  staff alt  lyrics w }
            """;
        var root = SyntaxTree.Parse(src).GetRoot();
        var lines = LyricSinger.LinesFor(root, RenderSpecParser.FindAll(root)[0]);
        Assert.Equal(["sop n/S", "alt n/X"], lines.Select(l => $"{l.Part} {l.Singer}"));
    }

    private const string FourOnOneTrack = """
        time 4/4
        part s { clef treble }
        part a { clef treble }
        part t { clef treble }
        part b { clef bass }
        section A { s { c''2 d | } a { e'2 f | } t { g2 a | } b { c2 d | } }
        lyrics w { あ い | }
        lyrics hm { ん ん | }
        form { A }
        singer "n/S" sings w on s
        singer "n/A" sings w on a
        singer "n/T" sings w on t
        singer "n/B" sings w on b

        """;

    // Owner's report 2026-11-10: only the tenor of a four-part choir was listed, its other three lost.
    [Fact]
    public void OneTrackOnFourParts_PrintedUnderTwoAndNotTheOthers_IsFourVoices()
    {
        var voices = Sung(FourOnOneTrack + "score { choirStaff { staff s  lyrics w  staff a  lyrics w  staff t  lyrics hm  staff b  lyrics hm } }\n");
        Assert.Equal(["s n/S", "a n/A", "t n/T", "b n/B"], voices.Select(v => $"{v.Part} {v.Singer}"));
        Assert.All(voices, v => Assert.Equal(["あ", "い"], v.Notes.Select(n => n.Lyric)));
        Assert.Equal([72.0, 64, 55, 48], voices.Select(v => v.Notes[0].Pitch));
    }

    [Fact]
    public void OneTrackOnPartsNoStaffShows_IsAVoiceEach()
    {
        var src = FourOnOneTrack + "score { staff s  lyrics w  staff a hidden  staff t hidden  staff b hidden }\n";
        var voices = Sung(src);
        Assert.Equal(["s", "a", "t", "b"], voices.Select(v => v.Part));
        Assert.Equal([72.0, 64, 55, 48], voices.Select(v => v.Notes[0].Pitch));
    }

    // A row is known by its track's name alone, so a track printed on a row that folds under no staff
    // used to take that track off every staff another line sang it on.
    [Theory]
    [InlineData("staff s  staff a  staff t  staff b  lyrics w on s")]           // a row under another part's staff
    [InlineData("lyrics w on s  staff s  staff a  staff t  staff b")]           // a row above every staff
    [InlineData("lyrics w  staff s  staff a  staff t  staff b")]                // an even-spread row
    [InlineData("staff s  staff a  staff t  staff b  lyrics w on s  lyrics w on t")]   // two rows of the track
    [InlineData("choirStaff { staff s  staff a  staff t  staff b  lyrics w on s }")]   // a row inside a group
    [InlineData("staff s  lyrics w  staff a  staff t  staff b  lyrics w on a")]  // a verse and a row of the track
    [InlineData("tab s  staff a  staff t  staff b  lyrics w on a")]             // a tab
    [InlineData("condensedStaff { s a }  staff t  staff b  lyrics w on t")]     // a staff of several parts
    [InlineData("combinedStaff { s a }  staff t  staff b  lyrics w on t")]
    public void OneTrackPrintedOnARow_IsStillAVoiceOnEveryPartThatSingsIt(string score)
    {
        var voices = Sung(FourOnOneTrack + "score { " + score + " }\n");
        Assert.Equal(["s n/S", "a n/A", "t n/T", "b n/B"], voices.Select(v => $"{v.Part} {v.Singer}").Order(Comparer<string>.Create((x, y) => "satb".IndexOf(x[0]) - "satb".IndexOf(y[0]))));
        Assert.All(voices, v => Assert.Equal(["あ", "い"], v.Notes.Select(n => n.Lyric)));
        Assert.Equal([72.0, 64, 55, 48], voices.OrderBy(v => "satb".IndexOf(v.Part[0])).Select(v => v.Notes[0].Pitch));
    }

    [Fact]
    public void ARowOnAPartNoLineSings_AddsNoVoice()
    {
        var src = Music.Replace("part m { clef treble }", "part m { clef treble }\npart n { clef treble }").Replace("m { c'2 d | }", "m { c'2 d | } n { e'2 f | }");
        var voice = Assert.Single(Sung(src + "singer \"n/A\" sings ja on m\nscore { staff m  staff n  lyrics ja on n }\n"));
        Assert.Equal("m", voice.Part);
        Assert.Equal(["あ", "い"], voice.Notes.Select(n => n.Lyric));
    }

    private const string TwoParts = """
        time 4/4
        part m { clef treble }
        part n { clef treble }
        section A { m { c'2 d | } n { voice v1 { e'2 f | } v2 { c'2 d | } } }
        lyrics ja { あ い | }
        lyrics en { la la | }
        form { A }

        """;

    [Fact]
    public void ARowOfASungTrack_KeepsItsWordsOnThePartNoLineSings()
    {
        var tree = SyntaxTree.Parse(TwoParts + "singer sings ja on m\nscore { lyrics ja on n  staff m  staff n }\n");
        var read = LyricSinger.WithSungWords(tree.GetRoot(), RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(["m", "m", "n", "n"], SungSyllables.Read(tree, read).Select(s => s.Part).Order());
    }

    [Fact]
    public void ARowOfATrackNoLineSings_StaysARow()
    {
        var tree = SyntaxTree.Parse(TwoParts + "singer sings ja on m\nscore { lyrics en on n  staff m  lyrics ja  staff n }\n");
        var read = LyricSinger.WithSungWords(tree.GetRoot(), RenderSpecParser.FindFirst(tree)!);
        Assert.Equal("en", Assert.Single(read.AllLyricsRows).PartName);
    }

    [Fact]
    public void ARowOnAVoice_GoesOnThatVoicesPart_NotOnAStaffOfItsOwn()
    {
        var tree = SyntaxTree.Parse(TwoParts + "singer sings ja on v2\nscore { lyrics ja on v2  staff m  staff n }\n");
        var read = LyricSinger.WithSungWords(tree.GetRoot(), RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(["m", "n"], read.GetVoiceBindings().Select(b => b.VoiceName));
        Assert.Equal(["ja"], read.GetVoiceBindings().Single(b => b.VoiceName == "n").WithLyrics);
    }

    [Fact]
    public void SungWordsMoved_LeaveTheRowsOfTheDrawnSpecAlone()
    {
        var tree = SyntaxTree.Parse(FourOnOneTrack + "score { lyrics w on s  staff s  staff a  staff t  staff b }\n");
        var spec = RenderSpecParser.FindFirst(tree)!;
        var read = LyricSinger.WithSungWords(tree.GetRoot(), spec);
        Assert.Single(spec.AllLyricsRows);
        Assert.Empty(read.AllLyricsRows);
    }

    [Fact]
    public void SungWordsAdded_LeaveTheDrawnPageAlone()
    {
        var tree = SyntaxTree.Parse(FourOnOneTrack + "score { staff s  lyrics hm  staff a }\n");
        var spec = RenderSpecParser.FindFirst(tree)!;
        var read = LyricSinger.WithSungWords(tree.GetRoot(), spec);
        Assert.NotSame(spec, read);
        Assert.Equal(["s", "a"], spec.GetVoiceBindings().Select(b => b.VoiceName));
        Assert.Equal(["s", "a", "t", "b"], read.GetVoiceBindings().Select(b => b.VoiceName));
    }

    [Fact]
    public void SameWordsLinesInAScore_AreTwoVoices()
    {
        var voices = Sung(Music + "score { singer \"n/A\" sings ja on m\nsinger \"n/B\" sings ja on m\nstaff m  lyrics ja }\n");
        Assert.Equal(["n/A", "n/B"], voices.Select(v => v.Singer));
    }

    [Fact]
    public void WithNoOn_AScoresLineFindsThePartItPrints()
    {
        const string src = """
            time 4/4
            part sop { clef treble }
            part alt { clef treble }
            section A { sop { c'2 d | } alt { e'2 f | } }
            lyrics w { la la | }
            form { A }
            score a { singer "n/A" sings w
            staff sop  staff alt  lyrics w }
            score b { staff sop  lyrics w  staff alt }
            """;
        Assert.Equal("alt", Assert.Single(LyricSinger.LinesOf(SyntaxTree.Parse(src).GetRoot())).Part);
    }

    [Fact]
    public void ASoundsVoice_AcceptsASingerOfItsOwnScore_AndNotAnothers()
    {
        var ok = Music + "score { singer \"n/A\" sings ja on m\nstaff m sounds voice }\n";
        Assert.DoesNotContain(Validate(ok), d => d.Code == DiagnosticCodes.StaffSoundNeedsSinger);

        var other = Music + "score a { singer \"n/A\" sings ja on m\nstaff m }\nscore b { staff m sounds voice }\n";
        Assert.Contains(Validate(other), d => d.Code == DiagnosticCodes.StaffSoundNeedsSinger);
    }

    [Fact]
    public void ASingerInAScore_IsReadAsAStatement_NotAsAnItem()
        => Assert.DoesNotContain(SyntaxTree.Parse(TwoScores).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
}
