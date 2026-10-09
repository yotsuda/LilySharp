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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The page breaker prices a line in LilyPond's OWN frame for it: the origin at its staves'
/// pure top (SystemDetails.AlignmentOriginUp, read by the spring between two lines), and the
/// System's grobs — marks, bar numbers — lifted by how far the line's staff top stands over
/// the staff top of the bar each was measured in (PagingAugmentProgram.ExecuteForBreaker);
/// volta brackets not priced at all.
/// MEASURED (2.26.0, Lab sessions/p853): with both, LilyPond's page-1 decision on Boogie Oogie
/// Oogie and three variants of it is reproduced to 0.05 mm of paper height; without them
/// Lily# put a system more on page 1 than LilyPond (13/11 against 12/12 at A4).
/// </summary>
[Trait("Category", "Unit")]
[Collection(PageChainDebugTests.HookCollection)]
public sealed class BreakerAlignmentFrameTests
{
    private static SystemDetails Line(double up, double origin) => new()
    {
        Height = up + 4 + 1.4,
        TopExtent = up,
        BottomExtent = 1.4,
        StaffHeight = 4,
        Padding = 1,
        MinDistance = 8,
        SpringLength = 12,
        RefpointExtentUp = -2,
        RefpointExtentDown = -2,
        AlignmentOriginUp = origin,
    };

    [Fact]
    public void TheSpring_ReadsEachLinesRefpointsInItsOwnOrigin()
    {
        // LILYPOND-REF: lily/constrained-breaking.cc:657-667 Line_details::spring_length —
        //   space_ − (tallness_ + refpoint_extent_[DOWN] − next.refpoint_extent_[UP]), the
        //   refpoints each about its own line's origin, which the alignment puts at −(the staff's
        //   pure top) (lily/align-interface.cc:215-219). Boogie Oogie Oogie's lines 3 and 4:
        //   staff tops 5.545 and 4.545 over the top line.
        var lines = PageBreaker.CalcLineHeights(new[] { Line(5.545, 5.545), Line(4.545, 4.545) });
        double tall = lines[0].Tallness;
        double expected = 12 - (tall + (-2 - 5.545) - (-2 - 4.545));
        Assert.Equal(expected, lines[0].SpringLengthTo(lines[1]), 9);

        // In one shared frame (both origins 0) the two lines' tops would not enter the spring.
        var flat = PageBreaker.CalcLineHeights(new[] { Line(5.545, 0), Line(4.545, 0) });
        Assert.Equal(expected - 1.0, flat[0].SpringLengthTo(flat[1]), 9);
    }

    [Fact]
    public void TheCompressedTitleLine_KeepsTheFirstSystemsOrigin()
    {
        // LILYPOND-REF: lily/page-breaking.cc:152-210 compress_lines — the merged line is the
        //   lower line's details, its refpoint DOWN the lower line's moved by the shape's rise:
        //   in the lower line's frame, so its origin is the lower line's.
        var title = new SystemDetails
        {
            Height = 6.5, TopExtent = 0, BottomExtent = 0, StaffHeight = 6.5,
            Padding = 0.5, SpringLength = 5, InverseHooke = 1, IsTitle = true,
            PagePermission = BreakPermission.Forbid,
        };
        var compressed = PageBreaker.CompressLines(new[] { title, Line(3.991, 3.545), Line(4.545, 4.545) });
        Assert.NotNull(compressed);
        Assert.Equal(3.545, compressed![0].AlignmentOriginUp);
    }

    private static (VerticalSkyline up, VerticalSkyline down) Baseline()
    {
        // A staff whose first bar (x 10..20) stands 0.05 over its top line and whose second
        // (x 20..30) 3.0 — a note inside it, clear of the bar's start; the prefix (x 0..10) 0.05.
        var up = new VerticalSkyline(VerticalDirection.Up);
        up.MergeBox(0, 30, 0, 0.05);
        up.MergeBox(22, 26, 0, 3.0);
        var down = new VerticalSkyline(VerticalDirection.Down);
        down.MergeBox(0, 30, -4.05, -4);
        return (up, down);
    }

    private static readonly ImmutableArray<MeasureLayout> Bars = ImmutableArray.Create(
        new MeasureLayout(0, 10, 10, ImmutableArray<ItemLayout>.Empty),
        new MeasureLayout(1, 20, 10, ImmutableArray<ItemLayout>.Empty));

    [Fact]
    public void AMarkOverALowBar_IsLiftedOntoTheLinesStaffTop()
    {
        // LILYPOND-REF: lily/system.cc:893-923 part_of_line_pure_height — the System's own
        //   heights, taken per bar with the staff at THAT bar's translation, are united with the
        //   staves translated by the LINE's: the mark over bar 0 (0.8 over a staff top of 0.05)
        //   stands 0.8 over the line's 3.0.
        var b = new PagingAugmentProgram.Builder();
        b.AddMusicMarkBoxes(12, 16, 1, 3, pureBottom: 0.85, pureTop: 2.85, pureXLeft: 12, pureXRight: 16);
        var (up, _) = b.Build().ExecuteForBreaker(Baseline(), Bars, out double pureTop, out var staffUp);
        Assert.Equal(3.0, pureTop, 9);
        Assert.Equal(3.0, staffUp.MaxHeight(), 9);
        Assert.Equal(2.85 + (3.0 - 0.05), up.MaxHeightInRange(12, 16), 9);
    }

    [Fact]
    public void ABarsStaffTop_TakesTheClefAndKey_NotWhatTheLineCarriesInFromTheLineBefore()
    {
        // LILYPOND-REF: lily/axis-group-interface.cc:417-458 adjacent_pure_heights — a bar's
        //   begin heights hold what LilyPond would print were a line to START at that bar (clef,
        //   key); a slur coming in from the line before spans only the line's first bars.
        // MEASURED (2.26.0, Lab sessions/p854/cjk, ひまわりの約束 line 3): the line opens on a
        //   slur from line 2, and the mark opening line 4 is priced over its last bar, whose
        //   staff top LilyPond reads at 0.05 (+ the key) — Lily# read 3.045 off the slur's piece
        //   over the line's prefix and lifted the mark 2.5 too little; LilyPond 7/8/8/1, Lily#
        //   8/8/8 until this. A replayed step over the prefix stands in for the slur's piece here.
        var b = new PagingAugmentProgram.Builder();
        b.AddMarkBox(2, 8, 0, 2.5);
        b.AddMusicMarkBoxes(12, 16, 1, 3, pureBottom: 0.85, pureTop: 2.85, pureXLeft: 12, pureXRight: 16);
        var (up, _) = b.Build().ExecuteForBreaker(Baseline(), Bars, out double pureTop, out _);
        Assert.Equal(3.0, pureTop, 9);
        // Bar 0's staff top is the base's prefix (0.05), not the step's 2.5.
        Assert.Equal(2.85 + (3.0 - 0.05), up.MaxHeightInRange(12, 16), 9);
    }

    [Fact]
    public void AMarkOverTheHighestBar_IsNotLifted_AndAVoltaIsNotPriced()
    {
        // LILYPOND-REF: lily/system.cc:940-967 calc_pure_relevant_grobs — the VoltaBracketSpanner
        //   is an axis group, so no volta bracket is in a line's pure heights.
        var b = new PagingAugmentProgram.Builder();
        b.AddVoltaBox(10, 30, 4, 6);
        b.AddMusicMarkBoxes(22, 26, 4, 6, pureBottom: 3.8, pureTop: 5.8, pureXLeft: 22, pureXRight: 26);
        var (up, _) = b.Build().ExecuteForBreaker(Baseline(), Bars, out _, out _);
        Assert.Equal(5.8, up.MaxHeightInRange(22, 26), 9);
        Assert.Equal(0.05, up.MaxHeightInRange(10, 19), 9);
        // The layout's replay still merges the volta where it is drawn.
        var (drawn, _) = b.Build().Execute(Baseline());
        Assert.Equal(6.0, drawn.MaxHeightInRange(10, 19), 9);
    }

    [Fact]
    public void AmandasStaffAndTabScore_PagesAsLilyPondDoes()
    {
        // MEASURED (2.26.0, the twin of audit/tabfingering/amanda.lys' "both" score, Lab
        // sessions/p851/ws): 7/8/3. Lily# put 8/8/2 while the two frames were missing.
        var path = System.IO.Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "tabfingering", "amanda.lys");
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(System.IO.File.ReadAllText(path));
        var spec = System.Linq.Enumerable.First(LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree),
            s => s.OutputFile.Contains("both"));
        var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine(score.Paper).Layout(score);
        Assert.Equal(new[] { 7, 8, 3 }, System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.Select(layout.Pages, p => p.Systems.Length)));
    }

    [Fact]
    public void TheCountLoopsEstimate_LiftsAMarkAsThePlacedLineDoes()
    {
        // The system-count loop prices candidate lines from per-bar tables
        // (LayoutEngine.EstimateMeasureHeights): a mark's height is kept over ITS bar's staff top
        // and lifted onto each candidate line's own. For a candidate that IS a placed line the two
        // must agree — a mark over a low bar, after a bar whose notes stand over the staff.
        const string source = """
            octave absolute
            time 4/4

            part p {
              clef bass
              section S {
                c'4 e' c' e' | c,4@mark("A") c, c, c, | break c,1 | c,1 |
              }
            }

            form { S }

            score {
              staff p
            }
            """;
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(source);
        var spec = System.Linq.Enumerable.First(LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree));
        var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, spec);
        var log = new System.Collections.Generic.List<string>();
        int mine = System.Environment.CurrentManagedThreadId;
        LayoutEngine.DebugPageBreakingScoring = s =>
        {
            if (System.Environment.CurrentManagedThreadId == mine)
                log.Add(s);
        };
        try
        {
            new LayoutEngine(score.Paper).Layout(score);
        }
        finally
        {
            LayoutEngine.DebugPageBreakingScoring = null;
        }
        static double RestUp(string row) => double.Parse(
            System.Text.RegularExpressions.Regex.Match(row, @"rest ([\d.]+)/").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var placed = System.Linq.Enumerable.First(log, l => l.Contains("placed sys 1:", System.StringComparison.Ordinal));
        var estimated = System.Linq.Enumerable.First(log, l => l.Contains("est line 1:", System.StringComparison.Ordinal));
        // The mark is lifted at all (the notes of bar 1 stand over the staff, bar 2's do not)...
        Assert.True(RestUp(placed) > 3.6, placed);
        // ...and the estimate prices the same line the same.
        Assert.Equal(RestUp(placed), RestUp(estimated), 6);
    }

    [Fact]
    public void ALineStartBarNumber_IsMeasuredAgainstTheFirstBar()
    {
        // A box left of the first bar (the begin bucket) was taken at the first bar's break
        // rank, so it rises by the line's staff top over the first bar's.
        var b = new PagingAugmentProgram.Builder();
        b.AddBarNumberBox(2, 6, 1.0, 2.3);
        var (up, _) = b.Build().ExecuteForBreaker(Baseline(), Bars, out _, out _);
        Assert.Equal(2.3 + (3.0 - 0.05), up.MaxHeightInRange(2, 6), 9);
    }
}
