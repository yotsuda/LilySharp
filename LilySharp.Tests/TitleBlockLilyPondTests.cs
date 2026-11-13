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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// The title block — every header row, the page text under it and the first system — placed
/// as LilyPond places it (owner, 2026-11-14: "LilyPond と合わせられる部分は合わせて"), and the
/// lines of ONE header line written as Markdown a little closer than the next row (owner,
/// 2026-11-14: "title 内の改行幅 y と、title と subtitle の間の幅 y は、少しだけ差をつけたい").
/// </summary>
/// <remarks>
/// MEASURED 2026-11-14, LilyPond 2.26.0 engraving each book's <c>lysc ly --pin-fonts</c> twin
/// (LilySharp-Lab sessions/p1226/md/header2: b1…b7, md-showcase; -dbackend=svg; a4, a 20 pt
/// staff, staff spaces from the paper's top, baselines). LILYPOND-REF: ly/titling-init.ly
/// bookTitleMarkup — a \column of \fill-line rows at baseline-skip 3.5, the subtitle
/// <c>\large \bold</c> (font-size 1: 2.4697); scm/stencil.scm stack-lines.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TitleBlockLilyPondTests(ITestOutputHelper output)
{
    private const string Music = """
        time 4/4
        octave absolute
        part m { clef treble }
        section A { m { c'4 d' e' f' | g'1 | c'4 d' e' f' | g'1 | } }
        form { ~A }
        score { staff m }
        """;

    private const string F = "```";

    /// <summary>The books and LilyPond's numbers: each text's baseline, and the first staff's top line.</summary>
    public static TheoryData<string, string, string> Books => new()
    {
        { "title only", "title \"Morning Song\"\n", "Morning Song=12.2642;staff=15.2456" },
        { "title and subtitle", "title \"Morning Song\"\nsubtitle \"A study in thirds\"\n",
            "Morning Song=12.2642;A study in thirds=15.7642;staff=18.5490" },
        { "poet and composer", "poet \"A. Poet\"\ncomposer \"A. Composer\"\n",
            "A. Poet=11.3120;A. Composer=11.3120;staff=14.0324" },
        { "composer and arranger", "composer \"A. Composer\"\narranger \"Arr. B. Arranger\"\n",
            "A. Composer=11.3120;Arr. B. Arranger=14.8120;staff=17.5346" },
        { "a page text under the title", $"title \"Morning Song\"\n\n{F}md\nA few words under the title.\n{F}\n\n",
            "Morning Song=12.2642;A few words under the title.=16.4696;staff=20.7456" },
        { "with copyright", "title \"Morning Song\"\ncomposer \"A. Composer\"\ncopyright \"Copyright 2026 Somebody\"\n",
            "Morning Song=12.2642;A. Composer=15.7642;Copyright 2026 Somebody=162.8656;staff=18.4847" },
        { "all six", "title \"Morning Song\"\nsubtitle \"A study in thirds\"\npoet \"A. Poet\"\ncomposer \"A. Composer\"\n"
            + "arranger \"Arr. B. Arranger\"\ncopyright \"Copyright 2026 Somebody\"\n",
            "Morning Song=12.2642;A study in thirds=15.7642;A. Poet=19.2642;A. Composer=19.2642;"
            + "Arr. B. Arranger=22.7642;Copyright 2026 Somebody=162.8656;staff=25.4869" },
    };

    private static double StaffTop(RenderedGeometry g)
        => g.Lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && !l.IsDashed && Math.Abs(l.X2 - l.X1) > 20)
            .Min(l => l.Y1);

    private void AssertAgainstLilyPond(string source, string expected)
    {
        var g = RenderedGeometry.RenderProduct(source);
        foreach (var pair in expected.Split(';'))
        {
            var (what, value) = (pair[..pair.LastIndexOf('=')], double.Parse(pair[(pair.LastIndexOf('=') + 1)..],
                System.Globalization.CultureInfo.InvariantCulture));
            double lily = what == "staff" ? StaffTop(g) : g.Texts.First(t => t.Text == what).Y;
            output.WriteLine($"{what}: Lily# {lily:F4}, LilyPond {value:F4}, {lily - value:+0.0000;-0.0000;0.0000}");
            Assert.True(Math.Abs(lily - value) <= 0.01, $"{what}: Lily# {lily:F4}, LilyPond {value:F4}");
        }
    }

    /// <summary>A single-line header: every row, the page text and the first staff where
    /// LilyPond puts them, within 0.01.</summary>
    [Theory]
    [MemberData(nameof(Books))]
    public void ASingleLineHeader_StandsWhereLilyPondsDoes(string name, string header, string expected)
    {
        Assert.NotNull(name);
        AssertAgainstLilyPond(header + Music, expected);
    }

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
        1902-1971
        ```

        arranger "Arr. B. Arranger"

        ```md copyright
        Copyright 2026 **Somebody**
        ```

        """;

    /// <summary>
    /// The showcase against LilyPond's engraving of ITS twin — which writes the fences' step
    /// and the reach of their columns (HeaderLineSet.NextRowTop), so LilyPond sets the title's
    /// two lines, the composer's and every row under them where the page does.
    /// </summary>
    [Fact]
    public void TheShowcase_AgainstLilyPondsEngravingOfItsTwin()
        => AssertAgainstLilyPond(Showcase + Music,
            "Morning Song=12.2642;for two flutes=16.3725;A study in thirds=19.8727;A. Poet=23.3727;"
            + "A. Composer=23.3727;1902-1971=26.5477;Arr. B. Arranger=30.0478;Somebody=162.8656;staff=32.7704");

    /// <summary>The white between two lines measured at the faces' designed ascender and
    /// descender (TextFontMetrics.TypoExtents), the upper line's size and face to the lower's.</summary>
    private static double White(double step, HeaderLineSet upper, double lowerAscender)
    {
        var (_, descender) = TextFontMetrics.TypoExtents(ScoreTextMetrics.Bundled.Face(upper.Role, upper.Style));
        return step + descender * upper.Em - lowerAscender;
    }

    /// <summary>
    /// The white between a header line's OWN lines is <see cref="HeaderLineSet.InnerWhiteShare"/>
    /// of the white between its last line and the next row — for a title over a subtitle, a
    /// composer over an arranger, and a copyright at the foot (no row under it: the white the
    /// 3.5 skip would leave between two of its own lines).
    /// </summary>
    [Theory]
    [InlineData("title")]
    [InlineData("composer")]
    [InlineData("copyright")]
    public void AHeaderLinesOwnLines_StandALittleCloserThanTheNextRow(string place)
    {
        string source = $"{F}md {place}\nOne line\\\nand another\n{F}\n"
            + (place == "title" ? "subtitle \"S\"\n" : place == "composer" ? "arranger \"A\"\n" : "")
            + Music;
        var tree = SyntaxTree.Parse(source);
        var layout = new LayoutEngine().Layout(Core.Svg.SvgGenerator.CollectScore(tree, Core.Svg.Collector.RenderSpecParser.FindFirst(tree)));
        var page = layout.Pages[0];
        var band = page.Header;
        var set = place switch { "title" => band!.TitleText!, "composer" => band!.ComposerText!, _ => page.Footer! };
        double inner = White(set.Step!.Value, set, set.Ascender(ScoreTextMetrics.Bundled));
        double outer = place switch
        {
            "title" => White(band!.SubtitleBaseline!.Value - band.TitleBaseline!.Value - set.LastBaseline, set,
                HeaderLineSet.AscenderOf(ScoreTextMetrics.Bundled, HeaderBand.SubtitleEm(ScoreTextMetrics.Bundled),
                    TextRole.Subtitle, FontStyle.Bold)),
            "composer" => White(band!.ArrangerBaseline!.Value - band.ComposerBaseline!.Value - set.LastBaseline, set,
                band.Arranger!.Ascender(ScoreTextMetrics.Bundled)),
            _ => White(HeaderBand.BaselineSkip, set, set.Ascender(ScoreTextMetrics.Bundled)),
        };
        Assert.True(inner > 0 && inner < outer, $"inner {inner:F4}, outer {outer:F4}");
        // A literal, not InnerWhiteShare: the constant is compiled into this assembly.
        Assert.Equal(0.75, inner / outer, 9);
        // The next row stands LilyPond's 3.5 under the fence's LAST line (the skip binds).
        if (place != "copyright")
            Assert.Equal(HeaderBand.BaselineSkip, place == "title"
                ? band!.SubtitleBaseline!.Value - band.TitleBaseline!.Value - set.LastBaseline
                : band!.ArrangerBaseline!.Value - band.ComposerBaseline!.Value - set.LastBaseline, 9);
    }

    /// <summary>A single-line header line is untouched: one line, no step, no reach.</summary>
    [Fact]
    public void ASingleLineFence_HasNoStep()
    {
        var tree = SyntaxTree.Parse($"{F}md title\nOne *line*\n{F}\nsubtitle \"S\"\n" + Music);
        var band = new LayoutEngine().Layout(Core.Svg.SvgGenerator.CollectScore(tree, Core.Svg.Collector.RenderSpecParser.FindFirst(tree))).Pages[0].Header!;
        Assert.Null(band.TitleText!.Step);
        Assert.Null(band.TitleText.NextRowTop);
        Assert.Equal(HeaderBand.BaselineSkip, band.SubtitleBaseline!.Value - band.TitleBaseline!.Value, 9);
    }

    /// <summary>The twin writes the page's step and the column's reach, so LilyPond's column
    /// steps by it and stacks the next row where the page does (checked on the showcase above).</summary>
    [Fact]
    public void TheTwin_WritesTheStepAndTheReach()
    {
        var tree = SyntaxTree.Parse(Showcase + Music);
        string ly = new LilyPondExporter().Export(tree);
        var band = new LayoutEngine().Layout(Core.Svg.SvgGenerator.CollectScore(tree, Core.Svg.Collector.RenderSpecParser.FindFirst(tree))).Pages[0].Header!;
        string N(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("title = \\markup { \\pad-to-box #'(0 . 0) #'(" + N(-band.TitleText!.NextRowTop!.Value)
            + " . 0) { \\override #'(baseline-skip . " + N(band.TitleText.Step!.Value) + ") \\center-column {", ly);
        Assert.Contains("composer = \\markup { \\pad-to-box #'(0 . 0) #'(" + N(-band.ComposerText!.NextRowTop!.Value)
            + " . 0) { \\override #'(baseline-skip . " + N(band.ComposerText.Step!.Value) + ") \\right-column {", ly);
    }
}
