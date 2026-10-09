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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tab voice's spacing wish, which LilyPond merges with the staff's: its left head is
/// LilyPond's fret digit, and a numbers-only tab's wish takes no stem correction.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, LilySharp-Lab sessions/p576 — Never Stop bars 29-32, the
/// excerpt of session 370): each column's gap with the page ragged and wide, so every spring
/// sits at its ideal. The staff+tab column is the MEAN of the staff's wish (stem correction
/// included) and the numbers-only tab's (none); the tab alone, under \tabFullNotation, is
/// the digit-headed wish with the stems' correction. Until session 576 Lily# priced the tab
/// voice as a notehead with the staff's correction — the staff's own numbers on both.
/// </remarks>
public class TabSpacingWishTests
{
    private const string Bar = """
        e,,8. b,,16 e,16 b,, e,,8 r16 b,, e, b,, e,,4 |
        """;

    private static double[] Ideals(string score)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            key d major
            time 4/4
            part bassline {
              clef bass
              tuning bass
              section S { {{Bar}} }
            }
            form { S }
            {{score}}
            """);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var data = SystemBreaker.ComputeMultiStaffSpringData(
            multi, SpacingRules.CalculateCommonShortestDuration(multi));
        return data[0].Springs.Select(s => s.IdealDistance).ToArray();
    }

    [Fact]
    public void StaffAndNumbersTab_TakeTheMeanOfTheStaffWishAndAStemlessDigitWish()
    {
        var ideals = Ideals("score { staff bassline  tab bassline }");
        // s1 dotted 8th → 16th, s2/s3/s4 16ths, s5 8th → 8th (flagged: no correction on
        // either side, so it is the plain digit-for-notehead mean).
        Assert.Equal(4.4236, ideals[1], precision: 4);
        Assert.Equal(2.5217, ideals[2], precision: 4);
        Assert.Equal(2.2717, ideals[3], precision: 4);
        Assert.Equal(2.2717, ideals[4], precision: 4);
        Assert.Equal(3.5967, ideals[5], precision: 4);
    }

    /// <summary>
    /// The closing quarter → bar line is merged the same way: LilyPond's 4.9062 is
    /// (staff 5.0828 + numbers-only tab 4.7297) / 2, where the staff's wish alone — what Lily#
    /// took until session 576 — is 5.0828.
    /// </summary>
    /// <remarks>
    /// The tab's 4.7297 carries its own +0.0405: the numbers-only tab's zero-length stem
    /// stub still meets the bar line's opposite-direction correction (the up stub on the low
    /// E, 1.1347 positions inside the bar's ±3, over 7, times 0.5, halved). Until session 661
    /// Lily# gave that stub nothing and closed the bar at 4.885971, 0.020 short.
    /// </remarks>
    [Fact]
    public void StaffAndNumbersTab_CloseTheBarOnTheMergedWish()
    {
        var ideals = Ideals("score { staff bassline  tab bassline }");
        Assert.Equal(4.9062, ideals[^1], precision: 4);
    }

    /// <summary>
    /// A numbers-only tab's quarters take the stem corrections of their zero-length stubs:
    /// one open string to the next up is +0.25, down −0.25, and the bar line ±0.0405 by the
    /// stub's direction. MEASURED (2.26.0, LilySharp-Lab sessions/p661/tabstem/q.ly, bass tab
    /// alone, NoteColumn and BarLine X with the line ragged): 2.7872 / 3.0372 / 2.7872 /
    /// 2.8277 and 2.7872 / 2.5372 / 2.7872 / 2.7467; the bar into a top-string quarter 0.0486
    /// wider than into a bottom-string one (the staff-spacing optical correction).
    /// </summary>
    [Fact]
    public void NumbersTabQuarters_TakeTheirStubsCorrections()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part bassline {
              clef bass
              tuning bass
              section S { e,,4 e,, a,, a,, | g, g, d, d, | }
            }
            form { S }
            score { tab bassline as numbers }
            """);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var data = SystemBreaker.ComputeMultiStaffSpringData(
            multi, SpacingRules.CalculateCommonShortestDuration(multi));
        double[] first = data[0].Springs.Select(s => s.IdealDistance).ToArray();
        double[] second = data[1].Springs.Select(s => s.IdealDistance).ToArray();
        Assert.Equal([2.7872, 3.0372, 2.7872, 2.8277], first[1..].Select(x => Math.Round(x, 4)));
        Assert.Equal([2.7872, 2.5372, 2.7872, 2.7467], second[1..].Select(x => Math.Round(x, 4)));
        Assert.Equal(0.0486, second[0] - first[0], precision: 4);
    }

    /// <summary>
    /// A tab chord's wish reads its FIRST head — the digit opposite the stem — not its widest
    /// fret: both chords here pair a two-digit fret with an open string on the far side.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, LilySharp-Lab sessions/p661/chordhead: c.ly, cf.ly, cs.ly — the
    /// chord's column to the next quarter's): numbers-only 2.787216 / 2.748759,
    /// <c>\tabFullNotation</c> 2.787216 / 2.663588, staff + numbers-only tab 2.658031 /
    /// 2.696931. Until session 661 the up chord read its "15" and stood 0.495078 wider.
    /// </remarks>
    [Theory]
    [InlineData("tab bassline as numbers", 2.787216, 2.748759)]
    [InlineData("tab bassline", 2.787216, 2.663588)]
    [InlineData("staff bassline  tab bassline", 2.658031, 2.696931)]
    public void TabChord_WishReadsTheFirstHeadsDigit(string staves, double upChord, double downChord)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part bassline {
              clef bass
              tuning bass
              section S { <e,,\4 c\3>4 e,,\4 e,,\4 e,,\4 | <c\2 g,\1>4 e,,\4 e,,\4 e,,\4 | }
            }
            form { S }
            score { {{staves}} }
            """);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var data = SystemBreaker.ComputeMultiStaffSpringData(
            multi, SpacingRules.CalculateCommonShortestDuration(multi));
        Assert.Equal(upChord, data[0].Springs[1].IdealDistance, precision: 5);
        Assert.Equal(downChord, data[1].Springs[1].IdealDistance, precision: 5);
    }

    /// <summary>
    /// A chord's stub begins at its STEM-SIDE digit (LilyPond's reference head under the
    /// TabStaff's avoid-note-head): <c>&lt;e,, d,&gt;4</c> stems up from the d, over (2.1346,
    /// 3.2692) positions, so it meets the bar line's ±3 over 0.8654 where the lone e,, meets
    /// it over 1.1347. MEASURED (2.26.0, LilySharp-Lab sessions/p661/tabstem/qc.ly): 2.8181
    /// into the bar against 2.8277.
    /// </summary>
    [Fact]
    public void NumbersTabChord_StubBeginsAtTheStemSideDigit()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part bassline {
              clef bass
              tuning bass
              section S { <e,, d,>4 <e,, d,> <e,, d,> <e,, d,> | e,,4 e,, e,, e,, | }
            }
            form { S }
            score { tab bassline as numbers }
            """);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var data = SystemBreaker.ComputeMultiStaffSpringData(
            multi, SpacingRules.CalculateCommonShortestDuration(multi));
        Assert.Equal(2.8181, data[0].Springs[^1].IdealDistance, precision: 4);
        Assert.Equal(2.8277, data[1].Springs[^1].IdealDistance, precision: 4);
    }

    [Fact]
    public void FullNotationTabAlone_ReadsTheDigitAndKeepsTheStemCorrection()
    {
        var ideals = Ideals("score { tab bassline }");
        Assert.Equal(4.4411, ideals[1], precision: 4);
        Assert.Equal(2.5392, ideals[2], precision: 4);
        Assert.Equal(2.0392, ideals[3], precision: 4);
        Assert.Equal(2.0392, ideals[4], precision: 4);
        Assert.Equal(3.4892, ideals[5], precision: 4);
    }
}
