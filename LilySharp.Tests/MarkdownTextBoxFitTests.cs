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
/// A boxed page text in its frame, and a page text's width (docs/markdown-text-design.md
/// section 5.5): the text stands in the frame's OPTICAL middle (the owner, 2026-11-13: "with
/// one line in the box, the text's y is not centred in the box; can it be centred?";
/// LILYSHARP-OWN, measured against the twin in LilyPond 2.26.0, LilySharp-Lab
/// sessions/p1226/md/boxfit), and a text that writes no <c>xMargin</c>, boxed or plain, runs
/// from the page's left margin to its right one (the owner, 2026-11-14: "既定は0にしておいて。
/// 気に入らなければ、ユーザーが好きな値を指定できるから" — for a day it stood on the staff).
/// </summary>
[Trait("Category", "Unit")]
public class MarkdownTextBoxFitTests
{
    private const string F = "```";

    private static readonly ScoreTextMetrics Fonts = ScoreTextMetrics.Bundled;
    private static readonly double Em = ProseBand.Em(Fonts);
    private static readonly double LineWidth = LayoutOptions.Default.ContentWidth;
    private static readonly double Margin = LayoutOptions.Default.MarginLeft;

    // ---- 1: the text in the frame's optical middle ---------------------------------------

    /// <summary>The white inside a boxed text's frame over its first baseline and under its last.</summary>
    private static (double Over, double Under) Inside(ProseBand band)
        => (band.Up - band.MarginTop - band.Frame!.Thickness + band.Lines[0].Baseline,
            band.Down - band.MarginBottom - band.Frame.Thickness - band.Lines[^1].Baseline);

    /// <summary>The white between the frame's inside and the INK of a one-line boxed text, above
    /// and below, for an ink that reaches <paramref name="top"/> over the baseline and
    /// <paramref name="bottom"/> under it.</summary>
    private static (double Above, double Below) White(string text, double top, double bottom)
    {
        var band = ProseBand.Build(text, 0, Fonts, 100, new ProseShape(0, 0, true, true))!;
        Assert.Single(band.Lines);
        var (over, under) = Inside(band);
        return (over - top, under - bottom);
    }

    private static (double Above, double Below) WhiteOfLatin(string text)
    {
        var ink = Fonts.Ink(text, Em, TextRole.Markdown);
        return White(text, ink.Top, -ink.Bottom);
    }

    /// <summary>
    /// The frame's inside is centred on the line's optical middle, 0.37 em over the baseline
    /// (<see cref="ProseBand.OpticalMiddle"/>): 3.314 over the baseline and 1.686 under at the
    /// default size, where the padding around the line's BOX gave 3.0 and 2.0.
    /// </summary>
    [Fact]
    public void TheFramesInsideIsCentredOnTheOpticalMiddle_AndIsAsTallAsItWas()
    {
        Assert.Equal(0.37, ProseBand.OpticalMiddle);
        Assert.Equal(0.314, ProseBand.BoxShift(Fonts), 9);
        var band = ProseBand.Build("One.", 0, Fonts, 100, new ProseShape(0, 0, false, true))!;
        var (over, under) = Inside(band);
        Assert.Equal((3.314, 1.686), (Math.Round(over, 9), Math.Round(under, 9)));
        Assert.Equal(over - ProseBand.OpticalMiddle * Em, under + ProseBand.OpticalMiddle * Em, 9);
        // The frame: 5.2 tall outside, as before; the band's Up — where the page stands the
        // frame under what is over it — as before; the text 0.314 lower in it.
        Assert.Equal(2.0 + 1.1 + ProseBand.DefaultMargin, band.Up, 9);
        Assert.Equal(1.0 + 1.1 + ProseBand.DefaultMargin, band.Down, 9);
        Assert.Equal(0.314, band.Lines[0].Baseline, 9);
        // A text with no frame is where it was: its reference point is its first baseline.
        Assert.Equal(0.0, ProseBand.Build("One.", 0, Fonts, 100)!.Lines[0].Baseline);
        Assert.Equal(0.0, ProseBand.Build("One.", 0, Fonts, 100, new ProseShape(5, 5, true, false))!.Lines[0].Baseline);
    }

    /// <summary>
    /// One line of Japanese and one of Latin capitals have as much white over their ink as
    /// under it, within 0.15 of a staff space: 1.378 / 1.422 and 1.693 / 1.653 (before the
    /// shift 1.064 / 1.736 and 1.379 / 1.967 — 0.67 and 0.59 more under than over).
    /// </summary>
    /// <remarks>
    /// The capitals' ink is the bundled face's own. The Japanese line's is the em square a
    /// Japanese face draws in — 0.88 em over the baseline, 0.12 under, the ideographic em box
    /// — since the face is the machine's and the layout does not measure it (ProseBand.Break
    /// sets each such character in a cell of one em). MEASURED 2026-11-13 on the PNG of the
    /// owner's example at 20 px a staff space (LilySharp-Lab sessions/p1226/md/boxfit
    /// owner.lys, this machine's face): ほえほえ has 1.50 over its ink and 1.60 under (before:
    /// 1.19 / 1.91); HOEHOE 1.65 / 1.65 (1.34 / 1.96).
    /// </remarks>
    [Fact]
    public void OneLineOfJapanese_AndOneOfCapitals_HaveTheSameWhiteAboveAndBelowTheirInk()
    {
        const double tolerance = 0.15;
        var japanese = White("ほえほえ", 0.88 * Em, 0.12 * Em);
        Assert.InRange(japanese.Above - japanese.Below, -tolerance, tolerance);
        Assert.Equal((1.378, 1.422), (Math.Round(japanese.Above, 3), Math.Round(japanese.Below, 3)));

        var capitals = WhiteOfLatin("HOEHOE");
        Assert.InRange(capitals.Above - capitals.Below, -tolerance, tolerance);
        // The face's capitals: what the constant was chosen against.
        var h = Fonts.Ink("H", Em, TextRole.Markdown);
        Assert.Equal(0.0, h.Bottom, 3);
        Assert.InRange(h.Top / Em / 2, ProseBand.OpticalMiddle - 0.03, ProseBand.OpticalMiddle + 0.03);
    }

    /// <summary>
    /// A line of lower-case letters stands low of the middle by its ink — the eye's middle of
    /// such a line is between the baseline and the x-height, under the capitals' — and is NOT
    /// special-cased: a text's lines are of every kind, and a frame that moved with its
    /// letters would stand differently round two notes on one page.
    /// </summary>
    /// <remarks>
    /// By the face's ink (white over / under; before the shift in brackets): a line of
    /// x-height letters alone, "moon over a canon", 2.256 / 1.653 (1.942 / 1.967) — its ink's
    /// middle 0.30 under the frame's; with one descender, "moon over a canyon", 2.256 / 1.233
    /// (1.942 / 1.547) — 0.51 under; a sentence, "Play gently.", 1.693 / 1.233 (1.379 /
    /// 1.547) — 0.23 under, where it was 0.08 over. ⚠️ An all-lower-case line was nearer its
    /// ink's middle BEFORE. MEASURED on the PNGs too (LilySharp-Lab sessions/p1226/md/boxfit
    /// lower.lys, mixed.lys; 20 px a staff space): 2.20 / 1.25 and 1.65 / 1.25. Judged on
    /// the PNG: a little low, not ugly — the eye sets such a line by its x-height band and
    /// discounts the one descender. If it is to be mended, the mend is a middle chosen from
    /// the text's own letters (capitals or wide characters anywhere: 0.37; none: about 0.23,
    /// half the x-height), which makes two frames on one page differ — proposed, not built.
    /// </remarks>
    [Fact]
    public void ALowerCaseLine_StandsALittleLow_ByThisMuch()
    {
        var xHeight = WhiteOfLatin("moon over a canon");       // x-height, no descender
        Assert.Equal(0.30, (xHeight.Above - xHeight.Below) / 2, 2);
        var descender = WhiteOfLatin("moon over a canyon");
        Assert.Equal(0.51, (descender.Above - descender.Below) / 2, 2);
        var sentence = WhiteOfLatin("Play gently.");
        Assert.InRange((sentence.Above - sentence.Below) / 2, 0.10, 0.28);
    }

    /// <summary>Several lines: the same shift — the white over the first line's optical
    /// middle is the white under the last line's. A first line that is a heading takes the
    /// shift at its own size; a text that ends with a list ends like any line.</summary>
    [Fact]
    public void SeveralLines_AHeadingFirst_AListLast_AreCentredTheSameWay()
    {
        double h1 = ProseBand.HeadingScale(1);
        var box = new ProseShape(0, 0, false, true);
        var three = ProseBand.Build("One.\n\nTwo.\n\nThree.", 0, Fonts, 100, box)!;
        Assert.Equal((3.314, 1.686), (Math.Round(Inside(three).Over, 9), Math.Round(Inside(three).Under, 9)));
        Assert.Equal(9.0, three.Lines[^1].Baseline - three.Lines[0].Baseline, 9);

        var headed = ProseBand.Build("# Head\n\nText.", 0, Fonts, 100, box)!;
        var (over, under) = Inside(headed);
        Assert.Equal(1.0 + (2.0 + 0.314) * h1, over, 9);
        Assert.Equal(1.686, under, 9);
        // …so the heading's optical middle is as far under the frame as the last line's is over it,
        // each line's box being as tall as the line is large.
        Assert.Equal(over - ProseBand.OpticalMiddle * Em * h1 - 1.5 * h1, under + ProseBand.OpticalMiddle * Em - 1.5, 9);
        // The band's Up is the heading's box as it was; the frame is 0.314 × (h1 − 1) taller below.
        Assert.Equal(2.0 * h1 + 1.1 + ProseBand.DefaultMargin, headed.Up, 9);
        var plain = ProseBand.Build("# Head\n\nText.", 0, Fonts, 100)!;
        Assert.Equal(plain.Up + plain.Down + 2.2 + 0.314 * (h1 - 1), headed.Up + headed.Down, 9);

        var listed = ProseBand.Build("Remember:\n\n- one\n- two", 0, Fonts, 100, box)!;
        Assert.Equal((3.314, 1.686), (Math.Round(Inside(listed).Over, 9), Math.Round(Inside(listed).Under, 9)));
    }

    // ---- the page ------------------------------------------------------------------------

    private const string OneStaff = """
        octave relative
        part m { clef treble }
        section A { m { c'4 d e f | g1 | c,4 d e f | g1 | } }
        form { A }
        score { staff m }
        """;

    private static readonly string Bars = "c'4 d e f | g1 | f4 e d c | c1 | " + string.Concat(Enumerable.Repeat("c4 d e f | g1 | f4 e d c | c1 | ", 6));

    /// <summary>Equal as the SVG writes a length: to its two places (a right end is an x and
    /// a width, each rounded).</summary>
    private static void Near(double expected, double actual)
        => Assert.InRange(actual, expected - 0.016, expected + 0.016);

    private static string Named(string parts, string score, string paper = "") => $$"""
        title "Morning Song"
        composer "A. Composer"
        {{paper}}
        {{parts}}
        octave relative
        section A { a { {{Bars}} } b { {{Bars}} } }
        form { A }
        score { {{score}} }
        """;

    private const string LongNames = "part a \"Violoncello piccolo\" \"Vc. p.\" { clef treble }\npart b \"Viola\" \"Va.\" { clef treble }";
    private const string NoNames = "part a { clef treble }\npart b { clef treble }";

    private static ScoreLayout Laid(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine(score.Paper).Layout(score);
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>What the SVG of <paramref name="book"/> draws, on the paper (staff spaces from
    /// its left edge): the frames' top rules (left, right, y), in the order of their fences, and the
    /// staff lines' ends (left, right, y) — a line's ink, its round end included.</summary>
    private static ((double Left, double Right, double Y)[] Frames, (double Left, double Right, double Y)[] Staff) Drawn(string book)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        // (A frame's two long rules are drawn top first; a fence's place in the file is its data-pos.)
        var rules = Regex.Matches(svg, "<rect x=\"([\\d.]+)\" y=\"([\\d.]+)\" width=\"([\\d.]+)\" height=\"0\\.10\" data-pos=\"(\\d+)\"/>")
            .Select(m => (Left: Num(m.Groups[1].Value), Right: Num(m.Groups[1].Value) + Num(m.Groups[3].Value), Y: Num(m.Groups[2].Value), At: int.Parse(m.Groups[4].Value)))
            .Where(r => r.Right - r.Left > 5).ToArray();
        // (The music is drawn in a group moved right by the left margin.)
        var staff = Regex.Matches(svg, "<line x1=\"([\\d.]+)\" y1=\"([\\d.]+)\" x2=\"([\\d.]+)\" y2=\"\\2\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
            .Select(m => (Left: Margin + Num(m.Groups[1].Value) - 0.05, Right: Margin + Num(m.Groups[3].Value) + 0.05, Y: Num(m.Groups[2].Value)))
            .ToArray();
        return ([.. rules.Where((_, i) => i % 2 == 0).OrderBy(r => r.At).Select(r => (r.Left, r.Right, r.Y))], staff);
    }

    /// <summary>
    /// The owner's decision of 2026-11-14: with long instrument names (the first system
    /// indented past the paper's 15 mm) and the default indent, a text that writes no
    /// <c>xMargin</c> — boxed or plain, under the title or after the last system — begins at
    /// the page's left margin and ends at its right one, and a centred line is centred between
    /// them, under the title's middle; <c>xMargin 15mm</c> puts it 15 mm in from each margin.
    /// (From 2026-11-13 to 2026-11-14 such a text began where the staff lines of the system
    /// beside it began: it sat off the page's middle and off the poet's and composer's lines.)
    /// </summary>
    [Fact]
    public void WithLongNames_ATextWithNoXMargin_RunsFromMarginToMargin_AndXMarginPutsItIn()
    {
        string Book(string x) => Named(LongNames, "staff a staff b", "paper { shortIndent 12mm }")
            + $"\n{F}md box{x}\nA boxed note.\n{F}\n{F}md{x}\nA plain one. {Long}\n{F}\n{F}md center{x}\nfor my choir\n{F}\n"
            + $"{F}md end box{x}\nA closing note.\n{F}\n{F}md end{x}\nAfter it. {Long}\n{F}\n";

        foreach (var (x, inset) in new[] { ("", 0.0), (" xMargin 15mm", 15 * 72.27 / 127.0) })
        {
            string book = Book(x);
            var layout = Laid(book);
            Assert.True(layout.Systems.Length > 1);
            // The first staff is indented for the long name, the last by the short indent: the
            // text does not follow either.
            Assert.True(layout.Systems[0].Indent > LayoutOptions.Default.Indent + 1);
            double left = inset, right = LineWidth - inset;

            var top = layout.Pages[0].Header!.Text!.Blocks.Select(b => b.Block).ToArray();
            var end = layout.Pages[^1].EndText!.Blocks.Select(b => b.Block).ToArray();
            Assert.Equal((3, 2), (top.Length, end.Length));
            foreach (var boxed in new[] { top[0], end[0] })
            {
                // (A length in millimetres is the paper's, to six places.)
                Assert.Equal(left, boxed.Frame!.Left, 5);
                Assert.Equal(right, boxed.Frame.Right, 5);
                Assert.Equal(right - left - 2.2, boxed.TextWidth, 5);
            }
            foreach (var plain in new[] { top[1], end[1] })
            {
                Assert.Null(plain.Frame);
                Assert.Equal(left, plain.Left, 5);
                Assert.Equal(right - left, plain.TextWidth, 5);
                Assert.True(plain.Lines.Length > 1);
                Assert.All(plain.Lines, l => Assert.Equal(left, l.Left, 5));
                // Broken at the right margin (less the inset): the full lines within a word of it.
                Assert.All(plain.Lines, l => Assert.True(l.Left + l.Width <= right + 1e-5));
                Assert.True(plain.Lines[0].Left + plain.Lines[0].Width > right - 8);
            }
            var centred = Assert.Single(top[2].Lines);
            Assert.Equal(LineWidth / 2, centred.Left + centred.Width / 2, 5);

            // Drawn, on the paper: the frames' rules and the plain texts' first letters.
            var (frames, _) = Drawn(book);
            Assert.Equal(2, frames.Length);
            Assert.All(frames, f => { Near(Margin + left, f.Left); Near(Margin + right, f.Right); });
            string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
            Near(Margin + left, TextLeft(svg, TextOf(top[1].Lines[0])));
            Near(Margin + left, TextLeft(svg, TextOf(end[1].Lines[0])));
            Near(Margin + left + 1.1, TextLeft(svg, "A boxed note."));
        }
    }

    /// <summary>A written <c>xMargin</c> is measured from the page's margins: <c>xMargin 0</c>
    /// is the text — a boxed text's frame — from margin to margin, as writing none is, and
    /// two lengths are the left white and the right one.</summary>
    [Fact]
    public void AWrittenXMargin_IsFromThePagesMargins_BoxedOrPlain()
    {
        string book = Named(LongNames, "staff a staff b")
            + $"\n{F}md box xMargin 0\nA note.\n{F}\n{F}md end box xMargin 10 20\nA closing note.\n{F}\n";
        var (frames, _) = Drawn(book);
        Near(Margin, frames[0].Left);
        Near(Margin + LineWidth, frames[0].Right);
        Near(Margin + 10, frames[^1].Left);
        Near(Margin + LineWidth - 20, frames[^1].Right);

        string plainBook = Named(LongNames, "staff a staff b")
            + $"\n{F}md xMargin 0\nA note. {Long}\n{F}\n{F}md end center xMargin 0\nThe end.\n{F}\n{F}md end xMargin 10 20\nAfter it.\n{F}\n";
        var plain = Laid(plainBook);
        var top = plain.Pages[0].Header!.Text!;
        Assert.Equal(0.0, top.Lines[0].Left);
        Assert.Equal(LineWidth, top.TextWidth, 9);
        Assert.True(top.Lines.Length > 1 && top.Lines[0].Width > LineWidth - 8, "broken at the line width");
        var end = plain.Pages[^1].EndText!.Blocks.ToArray();
        Assert.Equal(LineWidth, end[0].Block.TextWidth, 9);
        Assert.Equal(LineWidth / 2, end[0].Block.Lines[0].Left + end[0].Block.Lines[0].Width / 2, 6);
        Assert.Equal(10.0, end[1].Block.Lines[0].Left, 9);
        Assert.Equal(LineWidth - 30, end[1].Block.TextWidth, 9);
        // Drawn: at the page's left margin, left of the indented first staff.
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(plainBook));
        Near(Margin, TextLeft(svg, TextOf(top.Lines[0])));
        Near(Margin + 10, TextLeft(svg, "After it."));
    }

    private static readonly string Long = string.Join(' ', Enumerable.Repeat("measure", 40));

    private static string TextOf(ProseLine line) => string.Concat(line.Pieces.Select(p => p.Text));

    /// <summary>Where the SVG begins the text piece <paramref name="words"/>, on the paper.</summary>
    private static double TextLeft(string svg, string words)
    {
        var m = Regex.Match(svg, "<text x=\"([\\d.]+)\" y=\"[\\d.]+\"[^>]*>" + Regex.Escape(words) + "</text>");
        Assert.True(m.Success, words);
        return Num(m.Groups[1].Value);
    }

    /// <summary>
    /// What stays is the FRAME: under the title it stands the margin and the padding under
    /// the rows, as it did, and under the last system 8.9 under the staff's middle line —
    /// score-markup-spacing's 12 less the 3.1 it reaches over its reference point — as it
    /// did; the text in it is 0.314 lower (12.314 from the middle line where it was 12).
    /// MEASURED, LilyPond 2.26.0 (boxfit/names2 twin): the closing frame's top 114.3041 under
    /// a last staff whose middle line is at 105.4041 — 8.9000 — and its baseline 12.314 under it.
    /// </summary>
    [Fact]
    public void TheFrameStaysWhereItStood_AndTheTextMovesDownInIt()
    {
        string head = "title \"Morning Song\"\ncomposer \"A. Composer\"\n";
        var plain = Laid(head + $"{F}md\nOne line.\n{F}\n" + OneStaff).Pages[0];
        var boxed = Laid(head + $"{F}md box\nOne line.\n{F}\n" + OneStaff).Pages[0];
        var band = boxed.Header!.Text!;
        double frameTop = boxed.HeaderTextY - band.Up + band.MarginTop;
        Assert.Equal(boxed.HeaderTop + boxed.Header.Depth + 0.5 + ProseBand.DefaultMargin, frameTop, 6);
        Assert.Equal(plain.HeaderTextY - 2.0, frameTop, 6);         // where the plain line's box began
        Assert.Equal(frameTop + 0.1 + 3.314, boxed.HeaderTextY + band.Lines[0].Baseline, 6);
        // The music under it: as far under the frame as it was (the frame is as tall).
        Assert.Equal(plain.Height - plain.Systems[0].Y + 2.2, boxed.Height - boxed.Systems[0].Y, 6);

        var end = Laid(head + $"{F}md end box\nA closing note.\n{F}\n" + OneStaff).Pages[0];
        double middleLine = end.Height - end.Systems[^1].Y + 2.0;
        Assert.Equal(12.0, end.EndTextY - middleLine, 6);
        Assert.Equal(8.9, end.EndTextY - end.EndText!.Up + end.EndText.MarginTop - middleLine, 6);
        Assert.Equal(12.314, end.EndTextY + end.EndText.Lines[0].Baseline - middleLine, 6);
    }

    // ---- the twin ------------------------------------------------------------------------

    /// <summary>The twin rules the same frame as the page: with names and no <c>xMargin</c>
    /// the white before the box is the inset alone, under the title and after the last of
    /// several systems, the column as wide as the line less the inset.</summary>
    [Fact]
    public void TheTwin_RulesTheFrameFromTheMargins_AsThePageDoes()
    {
        string book = Named("part a \"Violin\" \"Vn.\" { clef treble }\npart b \"Viola\" \"Va.\" { clef treble }", "staff a staff b")
            + $"\n{F}md box\nA note under the title.\n{F}\n{F}md end box\nA closing note.\n{F}\n";
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        Assert.True(Laid(book).Systems.Length > 1);
        // The inset 1.1; the text 102.4299 − 2.2 wide.
        Assert.Contains("\\concat { \\hspace #1.1 \\translate #'(0 . -0.314) \\override #'(box-padding . 1) \\box\n"
            + "    \\with-dimension #X #'(0 . 100.2299) \\with-dimension #Y #'(-0.686 . 2.314) \\column {\n      \"A note under the title.\"\n", ly);
        Assert.Contains("\\concat { \\hspace #1.1 \\translate #'(0 . -0.314) \\override #'(box-padding . 1) \\box\n"
            + "    \\with-dimension #X #'(0 . 100.2299) \\with-dimension #Y #'(-0.686 . 2.314) \\column {\n      \"A closing note.\"\n", ly);
    }
}
