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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A scratch ARRAY lent from a drawer the thread keeps between calls — for a table a call
/// fills, reads and drops (a DP's rows, a prefix sum, one system's per-bar heights), sized by
/// that call and read by nothing after it.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="ListPool{T}"/> for arrays. A site here holds ONE array of a kind
/// at a time and never nests, so its drawer is a single <c>[ThreadStatic]</c> field of its own
/// (the single-drawer idiom of <c>VerticalSkyline</c>'s scratch, session 421), grown to the
/// largest length the site has asked for and kept at it.
/// </para>
/// <para>
/// ⚠️ THE FILL IS LOAD-BEARING. A fresh array arrives zeroed; the drawer's arrives holding the
/// PREVIOUS call's numbers — from a larger table, possibly — and this helper clears nothing
/// (a taker that fills every cell it reads would pay the clear twice). Every taker writes,
/// fills or clears the <c>[0, length)</c> it goes on to read BEFORE its first read: the
/// <c>Array.Fill</c> a fresh table used to get for free is written out at the site, and a
/// prefix sum's <c>[0]</c> is set to zero by hand.
/// </para>
/// <para>
/// MEASURED (session 526's array census — session 495's instrument re-run at HEAD, Release, the
/// reader's corpus, eight forward keystrokes a book): the four families that took this drawer
/// were 25,112 B a keystroke of fresh tables (2.0% of the render), each filled once, read
/// once and dropped — the page DP's rows 10.8 times a keystroke, the page-count table once,
/// the line DP's prefix sums once, a system's per-bar heights 23.7 times.
/// </para>
/// </remarks>
internal static class ScratchArray
{
    /// <summary>
    /// The drawer's array, grown to hold at least <paramref name="length"/> cells. NOT cleared:
    /// the taker fills the cells it reads (see the class remarks).
    /// </summary>
    public static T[] Take<T>(ref T[]? drawer, int length)
    {
        var array = drawer;
        if (array is null || array.Length < length)
            drawer = array = new T[length];
        return array;
    }
}
