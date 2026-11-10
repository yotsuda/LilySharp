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
