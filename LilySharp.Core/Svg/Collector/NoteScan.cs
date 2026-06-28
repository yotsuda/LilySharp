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
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>Shared forward scan over a voice's items, used to pair a start note with
/// its target across measure boundaries (ties to the next same-pitch note, glissandos
/// to the next note of any pitch).</summary>
internal static class NoteScan
{
    /// <summary>
    /// Scans forward from just after (<paramref name="startMeasureIdx"/>,
    /// <paramref name="startItemIdx"/>) for the first <see cref="NoteItem"/> matching
    /// <paramref name="match"/>, continuing into later measures. Returns null if none.
    /// </summary>
    public static (int MeasureIdx, int ItemIdx, NoteItem Note)? FindNextNote(
        ImmutableArray<Measure> measures,
        int startMeasureIdx,
        int startItemIdx,
        Func<NoteItem, bool> match)
    {
        // Rest of the start measure, then every following measure from its first item.
        var current = measures[startMeasureIdx];
        for (int i = startItemIdx + 1; i < current.Items.Length; i++)
            if (current.Items[i] is NoteItem c && match(c))
                return (startMeasureIdx, i, c);

        for (int m = startMeasureIdx + 1; m < measures.Length; m++)
        {
            var measure = measures[m];
            for (int i = 0; i < measure.Items.Length; i++)
                if (measure.Items[i] is NoteItem c && match(c))
                    return (m, i, c);
        }

        return null;
    }
}
