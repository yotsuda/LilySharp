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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A staff with no note and no rest between a bar line and its own next break-aligned grob —
/// a bar of spacers, a multi-measure rest, a percent repeat's bar — files a bar-line wish
/// WITHOUT the down-stem correction, and the merge averages it in: the bar opens narrower by
/// that staff's share of the correction (SpacingRules.IsSilentToItsNextChange).
/// LILYPOND-REF: lily/separating-line-group-engraver.cc:95-110 Separating_line_group_engraver::acknowledge_item;
/// LILYPOND-REF: lily/staff-spacing.cc:95-110 Staff_spacing::next_notes_correction.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1236/bar — probe.ps1, probe2.ps1, probe3.ps1 write
/// each book, its <c>lysc ly</c> twin and the head x of both sides; raggedRight): an upper staff
/// of quarters, the distance from the last head of bar 1 to the first of bar 2. Until session
/// 1236 every row with down stems above read 4.04 (the percent row 4.24).
/// </remarks>
[Trait("Category", "Unit")]
public class SilentStaffBarlineWishTests
{
    private const string Down = "e''4 e'' e'' e'' | e''4 e'' e'' e'' |";
    private const string Up = "e'4 e' e' e' | e'4 e' e' e' |";

    [Theory]
    // Alone, and beside a staff that sounds in bar 2: the whole correction.
    [InlineData(Down, "", "", "staff rh", 4.045)]
    [InlineData(Down, "part lh { clef bass }", "lh { c1 | c1 | }", "staff rh staff lh", 4.045)]
    [InlineData(Down, "part lh { clef bass }", "lh { c1 | r1 | }", "staff rh staff lh", 4.045)]
    // …anywhere in bar 2, in any of its voices.
    [InlineData(Down, "part lh { clef bass }", "lh { c1 | s2 c2 | }", "staff rh staff lh", 4.045)]
    [InlineData(Down, "part lh { clef bass }", "lh { c1 | s2 r2 | }", "staff rh staff lh", 4.045)]
    [InlineData(Down, "part lh { clef bass }", "lh { voice { c1 | s1 | } { s1 | c,1 | } }", "staff rh staff lh", 4.045)]
    // A silent staff: half the correction is gone — spacers, a multi-measure rest, on a grand
    // staff or two staves.
    [InlineData(Down, "part lh { clef bass }", "lh { s1 | s1 | }", "staff rh staff lh", 3.950)]
    [InlineData(Down, "part lh { clef bass }", "lh { s1 | s1 | }", "grandStaff { staff rh staff lh }", 3.950)]
    [InlineData(Down, "part lh { clef bass }", "lh { R1 | R1 | }", "staff rh staff lh", 3.950)]
    [InlineData(Down, "part lh { clef bass }", "lh { c1 | s1 | }", "staff rh staff lh", 3.950)]
    // …and silent up to a clef inside the bar: the note after it is the clef's column's.
    [InlineData(Down, "part lh { clef bass }", "lh { c1 | s2 clef treble c'2 | }", "staff rh staff lh", 3.950)]
    // Two silent staves of three: two thirds of it.
    [InlineData(Down, "part lh { clef bass } part b { clef bass }", "lh { s1 | s1 | } b { s1 | s1 | }", "staff rh staff lh staff b", 3.919)]
    // A percent repeat's bar is silent too (the wider crossing is bar 1's last column).
    [InlineData(Down, "part lh { clef bass }", "lh { c4 c c c | % | }", "staff rh staff lh", 4.151)]
    // Up stems earn no correction, so there is nothing to lose.
    [InlineData(Up, "", "", "staff rh", 4.329)]
    [InlineData(Up, "part lh { clef bass }", "lh { s1 | s1 | }", "staff rh staff lh", 4.329)]
    public void ASilentStaffsWish_CarriesNoStemCorrection(string upper, string parts, string lower, string score, double crossing)
    {
        string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\n" + parts
            + "\nsection A {\n  rh { " + upper + " }\n  " + lower + "\n}\nform { A }\nscore { " + score + " }\n";
        var heads = Regex.Matches(TwinBowSweep.Dump(src), @"^HEAD \S+ 0 x=([-\d.]+)", RegexOptions.Multiline)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(x => x).ToList();
        Assert.Equal(8, heads.Count);
        // 0.003: a bar line spanning staves is 0.05 longer in LilyPond and moves a crossing by up
        // to 0.0029 (session 739, not ported); the figures are LilyPond's to three places.
        Assert.Equal(crossing, heads[4] - heads[3], 0.003);
    }
}
