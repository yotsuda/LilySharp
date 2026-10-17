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

namespace LilySharp.Lsp;

// The lilysharp/midiOut/* requests (docs/playback-design.md §6): the preview's webview
// decides WHAT plays — the note list it already holds, from an offset; the pitches an
// audition strikes — and the server is the port. Every request names the port
// (lilysharp.playback.output), so a changed setting takes effect on the next sound.

/// <summary>Parameters for lilysharp/midiOut/ports (none).</summary>
public class MidiOutPortsParams { }

/// <summary>Response for lilysharp/midiOut/ports: the machine's MIDI output ports by name,
/// in the OS's order.</summary>
public class MidiOutPortsResponse
{
    public string[] Ports { get; set; } = System.Array.Empty<string>();
    public string? Error { get; set; }
}

/// <summary>Parameters for lilysharp/midiOut/play: the note list the preview holds
/// (<see cref="PlaybackNote"/>, seconds), played from <see cref="Offset"/> seconds into the
/// piece on the port named.</summary>
public class MidiOutPlayParams
{
    public string Port { get; set; } = "";
    public PlaybackNote[] Notes { get; set; } = System.Array.Empty<PlaybackNote>();
    public double Offset { get; set; }
}

/// <summary>Response for lilysharp/midiOut/play. <see cref="StartedAtMs"/> is the wall clock
/// (Unix ms, the page's <c>Date.now()</c>) at the playback's zero — the follow-along
/// highlight's clock; <see cref="EndSeconds"/> when the last note ends, in seconds into the
/// piece. Not started: <see cref="Error"/> says why, and the preview plays its own synth.</summary>
public class MidiOutPlayResponse
{
    public bool Started { get; set; }
    public long StartedAtMs { get; set; }
    public double EndSeconds { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameters for lilysharp/midiOut/stop (none: there is one playback).</summary>
public class MidiOutStopParams { }

/// <summary>Parameters for lilysharp/midiOut/strike: an audition's pitches (MIDI numbers;
/// a quarter tone rounds) with the part's program (-1: the timbre family's), held until
/// lilysharp/midiOut/release.</summary>
public class MidiOutStrikeParams
{
    public string Port { get; set; } = "";
    public double[] Pitches { get; set; } = System.Array.Empty<double>();
    public int Timbre { get; set; }
    public int Program { get; set; } = -1;
}

/// <summary>Parameters for lilysharp/midiOut/release: the channel the strike answered and
/// the pitches to let go.</summary>
public class MidiOutReleaseParams
{
    public int Channel { get; set; } = -1;
    public double[] Pitches { get; set; } = System.Array.Empty<double>();
}

/// <summary>Response for lilysharp/midiOut/stop, /strike and /release: <see cref="Channel"/>
/// is the strike's (-1 otherwise); <see cref="Error"/> why nothing sounded.</summary>
public class MidiOutResponse
{
    public int Channel { get; set; } = -1;
    public string? Error { get; set; }
}
