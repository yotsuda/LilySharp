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

using System;
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
/// Headings and lists in a fenced text (owner's request 2026-11-13: "draw bullet lists and
/// headings too"; docs/markdown-text-design.md section 8 step 2): two levels of heading, bold
/// and one or two of LilyPond's font-size steps over the text; bullet and numbered lists with
/// a hanging indent, nested, tight and loose — in the reading, the layout, the page's chain,
/// the SVG, the diagnostics and the LilyPond twin. The sizes, the white and the indents are
/// Lily#'s own; the twin was engraved in LilyPond 2.26.0 and compared (LilySharp-Lab
/// sessions/p1226/md/lists, showcase.lys).
/// </summary>
[Trait("Category", "Unit")]
public class MarkdownTextListTests
{
    private const string F = "```";

    /// <summary>The text's em, a list level's indent (1.5 em) and the marker's gap (0.5 em).</summary>
    private const double Em = 2.2, Indent = 3.3, Gap = 1.1;

    /// <summary>A level-one and a level-two heading's size over the text's: two steps and one of 2^(1/6).</summary>
    private static readonly double H1 = Math.Pow(2, 2 / 6.0), H2 = Math.Pow(2, 1 / 6.0);

    private static string Book(string top = "", string head = "title \"Morning Song\"\ncomposer \"A. Composer\"") => $$"""
        {{head}}
        {{top}}
        octave relative
        part m { clef treble }
        section A { m { c'4 d e f | g1 | c,4 d e f | g1 | } }
        form { A }
        score { staff m }
        """;

    private static ScoreLayout Laid(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static ProseBand Band(string markdown, double width = 100, ProseShape shape = default)
        => ProseBand.Build(markdown, 0, ScoreTextMetrics.Bundled, width, shape)!;

    private static string TextOf(ProseLine line) => string.Concat(line.Pieces.Select(p => p.Text));

    private static double Advance(string text)
        => ScoreTextMetrics.Bundled.Advance(text, Em, TextRole.Markdown, FontStyle.Regular);

    private static Diagnostic[] NotDrawn(string markdown)
        => SemanticValidation.Run(SyntaxTree.Parse(Book(top: $"{F}md\n{markdown}\n{F}")))
            .Where(d => d.Code == DiagnosticCodes.FencedTextNotDrawn).ToArray();

    // ---- reading -----------------------------------------------------------------------

    [Fact]
    public void Headings_AreReadInTwoLevels_AndADeeperOneIsTheSecond()
    {
        var prose = Prose.Read("# One\n\n## Two\n\n### Three\n\n###### Six\n\nSetext\n======\n\nText");
        Assert.Equal([1, 2, 2, 2, 1, 0], prose.Blocks.Select(b => b.Heading));
        Assert.Equal("One", prose.Blocks[0].Runs[0].Text);
        Assert.Empty(prose.NotDrawn);
    }

    [Fact]
    public void ABulletList_IsItsItems_EachWithItsMarker_TightUnlessABlankLineParts()
    {
        var tight = Prose.Read("Before.\n\n- one\n- two\n\nAfter.");
        Assert.Equal([null, Prose.Bullet, Prose.Bullet, null], tight.Blocks.Select(b => b.Marker));
        Assert.Equal([0, 1, 1, 0], tight.Blocks.Select(b => b.Depth));
        // The list stands a paragraph off the text over it; its second item follows the first.
        Assert.Equal([false, false, true, false], tight.Blocks.Select(b => b.Tight));
        Assert.Empty(tight.NotDrawn);

        var loose = Prose.Read("- one\n\n- two");
        Assert.Equal([false, false], loose.Blocks.Select(b => b.Tight));
    }

    [Fact]
    public void AnItemsSecondParagraph_IsOfTheItem_WithNoMarker()
    {
        var prose = Prose.Read("- one\n\n  more of one\n- two");
        Assert.Equal([Prose.Bullet, null, Prose.Bullet], prose.Blocks.Select(b => b.Marker));
        Assert.Equal([1, 1, 1], prose.Blocks.Select(b => b.Depth));
    }

    [Fact]
    public void ANestedList_IsOneDeeper_AndFollowsItsItemWithNoWhiteInATightList()
    {
        var prose = Prose.Read("- one\n  - inner\n    - innermost\n- two");
        Assert.Equal([1, 2, 3, 1], prose.Blocks.Select(b => b.Depth));
        Assert.Equal([false, true, true, true], prose.Blocks.Select(b => b.Tight));
    }

    /// <summary>CommonMark: a numbered list begins at the number its first item writes and
    /// counts on, whatever the later items write; the delimiter is the one written.</summary>
    [Theory]
    [InlineData("1. a\n2. b\n3. c", "1.,2.,3.")]
    [InlineData("3. a\n3. b\n3. c", "3.,4.,5.")]
    [InlineData("1. a\n1. b", "1.,2.")]
    [InlineData("7) a\n8) b", "7),8)")]
    [InlineData("0. a\n1. b", "0.,1.")]
    public void ANumberedList_CountsOnFromItsFirstNumber(string markdown, string markers)
        => Assert.Equal(markers, string.Join(',', Prose.Read(markdown).Blocks.Select(b => b.Marker)));

    [Fact]
    public void AListKnowsItsWidestMarker_SoThatItsItemsShareOneIndent()
    {
        var prose = Prose.Read(string.Join('\n', Enumerable.Range(1, 10).Select(n => $"{n}. item")));
        Assert.All(prose.Blocks, b => Assert.Equal("10.", Assert.Single(b.Lists)));
    }

    [Fact]
    public void ATaskItem_IsAnItemWhoseWordsBeginWithTheBrackets()
    {
        var prose = Prose.Read("- [ ] to do\n- [x] done");
        Assert.Equal(["[ ] to do", "[x] done"], prose.Blocks.Select(b => string.Concat(b.Runs.Select(r => r.Text))));
        Assert.Empty(prose.NotDrawn);
    }

    [Fact]
    public void AnItemThatWritesNoWords_KeepsItsMarker()
    {
        var prose = Prose.Read("-\n- two");
        Assert.Equal([Prose.Bullet, Prose.Bullet], prose.Blocks.Select(b => b.Marker));
        Assert.Empty(prose.Blocks[0].Runs);
        var band = Band("-\n- two");
        Assert.Equal(2, band.Lines.Length);
        Assert.NotNull(band.Lines[0].Marker);
        Assert.Empty(band.Lines[0].Pieces);
    }

    // ---- headings on the page ----------------------------------------------------------

    /// <summary>LILYPOND-REF: \fontsize's step is 2^(1/6); +2 on the text's 2.2 is 2.7719
    /// (measured), the piece's subtitle size — under the title's 3.49.</summary>
    [Fact]
    public void AHeadingIsBold_AndTwoStepsOrOneOverTheText()
    {
        var band = Band("# Head\n\n## Sub\n\nText");
        Assert.Equal([H1, H2, 1.0], band.Lines.Select(l => l.Scale));
        Assert.Equal(2.7718, Em * band.Lines[0].Scale, 4);       // LilyPond prints 2.7719 from its 11pt
        Assert.Equal(2.4694, Em * band.Lines[1].Scale, 4);
        Assert.True(Em * band.Lines[0].Scale < HeaderBand.TitleFontSize - 0.5);
        Assert.Equal(HeaderBand.SubtitleFontSize, Em * band.Lines[0].Scale, 2);
        Assert.All(band.Lines.Take(2), l => Assert.All(l.Pieces, p => Assert.True((p.Style & FontStyle.Bold) != 0)));
        Assert.Equal(FontStyle.Regular, band.Lines[2].Pieces[0].Style);
        // Italic in a heading is bold italic.
        Assert.Equal(FontStyle.Bold | FontStyle.Italic, Band("# A *b*").Lines[0].Pieces[1].Style);
    }

    /// <summary>A heading's box is the text's line box grown as the heading is (2 over the
    /// baseline and 1 under, times its scale); a line of white over it (3.0) and a quarter
    /// under it (0.75).</summary>
    [Fact]
    public void AHeadingHasALineOfWhiteOverIt_AndAQuarterUnderIt()
    {
        var band = Band("First.\n\n# Head\n\nText.\n\n## Sub\n\nMore.");
        double[] expected =
        [
            0,
            1.0 + 3.0 + 2.0 * H1,
            1.0 + 3.0 + 2.0 * H1 + 1.0 * H1 + 0.75 + 2.0,
            1.0 + 3.0 + 2.0 * H1 + 1.0 * H1 + 0.75 + 2.0 + 1.0 + 3.0 + 2.0 * H2,
            1.0 + 3.0 + 2.0 * H1 + 1.0 * H1 + 0.75 + 2.0 + 1.0 + 3.0 + 2.0 * H2 + 1.0 * H2 + 0.75 + 2.0,
        ];
        Assert.Equal(expected.Length, band.Lines.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], band.Lines[i].Baseline, 9);
    }

    [Fact]
    public void AHeadingFirstOrLast_GivesTheBandItsOwnBox()
    {
        var first = Band("# Head\n\nText.");
        Assert.Equal(2.0 * H1, first.Up, 9);
        Assert.Equal(first.Lines[^1].Baseline + 1.0, first.Down, 9);
        var last = Band("Text.\n\n## Sub");
        Assert.Equal(2.0, last.Up, 9);
        Assert.Equal(last.Lines[^1].Baseline + 1.0 * H2, last.Down, 9);
    }

    [Fact]
    public void ALongHeadingWraps_AtItsOwnSize()
    {
        string words = string.Join(' ', Enumerable.Repeat("measure", 12));
        var plain = Band(words, width: 40);
        var band = Band("# " + words, width: 40);
        Assert.True(band.Lines.Length > plain.Lines.Length);        // larger and bold: fewer words a line
        Assert.All(band.Lines, l => Assert.True(l.Width <= 40 + 1e-9));
        Assert.Equal(words, string.Join(' ', band.Lines.Select(TextOf)));
        // Its lines step by the skip grown as the heading is.
        Assert.Equal(3.0 * H1, band.Lines[1].Baseline - band.Lines[0].Baseline, 9);
    }

    /// <summary>The page's chain takes the heading's taller box: the first baseline stands
    /// that much lower under the composer line, and the music moves with it.</summary>
    [Fact]
    public void AHeadingFirstUnderTheTitle_StandsItsBoxUnderTheRows()
    {
        var page = Laid(Book(top: $"{F}md\n# Before you play\n\nSlowly.\n{F}")).Pages[0];
        Assert.Equal(page.HeaderTop + page.Header!.Depth + 0.5 + 2.0 * H1, page.HeaderTextY, 6);
    }

    // ---- lists on the page -------------------------------------------------------------

    [Fact]
    public void AnItemsWordsBeginAtTheIndent_AndItsMarkerHangsFlushRightBeforeThem()
    {
        var band = Band("- one\n- two");
        Assert.All(band.Lines, l => Assert.Equal(Indent, l.Left, 9));
        Assert.All(band.Lines, l => Assert.Equal(Indent, l.Indent, 9));
        Assert.All(band.Lines, l => Assert.Equal(Prose.Bullet, l.Marker!.Text));
        // The marker's right end is half an em short of the words.
        Assert.All(band.Lines, l => Assert.Equal(-Gap, l.Marker!.X + Advance(l.Marker.Text), 9));
        Assert.All(band.Lines, l => Assert.True(l.Left + l.Marker!.X >= 0));       // inside the margin
        // A tight list's items step by the skip.
        Assert.Equal([0.0, 3.0], band.Lines.Select(l => l.Baseline));
        Assert.Equal(2.0, band.Up, 9);
    }

    [Fact]
    public void AWrappedItem_HangsUnderItsWords_NotUnderItsMarker()
    {
        string words = string.Join(' ', Enumerable.Repeat("measure", 12));
        var band = Band("- " + words + "\n- two", width: 40);
        Assert.True(band.Lines.Length > 3);
        Assert.All(band.Lines, l => Assert.Equal(Indent, l.Left, 9));
        // The lines are broken to what the indent leaves.
        Assert.All(band.Lines, l => Assert.True(l.Width <= 40 - Indent + 1e-9));
        Assert.Contains(band.Lines, l => l.Width > 40 - Indent - 10);
        // One marker an item: on its first line.
        Assert.Equal(2, band.Lines.Count(l => l.Marker is not null));
        Assert.NotNull(band.Lines[0].Marker);
        Assert.Null(band.Lines[1].Marker);
        Assert.NotNull(band.Lines[^1].Marker);
        Assert.Equal(3.0, band.Lines[1].Baseline - band.Lines[0].Baseline, 9);
    }

    [Fact]
    public void ANestedList_IsIndentedAgain_AndItsBulletIsADash()
    {
        var band = Band("- one\n  - inner\n    - innermost\n- two");
        Assert.Equal([3.3, 6.6, 9.9, 3.3], band.Lines.Select(l => Math.Round(l.Left, 6)));
        Assert.Equal(["\u2022", "\u2013", "\u2022", "\u2022"], band.Lines.Select(l => l.Marker!.Text));
        Assert.Equal([0.0, 3.0, 6.0, 9.0], band.Lines.Select(l => l.Baseline));
    }

    [Fact]
    public void ANumberedListsStopsAlign_AndAWideNumberWidensTheWholeList()
    {
        var three = Band("1. a\n2. b\n3. c");
        Assert.Equal(["1.", "2.", "3."], three.Lines.Select(l => l.Marker!.Text));
        Assert.All(three.Lines, l => Assert.Equal(Indent, l.Left, 9));

        var ten = Band(string.Join('\n', Enumerable.Range(1, 10).Select(n => $"{n}. item")));
        double indent = Advance("10.") + Gap;
        Assert.True(indent > Indent);
        Assert.All(ten.Lines, l => Assert.Equal(indent, l.Left, 9));
        Assert.All(ten.Lines, l => Assert.Equal(-Gap, l.Marker!.X + Advance(l.Marker.Text), 9));
        Assert.Equal(0, ten.Lines[9].Left + ten.Lines[9].Marker!.X, 9);       // "10." begins at the margin
    }

    /// <summary>A list stands a paragraph's white (half a skip) off the text over it and
    /// under it; a loose list's items and an item's paragraphs stand as far apart.</summary>
    [Fact]
    public void TheWhiteAroundAList_AndInALooseOne_IsAParagraphs()
    {
        Assert.Equal([0.0, 4.5, 7.5, 12.0], Band("Before.\n\n- one\n- two\n\nAfter.").Lines.Select(l => l.Baseline));
        Assert.Equal([0.0, 4.5], Band("- one\n\n- two").Lines.Select(l => l.Baseline));
        var two = Band("- one\n\n  more of one\n- two");
        Assert.Equal([0.0, 4.5, 9.0], two.Lines.Select(l => l.Baseline));
        Assert.Null(two.Lines[1].Marker);
        Assert.Equal(Indent, two.Lines[1].Left, 9);
        // A loose list with a tight one inside: the inner one's first item stands off its paragraph.
        Assert.Equal([0.0, 4.5, 7.5, 12.0], Band("- one\n\n  - a\n  - b\n\n- two").Lines.Select(l => l.Baseline));
    }

    [Fact]
    public void JapaneseInAnItem_BreaksBetweenCharacters_UnderTheItemsWords()
    {
        var band = Band("- あいうえおかきくけこ。", width: Indent + 9);
        Assert.True(band.Lines.Length >= 3);
        Assert.Equal("あいうえおかきくけこ。", string.Concat(band.Lines.Select(TextOf)));
        Assert.All(band.Lines, l => Assert.Equal(Indent, l.Left, 9));
        Assert.All(band.Lines, l => Assert.NotEqual('。', TextOf(l)[0]));
    }

    // ---- with the shape words ----------------------------------------------------------

    [Fact]
    public void IndentAndBox_NarrowWhatAListWrapsTo_AndMoveItRight()
    {
        string words = string.Join(' ', Enumerable.Repeat("measure", 30));
        var indented = Band("- " + words, 100, new ProseShape(20, 10, false, false));
        Assert.All(indented.Lines, l => Assert.Equal(20 + Indent, l.Left, 9));
        Assert.All(indented.Lines, l => Assert.True(l.Width <= 100 - 30 - Indent + 1e-9));

        var boxed = Band("## Head\n\n- " + words, 100, new ProseShape(0, 0, false, true));
        Assert.Equal(1.1, boxed.Lines[0].Left, 9);
        Assert.All(boxed.Lines.Skip(1), l => Assert.Equal(1.1 + Indent, l.Left, 9));
        Assert.All(boxed.Lines.Skip(1), l => Assert.True(l.Width <= 100 - 2.2 - Indent + 1e-9));
        // The frame is around the heading's own box and the last item's.
        Assert.Equal(new ProseFrame(0, 100, ProseBand.BoxThickness), boxed.Frame);
        Assert.Equal(2.0 * H2 + 1.1 + ProseBand.BoxMargin, boxed.Up, 9);
        Assert.Equal(boxed.Lines[^1].Baseline + 1.0 + 1.1 + ProseBand.BoxMargin, boxed.Down, 9);
    }

    /// <summary>LILYSHARP-OWN (docs/markdown-text-design.md section 5.1, for the owner to
    /// confirm): under <c>center</c> a heading is centred as a paragraph is; a list keeps its
    /// hanging shape from the text's left edge.</summary>
    [Fact]
    public void Centred_AHeadingIsCentred_AndAListKeepsItsHangingShape()
    {
        var band = Band("# Head\n\nText.\n\n- one\n- two\n  - inner", 100, new ProseShape(0, 0, true, false));
        Assert.Equal((100 - band.Lines[0].Width) / 2, band.Lines[0].Left, 9);
        Assert.Equal((100 - band.Lines[1].Width) / 2, band.Lines[1].Left, 9);
        Assert.Equal([3.3, 3.3, 6.6], band.Lines.Skip(2).Select(l => Math.Round(l.Left, 6)));
        Assert.False(band.Lines[0].InList);
        Assert.All(band.Lines.Skip(2), l => Assert.True(l.InList));
    }

    // ---- what a text of paragraphs was, it is ------------------------------------------

    [Fact]
    public void ATextOfParagraphs_HasNoScaleNoMarkerNoIndent()
    {
        var band = Band("One **bold** line.\n\nAnother.  \nBy hand.");
        Assert.All(band.Lines, l => Assert.Equal((1.0, null, 0.0, false), (l.Scale, l.Marker, l.Indent, l.InList)));
        Assert.Equal([0.0, 4.5, 7.5], band.Lines.Select(l => l.Baseline));
        Assert.Equal(2.0, band.Up);
        Assert.Equal(8.5, band.Down);
    }

    // ---- the diagnostics ---------------------------------------------------------------

    [Fact]
    public void HeadingsAndLists_AreNoLongerSaidNotToBeDrawn()
    {
        Assert.Empty(NotDrawn("# Head\n\n## Sub\n\n### Deeper\n\n- one\n  - inner\n- two\n\n1. first\n2. second\n\n- [ ] a task"));
    }

    [Theory]
    [InlineData("> quoted", "A block quote")]
    [InlineData("> - a list in a quote\n> - is the quote's", "A block quote")]
    [InlineData("---", "A rule")]
    [InlineData("    indented code", "A code block")]
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |", "A table")]
    [InlineData("<div>x</div>", "HTML")]
    [InlineData("- see ![a hand](hand.png) here", "An image")]
    [InlineData("- press `c4` now", "Code")]
    [InlineData("- item\n\n      code in an item", "A code block")]
    public void WhatIsStillNotDrawn_IsStillSaid_OnceAndWhereItIs(string markdown, string what)
    {
        var d = Assert.Single(NotDrawn("A paragraph.\n\n" + markdown));
        Assert.StartsWith(what + " is read and not drawn: a fenced text prints its paragraphs, headings and lists", d.Message);
    }

    // ---- the SVG -----------------------------------------------------------------------

    [Fact]
    public void TheSvgDrawsAHeadingLargerAndBold_AndAMarkerBeforeItsItem()
    {
        string source = Book(top: $"{F}md\n# Before you play\n\n- Tune first.\n{F}");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source));
        Assert.Matches("<text x=\"8\\.54\" y=\"[\\d.]+\" font-size=\"2\\.77\" font-weight=\"bold\" data-pos=\"\\d+\">Before you play</text>", svg);
        // The item's words stand 3.3 right of the margin, the bullet before them.
        Assert.Matches("<text x=\"11\\.84\" y=\"[\\d.]+\" font-size=\"2\\.20\" data-pos=\"\\d+\">Tune first\\.</text>", svg);
        var bullet = System.Text.RegularExpressions.Regex.Match(svg, "<text x=\"([\\d.]+)\" y=\"[\\d.]+\" font-size=\"2\\.20\" data-pos=\"\\d+\">\u2022</text>");
        Assert.True(bullet.Success);
        double x = double.Parse(bullet.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        // (The SVG writes two places: within a hundredth.)
        Assert.Equal(LayoutOptions.Default.MarginLeft + Indent - Gap - Advance("\u2022"), x, 1);
    }

    // ---- the twin ----------------------------------------------------------------------

    /// <summary>MEASURED 2026-11-13, LilyPond 2.26.0 (LilySharp-Lab sessions/p1226/md/lists,
    /// lp-showcase.ly): these forms engrave the heading at 2.7719 / 2.4697, the items' words
    /// 3.3000 and 6.6000 right of the text's edge, and the baselines the page's distances
    /// apart (docs/markdown-text-design.md section 5.2).</summary>
    [Fact]
    public void TheTwin_WritesAHeadingAsBoldFontsize_AndAnItemAsAMarkerBoxAndItsWords()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book(top:
            $"{F}md\n# Head\n\nText.\n\n- one\n  - inner\n- two\n\n## Sub\n\n1. first\n{F}")));
        Assert.Contains("  \\with-dimension #Y #'(-1.2599 . 2.5198) \\fontsize #2 \\concat { \\bold \"Head\" }\n", ly);
        Assert.Contains("  \\with-dimension #Y #'(-1 . 2.75) \"Text.\"\n", ly);
        Assert.Contains("  \\with-dimension #Y #'(-1 . 3.5) \\concat { \\with-dimension #X #'(0 . 3.3) \\translate #'(2.2 . 0) \\right-align \"\u2022\" \"one\" }\n", ly);
        Assert.Contains("  \\concat { \\with-dimension #X #'(0 . 6.6) \\translate #'(5.5 . 0) \\right-align \"\u2013\" \"inner\" }\n", ly);
        Assert.Contains("  \\concat { \\with-dimension #X #'(0 . 3.3) \\translate #'(2.2 . 0) \\right-align \"\u2022\" \"two\" }\n", ly);
        Assert.Contains("  \\with-dimension #Y #'(-1.1225 . 5.2449) \\fontsize #1 \\concat { \\bold \"Sub\" }\n", ly);
        Assert.Contains("  \\with-dimension #Y #'(-1 . 2.75) \\concat { \\with-dimension #X #'(0 . 3.3) \\translate #'(2.2 . 0) \\right-align \"1.\" \"first\" }\n", ly);
    }

    [Fact]
    public void TheTwin_WritesAWrappedItemsLaterLines_AfterTheIndent()
    {
        string words = string.Join(' ', Enumerable.Repeat("measure", 40));
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book(top: $"{F}md\n- {words}\n{F}")));
        Assert.Contains("  \\concat { \\hspace #3.3 \"measure", ly);
    }

    [Fact]
    public void TheTwin_InABox_WritesTheWhiteAsVspace_AndCentresNoList()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book(top:
            $"{F}md box center\n## Head\n\n- one\n\nText.\n{F}")));
        // The column is given the heading's own box over the first baseline.
        Assert.Contains("\\with-dimension #Y #'(-9.3725 . 2.2449) \\column {\n", ly);
        Assert.Contains("      \\override #'(line-width . 100.2299) \\fill-line { \\fontsize #1 \\concat { \\bold \"Head\" } }\n", ly);
        // 1.1225 + 0.75 + 2.0 between the baselines: 0.8725 over the column's skip of 3.
        Assert.Contains("      \\vspace #0.2908\n      \\concat { \\with-dimension #X #'(0 . 3.3) \\translate #'(2.2 . 0) \\right-align \"\u2022\" \"one\" }\n", ly);
        Assert.Contains("      \\vspace #0.5\n      \\override #'(line-width . 100.2299) \\fill-line { \"Text.\" }\n", ly);
    }
}
