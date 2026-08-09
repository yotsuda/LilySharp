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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// tablature-tie-behaviour.ly: in tablature a tied-to note is invisible, EXCEPT
/// when its tie was split by a line break — there the fret prints in
/// parentheses — and a \repeatTie note prints parenthesized anywhere.
/// LP 2.26.0 oracle (tabtie-probe twin): the line-2 opener shows "(1)" — two
/// filled bow paths bulging 4/3 × 0.25 = 0.3333 outward, 0.2583 inner, control
/// stops 0.22/0.78 of the digit height — while the mid-line tie target prints
/// nothing at all.
/// LILYPOND-REF: scm/tablature.scm:186-224 tab-note-head::handle-ties.
/// </summary>
[Trait("Category", "Unit")]
public class TabSplitTieParenTests
{
    private const string SplitTieTwin = """
        octave absolute
        part m { clef treble_8 tuning guitar }
        section A {
          m {
            f2~ f4 e4 |
            c'1~ |
            break
            c'2~ c'2 |
          }
        }
        form main { A }
        score main { tab m as numbers }
        """;

    private const string RepeatTieTwin = """
        octave absolute
        part m { clef treble_8 tuning guitar }
        section A {
          m { c'2 c'2@repeatTie | }
        }
        form main { A }
        score main { tab m as numbers }
        """;

    private static List<(double X, double Y, string Text)> FretDigits(string svg)
    {
        var digits = new List<(double, double, string)>();
        foreach (Match m in Regex.Matches(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"3.00\" font-weight=\"bold\" text-anchor=\"middle\"[^>]*>(\\d+)</text>"))
            digits.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                m.Groups[3].Value));
        return digits;
    }

    private static int ParenBowCount(string svg) =>
        // A paren bow renders as a closed path of two cubics whose x-coordinates
        // stay within ~0.4 of each other (a tall thin crescent); ties are much
        // wider. Count paths whose total x-span is under 1.
        Regex.Matches(svg, "<path d=\"M ([-\\d.]+),[-\\d.]+ C[^\"]+Z\"")
            .Count(m =>
            {
                var xs = Regex.Matches(m.Value, "([-\\d.]+),")
                    .Select(x => double.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture))
                    .ToList();
                return xs.Max() - xs.Min() < 1.0;
            });

    [Fact]
    public void SplitTieTarget_ShowsParenthesizedFret_MidLineTargetHidden()
    {
        var svg = LiveRender.SvgFromRenderSpec(SplitTieTwin);
        var digits = FretDigits(svg);

        // Line 1: f2 (tied-to f4 hidden), e4, c'1. Line 2: ONLY the split-tie
        // opener — the mid-line tie target (second c'2) stays invisible.
        Assert.Equal(4, digits.Count);
        double line2Y = digits.Max(d => d.Y);
        Assert.Single(digits.Where(d => d.Y > line2Y - 3));

        // Its parentheses: exactly two thin bow paths.
        Assert.Equal(2, ParenBowCount(svg));
    }

    [Fact]
    public void RepeatTieNote_ShowsParenthesizedFret()
    {
        var svg = LiveRender.SvgFromRenderSpec(RepeatTieTwin);
        var digits = FretDigits(svg);

        // Both halves print (the \repeatTie one parenthesized, not hidden).
        Assert.Equal(2, digits.Count);
        Assert.Equal(2, ParenBowCount(svg));
    }
}
