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

namespace LilySharp.Lsp.MidiOut;

/// <summary>
/// An open MIDI output port of this machine: a three-byte channel message goes out the
/// moment it is sent. The scheduling is <see cref="MidiOutPlayer"/>'s; this is the wire.
/// One class per operating system, each on the OS's own API through P/Invoke and nothing
/// else (<see cref="WinMmPort"/>, <see cref="CoreMidiPort"/>, <see cref="AlsaSeqPort"/>) —
/// no package, no native binary of ours to ship (docs/playback-design.md §6).
/// </summary>
public interface IMidiOutPort : IDisposable
{
    /// <summary>The port's name as the machine lists it — the value of
    /// <c>lilysharp.playback.output</c>.</summary>
    string Name { get; }

    /// <summary>Sends one channel message: the status byte (with the channel in its low
    /// nibble) and its one or two data bytes.</summary>
    void Send(byte status, byte data1, byte data2);
}

/// <summary>The machine's MIDI output ports, by name, through the OS's own API.</summary>
public static class MidiOutPorts
{
    /// <summary>The names of the output ports this machine has now, in the OS's order.
    /// Empty on an OS without a MIDI API in reach (Linux without libasound).</summary>
    public static IReadOnlyList<string> Names()
    {
        if (OperatingSystem.IsWindows()) return WinMmPort.Names();
        if (OperatingSystem.IsMacOS()) return CoreMidiPort.Names();
        if (OperatingSystem.IsLinux()) return AlsaSeqPort.Names();
        return [];
    }

    /// <summary>Opens the port named <paramref name="name"/> (the first of that name when
    /// two share it). Throws, with a sentence for the user, when there is none or the OS
    /// refuses it.</summary>
    public static IMidiOutPort Open(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("no MIDI output is named");
        if (OperatingSystem.IsWindows()) return WinMmPort.Open(name);
        if (OperatingSystem.IsMacOS()) return CoreMidiPort.Open(name);
        if (OperatingSystem.IsLinux()) return AlsaSeqPort.Open(name);
        throw new PlatformNotSupportedException("MIDI output is not available on this operating system");
    }

    /// <summary>The sentence for a port that is not in the list: names what is, so the
    /// user can see what the setting should say.</summary>
    internal static InvalidOperationException NotFound(string name, IReadOnlyList<string> names)
        => new($"MIDI output \"{name}\" is not there"
            + (names.Count == 0 ? " (this machine lists no MIDI output)" : $"; this machine has: {string.Join(", ", names)}"));
}
