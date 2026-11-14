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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A head shifted by a collision moves the ideal of its column pair by its shift over ALL the
/// wishes of the pair — the system's, every staff's voices that have an item on both columns —
/// because a paper column is the system's and <c>merge_springs</c> averages what every voice
/// filed on it (SpacingRules.ColumnWishes).
/// LILYPOND-REF: lily/spacing-spanner.cc:300-381 musical_column_spacing;
/// LILYPOND-REF: lily/spring.cc:101-129 merge_springs.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1235/wish — probe.ps1 writes each book, its
/// <c>lysc ly</c> twin and the head x of both sides; raggedRight): an upper staff of two voices
/// a second apart in quarters, so one head of every column stands 1.3042 right, beside the
/// staves of each row. The gap is column 1 → column 2 of bar 1, the crossing the last column
/// of bar 1 → the first of bar 2 (the closing spring carries the same average). Until session
/// 1235 each staff divided its shifts by its own wishes: every row but the first, the half
/// notes' and the spacers' was 3.65 (the rests' 3.54), and the last one 4.31.
/// </remarks>
[Trait("Category", "Unit")]
public class SystemColumnWishesTests
{
    private const string Upper =
        "voice { e''4 e'' e'' e'' | e''4 e'' e'' e'' | } { d''4 d'' d'' d'' | d''4 d'' d'' d'' | }";

    [Theory]
    // Alone: the shift over the staff's two wishes.
    [InlineData("", "", "staff rh", 3.6544, 4.8158)]
    // A second staff of quarters: three wishes.
    [InlineData("part lh { clef bass }", "lh { c4 c c c | c4 c c c | }", "grandStaff { staff rh staff lh }", 3.4370, 4.6847)]
    // Half notes span no pair of quarter columns: two again.
    [InlineData("part lh { clef bass }", "lh { c2 c2 | c2 c2 | }", "grandStaff { staff rh staff lh }", 3.6544, 4.8155)]
    // A rest files a wish as a note does (and its narrower box is in the average).
    [InlineData("part lh { clef bass }", "lh { r4 r r r | r4 r r r | }", "grandStaff { staff rh staff lh }", 3.3189, 4.5104)]
    // Three staves, and a staff of two voices that do not collide: four wishes.
    [InlineData("part lh { clef bass } part b { clef bass }",
        "lh { c4 c c c | c4 c c c | } b { c4 c c c | c4 c c c | }", "staffGroup { staff rh staff lh staff b }", 3.3283, 4.6199)]
    [InlineData("part lh { clef bass }",
        "lh { voice { c4 c c c | c4 c c c | } { e,4 e, e, e, | e,4 e, e, e, | } }", "grandStaff { staff rh staff lh }", 3.3283, 4.5772)]
    // Both staves with a second: two shifts over four wishes — not each staff's half, twice.
    [InlineData("part lh { clef bass }",
        "lh { voice { e4 e e e | e4 e e e | } { d4 d d d | d4 d d d | } }", "grandStaff { staff rh staff lh }", 3.6544, 4.8530)]
    // A tab staff's voice is one of the column's too.
    [InlineData("part b { clef bass }", "b { e,4 e, e, e, | e,4 e, e, e, | }", "staff rh tab b", 3.3653, 4.6407)]
    public void AShiftIsAveragedOverTheSystemsWishes(string parts, string lower, string score, double gap, double crossing)
    {
        string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\n" + parts
            + "\nsection A {\n  rh { " + Upper + " }\n  " + lower + "\n}\nform { A }\nscore { " + score + " }\n";
        var columns = UpperColumns(TwinBowSweep.Dump(src));
        Assert.Equal(8, columns.Count);
        Assert.Equal(gap, columns[1] - columns[0], 0.001);
        // 0.003: a bar line spanning staves is 0.05 longer in LilyPond and moves a crossing by up
        // to 0.0029 (scm/bar-line.scm widen-bar-extent-on-span — priced in session 739 and not
        // ported); the single staff's crossing is within 0.001.
        Assert.Equal(crossing, columns[4] - columns[3], 0.003);
    }

    /// <summary>The left head of each column of the first staff, left to right (the shifted
    /// head of a column stands a head width right of it).</summary>
    private static List<double> UpperColumns(string dump)
    {
        var xs = Regex.Matches(dump, @"^HEAD \S+ 0 x=([-\d.]+)", RegexOptions.Multiline)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(x => x).ToList();
        var columns = new List<double>();
        foreach (double x in xs)
            if (columns.Count == 0 || x - columns[^1] > 2.0)
                columns.Add(x);
        return columns;
    }
}
