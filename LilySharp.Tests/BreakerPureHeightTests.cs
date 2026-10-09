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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The page BREAKER prices a line by LilyPond's PURE heights, not by the drawn ink: a beamed
/// stem at its group's unbeamed reach and no beam (lily/stem.cc:387-447), a slur as its notes
/// plus half a space (lily/slur.cc:74-130), no tuplet bracket and no tie (no pure Y-extent,
/// lily/grob-property.cc:357-360), and a mark opening a line in that line's begin heights
/// only (lily/axis-group-interface.cc:441-458).
/// </summary>
/// <remarks>
/// The expected numbers are LilyPond 2.26.0's own <c>adjacent-pure-heights</c> of the same
/// music (the twin, <c>lysc ly --pin-fonts</c>, with the VerticalAxisGroup's callback wrapped to
/// print its result — Lab sessions/p852/nb and /pr): the rest-of-line top above the staff's top
/// line. The line's shape is read off <see cref="LayoutEngine.DebugPageBreakingScoring"/>'s
/// "placed sys" row, which prints the breaker's own <c>LineShape</c>.
/// </remarks>
[Trait("Category", "Unit")]
[Collection(PageChainDebugTests.HookCollection)]
public class BreakerPureHeightTests
{
    private static string Book(string music) => $$"""
        octave absolute
        time 4/4

        part p {
          clef bass
          section S {
            {{music}}
          }
        }

        form { S }

        score {
          staff p
        }
        """;

    // The first placed line's shape: (begin up, begin down, rest up, rest down).
    private static (double BeginUp, double BeginDown, double RestUp, double RestDown) FirstLine(string source)
        => Line(source, 1);

    // Placed line <paramref name="n"/>'s shape (1-based).
    private static (double BeginUp, double BeginDown, double RestUp, double RestDown) Line(string source, int n)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindAll(tree).First();
        var score = SvgGenerator.CollectScore(tree, spec);
        var log = new List<string>();
        int mine = Environment.CurrentManagedThreadId;
        LayoutEngine.DebugPageBreakingScoring = s =>
        {
            if (Environment.CurrentManagedThreadId == mine)
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
        var row = log.First(l => l.Contains($"placed sys {n}:", StringComparison.Ordinal));
        var m = Regex.Match(row, @"shape begin ([\d.]+)/([\d.]+) rest ([\d.]+)/([\d.]+)");
        Assert.True(m.Success, row);
        double D(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
        return (D(1), D(2), D(3), D(4));
    }

    [Fact]
    public void ABeamedStem_IsPricedAtItsGroupsUnbeamedReach_NotAtTheDrawnBeam()
    {
        // MEASURED (2.26.0, Lab sessions/p852/nb): the rest-of-line top is 3.375 over the
        // middle line — the eighths' unbeamed stems — where the drawn beam stands at 3.72.
        var shape = FirstLine(Book(
            "des,8. des,16 r8 aes,, bes,,4 bes,,8 f,,16 e,, | ees,,8. ees,,16 r8 ees,, aes,,4 r8 aes,,16 c, | des,8. des,16 r8 aes,, des,4 des,16 aes,, f,,8 |"));
        Assert.Equal(3.375 - 2.0, shape.RestUp, 6);
    }

    [Fact]
    public void AMark_RestsItsPaddingOverTheStaffSymbolsExtent_NotItsTopLine()
    {
        // LILYPOND-REF: lily/side-position-interface.cc aligned_side — the mark's support is the
        //   staff symbol, whose extent reaches half a line thickness (0.05) over the top line;
        //   scm/define-grobs.scm RehearsalMark padding 0.8.
        // MEASURED (2.26.0, Lab sessions/p854: Final Fantasy V Main Theme's twin, a line opening
        //   on a boxed "B" over notes inside the staff): the line's pure top is 3.555 over the top
        //   line = 0.85 + the box's 2.705 (Lab sessions/p854/mark: "B" (−0.352 . 2.353)). Lily#
        //   read 3.505 while the padding stood on the top line.
        var shape = FirstLine(Book("""e,1 | e,1@mark("B") |"""));
        Assert.Equal(3.555, shape.RestUp, 3);
    }

    [Fact]
    public void AFlag_IsPricedByItsGlyphsBox_NotItsOutline()
    {
        // LILYPOND-REF: lily/flag.cc:183-196 Flag::internal_calc_y_offset — the flag's origin
        //   half a blot inside the stem's end; scm/define-grobs.scm Flag — its Y-extent is the
        //   stencil's, whose box tops out 0.065 over that origin.
        // MEASURED (2.26.0, Lab sessions/p856/net/f.lys, a bass tab of an unbeamed eighth on the
        //   A string): the line's rest top 2.275 over the top line, 0.025 over the stem's end;
        //   Lily# read 2.250 off the flag's outline. (Alone Again's tab lines: 2.275 / 2.250.)
        var shape = FirstLine("""
            octave absolute
            time 4/4

            part p {
              clef bass
              tuning bass
              section S { a,,8 r8 r4 r2 | a,,8 r8 r4 r2 | }
            }

            form { S }

            score {
              tab p
            }
            """);
        Assert.Equal(2.275, shape.RestUp, 3);
    }

    [Fact]
    public void AStaffsFlag_IsPricedByItsGlyphsBox_Too()
    {
        // The same rule on a five-line staff (MergeFlagInk's pureBox). MEASURED (2.26.0, Lab
        //   sessions/p856/net/n.lys): a down eighth's flag puts the line's rest bottom 1.400
        //   under the bottom line, 0.025 past the stem's end; the outline gave 1.375.
        var shape = FirstLine(Book("d,8 r8 r4 r2 | d,8 r8 r4 r2 |"));
        Assert.Equal(1.400, shape.RestDown, 3);
    }

    [Fact]
    public void AVoltaBracket_HasNoPureHeight()
    {
        // LILYPOND-REF: lily/system.cc:940-967 System::calc_pure_relevant_grobs — the
        //   VoltaBracketSpanner is an axis group, so it is in no line's pure heights.
        // MEASURED (2.26.0, Lab sessions/p856/net/v.lys): the line in the middle of a first
        //   ending prices the staff alone, rest top 0.050; Lily# gave the breaker the bracket's
        //   0.994 (ABC's tab lines under "1." and "2.": 0.050 / 1.586, 2.275 / 3.221).
        var shape = Line("""
            octave absolute
            time 4/4

            part p {
              clef bass
              section A { e,1 | }
              section E { e,1 | e,1 | break e,1 | e,1 | break e,1 | e,1 | break }
              section F { e,1 | e,1 | }
            }

            form { |: A [1. E] :| [2. F] }

            score {
              staff p
            }
            """, 2);
        Assert.Equal(0.050, shape.RestUp, 3);
        Assert.True(shape.BeginUp < 2.5, $"the bracket is in line 2's begin heights: {shape.BeginUp}");
    }

    [Fact]
    public void AMarkInsideALinesFirstBar_IsInItsMidHeightsAlone_NotOnTheLineBefore()
    {
        // LILYPOND-REF: lily/axis-group-interface.cc:417-458 adjacent_pure_heights — a grob on a
        //   musical column inside the bar has rank_span[LEFT] > start: no begin heights, and no
        //   interval of the line before reaches it.
        // MEASURED (2.26.0, Lab sessions/p854/fam1, まちぶせ line 7 `r4 d,@mark("B1")`):
        //   LilyPond's line 6 rest top 0.05, line 7 begin 3.825 / rest 5.075; Lily# priced the
        //   mark as opening line 7 (begin 5.100 / rest 2.545) and over line 6's end (3.555).
        const string music = """e,1 | e,1 | break r4 e,2.@mark("B") | e,1 |""";
        var first = Line(Book(music), 1);
        var second = Line(Book(music), 2);
        Assert.True(first.RestUp < 1.0, $"line 1 carries line 2's mid-bar mark: rest up {first.RestUp}");
        // The begin heights are the clef's (2.31); the mark would stand at 3.555.
        Assert.True(second.BeginUp < 3.0, $"the mid-bar mark is in line 2's begin heights: {second.BeginUp}");
        Assert.Equal(3.555, second.RestUp, 3);
    }

    [Fact]
    public void TheLineStartKeySignature_IsInTheStavesPureTop()
    {
        // MEASURED (2.26.0, Lab sessions/p853, もう恋なんてしない's twin, E major in the bass
        // clef): the staff's begin heights stand 1.0 over its top line at every bar — the
        // sharps. A line whose notes stay inside the staff therefore has its staves' pure top
        // (the line's origin in LilyPond's frame, SystemDetails.AlignmentOriginUp) there.
        const string source = """
            octave absolute
            time 4/4
            key e major

            part p {
              clef bass
              section S {
                e,4 e, e, e, | e,1 | break e,4 e, e, e, | e,1 |
              }
            }

            form { S }

            score {
              staff p
            }
            """;
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindAll(tree).First();
        var score = SvgGenerator.CollectScore(tree, spec);
        var log = new List<string>();
        int mine = Environment.CurrentManagedThreadId;
        LayoutEngine.DebugPageBreakingScoring = s =>
        {
            if (Environment.CurrentManagedThreadId == mine)
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
        var row = log.First(l => l.Contains("placed sys 2:", StringComparison.Ordinal));
        var m = Regex.Match(row, @"origin ([-\d.]+)");
        Assert.True(m.Success, row);
        Assert.Equal(1.0, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 2);
    }

    [Fact]
    public void ATabBeamsStems_ArePricedUnbeamed_InTheGroupsDirection()
    {
        // MEASURED (2.26.0, Lab sessions/p852/pr/tab, Billie Jean's riff on a four-string
        // bass): 5.625 over the tab's middle — the beam's up stems at their unbeamed length;
        // stemmed one by one, the riff's upper-string notes would point down.
        var shape = FirstLine("""
            octave absolute
            time 4/4

            part p {
              clef bass
              tuning bass
              section S {
                fis,,8 cis, e, fis, e, cis, b,, cis, | fis,,8 cis, e, fis, e, cis, b,, cis, |
              }
            }

            form { S }

            score {
              tab p
            }
            """);
        Assert.Equal(5.625 - 2.25, shape.RestUp, 6);
    }

    [Fact]
    public void ASlur_IsPricedAsItsNotesPlusHalfASpace_NotAsItsCurve()
    {
        // MEASURED (2.26.0, Lab sessions/p852/pr/slur): 6.545 over the middle line, the
        // slurred a8's head plus the pure estimate's 0.5; the drawn curve stood 0.48 higher.
        var shape = FirstLine(Book(
            "d,4. g,16 a, c4. d8~ | d4 r8 f8~ f4. d,8 | g4. a8 a2 | a8( g) f4 e8 a,4 a,,8 |"));
        Assert.Equal(6.545 - 2.0, shape.RestUp, 6);
    }

    [Fact]
    public void ATupletBracket_HasNoPureHeight()
    {
        // MEASURED (2.26.0, Lab sessions/p852/pr/tup2): (−3.333333, 3.0) about the middle
        // line — the stems; the bracket and its number under the down stems are not in the
        // pure heights.
        var shape = FirstLine(Book("tuplet 3/2 { c,4 d, e, } c,2 | c,1 |"));
        Assert.Equal(3.0 - 2.0, shape.RestUp, 6);
        Assert.Equal(10.0 / 3 - 2.0, shape.RestDown, 6);
    }

    [Fact]
    public void AMarkOpeningALine_IsInThatLinesBeginHeightsOnly()
    {
        // MEASURED (2.26.0, Lab sessions/p852/mk): a mark at a line's first bar is in the
        // System's begin heights of that bar alone. The line with the mark keeps the rest
        // height of the same line without it.
        var marked = Line(Book("c4 c c c | break c4@mark(\"A\") c c c | c4 c c c |"), 2);
        var plain = Line(Book("c4 c c c | break c4 c c c | c4 c c c |"), 2);
        Assert.True(marked.BeginUp > plain.BeginUp + 1, $"{marked} against {plain}");
        Assert.Equal(plain.RestUp, marked.RestUp, 9);
    }
}
