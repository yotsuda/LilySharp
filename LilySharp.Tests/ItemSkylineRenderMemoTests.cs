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
/// The spacing passes share a column's skylines within one render
/// (ItemSkylineFactory.BeginRenderMemo, session 492). A shared skyline must be the one a
/// fresh build would give for the SAME arguments — every argument, since a key that forgot
/// one would hand a pair the wrong view — and nothing may be shared outside a scope.
/// </summary>
[Trait("Category", "Unit")]
public class ItemSkylineRenderMemoTests
{
    // A dotted note: its paper column (the rod's view) holds the dot, its note column (the
    // wish's view) does not, so the two right-side views differ.
    private static NoteItem DottedQuarter(int staffPosition)
        => new(staffPosition, Fraction.Quarter, 1, null, false, 0);

    [Fact]
    public void InsideAScope_EachViewIsTheFreshBuildOfItsOwnArguments()
    {
        var note = DottedQuarter(0);
        var other = DottedQuarter(0);
        using (ItemSkylineFactory.BeginRenderMemo())
        {
            // Asked in an order that would let a key missing any one field serve an earlier
            // answer: same item other view, same item other shift, other staff Y, other item.
            var rod = ItemSkylineFactory.SharedRightSkylineAtColumn(note, 0.5, 0);
            var wish = ItemSkylineFactory.SharedWishRightSkylineAtColumn(note, 0.5, 0);
            var shifted = ItemSkylineFactory.SharedRightSkylineAtColumn(note, 1.25, 0);
            var lower = ItemSkylineFactory.SharedRightSkylineAtColumn(note, 0.5, 2);
            var left = ItemSkylineFactory.SharedLeftSkylineAtColumn(note, 0.5, 0);
            var wishLeft = ItemSkylineFactory.SharedWishLeftSkylineAtColumn(note, 0.5, 0);
            var high = DottedQuarter(6);
            var highRod = ItemSkylineFactory.SharedRightSkylineAtColumn(high, 0.5, 0);

            Assert.Equal(ItemSkylineFactory.CreateRightSkylineAtColumn(note, 0.5, 0).Buildings, rod.Buildings);
            Assert.Equal(ItemSkylineFactory.CreateWishRightSkylineAtColumn(note, 0.5, 0).Buildings, wish.Buildings);
            Assert.NotEqual(rod.Buildings, wish.Buildings);
            Assert.Equal(ItemSkylineFactory.CreateRightSkylineAtColumn(note, 1.25, 0).Buildings, shifted.Buildings);
            Assert.Equal(ItemSkylineFactory.CreateRightSkylineAtColumn(note, 0.5, 2).Buildings, lower.Buildings);
            Assert.Equal(ItemSkylineFactory.CreateLeftSkylineAtColumn(note, 0.5, 0).Buildings, left.Buildings);
            Assert.Equal(ItemSkylineFactory.CreateWishLeftSkylineAtColumn(note, 0.5, 0).Buildings, wishLeft.Buildings);
            Assert.Equal(ItemSkylineFactory.CreateRightSkylineAtColumn(high, 0.5, 0).Buildings, highRod.Buildings);

            // The same arguments are served, not rebuilt — by item REFERENCE: an equal record
            // is a different item.
            Assert.Same(rod, ItemSkylineFactory.SharedRightSkylineAtColumn(note, 0.5, 0));
            Assert.NotSame(rod, ItemSkylineFactory.SharedRightSkylineAtColumn(other, 0.5, 0));
        }
    }

    [Fact]
    public void OutsideAScope_NothingIsShared()
    {
        var note = DottedQuarter(0);
        using (ItemSkylineFactory.BeginRenderMemo())
            ItemSkylineFactory.SharedRightSkylineAtColumn(note, 0.5, 0);

        Assert.NotSame(ItemSkylineFactory.SharedRightSkylineAtColumn(note, 0.5, 0),
            ItemSkylineFactory.SharedRightSkylineAtColumn(note, 0.5, 0));
    }
}
