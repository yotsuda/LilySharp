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
using System.IO;
using System.Linq;
using System.Text;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// The six header lines written as Markdown (owner's decisions 2026-11-13;
/// docs/markdown-text-design.md "Header places"): <c>```md title</c>, <c>subtitle</c>,
/// <c>composer</c>, <c>poet</c>, <c>arranger</c>, <c>copyright</c> — a place word on the
/// fence's opening line. The page draws the fence at the line's own place, size and weight,
/// with the emphasis and line breaks the Markdown writes; the plain string, where one is
/// written too, serves everything that is not the page.
/// </summary>
/// <remarks>
/// The positions against LilyPond (the showcase's twin, and the single-line books) are
/// TitleBlockLilyPondTests' since 2026-11-14 (owner: the title block placed as LilyPond places
/// it; a header line's own lines a little closer than the next row — HeaderLineSet.InnerStep).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MarkdownHeaderTests
{
    private const string F = "```";

    private static string Book(string header, string inScore = "", string bars = "c'4 d' e' f' | g'1 | c'4 d' e' f' | g'1 |") => $$"""
        {{header}}
        time 4/4
        octave absolute
        part m { clef treble }
        section A { m { {{bars}} } }
        form { ~A }
        score { {{inScore}} staff m }
        """;

    private const string Showcase = """
        title "Morning Song"

        ```md title
        Morning Song\
        *for two flutes*
        ```

        subtitle "A study in thirds"
        poet "A. Poet"

        ```md composer
        A. Composer\
        1902–1971
        ```

        arranger "Arr. B. Arranger"

        ```md copyright
        Copyright 2026 **Somebody**
        ```
        """;

    private static ScoreLayout Laid(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static MultiStaffScore Collected(string source, string? score = null)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = score is null ? RenderSpecParser.FindFirst(tree) : RenderSpecParser.FindAll(tree).Single(s => s.Name == score);
        return SvgGenerator.CollectScore(tree, spec);
    }

    private static double FromTop(PageLayout page, double? baseline) => page.HeaderTop + baseline!.Value;

    private static string TextOf(HeaderTextLine line) => string.Concat(line.Pieces.Select(p => p.Text));

    private static Diagnostic[] Said(string source) => [.. SemanticValidation.Run(SyntaxTree.Parse(source))];

    // ----- what each place draws -----

    [Fact]
    public void ATitleFence_IsDrawnInTheTitlesRow_TwoLines_AtItsSizeAndBold_TheItalicWordBoldItalic()
    {
        var page = Laid(Book(Showcase)).Pages[0];
        var title = page.Header!.TitleText!;
        Assert.True(title.IsMarkdown);
        Assert.Equal(HeaderAlign.Center, title.Align);
        Assert.Equal(HeaderBand.TitleEm(ScoreTextMetrics.Bundled), title.Em, 9);
        Assert.Equal(TextRole.Title, title.Role);
        Assert.Equal(["Morning Song", "for two flutes"], title.Lines.Select(TextOf));
        Assert.Equal(FontStyle.Bold, title.Lines[0].Pieces.Single().Style);
        Assert.Equal(FontStyle.Bold | FontStyle.Italic, title.Lines[1].Pieces.Single().Style);
        // The second line a step AT THE TITLE'S SIZE under the first: its ascender and
        // descender and 0.75 of the white the 3.5 skip leaves over the subtitle under it
        // (HeaderLineSet.InnerStep; owner 2026-11-14). Moved from 4.7591 (the page text's 3.0
        // grown to 3.49), which stood the title's own lines further apart than the subtitle
        // stood under them; LilyPond steps by what the twin writes (16.3725 − 12.2642 = 4.1083).
        Assert.Equal(4.1083, title.Lines[1].Baseline, 4);
        // The first line where the plain title stands (LilyPond 12.2642; Lily#'s face 0.0015 lower).
        Assert.True(Math.Abs(12.2642 - FromTop(page, page.Header.TitleBaseline)) < 0.002);
        Assert.True(Math.Abs(16.3725 - FromTop(page, page.Header.TitleBaseline) - title.Lines[1].Baseline) < 0.002);
    }

    [Fact]
    public void TheShowcase_TheRowsUnderAFence_StandTheSkipUnderItsLastLine()
    {
        var page = Laid(Book(Showcase)).Pages[0];
        var band = page.Header!;
        double title = FromTop(page, band.TitleBaseline), subtitle = FromTop(page, band.SubtitleBaseline);
        double composer = FromTop(page, band.ComposerBaseline), arranger = FromTop(page, band.ArrangerBaseline);
        double copyright = page.Height - LayoutOptions.Default.MarginBottom + page.Footer!.InkBottom;
        // MEASURED 2026-11-14 (LilySharp-Lab sessions/p1226/md/header2, md-showcase-lp.svg).
        Assert.True(Math.Abs(12.2642 - title) < 0.002, $"{title:F4}");
        // The subtitle and the arranger stand LilyPond's 3.5 under the LAST line of the fence
        // over them (owner's decision 2026-11-14). They stood ink to ink until then — 3.2569
        // and 2.1522 under it, Lily#'s 0.5 padding binding and the subtitle at 2.77; LilyPond's
        // engraving of the twin (which writes the column's reach) puts them 3.5002 and 3.5001
        // under: 19.8727 − 16.3725, 30.0478 − 26.5477.
        Assert.Equal(HeaderBand.BaselineSkip, subtitle - title - band.TitleText!.LastBaseline, 9);
        Assert.Equal(HeaderBand.BaselineSkip, arranger - composer - band.ComposerText!.LastBaseline, 9);
        Assert.True(Math.Abs(30.0478 - arranger) < 0.01, $"{arranger:F4}");
        // The copyright's baseline: 162.8656, to the digit.
        Assert.True(Math.Abs(162.8656 - copyright) < 0.002, $"{copyright:F4}");
    }

    [Theory]
    [InlineData("subtitle", "Center", TextRole.Subtitle, FontStyle.Bold)]
    [InlineData("poet", "Left", TextRole.Poet, FontStyle.Regular)]
    [InlineData("composer", "Right", TextRole.Composer, FontStyle.Regular)]
    [InlineData("arranger", "Right", TextRole.Composer, FontStyle.Regular)]
    [InlineData("copyright", "Center", TextRole.PageNumber, FontStyle.Regular)]
    public void EachPlace_IsSetAsItsLineIs(string place, string alignName, TextRole role, FontStyle style)
    {
        var page = Laid(Book($"title \"T\"\n{F}md {place}\nOne *two*\\\n**three**\n{F}")).Pages[0];
        var band = page.Header!;
        var set = place switch
        {
            "subtitle" => band.SubtitleText,
            "poet" => band.PoetText,
            "composer" => band.ComposerText,
            "arranger" => band.Arranger,
            _ => page.Footer,
        };
        Assert.NotNull(set);
        Assert.True(set!.IsMarkdown);
        Assert.Equal(Enum.Parse<HeaderAlign>(alignName), set.Align);
        Assert.Equal(role, set.Role);
        Assert.Equal(style, set.Style);
        Assert.Equal(["One two", "three"], set.Lines.Select(TextOf));
        Assert.Equal(style | FontStyle.Italic, set.Lines[0].Pieces[1].Style);
        Assert.Equal(style | FontStyle.Bold, set.Lines[1].Pieces[0].Style);
        // The step at the line's size (HeaderLineSet.InnerStep): against the row under it where
        // one follows (a subtitle under the title, …), else against a row of its own size.
        Assert.True(set.Lines[1].Baseline > set.Em && set.Lines[1].Baseline < 1.6 * set.Em, $"{set.Lines[1].Baseline:F4}");
        // …and the row it stands in has a baseline (the poet's and the composer's share one).
        double? baseline = place switch
        {
            "subtitle" => band.SubtitleBaseline,
            "poet" or "composer" => band.ComposerBaseline,
            "arranger" => band.ArrangerBaseline,
            _ => 0,
        };
        Assert.NotNull(baseline);
    }

    [Fact]
    public void ADrawnPage_SetsEachPieceAtItsX_AndCarriesTheFencesPlace()
    {
        string source = Book(Showcase);
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });
        // The fence's words, not the plain title a second time.
        Assert.Single(svg.Split('\n'), l => l.Contains(">Morning Song</text>"));
        string italic = svg.Split('\n').Single(l => l.Contains(">for two flutes</text>"));
        Assert.Contains("italic", italic);
        Assert.Contains("bold", italic);
        Assert.Contains(svg.Split('\n'), l => l.Contains(">1902–1971</text>"));
        Assert.Contains(svg.Split('\n'), l => l.Contains(">Somebody</text>") && l.Contains("bold"));
        // The click goes to the fence.
        Assert.Contains($"data-pos=\"{source.IndexOf("```md title", StringComparison.Ordinal)}\"", svg);
    }

    // ----- both written, and the fence alone -----

    [Fact]
    public void BothWritten_ThePageDrawsTheFence_MusicXmlTakesTheString()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(Showcase))).ToXml().ToString();
        Assert.Contains("<work-title>Morning Song</work-title>", xml);
        Assert.Contains("<creator type=\"arranger\">Arr. B. Arranger</creator>", xml);
        // The composer and the copyright are written only as Markdown: their words, stripped.
        Assert.Contains("<creator type=\"composer\">A. Composer 1902–1971</creator>", xml);
        Assert.Contains("<rights>Copyright 2026 Somebody</rights>", xml);
    }

    [Fact]
    public void TheFenceAlone_GivesMusicXmlAndMidiItsWords_TheFormattingStripped()
    {
        string source = Book($"{F}md title\nMorning Song\\\n*for two flutes*\n{F}\n{F}md copyright\n(c) **2026**  \nSomebody\n{F}");
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();
        Assert.Contains("<work-title>Morning Song for two flutes</work-title>", xml);
        Assert.Contains("<rights>(c) 2026 Somebody</rights>", xml);

        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        using var bytes = new MemoryStream();
        MidiForFile.Shape(midi, tree.GetRoot(), spec).WriteTo(bytes);
        byte[] text = Encoding.UTF8.GetBytes("(c) 2026 Somebody");
        byte[] mid = bytes.ToArray();
        Assert.True(Enumerable.Range(0, mid.Length - text.Length).Any(i => mid.AsSpan(i, text.Length).SequenceEqual(text)));
        // …and the page's band still draws the fence (no plain title to fall back on).
        Assert.NotNull(Laid(source).Pages[0].Header!.TitleText);
    }

    [Fact]
    public void Plain_StripsEmphasis_AndJoinsLinesWithOneSpace()
    {
        Assert.Equal("Morning Song for two flutes", HeaderProse.PlainOf("Morning *Song*\\\n**for** two  \nflutes\n"));
        Assert.Equal("one two", HeaderProse.PlainOf("one\n\ntwo"));
        Assert.Equal("", HeaderProse.PlainOf(""));
    }

    // ----- the errors and the warning -----

    [Theory]
    [InlineData("md title box", "box")]
    [InlineData("md title center", "center")]
    [InlineData("md composer xMargin 20mm", "xMargin 20mm")]
    [InlineData("md copyright yMargin 2 1", "yMargin 2 1")]
    [InlineData("md title end", "end")]
    [InlineData("md end title", "end")]
    public void AHeaderPlace_TakesNoShapeAndNoEnd_AndSaysWhatItTakes(string opening, string flagged)
    {
        string source = Book($"{F}{opening}\nMorning Song\n{F}");
        var errors = Said(source).Where(d => d.Code == DiagnosticCodes.FencedTextWord).ToArray();
        var error = Assert.Single(errors);
        Assert.Equal(flagged, source.Substring(error.Span.Start, error.Span.Length));
        Assert.Contains("does not go with", error.Message);
        Assert.Contains(" is", error.Message);      // "…set where and as the title is: centred…"
    }

    [Fact]
    public void OnePlaceWordAFence()
    {
        var error = Assert.Single(Said(Book($"{F}md title composer\nX\n{F}")), d => d.Code == DiagnosticCodes.FencedTextWord);
        Assert.Contains("'composer' is a second place for one text", error.Message);
    }

    [Fact]
    public void TwoFencesForOneHeaderPlace_AreAnError_ButAScoreMayWriteItsOwn()
    {
        string two = Book($"{F}md title\nOne\n{F}\n{F}md title\nTwo\n{F}");
        var error = Assert.Single(Said(two), d => d.Code == DiagnosticCodes.FencedTextWord);
        Assert.Contains("A second ```md title", error.Message);
        Assert.Equal(two.LastIndexOf("title", StringComparison.Ordinal), error.Span.Start);

        // The file's and a score's: one each, no error.
        string own = Book($"{F}md title\nOne\n{F}", inScore: $"\n{F}md title\nTwo\n{F}\n");
        Assert.DoesNotContain(Said(own), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AHeadingOrAList_IsWarned_AndDrawnAsPlainLines()
    {
        string source = Book($"{F}md title\n# Morning Song\n- for two flutes\n- and piano\n{F}");
        var warnings = Said(source).Where(d => d.Code == DiagnosticCodes.FencedTextNotDrawn).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, w => Assert.Contains("the title takes inline text only", w.Message));
        Assert.StartsWith("A heading", warnings[0].Message);
        Assert.StartsWith("A list", warnings[1].Message);
        var title = Laid(source).Pages[0].Header!.TitleText!;
        Assert.Equal(["Morning Song", "for two flutes", "and piano"], title.Lines.Select(TextOf));
        // Plain: the title's size and weight, no heading size, no marker.
        Assert.All(title.Lines, l => Assert.Equal(FontStyle.Bold, l.Pieces.Single().Style));
        Assert.Equal(HeaderBand.TitleEm(ScoreTextMetrics.Bundled), title.Em, 9);
    }

    // ----- several lines move what is below -----

    [Fact]
    public void ASecondComposerLine_GrowsTheBand_AndTheMusicMovesDown()
    {
        var one = Laid(Book($"title \"T\"\n{F}md composer\nA. Composer\n{F}")).Pages[0];
        var two = Laid(Book($"title \"T\"\n{F}md composer\nA. Composer\\\n1902–1971\n{F}")).Pages[0];
        Assert.True(two.Header!.Depth > one.Header!.Depth + 2.5, $"{two.Header.Depth} against {one.Header.Depth}");
        // Y is up from the paper's bottom: the first system stands lower.
        Assert.True(two.Systems[0].Y < one.Systems[0].Y - 0.5, $"{two.Systems[0].Y} against {one.Systems[0].Y}");
        // A plain composer and a one-line fence are the same band.
        var plain = Laid(Book("title \"T\"\ncomposer \"A. Composer\"")).Pages[0];
        Assert.Equal(plain.Header!.Depth, one.Header.Depth, 9);
        Assert.Equal(plain.Systems[0].Y, one.Systems[0].Y, 9);
    }

    [Fact]
    public void ATwoLineCopyright_IsATallerFooter_AndPageOnesSystemsClearIt()
    {
        string bars = string.Concat(Enumerable.Repeat("c'4 d' e' f' | g'1 | c'4 d' e' f' | g'1 | break ", 12))
            + "pageBreak c'4 d' e' f' | g'1 |";
        var one = Laid(Book($"title \"T\"\ncopyright \"Copyright 2026 Somebody\"", bars: bars));
        var two = Laid(Book($"title \"T\"\n{F}md copyright\nCopyright 2026 Somebody\\\nAll rights reserved\n{F}", bars: bars));
        var footer = two.Pages[0].Footer!;
        Assert.Equal(2, footer.Lines.Length);
        Assert.True(footer.Height > one.Pages[0].Footer!.Height + 2.9);
        // The last system of page 1 stands higher by what the footer grew.
        double raised = two.Pages[0].Systems[^1].Y - one.Pages[0].Systems[^1].Y;
        Assert.Equal(footer.Height - one.Pages[0].Footer!.Height, raised, 6);
        // The lowest line's ink on the bottom margin: the first baseline that far above it.
        Assert.Null(two.Pages[1].Footer);
    }

    // ----- a score's own -----

    [Fact]
    public void AScoresOwnFence_IsThatScoresAlone_AndReplacesTheFilesLine()
    {
        string book = Book($"title \"Morning Song\"\n{F}md title\nMorning *Song*\n{F}")
            + $"\nscore extract {{\n{F}md title\nViolin *I*\n{F}\nstaff m\n}}\nscore plain {{ title \"Violin II\" staff m }}\n";
        var tree = SyntaxTree.Parse(book);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("Morning *Song*\n", Collected(book).HeaderLines.FenceOf("title")!.Value.Markdown);
        var extract = Collected(book, "extract");
        Assert.Equal("Violin *I*\n", extract.HeaderLines.FenceOf("title")!.Value.Markdown);
        // A score's line, as Markdown, restates the line whole: the file's string goes too.
        Assert.Null(extract.Title);
        // …and as a string: the file's Markdown goes.
        var plain = Collected(book, "plain");
        Assert.Null(plain.HeaderLines.FenceOf("title"));
        Assert.Equal("Violin II", plain.Title);

        // MusicXML of each score: the string where the score has one, else its Markdown's words.
        string Xml(string name) => new MusicXmlExporter { Score = RenderSpecParser.FindAll(tree).Single(s => s.Name == name) }
            .Export(tree).ToXml().ToString();
        Assert.Contains("<work-title>Violin I</work-title>", Xml("extract"));
        Assert.Contains("<work-title>Violin II</work-title>", Xml("plain"));
    }

    // ----- the twin -----

    [Fact]
    public void TheTwin_WritesEachFenceAsMarkupInTheHeader()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book(Showcase)));
        // The step the page steps by (HeaderLineSet.InnerStep; owner 2026-11-14) and the column's
        // reach down to the next row's ink top (HeaderLineSet.NextRowTop), so LilyPond stacks
        // the row under it where the page does (TitleBlockLilyPondTests).
        Assert.Matches(@"title = \\markup \{ \\pad-to-box #'\(0 \. 0\) #'\(-[\d.]+ \. 0\) \{ \\override #'\(baseline-skip \. 4\.1083\) \\center-column \{ ""Morning Song"" \\concat \{ \\italic ""for two flutes"" \} \} \} \}", ly);
        Assert.Matches(@"composer = \\markup \{ \\pad-to-box #'\(0 \. 0\) #'\(-[\d.]+ \. 0\) \{ \\override #'\(baseline-skip \. 3\.175\) \\right-column \{ ""A\. Composer"" ""1902–1971"" \} \} \}", ly);
        Assert.Contains("copyright = \\markup { \\concat { \"Copyright 2026 \" \\bold \"Somebody\" } }", ly);
        // The strings the fences do not replace stay strings.
        Assert.Contains("subtitle = \"A study in thirds\"", ly);
        Assert.Contains("arranger = \"Arr. B. Arranger\"", ly);
        Assert.DoesNotContain("title = \"Morning Song\"", ly);
    }

    // ----- a file with none -----

    [Fact]
    public void AFileWithNoHeaderFence_CarriesNone()
    {
        var score = Collected(Book("title \"T\"\ncomposer \"C\"\n```md\nA text.\n```"));
        Assert.Same(HeaderLines.None, score.HeaderLines);
        var band = Laid(Book("title \"T\"\ncomposer \"C\"")).Pages[0].Header!;
        Assert.Null(band.TitleText);
        Assert.Null(band.ComposerText);
    }

    // ----- completion -----

    private static int _documents;

    private static string[] Labels(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri($"file:///mdheader{System.Threading.Interlocked.Increment(ref _documents)}.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var items = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
            Context = new LilySharp.Lsp.Protocol.CompletionContext { TriggerKind = 1 },
        })?.Items ?? [];
        return [.. items.OrderBy(i => i.SortText ?? i.Label, StringComparer.Ordinal).Select(i => i.Label!)];
    }

    private const string Rest = "\nx\n```\npart m { clef treble }\nsection A { m { c'1 | } }\nform { A }\nscore { staff m }\n";

    [Fact]
    public void Completion_OffersThePlacesFirst_ThenTheShape()
    {
        Assert.Equal(
            ["title", "subtitle", "composer", "poet", "arranger", "copyright", "end", "box", "center", "xMargin", "yMargin"],
            Labels("```md ‸" + Rest));
    }

    [Fact]
    public void Completion_OncePlaced_OffersNoOtherPlace_AndAfterAHeaderPlaceNothing()
    {
        Assert.Equal(["box", "center", "xMargin", "yMargin"], Labels("```md end ‸" + Rest));
        Assert.Empty(Labels("```md title ‸" + Rest));
        // After a shape word no header place (it would take none): `end` still.
        Assert.Equal(["end", "center", "xMargin", "yMargin"], Labels("```md box ‸" + Rest));
    }
}
