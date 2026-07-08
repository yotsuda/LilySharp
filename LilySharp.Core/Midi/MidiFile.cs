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
/// Represents a MIDI track containing notes and events.
/// </summary>
public class MidiTrack
{
    /// <summary>The track name (written as an MIDI track-name meta event).</summary>
    public string Name { get; set; } = "";
    /// <summary>The MIDI channel (0-based) this track's events are emitted on.</summary>
    public int Channel { get; set; }
    /// <summary>The notes played on this track.</summary>
    public List<MidiNote> Notes { get; } = [];
    /// <summary>The tempo changes on this track.</summary>
    public List<TempoChange> TempoChanges { get; } = [];
    /// <summary>The time-signature changes on this track.</summary>
    public List<TimeSignatureChange> TimeSignatures { get; } = [];
    /// <summary>The lyric events on this track.</summary>
    public List<LyricEvent> Lyrics { get; } = [];
}

/// <summary>
/// Represents a complete MIDI file.
/// </summary>
public class MidiFile
{
    /// <summary>The default resolution of 480 ticks per quarter note.</summary>
    public const int DefaultTicksPerQuarter = 480;

    /// <summary>The timing resolution in ticks per quarter note.</summary>
    public int TicksPerQuarterNote { get; set; } = DefaultTicksPerQuarter;
    /// <summary>The tracks contained in this MIDI file.</summary>
    public List<MidiTrack> Tracks { get; } = [];

    /// <summary>
    /// Writes the MIDI file to a stream.
    /// </summary>
    public void WriteTo(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        WriteHeader(writer);
        var channelPlans = PlanQuarterToneChannels();
        for (int i = 0; i < Tracks.Count; i++)
        {
            WriteTrack(writer, Tracks[i], channelPlans[i]);
        }
    }

    /// <summary>
    /// MPE-lite channel plan for quarter tones: pitch bend is CHANNEL-wide, so
    /// a bent note sharing its channel with a simultaneous plain note would
    /// bend both. Each track's bent notes move to an auxiliary channel per
    /// bend value (¼-up and ¼-down each get their own), leaving the main
    /// channel clean; if the 16-channel space runs out, the affected notes
    /// fall back to in-channel bends (correct for monophonic lines).
    /// Returns, per track, the per-note output channel (null = no overrides).
    /// </summary>
    private List<int[]?> PlanQuarterToneChannels()
    {
        var used = new HashSet<int> { 9 }; // GM percussion stays reserved
        foreach (var t in Tracks)
            used.Add(t.Channel);
        var free = new Queue<int>(Enumerable.Range(0, 16).Where(c => !used.Contains(c)));

        var plans = new List<int[]?>(Tracks.Count);
        foreach (var t in Tracks)
        {
            if (t.Channel == 9 || !t.Notes.Any(n => n.QuarterBend != 0))
            {
                plans.Add(null);
                continue;
            }
            var auxByBend = new Dictionary<int, int>();
            var plan = new int[t.Notes.Count];
            for (int i = 0; i < t.Notes.Count; i++)
            {
                var n = t.Notes[i];
                if (n.QuarterBend == 0)
                {
                    plan[i] = n.Channel;
                    continue;
                }
                if (!auxByBend.TryGetValue(n.QuarterBend, out int aux))
                {
                    aux = free.Count > 0 ? free.Dequeue() : n.Channel;
                    auxByBend[n.QuarterBend] = aux;
                }
                plan[i] = aux;
            }
            plans.Add(plan);
        }
        return plans;
    }

    /// <summary>
    /// Saves the MIDI file to disk.
    /// </summary>
    public void Save(string path)
    {
        using var stream = File.Create(path);
        WriteTo(stream);
    }

    private void WriteHeader(BinaryWriter writer)
    {
        writer.Write((byte)'M');
        writer.Write((byte)'T');
        writer.Write((byte)'h');
        writer.Write((byte)'d');
        WriteBigEndian32(writer, 6);
        WriteBigEndian16(writer, 1);
        WriteBigEndian16(writer, (ushort)Tracks.Count);
        WriteBigEndian16(writer, (ushort)TicksPerQuarterNote);
    }

    private void WriteTrack(BinaryWriter writer, MidiTrack track, int[]? channelPlan = null)
    {
        using var trackStream = new MemoryStream();
        using var trackWriter = new BinaryWriter(trackStream);

        var events = BuildEventList(track, channelPlan);

        int lastTick = 0;
        foreach (var evt in events)
        {
            int delta = evt.Tick - lastTick;
            WriteVariableLength(trackWriter, delta);

            switch (evt.Type)
            {
                case MidiEventType.PitchBend:
                    trackWriter.Write((byte)(0xE0 | (evt.Channel & 0x0F)));
                    trackWriter.Write((byte)(evt.Data1 & 0x7F));
                    trackWriter.Write((byte)((evt.Data1 >> 7) & 0x7F));
                    break;
                case MidiEventType.NoteOn:
                    trackWriter.Write((byte)(0x90 | (evt.Channel & 0x0F)));
                    trackWriter.Write((byte)evt.Data1);
                    trackWriter.Write((byte)evt.Data2);
                    break;
                case MidiEventType.NoteOff:
                    trackWriter.Write((byte)(0x80 | (evt.Channel & 0x0F)));
                    trackWriter.Write((byte)evt.Data1);
                    trackWriter.Write((byte)0);
                    break;
                case MidiEventType.Tempo:
                    trackWriter.Write((byte)0xFF);
                    trackWriter.Write((byte)0x51);
                    trackWriter.Write((byte)0x03);
                    trackWriter.Write((byte)((evt.Data1 >> 16) & 0xFF));
                    trackWriter.Write((byte)((evt.Data1 >> 8) & 0xFF));
                    trackWriter.Write((byte)(evt.Data1 & 0xFF));
                    break;
                case MidiEventType.TimeSignature:
                    trackWriter.Write((byte)0xFF);
                    trackWriter.Write((byte)0x58);
                    trackWriter.Write((byte)0x04);
                    trackWriter.Write((byte)evt.Data1);
                    trackWriter.Write((byte)evt.Data2);
                    trackWriter.Write((byte)24);
                    trackWriter.Write((byte)8);
                    break;
                case MidiEventType.TrackName:
                    trackWriter.Write((byte)0xFF);
                    trackWriter.Write((byte)0x03);
                    var nameBytes = System.Text.Encoding.ASCII.GetBytes(track.Name);
                    WriteVariableLength(trackWriter, nameBytes.Length);
                    trackWriter.Write(nameBytes);
                    break;
                case MidiEventType.Lyric:
                    trackWriter.Write((byte)0xFF);
                    trackWriter.Write((byte)0x05); // Lyric meta event
                    var lyricBytes = System.Text.Encoding.UTF8.GetBytes(evt.Text ?? "");
                    WriteVariableLength(trackWriter, lyricBytes.Length);
                    trackWriter.Write(lyricBytes);
                    break;
            }
            lastTick = evt.Tick;
        }

        WriteVariableLength(trackWriter, 0);
        trackWriter.Write((byte)0xFF);
        trackWriter.Write((byte)0x2F);
        trackWriter.Write((byte)0x00);

        writer.Write((byte)'M');
        writer.Write((byte)'T');
        writer.Write((byte)'r');
        writer.Write((byte)'k');
        WriteBigEndian32(writer, (int)trackStream.Length);
        writer.Write(trackStream.ToArray());
    }

    private List<MidiEvent> BuildEventList(MidiTrack track, int[]? channelPlan = null)
    {
        var events = new List<MidiEvent>();

        if (!string.IsNullOrEmpty(track.Name))
            events.Add(new MidiEvent(0, MidiEventType.TrackName, track.Channel, 0, 0));

        foreach (var tempo in track.TempoChanges)
            events.Add(new MidiEvent(tempo.Tick, MidiEventType.Tempo, 0, tempo.MicrosecondsPerBeat, 0));

        foreach (var ts in track.TimeSignatures)
        {
            // Denominator is a power of two; take its exact log2 via trailing-zero
            // count. Floating-point Math.Log2 can return e.g. 2.9999… for 8 and
            // truncate to 2.
            int denomPow = ts.Denominator > 0
                ? System.Numerics.BitOperations.TrailingZeroCount((uint)ts.Denominator)
                : 2;
            events.Add(new MidiEvent(ts.Tick, MidiEventType.TimeSignature, 0, ts.Numerator, denomPow));
        }

        // Quarter tones: on the standard ±2-semitone (±8192-unit) bend range,
        // 50 cents = 2048 units. The channel plan routes bent notes to auxiliary
        // channels
        // (bend set once per channel); the fallback keeps in-channel bends,
        // switching the value only when it changes. Bend events sort BEFORE
        // note-on at the same tick (enum order).
        var channelBend = new Dictionary<int, int>();
        for (int ni = 0; ni < track.Notes.Count; ni++)
        {
            var note = track.Notes[ni];
            int channel = channelPlan != null ? channelPlan[ni] : note.Channel;
            int want = note.QuarterBend;
            if (channelBend.GetValueOrDefault(channel) != want)
            {
                events.Add(new MidiEvent(note.StartTick, MidiEventType.PitchBend,
                    channel, 8192 + want * 2048, 0));
                channelBend[channel] = want;
            }
            events.Add(new MidiEvent(note.StartTick, MidiEventType.NoteOn, channel, note.Pitch, note.Velocity));
            events.Add(new MidiEvent(note.StartTick + note.DurationTicks, MidiEventType.NoteOff, channel, note.Pitch, 0));
        }

        foreach (var lyric in track.Lyrics)
            events.Add(new MidiEvent(lyric.Tick, MidiEventType.Lyric, 0, 0, 0, lyric.Text));

        events.Sort((a, b) =>
        {
            int cmp = a.Tick.CompareTo(b.Tick);
            return cmp != 0 ? cmp : a.Type.CompareTo(b.Type);
        });

        return events;
    }

    private static void WriteBigEndian16(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteBigEndian32(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteVariableLength(BinaryWriter writer, int value)
    {
        if (value < 0) value = 0;
        var bytes = new List<byte> { (byte)(value & 0x7F) };
        value >>= 7;
        while (value > 0)
        {
            bytes.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        bytes.Reverse();
        foreach (var b in bytes) writer.Write(b);
    }

    private enum MidiEventType { NoteOff = 0, PitchBend = 1, NoteOn = 2, Tempo = 3, TimeSignature = 4, TrackName = 5, Lyric = 6 }
    private readonly record struct MidiEvent(int Tick, MidiEventType Type, int Channel, int Data1, int Data2, string? Text = null);
}