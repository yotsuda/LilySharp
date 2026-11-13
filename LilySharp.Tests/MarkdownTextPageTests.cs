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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A fenced Markdown text on the page (docs/markdown-text-design.md, section 8 step 1): the
/// text under the title and the text after the last system — paragraphs, bold and italic —
/// as lines of the page's chain, spaced as LilyPond 2.26.0 spaces a top-level markup list
/// (measured 2026-11-12, LilySharp-Lab sessions/p1226/md).
/// </summary>
[Trait("Category", "Unit")]
public class MarkdownTextPageTests
{
    private const string F = "```";

    private static string Book(string top = "", string inScore = "", string head = "title \"Morning Song\"\ncomposer \"A. Composer\"") => $$"""
        {{head}}
        {{top}}
        octave relative
        part m { clef treble }
        section A { m { c'4 d e f | g1 | c,4 d e f | g1 | } }
        form { A }
        score { {{inScore}} staff m }
        """;

    private static ScoreLayout Laid(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static ProseBand Band(string markdown, double width = 100)
        => ProseBand.Build(markdown, 0, ScoreTextMetrics.Bundled, width)!;

    private static string TextOf(ProseLine line) => string.Concat(line.Pieces.Select(p => p.Text));

    // ---- reading -----------------------------------------------------------------------

    [Fact]
    public void Paragraphs_BoldAndItalic_AreRead()
    {
        var prose = Prose.Read("Plain **bold** and *slanted* and ***both***.\n\nSecond.");
        Assert.Equal(2, prose.Blocks.Length);
        Assert.Equal(
            [("Plain ", FontStyle.Regular), ("bold", FontStyle.Bold), (" and ", FontStyle.Regular),
             ("slanted", FontStyle.Italic), (" and ", FontStyle.Regular),
             ("both", FontStyle.Bold | FontStyle.Italic), (".", FontStyle.Regular)],
            prose.Blocks[0].Runs.Select(r => (r.Text, r.Style)));
        Assert.Empty(prose.NotDrawn);
        // A run knows where it was written.
        Assert.Equal("Plain **bold**".IndexOf("bold", System.StringComparison.Ordinal), prose.Blocks[0].Runs[1].Offset);
    }

    [Theory]
    [InlineData("> quoted", "A block quote")]
    [InlineData("    indented code", "A code block")]
    [InlineData("---", "A rule")]
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |", "A table")]
    [InlineData("<div>x</div>", "HTML")]
    [InlineData("see ![a hand](hand.png) here", "An image")]
    [InlineData("press `c4` now", "Code")]
    public void WhatIsNotDrawn_IsKnownForWhatItIs(string markdown, string what)
        => Assert.Equal(what, Assert.Single(Prose.Read(markdown).NotDrawn).What);

    [Fact]
    public void ACodeSpansWords_StayInTheSentence()
        => Assert.Equal("press c4 now", string.Concat(Prose.Read("press `c4` now").Blocks[0].Runs.Select(r => r.Text)));

    [Fact]
    public void WhatIsNotDrawn_IsAWarningWhereItIsWritten()
    {
        // (Headings and lists are drawn since 2026-11-13: MarkdownTextListTests.)
        string source = Book(top: $"{F}md\nA paragraph.\n\n> A quotation.\n{F}");
        var tree = SyntaxTree.Parse(source);
        var d = Assert.Single(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.FencedTextNotDrawn);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(source.IndexOf("> A quotation.", System.StringComparison.Ordinal), d.Span.Start);
        Assert.Equal("> A quotation.".Length, d.Span.Length);
        Assert.StartsWith("A block quote is read and not drawn", d.Message);
    }

    [Fact]
    public void ASectionsText_IsSaidNotToBePrintedYet()
    {
        string source = Book().Replace("section A { m", $"section A {{\n{F}md\ntext\n{F}\n m");
        var d = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(source)), d => d.Code == DiagnosticCodes.FencedTextNotDrawn);
        Assert.Contains("not printed yet", d.Message);
    }

    // ---- setting -----------------------------------------------------------------------

    [Fact]
    public void ALineIsABox_AndAParagraphStandsHalfALineOff()
    {
        var band = Band("one\n\ntwo\n\nthree");
        // The text's size is the composer line's, its lines step by the baseline-skip.
        Assert.Equal(2.2, ProseBand.Em(ScoreTextMetrics.Bundled), 6);
        Assert.Equal([0.0, 4.5, 9.0], band.Lines.Select(l => l.Baseline));
        // Two thirds of the skip over the first baseline, one third under the last.
        Assert.Equal(2.0, band.Up, 9);
        Assert.Equal(9.0 + 1.0, band.Down, 9);
    }

    [Fact]
    public void LinesBreakAtSpaces_AndTheSpaceAtTheBreakIsNotSet()
    {
        string words = string.Join(' ', Enumerable.Repeat("measure", 12));
        var band = Band(words, width: 30);
        Assert.True(band.Lines.Length > 1);
        Assert.All(band.Lines, l => Assert.True(l.Width <= 30 + 1e-9));
        Assert.All(band.Lines, l => Assert.Equal(TextOf(l).Trim(), TextOf(l)));
        Assert.Equal(words, string.Join(' ', band.Lines.Select(TextOf)));
        // Inside a paragraph the lines step by 3.
        Assert.Equal(3.0, band.Lines[1].Baseline - band.Lines[0].Baseline, 9);
        // A line end inside a paragraph is a space: the same lines.
        Assert.Equal(band.Lines.Select(TextOf), Band(words.Replace(" measure measure ", " measure\nmeasure "), 30).Lines.Select(TextOf));
    }

    [Fact]
    public void AWordIsNotCut_EvenWhereItsStyleChanges()
    {
        var band = Band("aaaa bbbb**cccc**dddd eeee", width: 9);
        Assert.Contains(band.Lines, l => TextOf(l) == "bbbbccccdddd");
        var line = band.Lines.Single(l => TextOf(l) == "bbbbccccdddd");
        Assert.Equal([FontStyle.Regular, FontStyle.Bold, FontStyle.Regular], line.Pieces.Select(p => p.Style));
        // Each piece begins where the one before it ends.
        Assert.True(line.Pieces[1].X > 0 && line.Pieces[2].X > line.Pieces[1].X);
    }

    [Fact]
    public void ALineEndedByHand_Ends()
        => Assert.Equal(["one", "two"], Band("one  \ntwo").Lines.Select(TextOf));

    [Fact]
    public void JapaneseBreaksBetweenCharacters_NeverBeforeAStopNorAfterAnOpeningBracket()
    {
        // 2.2 a character: four to a line of 9.
        var band = Band("あいうえ。かき「くけ」こ", width: 9);
        var lines = band.Lines.Select(TextOf).ToArray();
        Assert.Equal("あいうえ。かき「くけ」こ", string.Concat(lines));
        Assert.All(lines, l => Assert.False("。」".Contains(l[0]), l));
        Assert.All(lines, l => Assert.False(l[^1] == '「', l));
        Assert.True(lines.Length >= 3);
    }

    [Fact]
    public void ALineEndBetweenTwoJapaneseCharacters_IsNoSpace()
    {
        Assert.Equal("あいうえ", TextOf(Assert.Single(Band("あい\nうえ").Lines)));
        Assert.Equal("ab cd", TextOf(Assert.Single(Band("ab\ncd").Lines)));
        Assert.Equal("あい cd", TextOf(Assert.Single(Band("あい\ncd").Lines)));
    }

    [Fact]
    public void AnEmptyFence_OrOneThatDrawsNothing_SetsNoBand()
    {
        Assert.Null(ProseBand.Build("", 0, ScoreTextMetrics.Bundled, 100));
        Assert.Null(ProseBand.Build("\n\n", 0, ScoreTextMetrics.Bundled, 100));
        Assert.Null(ProseBand.Build("> only a quotation", 0, ScoreTextMetrics.Bundled, 100));
    }

    // ---- the page ----------------------------------------------------------------------

    [Fact]
    public void AFileWithNoFence_IsThePageItWas()
    {
        var page = Laid(Book()).Pages[0];
        Assert.Null(page.Header!.Text);
        Assert.Null(page.EndText);
    }

    /// <summary>MEASURED, LilyPond 2.26.0 (t1tw2.ly): the rows' ink, the padding 0.5 and the
    /// line's box over its baseline (2.0) between the composer line and the text; the text's
    /// box under its last baseline (1.0), the padding 0.5 and the system's ink over its first
    /// staff's refpoint between the text and the music.</summary>
    [Fact]
    public void TheTextUnderTheTitle_IsALineOfThePagesChain()
    {
        var plain = Laid(Book()).Pages[0];
        var page = Laid(Book(top: $"{F}md\nOne line.\n\nAnother.\n{F}")).Pages[0];
        var band = page.Header!.Text!;
        Assert.Equal(2, band.Lines.Length);
        // The rows stand where they stood.
        Assert.Equal(plain.HeaderTop, page.HeaderTop, 9);
        Assert.Equal(plain.Header!.Depth, page.Header.Depth, 9);
        // The first baseline: the rows' depth, markup-markup-spacing's padding, the box.
        Assert.Equal(page.HeaderTop + page.Header.Depth + 0.5 + 2.0, page.HeaderTextY, 6);
        // The music moved down by what the text takes: its lines and the box, less nothing
        // the rows had (their own spring to the system was floored by the same ink).
        double textBottom = page.HeaderTextY + band.Down;
        double plainRowsBottom = plain.HeaderTop + plain.Header.Depth;
        double systemTop = page.Height - page.Systems[0].Y, plainSystemTop = plain.Height - plain.Systems[0].Y;
        Assert.Equal(plainSystemTop - plainRowsBottom, systemTop - textBottom, 6);
    }

    [Fact]
    public void ATextWithNoTitleOverIt_OpensThePage()
    {
        var page = Laid(Book(top: $"{F}md\nOne line.\n{F}", head: "")).Pages[0];
        Assert.NotNull(page.Header);
        Assert.False(page.Header!.HasRows);
        // top-markup-spacing's padding and the line's box, from the top margin.
        var options = LayoutOptions.Default;
        Assert.True(page.HeaderTextY >= options.MarginTop + 1.0 + 2.0 - 1e-9);
        Assert.True(page.Height - page.Systems[0].Y > page.HeaderTextY + page.Header.Text!.Down);
    }

    /// <summary>MEASURED (e1.ly, t1tw2.ly): the last staff's middle line to the closing text's
    /// baseline, 12.000000 — score-markup-spacing's basic-distance.</summary>
    [Fact]
    public void TheTextAfterTheLastSystem_StandsTwelveUnderItsMiddleLine()
    {
        var plain = Laid(Book()).Pages[0];
        var page = Laid(Book(top: $"{F}md end\nA closing note.\n{F}")).Pages[0];
        Assert.Null(page.Header!.Text);
        Assert.Single(page.EndText!.Lines);
        // The music stands where it stood.
        Assert.Equal(plain.Systems[0].Y, page.Systems[0].Y, 9);
        double middleLine = page.Height - page.Systems[^1].Y + 2.0;
        Assert.Equal(12.0, page.EndTextY - middleLine, 6);
    }

    [Fact]
    public void AScoresOwnText_ReplacesTheFilesForThatPlace_AndAnEmptyFenceLeavesNone()
    {
        string file = $"{F}md\nThe file's.\n{F}\n{F}md end\nThe file's end.\n{F}";
        var own = Laid(Book(top: file, inScore: $"{F}md\nThis score's.\n{F}\n")).Pages[0];
        Assert.Equal("This score's.", TextOf(Assert.Single(own.Header!.Text!.Lines)));
        Assert.Equal("The file's end.", TextOf(Assert.Single(own.EndText!.Lines)));      // the other place keeps the file's

        var none = Laid(Book(top: file, inScore: $"{F}md\n{F}\n{F}md end\n{F}\n")).Pages[0];
        Assert.Null(none.Header!.Text);
        Assert.Null(none.EndText);
    }

    [Fact]
    public void TwoFencesForOnePlace_PrintOneAfterTheOther()
    {
        var page = Laid(Book(top: $"{F}md\nFirst.\n{F}\n{F}md\nSecond.\n{F}")).Pages[0];
        Assert.Equal(["First.", "Second."], page.Header!.Text!.Lines.Select(TextOf));
        Assert.Equal(4.5, page.Header.Text.Lines[1].Baseline, 9);
    }

    [Fact]
    public void TheSvgSetsEachPiece_InItsStyle_FromTheLeftMargin()
    {
        string source = Book(top: $"{F}md\nPlain **bold** *slanted*.\n{F}");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source));
        Assert.Matches("<text x=\"8\\.54\" y=\"[\\d.]+\" font-size=\"2\\.20\" data-pos=\"\\d+\">Plain </text>", svg);
        Assert.Matches("font-weight=\"bold\" data-pos=\"\\d+\">bold</text>", svg);
        Assert.Matches("font-style=\"italic\" data-pos=\"\\d+\">slanted</text>", svg);
        // The click goes to the fence.
        int fence = source.IndexOf(F, System.StringComparison.Ordinal);
        Assert.Contains($"data-pos=\"{fence}\">Plain </text>", svg);
    }

    // ---- the twin ----------------------------------------------------------------------

    [Fact]
    public void TheTwin_WritesThePagesLines_AsAMarkupList()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(
            Book(top: $"{F}md\nPlain **bold** and *slanted* \"quoted\".\n\nSecond.\n{F}\n{F}md end\nThe end.\n{F}")));
        int before = ly.IndexOf("\\markuplist \\column-lines {", System.StringComparison.Ordinal);
        int score = ly.IndexOf("\\score {", System.StringComparison.Ordinal);
        int after = ly.LastIndexOf("\\markuplist \\column-lines {", System.StringComparison.Ordinal);
        Assert.True(before >= 0 && before < score && score < after);
        Assert.Contains("  \\concat { \"Plain \" \\bold \"bold\" \" and \" \\italic \"slanted\" \" \\\"quoted\\\".\" }\n", ly);
        // The white over a paragraph is its first line's taller box: 2 + half a skip.
        Assert.Contains("  \\with-dimension #Y #'(-1 . 3.5) \"Second.\"\n", ly);
        Assert.Contains("  \"The end.\"\n}", ly);
        // A file with no fence writes none.
        Assert.DoesNotContain("\\markuplist", new LilyPondExporter().Export(SyntaxTree.Parse(Book())));
    }
}
