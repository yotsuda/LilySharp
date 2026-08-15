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
/// A tab stem leaves the AXIS its fret digits are placed around — the note column's
/// digit centre — and a chord's zigzag straddles it, exactly as a notation chord's
/// staggered noteheads straddle their stem.
/// </summary>
/// <remarks>
/// ⚠️ THE SNAPSHOTS CANNOT HOLD THIS DOWN. Twenty-two of them move when the rule
/// changes, and every one can be rebased — approving a picture is not observing a rule
/// (RULES §5.0). What the rule says is a relation between two numbers in the same
/// drawing, so it is asserted as one.
/// <para>
/// WHY THE AXIS. The stem is the only mark that ties a chord's digits — scattered over
/// several string lines and, at this digit size, over two columns — into one sounding.
/// A stem leaving the axis belongs to all of them; one leaving an edge or a single
/// column belongs to what it touches, and the chord stops reading as simultaneous
/// (user, 2026-08-16).
/// </para>
/// <para>
/// MEASURED, and the reason the old rule went: the tab stem used to stand at the
/// companion NOTATION staff's stem x (the notehead edge). ⑴ That alignment only held
/// when both staves picked the same stem direction — where they differ, notation 9.66
/// against tab 8.48, 1.18 apart where the axis leaves 0.59. ⑵ On a chord the x and the
/// y then came from different digits: on <c>&lt;e a d' g'&gt;</c> the y was taken from
/// the bottom digit (column 7.41) while the x stood at 5.87.
/// LilyPond centres its own tab stem on the digit in both directions (2.26.0: head
/// 8.62 + ink centre 0.594 = 9.214093543307087 for dir=1 and dir=-1 alike).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TabStemAxisTests
{
    private static (List<(double X, double Y, string Text)> Digits, List<double> StemXs) Read(string svg)
    {
        var digits = new List<(double, double, string)>();
        foreach (Match m in Regex.Matches(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"[\\d.]+\" font-weight=\"bold\""
            + " text-anchor=\"middle\"[^>]*>(\\d+)</text>"))
            digits.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                m.Groups[3].Value));

        // A stem is a thin VERTICAL line (x1 == x2); the staff's string lines are
        // horizontal and the bar lines are thicker.
        var stems = new List<double>();
        foreach (Match m in Regex.Matches(svg,
            "<line x1=\"([-\\d.]+)\" y1=\"[-\\d.]+\" x2=\"([-\\d.]+)\" y2=\"[-\\d.]+\""
            + "[^>]*stroke-width=\"([\\d.]+)\""))
        {
            if (m.Groups[1].Value != m.Groups[2].Value) continue;
            if (double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) >= 0.2) continue;
            stems.Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return (digits, stems);
    }

    private static string Book(string music) => $$"""
        octave absolute
        time 4/4
        part m { clef treble_8 tuning guitar }
        section A { m { {{music}} } }
        form main { A }
        score main { tab m }
        """;

    [Fact]
    public void ASingleNotesStem_LeavesTheDigitsOwnX()
    {
        var (digits, stems) = Read(LiveRender.SvgFromRenderSpec(Book("g4 g4 g4 g4 |")));

        Assert.Equal(4, digits.Count);
        Assert.Equal(4, stems.Count);
        foreach (var (digit, stem) in digits.OrderBy(d => d.X).Zip(stems.OrderBy(x => x)))
            Assert.Equal(digit.X, stem, 2);
    }

    /// <summary>
    /// The zigzag case: two columns of digits, one stem, and the stem between them.
    /// </summary>
    [Fact]
    public void AChordsStem_LeavesTheAxisTheZigzagStraddles()
    {
        var (digits, stems) = Read(LiveRender.SvgFromRenderSpec(Book("<e a d' g'>4 r4 r2 |")));

        // Four adjacent strings zigzag into exactly two columns.
        var columns = digits.Select(d => Math.Round(d.X, 2)).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(4, digits.Count);
        Assert.Equal(2, columns.Count);

        double stem = Assert.Single(stems);
        // The stem is the axis: equidistant from the two columns, and BETWEEN them —
        // so both are read as hanging off it, rather than one of them owning it.
        // ⚠️ A tolerance, not a decimal count: the axis here is 6.455 and the SVG writes
        // coordinates to two places (SvgGenerator's F2), so the drawn 6.45 and the
        // computed 6.455 round to different second decimals (RULES §5.3 — do not take
        // precise measurements off an SVG). Half a hundredth is far tighter than the
        // 0.955 this test is separating the answers by.
        Assert.InRange(stem, (columns[0] + columns[1]) / 2 - 0.01, (columns[0] + columns[1]) / 2 + 0.01);
        Assert.True(columns[0] < stem && stem < columns[1],
            $"the stem ({stem}) must stand between the columns ({columns[0]}, {columns[1]})");
    }
}
