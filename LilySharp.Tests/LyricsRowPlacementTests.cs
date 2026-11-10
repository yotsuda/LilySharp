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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>One lyrics track placed more than once in a score: on several rows, or as a staff's
/// verse and a row. Each placement is its own (docs/lyrics-singer-design.md §1, §4).</summary>
public class LyricsRowPlacementTests
{
    private const string Music = """
        time 4/4
        part s { clef treble }
        part a { clef treble }
        part t { clef treble }
        part x { clef treble }
        part bass { clef bass }
        section A { s { c''4 d e f | } a { e'2 f | } t { g4. a8 b2 | } x { c'1 | } bass { c4 d e f | } }
        lyrics w { あ い う え | }
        lyrics v { か き く け | }
        form { A }

        """;

    private static MultiStaffScore Page(string score, out SyntaxTree tree)
    {
        tree = SyntaxTree.Parse(Music + score + "\n");
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    private static int[] SyllablesPerLine(string score)
        => [.. Page(score, out _).Lyrics.GroupBy(l => l.StaffIndex).OrderBy(g => g.Key).Select(g => g.Count())];

    [Theory]
    [InlineData("score { staff s  staff a  lyrics w on s  staff t }", new[] { 4 })]                     // a row on another part
    [InlineData("score { staff s  staff a  lyrics w on a  staff t  lyrics w on s }", new[] { 2, 4 })]    // two rows of the track
    [InlineData("score { lyrics w on s  staff s  staff a  lyrics w on s  staff t }", new[] { 4, 4 })]    // the same part twice
    [InlineData("score { staff s  lyrics w  staff a  staff t  lyrics w on a }", new[] { 4, 2 })]         // a verse and a row
    [InlineData("score { lyrics w on t  staff s  lyrics w  staff a  staff t }", new[] { 3, 4 })]         // a row, then a verse
    [InlineData("score { lyrics w  staff s  staff a  lyrics w on s  staff t }", new[] { 4, 4 })]         // an even row and a bound one
    [InlineData("score { choirStaff { staff s  staff a  lyrics w on s  staff t  lyrics w on a } }", new[] { 4, 2 })]
    public void EveryPlacementOfATrack_PrintsItsOwnSyllables(string score, int[] expected)
        => Assert.Equal(expected, SyllablesPerLine(score));

    [Theory]
    [InlineData("s")]
    [InlineData("a")]
    [InlineData("bass")]
    public void OnNamesAnyPart_WhateverItsNameLexesAs(string part)
    {
        var root = SyntaxTree.Parse(Music + $"score {{ lyrics w on {part}  staff x }}\n").GetRoot();
        Assert.Equal(part, RenderSpecParser.RowTarget(root.DescendantNodes<LyricsRowRenderSyntax>().Single()));
    }

    [Fact]
    public void TwoRowsOnMelodiesNoStaffShows_EachKeepItsOwnRhythm()
    {
        // Each row's moments join the spacing (LilyPond's \lyricsto over a NullVoice).
        var page = Page("score { lyrics w on s  staff x  lyrics w on t }", out _);
        var rows = page.StaffGroups.SelectMany(g => g.Staves).Where(s => s.PrimaryVoice.Name == "w").ToList();
        Assert.Equal([4, 3], rows.Select(r => r.PrimaryVoice.Measures[0].Items.Length));
    }

    [Fact]
    public void TwoRowsOnMelodiesNoStaffShows_EachReadTheirOwnNotes()
    {
        Page("score { lyrics w on s  staff x  lyrics w on t }", out var tree);
        var read = SungSyllables.Read(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(["s", "s", "s", "s", "t", "t", "t"], read.Select(s => s.Part).Order());
    }

    [Fact]
    public void TwoRowsOnParts_EachReadTheirOwnNotes()
    {
        Page("score { staff s  staff a  lyrics w on s  staff t  lyrics w on a }", out var tree);
        var read = SungSyllables.Read(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(["a", "a", "s", "s", "s", "s"], read.Select(s => s.Part).Order());
    }

    [Fact]
    public void AVerseAndARowOfATrack_AreDifferentLines_ThePageKeepsApart()
    {
        var page = Page("score { staff s  lyrics w  staff a  staff t  lyrics w on a }", out _);
        Assert.Equal([false, true], page.Lyrics.GroupBy(l => l.StaffIndex).OrderBy(g => g.Key).Select(g => g.First().IsLyricsRow));
    }

    private static string Twin(string score) => new LilyPondExporter().Export(SyntaxTree.Parse(Music + score + "\n"));

    private static List<string> ScoreLines(string ly)
        => [.. ly[ly.IndexOf("\\score", StringComparison.Ordinal)..].Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("\\new", StringComparison.Ordinal))];

    [Fact]
    public void TheTwin_StandsEachRowWhereItIsWritten()
    {
        // Neither row folds: the first has no staff above, the second follows another part.
        var lines = ScoreLines(Twin("score { lyrics w on s  staff s  staff a  lyrics w on s  staff t }"));
        Assert.Equal(["Lyrics", "Staff", "Staff", "Lyrics", "Staff"], lines.Select(l => l.Split(' ')[1]));
        Assert.NotEqual(lines[0], lines[3]);
    }

    [Fact]
    public void TheTwin_DoesNotMoveARowToAVersesPlace()
    {
        var lines = ScoreLines(Twin("score { staff s  lyrics w  staff a  staff t  lyrics w on a }"));
        Assert.Equal(["Staff", "Lyrics", "Staff", "Staff", "Lyrics"], lines.Select(l => l.Split(' ')[1]));
    }

    [Fact]
    public void TheTwin_StandsEachRowInAGroupWhereItIsWritten()
    {
        var lines = ScoreLines(Twin("score { choirStaff { staff s  staff a  lyrics w on s  staff t  lyrics w on a } }"));
        var lyrics = lines.Where(l => l.StartsWith("\\new Lyrics", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lyrics.Count);
        Assert.NotEqual(lyrics[0], lyrics[1]);
    }
}
