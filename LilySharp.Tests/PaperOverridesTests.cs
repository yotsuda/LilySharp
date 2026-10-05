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
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc svg|png|pdf --set KEY=VALUE</c> (LilySharp-Omr's proposal of 2026-10-02, P1): paper
/// values given from outside the file, read as paper entries and laid over the file's paper.
/// </summary>
[Trait("Category", "Unit")]
public class PaperOverridesTests
{
    private static LayoutOptions Over(LayoutOptions paper, params string[] settings)
    {
        var parsed = PaperOverrides.Parse(settings, out var error);
        Assert.Null(error);
        return parsed!.Apply(paper);
    }

    [Fact]
    public void ASettingIsAPaperEntry()
    {
        var p = Over(LayoutOptions.Default, "spacingIncrement=1.6", "leftMargin=13mm",
            "staffStaffSpacing.padding=2.5", "raggedRight");
        Assert.Equal(1.6, p.SpacingIncrement);
        Assert.Equal(7.397717, p.MarginLeft);   // 13mm, as the block reads it
        Assert.Equal(2.5, p.StaffSpacing.StaffStaff.Padding);
        Assert.True(p.RaggedRight);
    }

    [Fact]
    public void AFlagCanBeTurnedOff()
    {
        var on = LayoutOptions.Default with { RaggedRight = true, BreaksOnly = true };
        var p = Over(on, "raggedRight=false", "breaksOnly=false");
        Assert.False(p.RaggedRight);
        Assert.False(p.BreaksOnly);
    }

    [Theory]
    [InlineData("noSuchKey=1")]
    [InlineData("leftMargin=13furlongs")]
    [InlineData("spacingIncrement")]
    [InlineData("raggedRight=maybe")]
    [InlineData("=3")]
    public void ASettingThatDoesNotRead_IsRefused(string setting)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>The settings win over the file's paper and reach the page.</summary>
    [Fact]
    public void TheSettingsOverlayTheFilesPaper()
    {
        const string book = """
            paper { spacingIncrement 1.2  raggedRight }
            octave absolute
            part m { }
            section A { m { c'4 d' e' f' | g'1 | } }
            form main { A }
            score main { staff m }
            """;
        var tree = SyntaxTree.Parse(book);
        var settings = PaperOverrides.Parse(["spacingIncrement=2.4", "raggedRight=false"], out _);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree), settings: settings);
        Assert.Equal(2.4, score.Paper.SpacingIncrement);
        Assert.False(score.Paper.RaggedRight);

        string plain = SvgGenerator.Generate(tree, new SvgRenderOptions());
        string set = SvgGenerator.Generate(tree, new SvgRenderOptions { PaperOverrides = settings });
        Assert.NotEqual(plain, set);
        Assert.Equal(plain, SvgGenerator.Generate(tree, new SvgRenderOptions { PaperOverrides = PaperOverrides.Parse([], out _) }));
    }

    // ---- P2 (session 819): keys only a setting carries — LilyPond's shortest-duration-space
    // and its \paper systems-per-page / min- / max-. The owner's decision (2026-10-05): the
    // training-data knobs stay out of the language.

    [Fact]
    public void ASettingOnlyKey_IsASetting()
    {
        var p = Over(LayoutOptions.Default, "shortestDurationSpace=2.5", "maxSystemsPerPage=4", "minSystemsPerPage=2");
        Assert.Equal(2.5, p.ShortestDurationSpace);
        Assert.Equal(4, p.PageBreaking.MaxSystemsPerPage);
        Assert.Equal(2, p.PageBreaking.MinSystemsPerPage);
        Assert.Equal(6, Over(LayoutOptions.Default, "systemsPerPage=6").PageBreaking.SystemsPerPage);
    }

    [Theory]
    [InlineData("shortestDurationSpace=0")]
    [InlineData("shortestDurationSpace=2mm")]
    [InlineData("systemsPerPage=2.5")]
    [InlineData("systemsPerPage=0")]
    [InlineData("maxSystemsPerPage=-1")]
    public void ASettingOnlyKeyThatDoesNotRead_IsRefused(string setting)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>LilyPond warns and drops these (lily/page-breaking.cc:297-308 min_systems_per_page_); a setting is refused.</summary>
    [Theory]
    [InlineData("systemsPerPage=3", "maxSystemsPerPage=4")]
    [InlineData("systemsPerPage=3", "minSystemsPerPage=2")]
    [InlineData("minSystemsPerPage=5", "maxSystemsPerPage=4")]
    public void ConflictingPageCounts_AreRefused(string a, string b)
    {
        Assert.Null(PaperOverrides.Parse([a, b], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>Not a paper key: the file cannot write one.</summary>
    [Fact]
    public void ASettingOnlyKey_IsNotAPaperKey()
    {
        var tree = SyntaxTree.Parse("""
            paper { shortestDurationSpace 3 }
            part m { }
            section A { m { c'1 | } }
            form main { A }
            score main { staff m }
            """);
        var paper = tree.GetRoot().DescendantNodes<PaperDeclarationSyntax>().First();
        PaperPlanReader.Read(paper, out var problems);
        Assert.Contains(problems, p => p.Code == DiagnosticCodes.UnknownPaperKey);
    }

    /// <summary>
    /// shortestDurationSpace reaches the duration springs by LilyPond's numbers: the ragged
    /// line of <c>SpacingIncrementTests</c>' book.
    /// </summary>
    /// <remarks>MEASURED, LilyPond 2.26.0 (Lab sessions/p819/p2/sds): the <c>lysc ly --pin-fonts</c>
    /// twin with <c>\override Score.SpacingSpanner.shortest-duration-space</c> 1.5 / 2 / 3 draws a
    /// staff 68.5088 / 74.9525 / 88.1525 long.</remarks>
    [Theory]
    [InlineData("1.5", 68.5088)]
    [InlineData("2", 74.9525)]
    [InlineData("3", 88.1525)]
    public void TheShortestDurationSpace_IsLilyPonds(string space, double lilyPond)
    {
        var tree = TestPaper.ParseAtIndentZero("""
            paper { raggedRight }
            octave absolute
            part m { clef treble
              section A { c'4 d'8 e' f'2 | g'16 a' b' c'' d''4 e''2 | s1 | c'1 | }
            }
            form main { A }
            score main { staff m }
            """);
        string svg = SvgGenerator.Generate(tree, new SvgRenderOptions
        {
            EmbedFont = false, PaperOverrides = PaperOverrides.Parse(["shortestDurationSpace=" + space], out _),
        });
        var lengths = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.100\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value)
            .Select(m => double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                         - double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(5, lengths.Count);   // one system
        Assert.Equal(lilyPond, lengths.Average(), 1);
    }

    /// <summary>60 bars of one staff — 12 systems on one page by default.</summary>
    private static readonly string SixtyBars =
        "octave absolute\npart m { clef treble\n  section A { "
        + string.Concat(Enumerable.Repeat("c'8 d' e' f' g'4 a' | ", 60))
        + "}\n}\nform main { A }\nscore main { staff m }\n";

    /// <summary>The systems on each page, and what the layout said.</summary>
    private static (int[] PerPage, List<string> Warnings) Paged(params string[] settings)
    {
        var warnings = new List<string>();
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(SixtyBars), new SvgRenderOptions
        {
            EmbedFont = false, PaperOverrides = PaperOverrides.Parse(settings, out _), LayoutWarning = warnings.Add,
        });
        var pages = Regex.Split(svg, "<g class=\"page\"").Skip(1).ToList();
        if (pages.Count == 0)
            pages.Add(svg);
        return ([.. pages.Select(p => Regex.Matches(p, "stroke-width=\"0.100\"").Count / 5)], warnings);
    }

    /// <summary>
    /// The page counts LilyPond 2.26.0 makes of the same music (Lab sessions/p819/p2/lp, the
    /// twin with <c>\paper { systems-per-page = N }</c>): it RE-BREAKS THE LINES to fill every
    /// page — 12 systems under 5 become 15, paged 5/5/5.
    /// </summary>
    [Theory]
    [InlineData("systemsPerPage=3", new[] { 3, 3, 3, 3 })]
    [InlineData("systemsPerPage=5", new[] { 5, 5, 5 })]
    [InlineData("systemsPerPage=7", new[] { 7, 7 })]
    [InlineData("maxSystemsPerPage=4", new[] { 4, 4 })]
    public void SystemsPerPage_PagesAsLilyPondDoes(string setting, int[] lilyPond)
    {
        Assert.Equal([12], Paged().PerPage);
        var (perPage, warnings) = Paged(setting);
        Assert.Equal(lilyPond, perPage);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// More systems than fit: LilyPond places them anyway and compresses the page ("compressing
    /// over-full page by 14.6 staff-spaces", "page 1 has been compressed" — the same twin at 20).
    /// The systems stay on the paper, and the layout says so.
    /// </summary>
    [Fact]
    public void TooManySystemsForThePage_AreCompressedOntoIt_AndSaidSo()
    {
        var (perPage, warnings) = Paged("systemsPerPage=20");
        Assert.Equal([20], perPage);
        Assert.Single(warnings);
        Assert.StartsWith("page 1 is over-full by ", warnings[0]);
    }
}
