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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LilySharp.Lsp.MidiOut;

/// <summary>
/// macOS: CoreMIDI. A destination is every endpoint the system lists — an IAC Driver bus
/// (the loopMIDI of the Mac, in Audio MIDI Setup), a DAW's virtual input, an interface —
/// named by its display name. One client and one output port for the server's life; a
/// message is one packet in a packet list built by CoreMIDI's own
/// <c>MIDIPacketListInit</c> / <c>MIDIPacketListAdd</c>, so the list's layout (which
/// differs between Intel and Apple silicon) is never spelled here.
/// ⚠️ Written against the documented API on a Windows machine; not yet run on a Mac
/// (editors/vscode/MANUAL-CHECKS.md).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class CoreMidiPort : IMidiOutPort
{
    private const string CoreMidi = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint kCFStringEncodingUTF8 = 0x08000100;
    private const int PacketListBytes = 256;

    [DllImport(CoreMidi)] private static extern int MIDIClientCreate(IntPtr name, IntPtr notifyProc, IntPtr notifyRefCon, out uint client);
    [DllImport(CoreMidi)] private static extern int MIDIClientDispose(uint client);
    [DllImport(CoreMidi)] private static extern int MIDIOutputPortCreate(uint client, IntPtr portName, out uint port);
    [DllImport(CoreMidi)] private static extern int MIDIPortDispose(uint port);
    [DllImport(CoreMidi)] private static extern nuint MIDIGetNumberOfDestinations();
    [DllImport(CoreMidi)] private static extern uint MIDIGetDestination(nuint index);
    [DllImport(CoreMidi)] private static extern int MIDIObjectGetStringProperty(uint obj, IntPtr propertyId, out IntPtr str);
    [DllImport(CoreMidi)] private static extern IntPtr MIDIPacketListInit(IntPtr packetList);
    [DllImport(CoreMidi)] private static extern IntPtr MIDIPacketListAdd(IntPtr packetList, nuint listSize, IntPtr current, ulong time, nuint dataCount, byte[] data);
    [DllImport(CoreMidi)] private static extern int MIDISend(uint port, uint destination, IntPtr packetList);

    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, nint bufferSize, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

    private uint _client;
    private uint _port;
    private readonly uint _destination;
    private IntPtr _packets;
    private readonly object _gate = new();
    public string Name { get; }

    private CoreMidiPort(string name, uint client, uint port, uint destination)
    {
        Name = name;
        _client = client;
        _port = port;
        _destination = destination;
        _packets = Marshal.AllocHGlobal(PacketListBytes);
    }

    /// <summary>Every destination's display name (its name when it has none), in order.</summary>
    public static IReadOnlyList<string> Names()
    {
        nuint n = MIDIGetNumberOfDestinations();
        var names = new List<string>((int)n);
        for (nuint i = 0; i < n; i++)
            names.Add(NameOf(MIDIGetDestination(i)));
        return names;
    }

    private static string NameOf(uint endpoint)
        => StringProperty(endpoint, "displayName") ?? StringProperty(endpoint, "name") ?? "(unnamed)";

    private static string? StringProperty(uint obj, string property)
    {
        IntPtr key = CFStringCreateWithCString(IntPtr.Zero, property, kCFStringEncodingUTF8);
        try
        {
            if (MIDIObjectGetStringProperty(obj, key, out var value) != 0 || value == IntPtr.Zero)
                return null;
            try
            {
                var buffer = new byte[512];
                if (!CFStringGetCString(value, buffer, buffer.Length, kCFStringEncodingUTF8))
                    return null;
                int len = Array.IndexOf(buffer, (byte)0);
                return Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
            }
            finally { CFRelease(value); }
        }
        finally { CFRelease(key); }
    }

    public static CoreMidiPort Open(string name)
    {
        nuint n = MIDIGetNumberOfDestinations();
        uint destination = 0;
        bool found = false;
        for (nuint i = 0; i < n && !found; i++)
        {
            var d = MIDIGetDestination(i);
            if (NameOf(d) == name) { destination = d; found = true; }
        }
        if (!found)
            throw MidiOutPorts.NotFound(name, Names());
        IntPtr clientName = CFStringCreateWithCString(IntPtr.Zero, "Lily#", kCFStringEncodingUTF8);
        IntPtr portName = CFStringCreateWithCString(IntPtr.Zero, "Lily# out", kCFStringEncodingUTF8);
        try
        {
            int rc = MIDIClientCreate(clientName, IntPtr.Zero, IntPtr.Zero, out uint client);
            if (rc != 0)
                throw new InvalidOperationException($"MIDI output \"{name}\": MIDIClientCreate failed ({rc})");
            rc = MIDIOutputPortCreate(client, portName, out uint port);
            if (rc != 0)
            {
                MIDIClientDispose(client);
                throw new InvalidOperationException($"MIDI output \"{name}\": MIDIOutputPortCreate failed ({rc})");
            }
            return new CoreMidiPort(name, client, port, destination);
        }
        finally
        {
            CFRelease(portName);
            CFRelease(clientName);
        }
    }

    public void Send(byte status, byte data1, byte data2)
    {
        lock (_gate)
        {
            if (_packets == IntPtr.Zero) return;
            int count = (status & 0xF0) is 0xC0 or 0xD0 ? 2 : 3;
            var data = count == 2 ? new[] { status, data1 } : new[] { status, data1, data2 };
            IntPtr packet = MIDIPacketListInit(_packets);
            packet = MIDIPacketListAdd(_packets, (nuint)PacketListBytes, packet, 0, (nuint)count, data);
            if (packet == IntPtr.Zero)
                throw new InvalidOperationException($"MIDI output \"{Name}\": the packet list overflowed");
            int rc = MIDISend(_port, _destination, _packets);
            if (rc != 0)
                throw new InvalidOperationException($"MIDI output \"{Name}\": MIDISend failed ({rc})");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_packets == IntPtr.Zero) return;
            Marshal.FreeHGlobal(_packets);
            _packets = IntPtr.Zero;
            if (_port != 0) { MIDIPortDispose(_port); _port = 0; }
            if (_client != 0) { MIDIClientDispose(_client); _client = 0; }
        }
    }
}
