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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>subtitle</c> and <c>poet</c>: LilyPond's <c>\header</c> fields of the same names, drawn
/// where bookTitleMarkup draws them — the subtitle centred on its own row under the title,
/// the poet at the left end of the composer's row.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/titling-init.ly bookTitleMarkup (lines 68-97)
/// </remarks>
[Trait("Category", "Unit")]
public sealed class HeaderSubtitlePoetTests
{
    private const string Book = """
        title "Ttl"
        subtitle "Sbt"
        composer "Cmp"
        poet "Pht"
        part m { clef treble }
        section A { m { c'4 d e f | } }
        form { ~A }
        score { staff m }
        """;

    private static string Svg(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    /// <summary>The attributes of the one <c>&lt;text&gt;</c> that draws <paramref name="text"/>.</summary>
    private static string Attrs(string svg, string text)
    {
        var matches = Regex.Matches(svg, "<text([^>]*)>" + Regex.Escape(text) + "</text>");
        Assert.Single(matches);
        return matches[0].Groups[1].Value;
    }

    private static double Num(string attrs, string name) =>
        double.Parse(Regex.Match(attrs, name + "=\"([-0-9.]+)\"").Groups[1].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void TheFourHeaderWords_ParseAtTheTopLevelAndInAScore()
    {
        var tree = SyntaxTree.Parse(Book + """

            score "part" { subtitle "Part" poet "Other" staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void TheSubtitleIsCentredUnderTheTitle_AndThePoetOpensTheComposersRow()
    {
        string svg = Svg(Book);
        var title = Attrs(svg, "Ttl");
        var subtitle = Attrs(svg, "Sbt");
        var composer = Attrs(svg, "Cmp");
        var poet = Attrs(svg, "Pht");

        // Centred like the title, one baseline-skip below it (the skip binds on these
        // strings), bold at \large.
        Assert.Equal(Num(title, "x"), Num(subtitle, "x"), 6);
        Assert.Contains("text-anchor=\"middle\"", subtitle);
        Assert.Contains("font-weight=\"bold\"", subtitle);
        Assert.Equal(HeaderBand.SubtitleFontSize, Num(subtitle, "font-size"), 2);
        Assert.Equal(Num(title, "y") + HeaderBand.BaselineSkip, Num(subtitle, "y"), 6);

        // The poet shares the composer's baseline and size, at the other end of the row.
        Assert.Equal(Num(composer, "y"), Num(poet, "y"), 6);
        Assert.Equal(Num(subtitle, "y") + HeaderBand.BaselineSkip, Num(poet, "y"), 6);
        Assert.Equal(Num(composer, "font-size"), Num(poet, "font-size"), 6);
        Assert.DoesNotContain("text-anchor", poet);  // start — left-aligned
        Assert.True(Num(poet, "x") < Num(title, "x") && Num(title, "x") < Num(composer, "x"));

        // Each carries its own source offset for click-to-jump.
        Assert.Contains("data-pos", subtitle);
        Assert.Contains("data-pos", poet);
    }

    [Fact]
    public void HeaderBand_StacksTheSubtitleRow_AndGivesThePoetTheComposersRow()
    {
        var fonts = ScoreTextMetrics.Bundled;
        var all = HeaderBand.Build("T", "C", fonts, subtitle: "S", poet: "P")!;
        var plain = HeaderBand.Build("T", "C", fonts)!;
        Assert.NotNull(all.SubtitleBaseline);
        Assert.True(all.TitleBaseline < all.SubtitleBaseline && all.SubtitleBaseline < all.ComposerBaseline);
        // One row more than title + composer; a poet alone opens the row too.
        Assert.True(all.Depth > plain.Depth);
        Assert.NotNull(HeaderBand.Build(null, null, fonts, poet: "P")!.ComposerBaseline);
        Assert.NotNull(HeaderBand.Build(null, null, fonts, subtitle: "S")!.SubtitleBaseline);
        // The poet and the composer share their row's width, fill-line's word-space apart.
        double row = fonts.Advance("P", HeaderBand.ComposerFontSize, TextRole.Poet, FontStyle.Regular)
                     + fonts.Advance("C", HeaderBand.ComposerFontSize, TextRole.Composer, FontStyle.Regular)
                     + HeaderBand.FillLineWordSpace;
        Assert.True(all.Width >= row - 1e-9);
    }

    /// <summary>
    /// A row made larger than the baseline-skip leaves room for keeps
    /// <see cref="HeaderBand.RowPadding"/> of white under the row above.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (user decision 2026-09-26): LilyPond stacks the rows with zero padding, so
    /// <c>subtitle step +5</c> put the subtitle's cap height ON the title's baseline — in
    /// LilyPond 2.26.0 too. The default sizes stay LilyPond's (the skip binds).
    /// </remarks>
    [Fact]
    public void ASteppedUpSubtitle_KeepsClearOfTheTitle()
    {
        string svg = Svg("fonts { subtitle step +5 }\n" + Book);
        var title = Attrs(svg, "Ttl");
        var subtitle = Attrs(svg, "Sbt");
        var fonts = ScoreTextMetrics.Bundled;
        var titleInk = fonts.Ink("Ttl", Num(title, "font-size"), TextRole.Title, FontStyle.Bold);
        var subtitleInk = fonts.Ink("Sbt", Num(subtitle, "font-size"), TextRole.Subtitle, FontStyle.Bold);

        double gap = (Num(subtitle, "y") - subtitleInk.Top) - (Num(title, "y") - titleInk.Bottom);
        // The ink binds here, and keeps its padding. ⚠️ A literal, not HeaderBand.RowPadding:
        // the constant is compiled into this assembly, so reading it would move the expected
        // value with the code under test and a zeroed padding would pass.
        Assert.Equal(0.5, gap, 2);

        // …while the unstepped header is unmoved: the baseline-skip still binds.
        string plain = Svg(Book);
        Assert.Equal(Num(Attrs(plain, "Ttl"), "y") + HeaderBand.BaselineSkip,
            Num(Attrs(plain, "Sbt"), "y"), 6);
    }

    /// <summary>
    /// A row the bundled face cannot spell (CJK, drawn from a fallback face the layout never
    /// measures) reserves the ideographic em box for its CJK glyphs, not only the ink of the
    /// glyphs the face has — "ホ短調 in E minor" used to be measured as "in E minor" and its
    /// kanji ran into the title (2026-09-26). The box was the face's own ascender box until
    /// 2026-10-07 (TextFontMetrics.InkOrFallbackBox).
    /// </summary>
    [Fact]
    public void ASubtitleTheFaceCannotSpell_KeepsClearOfTheTitle()
    {
        string svg = Svg("fonts { subtitle step +5 }\n"
                         + Book.Replace("subtitle \"Sbt\"", "subtitle \"ホ短調 Sbt\""));
        var title = Attrs(svg, "Ttl");
        var subtitle = Attrs(svg, "ホ短調 Sbt");
        var fonts = ScoreTextMetrics.Bundled;
        var titleInk = fonts.Ink("Ttl", Num(title, "font-size"), TextRole.Title, FontStyle.Bold);
        // ⚠️ A literal, not TextFontMetrics.IdeographicEmBoxTop — see the padding above.
        double subtitleTop = Num(subtitle, "y") - 0.88 * Num(subtitle, "font-size");

        // 0.02 of slack: the SVG writes y and font-size to two decimals.
        Assert.True(subtitleTop - (Num(title, "y") - titleInk.Bottom) >= 0.5 - 0.02,
            $"the subtitle's box top {subtitleTop} is within 0.5 of the title's ink bottom");
    }

    [Fact]
    public void AScoresOwnSubtitle_ReplacesTheFilesForThatScore()
    {
        string svg = Svg(Book.Replace("score { staff m }",
            "score { subtitle \"Own\" staff m }"));
        Assert.Contains(">Own</text>", svg);
        Assert.DoesNotContain(">Sbt</text>", svg);
    }

    [Fact]
    public void TheTwinWritesBothHeaderFields()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        Assert.Contains("subtitle = \"Sbt\"", ly);
        Assert.Contains("poet = \"Pht\"", ly);
    }

    [Fact]
    public void MusicXmlRoundTrips_TheSubtitleAndThePoet()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book)).ToXml().ToString();
        Assert.Contains("<creator type=\"poet\">Pht</creator>", xml);
        Assert.Contains("<credit-type>subtitle</credit-type>", xml);

        var report = new LilySharp.Core.MusicXmlImport.ImportReport();
        string lys = LilySharp.Core.MusicXmlImport.LysWriter.Write(
            LilySharp.Core.MusicXmlImport.MusicXmlReader.Read(xml, report), report);
        Assert.Contains("subtitle \"Sbt\"", lys);
        Assert.Contains("poet \"Pht\"", lys);
    }
}
