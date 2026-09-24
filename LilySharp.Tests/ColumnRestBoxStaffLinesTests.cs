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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The column view's box for a rest stands where the staff draws the rest — the staff's
/// own line count, not five lines whatever the staff (session 557, HANDOFF ⒳¹⁷; the drawing
/// and the vertical seed took the staff's lines in session 536, ReducedStaffRestTests).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/rest.cc:90-129 staff_position_internal — the rest's neutral position
/// is read off the staff's line-positions: a whole rest hangs from the first line above the
/// middle (on one line, the line itself), a half sits on the last line at or below it (on
/// the timbales pair, the lower line).
/// LILYPOND-REF: lily/separation-item.cc:163 Separation_item::boxes — the column box is the
/// grob's extent at that position. The claims are ABSOLUTE offsets between staves, and
/// the poison (the five-line letter for every staff) moves the one- and two-line bands onto
/// the five-line ones.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ColumnRestBoxStaffLinesTests
{
    private static RestItem Rest(int noteValue) => new(Fraction.FromNoteValue(noteValue), 0, 0);

    [Fact]
    public void AWholeRestOnOneLine_IsBoxedASpaceLowerThanOnFive()
    {
        var five = ItemSkylineFactory.ColumnYExtent(Rest(1), staffY: 0, staffLines: 5);
        var one = ItemSkylineFactory.ColumnYExtent(Rest(1), staffY: 0, staffLines: 1);
        // Five lines: the whole rest hangs from the line above the middle, one space up
        // (a negative Y is up). One line: from the line itself.
        Assert.Equal(five.YMin + 1.0, one.YMin, 9);
        Assert.Equal(five.YMax + 1.0, one.YMax, 9);
    }

    [Fact]
    public void AHalfRestOnTheTimbalesPair_IsBoxedASpaceLowerThanOnFive()
    {
        var five = ItemSkylineFactory.ColumnYExtent(Rest(2), staffY: 0, staffLines: 5);
        var two = ItemSkylineFactory.ColumnYExtent(Rest(2), staffY: 0, staffLines: 2);
        // Five lines: the half rest sits on the middle line. Two lines (±2): on the lower one.
        Assert.Equal(five.YMin + 1.0, two.YMin, 9);
        Assert.Equal(five.YMax + 1.0, two.YMax, 9);
    }

    [Fact]
    public void AQuarterRest_IsBoxedTheSameOnEveryStaff()
    {
        // The control: nothing shorter than a half is aligned to a line (rest.cc:79-81), so the
        // box is the staff's middle whatever the lines.
        var five = ItemSkylineFactory.ColumnYExtent(Rest(4), staffY: 0, staffLines: 5);
        foreach (int lines in new[] { 1, 2, 3, 4 })
        {
            var other = ItemSkylineFactory.ColumnYExtent(Rest(4), staffY: 0, staffLines: lines);
            Assert.Equal(five, other);
        }
    }
}
