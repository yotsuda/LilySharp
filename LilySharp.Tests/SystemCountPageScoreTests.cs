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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// The system count is chosen by the PAGE's score — LilyPond's
/// lily/optimal-page-breaking.cc:41-254 <c>Optimal_page_breaking::solve</c>, which starts
/// from the line DP's best count and re-chooses it by Σ line force² + Σ break penalty +
/// 10 × page demerits (page-breaking.cc:1548-1586 finalize_spacing_result) — and not by the
/// line DP alone, whose Δforce² term splits a line after a very underfull forced-break
/// line. Session 321 traced the 69-book "LilyPond sets 4 bars where Lily# sets 2+2" family
/// (HANDOFF §2 T7 B-eng) to exactly that term; session 322 ported the loop.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SystemCountPageScoreTests
{
    private readonly ITestOutputHelper _output;

    public SystemCountPageScoreTests(ITestOutputHelper output) => _output = output;

    private const string Head = """
        octave absolute
        key f major
        time 4/4
        part bl {
          clef bass
          section Body {

        """;

    private const string Tail = """

          }
        }
        form main { Body }
        score main { staff bl }
        """;

    private const string FourBusyBars =
        "d,4 a,, bes,,8. a,,16 r a,, cis,8 | d,4 a,, bes,,8. a,,16 r a,, cis,8 | "
        + "d,4 a,, bes,,8. a,,16 r a,, a,,8 | bes,,4 r16 a,,8. d,4 r |";

    private const string TwoClosingBars = "a,,4 cis, d, f,8 d, | a,,4 cis, d, a,,8 d, |";

    /// <summary>scratch/p321/fx/bis-v6-proper-rests-first.lys — the reproduction.</summary>
    private const string RestsThenBreak =
        Head + "r1 | r1 | break " + FourBusyBars + " break " + TwoClosingBars + Tail;

    /// <summary>bis-v8-quarters-first.lys — a first line of quarters, same split.</summary>
    private const string QuartersThenBreak =
        Head + "c,4 c, c, c, | c, c, c, c, | break " + FourBusyBars + " break " + TwoClosingBars + Tail;

    /// <summary>bis-v12-full-eighths-first.lys — a FULL first line: the line DP never split
    /// this, and the page score must not move it.</summary>
    private const string FullEighthsThenBreak =
        Head + "c,8 c, c, c, c, c, c, c, | c,8 c, c, c, c, c, c, c, | c,8 c, c, c, c, c, c, c, | "
        + "c,8 c, c, c, c, c, c, c, | break " + FourBusyBars + " break " + TwoClosingBars + Tail;

    /// <summary>bis-v3-nointro.lys — no underfull first line at all.</summary>
    private const string NoIntro = Head + FourBusyBars + " break " + TwoClosingBars + Tail;

    private static MultiStaffScore ScoreOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static int[] BarsPerSystem(string source)
    {
        var score = ScoreOf(source);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        return layout.AllSystems.Select(s => s.Measures.Length).ToArray();
    }

    [Fact]
    public void RestsThenBreak_TakesLilyPondsThreeSystems()
    {
        // LilyPond 2.26.0: 2 | 4 | 2 (scratch/p321/fx/bis-v6-proper-rests-first-lp.out).
        // The line DP alone: 2 | 2 | 2 | 2.
        Assert.Equal(new[] { 2, 4, 2 }, BarsPerSystem(RestsThenBreak));
    }

    [Fact]
    public void QuartersThenBreak_TakesLilyPondsThreeSystems()
    {
        // LilyPond: 2 | 4 | 2 (bis-v8-quarters-first-lp.out).
        Assert.Equal(new[] { 2, 4, 2 }, BarsPerSystem(QuartersThenBreak));
    }

    [Fact]
    public void FullFirstLine_IsNotMoved()
    {
        // Positive control for the loop's restraint: LilyPond 4 | 4 | 2
        // (bis-v12-full-eighths-first-lp.out), the line DP the same.
        Assert.Equal(new[] { 4, 4, 2 }, BarsPerSystem(FullEighthsThenBreak));
    }

    [Fact]
    public void NoUnderfullLine_IsNotMoved()
    {
        Assert.Equal(new[] { 4, 2 }, BarsPerSystem(NoIntro));
    }

    /// <summary>
    /// The line DP's table answers every count, and the page score reads Σ force² without
    /// the Δforce² term: on the reproduction, 3 lines are cheaper than 4 by that sum while
    /// the DP's own demerits (with Δforce²) prefer 4. LilyPond's page scores for the same
    /// book are 38.781 (3 systems) and 42.466 (4) — bis-v6-dbg.err — and on a one-page
    /// ragged-last book the page term is 0, so those ARE the line sums.
    /// </summary>
    [Fact]
    public void LineBreakSolutions_SumsForceSquaredWithoutTheDeltaTerm()
    {
        var score = ScoreOf(RestsThenBreak);
        var options = score.Paper;
        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        var springs = SystemBreaker.ComputeMultiStaffSpringData(score, shortest);
        double clef = SpacingRules.MaxClefWidth(score);
        var breaker = new KnuthPlassBreaker(
            options.ContentWidth,
            SystemBreaker.GateFirstPrefixWidth(score, clef) + options.Indent,
            SystemBreaker.GateContinuationPrefixWidth(score, clef) + options.ShortIndent,
            raggedRight: options.RaggedRight);
        var solutions = breaker.Solve(springs);

        Assert.True(solutions.HasAlternatives);
        Assert.Equal(4, solutions.IdealLineCount);
        Assert.Equal(8, solutions.MaxLineCount);

        var three = solutions.For(3);
        var four = solutions.For(4);
        Assert.NotNull(three);
        Assert.NotNull(four);
        _output.WriteLine($"3 lines: Σf² {three!.Value.ForceSquaredSum:F3} {string.Join(",", three.Value.Breaks)}");
        _output.WriteLine($"4 lines: Σf² {four!.Value.ForceSquaredSum:F3} {string.Join(",", four.Value.Breaks)}");

        Assert.Equal(new[] { 2, 6, 8 }, three.Value.Breaks);
        Assert.True(three.Value.ForceSquaredSum < four.Value.ForceSquaredSum,
            $"3 lines Σf² {three.Value.ForceSquaredSum:F3} should be under 4 lines' {four.Value.ForceSquaredSum:F3}");
        // Within LilyPond's own numbers to the coarser digit (its forces come from its own
        // spacing, so the last digits are not expected to agree).
        Assert.InRange(three.Value.ForceSquaredSum, 38.781 - 0.6, 38.781 + 0.6);
        Assert.InRange(four.Value.ForceSquaredSum, 42.466 - 0.6, 42.466 + 0.6);

        // Two forced breaks: no breaking into 2 lines exists, and the ideal is reachable.
        Assert.Null(solutions.For(2));
        Assert.Equal(solutions.IdealBreaks, solutions.For(4)!.Value.Breaks);
    }

    /// <summary>scratch/p322/fx/alone-intro8.lys — the head of the corpus book "Alone Again"
    /// (HANDOFF §2 T7 F12): LilyPond breaks its twelve bars 4 | 4 | 4, and until session 323
    /// Lily# broke them 8 | 4.</summary>
    private const string AloneAgainIntro = """
        octave absolute
        tempo 86
        key fis major
        time 4/4
        part bl {
          clef bass
          section Intro {
            fis,,2 fis,,8 fis,, r cis, | ais,,2 ais,,8 ais,, r ais,, | gis,,4 r8 dis,8 cis, cis, r cis, | fis,,2 r8 fis,, cis,16 c, cis,8 |
          }
          section A {
            fis,2 fis,8 fis, r fis, | ais,2 ais,8 ais, r ais, | cis,4. cis,8 cis,4. cis,8 | ais,4 r8 ais, dis,2 | break
            gis,4. dis,8 gis, gis, r dis, | gis,2 gis,8 gis, r cis, | fis,2 fis,8 fis, r cis, | fis,,4 r8 fis,, eis,,4 eis, |
          }
        }
        form main { Intro A }
        score main { staff bl }
        """;

    /// <summary>
    /// A compressed line is priced by SOLVING its springs, not by the linear estimate: the
    /// 8-bar first line of this book has every bar end on an up-stem flagged eighth whose
    /// spring blocks at force −0.17, and once those block the rest must give more. LilyPond
    /// (2.26.0, -ddebug-page-breaking-scoring on scratch/p322/fx/alone-intro8.ly) scores
    /// 3 systems 1.274284 and 2 systems 1.648071; the sums-only estimate priced the 2-system
    /// breaking 1.222 and chose it.
    /// </summary>
    [Fact]
    public void AloneAgainIntro_TakesLilyPondsThreeSystems()
    {
        Assert.Equal(new[] { 4, 4, 4 }, BarsPerSystem(AloneAgainIntro));

        var score = ScoreOf(AloneAgainIntro);
        var options = score.Paper;
        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        var springs = SystemBreaker.ComputeMultiStaffSpringData(score, shortest);
        double clef = SpacingRules.MaxClefWidth(score);
        var breaker = new KnuthPlassBreaker(
            options.ContentWidth,
            SystemBreaker.GateFirstPrefixWidth(score, clef) + options.Indent,
            SystemBreaker.GateContinuationPrefixWidth(score, clef) + options.ShortIndent,
            raggedRight: options.RaggedRight);
        // The forced break after bar 8 splits the book: 12 bars, so the first 8 are the
        // part the count loop weighs, 2 or 3 lines before the forced break.
        var solutions = breaker.Solve(springs);
        var three = solutions.For(3);
        var two = solutions.For(2);
        Assert.NotNull(three);
        Assert.NotNull(two);
        _output.WriteLine($"3 lines: Σf² {three!.Value.ForceSquaredSum:F4} {string.Join(",", three.Value.Breaks)}");
        _output.WriteLine($"2 lines: Σf² {two!.Value.ForceSquaredSum:F4} {string.Join(",", two.Value.Breaks)}");
        // One page, ragged last: the page term is 0, so these ARE LilyPond's scores.
        Assert.InRange(three.Value.ForceSquaredSum, 1.274 - 0.05, 1.274 + 0.05);
        Assert.InRange(two.Value.ForceSquaredSum, 1.648 - 0.2, 1.648 + 0.2);
        Assert.True(two.Value.ForceSquaredSum > three.Value.ForceSquaredSum);
    }

    private static string FixtureText(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (Directory.Exists(candidate))
                return File.ReadAllText(Path.Combine(candidate, name + ".lys"));
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/");
    }

    /// <summary>
    /// The count loop prices each candidate line's BEGIN bucket by that line's own start —
    /// LilyPond's begin_line_heights is per break rank (lily/axis-group-interface.cc:417-458
    /// adjacent_pure_heights; lily/system.cc:926-928 begin_of_line_pure_height reads the one
    /// entry at the line's start) — and not by the widest line start the ideal placement
    /// showed. The excerpt is the real-corpus book "Le Freak", bars 1-57, staff + tab: the
    /// first line carries the tempo and the "Intro" box over its prefix (7.30 above the
    /// body), every continuation line a clef and a bar number (2.31). With one bucket for
    /// every line the estimate fitted six systems to a page where the placement fits eight,
    /// the ideal 13 lines needed three pages against 12's two, and the loop's "one page
    /// fewer and stretched" exit stopped it at 12 — LilyPond's 11, whose line sum the DP
    /// had already priced cheapest (4.56 against LilyPond's 4.54,
    /// -ddebug-page-breaking-scoring on scratch/p335/lsi.ly), was never tried.
    /// <para>
    /// LilyPond 2.26.0 engraves 11 systems: the twin of this fixture as 5|4|6|10|…
    /// (scratch/p335/lsi-when.txt) and the hand-written corpus book as 5|4|4|12|… — the
    /// two 11-line breakings of A1 are 0.02 demerits apart at the DP state that decides
    /// them (Lily# prices 4|12 at 13.28 and 6|10 at 12.82 over the whole book, but the
    /// state after bar 25 keeps 4|12 by 2.52 against 2.55), and the tie turns on bars
    /// 10-11 being 1.3 ss wider in Lily# than in LilyPond — NOT on the twin's string
    /// numbers or its tab mode: both were taken out of the twin (session 335) and its
    /// widths did not move by a hundredth. The COUNT is what this test guards; the split
    /// is the hand-written book's, which the corpus sweep measures
    /// (scratch/p335/structure-after335.csv: Le Freak's three scores match LilyPond).
    /// </para>
    /// </summary>
    [Fact]
    public void LineStartInk_IsPricedPerCandidateLine()
    {
        var withInk = FixtureText("test/system-count-line-start-ink");
        Assert.Equal(new[] { 5, 4, 4, 12, 4, 6, 2, 8, 4, 4, 4 }, BarsPerSystem(withInk));

        // The pair: the same book without the ink over its first line. The first line's
        // ink must not price the other lines, so the two books break the same way — and
        // LilyPond engraves the plain book in the same 11 systems (lsi-plain-when.txt).
        var plain = withInk.Replace("tempo 120\n", "").Replace("tempo 120\r\n", "")
            .Replace("@mark(\"Intro\")", "");
        Assert.NotEqual(withInk, plain);
        Assert.Equal(new[] { 5, 4, 4, 12, 4, 6, 2, 8, 4, 4, 4 }, BarsPerSystem(plain));
    }

    private static PageBreaker Breaker(PageBreakingParameters parameters) =>
        new(pageHeight: 169.009370, topMargin: 5.690551, bottomMargin: 5.690551,
            headerHeight: 0, parameters: parameters);

    private static PageBreakResult Pages(params double[] forces) => new()
    {
        Penalty = 0,
        Forces = forces.ToImmutableArray(),
        SystemsPerPage = forces.Select(_ => 1).ToImmutableArray(),
    };

    /// <summary>LILYPOND-REF: lily/page-breaking.cc:1548-1586 finalize_spacing_result — the
    /// page range charged.</summary>
    [Fact]
    public void Demerits_ChargesLinesPlusWeightedPages_AndSparesTheRaggedLastPage()
    {
        var raggedLast = Breaker(PageBreakingParameters.Default);   // ragged-last-bottom = ##t
        // The last page is not charged; the first is: 10 × 2² + (5 + 1).
        Assert.Equal(46.0, raggedLast.Demerits(Pages(2, 3), lineForceSquared: 5, lineBreakPenalty: 1), 9);

        var justified = Breaker(PageBreakingParameters.Default with { RaggedLastBottom = false });
        Assert.Equal(136.0, justified.Demerits(Pages(2, 3), 5, 1), 9);

        var ragged = Breaker(PageBreakingParameters.Default with { RaggedBottom = true, RaggedLastBottom = false });
        // ragged () charges the LAST page only: 10 × 3² + 6.
        Assert.Equal(96.0, ragged.Demerits(Pages(2, 3), 5, 1), 9);

        // An overfull page is BAD_SPACING_PENALTY, not infinite (:1576).
        Assert.Equal(6 + 10 * PageBreaker.BadSpacingPenalty,
            justified.Demerits(Pages(double.NegativeInfinity, 0), 5, 1), 3);
        // ...and no pages at all is the infinite Page_spacing_result.
        Assert.Equal(double.PositiveInfinity, justified.Demerits(Pages(), 5, 1));
    }

    /// <summary>LILYPOND-REF: lily/page-breaking.cc:1186-1278 min_page_count.</summary>
    [Fact]
    public void MinPageCount_StacksAtMinimumSpacing()
    {
        var breaker = Breaker(PageBreakingParameters.Default);
        SystemDetails Tall(double height) => PageBreaker.CreateFromLayout(
            staffHeight: 4, topExtent: (height - 4) / 2, bottomExtent: (height - 4) / 2,
            padding: 1, springLength: 12);

        Assert.Equal(1, breaker.MinPageCount(new[] { Tall(20) }));
        Assert.Equal(1, breaker.MinPageCount(new[] { Tall(20), Tall(20), Tall(20) }));
        // Three systems of 100 on a 157.6 band: none share a page.
        Assert.Equal(3, breaker.MinPageCount(new[] { Tall(100), Tall(100), Tall(100) }));
        // A forced page break opens a page whatever the heights (:1222-1223).
        var forced = Tall(20) with { PagePermission = BreakPermission.Force };
        Assert.Equal(2, breaker.MinPageCount(new[] { forced, Tall(20) }));
    }

    /// <summary>
    /// The unconstrained one-dimensional page DP (LilyPond's simple_state_, what the scored
    /// breaker runs) and the two-dimensional table the paging path runs answer the same
    /// question: the same pages, on books tall enough to need several, with a title header
    /// on the first, and with a forced page break inside.
    /// </summary>
    [Fact]
    public void BreakIntoPagesScored_AgreesWithTheTwoDimensionalTable()
    {
        SystemDetails Sys(double h, BreakPermission after = BreakPermission.Allow) =>
            PageBreaker.CreateFromLayout(4, (h - 4) / 2, (h - 4) / 2, padding: 1, springLength: 12)
                with { PagePermission = after };
        var books = new[]
        {
            Enumerable.Range(0, 30).Select(i => Sys(6 + (i * 7) % 11)).ToArray(),
            Enumerable.Range(0, 45).Select(i => Sys(5 + (i * 3) % 9)).ToArray(),
            Enumerable.Range(0, 24).Select(i => Sys(8, i == 9 ? BreakPermission.Force : BreakPermission.Allow)).ToArray(),
            Enumerable.Range(0, 24).Select(i => Sys(8, i == 5 ? BreakPermission.Forbid : BreakPermission.Allow)).ToArray(),
        };
        foreach (double header in new[] { 0.0, 9.5 })
        foreach (var book in books)
        {
            var breaker = new PageBreaker(169.009370, 5.690551, 5.690551, header, PageBreakingParameters.Default);
            var table = breaker.BreakIntoPages(book);
            var sizes = new List<int>();
            int start = 0;
            foreach (int end in table) { sizes.Add(end - start); start = end; }
            var scored = breaker.BreakIntoPagesScored(book);
            Assert.True(sizes.SequenceEqual(scored.SystemsPerPage),
                $"header {header}, {book.Length} systems: table {string.Join(",", sizes)} / scored {string.Join(",", scored.SystemsPerPage)}");
            Assert.True(scored.PageCount > 1, "the book must need several pages for the net to compare anything");
        }
    }

    /// <summary>
    /// The scored breaker reports what the loop reads: pages, systems per page, forces —
    /// and a ragged last page that would stretch is reported at force 0 (page-spacing.cc:357).
    /// </summary>
    [Fact]
    public void BreakIntoPagesScored_ReportsForcesPerPage()
    {
        var breaker = Breaker(PageBreakingParameters.Default);
        var one = PageBreaker.CreateFromLayout(4, 2, 2, padding: 1, springLength: 12);
        var result = breaker.BreakIntoPagesScored(new[] { one, one, one });
        Assert.Equal(1, result.PageCount);
        Assert.Equal(new[] { 3 }, result.SystemsPerPage);
        Assert.Equal(0.0, result.Forces[0], 9);
        Assert.Equal(0.0, result.AverageForce, 9);
    }
}
