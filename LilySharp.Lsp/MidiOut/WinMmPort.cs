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
/// Windows: the multimedia MIDI API (<c>winmm.dll</c>, <c>midiOut*</c>) — the one every
/// Windows MIDI program has used since 3.1, which reaches the Microsoft GS Wavetable Synth
/// with no setup, a loopMIDI port, and any interface's driver. A device is named by
/// <c>MIDIOUTCAPS.szPname</c> (32 characters, as the OS truncates it).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WinMmPort : IMidiOutPort
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIDIOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public ushort wTechnology;
        public ushort wVoices;
        public ushort wNotes;
        public ushort wChannelMask;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiOutGetDevCapsW")]
    private static extern uint midiOutGetDevCaps(nuint deviceId, ref MIDIOUTCAPS caps, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern uint midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiOutClose(IntPtr handle);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiOutGetErrorTextW")]
    private static extern uint midiOutGetErrorText(uint error, StringBuilder text, uint size);

    private IntPtr _handle;
    public string Name { get; }

    private WinMmPort(string name, IntPtr handle)
    {
        Name = name;
        _handle = handle;
    }

    public static IReadOnlyList<string> Names()
    {
        uint n = midiOutGetNumDevs();
        var names = new List<string>((int)n);
        for (uint i = 0; i < n; i++)
        {
            var caps = new MIDIOUTCAPS();
            if (midiOutGetDevCaps(i, ref caps, (uint)Marshal.SizeOf<MIDIOUTCAPS>()) == 0)
                names.Add(caps.szPname);
        }
        return names;
    }

    public static WinMmPort Open(string name)
    {
        var names = Names();
        int id = names.ToList().IndexOf(name);
        if (id < 0)
            throw MidiOutPorts.NotFound(name, names);
        uint rc = midiOutOpen(out var handle, (uint)id, IntPtr.Zero, IntPtr.Zero, 0);
        if (rc != 0)
            throw new InvalidOperationException($"MIDI output \"{name}\" could not be opened: {ErrorText(rc)}");
        return new WinMmPort(name, handle);
    }

    private static string ErrorText(uint rc)
    {
        var text = new StringBuilder(256);
        return midiOutGetErrorText(rc, text, (uint)text.Capacity) == 0 ? text.ToString() : $"MMSYSERR {rc}";
    }

    public void Send(byte status, byte data1, byte data2)
    {
        var h = _handle;
        if (h == IntPtr.Zero) return;
        uint rc = midiOutShortMsg(h, (uint)(status | (data1 << 8) | (data2 << 16)));
        if (rc != 0)
            throw new InvalidOperationException($"MIDI output \"{Name}\": {ErrorText(rc)}");
    }

    public void Dispose()
    {
        var h = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (h == IntPtr.Zero) return;
        midiOutReset(h);   // every note off, before the handle goes
        midiOutClose(h);
    }
}
