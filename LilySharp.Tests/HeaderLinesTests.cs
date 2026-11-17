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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>arranger</c> and <c>copyright</c> (owner's decision 2026-11-13): LilyPond's
/// <c>\header arranger</c> — right-aligned on the line under the composer — and
/// <c>\header copyright</c> — centred at the foot of the first page, the systems clearing it.
/// </summary>
/// <remarks>
/// The numbers are LilyPond 2.26.0's, measured on the probes in LilySharp-Lab
/// sessions/p1226/md/header (p1–p5 .ly / .svg; a4, a 20 pt staff, staff spaces from the
/// paper's top-left corner):
/// p1 — title 12.2642, subtitle 15.7642, poet / composer 19.2642, arranger 22.7642 ending at
///   110.9657 (94.0648 + 16.9009), "Copyright 2026 Somebody" at 2.2000 with its baseline at
///   162.8656, centred on 59.7508;
/// p2 — no poet, no composer: the arranger 3.5000 under the title (15.7642 − 12.2642);
/// p5 — "COPYRIGHT 2026 SOMEBODY": its baseline at 163.2858;
/// p3 / p4 — twelve systems forced onto page 1: the last staff's bottom line at 158.6992
///   over the copyright and 160.7738 with none, 2.0746 higher — the copyright's INK height.
/// LILYPOND-REF: ly/titling-init.ly bookTitleMarkup (lines 68-97) and oddFooterMarkup
///   (lines 124-135); scm/page.scm:261-270 make-page-stencil.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class HeaderLinesTests
{
    // (LF whatever this file's line endings are: the tests cut lines out of it by "\n" — red on a checkout with autocrlf.)
    private static readonly string P1 = """
        title "Morning Song"
        subtitle "Sub"
        poet "A. Poet"
        composer "A. Composer"
        arranger "Arr. B. Arranger"
        copyright "Copyright 2026 Somebody"
        time 4/4
        octave absolute
        part m { clef treble }
        section A { m { c'4 d' e' f' | g'1 | c'4 d' e' f' | g'1 | break c'4 d' e' f' | g'1 | c'4 d' e' f' | g'1 | } }
        form { ~A }
        score { staff m }
        """.ReplaceLineEndings("\n");

    private static ScoreLayout Laid(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static MultiStaffScore Collected(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    /// <summary>A baseline below the page's top edge, as the draw places it.</summary>
    private static double FromTop(PageLayout page, double? baseline) => page.HeaderTop + baseline!.Value;

    /// <summary>The copyright's baseline below the page's top edge, as the draw places it
    /// (SharedRenderer.DrawFooter).</summary>
    private static double CopyrightBaseline(PageLayout page)
        => page.Height - LayoutOptions.Default.MarginBottom + page.Footer!.InkBottom;

    // ----- geometry, against LilyPond's numbers -----

    [Fact]
    public void TheArranger_StandsUnderTheComposer_RightAligned_AsLilyPondSetsIt()
    {
        var page = Laid(P1).Pages[0];
        var band = page.Header!;
        Assert.NotNull(band.Arranger);

        // The rows above it are LilyPond's (p1.svg) to the title's ink top: Lily#'s face
        // stands the bold title 0.0015 lower than LilyPond's C059 (12.2627 against 12.2642),
        // and every row under it keeps that — the steps between them are exact.
        Assert.Equal(12.2642, FromTop(page, band.TitleBaseline), 2);
        Assert.Equal(3.5, band.SubtitleBaseline!.Value - band.TitleBaseline!.Value, 9);     // 15.7642 − 12.2642
        Assert.Equal(3.5, band.ComposerBaseline!.Value - band.SubtitleBaseline.Value, 9);   // 19.2642 − 15.7642
        // The composer → arranger gap is the column's baseline-skip, as LilyPond's
        // (22.7642 − 19.2642), and the arranger's baseline LilyPond's to that same 0.0015.
        Assert.Equal(3.5, band.ArrangerBaseline!.Value - band.ComposerBaseline.Value, 9);
        Assert.Equal(22.7642, FromTop(page, band.ArrangerBaseline), 2);
        Assert.True(Math.Abs(22.7642 - FromTop(page, band.ArrangerBaseline)) < 0.002);
        // The composer's size and face, ending at the right margin.
        Assert.Equal(2.2, band.Arranger!.Em, 9);
        Assert.Equal(TextRole.Composer, band.Arranger.Role);
        Assert.Equal(HeaderAlign.Right, band.Arranger.Align);
        Assert.Equal(110.9657, page.Width - LayoutOptions.Default.MarginLeft, 3);
    }

    [Fact]
    public void WithNoComposer_TheArrangerIsTheRowUnderTheTitle()
    {
        var page = Laid("""
            title "Morning Song"
            arranger "Arr. B. Arranger"
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            form { ~A }
            score { staff m }
            """).Pages[0];
        var band = page.Header!;
        Assert.Null(band.ComposerBaseline);
        Assert.Equal(3.5, band.ArrangerBaseline!.Value - band.TitleBaseline!.Value, 9);
        Assert.True(Math.Abs(15.7642 - FromTop(page, band.ArrangerBaseline)) < 0.002,
            $"arranger baseline {FromTop(page, band.ArrangerBaseline):F4}, LilyPond 15.7642");
    }

    [Fact]
    public void TheArrangerAlone_IsAHeaderOfItsOwn()
    {
        var band = HeaderBand.Build(null, null, ScoreTextMetrics.Bundled,
            arranger: HeaderLineSet.Arranger(Collected("arranger \"A\"\npart m\nsection S { m { c'1 | } }\nform { S }\nscore { staff m }")));
        Assert.NotNull(band);
        Assert.Equal(band!.ArrangerBaseline!.Value - band.Arranger!.InkBottom, band.Depth, 9);
    }

    [Theory]
    // p1.svg: the descenders' ink on the bottom margin.
    [InlineData("Copyright 2026 Somebody", 162.8656)]
    // p5.svg: capitals — the round letters' overshoot on the margin.
    [InlineData("COPYRIGHT 2026 SOMEBODY", 163.2858)]
    public void TheCopyright_StandsOnTheFirstPagesBottomMargin_Centred(string copyright, double lilyPondBaseline)
    {
        var page = Laid(P1.Replace("Copyright 2026 Somebody", copyright)).Pages[0];
        var footer = page.Footer!;
        Assert.Equal(copyright, footer.Text);
        Assert.Equal(2.2, footer.Em, 9);
        Assert.Equal(HeaderAlign.Center, footer.Align);
        // The paper LilyPond measured on: a4, its bottom margin's edge at 163.3188.
        Assert.Equal(169.0094, page.Height, 3);
        Assert.Equal(163.3188, page.Height - LayoutOptions.Default.MarginBottom, 3);
        // Its lowest ink on that edge, to the ink of the face.
        Assert.True(Math.Abs(lilyPondBaseline - CopyrightBaseline(page)) < 0.002,
            $"copyright baseline {CopyrightBaseline(page):F4}, LilyPond {lilyPondBaseline}");
        // Centred on the page (p1.svg: 46.5032 + 26.4951 / 2).
        Assert.Equal(59.7508, page.Width / 2, 3);
    }

    /// <summary>Twelve systems forced onto page 1 (p3 / p4): the page is justified to its foot,
    /// and the last system stands higher over a copyright by the copyright's ink, as LilyPond's
    /// footer_height_ makes it.</summary>
    [Fact]
    public void TheSystemsOfTheFirstPage_ClearTheCopyright()
    {
        string bars = string.Concat(Enumerable.Repeat("c'4 d' e' f' | g'1 | c'4 d' e' f' | g'1 | break ", 12));
        string Book(string copyright) => $$"""
            title "Morning Song"
            composer "A. Composer"
            {{copyright}}
            octave absolute
            part m { clef treble }
            section A { m { {{bars}} pageBreak c'4 d' e' f' | g'1 | c'4 d' e' f' | g'1 | } }
            form { ~A }
            score { staff m }
            """;
        var with = Laid(Book("copyright \"Copyright 2026 Somebody\""));
        var without = Laid(Book(""));
        Assert.Equal(2, with.Pages.Length);
        Assert.Equal(12, with.Pages[0].Systems.Length);
        Assert.Equal(12, without.Pages[0].Systems.Length);
        Assert.NotNull(with.Pages[0].Footer);
        Assert.Null(with.Pages[1].Footer);      // the first page only

        double raised = with.Pages[0].Systems[^1].Y - without.Pages[0].Systems[^1].Y;
        Assert.Equal(with.Pages[0].Footer!.Height, raised, 6);
        // LilyPond: 160.7738 − 158.6992.
        Assert.True(Math.Abs(raised - 2.0746) < 0.002, $"raised {raised:F4}, LilyPond 2.0746");
        // …and where the last staff's bottom line stands, from the paper's top (a system's Y
        // is its top line's, Y-up from the paper's bottom).
        double BottomLine(ScoreLayout layout)
            => layout.Pages[0].Height - layout.Pages[0].Systems[^1].Y + LayoutOptions.Default.StaffHeight;
        Assert.True(Math.Abs(BottomLine(without) - 160.7738) < 0.002, $"without: {BottomLine(without):F4}, LilyPond 160.7738");
        Assert.True(Math.Abs(BottomLine(with) - 158.6992) < 0.002, $"with: {BottomLine(with):F4}, LilyPond 158.6992");
        // p5: in capitals the ink is shorter, and the system lower.
        var capitals = Laid(Book("copyright \"COPYRIGHT 2026 SOMEBODY\""));
        Assert.True(Math.Abs(BottomLine(capitals) - 159.1194) < 0.002, $"capitals: {BottomLine(capitals):F4}, LilyPond 159.1194");
        // The second page is the same either way.
        Assert.Equal(without.Pages[1].Systems.Select(s => s.Y), with.Pages[1].Systems.Select(s => s.Y));
    }

    [Fact]
    public void ADrawnPage_PrintsBothLines_TheCopyrightAtItsFoot()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(P1), new SvgRenderOptions { EmbedFont = false });
        Assert.Contains(">Arr. B. Arranger</text>", svg);
        string line = svg.Split('\n').Single(l => l.Contains(">Copyright 2026 Somebody</text>"));
        Assert.Contains("text-anchor=\"middle\"", line);
        Assert.Contains("data-pos", line);
    }

    // ----- a file with neither line -----

    [Fact]
    public void AFileWithNeither_CarriesNone_AndLaysOutAsBefore()
    {
        string plain = P1.Replace("arranger \"Arr. B. Arranger\"\n", "").Replace("copyright \"Copyright 2026 Somebody\"\n", "");
        var score = Collected(plain);
        Assert.Same(HeaderLines.None, score.HeaderLines);
        var page = Laid(plain).Pages[0];
        Assert.Null(page.Footer);
        Assert.Null(page.Header!.Arranger);
        Assert.Null(page.Header.ArrangerBaseline);
        // The band is the one HeaderBand.Build always made for these four strings.
        Assert.Equal(HeaderBand.Build("Morning Song", "A. Composer", score.TextMetrics, "Sub", "A. Poet")!.Depth,
            page.Header.Depth, 12);
    }

    // ----- a score's own -----

    [Fact]
    public void AScoreRestatesBoth_ForItselfAlone()
    {
        string book = P1.Replace("score { staff m }",
            "score { staff m }\nscore extract { arranger \"Own arr.\" copyright \"Own (c)\" staff m }");
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var all = RenderSpecParser.FindAll(tree);
        var extract = SvgGenerator.CollectScore(tree, all.Single(s => s.Name == "extract"));
        var main = SvgGenerator.CollectScore(tree, all.Single(s => s.Name == ""));
        Assert.Equal("Own arr.", extract.HeaderLines.Arranger);
        Assert.Equal("Own (c)", extract.HeaderLines.Copyright);
        Assert.Equal("Arr. B. Arranger", main.HeaderLines.Arranger);
        Assert.Equal("Copyright 2026 Somebody", main.HeaderLines.Copyright);
    }

    // ----- exports -----

    [Fact]
    public void TheTwin_WritesBothHeaderFields()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(P1));
        Assert.Contains("arranger = \"Arr. B. Arranger\"", ly);
        Assert.Contains("copyright = \"Copyright 2026 Somebody\"", ly);
    }

    [Fact]
    public void MusicXml_WritesTheArrangerAndTheRights_AndReadsThemBack()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(P1)).ToXml().ToString();
        Assert.Contains("<creator type=\"arranger\">Arr. B. Arranger</creator>", xml);
        Assert.Contains("<rights>Copyright 2026 Somebody</rights>", xml);
        // The schema's order: the creators, then the rights.
        Assert.True(xml.IndexOf("<creator type=\"poet\">", StringComparison.Ordinal)
                    < xml.IndexOf("<creator type=\"arranger\">", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<creator type=\"arranger\">", StringComparison.Ordinal)
                    < xml.IndexOf("<rights>", StringComparison.Ordinal));

        var report = new LilySharp.Core.MusicXmlImport.ImportReport();
        string lys = LilySharp.Core.MusicXmlImport.LysWriter.Write(
            LilySharp.Core.MusicXmlImport.MusicXmlReader.Read(xml, report), report);
        Assert.Contains("arranger \"Arr. B. Arranger\"", lys);
        Assert.Contains("copyright \"Copyright 2026 Somebody\"", lys);
        Assert.False(SyntaxTree.Parse(lys).HasErrors);
    }

    [Fact]
    public void AScoresOwnRights_AreItsMusicXml()
    {
        var tree = SyntaxTree.Parse(P1.Replace("score { staff m }", "score { copyright \"Own\" staff m }"));
        string xml = new MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.Contains("<rights>Own</rights>", xml);
        Assert.DoesNotContain("Copyright 2026 Somebody", xml);
    }

    private static byte[] MidOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        using var bytes = new MemoryStream();
        MidiForFile.Shape(midi, tree.GetRoot(), spec).WriteTo(bytes);
        return bytes.ToArray();
    }

    [Fact]
    public void TheMid_OpensItsFirstTrackWithTheCopyrightNotice()
    {
        byte[] mid = MidOf(P1);
        // The first MTrk: its first event, at delta 0, is FF 02 <len> "Copyright 2026 Somebody".
        int track = IndexOf(mid, "MTrk"u8.ToArray());
        byte[] text = Encoding.UTF8.GetBytes("Copyright 2026 Somebody");
        byte[] expected = [0x00, 0xFF, 0x02, (byte)text.Length, .. text];
        Assert.Equal(expected, mid.Skip(track + 8).Take(expected.Length).ToArray());
        // Once, on that track alone.
        Assert.Equal(1, Count(mid, [0xFF, 0x02]));

        // …and a file with no copyright writes none.
        Assert.Equal(0, Count(MidOf(P1.Replace("copyright \"Copyright 2026 Somebody\"\n", "")), [0xFF, 0x02, (byte)text.Length]));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }

    private static int Count(byte[] haystack, byte[] needle)
    {
        int n = 0;
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                n++;
        return n;
    }

    // ----- names -----

    [Theory]
    [InlineData("part arranger { clef treble }", "arranger")]
    [InlineData("part copyright { clef treble }", "copyright")]
    [InlineData("part m\nsection copyright { m { c'1 | } }", "copyright")]
    public void ANameCannotBeAHeaderWord_AndSaysSo_AsComposerDoes(string source, string word)
    {
        var said = SyntaxTree.Parse(source).Diagnostics.Select(d => d.Message).ToArray();
        Assert.Contains(said, m => m.StartsWith($"Expected a name, found '{word}', a reserved word", StringComparison.Ordinal));
        var composer = SyntaxTree.Parse("part composer { clef treble }").Diagnostics.Select(d => d.Message).First();
        Assert.Contains(said, m => m == composer.Replace("'composer'", $"'{word}'"));
    }

    // ----- the editor -----

    private static int _documents;

    private static string[] Labels(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri($"file:///headerlines{System.Threading.Interlocked.Increment(ref _documents)}.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return [.. (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
            Context = new LilySharp.Lsp.Protocol.CompletionContext { TriggerKind = 1 },
        })?.Items ?? []).Select(i => i.Label!)];
    }

    [Theory]
    [InlineData("title \"T\"\n‸\npart m { clef treble }\nsection A { m { c'1 | } }\nform { A }\nscore { staff m }\n")]
    [InlineData("part m { clef treble }\nsection A { m { c'1 | } }\nform { A }\nscore {\n  staff m\n  ‸\n}\n")]
    public void Completion_OffersBothWhereItOffersTheComposer(string marked)
    {
        var labels = Labels(marked);
        Assert.Contains("composer", labels);
        Assert.Contains("arranger", labels);
        Assert.Contains("copyright", labels);
    }

    [Fact]
    public void Completion_DropsATopLevelArranger_OnceWritten()
    {
        var labels = Labels("arranger \"A\"\n‸\npart m { clef treble }\n");
        Assert.DoesNotContain("arranger", labels);
        Assert.Contains("copyright", labels);
    }

    [Theory]
    [InlineData("arranger ", "Quoted arranger name")]
    [InlineData("copyright ", "Quoted copyright line")]
    public void AfterTheWord_TheQuotedTextIsOffered(string text, string detail)
    {
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterTitleText,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
        Assert.Equal(detail, LilySharpLanguageServer.GetTitleTextCompletions(text.Trim()).Items.Single().Detail);
    }

    [Theory]
    [InlineData("arranger", "**Arranger**")]
    [InlineData("copyright", "**Copyright**")]
    public void Hover_SaysWhereTheLineIsPrinted(string word, string head)
    {
        var tree = SyntaxTree.Parse($"{word} \"X\"\npart m\nsection A {{ m {{ c'1 | }} }}\nform {{ A }}\nscore {{ {word} \"Y\" staff m }}");
        var lines = tree.GetRoot().DescendantNodes().OfType<MetadataDeclarationSyntax>().ToArray();
        Assert.Equal(2, lines.Length);
        string top = LanguageReference.Hover(lines[0].KeywordToken)!;
        string own = LanguageReference.Hover(lines[1].KeywordToken)!;
        Assert.StartsWith(head, top);
        Assert.DoesNotContain("this score's own", top);
        Assert.StartsWith(head, own);
        Assert.Contains("this score's own", own);
    }
}
