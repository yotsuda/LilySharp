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

using Xunit;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

/// <summary>
/// The fingering/script memo's ROOMS (<see cref="FingScriptMemo"/>). The preliminary
/// annotation pass runs once per placement, and a keystroke whose page score picks another
/// line count places the book twice — so one room per (staff, system) is one room for two
/// runs, and each takes the other's slot.
/// <para>
/// MEASURED (session 417, the owner's 231 books × 8 forward keystrokes, Release): of the
/// preliminary store's 10,756 misses, <b>4,208</b> landed on a slot the SECOND placement
/// wrote and <b>4,208</b> on a slot the FIRST wrote — the two counts EQUAL, which is the
/// thrash signature — against a floor of 2,340 on a slot the same placement wrote. Hit rate
/// by placement 83.5% / 38.1% before, 93.4% / 95.6% after. This is the same shape session
/// 413 gave the paging-augment store and session 415 the above-stack one.
/// </para>
/// These nets are at the STORE, not at the pass: what they pin is that two alternating
/// programs both keep hitting, that the room served is the one that MATCHED (so a promotion
/// can never hand back the other placement's digits), and that a third program evicts the
/// older room rather than growing the store without bound.
/// </summary>
[Trait("Category", "Unit")]
public class FingScriptMemoRoomsTests
{
    /// <summary>One unit's program, distinguished by its staff offset, with an output that
    /// names the program it came from (the digit is the offset).</summary>
    private static FingScriptMemo.UnitEntry Program(double offset) => new()
    {
        MeasureIndices = [0, 1],
        StaffOffsets = [offset],
        Adjusted =
        [
            new FingeringLayout(MeasureIndex: 0, ItemIndex: 0, Number: (int)offset,
                X: 10, YUp: offset, IsAbove: true, SourcePosition: 0),
        ],
    };

    [Fact]
    public void TwoPlacementsAlternating_BothKeepHitting()
    {
        var memo = new FingScriptMemo();
        var first = Program(3);
        var second = Program(6);

        Assert.Null(memo.TryMatch(0, 0, first));
        memo.Store(0, 0, first);
        Assert.Null(memo.TryMatch(0, 0, second));
        memo.Store(0, 0, second);
        Assert.Equal(0, memo.Hits);
        Assert.Equal(2, memo.Misses);   // each placement's cold fill

        // ⚠️ WITH ONE ROOM every line below is a miss: each placement evicts the other.
        for (int keystroke = 0; keystroke < 2; keystroke++)
        {
            var a = memo.TryMatch(0, 0, Program(3));
            var b = memo.TryMatch(0, 0, Program(6));
            Assert.NotNull(a);
            Assert.NotNull(b);
            // THE ROOM SERVED IS THE ONE THAT MATCHED — a promotion that handed back the
            // other placement's entry would print the other placement's digit.
            Assert.Equal(3, a!.Adjusted[0].Number);
            Assert.Equal(6, b!.Adjusted[0].Number);
        }
        Assert.Equal(4, memo.Hits);
        Assert.Equal(2, memo.Misses);   // nothing evicted anything
    }

    [Fact]
    public void AThirdProgram_EvictsTheOlderRoom_AndTheEvictedOneIsRecomputed()
    {
        var memo = new FingScriptMemo();
        memo.Store(0, 0, Program(3));          // rooms: [first, -]
        memo.Store(0, 0, Program(6));          // rooms: [second, first]

        Assert.NotNull(memo.TryMatch(0, 0, Program(3)));  // from the OLDER room, promoted
        Assert.Equal(1, memo.Hits);
        Assert.Equal(0, memo.Misses);

        memo.Store(0, 0, Program(9));          // rooms: [third, first] — second evicted
        Assert.Null(memo.TryMatch(0, 0, Program(6)));     // …so it recomputes
        Assert.Equal(1, memo.Hits);
        Assert.Equal(1, memo.Misses);
        // And the two rooms that remain still answer for themselves.
        Assert.Equal(9, memo.TryMatch(0, 0, Program(9))!.Adjusted[0].Number);
        Assert.Equal(3, memo.TryMatch(0, 0, Program(3))!.Adjusted[0].Number);
    }

    /// <summary>The key is still per (staff, system): two staves of one system do not share
    /// a pair of rooms.</summary>
    [Fact]
    public void TheRoomsAreStillPerStaffAndSystem()
    {
        var memo = new FingScriptMemo();
        memo.Store(0, 0, Program(3));
        memo.Store(1, 0, Program(6));
        Assert.Equal(3, memo.TryMatch(0, 0, Program(3))!.Adjusted[0].Number);
        Assert.Equal(6, memo.TryMatch(1, 0, Program(6))!.Adjusted[0].Number);
        Assert.Null(memo.TryMatch(0, 0, Program(6)));
        Assert.Null(memo.TryMatch(1, 0, Program(3)));
    }
}
