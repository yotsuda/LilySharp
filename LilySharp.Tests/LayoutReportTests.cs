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

using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The 'layout' report exposes the engine's line-break / system / page decisions as
/// plain text, so an author (AI or human) can verify the layout without rendering.
/// </summary>
[Trait("Category", "Unit")]
public class LayoutReportTests
{
    private static string Report(string src) => LayoutReport.Generate(SyntaxTree.Parse(src));

    [Fact]
    public void ForcedBreaks_AreListedAndCollapseRuns()
    {
        // 'break' after bar 2 and bar 4 → three 2-bar systems. The equal-bar-count run
        // collapses to one line, and the explicit breaks are listed concisely.
        var report = Report(
            "part melody\n" +
            "section Main {\n" +
            "  melody {\n" +
            "    c4 d e f | g4 a b c' |\n" +
            "    break\n" +
            "    c4 d e f | g2. r4 |\n" +
            "    break\n" +
            "    e4 f g a | b2. r4 |\n" +
            "  }\n" +
            "}\n" +
            "form main { Main }\n" +
            "score main \"brk\" { staff melody }\n");

        Assert.Contains("score main \"brk\"", report);
        Assert.Contains("3 systems", report);
        Assert.Contains("systems 1-3: 2 bars each (bars 1-6)", report);
        Assert.Contains("forced breaks after bar: 2, 4", report);
    }

    [Fact]
    public void SingleSystem_ReportsOneRunAndNoForcedBreaks()
    {
        var report = Report(
            "part melody\n" +
            "section Main { melody { c4 d e f | g4 a b c' | c'4 b a g | f4 e d c | } }\n" +
            "form main { Main }\n" +
            "score main \"one\" { staff melody }\n");

        Assert.Contains("1 system, 4 bars", report);
        Assert.Contains("system 1: bars 1-4", report);
        Assert.DoesNotContain("forced breaks", report);
    }

    [Fact]
    public void MidPieceTimeChanges_AreReportedWithTheirBars()
    {
        var report = Report(
            "time 4/4\n" +
            "part melody { clef treble }\n" +
            "section Main { melody {\n" +
            "  c'4 d e f |\n" +
            "  time 3/4 g4 a b |\n" +
            "  time 6/8 a8 g f e d c |\n" +
            "} }\n" +
            "form main { Main }\n" +
            "score main \"meter\" { staff melody }\n");

        Assert.Contains("time 4/4 -> 3/4 (bar 2) -> 6/8 (bar 3)", report);
    }

    [Fact]
    public void AllScores_FlagSelectsFirstOrEvery()
    {
        var src =
            "part rh { clef treble }\n" +
            "section A { rh { c'4 d' e' f' | } }\n" +
            "form main { A }\n" +
            "score main \"first\" { staff rh }\n" +
            "score main \"second\" { staff rh }\n";
        var tree = SyntaxTree.Parse(src);

        var firstOnly = LayoutReport.Generate(tree);
        Assert.Contains("score main \"first\"", firstOnly);
        Assert.DoesNotContain("score main \"second\"", firstOnly);

        var all = LayoutReport.Generate(tree, allScores: true);
        Assert.Contains("score main \"first\"", all);
        Assert.Contains("score main \"second\"", all);
    }

    [Fact]
    public void Pages_AreReportedWithTheirSystemCounts()
    {
        // Twelve forced one-bar systems on a page too short to hold them: the breaker
        // must split them over more than one page, and the report says how — the page
        // count and each page's system count, which is what the corpus sweep compares
        // against LilyPond's page count.
        var bars = string.Join(" break\n", Enumerable.Repeat("c4 d e f |", 12));
        var report = Report(
            "paper { paperHeight 80mm }\n" +
            "part melody\n" +
            "section Main { melody {\n" + bars + "\n} }\n" +
            "form main { Main }\n" +
            "score main \"pg\" { staff melody }\n");

        Assert.Contains("12 systems, 12 bars", report);
        var line = report.Split('\n').Single(l => l.TrimStart().StartsWith("pages: ")).Trim();
        var m = System.Text.RegularExpressions.Regex.Match(
            line, @"pages: (\d+)  \|  systems per page: ([\d, ]+)$");
        Assert.True(m.Success, line);
        int pageCount = int.Parse(m.Groups[1].Value);
        var perPage = m.Groups[2].Value.Split(", ").Select(int.Parse).ToArray();
        Assert.True(pageCount > 1, line);
        Assert.Equal(pageCount, perPage.Length);
        Assert.Equal(12, perPage.Sum());
    }

    [Fact]
    public void SinglePage_ReportsOnePageHoldingEverySystem()
    {
        var report = Report(
            "part melody\n" +
            "section Main { melody { c4 d e f | g4 a b c' | } }\n" +
            "form main { Main }\n" +
            "score main \"one\" { staff melody }\n");

        Assert.Contains("pages: 1  |  systems per page: 1", report);
    }

    [Fact]
    public void Header_ListsEveryStaffAndClef()
    {
        var report = Report(
            "part rh { clef treble }\n" +
            "part lh { clef bass }\n" +
            "section A { rh { c'4 d' e' f' | } lh { c2 g, | } }\n" +
            "form main { A }\n" +
            "score main \"gs\" { grandStaff { staff rh staff lh } }\n");

        Assert.Contains("staves: treble, bass", report);
    }
}
