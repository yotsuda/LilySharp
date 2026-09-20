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

namespace LilySharp.Core.Parser;

/// <summary>
/// Collects a run of green nodes without building a list for the run that holds nothing or one.
/// </summary>
/// <remarks>
/// <para>
/// A parser collects runs that are almost always empty or one long — trivia, articulations,
/// augmentation dots — and a <see cref="List{T}"/> built to hold them pays for its object and
/// for its first array whether or not anything arrives. Session 447 measured that over the
/// reader's corpus: 3,632 trivia scans a keystroke, of which 2,906 find nothing and 726 find
/// exactly one node, and 519 articulation scans, of which 369 find nothing. Keeping the first
/// node in a local and building the list only when a SECOND arrives takes the container out of
/// every one of those calls.
/// </para>
/// <para>
/// ⚠️ It is a static taking two <c>ref</c>s and not a local function closing over the two
/// locals: a closure would allocate a display class on every call, which is the cost this
/// exists to remove.
/// </para>
/// </remarks>
internal static class GreenRun
{
    /// <summary>Adds <paramref name="node"/> to the run.</summary>
    /// <param name="node">The node to keep.</param>
    /// <param name="first">The first node of the run, or <c>null</c> while the run is empty.</param>
    /// <param name="more">The rest of the run, built on the second node and not before.</param>
    internal static void Take<T>(T node, ref T? first, ref List<T>? more)
        where T : class
    {
        if (first is null)
            first = node;
        else
            (more ??= new List<T>()).Add(node);
    }
}
