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

    /// <summary>
    /// A bar opening with a grace run: the crossing to the first MAIN head is the same over a
    /// silent staff as over a sounding one. MEASURED (session 1373, Lab sessions/p1373/wish
    /// g0–g6): 5.023 (one grace) and 5.929 (two), with the lower staff sounding, silent or
    /// resting.
    /// </summary>
    [Theory]
    [InlineData("grace { c''16 } e''4 e'' e'' e'' |", "lh { c1 | c1 | }", 5.023)]
    [InlineData("grace { c''16 } e''4 e'' e'' e'' |", "lh { s1 | s1 | }", 5.023)]
    [InlineData("grace { c''16 } e''4 e'' e'' e'' |", "lh { c1 | R1 | }", 5.023)]
    [InlineData("grace { c''8 d''8 } e''4 e'' e'' e'' |", "lh { c1 | c1 | }", 5.929)]
    [InlineData("grace { c''8 d''8 } e''4 e'' e'' e'' |", "lh { s1 | s1 | }", 5.929)]
    public void ABarOpeningWithAGraceRun_LosesNothingToASilentStaff(string bar2, string lower, double crossing)
    {
        string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\npart lh { clef bass }"
            + "\nsection A {\n  rh { e''4 e'' e'' e'' | " + bar2 + " }\n  " + lower
            + "\n}\nform { A }\nscore { staff rh staff lh }\n";
        // The upper staff's heads by x: bar 1's four, the graces, then bar 2's four.
        var heads = Regex.Matches(TwinBowSweep.Dump(src), @"^HEAD \S+ 0 x=([-\d.]+)", RegexOptions.Multiline)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(x => x).ToList();
        int graces = heads.Count - 8;
        Assert.InRange(graces, 1, 2);
        // 0.01: the two-grace run is 5.920 here (0.009 short of LilyPond either way).
        Assert.Equal(crossing, heads[4 + graces] - heads[3], 0.01);
    }

    /// <summary>
    /// A line OPENING WITH <c>|:</c> earns the down-stem correction off that bar line (the
    /// only line start that does), and a silent staff's wish carries none of it there too —
    /// the first head of the line stands the silent staff's share closer. A line start
    /// without a bar line has no correction to lose.
    /// MEASURED (session 1373, Lab sessions/p1373/wish r1/r2 and l1/l2): the second line's
    /// first head 7.513 over <c>c1</c>, 7.418 over <c>s1</c> (− 0.19 / 2); 5.800 both ways
    /// without the <c>|:</c>. Until session 1373 Lily# put both at the same place.
    /// </summary>
    [Theory]
    [InlineData("e''4 e'' e'' e'' break |: e''4 e'' e'' e'' :|", "c1 break |: c1 :|", "s1 break |: s1 :|", 0.095)]
    [InlineData("e''4 e'' e'' e'' | break e''4 e'' e'' e'' |", "c1 | c1 |", "s1 | s1 |", 0.0)]
    public void ALineOpeningWithARepeat_LosesTheSilentStaffsShare(string upper, string sounding, string silent, double share)
    {
        double First(string lower)
        {
            string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\npart lh { clef bass }"
                + "\nsection A {\n  rh { " + upper + " }\n  lh { " + lower
                + " }\n}\nform { A }\nscore { staff rh staff lh }\n";
            // Staff 2 is the second line's upper staff.
            var heads = Regex.Matches(TwinBowSweep.Dump(src), @"^HEAD \S+ 2 x=([-\d.]+)", RegexOptions.Multiline)
                .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .OrderBy(x => x).ToList();
            Assert.Equal(4, heads.Count);
            return heads[0];
        }
        Assert.Equal(share, First(sounding) - First(silent), 0.007);
    }
}
