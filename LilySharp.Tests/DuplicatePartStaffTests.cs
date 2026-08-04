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
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A score may put ONE part on more than one staff (<c>staff melody</c> written twice).
/// Such a score must lay out exactly like the same music written as that many separate
/// parts — the staves are different elements even though their contents are equal.
/// </summary>
/// <remarks>
/// THE DEFECT THIS PINS. <c>SkylineBuilder.BuildSystemSkylines</c> asks whether the system's
/// bottom edge staff is a different staff from its top edge one before seeding the bottom
/// one's ink and staff symbol into the system silhouette. <c>Staff</c> is a record, so the
/// question was asked with <c>!=</c> — VALUE equality — and one part on two staves builds
/// two Staff records whose fields are all equal, down to the same <c>Voices</c> array
/// instance (MeasureCollector keys its voice table by part name). The bottom edge then
/// seeded nothing: the system's DOWN silhouette was its TOP staff's, its down extent read
/// 0.000000 against the twin's 1.545000, the inter-system distance collapsed to the pair's
/// basic-distance, and the next system was drawn through this one's lower staff.
/// <para>
/// ⚠️ IT IS THE TWO ENDS, NOT "A DUPLICATE SOMEWHERE" — the theory cases say so in both
/// directions. <c>melody bass melody</c> was wrong and <c>bass melody melody</c> was right,
/// because only the outermost staves are compared. A future change that deduplicates staves
/// anywhere else has to keep both rows passing.
/// </para>
/// <para>
/// MEASURED IN STAFF LINES rather than in <c>SystemLayout.Y</c> on purpose: the two paging
/// paths store that field in different frames (device-down in <c>CreatePages</c>, page Y-up
/// in <c>PageLayouter</c>), so a test written against it would pass or fail on which regime
/// the paper put the score in. A drawn staff line has one frame.
/// </para>
/// </remarks>
public class DuplicatePartStaffTests
{
    private const string Music =
        "  clef treble\n" +
        "  section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n" +
        "  section B { g'4 g f f | break e e d2 | }\n";

    private static string Source(string parts, params string[] staves) =>
        string.Concat(parts.Split(' ').Distinct()
            .Select(p => $"part {p} {{\n{Music}}}\n\n"))
        + "form main { A |: B :| A }\n\n"
        + "score main {\n"
        + string.Concat(staves.Select(s => $"  staff {s}\n"))
        + "}\n";

    /// <summary>
    /// Every drawn staff line's Y, top of the page down. Staff lines are the horizontal
    /// rules that reach across the system; a ledger line is short and drops out by span.
    /// </summary>
    private static List<double> StaffLineYs(string svg)
    {
        var ys = new List<double>();
        foreach (Match m in Regex.Matches(
            svg, @"<line[^>]*x1=""([\d.]+)""[^>]*y1=""([\d.]+)""[^>]*x2=""([\d.]+)""[^>]*y2=""([\d.]+)"""))
        {
            double x1 = double.Parse(m.Groups[1].Value), y1 = double.Parse(m.Groups[2].Value);
            double x2 = double.Parse(m.Groups[3].Value), y2 = double.Parse(m.Groups[4].Value);
            if (y1 == y2 && x2 - x1 > 50)
                ys.Add(y1);
        }
        ys.Sort();
        return ys;
    }

    /// <summary>The staves, as (topLineY, bottomLineY) pairs: five lines one space apart.</summary>
    private static List<(double Top, double Bottom)> Staves(string svg)
    {
        var ys = StaffLineYs(svg).Distinct().ToList();
        var staves = new List<(double, double)>();
        for (int i = 0; i < ys.Count; i += 5)
        {
            // Five lines a space apart make a staff. A leftover run means two staves were
            // drawn INTO each other and their lines merged into one — which is the defect
            // itself, so say that rather than "grouping failed".
            Assert.True(i + 4 < ys.Count,
                "staff lines do not divide into whole staves, i.e. two staves overlap: "
                + $"[{string.Join(", ", ys)}]");
            staves.Add((ys[i], ys[i + 4]));
        }
        return staves;
    }

    [Theory]
    // The reported score: one part on two staves, against the same music as two parts.
    [InlineData("melody back", new[] { "melody", "melody" }, new[] { "melody", "back" })]
    // The duplicate at BOTH ENDS with a third staff between them — the shape that says the
    // comparison is of the ends and not of the set.
    [InlineData("melody back tenor",
        new[] { "melody", "back", "melody" }, new[] { "melody", "back", "tenor" })]
    // ...and its control: the same duplicate NOT at both ends was never affected.
    [InlineData("melody back tenor",
        new[] { "back", "melody", "melody" }, new[] { "back", "melody", "tenor" })]
    public void OnePartOnSeveralStaves_LaysOutLikeThatManyParts(
        string parts, string[] duplicated, string[] distinct)
    {
        var dup = Staves(LiveRender.SvgFromRenderSpec(Source(parts, duplicated)));
        var twin = Staves(LiveRender.SvgFromRenderSpec(Source(parts, distinct)));

        Assert.Equal(duplicated.Length * 2, dup.Count);   // two systems, every staff on each
        Assert.Equal(twin.Count, dup.Count);
        for (int i = 0; i < dup.Count; i++)
        {
            Assert.Equal(twin[i].Top, dup[i].Top, 9);
            Assert.Equal(twin[i].Bottom, dup[i].Bottom, 9);
        }
    }

    /// <summary>
    /// The same statement without a twin to compare against: whatever the distance works out
    /// to, a system may not be drawn through the one above it.
    /// </summary>
    [Fact]
    public void OnePartOnTwoStaves_SecondSystemClearsTheFirst()
    {
        var staves = Staves(LiveRender.SvgFromRenderSpec(
            Source("melody", "melody", "melody")));

        Assert.Equal(4, staves.Count);
        // staves[1] is the first system's lower staff, staves[2] the second system's upper.
        Assert.True(staves[2].Top > staves[1].Bottom,
            $"the second system's first staff (top {staves[2].Top:F3}) is drawn through the "
            + $"first system's last staff (bottom {staves[1].Bottom:F3})");
    }
}
