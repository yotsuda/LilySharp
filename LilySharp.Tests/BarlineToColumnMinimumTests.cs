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
using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The minimum a bar line keeps from the note column after it is a SKYLINE distance: the bar
/// line's box spans the staff and reaches at most 1.01 past it toward the column, so an
/// accidental hanging wholly beyond that — a sharp on F#3, a flat on E-flat 3, a sharp high
/// above the staff — stands under or over it and takes no room, and the head's own box
/// (grown to its first ledger line) is what meets the bar line.
/// LILYPOND-REF: lily/staff-spacing.cc:210 Staff_spacing::get_spacing — <c>Paper_column::minimum_distance</c>;
/// LILYPOND-REF: lily/paper-column.cc:145-164 Paper_column::minimum_distance;
/// LILYPOND-REF: scm/output-lib.scm:965-974 pure-from-neighbor-interface::account-for-span-bar.
/// </summary>
[Trait("Category", "Unit")]
public class BarlineToColumnMinimumTests
{
    /// <summary>MEASURED, LilyPond 2.26.0 (Lab sessions/p693/bl and bl2, the <c>lysc ly</c> twins
    /// of these books through sessions/p647/bows/bowdump.ily, raggedRight): the last quarter of
    /// bar 1 → the first head of bar 2. Until session 693 Lily# priced this pair along X alone and
    /// put every accidental in front: fis, 5.077, ees, 4.897, fis''' 5.077.</summary>
    [Theory]
    [InlineData("f,4", 3.926973)]          // control: a head on a ledger line, no accidental
    [InlineData("fis,4", 3.926973)]        // the sharp hangs below the box
    [InlineData("gis,4", 5.076973)]        // one step higher it reaches in
    [InlineData("ees,4", 3.926973)]        // a flat below the box
    [InlineData("aes,4", 4.896973)]        // …and reaching in
    [InlineData("c'''4", 4.041259)]        // control above the staff
    [InlineData("fis'''4", 4.041259)]      // the sharp stands over the box
    [InlineData("<fis, c'>4", 3.926973)]   // a chord's low sharp, its other head in the staff
    [InlineData("<fis, fis'>4", 5.076973)] // …and a sharp in the staff: that one takes the room
    public void AnAccidentalClearOfTheBarLinesBox_TakesNoRoomAfterIt(string opening, double gap)
    {
        string src = $$"""
            octave absolute
            time 4/4
            paper { raggedRight }
            part m {
              section A {
                c'4 c' c' c'4 |
                {{opening}} c' c' c' |
              }
            }
            form main { A }
            score main {
              staff m
            }
            """;
        var heads = HeadColumns(TwinBowSweep.Dump(src));
        Assert.Equal(8, heads.Count);
        Assert.Equal(gap, heads[4] - heads[3], 0.001);
    }

    /// <summary>The distinct head x of the dump, left to right (a chord's heads share one).</summary>
    private static List<double> HeadColumns(string dump)
    {
        var xs = new List<double>();
        foreach (Match m in Regex.Matches(dump, @"^HEAD \S+ \S+ x=([-\d.]+)", RegexOptions.Multiline))
        {
            double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (xs.Count == 0 || System.Math.Abs(xs[^1] - x) > 1e-6)
                xs.Add(x);
        }
        return xs;
    }
}
