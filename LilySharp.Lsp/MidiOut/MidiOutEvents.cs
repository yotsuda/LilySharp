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

/// <summary>One channel message at a time, in seconds from the playback's start.</summary>
public readonly record struct MidiOutEvent(double Time, byte Status, byte Data1, byte Data2)
{
    public int Channel => Status & 0x0F;
}

/// <summary>
/// The note list the preview plays (<see cref="PlaybackNote"/>, seconds) turned into the
/// channel messages a MIDI output port is sent, in order — the server-side twin of the
/// webview's <c>toEvents</c> (editors/vscode/src/webview/playbackCore.ts), with the same
/// rules so the external synth hears what the preview's synth hears: each pitched channel's
/// program once, at the start; a quarter tone as the key below bent up by the wheel, centred
/// after; at one instant a note's off before its own re-strike, the programs and bends before
/// the notes that need them. Pure; <see cref="MidiOutPlayer"/> does the clock.
/// </summary>
public static class MidiOutEvents
{
    public const int DrumChannel = 9;
    /// <summary>The channel the auditions strike on (the step keys, Alt+P, the caret): the
    /// last one, which a score's parts take only past fourteen pitched sounds.</summary>
    public const int AuditionChannel = 15;
    public const int BendCentre = 8192;
    /// <summary>One semitone of the wheel at a synth's default ±2 range.</summary>
    public const int BendPerSemitone = 4096;
    private const double BendEpsilon = 0.01;
    private const double MinDuration = 0.03;
    public const byte NoteOff = 0x80, NoteOn = 0x90, Control = 0xB0, Program = 0xC0, PitchBend = 0xE0;
    public const byte AllSoundOff = 120, AllNotesOff = 123;

    /// <summary>The program a timbre family sounds with when no part names one (the
    /// oscillator patches' families: 0 piano, 1 flute, 2 reed, 3 strings, 4 guitar, 5 bass,
    /// 6 brass, 7 organ, 8 voice, 9 drums): the nearest General MIDI voice.</summary>
    public static readonly int[] FamilyProgram = [0, 73, 71, 48, 24, 33, 61, 19, 52, 0];

    public static int ChannelOf(int channel, int timbre)
        => channel is >= 0 and <= 15 ? channel : timbre == 9 ? DrumChannel : 0;

    public static int ProgramOf(int program, int timbre)
        => program is >= 0 and <= 127 ? program
            : timbre >= 0 && timbre < FamilyProgram.Length ? FamilyProgram[timbre] : 0;

    private static int KindOrder(byte status) => (status & 0xF0) switch
    {
        NoteOff => 0,
        Program => 1,
        PitchBend => 2,
        _ => 3,
    };

    /// <summary>The events of <paramref name="notes"/> from <paramref name="offset"/>
    /// seconds into the piece on, timed from the offset; notes already past it are dropped
    /// whole.</summary>
    public static List<MidiOutEvent> From(IEnumerable<PlaybackNote> notes, double offset)
    {
        var events = new List<(MidiOutEvent E, int I)>();
        var programs = new Dictionary<int, int>();
        int serial = 0;
        void Add(double t, byte status, int d1, int d2)
            => events.Add((new MidiOutEvent(t, status, (byte)Math.Clamp(d1, 0, 127), (byte)Math.Clamp(d2, 0, 127)), serial++));

        foreach (var n in notes)
        {
            if (n.T < offset - 0.001) continue;
            int c = ChannelOf(n.C, n.I);
            if (c != DrumChannel && !programs.ContainsKey(c)) programs[c] = ProgramOf(n.G, n.I);
            double t = Math.Max(0, n.T - offset);
            double off = t + Math.Max(MinDuration, n.D);
            int key = (int)Math.Floor(n.P + BendEpsilon);
            double frac = n.P - key;
            byte on = (byte)(NoteOn | c), release = (byte)(NoteOff | c);
            if (Math.Abs(frac) >= BendEpsilon)
            {
                int value = Math.Clamp((int)Math.Round(BendCentre + frac * BendPerSemitone), 0, 16383);
                Add(t, (byte)(PitchBend | c), value & 0x7F, value >> 7);
                Add(off, (byte)(PitchBend | c), BendCentre & 0x7F, BendCentre >> 7);
            }
            Add(t, on, key, Math.Max(1, (int)Math.Round((double)n.V)));
            Add(off, release, key, 0);
        }
        foreach (var (c, g) in programs)
            Add(0, (byte)(Program | c), g, 0);
        return events
            .OrderBy(x => x.E.Time)
            .ThenBy(x => KindOrder(x.E.Status))
            .ThenBy(x => x.I)
            .Select(x => x.E)
            .ToList();
    }

    /// <summary>When the last note from <paramref name="offset"/> on ends, in seconds from
    /// the piece's start.</summary>
    public static double EndOf(IEnumerable<PlaybackNote> notes, double offset)
    {
        double end = offset;
        foreach (var n in notes)
        {
            if (n.T < offset - 0.001) continue;
            end = Math.Max(end, n.T + Math.Max(MinDuration, n.D));
        }
        return end;
    }

    /// <summary>The channels <paramref name="events"/> touch — what a stop has to silence.</summary>
    public static IReadOnlyList<int> ChannelsOf(IEnumerable<MidiOutEvent> events)
        => events.Select(e => e.Channel).Distinct().OrderBy(c => c).ToList();

    /// <summary>What a stop sends a channel: every note off, every sound off, the wheel
    /// centred — a synth left with a note hanging is the one thing a MIDI player must not do.</summary>
    public static IEnumerable<MidiOutEvent> Silence(int channel)
    {
        yield return new MidiOutEvent(0, (byte)(Control | channel), AllNotesOff, 0);
        yield return new MidiOutEvent(0, (byte)(Control | channel), AllSoundOff, 0);
        yield return new MidiOutEvent(0, (byte)(PitchBend | channel), BendCentre & 0x7F, BendCentre >> 7);
    }
}
