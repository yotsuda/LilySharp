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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
/// The shape a fenced text's opening line gives it (owner-approved 2026-11-13;
/// docs/markdown-text-design.md sections 2.3 and 5.1): <c>xMargin LEFT [RIGHT]</c>,
/// <c>center</c>, <c>box</c> after <c>md</c> and the optional <c>end</c>, in any order — in
/// the layout, the page's chain, the SVG and the LilyPond twin. The frame is LilyPond's
/// <c>\box</c> with a padding of Lily#'s own; measured against LilyPond 2.26.0 in
/// LilySharp-Lab sessions/p1226/md/fencelayout (showcase.lys, box-pad.ly).
/// </summary>
[Trait("Category", "Unit")]
public class MarkdownTextShapeTests
{
    private const string F = "```";

    /// <summary>The default paper's line width and left margin, in staff spaces (a4, 15 mm).</summary>
    private static readonly double LineWidth = LayoutOptions.Default.ContentWidth;

    /// <summary>Millimetres as staff spaces on the default 20 pt staff.</summary>
    private static double Mm(double mm) => Math.Round(mm * 72.27 / 127.0, 6);

    private static readonly string Words = string.Join(' ', Enumerable.Repeat("measure", 60));

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
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static ProseBand Band(string markdown, ProseShape shape, double width = 100)
        => ProseBand.Build(markdown, 0, ScoreTextMetrics.Bundled, width, shape)!;

    private static string TextOf(ProseLine line) => string.Concat(line.Pieces.Select(p => p.Text));

    private static TextShape ShapeOf(string openingLine) => FencedText.Read(openingLine + "\ntext\n```").Shape;

    private static Diagnostic[] WordErrors(string book)
        => [.. SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.FencedTextWord)];

    // ---- the words ---------------------------------------------------------------------

    [Fact]
    public void TheWordsAreReadInAnyOrder_AfterMdAndEnd()
    {
        Assert.True(ShapeOf("```md").IsPlain);
        Assert.True(ShapeOf("```md end").IsPlain);
        Assert.Equal(new TextShape(default, default, false, true), ShapeOf("```md box"));
        Assert.Equal(new TextShape(default, default, true, false), ShapeOf("```md end center"));
        var twenty = new TextLength(20, "mm");
        Assert.Equal(new TextShape(twenty, twenty, false, false), ShapeOf("```md xMargin 20mm"));
        Assert.Equal(new TextShape(twenty, new TextLength(1.5, "cm"), true, true), ShapeOf("```md xMargin 20mm 1.5cm box center"));
        Assert.Equal(ShapeOf("```md xMargin 20mm 1.5cm box center end"), ShapeOf("```markdown center end box xMargin 20mm 1.5cm"));
        // yMargin: one length is above and below, two are above, then below; with none
        // written the shape says nothing and the page keeps its default.
        Assert.Equal((null, null), (ShapeOf("```md box").MarginTop, ShapeOf("```md box").MarginBottom));
        var two = new TextLength(2, null);
        Assert.Equal(new TextShape(default, default, false, false, two, two), ShapeOf("```md yMargin 2"));
        Assert.Equal(new TextShape(twenty, twenty, false, true, new TextLength(3, null), new TextLength(5, "mm")),
            ShapeOf("```md end yMargin 3 5mm box xMargin 20mm"));
        Assert.False(ShapeOf("```md yMargin 0").IsPlain);
    }

    /// <summary>The units are <c>paper { }</c>'s, through its conversion; a bare number is staff spaces.</summary>
    [Theory]
    [InlineData("20mm", 20 * 72.27 / 127.0)]
    [InlineData("2cm", 20 * 72.27 / 127.0)]
    [InlineData("1in", 25.4 * 72.27 / 127.0)]
    [InlineData("7.5", 7.5)]
    [InlineData("0", 0)]
    public void ALengthIsWrittenAsPaperWritesOne(string length, double staffSpaces)
    {
        var shape = ProseShape.Of(ShapeOf($"```md xMargin {length}"), LayoutOptions.DefaultStaffSpaceMm);
        Assert.Equal(staffSpaces, shape.MarginLeft, 5);
        Assert.Equal(staffSpaces, shape.MarginRight, 5);
        // On a smaller staff the same millimetres are more staff spaces; a bare number is not.
        var small = ProseShape.Of(ShapeOf($"```md xMargin {length}"), LayoutOptions.DefaultStaffSpaceMm / 2);
        Assert.Equal(length.Any(char.IsLetter) ? 2 * staffSpaces : staffSpaces, small.MarginLeft, 5);
    }

    [Theory]
    [InlineData("```md frame", "'frame' means nothing here")]
    [InlineData("```md frame", "end (under the last system), box (a ruled frame), center (each line centred), xMargin LEFT [RIGHT]")]
    [InlineData("```md box box", "'box' is written twice")]
    [InlineData("```md center end center", "'center' is written twice")]
    [InlineData("```md xMargin", "'xMargin' needs a length")]
    [InlineData("```md xMargin box", "'xMargin' needs a length")]
    [InlineData("```md xMargin 10mm xMargin 20mm", "'xMargin' is written twice")]
    [InlineData("```md xMargin 10mm 20mm 30mm", "'30mm' is a third length")]
    [InlineData("```md xMargin -5mm", "'-5mm' is negative")]
    [InlineData("```md xMargin 20pt", "'pt' is not a unit of this language")]
    [InlineData("```md xMargin 20MM", "Units are case-sensitive: write 'mm'")]
    [InlineData("```md xMargin 1.2.3mm", "'1.2.3mm' is not a length")]
    [InlineData("```md 20mm", "'20mm' means nothing here")]                         // a length with no margin
    [InlineData("```md xMargin 95mm", "These margins leave the text 0 mm of a line width of 180 mm")]
    [InlineData("```md xMargin 150mm 20mm", "A text keeps at least 17.6 mm")]
    [InlineData("```md box xMargin 82mm", "(the frame takes its padding too)")]
    [InlineData("```md frame", "yMargin TOP [BOTTOM] (white above and below: ```md yMargin 2)")]
    [InlineData("```md yMargin", "'yMargin' needs a length: ```md yMargin 2 for above and below")]
    [InlineData("```md yMargin 1 yMargin 2", "'yMargin' is written twice")]
    [InlineData("```md yMargin 1 2 3", "'3' is a third length. 'yMargin' takes one (above and below) or two (above, then below)")]
    [InlineData("```md yMargin -1", "'-1' is negative")]
    [InlineData("```md yMargin 2pt", "'pt' is not a unit of this language")]
    [InlineData("```md yMargin 100", "A margin above or below a text is at most 87.9 mm (50 staff spaces)")]
    [InlineData("```md end yMargin 1 9cm", "A margin above or below a text is at most")]
    // The word xMargin had for a few hours of 2026-11-13, never released: unknown, with its own hint.
    [InlineData("```md indent 20mm 10mm box", "'indent' is not a word of this line: the white at a text's sides is written 'xMargin'")]
    public void AWordThatDoesNotRead_IsAnError_WhereItIsWritten(string openingLine, string says)
    {
        string book = Book(top: $"{openingLine}\ntext\n{F}");
        var d = Assert.Single(WordErrors(book));
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(says, d.Message);
        // On the opening line, after the word md.
        int line = book.IndexOf(openingLine, StringComparison.Ordinal);
        Assert.InRange(d.Span.Start, line + 6, line + openingLine.Length - 1);
        Assert.InRange(d.Span.End, line + 6, line + openingLine.Length);
    }

    [Fact]
    public void TheWidthLeft_IsJudgedOnThePaperTheFenceStandsUnder()
    {
        // 82 mm a side leaves 16 mm of the default 180: refused. On a wider line it is taken…
        Assert.Single(WordErrors(Book(top: $"{F}md xMargin 82mm\ntext\n{F}")));
        Assert.Empty(WordErrors(Book(top: $"paper {{ paperWidth 297mm }}\n{F}md xMargin 82mm\ntext\n{F}")));
        // …and a score's fence is judged on that score's own paper.
        string narrow = "paper narrow { paperWidth 148mm }\n";
        Assert.Empty(WordErrors(Book(top: narrow, inScore: $"{F}md xMargin 52mm\ntext\n{F}\n").Replace("paper narrow { paperWidth 148mm }", "")));
        Assert.Single(WordErrors(Book(top: narrow, inScore: $"paper narrow\n{F}md xMargin 52mm\ntext\n{F}\n")));
    }

    [Fact]
    public void TwoFencesForOnePlace_AreTwoBlocks_EachSetAsItsOwnLineSays()
    {
        // (Until grammar evaluation 4, 2026-11-13, the place was ONE text set as the first
        // fence said, and a shape on the second an error: GrammarEvaluation4Tests has the
        // stack's geometry.)
        string both = $"{F}md box\nFirst.\n{F}\n{F}md\nSecond.\n{F}";
        Assert.Empty(WordErrors(Book(top: both)));
        var blocks = Laid(Book(top: both)).Pages[0].Header!.Text!.Blocks.Select(b => b.Block).ToArray();
        Assert.Equal(2, blocks.Length);
        Assert.NotNull(blocks[0].Frame);
        Assert.Null(blocks[1].Frame);
        Assert.Equal(["First.", "Second."], blocks.Select(b => TextOf(Assert.Single(b.Lines))));

        // A shape on the second fence is that block's.
        Assert.Empty(WordErrors(Book(top: $"{F}md\nFirst.\n{F}\n{F}md center box\nSecond.\n{F}")));
        // The other place is another text, with a shape of its own.
        Assert.Empty(WordErrors(Book(top: $"{F}md box\nFirst.\n{F}\n{F}md end center\nLast.\n{F}")));
        // A score's fence begins that score's text.
        Assert.Empty(WordErrors(Book(top: $"{F}md box\nFirst.\n{F}", inScore: $"{F}md center\nThis score's.\n{F}\n")));
    }

    [Fact]
    public void ASectionsText_TakesNoShape()
    {
        string source = Book().Replace("section A { m", $"section A {{\n{F}md box\ntext\n{F}\n m");
        Assert.Contains("'box' is not a printing", Assert.Single(WordErrors(source)).Message);
    }

    // ---- setting -----------------------------------------------------------------------

    [Fact]
    public void WithNoWord_TheBandIsWhatItWas()
    {
        var plain = ProseBand.Build(Words + "\n\nSecond.", 7, ScoreTextMetrics.Bundled, 60)!;
        var same = ProseBand.Build(Words + "\n\nSecond.", 7, ScoreTextMetrics.Bundled, 60, default(ProseShape))!;
        Assert.Equal(plain.Lines.Select(l => (TextOf(l), l.Baseline, l.Width, l.Left)), same.Lines.Select(l => (TextOf(l), l.Baseline, l.Width, l.Left)));
        Assert.All(plain.Lines, l => Assert.Equal(0.0, l.Left));
        Assert.Equal((0.0, 60.0, 2.0 + ProseBand.DefaultMargin), (plain.Left, plain.TextWidth, plain.Up));
        Assert.Null(plain.Frame);
        Assert.Equal(plain.Lines.Max(l => l.Width), plain.Width, 12);
        Assert.Equal(plain.Lines[^1].Baseline + 1.0 + ProseBand.DefaultMargin, plain.Down, 12);
    }

    [Fact]
    public void XMargin_SetsTheTextNarrower_AndFurtherRight()
    {
        var plain = Band(Words, default);
        var band = Band(Words, new ProseShape(12, 20, false, false));
        Assert.Equal(12.0, band.Left, 12);
        Assert.Equal(100.0 - 12 - 20, band.TextWidth, 12);
        Assert.All(band.Lines, l => Assert.Equal(12.0, l.Left, 12));
        Assert.All(band.Lines, l => Assert.True(l.Width <= band.TextWidth + 1e-9));
        // Broken again to the narrower width: more lines, the same words.
        Assert.True(band.Lines.Length > plain.Lines.Length);
        Assert.Equal(Words, string.Join(' ', band.Lines.Select(TextOf)));
        // No frame: the band is its lines and the white every text keeps above and below.
        Assert.Null(band.Frame);
        Assert.Equal(2.0 + ProseBand.DefaultMargin, band.Up, 12);
        Assert.Equal(band.Lines[^1].Baseline + 1.0 + ProseBand.DefaultMargin, band.Down, 12);
        // One length is both sides.
        Assert.Equal(100.0 - 24, Band(Words, ProseShape.Of(ShapeOf("```md xMargin 12"), LayoutOptions.DefaultStaffSpaceMm)).TextWidth, 12);
    }

    [Fact]
    public void Center_SetsEachLineInTheMiddleOfTheTextsWidth()
    {
        var band = Band("A short line.\n\n" + Words + "\n\nLast.", new ProseShape(10, 30, true, false));
        double middle = 10 + (100.0 - 10 - 30) / 2;
        Assert.True(band.Lines.Length > 4);
        Assert.All(band.Lines, l => Assert.Equal(middle, l.Left + l.Width / 2, 9));
        // The lines are the ones a ragged text of that width has.
        Assert.Equal(Band("A short line.\n\n" + Words + "\n\nLast.", new ProseShape(10, 30, false, false)).Lines.Select(TextOf), band.Lines.Select(TextOf));
        // A word wider than the text keeps the left edge and runs over, as it always has.
        var over = Band("Donaudampfschifffahrtsgesellschaftskapitän", new ProseShape(40, 40, true, false));
        Assert.Equal(40.0, Assert.Single(over.Lines).Left, 12);
    }

    /// <summary>LILYPOND-REF: scm/stencil.scm box-stencil — the rule outside the padded
    /// extent. MEASURED (showcase.ly): the frame of a two-line text reaches 3.1000 over the
    /// first baseline and 5.1000 under it, 1.1000 left of the text.</summary>
    [Fact]
    public void Box_RulesAFrameOnTheSideMarginsEdges_AndInsetsTheText()
    {
        var plain = Band(Words, default);
        var band = Band(Words, new ProseShape(0, 0, false, true));
        var frame = band.Frame!;
        Assert.Equal((0.0, 100.0, 0.1), (frame.Left, frame.Right, frame.Thickness));
        // The text stands the padding and the rule inside, both sides, and is broken to that.
        Assert.Equal(1.1, ProseBand.BoxPadding + ProseBand.BoxThickness, 12);
        Assert.Equal(1.1, band.Left, 12);
        Assert.Equal(100.0 - 2.2, band.TextWidth, 12);
        Assert.All(band.Lines, l => Assert.True(l.Left + l.Width <= frame.Right - 1.1 + 1e-9));
        Assert.Equal(Words, string.Join(' ', band.Lines.Select(TextOf)));
        // The band is the frame and the white the page keeps over and under it (DefaultMargin, the
        // owner 2026-11-13: "the white above and below the box looks too tight"): the lines'
        // boxes, the padding, the rule, the margin.
        Assert.Equal(2.0 + 1.1 + ProseBand.DefaultMargin, band.Up, 12);
        Assert.Equal(band.Lines[^1].Baseline + 1.0 + 1.1 + ProseBand.DefaultMargin, band.Down, 12);
        Assert.Equal(100.0, band.Width, 12);
        Assert.True(band.Lines.Length >= plain.Lines.Length);

        // With side margins the frame moves in, and the text with it.
        var moved = Band(Words, new ProseShape(12, 20, true, true));
        Assert.Equal((12.0, 80.0), (moved.Frame!.Left, moved.Frame.Right));
        Assert.Equal(13.1, moved.Left, 12);
        Assert.Equal(80.0 - 12 - 2.2, moved.TextWidth, 12);
        Assert.All(moved.Lines, l => Assert.Equal(46.0, l.Left + l.Width / 2, 9));
    }

    [Fact]
    public void SideMarginsGiveWay_BeforeTheTextIsNarrowerThanItsLeast()
    {
        // (Refused where the fence is written; another score's narrower paper lands here.)
        var band = Band(Words, new ProseShape(60, 30, false, false));
        Assert.Equal(ProseBand.MinimumWidth, band.TextWidth, 9);
        Assert.Equal(60.0, band.Left, 9);        // 60 : 30 of the 90 left over
        var boxed = Band(Words, new ProseShape(500, 500, false, true), width: 8);
        Assert.Equal(8 - 2.2, boxed.TextWidth, 9);
        Assert.Equal((0.0, 8.0), (boxed.Frame!.Left, boxed.Frame.Right));
    }

    // ---- the page ----------------------------------------------------------------------

    [Fact]
    public void OnThePage_TheXMarginIsInsideTheLineWidth_InThePapersMillimetres()
    {
        var page = Laid(Book(top: $"{F}md xMargin 20mm 30mm\n{Words}\n{F}")).Pages[0];
        var band = page.Header!.Text!;
        Assert.Equal(Mm(20), band.Left, 5);
        Assert.Equal(LineWidth - Mm(20) - Mm(30), band.TextWidth, 5);
        Assert.True(band.Lines.Length > 1);
    }

    /// <summary>MEASURED, LilyPond 2.26.0 (showcase.ly): the composer's baseline to a boxed
    /// text's first baseline 4.0705 — the unboxed 2.9705 and the frame's 1.1 — where the page
    /// has 4.05.</summary>
    [Fact]
    public void TheFrameIsPartOfTheBand_SoTheTitleAndTheSystemStandClearOfIt()
    {
        var plain = Laid(Book(top: $"{F}md\nOne line.\n{F}")).Pages[0];
        var page = Laid(Book(top: $"{F}md box\nOne line.\n{F}")).Pages[0];
        var band = page.Header!.Text!;
        // The frame's top stands the margin and the padding 0.5 under the rows, where the plain
        // line's box stood (both keep the default margin: one white, not two).
        Assert.Equal(page.HeaderTop + page.Header.Depth + 0.5 + ProseBand.DefaultMargin, page.HeaderTextY - band.Up + band.MarginTop, 6);
        Assert.Equal(page.HeaderTop + page.Header.Depth + 0.5, page.HeaderTextY - band.Up, 6);
        Assert.Equal(plain.HeaderTextY + 1.1, page.HeaderTextY, 6);
        // The music moved down by the frame, top and bottom: it is as far under the frame's
        // bottom as it was under the plain line's box.
        double systemTop = page.Height - page.Systems[0].Y, plainSystemTop = plain.Height - plain.Systems[0].Y;
        Assert.Equal(plainSystemTop + 2.2, systemTop, 6);
        Assert.True(systemTop > page.HeaderTextY + band.Down);
    }

    /// <summary>MEASURED (showcase.ly): the last staff's middle line to a boxed closing text's
    /// first baseline, 12.0000 — the frame's top is inside score-markup-spacing's 12.</summary>
    [Fact]
    public void ABoxedClosingText_StandsUnderTheLastSystem_AndOverThePagesFoot()
    {
        var plain = Laid(Book(top: $"{F}md end\nA closing note.\n{F}")).Pages[0];
        var page = Laid(Book(top: $"{F}md end xMargin 30mm box\nA closing note.\n{F}")).Pages[0];
        var band = page.EndText!;
        Assert.Equal(plain.Systems[0].Y, page.Systems[0].Y, 9);
        double middleLine = page.Height - page.Systems[^1].Y + 2.0;
        Assert.Equal(12.0, page.EndTextY - middleLine, 6);
        // The frame: under the staff's bottom line, over the bottom margin.
        Assert.True(page.EndTextY - band.Up > middleLine + 2.0);
        Assert.True(page.EndTextY + band.Down <= page.Height - LayoutOptions.Default.MarginBottom + 1e-9);
        Assert.Equal(Mm(30), band.Frame!.Left, 4);
        Assert.Equal(LineWidth - Mm(30), band.Frame.Right, 4);
    }

    [Fact]
    public void EachPlaceHasItsOwnWords_AndAScoresFenceItsOwn()
    {
        string file = $"{F}md box\nThe file's.\n{F}\n{F}md end center\nThe file's end.\n{F}";
        var page = Laid(Book(top: file)).Pages[0];
        Assert.NotNull(page.Header!.Text!.Frame);
        Assert.Null(page.EndText!.Frame);
        Assert.Equal(LineWidth / 2, page.EndText.Lines[0].Left + page.EndText.Lines[0].Width / 2, 6);
        Assert.Equal(1.1, page.Header.Text.Lines[0].Left, 9);

        // The score's own text for a place brings its own shape — here none — and the other
        // place keeps the file's.
        var own = Laid(Book(top: file, inScore: $"{F}md xMargin 10\nThis score's.\n{F}\n")).Pages[0];
        Assert.Null(own.Header!.Text!.Frame);
        Assert.Equal(10.0, own.Header.Text.Lines[0].Left, 9);
        Assert.Equal(LineWidth / 2, own.EndText!.Lines[0].Left + own.EndText.Lines[0].Width / 2, 6);
    }

    [Fact]
    public void TheSvgRulesTheFrame_AndSetsTheTextInsideIt()
    {
        string source = Book(top: $"{F}md box xMargin 20mm\nA note.\n{F}");
        var page = Laid(source).Pages[0];
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source));
        string N(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        double margin = LayoutOptions.Default.MarginLeft;
        double left = margin + Mm(20), right = margin + LineWidth - Mm(20);
        double top = page.HeaderTextY - 3.1, bottom = page.HeaderTextY + 2.1;
        int fence = source.IndexOf(F, StringComparison.Ordinal);
        // Four filled rules: the two long ones the frame's width, the two upright ones between.
        Assert.Contains($"<rect x=\"{N(left)}\" y=\"{N(top)}\" width=\"{N(right - left)}\" height=\"0.10\" data-pos=\"{fence}\"/>", svg);
        Assert.Contains($"<rect x=\"{N(left)}\" y=\"{N(bottom - 0.1)}\" width=\"{N(right - left)}\" height=\"0.10\" data-pos=\"{fence}\"/>", svg);
        Assert.Contains($"<rect x=\"{N(left)}\" y=\"{N(top + 0.1)}\" width=\"0.10\" height=\"{N(bottom - top - 0.2)}\" data-pos=\"{fence}\"/>", svg);
        Assert.Contains($"<rect x=\"{N(right - 0.1)}\" y=\"{N(top + 0.1)}\" width=\"0.10\" height=\"{N(bottom - top - 0.2)}\" data-pos=\"{fence}\"/>", svg);
        Assert.Matches($"<text x=\"{Regex.Escape(N(left + 1.1))}\" y=\"{Regex.Escape(N(page.HeaderTextY))}\" font-size=\"2\\.20\" data-pos=\"{fence}\">A note\\.</text>", svg);
        // A text with no box rules nothing.
        string plain = SvgGenerator.Generate(SyntaxTree.Parse(Book(top: $"{F}md\nA note.\n{F}")));
        Assert.Equal(Regex.Matches(plain, "<rect ").Count + 4, Regex.Matches(svg, "<rect ").Count);
    }

    // ---- the white above and below: yMargin -------------------------------------------

    /// <summary>The baselines and the first staff's top line of a book's page, as its SVG
    /// draws them (staff spaces, y down): the composer's line, the text's lines, the staff.</summary>
    private static (double Composer, double[] Text, double StaffTop, double StaffMiddle) Drawn(string book, params string[] lines)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        double Y(string words) => double.Parse(
            System.Text.RegularExpressions.Regex.Match(svg, "<text x=\"[\\d.]+\" y=\"([\\d.]+)\"[^>]*>" + System.Text.RegularExpressions.Regex.Escape(words) + "</text>").Groups[1].Value,
            CultureInfo.InvariantCulture);
        double[] staff = [.. System.Text.RegularExpressions.Regex.Matches(svg, "<line x1=\"[\\d.]+\" y1=\"([\\d.]+)\" x2=\"[\\d.]+\" y2=\"[\\d.]+\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order()];
        return (Y("A. Composer"), [.. lines.Select(Y)], staff[0], staff[2]);
    }

    /// <summary>
    /// <c>yMargin 0</c> is LilyPond's own spacing of a markup list, to the numbers measured
    /// before a text had a margin — the reference the default and every other margin are
    /// counted from.
    /// MEASURED 2026-11-12 and 2026-11-13, LilyPond 2.26.0 (LilySharp-Lab sessions/p1226/md
    /// t1tw2.ly, and md/margins p-zero.lys with its <c>lysc ly</c> twin): the composer's
    /// baseline to the first line 2.9705, paragraph to paragraph 4.5000, the last baseline to
    /// the staff's top line 4.9312 where the page has 5.09 (the book's own difference, with
    /// or without a text: docs/markdown-text-design.md section 7.1), the last staff's middle
    /// line to an <c>end</c> text 12.0000.
    /// </summary>
    [Fact]
    public void YMarginZero_IsLilyPondsOwnSpacing_AsMeasured()
    {
        var (composer, text, staffTop, _) = Drawn(Book(top: $"{F}md yMargin 0\nOne line.\n\nAnother.\n{F}"), "One line.", "Another.");
        Assert.Equal(2.95, text[0] - composer, 2);
        Assert.Equal(4.50, text[1] - text[0], 2);
        Assert.Equal(5.09, staffTop - text[1], 2);
        var (_, end, _, middle) = Drawn(Book(top: $"{F}md end yMargin 0\nThe end.\n{F}"), "The end.");
        Assert.Equal(12.00, end[0] - middle, 2);
    }

    /// <summary>
    /// With no <c>yMargin</c> a text keeps <see cref="ProseBand.DefaultMargin"/> above and
    /// below (the owner, 2026-11-13: "the white above and below the md text is tight; widen it
    /// a little, or make it settable"), and <c>yMargin TOP [BOTTOM]</c> sets it.
    /// MEASURED 2026-11-13, LilyPond 2.26.0 (md/margins p-default, p-31 and their twins): the
    /// composer's baseline to the first line 3.9705 and 5.9705, the last baseline to the
    /// staff's top line 5.9312 in both (the page: 3.95, 5.95, 6.09).
    /// </summary>
    [Fact]
    public void ATextKeepsOneStaffSpaceAboveAndBelow_AndYMarginSetsIt()
    {
        const string body = "\nOne line.\n\nAnother.\n";
        var tight = Drawn(Book(top: $"{F}md yMargin 0{body}{F}"), "One line.", "Another.");
        var plain = Drawn(Book(top: $"{F}md{body}{F}"), "One line.", "Another.");
        var set = Drawn(Book(top: $"{F}md yMargin 3 1{body}{F}"), "One line.", "Another.");
        var mm = Drawn(Book(top: $"{F}md yMargin 5mm{body}{F}"), "One line.", "Another.");
        Assert.Equal(tight.Composer, plain.Composer, 9);
        // The default: 1.0 further under the composer's line, the music 1.0 further under the text.
        Assert.Equal(3.95, plain.Text[0] - plain.Composer, 2);
        Assert.Equal(6.09, plain.StaffTop - plain.Text[1], 2);
        Assert.Equal(tight.StaffTop + 2.0, plain.StaffTop, 2);
        // yMargin 3 1: 3 above, 1 below.
        Assert.Equal(5.95, set.Text[0] - set.Composer, 2);
        Assert.Equal(6.09, set.StaffTop - set.Text[1], 2);
        // A length in millimetres is the paper's: 5 mm is 2.845 staff spaces, both ways
        // (the SVG's two places of each end: within 0.011).
        Assert.InRange(mm.Text[0] - mm.Composer - (2.95 + Mm(5)), -0.011, 0.011);
        Assert.InRange(mm.StaffTop - mm.Text[1] - (5.09 + Mm(5)), -0.011, 0.011);
    }

    /// <summary>
    /// The margin is part of the text's EXTENT in the page's chain, as the twin's
    /// <c>\with-dimension</c> is of its line's: where a basic-distance already holds more
    /// white than the extents and the padding ask, nothing moves.
    /// MEASURED 2026-11-13, LilyPond 2.26.0 (md/margins p-end, p-end8 and their twins): the
    /// last staff's middle line to a closing text's first baseline 12.0000 at the default
    /// margin as at none, and 14.0450 at <c>yMargin 8</c> (the page: 12.00, 14.05).
    /// </summary>
    [Fact]
    public void UnderTheLastSystem_TheMarginIsInsideTheTwelve_UntilItOutgrowsIt()
    {
        var tight = Laid(Book(top: $"{F}md end yMargin 0\nA closing note.\n{F}")).Pages[0];
        var plain = Laid(Book(top: $"{F}md end\nA closing note.\n{F}")).Pages[0];
        var wide = Laid(Book(top: $"{F}md end yMargin 8\nA closing note.\n{F}")).Pages[0];
        double Under(PageLayout page) => page.EndTextY - (page.Height - page.Systems[^1].Y + 2.0);
        Assert.Equal(12.0, Under(tight), 6);
        Assert.Equal(12.0, Under(plain), 6);
        Assert.True(Under(wide) > 13.5, Under(wide).ToString(CultureInfo.InvariantCulture));
        Assert.Equal((1.0, 1.0, 8.0), (plain.EndText!.MarginTop, plain.EndText.MarginBottom, wide.EndText!.MarginTop));
    }

    /// <summary>A box's frame stands where it stood before every text had a margin: the
    /// default is ONE white for all texts, not a text's and a box's added; and
    /// <c>yMargin</c> moves the white outside the frame, not the frame's padding.</summary>
    [Fact]
    public void ABoxesFrameIsInsideTheMargin_AndTheDefaultIsNotAddedTwice()
    {
        var boxed = Band("One.", new ProseShape(0, 0, false, true));
        Assert.Equal((1.0, 1.0), (boxed.MarginTop, boxed.MarginBottom));
        Assert.Equal(2.0 + 1.1 + 1.0, boxed.Up, 12);
        var set = Band("One.", new ProseShape(0, 0, false, true, 3, 0));
        Assert.Equal(2.0 + 1.1 + 3.0, set.Up, 12);
        Assert.Equal(1.0 + 1.1, set.Down, 12);
        // On the page the rule is drawn the margin inside the band: 3 under where the band begins.
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book(top: $"{F}md box yMargin 3 0\nOne.\n{F}")));
        var page = Laid(Book(top: $"{F}md box yMargin 3 0\nOne.\n{F}")).Pages[0];
        string top = (page.HeaderTextY - 2.0 - 1.1).ToString("0.00", CultureInfo.InvariantCulture);
        Assert.Contains($"y=\"{top}\" width=\"{LineWidth.ToString("0.00", CultureInfo.InvariantCulture)}\" height=\"0.10\"", svg);
        Assert.Equal(page.HeaderTop + page.Header!.Depth + 0.5 + 3.0 + 1.1 + 2.0, page.HeaderTextY, 6);
    }

    /// <summary>The band clamps what did not come through the validator (another caller, a
    /// test): never negative, never over <see cref="ProseBand.MaximumMargin"/>.</summary>
    [Fact]
    public void AMarginOutOfRange_IsClampedInTheBand()
    {
        var band = Band("One.", new ProseShape(0, 0, false, false, -4, 900));
        Assert.Equal((0.0, ProseBand.MaximumMargin), (band.MarginTop, band.MarginBottom));
        Assert.Equal((2.0, 1.0 + ProseBand.MaximumMargin), (band.Up, band.Down));
    }

    [Fact]
    public void TheTwin_WritesTheMarginAsTheFirstAndLastLinesExtent()
    {
        // One line carries both; 3 above and 1 below its box of 2 over and 1 under.
        Assert.Contains("  \\with-dimension #Y #'(-2 . 5) \"One.\"\n", Twin(Book(top: $"{F}md yMargin 3 1\nOne.\n{F}")));
        // Millimetres through the paper's conversion (5 mm = 2.8453).
        Assert.Contains("  \\with-dimension #Y #'(-3.8453 . 4.8453) \"One.\"\n", Twin(Book(top: $"{F}md yMargin 5mm\nOne.\n{F}")));
        // In a box: the line's extent past the frame's (2 + 1.1 over, 1 + 1.1 under).
        Assert.Contains("  \\with-dimension #Y #'(-2.1 . 6.1) \\concat { \\hspace #1.1", Twin(Book(top: $"{F}md box yMargin 3 0\nOne.\n{F}")));
        // None: LilyPond's own line.
        Assert.Contains("\\markuplist \\column-lines {\n  \"One.\"\n}", Twin(Book(top: $"{F}md yMargin 0\nOne.\n{F}")));
    }

    // ---- the twin ----------------------------------------------------------------------

    private static string Twin(string book) => new LilyPondExporter().Export(SyntaxTree.Parse(book));

    [Fact]
    public void TheTwin_WritesTheXMarginAsWhite_AndLeavesTheCentringToLilyPond()
    {
        string ly = Twin(Book(top: $"{F}md xMargin 10 30\nOne **bold** line.\n\nSecond.\n{F}\n{F}md end center xMargin 10 30\nThe end.\n{F}"));
        Assert.Contains("  \\with-dimension #Y #'(-1 . 3) \\concat { \\hspace #10 \\concat { \"One \" \\bold \"bold\" \" line.\" } }\n", ly);
        Assert.Contains("  \\with-dimension #Y #'(-2 . 3.5) \\concat { \\hspace #10 \"Second.\" }\n", ly);
        // \fill-line of one markup centres it in the line-width: LilyPond's own measure of the words.
        string width = Math.Round(LineWidth - 40, 4).ToString("0.####", CultureInfo.InvariantCulture);
        Assert.Contains($"  \\with-dimension #Y #'(-2 . 3) \\concat {{ \\hspace #10 \\override #'(line-width . {width}) \\fill-line {{ \"The end.\" }} }}\n", ly);
        // With no xMargin a centred line begins at the margin.
        Assert.Contains("  \\concat { \\override #'(line-width . ", Twin(Book(top: $"{F}md center yMargin 0\nOne.\n{F}")));
    }

    /// <summary>MEASURED, LilyPond 2.26.0 (showcase.ly → lp-showcase.svg): this form's frame
    /// stands at the left margin 8.5358, 102.4299 wide, 3.1000 over the first baseline and
    /// 5.1000 under it for two lines; a paragraph's \vspace #0.5 steps 4.5000.</summary>
    [Fact]
    public void TheTwin_WritesABoxedText_AsLilyPondsBox_AroundAColumnOfThePagesExtent()
    {
        string ly = Twin(Book(top: $"{F}md box xMargin 10 30\nOne.\n\nTwo.\n{F}"));
        string width = Math.Round(LineWidth - 40 - 2.2, 4).ToString("0.####", CultureInfo.InvariantCulture);
        Assert.Contains(
            "\\markuplist \\column-lines {\n"
            + "  \\with-dimension #Y #'(-7.6 . 4.1) \\concat { \\hspace #11.1 \\override #'(box-padding . 1) \\box\n"
            + $"    \\with-dimension #X #'(0 . {width}) \\with-dimension #Y #'(-5.5 . 2) \\column {{\n"
            + "      \"One.\"\n"
            + "      \\vspace #0.5\n"
            + "      \"Two.\"\n"
            + "    } }\n"
            + "}\n", ly);
        // Centred in the box: each line LilyPond's to centre in the text's width.
        Assert.Contains($"      \\override #'(line-width . {width}) \\fill-line {{ \"One.\" }}\n",
            Twin(Book(top: $"{F}md box center xMargin 10 30\nOne.\n{F}")));
        // The frame's edge is at the margin with no xMargin: the white is the inset alone.
        Assert.Contains(") \\concat { \\hspace #1.1 \\override #'(box-padding . 1) \\box\n", Twin(Book(top: $"{F}md end box\nOne.\n{F}")));
    }

    [Fact]
    public void TheTwin_WritesAScoresOwnText_NotTheFiles()
    {
        string ly = Twin(Book(top: $"{F}md box\nThe file's.\n{F}", inScore: $"{F}md center\nThis score's.\n{F}\n"));
        Assert.Contains("\\fill-line { \"This score's.\" }", ly);
        Assert.DoesNotContain("The file's.", ly);
        Assert.DoesNotContain("box-padding", ly);
    }
}
