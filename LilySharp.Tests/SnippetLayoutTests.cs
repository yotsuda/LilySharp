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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The snippet layout a Markdown lily# fence draws with (HANDOFF §2F F-mdfence ⑵,
/// <see cref="LayoutOptions.Snippet"/>): LilyPond's ly:one-page-breaking — one page as
/// tall as the music, no automatic page break, <c>pageBreak</c> a line break — plus the
/// page cropped to the widest system. The defaults are the positive control: the same
/// music on <see cref="LayoutOptions.Default"/> pages as it always did.
/// </summary>
public sealed class SnippetLayoutTests
{
    private const string TwoBars = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody { c4 d e f | g1 | } }
        form main { Main }
        score main { staff melody }
        """;

    private const string TwoSections = """
        time 4/4
        key c major
        part m { clef treble
          section A { c'4 d' e' f' | g'4 a' b' c'' | }
          section B { c'4 d' e' f' | g'1 | }
        }
        form main { A {{JOIN}} B }
        score main { staff m }
        """;

    private static string LongScore(int bars) =>
        "time 4/4\nkey c major\npart m { clef treble\n  section A { "
        + string.Concat(Enumerable.Repeat("c'4 d' e' f' | ", bars))
        + "} }\nform main { A }\nscore main { staff m }";

    private static (MultiStaffScore Score, ScoreLayout Layout) LayoutOf(string source, LayoutOptions paperBase)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec, paperBase);
        return (score, new LayoutEngine(score.Paper).Layout(score));
    }

    [Fact]
    public void OneSystem_IsCroppedToItsMusic_PlusTheMargins()
    {
        var (score, snippet) = LayoutOf(TwoBars, LayoutOptions.Snippet);
        var (_, page) = LayoutOf(TwoBars, LayoutOptions.Default);

        Assert.Single(snippet.Pages);
        Assert.Single(snippet.AllSystems);
        Assert.Equal(LayoutOptions.Default.PageWidth, page.Width);
        Assert.True(snippet.Width < page.Width, $"cropped {snippet.Width} against the paper {page.Width}");
        // The width is the drawn staff's end (the ONE reading the renderer ends the lines
        // at) plus the two margins — the paper never ends short of the ink.
        var (_, notationRight, tabRight) = SharedRenderer.StaffRightEdges(score, snippet.AllSystems[0]);
        double expected = LayoutOptions.Snippet.MarginLeft + Math.Max(notationRight, tabRight)
            + LayoutOptions.Snippet.MarginRight;
        Assert.Equal(expected, snippet.Width, 9);
        // The height is content-driven in both: the snippet changes nothing vertically.
        Assert.Equal(page.Height, snippet.Height, 9);
    }

    /// <summary>
    /// A numbers-only tab under a notation staff ends its string lines where the SYSTEM ends,
    /// though it draws no courtesy meter of its own: every staff's StaffSymbol spans the
    /// system to its last column. ABC.lys "both" bar 36 (Lab corpus) stopped the tab at the
    /// bar line under the bass staff's courtesy `C`; LilyPond 2.26 runs it on.
    /// </summary>
    [Theory]
    [InlineData("time 2/4")]              // the meter alone (ABC.lys)
    [InlineData("key g major time 2/4")]  // a courtesy key in front of it widens the suffix
    public void ANumbersOnlyTab_RunsItsLinesToTheSystemsEnd_UnderAnotherStaffsCourtesyMeter(string change)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse($$"""
            octave absolute
            part bl { instrument bass }
            section A { bl { c4 d e f | break {{change}} g4 a | } }
            form main { A }
            score main { staff bl  tab bl }
            """), new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        // The first system's staff and string lines: the nine highest horizontal lines
        // (five of the bass staff, four of the tab), each keyed by its Y.
        var lines = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([\\d.]+)\" y1=\"([\\d.]+)\" x2=\"([\\d.]+)\" y2=\"\\2\"")
            .Select(m => (Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                          X2: double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .GroupBy(l => l.Y).Select(g => (Y: g.Key, X2: g.Max(l => l.X2)))
            .OrderBy(l => l.Y).Take(9).ToArray();

        Assert.Equal(9, lines.Length);
        double notationEnd = lines[..5].Max(l => l.X2);
        Assert.All(lines[5..], l => Assert.Equal(notationEnd, l.X2, 2));
    }

    [Fact]
    public void ATitleWiderThanTheMusic_WidensThePicture_ToTheTitle()
    {
        string titled = "title \"A Rather Long Title Over Two Short Bars\"\n" + TwoBars;

        var (_, plain) = LayoutOf(TwoBars, LayoutOptions.Snippet);
        var (score, titledLayout) = LayoutOf(titled, LayoutOptions.Snippet);

        var header = titledLayout.Pages[0].Header;
        Assert.NotNull(header);
        var (_, notationRight, tabRight) = SharedRenderer.StaffRightEdges(score, titledLayout.AllSystems[0]);
        Assert.True(header!.Width > Math.Max(notationRight, tabRight), "the fixture's title must be the wider");
        Assert.True(titledLayout.Width > plain.Width);
        Assert.Equal(LayoutOptions.Snippet.MarginLeft + header.Width + LayoutOptions.Snippet.MarginRight,
            titledLayout.Width, 9);
    }

    [Fact]
    public void PageBreak_IsALineBreak_AndThePageStaysOne()
    {
        var (_, page) = LayoutOf(TwoSections.Replace("{{JOIN}}", "pageBreak"), LayoutOptions.Default);
        var (_, snippet) = LayoutOf(TwoSections.Replace("{{JOIN}}", "pageBreak"), LayoutOptions.Snippet);
        var (_, plain) = LayoutOf(TwoSections.Replace("{{JOIN}}", ""), LayoutOptions.Snippet);

        Assert.Equal(2, page.Pages.Length);
        Assert.Single(snippet.Pages);
        Assert.Single(plain.Pages);
        Assert.Single(plain.AllSystems);
        // The line still breaks where the page would have: B opens the second system.
        Assert.Equal(2, snippet.AllSystems.Length);
        Assert.Equal(2, snippet.AllSystems[1].Measures[0].MeasureIndex);
    }

    [Fact]
    public void ALongScore_StaysOnOnePage_AsTallAsItsMusic()
    {
        var (_, page) = LayoutOf(LongScore(80), LayoutOptions.Default);
        var (_, snippet) = LayoutOf(LongScore(80), LayoutOptions.Snippet);

        Assert.True(page.Pages.Length >= 2, $"the control should paginate, got {page.Pages.Length}");
        Assert.Single(snippet.Pages);
        Assert.True(snippet.Height > LayoutOptions.Default.PageHeight,
            $"one page taller than the paper: {snippet.Height}");
        Assert.Equal(page.AllSystems.Length, snippet.AllSystems.Length);
    }

    [Fact]
    public void JustifiedSystems_KeepTheLineWidth()
    {
        // Several systems are justified (only a lone system is ragged, LilyPond's rule), so
        // the widest system IS the line width and the crop changes nothing horizontally.
        var (_, snippet) = LayoutOf(LongScore(80), LayoutOptions.Snippet);

        Assert.True(snippet.AllSystems.Length >= 2);
        Assert.Equal(LayoutOptions.Default.PageWidth, snippet.Width, 6);
    }

    [Fact]
    public void ThePaperBlock_OverlaysTheSnippet()
    {
        string source = "paper { raggedRight }\n" + TwoBars;

        var (onSnippet, _) = LayoutOf(source, LayoutOptions.Snippet);
        var (onDefault, _) = LayoutOf(source, LayoutOptions.Default);

        Assert.True(onSnippet.Paper.RaggedRight);
        Assert.Equal(0, onSnippet.Paper.PageHeight);
        Assert.True(onSnippet.Paper.CropWidth);
        Assert.True(onDefault.Paper.RaggedRight);
        Assert.Equal(LayoutOptions.Default.PageHeight, onDefault.Paper.PageHeight);
        Assert.False(onDefault.Paper.CropWidth);
    }

    [Fact]
    public void TheSnippet_DiffersFromTheDefaults_InThePageAlone()
    {
        Assert.False(LayoutOptions.Default.CropWidth);
        Assert.True(LayoutOptions.Default.PageHeight > 0);
        Assert.Equal(LayoutOptions.Default with { PageHeight = 0, CropWidth = true }, LayoutOptions.Snippet);
    }
}
