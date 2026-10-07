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

        form main { S }

        score main {
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

            form main { S }

            score main {
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

            form main { S }

            score main {
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
