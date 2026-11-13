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

    // ==== second batch: the engraving ========================================================

    private static int[] MidiPitches(string source) => [.. new LilySharp.Core.Midi.MidiExporter()
        .Export(SyntaxTree.Parse(source)).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => n.Pitch)];

    private static string Twin(string source) => new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source));

    // ---- B1. `<< … >>` under `octave absolute` ----------------------------------------------

    /// <summary><c>&lt;&lt; fis' a' d'' &gt;&gt;</c> under <c>octave absolute</c> drew fis' a''
    /// d'''': the members after the first were STACKED on it (the relative rule) and their own
    /// marks counted from there, where a <c>&lt;…&gt;</c> chord's members are where they are
    /// written. Nothing stacks in absolute mode now, on the page, in the sound, in MusicXML and
    /// in the twin; the marks after <c>&gt;&gt;</c> move every member; relative is as it was.</summary>
    [Fact]
    public void AnArpeggioGroupUnderOctaveAbsolute_IsAbsolute_AsAChordIs()
    {
        string Book(string music, string mode = "octave absolute\n") => mode
            + $"part m {{ clef treble }}\nsection A {{ m {{ {music} | }} }}\nform {{ ~A }}\nscore {{ staff m }}\n";
        int[] chord = [66, 69, 74];   // F#4 A4 D5
        Assert.Equal(chord, MidiPitches(Book("<fis' a' d''>2.")));
        Assert.Equal(chord, MidiPitches(Book("<< fis' a' d'' >>2.")));
        // Written downwards, and a member below the first: no stacking either way.
        Assert.Equal(new[] { 74, 69, 66 }, new LilySharp.Core.Midi.MidiExporter().Export(SyntaxTree.Parse(Book("<< d'' a' fis' >>2.")))
            .Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).Select(n => n.Pitch));
        // The marks after '>>' move the whole group, a chord member inside included.
        Assert.Equal(new[] { 54, 57, 62 }, MidiPitches(Book("<< fis' a' d'' >>,2.")));
        Assert.Equal(new[] { 66, 69, 74, 78 }, MidiPitches(Book("<< fis <a d'> fis' >>'2.")));
        // The page: the three heads of the group are the three of the chord before it.
        var heads = LpFidelity.RenderedGeometry.Render(Book("<fis' a' d''>4 << fis' a' d'' >>2.")).Noteheads.Select(h => Math.Round(h.Y, 6)).ToArray();
        Assert.Equal(6, heads.Length);
        Assert.Equal(heads[..3].Order(), heads[3..].Order());
        // MusicXML and the twin (three in a dotted half are plain quarters).
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book("<< fis' a' d'' >>2."))).ToXml().ToString();
        Assert.Equal(new[] { "4", "4", "5" }, Regex.Matches(xml, "<octave>(\\d)</octave>").Select(m => m.Groups[1].Value));
        Assert.Contains("fis'4 a'4 d''4", Twin(Book("<< fis' a' d'' >>2.")));
        // Relative mode still stacks on the root: c' then e and g above it.
        Assert.Equal(new[] { 60, 64, 67 }, MidiPitches(Book("<< c' e g >>2.", mode: "")));
    }

    // ---- B2. a condensed staff whose first part has a `voice { } { }` somewhere --------------

    /// <summary><c>condensedStaff { sop alt }</c>: one divisi bar in the soprano
    /// (<c>voice { e''1 } { c''1 }</c>, a section away) made the alto the staff's THIRD voice
    /// for the whole piece — stems up with the soprano's, the unison beams doubled, its rests
    /// above the staff (choral/r-05-c against r-05-a). The voice props go by PART on a shared
    /// staff (LilyPond's <c>&lt;&lt; \sop \\ \alt &gt;&gt;</c>), and a part's own span is
    /// numbered from one again while it lasts.</summary>
    [Fact]
    public void ACondensedStaffsLowerPart_KeepsItsStemsDown_WhenTheUpperPartHasADivisi()
    {
        string Book(string sopB, string altB = "c'1 |") => "octave absolute\npart sop { clef treble }\npart alt { clef treble }\n"
            + "section A { sop { a'4 d''8 cis'' b'4 a' | d''4 d''8 e'' fis''4 e'' | } alt { a'4 d''8 cis'' b'4 a' | fis'4 fis'8 g' a'4 r4 | } }\n"
            + $"section B {{ sop {{ {sopB} }} alt {{ {altB} }} }}\nform {{ ~A ~B }}\nscore {{ condensedStaff {{ sop alt }} }}\n";
        static LilySharp.Core.Svg.Model.Voice[] Voices(string source)
        {
            var tree = SyntaxTree.Parse(source);
            return [.. SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).StaffGroups.Single().Staves.Single().Voices];
        }
        static (bool? Stem, int RestSide)[] Sides(LilySharp.Core.Svg.Model.Voice voice, int bar)
            => [.. voice.Measures[bar].Items.Select(i => i switch
            {
                LilySharp.Core.Svg.Model.NoteItem n => (n.StemUpOverride, 0),
                LilySharp.Core.Svg.Model.RestItem r => ((bool?)null, r.VoiceDirection),
                _ => ((bool?)null, 0),
            })];

        var plain = Voices(Book("e''1 |"));
        var divisi = Voices(Book("voice { e''1 | } { c''1 | }"));
        Assert.Equal(2, plain.Length);
        Assert.Equal(3, divisi.Length);
        // Bars 1 and 2 are the plain book's, part for part: soprano up, alto down, its rest below.
        for (int bar = 0; bar < 2; bar++)
        {
            Assert.Equal(Sides(plain[0], bar), Sides(divisi[0], bar));
            Assert.Equal(Sides(plain[1], bar), Sides(divisi[2], bar));
            Assert.All(Sides(divisi[2], bar), s => Assert.True(s.Stem == false || s.RestSide == -1));
        }
        // …and the numbers the readers ask: the alto is voice two throughout; the soprano's
        // second voice is voice two of its own span, the soprano voice one either way.
        Assert.Equal(new[] { 1, 2, 2 }, Enumerable.Range(0, 3).Select(v => LilySharp.Core.Svg.Model.VoiceDefaults.DirectionNumber([.. divisi], v, 2)));
        Assert.Equal(2, LilySharp.Core.Svg.Model.VoiceDefaults.DirectionNumber([.. divisi], 2, 0));
        // The lower part's own divisi: up and down inside it, the part's side outside.
        var lower = Voices(Book("e''1 |", altB: "voice { e'1 | } { c'1 | }"));
        Assert.Equal(new[] { 1, 1, 2 }, Enumerable.Range(0, 3).Select(v => LilySharp.Core.Svg.Model.VoiceDefaults.DirectionNumber([.. lower], v, 2)));
        Assert.Equal(new[] { 1, 2 }, Enumerable.Range(0, 2).Select(v => LilySharp.Core.Svg.Model.VoiceDefaults.DirectionNumber([.. lower], v, 0)));
        // The page of the first two bars is the plain book's, glyph for glyph.
        static (double X, double Y, string Glyph)[] Glyphs(string source) => [.. Regex.Matches(SvgGenerator.Generate(SyntaxTree.Parse(source)),
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>([^<]+)<")
            .Select(m => (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[3].Value))
            .OrderBy(g => g.Item1).ThenBy(g => g.Item2).ThenBy(g => g.Value, StringComparer.Ordinal)];
        var (a, c) = (Glyphs(Book("e''1 |")), Glyphs(Book("voice { e''1 | } { c''1 | }")));
        Assert.True(a.Length > 20);
        Assert.Equal(a[..^2], c[..(a.Length - 2)]);
    }

    // ---- B3. a group with no staff on the system has no name there --------------------------

    /// <summary>A <c>grandStaff</c> whose two staves are both hidden on a system
    /// (<c>removeEmpty all</c>) still printed its name — at a brace of no height, on top of
    /// the neighbouring staff's: "Marimba" over "Flute" (chamber/r-09). Where a staff of the
    /// group is shown, the name is too.</summary>
    [Fact]
    public void AFullyHiddenGrandStaff_PrintsNoName()
    {
        string Book(string marimba) => "octave absolute\npart fl \"Flute\" \"Fl.\" { clef treble }\npart mR \"Marimba\" \"Mar.\" { clef treble }\npart mL { clef bass }\n"
            + $"section A {{ fl {{ c''1 | d''1 | e''1 | f''1 break | g''1 | a''1 | }} mR {{ {marimba} }} mL {{ R*6 | }} }}\nform {{ ~A }}\n"
            + "score { staff fl  grandStaff { staff mR as removeEmpty all  staff mL as removeEmpty all } }\n";
        string hidden = SvgGenerator.Generate(SyntaxTree.Parse(Book("R*6 |")));
        Assert.Equal(1, Count(hidden, ">Flute<"));
        Assert.Equal(1, Count(hidden, ">Fl.<"));
        Assert.Equal(0, Count(hidden, ">Marimba<"));
        Assert.Equal(0, Count(hidden, ">Mar.<"));
        // Playing on the second system only: its short name there, no long one on the first.
        string second = SvgGenerator.Generate(SyntaxTree.Parse(Book("R*4 | c''1 | R1 |")));
        Assert.Equal(0, Count(second, ">Marimba<"));
        Assert.Equal(1, Count(second, ">Mar.<"));
    }

    // ---- B4. another part's mark splits a compressed rest -----------------------------------

    /// <summary>On a part sheet <c>R*13</c> was one "13" with the rehearsal mark another part
    /// wrote at its fifth bar printed at its END (chamber/r-16). A rehearsal mark, a tempo and
    /// a double bar reached from another part each cut the rest, as LilyPond's do — MEASURED
    /// 2.26.0 (Lab sessions/p1226/md/eval4/fixB/lp/r16b.ly): 4, A 4, Fast 3, || 2.</summary>
    [Fact]
    public void AnotherPartsMarkTempoAndDoubleBar_SplitACompressedRest()
    {
        string Book(string b) => "octave absolute\nlayout { compressRests true }\npart a { clef treble }\npart b { clef treble }\n"
            + $"section A {{ a {{ R*13 | c'1 | }} b {{ {b} }} }}\nform {{ ~A }}\nscore {{ staff a }}\n";
        static (int, int)[] Rests(string source) => [.. Laid(source).MultiMeasureRestLayouts
            .Select(m => (m.StartMeasureIndex, m.MeasureCount)).OrderBy(t => t.StartMeasureIndex)];
        Assert.Equal(new[] { (0, 13) }, Rests(Book(string.Join(" | ", Enumerable.Repeat("c'1", 14)) + " |")));
        var cut = Book("c'1 | c'1 | c'1 | c'1 | c'1@mark(\"D\") | c'1 | c'1 | c'1 | tempo \"Fast\" 4 = 144 c'1 | c'1 | c'1 || c'1 | c'1 | c'1 |");
        Assert.Equal(new[] { (0, 4), (4, 4), (8, 3), (11, 2) }, Rests(cut));
        // The mark stands at the bar it was written at: the fifth bar's left bar line.
        var layout = Laid(cut);
        var mark = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == LilySharp.Core.Svg.Model.MusicMarkType.Rehearsal);
        var bar5 = layout.Pages[0].Systems[0].Measures.Single(m => m.MeasureIndex == 4);
        Assert.InRange(mark.X, bar5.X - 2.0, bar5.X + 2.0);
        // The spacing and the drawing read the same runs: four bars' boxes hold the rests.
        Assert.Equal(new[] { 0, 4, 8, 11, 13 }, layout.Pages[0].Systems[0].Measures.Where(m => m.Width > 1.0).Select(m => m.MeasureIndex));
    }

    // ---- B5. pedals: the continued bracket, the mixed style's words --------------------------

    /// <summary>A pedal bracket continued after a system break was drawn under the WHOLE
    /// system — on the cello's hairpin — where its first portion stands under its own staff
    /// (chamber/r-11): the continued portion asked for its solved line by a measure the solve
    /// did not key it with. Both portions stand between the left hand's staff and the staff
    /// under it, as LilyPond's do (Lab sessions/p1226/md/eval4/fixB/lp/r11.png).</summary>
    [Fact]
    public void APedalBracketContinuedAfterABreak_StaysUnderItsOwnStaff()
    {
        string source = "octave absolute\npart rh { clef treble }\npart lh { clef bass }\npart vn { clef treble }\npart vc { clef bass }\n"
            + "section A {\n rh { c''1 | d''1 | e''1 break | R | f''1 | }\n lh { c,1 | <c, g,>1@sostenuto | R | r2. r4@!sostenuto | c,1 | }\n"
            + " vn { c''1 | c''1 | c''1 | c''1 | c''1 | }\n vc { c1 | c1 | c1 | c1@p@cresc | c1@f | }\n}\nform { ~A }\n"
            + "score { grandStaff { staff rh staff lh } staffGroup { staff vn staff vc } }\n";
        var layout = Laid(source);
        Assert.Equal(2, layout.Pages[0].Systems.Length);
        Assert.Equal(2, layout.PedalBracketLayouts.Length);
        var continued = Assert.Single(layout.PedalBracketLayouts, b => b.BrokenLeft);
        Assert.Equal(3, continued.StartMeasureIndex);
        var staves = layout.Pages[0].Systems[1].StaffGroups.SelectMany(g => g.Staves).ToArray();
        // Device offsets down from the system's top: the left hand's bottom line, the violin's top.
        double leftHandBottom = staves[1].Height - staves[1].Y, violinTop = -staves[2].Y;
        Assert.InRange(continued.Y, leftHandBottom + 0.5, violinTop - 0.5);
        // …and clear of the cello's hairpin, which is under the whole system.
        double celloBottom = staves[3].Height - staves[3].Y;
        Assert.True(continued.Y < celloBottom - 4.0);
    }

    /// <summary><c>pedal mixed</c>: the line of a <c>@sostenuto</c> and of an
    /// <c>@unaCorda</c> began 2.9 from the note — the sustain glyphs' clearance — inside
    /// "Sost. Ped." and "una corda", which are twice as wide (chamber/r-14). Each line begins
    /// its word's half width and the bound's padding (1.0) from the column, LilyPond's picture.
    /// (In the bracket style all three pedals are the same plain bracket in LilyPond too.)</summary>
    [Fact]
    public void AMixedPedalsLine_BeginsAfterItsOwnWord()
    {
        string source = "part c { clef bass pedal mixed }\nsection A { c { c4@sustain d e f@!sustain | c4@sostenuto d e f@!sostenuto | c4@unaCorda d e f@treCorde | } }\n"
            + "form { ~A }\nscore { staff c }\n";
        var layout = Laid(source);
        var lines = layout.PedalBracketLayouts.OrderBy(b => b.StartX).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.All(lines, b => Assert.True(b.IsMixed));
        Assert.Equal(2.9, lines[0].MixedLineStart, 9);
        var fonts = LilySharp.Core.Rendering.ScoreTextMetrics.Bundled;
        foreach (var (line, word, on) in new[] { (lines[1], "Sost. Ped.", LilySharp.Core.Svg.Model.MusicMarkType.SostenutoOn),
                     (lines[2], "una corda", LilySharp.Core.Svg.Model.MusicMarkType.UnaCordaOn) })
        {
            var mark = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == on);
            Assert.Equal(word, mark.Text);
            double width = fonts.Advance(word, MusicMarkEngraver.PlainMarkEm(fonts, on), MusicMarkEngraver.TextRoleOf(on), MusicMarkEngraver.TextStyleOf(fonts, on));
            Assert.True(width / 2 > 2.9, $"{word} is {width:F2} wide");
            Assert.Equal(width / 2 + 1.0, line.MixedLineStart, 9);
            // The word is centred on the column the line is measured from.
            Assert.Equal(line.StartX, mark.X, 1);
            Assert.True(line.StartX + line.MixedLineStart < line.EndX);
        }
    }

    // ---- B6. a repeat played more than twice says so ----------------------------------------

    /// <summary><c>|: A :|*4</c> was engraved as an ordinary repeat: nothing on the page said
    /// four, and MusicXML's backward repeat had no <c>times</c> (rock/r-06). "4×" stands over
    /// the closing repeat bar, its right edge on the bar line, once a score, placed with the
    /// other marks above the staff (LILYSHARP-OWN: LilyPond prints nothing for
    /// <c>\repeat volta 4</c>). A repeat played twice says nothing, as before.</summary>
    [Fact]
    public void ARepeatPlayedMoreThanTwice_PrintsItsCountOverTheClosingBar()
    {
        string Book(string form) => "octave absolute\npart g { clef treble }\npart b { clef bass }\n"
            + "section A { g { c'4 d' e' f' | g'1@fermata | } b { c1 | c1 | } }\nsection B { g { g'1 | } b { c1 | } }\n"
            + $"form {{ {form} }}\nscore {{ staff g staff b }}\n";
        var layout = Laid(Book("|: A :|*4 B"));
        var count = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == LilySharp.Core.Svg.Model.MusicMarkType.RepeatCount);
        Assert.Equal("4×", count.Text);
        var closing = layout.Pages[0].Systems[0].Measures.Single(m => m.MeasureIndex == 1);
        Assert.InRange(count.X, closing.X + closing.Width - 1.5, closing.X + closing.Width + 0.01);
        // Above the staff, and clear of the next section's label: no shared ink.
        var label = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == LilySharp.Core.Svg.Model.MusicMarkType.SectionLabel && m.Text == "B");
        Assert.True(label.YUp - count.YUp > 1.5 || label.X - 1.0 > count.X, $"label at ({label.X:F2}, {label.YUp:F2}), count ending at ({count.X:F2}, {count.YUp:F2})");
        Assert.True(count.YUp > 2.0, $"{count.YUp}");
        Assert.Equal(1, Count(SvgGenerator.Generate(SyntaxTree.Parse(Book("|: A :|*4 B"))), ">4×<"));

        Assert.DoesNotContain(Laid(Book("|: A :| B")).MusicMarkLayouts, m => m.MarkType == LilySharp.Core.Svg.Model.MusicMarkType.RepeatCount);
        Assert.DoesNotContain(Laid(Book("|: A :|*2 B")).MusicMarkLayouts, m => m.MarkType == LilySharp.Core.Svg.Model.MusicMarkType.RepeatCount);

        string Xml(string form) => new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book(form))).ToXml().ToString();
        Assert.Equal(2, Count(Xml("|: A :|*4 B"), "<repeat direction=\"backward\" times=\"4\" />"));
        Assert.Equal(0, Count(Xml("|: A :| B"), "times="));
        Assert.Contains("\\repeat volta 4", Twin(Book("|: A :|*4 B")));
    }

    // ---- B8. the tab: a repeated chord's strings, q@dead, a tie into a chord ----------------

    private static LilySharp.Core.Svg.Model.ChordItem[] TabChords(string music)
    {
        var tree = SyntaxTree.Parse("octave absolute\npart g { instrument guitar  tuning guitarDropD }\n"
            + $"section A {{ g {{ {music} }} }}\nform {{ ~A }}\nscore {{ staff g  tab g }}\n");
        Assert.False(tree.HasErrors);
        var staves = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).StaffGroups.SelectMany(g => g.Staves).ToArray();
        Assert.True(staves[1].IsTab);
        return [.. staves[1].Voices[0].Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.ChordItem>()];
    }

    /// <summary><c>&lt;bes,\6 f\5 bes\4&gt;4 q</c> was 8-8-8 and then x-1-3-3 on the tab: a
    /// <c>q</c> and a bare duration copied the chord's notes without the strings they are
    /// written on, so the tab fretted the copy afresh (rock/r-05). The strings are part of
    /// what is repeated (LILYSHARP-OWN: LilyPond wants <c>\tabChordRepeats</c> for it).</summary>
    [Theory]
    [InlineData("<bes,\\6 f\\5 bes\\4>4 q q q |")]
    [InlineData("<bes,\\6 f\\5 bes\\4>4 4 4 4 |")]
    [InlineData("<bes, f bes>4\\6\\5\\4 q 4 q |")]
    public void ARepeatedChord_KeepsTheStringsItIsWrittenOn(string bar)
    {
        var chords = TabChords(bar);
        Assert.Equal(4, chords.Length);
        Assert.All(chords, c => Assert.Equal(new int?[] { 6, 5, 4 }, c.Notes.Select(n => n.StringNumber)));
        // …so every chord is the same three frets: 8 on each string of drop D.
        int[] tuning = [38, 45, 50, 55, 59, 64];   // d, a, d g b e'
        Assert.All(chords, c => Assert.Equal(new[] { 8, 8, 8 }, LilySharp.Core.Tablature.Tunings
            .CalculateChordFrets([.. c.Notes.Select(n => (n.Midi, n.StringNumber))], tuning).Select(p => p.fret)));
    }

    /// <summary><c>q@dead</c> drew an ordinary chord (and <c>4@dead</c> after a chord): the
    /// repetition's own <c>@dead</c> mutes the copy, as a chord's own mutes its members. And a
    /// tie into a chord printed the held numbers again: only note-to-note ties were followed
    /// on a tab (rock/r-03).</summary>
    [Fact]
    public void ARepeatedChordsOwnDead_AndATieIntoAChord_ReachTheTab()
    {
        var dead = TabChords("<d, a, d g>4 q@dead 4@dead q |");
        Assert.Equal(new[] { false, true, true, false }, dead.Select(c => c.Notes.All(n => n.IsDead)));
        Assert.All(dead[1].Notes, n => Assert.Equal(LilySharp.Core.Svg.Model.NoteheadStyle.Cross, n.Notehead));

        var tied = TabChords("<f,\\6 c\\5 f\\4>8~ q4. <f,\\6 c\\5 f\\4>8~ <f,\\6 c\\5 f\\4>4. |");
        Assert.Equal(new[] { false, true, false, true }, tied.Select(c => c.Notes.All(n => n.IsTieTarget)));
        Assert.All(tied, c => Assert.Equal(new int?[] { 6, 5, 4 }, c.Notes.Select(n => n.StringNumber)));
        // One member tied: that one alone is held.
        var one = TabChords("<f, c f~>2 <f, c f>2 |");
        Assert.Equal(new[] { false, false, true }, one[1].Notes.Select(n => n.IsTieTarget));
        // Drawn on a numbers tab (a tab beside its staff): the held chord's three 3s are not
        // printed again — six digits, not twelve. (A full tab keeps them under its tie bows:
        // TabChordTieTests.)
        string svg = SvgGenerator.Generate(SyntaxTree.Parse("octave absolute\npart g { instrument guitar  tuning guitarDropD }\n"
            + "section A { g { <f,\\6 c\\5 f\\4>8~ q4. <f,\\6 c\\5 f\\4>8~ <f,\\6 c\\5 f\\4>4. | } }\nform { ~A }\nscore { staff g  tab g }\n"));
        Assert.Equal(6, Regex.Matches(svg, ">3<").Count);
    }

    // ---- B9. the tab's fret choice ----------------------------------------------------------

    private static int?[] TabStrings(string music, string part = "instrument guitar  tuning \"c, g, c f g c'\"", string score = "staff a  tab a capo 2")
    {
        var tree = SyntaxTree.Parse($"octave absolute\npart a {{ {part} }}\nsection A {{ a {{ {music} }} }}\nform {{ ~A }}\nscore {{ {score} }}\n");
        Assert.False(tree.HasErrors);
        var tab = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).StaffGroups.SelectMany(g => g.Staves).Single(s => s.IsTab);
        return [.. tab.Voices[0].Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.NoteItem>().Select(n => n.StringNumber)];
    }

    /// <summary>The same bar of open strings was 0 0 0 0 0 0 0 0 before a written bar and
    /// 0 0 0 0 0 5 2 5 before a <c>%</c> (rock/r-04; the <c>@mp</c> the writer suspected was a
    /// bystander): the <c>%</c> bar is a spacer in the model, and the fingering planner read
    /// it as a rest — the hand free, the bar before planned with nothing after it. The planner
    /// is told the bar again.</summary>
    [Fact]
    public void ABarBeforeAPercentRepeat_IsFrettedAsBeforeTheSameBarWrittenOut()
    {
        const string bar = "d,8 a, d g a d' a g |", next = "f,8 a, d g a d' a g |";
        var written = TabStrings($"{bar} {bar} {next}");
        var percent = TabStrings($"{bar} % | {next}");
        Assert.Equal(new int?[] { 6, 5, 4, 3, 2, 1, 2, 3 }, written[..8]);
        Assert.Equal(written[..8], percent[..8]);
        Assert.Equal(written[16..], percent[8..]);
        Assert.Equal(written[..8], TabStrings($"d,8@letRing@mp a, d g a d' a g@!letRing | % | {next}")[..8]);
    }

    /// <summary>A pull-off or a hammer-on is one string's: <c>f'8@pullOff( d')</c> was the
    /// first string's 1 and the second string's 3 (rock/r-06). And a <c>@finger</c> was printed
    /// twice, over the staff and over the tab beside it: the tab's copy carries none there (a
    /// tab alone keeps it).</summary>
    [Fact]
    public void APullOffPairIsOneStrings_AndTheFingeringIsTheStaffs()
    {
        const string guitar = "instrument guitar";
        var pair = TabStrings("e'2 f'8@pullOff( d') d'8@hammerOn( f') | a'2@finger(3) r2 |", guitar, "staff a  tab a");
        Assert.Equal(pair[1], pair[2]);
        Assert.Equal(pair[3], pair[4]);

        static int Fingerings(string score)
        {
            var tree = SyntaxTree.Parse($"octave absolute\npart a {{ instrument guitar }}\nsection A {{ a {{ a'2@finger(3) <c' e'@finger(1)>2 | }} }}\nform {{ ~A }}\nscore {{ {score} }}\n");
            return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).StaffGroups.SelectMany(g => g.Staves).Where(s => s.IsTab)
                .SelectMany(s => s.Voices[0].Measures).SelectMany(m => m.Items)
                .Sum(i => i switch { LilySharp.Core.Svg.Model.NoteItem { Fingering: not null } => 1, LilySharp.Core.Svg.Model.ChordItem c => c.Notes.Count(n => n.Fingering != null), _ => 0 });
        }
        Assert.Equal(0, Fingerings("staff a  tab a"));
        Assert.Equal(2, Fingerings("tab a"));
    }
}
