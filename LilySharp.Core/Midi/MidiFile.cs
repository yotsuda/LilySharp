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
    public string Name { get; set; } = "";
    public int Channel { get; set; }
    public List<MidiNote> Notes { get; } = [];
    public List<TempoChange> TempoChanges { get; } = [];
    public List<TimeSignatureChange> TimeSignatures { get; } = [];
    public List<LyricEvent> Lyrics { get; } = [];
}

/// <summary>
/// Represents a complete MIDI file.
/// </summary>
public class MidiFile
{
    public const int DefaultTicksPerQuarter = 480;

    public int TicksPerQuarterNote { get; set; } = DefaultTicksPerQuarter;
    public List<MidiTrack> Tracks { get; } = [];

    /// <summary>
    /// Writes the MIDI file to a stream.
    /// </summary>
    public void WriteTo(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        WriteHeader(writer);
        foreach (var track in Tracks)
        {
            WriteTrack(writer, track);
        }
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

    private void WriteTrack(BinaryWriter writer, MidiTrack track)
    {
        using var trackStream = new MemoryStream();
        using var trackWriter = new BinaryWriter(trackStream);

        var events = BuildEventList(track);

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

    private List<MidiEvent> BuildEventList(MidiTrack track)
    {
        var events = new List<MidiEvent>();

        if (!string.IsNullOrEmpty(track.Name))
            events.Add(new MidiEvent(0, MidiEventType.TrackName, track.Channel, 0, 0));

        foreach (var tempo in track.TempoChanges)
            events.Add(new MidiEvent(tempo.Tick, MidiEventType.Tempo, 0, tempo.MicrosecondsPerBeat, 0));

        foreach (var ts in track.TimeSignatures)
        {
            int denomPow = (int)Math.Log2(ts.Denominator);
            events.Add(new MidiEvent(ts.Tick, MidiEventType.TimeSignature, 0, ts.Numerator, denomPow));
        }

        bool bentNow = false;
        foreach (var note in track.Notes)
        {
            // Quarter tones: bend before the note-on, reset when a later note
            // has none. ±50 cents = ±1024 on the standard ±2-semitone range.
            // Sorted BEFORE note-on at the same tick (enum order).
            if (note.QuarterBend != 0)
            {
                events.Add(new MidiEvent(note.StartTick, MidiEventType.PitchBend,
                    note.Channel, 8192 + note.QuarterBend * 1024, 0));
                bentNow = true;
            }
            else if (bentNow)
            {
                events.Add(new MidiEvent(note.StartTick, MidiEventType.PitchBend,
                    note.Channel, 8192, 0));
                bentNow = false;
            }
            events.Add(new MidiEvent(note.StartTick, MidiEventType.NoteOn, note.Channel, note.Pitch, note.Velocity));
            events.Add(new MidiEvent(note.StartTick + note.DurationTicks, MidiEventType.NoteOff, note.Channel, note.Pitch, 0));
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