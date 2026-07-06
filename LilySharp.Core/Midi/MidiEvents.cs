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

namespace LilySharp.Core.Midi;

/// <summary>
/// Represents a MIDI note event.
/// </summary>
public readonly record struct MidiNote(
    int Channel,
    int Pitch,
    int Velocity,
    int StartTick,
    int DurationTicks,
    // Source offset of the note's syntax — lets the preview player
    // highlight the notation being played. -1 = no source link. Not written
    // to .mid files.
    int SourcePos = -1,
    // Quarter-tone offset (+1 = +50 cents): pitch bend in the .mid, a
    // fractional pitch in the preview player. 0 for normal notes.
    int QuarterBend = 0,
    // Which PRINTED copy of the source this onset corresponds to
    // (phrase expansions share one position; repeats replay their pass-one
    // ordinals). Not written to .mid files.
    int SourceOrdinal = 0,
    // Timbre family for the preview synth (0 piano, 1 flute,
    // 2 clarinet, 3 strings, 4 guitar, 5 bass, 6 brass, 7 organ, 8 voice),
    // resolved from the part's `instrument` (or its name). Not written to
    // .mid files.
    int Timbre = 0
);

/// <summary>
/// Represents a tempo change event.
/// </summary>
public readonly record struct TempoChange(
    int Tick,
    int MicrosecondsPerBeat
);

/// <summary>
/// Represents a time signature change.
/// </summary>
public readonly record struct TimeSignatureChange(
    int Tick,
    int Numerator,
    int Denominator
);

/// <summary>
/// Represents a MIDI lyric event (Meta event 0x05).
/// </summary>
public readonly record struct LyricEvent(
    int Tick,
    string Text
);