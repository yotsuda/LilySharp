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
/// A tuplet's bracket reaches a note <c>@cross</c> moved where it stands, on the other staff
/// (owner's decision 2026-11-22, "the same as LilyPond"; TupletBracketEngraver.CalculateSlope).
/// A moved REST stays out of it (CrossStaffRestTests, owner's decision 2026-11-21).
/// </summary>
public class CrossStaffTupletBracketTests
{
    private static string Book(string treble) => $$"""
        octave absolute
        time 4/4
        paper { raggedRight }
        part rh { clef treble }
        part lh { clef bass }
        section A { rh { c''4 tuplet 3/2 { {{treble}} } c''4 | } lh { c1 | } }
        form { A }
        score { grandStaff { staff rh staff lh } }
        """;

    /// <summary>
    /// The bracket under the right hand's tuplet, in spaces above the treble's middle line. A
    /// moved note takes it under that note's stem, into the left hand's staff, flat (the moved
    /// head's position and the unmoved one's disagree with the drawn slope); a moved FIRST note
    /// makes it the left hand's bracket, and a flat bracket steps off that staff's lines. Every
    /// pin is LilyPond 2.26.0's line, its ink less half the 0.16 thickness, within the 0.005 the
    /// two staff distances differ by (Lab sessions/p1357/tb x0, sessions/p1358/tb x1–x5). Until
    /// session 1358 the moved column was read on its own staff and the bracket ran through the
    /// moved head (x0: −8.73 to −11.28, sloped).
    /// LILYPOND-REF: lily/tuplet-bracket.cc:554-562 calc_position_and_height (commony);
    ///   :530-549 the slope bounds; :726-745 the staff-line step.
    /// </summary>
    [Theory]
    [InlineData("c''4@cross c'' c''", -11.6)]
    [InlineData("c''4 c''@cross c''", -11.2)]
    [InlineData("c''4 c'' c''@cross", -11.2)]
    [InlineData("a'4@cross b' c''", -10.6)]
    [InlineData("e'4@cross c'' c''", -10.5)]
    public void ABracketReachesAMovedNote_OnTheStaffItStandsOn(string treble, double expected)
    {
        var svg = TestPaper.SvgFromRenderSpec(Book(treble));
        var lines = Regex.Matches(svg,
                "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0\\.100\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(y => y).ToList();
        Assert.Equal(10, lines.Count);
        var bracket = Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"\\2\" stroke=\"#000000\" stroke-width=\"0\\.160\" data-pos")
            .Select(m => Math.Round(lines[2] - double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), 2))
            .Distinct().ToList();
        Assert.Equal(expected, Assert.Single(bracket));
    }
}
