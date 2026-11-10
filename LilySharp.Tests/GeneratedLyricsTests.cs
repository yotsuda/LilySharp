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

using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>The words a file does not write: <c>hum</c> and <c>solfege</c> (docs/lyrics-singer-design.md §6).</summary>
public class GeneratedLyricsTests
{
    private static string Book(string music, string score, string head = "") => $$"""
        time 4/4
        {{head}}
        part m { clef treble }
        section A { m { {{music}} } }
        form { A }
        {{score}}
        """;

    private static string[] Words(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return score.Lyrics.OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing).Select(l => l.Text).ToArray();
    }

    private static IReadOnlyList<Diagnostic> Validate(string src) => SemanticValidation.Run(SyntaxTree.Parse(src));

    private const string Scale = "c'8 d e f g a b c |";
    private const string Printed = "score { staff m  lyrics solfege }";

    [Theory]
    [InlineData("", "ド レ ミ ファ ソ ラ シ ド")]
    [InlineData("language english", "do re mi fa sol la ti do")]
    [InlineData("language chinese", "哆 来 咪 发 唆 拉 西 哆")]
    [InlineData("language korean", "도 레 미 파 솔 라 시 도")]
    public void TheFilesLanguagePicksTheWords(string language, string words)
        => Assert.Equal(words.Split(' '), Words(Book(Scale, Printed, language)));

    [Theory]
    [InlineData(0, 0, "ド")]    // C major: C is do
    [InlineData(4, 1, "ド")]    // G major: G is do
    [InlineData(3, -1, "ド")]   // F major: F is do
    [InlineData(6, -2, "ド")]   // B flat major: B is do
    [InlineData(5, 0, "ラ")]    // A minor, no signature: A is la
    [InlineData(2, 1, "ラ")]    // E minor, one sharp: E is la
    public void DoIsTheSignaturesMajorTonic(int step, int sharps, string name)
        => Assert.Equal(name, GeneratedLyrics.SolfegeName(step, sharps, "ja"));

    [Fact]
    public void AKeyChangeMovesDo()
        => Assert.Equal(["ド", "ファ"], Words(Book("c'2 r | key g major c'2 r |", Printed)));

    [Fact]
    public void AnAlteredNoteTakesTheNameOfItsLetter()
        => Assert.Equal(["ファ", "ファ"], Words(Book("f'2 fis |", Printed)));

    [Fact]
    public void ATiedNoteKeepsItsSyllable_ARestAndAGraceNoteTakeNone()
        => Assert.Equal(["ド", "レ"], Words(Book("c'4~ c r grace { e8 } d |", Printed)));

    [Fact]
    public void AChordTakesItsTopNote()
        => Assert.Equal(["ソ"], Words(Book("<c' e g>1 |", Printed)));

    [Fact]
    public void ARowOnAnotherPartFollowsThatPart()
    {
        const string src = """
            time 4/4
            part sop { clef treble }
            part alt { clef treble }
            section A { sop { c'1 | } alt { e'2 f | } }
            form { A }
            score { staff sop  lyrics solfege on alt  staff alt }
            """;
        Assert.Equal(["ミ", "ファ"], Words(src));
    }

    [Fact]
    public void ARowOnANamedVoiceFollowsThatVoice()
        => Assert.Equal(["ミ"], Words(Book("voice upper { c''2 d | } lower { e'1 | }", "score { staff m  lyrics solfege on lower }")));

    [Fact]
    public void TheWrittenWordsComeFirst_TheNoteNamesUnderThem()
    {
        var src = Book("c'2 d |", "score { staff m  lyrics solfege  lyrics w }").Replace("form { A }", "lyrics w { la la | }\nform { A }");
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(["ド", "レ"], score.Lyrics.Where(l => l.VerseNumber == 2).Select(l => l.Text));
        Assert.Equal(["la", "la"], score.Lyrics.Where(l => l.VerseNumber == 1).Select(l => l.Text));
    }

    [Fact]
    public void ASingerHumsWithNothingPrinted()
    {
        var src = Book("c'2 d |", "singer female sings hum on m\nscore { staff m }");
        Assert.DoesNotContain(Validate(src), d => d.Severity == DiagnosticSeverity.Error);
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.Empty(new MeasureCollector().CollectMultiStaff(tree, spec).Lyrics);
        var sung = SungSyllables.Read(tree, LyricSinger.WithSungWords(tree.GetRoot(), spec));
        Assert.Equal(["ん", "ん"], sung.Select(s => s.Lyric.Text));
        Assert.All(sung, s => Assert.Equal("hum", s.Track));
    }

    [Fact]
    public void AHumIsJapanese_WhateverTheFilesLanguage()
    {
        var root = SyntaxTree.Parse("language english\n").GetRoot();
        Assert.Equal("ja", LyricLanguage.ReaderOf(root)("hum"));
        Assert.Equal("en", LyricLanguage.ReaderOf(root)("solfege"));
    }

    [Fact]
    public void ASingerSingsTheNoteNames()
    {
        var src = Book("c'2 d |", "singer female sings solfege on m\nscore { staff m }");
        Assert.DoesNotContain(Validate(src), d => d.Severity == DiagnosticSeverity.Error);
        var tree = SyntaxTree.Parse(src);
        var sung = SungSyllables.Read(tree, LyricSinger.WithSungWords(tree.GetRoot(), RenderSpecParser.FindFirst(tree)!));
        Assert.Equal(["ド", "レ"], sung.Select(s => s.Lyric.Text));
    }

    [Fact]
    public void HumHasNothingToPrint()
        => Assert.Contains(Validate(Book("c'1 |", "score { staff m  lyrics hum }")), d => d.Code == DiagnosticCodes.LyricsHumNotPrinted);

    [Fact]
    public void TheNoteNamesNeedNotes()
        => Assert.Contains(Validate(Book("c'1 |", "score { lyrics solfege  staff m }")), d => d.Code == DiagnosticCodes.LyricsRowNeedsOn);

    [Fact]
    public void TheTwinWritesTheNoteNames()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("c'2 d |", Printed)));
        Assert.Contains("ド", ly);
        Assert.Contains("レ", ly);
    }
}
