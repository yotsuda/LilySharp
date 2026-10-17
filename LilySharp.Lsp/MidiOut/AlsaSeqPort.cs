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

namespace LilySharp.Lsp.MidiOut;

/// <summary>
/// Linux: the ALSA sequencer (<c>libasound.so.2</c>) — the sequencer, not rawmidi, because
/// that is where the software synths are: FluidSynth / Qsynth, TiMidity++, a DAW's input,
/// all register as sequencer clients, and <c>aconnect -l</c> lists them. A port is named
/// <c>client name: port name</c>; the server connects its own client to it and dispatches
/// each event directly (no queue). Needs libasound2 installed, as every ALSA program does;
/// without it the P/Invoke throws and the port list is empty.
/// ⚠️ Written against the documented API on a Windows machine; not yet run on Linux
/// (editors/vscode/MANUAL-CHECKS.md).
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class AlsaSeqPort : IMidiOutPort
{
    private const string Lib = "libasound.so.2";
    private const int SND_SEQ_OPEN_OUTPUT = 1;
    private const uint SND_SEQ_PORT_CAP_READ = 1 << 0;
    private const uint SND_SEQ_PORT_CAP_WRITE = 1 << 1;
    private const uint SND_SEQ_PORT_CAP_SUBS_READ = 1 << 5;
    private const uint SND_SEQ_PORT_CAP_SUBS_WRITE = 1 << 6;
    private const uint SND_SEQ_PORT_TYPE_MIDI_GENERIC = 1 << 1;
    private const uint SND_SEQ_PORT_TYPE_APPLICATION = 1 << 20;
    private const byte SND_SEQ_QUEUE_DIRECT = 253;
    private const byte SND_SEQ_ADDRESS_UNKNOWN = 253;
    private const byte SND_SEQ_ADDRESS_SUBSCRIBERS = 254;
    private const byte SND_SEQ_EVENT_NOTEON = 6;
    private const byte SND_SEQ_EVENT_NOTEOFF = 7;
    private const byte SND_SEQ_EVENT_KEYPRESS = 8;
    private const byte SND_SEQ_EVENT_CONTROLLER = 10;
    private const byte SND_SEQ_EVENT_PGMCHANGE = 11;
    private const byte SND_SEQ_EVENT_CHANPRESS = 12;
    private const byte SND_SEQ_EVENT_PITCHBEND = 13;

    /// <summary>snd_seq_event_t: 28 bytes — type, flags, tag, queue; the 8-byte timestamp;
    /// source and destination addresses (client, port each); and 12 bytes of data, which
    /// are the note (channel, note, velocity, off velocity, duration) or the control
    /// (channel, three unused, param, value). Packed as the C union is (its ext member is
    /// <c>__attribute__((packed))</c>).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SndSeqEvent
    {
        public byte Type, Flags, Tag, Queue;
        public uint Time0, Time1;
        public byte SourceClient, SourcePort, DestClient, DestPort;
        public byte D0, D1, D2, D3;   // note: channel, note, velocity, off_velocity / control: channel, unused ×3
        public uint D4;               // note: duration / control: param
        public int D8;                // control: value
    }

    [DllImport(Lib)] private static extern int snd_seq_open(out IntPtr handle, string name, int streams, int mode);
    [DllImport(Lib)] private static extern int snd_seq_close(IntPtr handle);
    [DllImport(Lib)] private static extern int snd_seq_client_id(IntPtr handle);
    [DllImport(Lib)] private static extern int snd_seq_set_client_name(IntPtr handle, string name);
    [DllImport(Lib)] private static extern int snd_seq_create_simple_port(IntPtr handle, string name, uint caps, uint type);
    [DllImport(Lib)] private static extern int snd_seq_connect_to(IntPtr handle, int myPort, int destClient, int destPort);
    [DllImport(Lib)] private static extern int snd_seq_event_output_direct(IntPtr handle, ref SndSeqEvent ev);
    [DllImport(Lib)] private static extern int snd_seq_client_info_malloc(out IntPtr info);
    [DllImport(Lib)] private static extern void snd_seq_client_info_free(IntPtr info);
    [DllImport(Lib)] private static extern void snd_seq_client_info_set_client(IntPtr info, int client);
    [DllImport(Lib)] private static extern int snd_seq_client_info_get_client(IntPtr info);
    [DllImport(Lib)] private static extern IntPtr snd_seq_client_info_get_name(IntPtr info);
    [DllImport(Lib)] private static extern int snd_seq_query_next_client(IntPtr handle, IntPtr info);
    [DllImport(Lib)] private static extern int snd_seq_port_info_malloc(out IntPtr info);
    [DllImport(Lib)] private static extern void snd_seq_port_info_free(IntPtr info);
    [DllImport(Lib)] private static extern void snd_seq_port_info_set_client(IntPtr info, int client);
    [DllImport(Lib)] private static extern void snd_seq_port_info_set_port(IntPtr info, int port);
    [DllImport(Lib)] private static extern int snd_seq_port_info_get_port(IntPtr info);
    [DllImport(Lib)] private static extern uint snd_seq_port_info_get_capability(IntPtr info);
    [DllImport(Lib)] private static extern IntPtr snd_seq_port_info_get_name(IntPtr info);
    [DllImport(Lib)] private static extern int snd_seq_query_next_port(IntPtr handle, IntPtr info);
    [DllImport(Lib)] private static extern IntPtr snd_strerror(int error);

    private IntPtr _handle;
    private readonly int _port;
    private readonly object _gate = new();
    public string Name { get; }

    private AlsaSeqPort(string name, IntPtr handle, int port)
    {
        Name = name;
        _handle = handle;
        _port = port;
    }

    private readonly record struct Destination(string Name, int Client, int Port);

    /// <summary>Every port another client offers for writing (<c>WRITE</c> and
    /// <c>SUBS_WRITE</c>), as <c>client: port</c>.</summary>
    private static List<Destination> Destinations(IntPtr handle)
    {
        var list = new List<Destination>();
        int self = snd_seq_client_id(handle);
        snd_seq_client_info_malloc(out var cinfo);
        snd_seq_port_info_malloc(out var pinfo);
        try
        {
            snd_seq_client_info_set_client(cinfo, -1);
            while (snd_seq_query_next_client(handle, cinfo) >= 0)
            {
                int client = snd_seq_client_info_get_client(cinfo);
                if (client == self) continue;
                string clientName = Marshal.PtrToStringUTF8(snd_seq_client_info_get_name(cinfo)) ?? $"client {client}";
                snd_seq_port_info_set_client(pinfo, client);
                snd_seq_port_info_set_port(pinfo, -1);
                while (snd_seq_query_next_port(handle, pinfo) >= 0)
                {
                    uint caps = snd_seq_port_info_get_capability(pinfo);
                    if ((caps & (SND_SEQ_PORT_CAP_WRITE | SND_SEQ_PORT_CAP_SUBS_WRITE)) != (SND_SEQ_PORT_CAP_WRITE | SND_SEQ_PORT_CAP_SUBS_WRITE))
                        continue;
                    int port = snd_seq_port_info_get_port(pinfo);
                    string portName = Marshal.PtrToStringUTF8(snd_seq_port_info_get_name(pinfo)) ?? $"port {port}";
                    list.Add(new Destination($"{clientName}: {portName}", client, port));
                }
            }
        }
        finally
        {
            snd_seq_port_info_free(pinfo);
            snd_seq_client_info_free(cinfo);
        }
        return list;
    }

    private static IntPtr OpenSequencer()
    {
        int rc = snd_seq_open(out var handle, "default", SND_SEQ_OPEN_OUTPUT, 0);
        if (rc < 0)
            throw new InvalidOperationException($"the ALSA sequencer could not be opened: {Error(rc)}");
        snd_seq_set_client_name(handle, "Lily#");
        return handle;
    }

    private static string Error(int rc) => Marshal.PtrToStringUTF8(snd_strerror(rc)) ?? $"error {rc}";

    public static IReadOnlyList<string> Names()
    {
        IntPtr handle = OpenSequencer();
        try { return Destinations(handle).Select(d => d.Name).ToList(); }
        finally { snd_seq_close(handle); }
    }

    public static AlsaSeqPort Open(string name)
    {
        IntPtr handle = OpenSequencer();
        try
        {
            var destinations = Destinations(handle);
            var dest = destinations.FirstOrDefault(d => d.Name == name);
            if (dest.Name == null)
                throw MidiOutPorts.NotFound(name, destinations.Select(d => d.Name).ToList());
            int port = snd_seq_create_simple_port(handle, "Lily# out",
                SND_SEQ_PORT_CAP_READ | SND_SEQ_PORT_CAP_SUBS_READ,
                SND_SEQ_PORT_TYPE_MIDI_GENERIC | SND_SEQ_PORT_TYPE_APPLICATION);
            if (port < 0)
                throw new InvalidOperationException($"MIDI output \"{name}\": the sequencer port could not be created: {Error(port)}");
            int rc = snd_seq_connect_to(handle, port, dest.Client, dest.Port);
            if (rc < 0)
                throw new InvalidOperationException($"MIDI output \"{name}\" could not be connected: {Error(rc)}");
            var opened = new AlsaSeqPort(name, handle, port);
            handle = IntPtr.Zero;
            return opened;
        }
        finally
        {
            if (handle != IntPtr.Zero) snd_seq_close(handle);
        }
    }

    public void Send(byte status, byte data1, byte data2)
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return;
            var ev = new SndSeqEvent
            {
                Queue = SND_SEQ_QUEUE_DIRECT,
                SourcePort = (byte)_port,
                DestClient = SND_SEQ_ADDRESS_SUBSCRIBERS,
                DestPort = SND_SEQ_ADDRESS_UNKNOWN,
                D0 = (byte)(status & 0x0F),
            };
            switch (status & 0xF0)
            {
                case 0x80: ev.Type = SND_SEQ_EVENT_NOTEOFF; ev.D1 = data1; ev.D2 = data2; break;
                case 0x90: ev.Type = SND_SEQ_EVENT_NOTEON; ev.D1 = data1; ev.D2 = data2; break;
                case 0xA0: ev.Type = SND_SEQ_EVENT_KEYPRESS; ev.D1 = data1; ev.D2 = data2; break;
                case 0xB0: ev.Type = SND_SEQ_EVENT_CONTROLLER; ev.D4 = data1; ev.D8 = data2; break;
                case 0xC0: ev.Type = SND_SEQ_EVENT_PGMCHANGE; ev.D8 = data1; break;
                case 0xD0: ev.Type = SND_SEQ_EVENT_CHANPRESS; ev.D8 = data1; break;
                case 0xE0: ev.Type = SND_SEQ_EVENT_PITCHBEND; ev.D8 = (data1 | (data2 << 7)) - 8192; break;
                default: return;
            }
            int rc = snd_seq_event_output_direct(_handle, ref ev);
            if (rc < 0)
                throw new InvalidOperationException($"MIDI output \"{Name}\": {Error(rc)}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return;
            snd_seq_close(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
