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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Scratch lists lent from a stack the thread keeps between calls — for a call that builds
/// SEVERAL lists of one kind at once (one a staff, one a system, one a nested block) and drops
/// them all when it returns.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE SAME IDIOM AS THE SINGLE-DRAWER SITES, widened to a stack (session 476): a site that
/// holds ONE container at a time parks it in its own <c>[ThreadStatic]</c> field
/// (<c>VerticalSkyline</c>'s scratch, session 421); a site that holds several, or that nests,
/// needs as many as it holds at once, and <c>VerticalSkyline.RentBatch</c> already keeps them in
/// a stack for the same reason. This is that stack, once, for any element type.
/// </para>
/// <para>
/// RENTING TAKES THE LIST OFF THE STACK, so a list is its taker's alone from the rent to the
/// give — a nested call rents another one. THE CLEARING IS ON GIVE (session 456): a list given
/// back still holding items would hand them to the next taker, which reads it from the start.
/// A throw between the rent and the give only costs a later call a new list.
/// </para>
/// <para>
/// WHAT IT RETAINS is, per element type and thread, as many emptied lists as that thread ever
/// held at once, each at its largest capacity. They pin nothing: <see cref="Give"/> clears.
/// </para>
/// </remarks>
/// <typeparam name="T">The element type; each closed type has a stack of its own.</typeparam>
internal static class ListPool<T>
{
    [System.ThreadStatic]
    private static Stack<List<T>>? t_lists;

    /// <summary>Takes an empty list off the thread's stack, or makes one.</summary>
    public static List<T> Rent() =>
        t_lists is { Count: > 0 } lists ? lists.Pop() : new List<T>();

    /// <summary>Empties a finished list and puts it back, with its capacity.</summary>
    public static void Give(List<T> list)
    {
        list.Clear();
        (t_lists ??= new Stack<List<T>>()).Push(list);
    }
}
