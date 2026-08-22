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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chord row's measure-relative slot grid (GRAMMAR_AUDIT 8.1): a bar's
/// written slots divide it on the meter's OWN beat grid — the same
/// <c>BeamingPattern.Options</c> structure the melody's beams are grouped by,
/// so the chord grid and the beam grid can never disagree. One slot = the bar;
/// slots = beats sit one per beat; a multiple k splits each beat into k; a
/// divisor groups whole beats; anything else is null (the caller divides
/// equally and warns, LYS2009).
/// </summary>
[Trait("Category", "Unit")]
public class ChordRhythmTests
{
    [Theory]
    [InlineData(1, new[] { "1" })]
    [InlineData(2, new[] { "1/2", "1/2" })]
    [InlineData(4, new[] { "1/4", "1/4", "1/4", "1/4" })]
    [InlineData(8, new[] { "1/8", "1/8", "1/8", "1/8", "1/8", "1/8", "1/8", "1/8" })]
    public void SlotDurations_44_SitsOnTheBeatGrid(int count, string[] expected)
    {
        var durs = ChordRhythm.SlotDurations(count, 4, 4);
        Assert.NotNull(durs);
        Assert.Equal(expected, durs!.Value.Select(FractionString).ToArray());

        // Each row fills exactly one whole-note measure.
        var sum = durs.Value.Aggregate(Fraction.Zero, (a, b) => a + b);
        Assert.Equal(Fraction.Whole, sum);
    }

    [Fact]
    public void SlotDurations_OffTheGrid_IsNull()
    {
        // 3 slots in 4/4 match no beat: not 1, not a divisor of 4, not a
        // multiple. (The rejected "equal division" design would have allowed
        // a 1/3-of-a-whole slot here — GRAMMAR_AUDIT 8.1's 落選 list.)
        Assert.Null(ChordRhythm.SlotDurations(3, 4, 4));
        Assert.Null(ChordRhythm.SlotDurations(5, 4, 4));
        Assert.Null(ChordRhythm.SlotDurations(0, 4, 4));
    }

    [Fact]
    public void SlotDurations_68_BeatsAreDottedQuarters()
    {
        // 6/8 has TWO beats ([3,3] eighths), not six: two slots are the two
        // dotted quarters, and 6 slots subdivide each beat in three.
        Assert.Equal(new[] { "3/8", "3/8" },
            ChordRhythm.SlotDurations(2, 6, 8)!.Value.Select(FractionString).ToArray());
        Assert.Equal(6, ChordRhythm.SlotDurations(6, 6, 8)!.Value.Length);
        Assert.Null(ChordRhythm.SlotDurations(3, 6, 8)); // 3 fits neither 1, 2, 4, 6…
    }

    [Fact]
    public void SlotDurations_58_KeepsLilyPondsUnevenGroups()
    {
        // 5/8 is [3,2] by LilyPond's table (measured 2026-08-22, audit 8.1):
        // two slots are the two UNEVEN beats, 3/8 then 2/8.
        Assert.Equal(new[] { "3/8", "1/4" },
            ChordRhythm.SlotDurations(2, 5, 8)!.Value.Select(FractionString).ToArray());
        // Subdivision splits each beat of ITS OWN length: k=2 gives 3/16 ×2, 1/8 ×2.
        Assert.Equal(new[] { "3/16", "3/16", "1/8", "1/8" },
            ChordRhythm.SlotDurations(4, 5, 8)!.Value.Select(FractionString).ToArray());
    }

    [Fact]
    public void SlotDurations_34_GroupsWholeBeatsOnly()
    {
        // 3/4 is three quarter beats: 3 slots sit on them; 2 slots would have
        // to split a beat across slots (3 % 2 != 0) — off the grid. Write the
        // held shape with '.' instead: | Am . E | is 3 slots, Am held 2 beats;
        // the 3/8+3/8 feel is 6 slots, | Am . . E . . |.
        Assert.Equal(new[] { "1/4", "1/4", "1/4" },
            ChordRhythm.SlotDurations(3, 3, 4)!.Value.Select(FractionString).ToArray());
        Assert.Null(ChordRhythm.SlotDurations(2, 3, 4));
        Assert.Equal(6, ChordRhythm.SlotDurations(6, 3, 4)!.Value.Length);
    }

    [Fact]
    public void SlotDurations_OneSlot_TakesAnyBar()
    {
        Assert.Equal("3/4", FractionString(ChordRhythm.SlotDurations(1, 3, 4)!.Value[0]));
        Assert.Equal("3/4", FractionString(ChordRhythm.SlotDurations(1, 6, 8)!.Value[0]));
        Assert.Equal("5/8", FractionString(ChordRhythm.SlotDurations(1, 5, 8)!.Value[0]));
    }

    private static string FractionString(Fraction f) =>
        f.Denominator == 1 ? f.Numerator.ToString() : $"{f.Numerator}/{f.Denominator}";
}
