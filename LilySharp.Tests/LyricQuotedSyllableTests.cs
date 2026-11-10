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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A quoted syllable is its words and nothing else, and a hyphen after the closing quote is the
/// connector (GRAMMAR LyricSyllable, LyricSyllableReader.Read).
/// </summary>
[Trait("Category", "Unit")]
public class LyricQuotedSyllableTests
{
    private const string Header = "octave absolute\ntime 4/4\npart m { clef treble }\n";

    /// <summary>The page's syllables of a sung line, as "text@item" with "+" for a hyphen after it.</summary>
    private static string[] Sung(string words) => Page(
        Header + "section A { m { c''4 d'' e'' f'' | } lyrics w { " + words + " } }\n"
        + "form { A }\nscore { staff m  lyrics w on m }\n")
        .OrderBy(l => l.ItemIndex)
        .Select(Signature).ToArray();

    /// <summary>The same words on a row with no melody.</summary>
    private static string[] Row(string words) => Page(
        "time 4/4\nlyrics w { " + words + " }\nscore { lyrics w on m }\n")
        .Where(l => l.IsLyricsRow)
        .OrderBy(l => l.ItemIndex)
        .Select(Signature).ToArray();

    private static string Signature(LyricItem l)
        => l.Text + "@" + l.ItemIndex + (l.ConnectorType == LyricConnectorType.Hyphen ? "+" : "");

    private static System.Collections.Immutable.ImmutableArray<LyricItem> Page(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).Lyrics;
    }

    [Fact]
    public void AHyphenAfterTheClosingQuote_IsTheConnector_AndTheQuotesAreNotPrinted()
        => Assert.Equal(new[] { "a b@0+", "c@1", "d e@2+", "f@3" }, Sung("\"a b\"- c \"d e\"-- f |"));

    [Fact]
    public void AHyphenStandingApartFromTheQuote_ReadsTheSame()
        => Assert.Equal(new[] { "a@0+", "b@1", "c@2", "d@3" }, Sung("\"a\" -- b c d |"));

    [Fact]
    public void QuotedMarks_AreWords_TakingANoteEach()
        => Assert.Equal(new[] { "-@0", "~@1", "_@2", "--@3" }, Sung("\"-\" \"~\" \"_\" \"--\" |"));

    [Fact]
    public void AQuotedBar_IsAWord_NotAMeasureBoundary()
        => Assert.Equal(new[] { "x@0", "|@1", "y@2" }, Sung("x \"|\" y |"));

    [Fact]
    public void AQuotedWordEndingInAHyphen_KeepsIt_WithNoConnector()
        => Assert.Equal(new[] { "là-@0", "b@1", "c@2", "d@3" }, Sung("\"là-\" b c d |"));

    [Fact]
    public void AQuotedTilde_IsNotAnElision()
        => Assert.Equal(new[] { "a~b@0", "c‿d@1" }, Sung("\"a~b\" c~d |"));

    [Fact]
    public void TheRowReadsThemTheSameWay()
        => Assert.Equal(new[] { "a b@0+", "-@1", "c‿d@2", "là-@3" }, Row("\"a b\"- \"-\" c~d \"là-\" |"));

    [Fact]
    public void AQuotedBarDoesNotCloseARowsBar()
    {
        // No bar line of its own after it: the row's closing token would be the quoted one.
        var tree = SyntaxTree.Parse("time 4/4\nlyrics w { x \"||\" }\nscore { lyrics w on m }\n");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var bars = score.StaffGroups.SelectMany(g => g.Staves).First().PrimaryVoice.Measures;
        Assert.Equal(BarlineType.Single, bars[0].EndBarline);
    }

    /// <summary>LilyPond's way of leaving a note unsung: the next word stands on the next note.</summary>
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    public void AQuotedBlank_TakesItsNote_AndPrintsNothing(string blank)
        => Assert.Equal(new[] { "a@0", "c@2", "d@3" }, Sung("a " + blank + " c d |"));

    [Fact]
    public void AMeltingMarkAfterABlank_HoldsNothing()
    {
        var l = Page(Header + "section A { m { c''4 d'' e'' f'' | } lyrics w { a \"\" ~ d | } }\nform { A }\nscore { staff m  lyrics w on m }\n")
            .OrderBy(x => x.ItemIndex).ToList();
        Assert.Equal(new[] { "a", "d" }, l.Select(x => x.Text));
        Assert.False(l[0].MelismaAlignLeft);
        Assert.Equal(3, l[1].ItemIndex);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    public void ABlankOnARow_KeepsItsShareOfTheBar_AndPrintsNothing(string blank)
    {
        var words = Page("time 4/4\nlyrics w { a " + blank + " c d | }\nscore { lyrics w on m }\n");
        Assert.Equal(new[] { "a", "c", "d" }, words.Select(l => l.Text));
        Assert.Equal(new[] { 0, 2, 3 }, words.Select(l => l.ItemIndex));
    }

    [Fact]
    public void BareSpellings_AreUnchanged()
        => Assert.Equal(new[] { "Hap@0+", "py@1", "la@2+", "la@3" }, Sung("Hap- py la -- la |"));
}
