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
            form { A }
            score { staff m }
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
        Assert.Equal(4, Over(LayoutOptions.Default, "measuresPerSystem=4").MeasuresPerSystem);
    }

    [Theory]
    [InlineData("shortestDurationSpace=0")]
    [InlineData("shortestDurationSpace=2mm")]
    [InlineData("systemsPerPage=2.5")]
    [InlineData("systemsPerPage=0")]
    [InlineData("maxSystemsPerPage=-1")]
    [InlineData("measuresPerSystem=0")]
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

    /// <summary>A paper key since the owner's decision of 2026-10-05: the file writes it as the
    /// setting does, and the setting wins.</summary>
    [Fact]
    public void ThePageKeys_AreInTheLanguage_AndTheSettingWins()
    {
        const string book = """
            paper { shortestDurationSpace 3  systemsPerPage 4  measuresPerSystem 2  staffSpace 1.5mm  leftMargin 30mm }
            part m { }
            section A { m { c'1 | } }
            form { A }
            score { staff m }
            """;
        var tree = SyntaxTree.Parse(book);
        var paper = tree.GetRoot().DescendantNodes<PaperDeclarationSyntax>().First();
        PaperPlanReader.Read(paper, out var problems);
        Assert.Empty(problems);

        var p = PaperOf(book);
        Assert.Equal(3, p.ShortestDurationSpace);
        Assert.Equal(4, p.PageBreaking.SystemsPerPage);
        Assert.Equal(2, p.MeasuresPerSystem);
        Assert.Equal(1.5, p.StaffSpaceMm);
        Assert.Equal(20, p.MarginLeft);   // 30mm on the file's own staff space, wherever it is written

        var set = PaperOf(book, "staffSpace=2mm", "maxSystemsPerPage=3");
        Assert.Equal(2, set.StaffSpaceMm);
        Assert.Equal(15, set.MarginLeft);
        Assert.Equal(0, set.PageBreaking.SystemsPerPage);   // a later max clears the file's fixed count
        Assert.Equal(3, set.PageBreaking.MaxSystemsPerPage);
    }

    /// <summary>A score overrides the page keys as it overrides any other: a named block,
    /// referenced and overridden in part.</summary>
    [Fact]
    public void AScore_OverridesThePageKeys()
    {
        var tree = SyntaxTree.Parse("""
            paper small { staffSpace 1.4mm  systemsPerPage 6 }
            part m { }
            section A { m { c'1 | } }
            form { A }
            score { paper small { staffSpace 1.6mm }  staff m }
            """);
        var p = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).Paper;
        Assert.Equal(1.6, p.StaffSpaceMm);
        Assert.Equal(6, p.PageBreaking.SystemsPerPage);
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
            form { A }
            score { staff m }
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
        + "}\n}\nform { A }\nscore { staff m }\n";

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
    /// <summary>measuresPerSystem: N bars a system, however many the breaker would have put
    /// there (12 systems of 5 by default).</summary>
    [Theory]
    [InlineData(2, 30)]
    [InlineData(4, 15)]
    [InlineData(8, 8)]
    public void MeasuresPerSystem_FixesTheBarsOfEverySystem(int bars, int systems)
    {
        var (perPage, warnings) = Paged("measuresPerSystem=" + bars);
        Assert.Equal(systems, perPage.Sum());
        Assert.Empty(warnings);
    }

    /// <summary>
    /// Bars that cannot fit a system run past the margin — LilyPond's forced <c>\break</c> does
    /// the same and says nothing; here the layout says which system, since a PNG cuts that ink.
    /// </summary>
    [Fact]
    public void BarsThatCannotFitTheSystem_AreSaidSo()
    {
        string dense = "octave absolute\npart m { clef treble\n  section A { "
            + string.Concat(Enumerable.Repeat("cis''16 d'' ees'' fis'' g'' aes'' b'' c''' d'''4 bes'' | ", 16))
            + "}\n}\nform { A }\nscore { staff m }\n";
        var warnings = new List<string>();
        SvgGenerator.Generate(SyntaxTree.Parse(dense), new SvgRenderOptions
        {
            EmbedFont = false, PaperOverrides = PaperOverrides.Parse(["measuresPerSystem=8"], out _),
            LayoutWarning = warnings.Add,
        });
        Assert.Equal(2, warnings.Count);
        Assert.StartsWith("system 1 (page 1) is over-full by ", warnings[0]);
        Assert.StartsWith("system 2 (page 1) is over-full by ", warnings[1]);

        warnings.Clear();
        SvgGenerator.Generate(SyntaxTree.Parse(dense), new SvgRenderOptions { EmbedFont = false, LayoutWarning = warnings.Add });
        Assert.Empty(warnings);
    }

    [Fact]
    public void TooManySystemsForThePage_AreCompressedOntoIt_AndSaidSo()
    {
        var (perPage, warnings) = Paged("systemsPerPage=20");
        Assert.Equal([20], perPage);
        Assert.Single(warnings);
        Assert.StartsWith("page 1 is over-full by ", warnings[0]);
    }

    // ---- P3 (session 821): staffSpace=1.5mm, the staff's size on the paper — LilyPond's
    // set-global-staff-size. A setting only (the owner's decision 2026-10-05); the paper keeps
    // its millimetres and every output keeps the paper's size.

    [Theory]
    [InlineData("staffSpace=1.5mm", 1.5)]
    [InlineData("staffSpace=0.2cm", 2.0)]
    [InlineData("staffSpace=0.1in", 2.54)]
    public void StaffSpace_IsASetting(string setting, double mm)
    {
        var parsed = PaperOverrides.Parse([setting], out var error);
        Assert.Null(error);
        Assert.Equal(mm, parsed!.StaffSpaceMm!.Value, 9);
        Assert.Null(PaperOverrides.Parse(["spacingIncrement=1.6"], out _)!.StaffSpaceMm);
    }

    [Theory]
    [InlineData("staffSpace=1.5")]     // a bare number would be staff spaces — the unit it sets
    [InlineData("staffSpace=1.5pt")]
    [InlineData("staffSpace=0mm")]
    [InlineData("staffSpace=-1mm")]
    [InlineData("staffSpace=mm")]
    public void AStaffSpaceThatDoesNotRead_IsRefused(string setting)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    private static LayoutOptions PaperOf(string book, params string[] settings)
    {
        var tree = SyntaxTree.Parse(book);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree),
            settings: PaperOverrides.Parse(settings, out _)).Paper;
    }

    private const string OneBar = """
        part m { }
        section A { m { c'1 | } }
        form { A }
        score { staff m }
        """;

    /// <summary>
    /// The paper keeps its millimetres: a4 on a 1.5mm staff space is 140 × 198 spaces, its 15mm
    /// margins 10. The file's millimetres are read through the same staff space; its bare
    /// numbers are staff spaces and stay as written.
    /// </summary>
    [Fact]
    public void ThePapersMillimetres_AreReadThroughTheStaffSpace()
    {
        var p = PaperOf(OneBar, "staffSpace=1.5mm");
        Assert.Equal(1.5, p.StaffSpaceMm);
        Assert.Equal(140, p.PageWidth);
        Assert.Equal(198, p.PageHeight);
        Assert.Equal(10, p.MarginLeft);
        Assert.Equal(10, p.MarginRight);
        Assert.Equal(6.666667, p.MarginTop);
        Assert.Equal(10, p.Indent);
        Assert.Equal(p.ContentWidth, PaperOf("paper { paperWidth 210mm }\n" + OneBar, "staffSpace=1.5mm").ContentWidth);

        var written = PaperOf("paper { leftMargin 30mm  indent 5  staffStaffSpacing { basicDistance 12mm } }\n" + OneBar,
            "staffSpace=1.5mm");
        Assert.Equal(20, written.MarginLeft);
        Assert.Equal(5, written.Indent);
        Assert.Equal(8, written.StaffSpacing.StaffStaff.BasicDistance);
        Assert.Equal(98.666667, PaperOf("paper { size a5 }\n" + OneBar, "staffSpace=1.5mm").PageWidth);
        // A setting's millimetres too, read over the file's paper.
        Assert.Equal(20, PaperOf(OneBar, "staffSpace=1.5mm", "leftMargin=30mm").MarginLeft);

        // No setting, no change: LilyPond's staff space and the defaults' numbers.
        var plain = PaperOf(OneBar);
        Assert.Equal(LayoutOptions.DefaultStaffSpaceMm, plain.StaffSpaceMm);
        Assert.Equal(1.0, plain.StaffSpaceScale);
        Assert.Equal(LayoutOptions.Default, plain with { });
    }

    /// <summary>
    /// The systems LilyPond 2.26.0 makes of 40 bars under <c>#(set-global-staff-size N)</c> (Lab
    /// sessions/p821/p3, the <c>lysc ly --pin-fonts</c> twin): 6 / 8 / 10 systems at 15 / 20 / 26pt,
    /// the full ones 136.451 / 102.33 / 78.707 long — the same 180mm line in its staff spaces
    /// (LilyPond's line starts half its thickness in, and its thickness is 0.122 / 0.1 / 0.085 there).
    /// </summary>
    [Theory]
    [InlineData(15, 6, 136.451)]
    [InlineData(20, 8, 102.33)]
    [InlineData(26, 10, 78.707)]
    public void TheSystems_AreLilyPondsAtItsStaffSize(int points, int systems, double fullLine)
    {
        string forty = "octave absolute\npart m { clef treble\n  section A { "
            + string.Concat(Enumerable.Repeat("c'8 d' e' f' g'4 a' | ", 40))
            + "}\n}\nform { A }\nscore { staff m }\n";
        double mm = points / 4.0 * 25.4 / 72.27;
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(forty), new SvgRenderOptions
        {
            EmbedFont = false,
            PaperOverrides = PaperOverrides.Parse([string.Format(CultureInfo.InvariantCulture, "staffSpace={0:F6}mm", mm)], out _),
        });
        var lengths = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.100\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value)
            .Select(m => double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                         - double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(systems * 5, lengths.Count);
        Assert.Equal(fullLine, lengths[^1], 1);
    }

    /// <summary>Every output keeps the paper's size: a smaller staff space is a smaller staff on
    /// the same page, not a bigger picture. The boxes say which staff space they are in.</summary>
    [Fact]
    public void TheOutputs_KeepThePapersSize()
    {
        var tree = SyntaxTree.Parse(OneBar);
        var small = PaperOverrides.Parse(["staffSpace=1.2mm"], out _);
        static string Width(string svg) => Regex.Match(svg, "<svg [^>]*width=\"([\\d.]+)\"").Groups[1].Value;
        string plain = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        string set = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false, PaperOverrides = small });
        Assert.Equal("1195.0", Width(plain));
        Assert.Equal(Width(plain), Width(set));
        Assert.Contains("viewBox=\"0 0 175.00 ", set);   // 210mm / 1.2mm

        static int PngWidth(byte[] png) { using var b = SkiaSharp.SKBitmap.Decode(png); return b.Width; }
        Assert.Equal(PngWidth(LilySharp.Core.Png.PngGenerator.Generate(tree)),
            PngWidth(LilySharp.Core.Png.PngGenerator.Generate(tree, new LilySharp.Core.Png.PngRenderOptions { PaperOverrides = small })));

        var pages = LilySharp.Core.Rendering.Boxes.BoxesGenerator.GeneratePages(tree, RenderSpecParser.FindFirst(tree), small);
        Assert.Contains("\"staffSpaceMm\":1.2,", LilySharp.Core.Rendering.Boxes.BoxesGenerator.ToJson(pages, small!.StaffSpaceMm!.Value));
        Assert.Contains("\"staffSpaceMm\":1.757299,", LilySharp.Core.Rendering.Boxes.BoxesGenerator.ToJson(pages));
    }
}
