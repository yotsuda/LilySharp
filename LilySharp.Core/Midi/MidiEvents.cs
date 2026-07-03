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
    /// <summary>Source offset of the note's syntax — lets the preview player
    /// highlight the notation being played. -1 = no source link. Not written
    /// to .mid files.</summary>
    int SourcePos = -1
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