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
    /// <paramref name="startItemIdx"/>) for the first item satisfying
    /// <paramref name="match"/>, continuing into later measures. Returns null if none.
    /// This is the shared forward scan; the typed helpers below wrap it with a predicate.
    /// </summary>
    public static (int MeasureIdx, int ItemIdx, MusicItem Item)? FindNext(
        ImmutableArray<Measure> measures,
        int startMeasureIdx,
        int startItemIdx,
        Func<MusicItem, bool> match)
    {
        // GRACE TIME IS INVISIBLE TO THIS SCAN, for the same reason VoiceScan.WalkVoiceItems
        // steps past it: every caller is looking for the note a span REACHES, and a grace
        // takes no measure time, so it stands between a note and that note's target.
        // MEASURED: `d4@glissando grace { d8 } c` slid to the GRACE instead of the c, and
        // being written at the same pitch the line came out horizontal — the same defect a
        // tie would show as a tie into a note the writer did not tie to.
        // ⚠️ SCAFFOLDING; it goes with the rest when ⒝2 lets the ordinary engravers draw
        // grace time (HANDOFF §2 U8), at which point a grace note IS a legal span bound —
        // which is exactly what LilyPond makes of one.
        static bool Reachable(MusicItem item, Func<MusicItem, bool> match)
            => !item.GraceTime && match(item);

        // Rest of the start measure, then every following measure from its first item.
        var current = measures[startMeasureIdx];
        for (int i = startItemIdx + 1; i < current.Items.Length; i++)
            if (Reachable(current.Items[i], match))
                return (startMeasureIdx, i, current.Items[i]);

        for (int m = startMeasureIdx + 1; m < measures.Length; m++)
        {
            var measure = measures[m];
            for (int i = 0; i < measure.Items.Length; i++)
                if (Reachable(measure.Items[i], match))
                    return (m, i, measure.Items[i]);
        }

        return null;
    }

    /// <summary>
    /// Scans forward for the first <see cref="NoteItem"/> matching <paramref name="match"/>.
    /// </summary>
    public static (int MeasureIdx, int ItemIdx, NoteItem Note)? FindNextNote(
        ImmutableArray<Measure> measures,
        int startMeasureIdx,
        int startItemIdx,
        Func<NoteItem, bool> match)
        => FindNext(measures, startMeasureIdx, startItemIdx, x => x is NoteItem n && match(n))
            is { } v ? (v.MeasureIdx, v.ItemIdx, (NoteItem)v.Item) : null;

    /// <summary>
    /// Like <see cref="FindNextNote"/> but matches a <see cref="NoteItem"/> OR a
    /// <see cref="ChordItem"/> — used by glissandos, which may slide into a chord
    /// (a note-only scan would skip the chord and drop the glissando).
    /// </summary>
    public static (int MeasureIdx, int ItemIdx, MusicItem Item)? FindNextNoteOrChord(
        ImmutableArray<Measure> measures,
        int startMeasureIdx,
        int startItemIdx)
        => FindNext(measures, startMeasureIdx, startItemIdx, x => x is NoteItem or ChordItem);
}
