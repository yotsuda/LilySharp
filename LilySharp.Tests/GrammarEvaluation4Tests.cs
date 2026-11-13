// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What four more large scores written from the docs found on 2026-11-13 ("grammar evaluation
/// 4": a big-band chart, a choral piece with piano, a chamber piece, a rock song). This file
/// begins with the PAGE TEXT (the Markdown fence, docs/markdown-text-design.md). Each test is
/// one finding, as it was met.
/// </summary>
public class GrammarEvaluation4Tests
{
    private const string F = "```";

    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static ScoreLayout Laid(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static int Count(string haystack, string needle) => Regex.Matches(haystack, Regex.Escape(needle)).Count;

    // ---- 2. two fences for one place, two shapes --------------------------------------------

    private static string Titled(string top, string inScore = "") => $"title \"T\"\ncomposer \"C\"\n{top}\n"
        + $"part m {{ clef treble }}\nsection A {{ m {{ c'1 | }} }}\nform {{ ~A }}\nscore {{ {inScore} staff m }}\n";

    private static string TextOf(ProseLine line) => string.Concat(line.Pieces.Select(p => p.Text));

    /// <summary>A boxed note and a centred dedication under the title: "two fences for one
    /// place are one text, shaped by the first; a shape word on the second is an error"
    /// (LYS4057). They are a sequence of blocks now, each with its own shape, stacked in
    /// written order with their own margins between them. MEASURED 2026-11-13, LilyPond 2.26.0
    /// (LilySharp-Lab sessions/p1226/md/eval4/fixA/i2, r02.lys and its twin): the boxed
    /// block's last baseline to the centred line's, 6.10 against 6.1001.</summary>
    [Fact]
    public void TwoFencesForOnePlace_AreBlocks_EachWithItsOwnShape()
    {
        string source = Titled($"{F}md box\n# Note\n- one\n{F}\n{F}md center\n*for my choir*\n{F}");
        Assert.DoesNotContain(Said(source), d => d.Severity == DiagnosticSeverity.Error);
        var page = Laid(source).Pages[0];
        var place = page.Header!.Text!;
        var blocks = place.Blocks.ToArray();
        Assert.Equal(2, blocks.Length);
        var (box, dedication) = (blocks[0].Block, blocks[1].Block);
        Assert.NotNull(box.Frame);
        Assert.False(box.Center);
        Assert.Null(dedication.Frame);
        Assert.True(dedication.Center);
        Assert.Equal("for my choir", TextOf(Assert.Single(dedication.Lines)));
        // Extent to extent: the box's white below (1.0) and the line's white above (1.0)
        // stand between the frame's rule and the line's box.
        Assert.Equal(0, blocks[0].Offset);
        Assert.Equal(box.Down + dedication.Up, blocks[1].Offset, 9);
        Assert.Equal(1.0 + 1.0 + 2.0, blocks[1].Offset - (box.Down - box.MarginBottom), 9);
        // The band the page spaces is the whole stack: the music stands under the last block.
        Assert.Equal(blocks[1].Offset + dedication.Down, place.Down, 9);
        Assert.True(page.Height - page.Systems[0].Y > page.HeaderTextY + place.Down);
        // The centred line is centred in the line width, not in the box's narrower one.
        double lineWidth = LayoutOptions.Default.ContentWidth;
        Assert.Equal(lineWidth / 2, dedication.Lines[0].Left + dedication.Lines[0].Width / 2, 6);

        // Drawn: the frame's four rules once, the dedication once, in its slant.
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source));
        Assert.Equal(1, Count(svg, ">for my choir<"));
        Assert.Equal(1, Count(svg, ">Note<"));

        // The twin writes each block into the one list: the box, then the centred line.
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source));
        Assert.Equal(1, Count(ly, "\\markuplist \\column-lines"));
        Assert.True(ly.IndexOf("\\box", StringComparison.Ordinal) < ly.IndexOf("\\fill-line { \\concat { \\italic \"for my choir\" } }", StringComparison.Ordinal));
    }

    /// <summary>What the blocks leave as it was: fences with no shape words are one text,
    /// the second its next paragraph; each place and each score has its own sequence.</summary>
    [Fact]
    public void PlainFences_AreOneTextAsBefore_AndEachPlaceHasItsOwnBlocks()
    {
        var plain = Laid(Titled($"{F}md\nFirst.\n{F}\n{F}md\nSecond.\n{F}")).Pages[0].Header!.Text!;
        var one = Laid(Titled($"{F}md\nFirst.\n\nSecond.\n{F}")).Pages[0].Header!.Text!;
        Assert.Single(plain.Blocks);
        Assert.Equal(one.Lines.Select(l => (TextOf(l), l.Baseline)), plain.Lines.Select(l => (TextOf(l), l.Baseline)));
        Assert.Equal((one.Up, one.Down), (plain.Up, plain.Down));

        // The closing text takes blocks too; a score's fences replace the file's sequence whole.
        string file = $"{F}md box\nA\n{F}\n{F}md center\nB\n{F}\n{F}md end center\nC\n{F}\n{F}md end xMargin 10 box\nD\n{F}";
        var page = Laid(Titled(file)).Pages[0];
        Assert.Equal(2, page.Header!.Text!.Blocks.Count());
        Assert.Equal(2, page.EndText!.Blocks.Count());
        Assert.NotNull(page.EndText.Blocks.Last().Block.Frame);
        var own = Laid(Titled(file, inScore: $"{F}md center\nMine\n{F}\n")).Pages[0];
        Assert.Equal("Mine", TextOf(Assert.Single(Assert.Single(own.Header!.Text!.Blocks).Block.Lines)));
        Assert.Equal(2, own.EndText!.Blocks.Count());
    }

    // ---- 3. a style change inside Japanese text ---------------------------------------------

    /// <summary>A wide gap after a bold or italic JAPANESE run: the layout reserved an em
    /// square a character and handed the drawer the whole run, which the machine's face set
    /// by its own (narrower) advances — MEASURED on the PNG of choral/r-08: 2.9 staff spaces
    /// of white after seven bold characters, 4.3 after seven italic ones. Every character the
    /// text's face lacks is now a piece at its own square, so no run can end short of the
    /// next piece — whatever face draws it, in the SVG, the PDF and the PNG.</summary>
    [Fact]
    public void AStyleChangeInJapaneseText_LeavesNoGap_EachCharacterStandsAtItsSquare()
    {
        string source = Titled($"{F}md\n**ことばを大切に**、子音を早めに。*ささやくように*。ふつうの文。\n**Bold** next, *italic* next.\n{F}");
        var line = Assert.Single(Laid(source).Pages[0].Header!.Text!.Lines);
        double em = 2.2;
        var wide = line.Pieces.Where(p => p.Text.Length == 1 && p.Text[0] > 0x2E80).ToArray();
        Assert.Equal("ことばを大切に、子音を早めに。ささやくように。ふつうの文。", string.Concat(wide.Select(p => p.Text)));
        // One character a piece, each an em after the one before — across the changes of
        // style too: bold → plain at 、, plain → italic at さ, italic → plain at 。
        for (int i = 0; i < wide.Length; i++)
            Assert.Equal(i * em, wide[i].X, 9);
        Assert.Equal(LilySharp.Core.Rendering.FontStyle.Bold, wide[6].Style);       // に
        Assert.Equal(LilySharp.Core.Rendering.FontStyle.Regular, wide[7].Style);    // 、
        Assert.Equal(LilySharp.Core.Rendering.FontStyle.Italic, wide[15].Style);    // さ
        // Latin words are still one piece a style, set by the text's own (measured) face.
        Assert.Contains(line.Pieces, p => p.Text == "Bold");
        Assert.Contains(line.Pieces, p => p.Text == " next, ");

        // The SVG says each character's x, so a viewer's face cannot move one off its square.
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source));
        var xs = Regex.Matches(svg, "<text x=\"([\\d.]+)\"[^>]*>([^<])</text>").Where(m => m.Groups[2].Value[0] > 0x2E80)
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(wide.Length, xs.Length);
        for (int i = 1; i < xs.Length; i++)
            Assert.Equal(em, xs[i] - xs[i - 1], 2);

        // The twin hands LilyPond whole strings again: it sets them by its own face.
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source));
        Assert.Contains("\\bold \"ことばを大切に\" \"、子音を早めに。\" \\italic \"ささやくように\"", ly);
    }

    // ---- 4. a swing tempo's equation over the first system, under a page text ---------------

    /// <summary>The swing equation's triplet bracket overprinted the last line of the text
    /// under the title: on a score of several staves the pass that prices the systems for the
    /// page was handed the opening tempo WITHOUT its swing equation (and the equation's own
    /// top left out the bracket's number), so the first system's ink above the staff ended at
    /// the metronome note's stem. The equation is part of what the text clears now.</summary>
    [Theory]
    [InlineData("layout { markTempo beside }")]
    [InlineData("")]
    public void ASwingEquationOverTheFirstSystem_ClearsTheTextUnderTheTitle(string layout)
    {
        string Book(string tempo) => $"title \"Collision\"\npoet \"Medium swing chart\"\ncomposer \"writer\"\n{tempo}\n"
            + $"{F}md\n**Performance notes**\n\n- Eighth notes swing throughout.\n{F}\n"
            + "part a { clef treble } part b { clef treble } part c { clef treble }\n"
            + $"section Intro {{ a {{ c'1 | }} b {{ c'1 | }} c {{ c'1 | }} }}\nform {{ Intro }}\n{layout}\n"
            + "score { staff a staff b staff c }\n";
        var swung = Laid(Book("tempo \"Medium Swing\" 4 = 138 swing"));
        var straight = Laid(Book("tempo \"Medium Swing\" 4 = 138"));
        var page = swung.Pages[0];
        var tempo = Assert.Single(swung.MusicMarkLayouts, m => m.MarkType == LilySharp.Core.Svg.Model.MusicMarkType.Tempo);
        Assert.NotEqual(0, tempo.SwingSubdivision);
        var ink = MetronomeMarkGeometry.Ink(LilySharp.Core.Rendering.ScoreTextMetrics.Bundled, tempo.Text, tempo.TempoText,
            tempo.TempoBeatUnit, tempo.TempoDots, tempo.SwingSubdivision);
        // The equation's top is its bracket's number, over the bracket's line.
        var swing = MetronomeMarkGeometry.Swing(LilySharp.Core.Rendering.ScoreTextMetrics.Bundled, 8);
        double line = swing.Pieces.Where(p => p.Kind == MetronomeMarkGeometry.SwingPieceKind.BracketLine).Max(p => p.Y0);
        Assert.True(swing.Top > line + 0.3, $"{swing.Top} over the line at {line}");
        Assert.Equal(swing.Top, ink.Top, 9);
        // Down the page: the text's band, markup-system-spacing's padding, the mark's top.
        double textBottom = page.HeaderTextY + page.Header!.Text!.Down;
        double middle = page.Height - page.Systems[0].Y + 2.0;
        double markTop = middle - (tempo.YUp + ink.Top);
        Assert.True(markTop >= textBottom + 0.5 - 1e-6, $"the equation's top {markTop:F3} under the text's {textBottom:F3}");
        // …and beside the label it is the equation that holds the system there: the same
        // book with no swing stands higher by what the equation adds over the metronome note.
        // (Stacked, the section label stands over the tempo and is the system's top either way.)
        double lower = (page.Height - page.Systems[0].Y) - (straight.Pages[0].Height - straight.Pages[0].Systems[0].Y);
        if (layout.Length > 0)
            Assert.True(lower > 1.0, $"moved {lower:F3}");
        else
            Assert.Equal(0, lower, 6);
    }

    // ---- 1. the closing text on a score of several pages ----------------------------------

    /// <summary>A piece of <paramref name="pages"/> pages (a <c>pageBreak</c> between them)
    /// with a closing text, the file's or the score's own.</summary>
    private static string Paged(int pages, string fence, bool inScore = false)
    {
        string bars = string.Join(" | pageBreak ", Enumerable.Repeat("c'1", pages));
        return $"title \"T\"\n{(inScore ? "" : fence)}\npart m {{ clef treble }}\n"
            + $"section A {{ m {{ {bars} | }} }}\nform {{ ~A }}\nscore {{ {(inScore ? fence + "\n" : "")} staff m }}\n";
    }

    /// <summary><c>md end</c> text was printed on NO page once the score had two or more, and
    /// nothing was said: the layout placed it on the last page, and the draw looked for it
    /// only where it drew the title — the first.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void TheClosingText_IsDrawnUnderTheLastSystemOfTheLastPage(int pages, bool inScore)
    {
        string source = Paged(pages, $"{F}md end xMargin 30mm\n## Words\nENDTEXT line one\n{F}", inScore);
        var layout = Laid(source);
        Assert.Equal(pages, layout.Pages.Length);
        // The last page holds it, under its last system; no other page does.
        Assert.All(layout.Pages.Take(pages - 1), p => Assert.Null(p.EndText));
        var last = layout.Pages[^1];
        Assert.NotNull(last.EndText);
        Assert.Single(last.Systems);
        Assert.True(last.EndTextY - last.EndText!.Up > last.Height - last.Systems[^1].Y + 4.0);
        Assert.True(last.EndTextY + last.EndText.Down <= last.Height - LayoutOptions.Default.MarginBottom + 1e-9);
        // Drawn once: the one SVG of every page, and the last page's PNG alone.
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(1, Count(SvgGenerator.Generate(tree), ">ENDTEXT line one<"));
        var png = LilySharp.Core.Png.PngGenerator.GeneratePages(tree);
        var bare = LilySharp.Core.Png.PngGenerator.GeneratePages(SyntaxTree.Parse(Paged(pages, "")));
        Assert.Equal(pages, png.Count);
        Assert.NotEqual(bare[^1], png[^1]);
        // …and the PDF's pages carry it too (the same draw): the file is not the bare one's size.
        Assert.NotEqual(LilySharp.Core.Pdf.PdfGenerator.Generate(SyntaxTree.Parse(Paged(pages, ""))).Length,
            LilySharp.Core.Pdf.PdfGenerator.Generate(tree).Length);
    }

    /// <summary>…and with no <c>pageBreak</c> written: the breaker's own pages.</summary>
    [Fact]
    public void TheClosingText_IsDrawnOnTheLastPageTheBreakerMakes()
    {
        string phrases = string.Join(' ', Enumerable.Repeat("x", 100));
        string source = $"octave absolute\ntitle \"T\"\n{F}md end center\nENDTEXT line one\n{F}\npart a {{ clef treble }}\n"
            + "phrase x { c'4 d' e' f' | g'4 a' b' c'' | }\n"
            + $"section A {{ a {{ {phrases} }} }}\nform {{ ~A }}\nscore {{ staff a }}\n";
        var layout = Laid(source);
        Assert.True(layout.Pages.Length >= 2);
        Assert.All(layout.Pages.Take(layout.Pages.Length - 1), p => Assert.Null(p.EndText));
        Assert.NotNull(layout.Pages[^1].EndText);
        Assert.All(layout.Pages, p => Assert.Equal(0, p.Overflow));
        Assert.Equal(1, Count(SvgGenerator.Generate(SyntaxTree.Parse(source)), ">ENDTEXT line one<"));
    }

    /// <summary>A closing text the last page has no room for — it and the last system are
    /// taller than the paper — opens a page of its own after the music (LilyPond sends a
    /// trailing markup to the next page); the music stands as it does with no text. It was
    /// one over-full page, its system pressed up into the top margin.</summary>
    [Fact]
    public void AClosingTextTooTallForTheLastPage_TakesAPageAfterIt()
    {
        string Book(int lines) => "title \"T\"\n"
            + (lines == 0 ? "" : $"{F}md end\n{string.Join("\\\n", Enumerable.Range(1, lines).Select(n => $"FIT line {n}"))}\n{F}\n")
            + "part m { clef treble }\nsection A { m { c'1 | c'1 | c'1 | c'1 | } }\nform { ~A }\nscore { staff m }\n";
        var bare = Laid(Book(0));
        var layout = Laid(Book(48));
        Assert.Equal(2, layout.Pages.Length);
        Assert.Null(layout.Pages[0].EndText);
        Assert.Equal(bare.Pages[0].Systems.Select(s => s.Y), layout.Pages[0].Systems.Select(s => s.Y));
        var text = layout.Pages[1];
        Assert.Empty(text.Systems);
        Assert.Equal(48, text.EndText!.Lines.Length);
        Assert.Equal(0, text.Overflow);
        var paper = LayoutOptions.Default;
        Assert.True(text.EndTextY - text.EndText.Up >= paper.MarginTop);
        Assert.True(text.EndTextY + text.EndText.Down <= text.Height - paper.MarginBottom);
        var tree = SyntaxTree.Parse(Book(48));
        Assert.Equal(1, Count(SvgGenerator.Generate(tree), ">FIT line 48<"));
        Assert.Equal(2, LilySharp.Core.Png.PngGenerator.GeneratePages(tree).Count);

        // A text taller than the paper itself is still not broken over a page: it runs off
        // its own page, the music is whole, and the warning says which it is.
        var warnings = new List<string>();
        var tall = Laid(Book(70));
        LayoutWarnings.Report(tall, paper, warnings.Add);
        Assert.Equal(0, tall.Pages[0].Overflow);
        Assert.Contains("`md end` text is taller than the page", Assert.Single(warnings));
    }
}
