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
using System.Collections.Generic;
using System.Linq;

namespace LilySharp.Core.Midi;

/// <summary>
/// The seconds of a tick under a file's tempo map — the one reading the preview's note list
/// (<c>lilysharp/playback</c>) and the singing voice (<see cref="SungVoices"/>) share, so the
/// voice lands where the synth's notes do.
/// </summary>
public static class MidiClock
{
    /// <summary>
    /// A function from tick to seconds: every track's tempo changes merged in tick order, 120 to
    /// the quarter before the first.
    /// </summary>
    public static Func<int, double> SecondsOf(MidiFile midi)
    {
        int tpq = midi.TicksPerQuarterNote;
        List<TempoChange> tempos = midi.Tracks.SelectMany(t => t.TempoChanges).OrderBy(t => t.Tick).ToList();
        return tick =>
        {
            double sec = 0;
            int prevTick = 0;
            double usPerBeat = 500000; // 120 bpm
            foreach (var tc in tempos)
            {
                if (tc.Tick >= tick)
                    break;
                sec += (tc.Tick - prevTick) * usPerBeat / tpq / 1e6;
                prevTick = tc.Tick;
                usPerBeat = tc.MicrosecondsPerBeat;
            }
            return sec + (tick - prevTick) * usPerBeat / tpq / 1e6;
        };
    }
}
