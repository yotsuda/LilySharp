// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Music;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Midi;

/// <summary>
/// The piece as a .mid file says it, made from the piece as Lily# plays it
/// (<see cref="MidiExporter.Export"/>). LILYSHARP-OWN: LilyPond writes no singer.
/// </summary>
/// <remarks>
/// <para>
/// The exporter's <see cref="MidiFile"/> is shaped for the preview and the singing engine: every
/// word lands on the first part's track with who sings it beside it, and a singer's harmony is no
/// note at all — <see cref="SungVoices"/> works it out for the engine. Written out as it stood
/// (until 2026-11-12, the owner's question "does the MIDI say the voices?"), a file had
/// <list type="bullet">
/// <item>its words on the first track, whichever part sings them;</item>
/// <item>every lyrics row of the score as an event of its own, so a score that prints the words,
/// a singer's words and the note names under one tune wrote three and four words on a note;</item>
/// <item>the printed word where a reading is sung (<c>は[わ]</c> wrote は);</item>
/// <item>no note for <c>singer carol on lead up 3</c>, and nothing of who sings.</item>
/// </list>
/// </para>
/// <para>
/// Here: each part's words go to that part's track, one word a note (the row a voice sings
/// first, then the first placed), as SUNG; each voice that sings other notes than its part's —
/// an interval, an octave — is a track of its own, named as the singer is (else as the voice's
/// key, <c>lead#2</c>), on its part's channel and sound, with its notes and its words; and a
/// track a voice sings says the voice as its instrument name (meta 0x04,
/// <see cref="MidiTrack.Instrument"/>). A staff Lily# made of a singer's lines
/// (<c>staff alice</c>, <see cref="Editing.SingerParts"/>) is named as the singer.
/// What a voice's <c>with …</c> and <c>@vibrato</c> say has no place in a .mid and is not written.
/// </para>
/// </remarks>
public static class MidiForFile
{
    /// <summary>The file's shape of <paramref name="midi"/>; <paramref name="midi"/> is not changed.</summary>
    public static MidiFile Shape(MidiFile midi, SyntaxNode root, RenderSpec? spec)
    {
        var file = new MidiFile { TicksPerQuarterNote = midi.TicksPerQuarterNote };
        foreach (var track in midi.Tracks)
        {
            var copy = new MidiTrack { Name = track.Name, Channel = track.Channel, Program = track.Program, Instrument = track.Instrument };
            copy.Notes.AddRange(track.Notes);
            copy.TempoChanges.AddRange(track.TempoChanges);
            copy.TimeSignatures.AddRange(track.TimeSignatures);
            copy.Pedals.AddRange(track.Pedals);
            file.Tracks.Add(copy);
        }

        // The words, each to its part's track, one a note.
        var taken = new HashSet<(MidiTrack, int)>();
        foreach (var (lyric, from) in midi.Tracks.SelectMany((t, i) => t.Lyrics.Select(l => (l, i))).OrderByDescending(x => x.l.Voiced))
        {
            var to = file.Tracks.FirstOrDefault(t => lyric.Part != null && t.Name == lyric.Part) ?? file.Tracks[from];
            if (taken.Add((to, lyric.Tick)))
                to.Lyrics.Add(new LyricEvent(lyric.Tick, lyric.Sung ?? lyric.Text));
        }
        foreach (var track in file.Tracks)
            track.Lyrics.Sort((a, b) => a.Tick.CompareTo(b.Tick));

        // The voices: who sings a part's own notes is said on its track; who sings others is a track.
        var secondsAt = MidiClock.SecondsOf(midi);
        int end = midi.EndTick;
        int TickAt(double seconds)
        {
            int lo = 0, hi = end + 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (secondsAt(mid) < seconds - 1e-7) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
        var nameOf = LyricSinger.NamesReaderOf(root, spec);
        var harmonies = new List<MidiTrack>();
        foreach (var voice in SungVoices.Read(midi, secondsAt))
        {
            if (file.Tracks.FirstOrDefault(t => t.Name == voice.Part) is not { } own || voice.Notes.Count == 0)
                continue;
            var ownAt = own.Notes.ToLookup(n => n.StartTick);
            var sung = voice.Notes.Select(n => (Note: n, Tick: TickAt(n.Start), End: TickAt(n.Start + n.Duration), Pitch: (int)Math.Round(n.Pitch))).ToList();
            if (sung.All(s => ownAt[s.Tick].Any(n => n.Pitch == s.Pitch)))
            {
                own.Instrument ??= voice.Singer;
                continue;
            }
            var track = new MidiTrack
            {
                Name = nameOf(voice.Track, voice.Part, voice.Line) ?? voice.Key ?? voice.Part,
                Channel = own.Channel, Program = own.Program, Instrument = voice.Singer,
            };
            foreach (var (note, tick, to, pitch) in sung)
            {
                if (pitch is < 0 or > 127 || to <= tick)
                    continue;
                int velocity = ownAt[tick].Select(n => n.Velocity).DefaultIfEmpty(MidiExporter.DefaultVelocity).First();
                track.Notes.Add(new MidiNote(own.Channel, pitch, velocity, tick, to - tick));
                if (!string.IsNullOrEmpty(note.Lyric))
                    track.Lyrics.Add(new LyricEvent(tick, note.Lyric));
            }
            if (track.Notes.Count > 0)
                harmonies.Add(track);
        }
        // After its part, in the order sung.
        foreach (var track in harmonies)
            file.Tracks.Insert(file.Tracks.FindLastIndex(t => t.Channel == track.Channel) + 1, track);

        foreach (var track in file.Tracks)
            if (LyricSinger.IsMadePart(root, track.Name))
                track.Name = track.Name[..track.Name.LastIndexOf(LyricSinger.ScopedMark, StringComparison.Ordinal)];
        return file;
    }
}
