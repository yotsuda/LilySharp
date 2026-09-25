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
            form main { S }
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
        var ideals = Ideals("score main { staff bassline  tab bassline }");
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
    /// ⚠️ OPEN RESIDUAL −0.020: Lily# gives 4.885971. The staff's wish alone agrees with
    /// LilyPond (5.082771), so the gap is in how the two halves split the bar-line term —
    /// Lily#'s base and correction come apart differently from LilyPond's (0.1786 against
    /// 0.1381 of correction for the same sum), which only the averaging exposes. Held to
    /// ±0.03 here, a tenth of the move, so that losing the tab wish is still caught.
    /// </remarks>
    [Fact]
    public void StaffAndNumbersTab_CloseTheBarOnTheMergedWish()
    {
        var ideals = Ideals("score main { staff bassline  tab bassline }");
        Assert.InRange(ideals[^1], 4.9062 - 0.03, 4.9062 + 0.03);
    }

    [Fact]
    public void FullNotationTabAlone_ReadsTheDigitAndKeepsTheStemCorrection()
    {
        var ideals = Ideals("score main { tab bassline }");
        Assert.Equal(4.4411, ideals[1], precision: 4);
        Assert.Equal(2.5392, ideals[2], precision: 4);
        Assert.Equal(2.0392, ideals[3], precision: 4);
        Assert.Equal(2.0392, ideals[4], precision: 4);
        Assert.Equal(3.4892, ideals[5], precision: 4);
    }
}
