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

using System.Text;
using LilySharp.Core.Midi;

namespace LilySharp.Core.MusicXmlImport;

/// <summary>
/// Reads a Standard MIDI File into the Import IR (<see cref="ImportDocument"/>), the model the
/// MusicXML reader fills and <see cref="LysWriter"/> writes (owner's request 2026-11-12).
/// LILYSHARP-OWN: a .mid is a performance, not a page, so every notated thing is a reading.
/// </summary>
/// <remarks>
/// <para>What a .mid states and is taken as stated: the notes (key, onset, length), the meters,
/// the tempos, the key signatures, each track's name, sound (program) and words (lyric events),
/// and — as Lily# writes it (<c>Midi.MidiForFile</c>) — the voice that sings a track, its
/// instrument name.</para>
/// <para>What is READ INTO it, each said in the report where it changes the music:</para>
/// <list type="bullet">
/// <item>RHYTHM. Onsets and ends are brought to the nearer of a sixteenth's grid and an eighth
/// triplet's; a beat with a triplet point in it is written <c>tuplet 3/2 { }</c>. A played file
/// is quantized by this, a written one (Lily#'s own, a notation program's) comes back as it was.
/// Nothing shorter than a sixteenth survives.</item>
/// <item>VOICES. A part is one voice, and a second where notes overlap (a held bass under a
/// tune); what overlaps past two is shortened.</item>
/// <item>SPELLING. A key is one of twelve: its letter and accidental come from the key signature
/// (a minor key's raised sixth and seventh are sharps of the degree below).</item>
/// <item>THE KEY, where the file states none: the major key whose scale holds most of the notes.</item>
/// <item>THE CLEF: bass for a part that lies under middle C, else treble; percussion for channel 10.</item>
/// </list>
/// <para>Not read: dynamics from velocities, the pedal, pitch bends, pickup bars (a .mid has no
/// partial bar: the first bar is a whole one with rests), repeats (a .mid is played out).</para>
/// </remarks>
internal static class MidiReader
{
    /// <summary>Whether <paramref name="bytes"/> open as a Standard MIDI File.</summary>
    public static bool IsMidi(byte[] bytes)
        => bytes.Length >= 14 && bytes[0] == 'M' && bytes[1] == 'T' && bytes[2] == 'h' && bytes[3] == 'd';

    private sealed record RawNote(int Tick, int End, int Pitch, int Channel);

    private sealed class RawTrack
    {
        public string? Name, Instrument;
        public readonly Dictionary<int, int> Programs = new();
        public readonly List<RawNote> Notes = new();
        public readonly List<(int Tick, string Text)> Lyrics = new();
    }

    private sealed class Raw
    {
        public int Division;
        public readonly List<RawTrack> Tracks = new();
        public readonly SortedDictionary<int, int> Tempos = new();                          // tick → µs a quarter
        public readonly SortedDictionary<int, (int Beats, int BeatType)> Meters = new();
        public readonly SortedDictionary<int, (int Fifths, bool Minor)> Keys = new();
    }

    // ---- the bytes --------------------------------------------------------------------------

    private static Raw Parse(byte[] b)
    {
        if (!IsMidi(b))
            throw new InvalidDataException("Not a Standard MIDI File (no MThd header).");
        var raw = new Raw { Division = (b[12] << 8) | b[13] };
        if ((raw.Division & 0x8000) != 0)
            throw new InvalidDataException("This MIDI file counts time in SMPTE frames, not in beats: it has no bars to import.");
        if (raw.Division == 0)
            throw new InvalidDataException("This MIDI file states no ticks per quarter note.");
        int p = 8 + ((b[4] << 24) | (b[5] << 16) | (b[6] << 8) | b[7]);
        while (p + 8 <= b.Length)
        {
            int length = (b[p + 4] << 24) | (b[p + 5] << 16) | (b[p + 6] << 8) | b[p + 7];
            bool isTrack = b[p] == 'M' && b[p + 1] == 'T' && b[p + 2] == 'r' && b[p + 3] == 'k';
            int start = p + 8, end = Math.Min(b.Length, start + Math.Max(0, length));
            if (isTrack)
                raw.Tracks.Add(ParseTrack(b, start, end, raw));
            p = end;
        }
        return raw;
    }

    private static RawTrack ParseTrack(byte[] b, int q, int end, Raw raw)
    {
        var track = new RawTrack();
        var open = new Dictionary<(int Channel, int Pitch), Stack<int>>();
        int tick = 0, status = 0;
        int Variable()
        {
            int value = 0;
            while (q < end)
            {
                int c = b[q++];
                value = (value << 7) | (c & 0x7F);
                if ((c & 0x80) == 0)
                    break;
            }
            return value;
        }
        void Off(int channel, int pitch)
        {
            if (open.TryGetValue((channel, pitch), out var starts) && starts.Count > 0)
            {
                int from = starts.Pop();
                if (tick > from)
                    track.Notes.Add(new RawNote(from, tick, pitch, channel));
            }
        }
        while (q < end)
        {
            tick += Variable();
            if (q >= end)
                break;
            int s = b[q];
            if (s == 0xFF)
            {
                if (q + 1 >= end)
                    break;
                int type = b[q + 1];
                q += 2;
                int length = Variable();
                int at = q;
                q = Math.Min(end, q + length);
                int size = q - at;
                switch (type)
                {
                    case 0x03: track.Name ??= Text(b, at, size); break;
                    case 0x04: track.Instrument ??= Text(b, at, size); break;
                    case 0x05: track.Lyrics.Add((tick, Text(b, at, size))); break;
                    case 0x51 when size >= 3: raw.Tempos[tick] = (b[at] << 16) | (b[at + 1] << 8) | b[at + 2]; break;
                    case 0x58 when size >= 2: raw.Meters[tick] = (Math.Max(1, (int)b[at]), 1 << Math.Min(6, (int)b[at + 1])); break;
                    case 0x59 when size >= 2: raw.Keys[tick] = (Math.Clamp((int)(sbyte)b[at], -7, 7), b[at + 1] == 1); break;
                }
                continue;
            }
            if (s is 0xF0 or 0xF7)
            {
                q++;
                int length = Variable();
                q = Math.Min(end, q + length);
                continue;
            }
            if ((s & 0x80) != 0)
            {
                status = s;
                q++;
            }
            int kind = status & 0xF0, channel = status & 0x0F;
            int d1 = q < end ? b[q] : 0, d2 = q + 1 < end ? b[q + 1] : 0;
            switch (kind)
            {
                case 0x90 when d2 > 0:
                    if (!open.TryGetValue((channel, d1), out var starts))
                        open[(channel, d1)] = starts = new Stack<int>();
                    starts.Push(tick);
                    q += 2;
                    break;
                case 0x90:
                case 0x80:
                    Off(channel, d1);
                    q += 2;
                    break;
                case 0xC0:
                    track.Programs.TryAdd(channel, d1);
                    q += 1;
                    break;
                case 0xD0:
                    q += 1;
                    break;
                case 0xA0 or 0xB0 or 0xE0:
                    q += 2;
                    break;
                default:
                    q = end;     // a data byte with no status before it: the track is not readable further
                    break;
            }
        }
        // A note never released ends with its track.
        foreach (var ((channel, pitch), starts) in open)
            foreach (int from in starts)
                if (tick > from)
                    track.Notes.Add(new RawNote(from, tick, pitch, channel));
        return track;
    }

    private static string Text(byte[] b, int at, int size)
    {
        string text = new UTF8Encoding(false, false).GetString(b, at, size);
        if (text.Contains('�'))
        {
            // Not UTF-8: a Japanese sequencer's Shift_JIS, most often.
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(932).GetString(b, at, size);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { }
        }
        return text.Trim('\0', ' ', '\r', '\n', '\t');
    }

    // ---- the reading ------------------------------------------------------------------------

    /// <summary>Twelfths of a quarter note: a sixteenth is 3 of them, a triplet eighth 4.</summary>
    private const int Quarter = 12;

    private sealed record Event(int Start, int End, List<int> Pitches);

    public static ImportDocument Read(byte[] bytes, ImportReport report, out List<(string Part, string Voice)> singers)
    {
        var raw = Parse(bytes);
        var doc = new ImportDocument();
        singers = new List<(string, string)>();
        int Units(int tick) => (int)Math.Round(tick * (double)Quarter / raw.Division, MidpointRounding.AwayFromZero);

        // A point of the performance, on the page: the nearer of the sixteenths and the triplet eighths.
        static int Snap(int units)
        {
            int straight = (int)Math.Round(units / 3.0, MidpointRounding.AwayFromZero) * 3;
            int triplet = (int)Math.Round(units / 4.0, MidpointRounding.AwayFromZero) * 4;
            return Math.Abs(triplet - units) < Math.Abs(straight - units) ? triplet : straight;
        }

        // The parts: a track's notes on one channel (a format-0 file is one track of many channels).
        var sources = new List<(RawTrack Track, int Channel, List<RawNote> Notes)>();
        foreach (var track in raw.Tracks)
            foreach (var group in track.Notes.GroupBy(n => n.Channel).OrderBy(g => g.Key))
                sources.Add((track, group.Key, group.ToList()));
        if (sources.Count == 0)
            throw new InvalidDataException("This MIDI file holds no notes.");
        if (raw.Tracks.FirstOrDefault(t => t.Notes.Count == 0 && !string.IsNullOrWhiteSpace(t.Name)) is { } conductor
            && !System.Text.RegularExpressions.Regex.IsMatch(conductor.Name!, "(?i)tempo|conductor|control|track|untitled") && raw.Tracks.IndexOf(conductor) == 0)
            doc.Title = conductor.Name;

        // The bars, from the meters.
        int last = sources.Max(s => s.Notes.Max(n => Units(n.End)));
        var meters = new SortedDictionary<int, (int Beats, int BeatType)>();
        foreach (var (tick, meter) in raw.Meters)
            meters[Units(tick)] = meter;
        if (!meters.ContainsKey(0))
            meters[0] = meters.Count > 0 && meters.Keys.First() <= Quarter ? meters.Values.First() : (4, 4);
        var bars = new List<(int Start, int Length, (int Beats, int BeatType) Meter)>();
        for (int at = 0; at < last || bars.Count == 0;)
        {
            var meter = meters.Last(m => m.Key <= at).Value;
            int length = meter.Beats * Quarter * 4 / meter.BeatType;
            bars.Add((at, length, meter));
            at += length;
        }
        if (bars.Any(bar => bar.Length % 3 != 0))
            throw new InvalidDataException("This MIDI file has a meter finer than sixteenths (x/32 or x/64), which the import does not read.");

        // The key: stated, else the major key whose scale holds most of what is played.
        var keys = new SortedDictionary<int, (int Fifths, bool Minor)>();
        foreach (var (tick, key) in raw.Keys)
            keys[BarOf(bars, Units(tick))] = key;
        bool pitched = sources.Any(s => s.Channel != 9);
        if (keys.Count == 0 && pitched)
        {
            int best = 0;
            long bestScore = -1;
            foreach (int fifths in new[] { 0, 1, -1, 2, -2, 3, -3, 4, -4, 5, -5, 6, -6 })
            {
                var scale = ScaleOf(fifths, minor: false).Select(s => s.Class).ToHashSet();
                long score = sources.Where(s => s.Channel != 9).SelectMany(s => s.Notes)
                    .Where(n => scale.Contains(n.Pitch % 12)).Sum(n => (long)(n.End - n.Tick));
                if (score > bestScore)
                    (best, bestScore) = (fifths, score);
            }
            keys[0] = (best, false);
            report.Warn("the file states no key: " + (best == 0 ? "C major" : $"a key of {Math.Abs(best)} {(best > 0 ? "sharp" : "flat")}{(Math.Abs(best) == 1 ? "" : "s")}")
                + " was taken from the notes.");
        }
        else if (keys.Count > 0 && !keys.ContainsKey(0))
            keys[0] = keys.Values.First();

        if (raw.Tempos.Count > 0)
            doc.Tempo = Bpm(raw.Tempos.Values.First());

        var used = new HashSet<string>(StringComparer.Ordinal);
        int moved = 0, shortened = 0, lostWords = 0, tiny = 0;
        foreach (var (track, channel, notes) in sources)
        {
            bool drums = channel == 9;
            string? name = !string.IsNullOrWhiteSpace(track.Name) ? track.Name
                : drums ? "Drums"
                : track.Programs.TryGetValue(channel, out int named) && named < GeneralMidi.InstrumentNames.Count ? GeneralMidi.InstrumentNames[named] : null;
            if (raw.Tracks.Count == 1 && sources.Count > 1 && track.Name != null && !drums)
                name = track.Programs.TryGetValue(channel, out int sound) && sound < GeneralMidi.InstrumentNames.Count ? GeneralMidi.InstrumentNames[sound] : $"{track.Name} {channel + 1}";
            var part = new ImportPart { Id = "T" + (doc.Parts.Count + 1), Name = name, Label = name };
            part.SafeName = MusicXmlReader.SafeIdentifier(name, doc.Parts.Count, used);
            if (!drums && track.Programs.TryGetValue(channel, out int program) && program is > 0 and < 128 && program < GeneralMidi.InstrumentNames.Count)
                part.MidiInstrument = GeneralMidi.InstrumentNames[program];
            part.Clef = drums ? "percussion" : notes.Average(n => (double)n.Pitch) < 57 ? "bass" : "treble";

            // The notes on the grid.
            var onGrid = new List<(int Start, int End, int Pitch)>();
            foreach (var n in notes)
            {
                if (drums && !DrumNames.ContainsKey(n.Pitch))
                    continue;
                int exact = Units(n.Tick), start = Snap(exact), end = Snap(Units(n.End));
                if ((n.End - n.Tick) * (double)Quarter / raw.Division < 2.4 && !drums)
                    tiny++;
                else if (Math.Abs(n.Tick * (double)Quarter / raw.Division - start) > 0.01)
                    moved++;
                if (end <= start)
                    end = start + (start % 3 == 0 ? 3 : 4);
                onGrid.Add((start, end, n.Pitch));
            }
            if (drums)
            {
                // A drum is struck, not held: a hit lasts to the next one, a quarter at most.
                var hits = onGrid.Select(n => n.Start).Distinct().Order().ToList();
                onGrid = onGrid.Select(n =>
                {
                    int next = hits.FirstOrDefault(h => h > n.Start, n.Start + Quarter);
                    return (n.Start, Math.Min(next, n.Start + Quarter - n.Start % Quarter), n.Pitch);
                }).ToList();
            }

            // One voice, and a second where notes overlap.
            var voices = new[] { new List<Event>(), new List<Event>() };
            foreach (var n in onGrid.Distinct().OrderBy(n => n.Start).ThenByDescending(n => n.Pitch))
            {
                bool placed = false;
                foreach (var voice in voices)
                {
                    var current = voice.Count > 0 ? voice[^1] : null;
                    if (current != null && current.Start == n.Start && current.End == n.End)
                    {
                        if (!current.Pitches.Contains(n.Pitch))
                            current.Pitches.Add(n.Pitch);
                        placed = true;
                    }
                    else if (current == null || current.End <= n.Start)
                    {
                        voice.Add(new Event(n.Start, n.End, [n.Pitch]));
                        placed = true;
                    }
                    if (placed)
                        break;
                }
                if (placed)
                    continue;
                // A third line: sung with the first voice's chord where it begins with it, else the
                // first voice gives way.
                shortened++;
                var top = voices[0][^1];
                if (top.Start == n.Start)
                {
                    if (!top.Pitches.Contains(n.Pitch))
                        top.Pitches.Add(n.Pitch);
                }
                else
                {
                    voices[0][^1] = top with { End = n.Start };
                    voices[0].Add(new Event(n.Start, n.End, [n.Pitch]));
                }
            }

            // The words, on the notes they begin with.
            var words = new Dictionary<int, string>();
            foreach (var (tick, text) in track.Lyrics)
                if (text.Length > 0 && !words.TryAdd(Snap(Units(tick)), text))
                    lostWords++;
            var onsets = voices[0].Select(e => e.Start).ToHashSet();
            lostWords += words.Keys.Count(k => !onsets.Contains(k));
            if (words.Count > 0 && track.Instrument is { Length: > 0 } voiceName && !drums)
                singers.Add((part.SafeName, voiceName));

            for (int bar = 0; bar < bars.Count; bar++)
            {
                var (start, length, meter) = bars[bar];
                var measure = new ImportMeasure();
                if (bar == 0 || bars[bar - 1].Meter != meter)
                    measure.Time = new ImportTime(meter.Beats, meter.BeatType);
                if (keys.TryGetValue(bar, out var key) && !drums)
                    measure.Key = new ImportKey(key.Fifths, key.Minor ? "minor" : "major");
                if (doc.Parts.Count == 0 && bar > 0)
                {
                    // A tempo that changes in a bar is heard from that bar's head.
                    int? bpm = raw.Tempos.Where(t => BarOf(bars, Units(t.Key)) == bar).Select(t => (int?)Bpm(t.Value)).LastOrDefault();
                    if (bpm != null)
                        measure.Tempo = bpm;
                }
                var keyHere = keys.Count > 0 ? keys.Last(k => k.Key <= bar).Value : (Fifths: 0, Minor: false);
                for (int v = 0; v < voices.Length; v++)
                {
                    var inBar = voices[v].Where(e => e.End > start && e.Start < start + length).ToList();
                    if (inBar.Count == 0 && v > 0)
                        continue;
                    WriteBar(measure.Voice(v + 1), inBar, start, length, keyHere, drums, v == 0 ? words : null);
                }
                part.Measures.Add(measure);
            }
            doc.Parts.Add(part);
        }

        if (moved > 0)
            report.Warn($"{moved} note{(moved == 1 ? "" : "s")} did not begin on a sixteenth or a triplet eighth and {(moved == 1 ? "was" : "were")} brought to the nearest: a played file is quantized.");
        if (tiny > 0)
            report.Warn($"{tiny} note{(tiny == 1 ? "" : "s")} shorter than a sixteenth (an ornament, a thirty-second) {(tiny == 1 ? "was" : "were")} written as a sixteenth or joined the note beside {(tiny == 1 ? "it" : "them")}.");
        if (shortened > 0)
            report.Warn($"{shortened} note{(shortened == 1 ? "" : "s")} sounded over two other lines of {(shortened == 1 ? "its" : "their")} part and {(shortened == 1 ? "was" : "were")} written with the upper voice: a part is read as two voices at most.");
        if (lostWords > 0)
            report.Warn($"{lostWords} word{(lostWords == 1 ? "" : "s")} stood where no note of {(lostWords == 1 ? "its" : "their")} track begins, or on a note that had one, and {(lostWords == 1 ? "was" : "were")} left out.");
        if (raw.Tempos.Count > 1 && raw.Tempos.Keys.Skip(1).Any(t => Units(t) != bars[BarOf(bars, Units(t))].Start))
            report.Warn("a tempo that changes inside a bar is written at the head of its bar.");
        return doc;
    }

    private static int Bpm(int microsecondsPerQuarter) => (int)Math.Round(60_000_000.0 / Math.Max(1, microsecondsPerQuarter));

    private static int BarOf(List<(int Start, int Length, (int, int) Meter)> bars, int units)
    {
        int at = bars.FindLastIndex(bar => bar.Start <= units);
        return Math.Max(0, at);
    }

    /// <summary>One voice's bar: its events and the rests between them, as notes of written values.</summary>
    private static void WriteBar(List<ImportItem> items, List<Event> events, int barStart, int barLength,
        (int Fifths, bool Minor) key, bool drums, Dictionary<int, string>? words)
    {
        if (events.Count == 0)
        {
            items.Add(new ImportNote { IsRest = true, IsMeasureRest = true, NoteValue = 1 });
            return;
        }
        // The bar as spans: what sounds and what is silent, clipped to it.
        var spans = new List<(int From, int To, Event? Sounding, bool Head, bool Tail)>();     // Head, Tail: it begins, ends in this bar
        int at = barStart;
        foreach (var e in events)
        {
            int from = Math.Max(e.Start, barStart), to = Math.Min(e.End, barStart + barLength);
            if (from > at)
                spans.Add((at, from, null, true, true));
            if (to > from)
                spans.Add((from, to, e, e.Start >= barStart, e.End <= barStart + barLength));
            at = Math.Max(at, to);
        }
        if (at < barStart + barLength)
            spans.Add((at, barStart + barLength, null, true, true));

        // Beat by beat: a beat with a triplet point in it is a triplet, the runs between are straight.
        for (int beat = barStart; beat < barStart + barLength;)
        {
            bool Triplet(int b) => b + Quarter <= barStart + barLength
                && spans.Any(s => (s.From > b && s.From < b + Quarter && (s.From - b) % 3 != 0) || (s.To > b && s.To < b + Quarter && (s.To - b) % 3 != 0));
            if (Triplet(beat))
            {
                var pieces = new List<(int Slots, Event? Sounding, bool First, bool Last)>();
                foreach (var s in spans.Where(s => s.To > beat && s.From < beat + Quarter))
                {
                    int from = (int)Math.Round((Math.Max(s.From, beat) - beat) / 4.0), to = (int)Math.Round((Math.Min(s.To, beat + Quarter) - beat) / 4.0);
                    if (to > from)
                        pieces.Add((to - from, s.Sounding, s.Head && s.From >= beat, s.Tail && s.To <= beat + Quarter));
                }
                for (int i = 0; i < pieces.Count; i++)
                {
                    var (slots, sounding, first, lastOfNote) = pieces[i];
                    // Three slots cannot be here (the beat would have no triplet point); two is a quarter.
                    var written = Notes(sounding, slots >= 2 ? 4 : 8, 0, key, drums, tieFrom: !first, tieTo: !lastOfNote,
                        first && sounding != null && words != null && words.TryGetValue(sounding.Start, out var w) ? w : null);
                    if (i == 0)
                        written[0].TupletStart = (3, 2);
                    if (i == pieces.Count - 1)
                        written[0].TupletStop = true;
                    items.AddRange(written);
                }
                beat += Quarter;
                continue;
            }
            int runEnd = beat + Quarter;
            while (runEnd < barStart + barLength && !Triplet(runEnd))
                runEnd += Quarter;
            runEnd = Math.Min(runEnd, barStart + barLength);
            foreach (var s in spans.Where(s => s.To > beat && s.From < runEnd))
            {
                // In sixteenths from the bar's head. A point inside a straight beat is on a sixteenth.
                int from = (Math.Max(s.From, beat) - barStart) / 3, to = (Math.Min(s.To, runEnd) - barStart + 2) / 3;
                bool begins = s.Head && s.From >= beat, ends = s.Tail && s.To <= runEnd;
                var lengths = Values(from, to, rest: s.Sounding == null);
                for (int i = 0; i < lengths.Count; i++)
                {
                    var (value, dots) = lengths[i];
                    items.AddRange(Notes(s.Sounding, value, dots, key, drums,
                        tieFrom: i > 0 || !begins, tieTo: i < lengths.Count - 1 || !ends,
                        i == 0 && begins && s.Sounding != null && words != null && words.TryGetValue(s.Sounding.Start, out var w) ? w : null));
                }
            }
            beat = runEnd;
        }
    }

    /// <summary>The written values that fill [from, to), in sixteenths from the bar's head: the
    /// longest that begins where such a value may, again and again.</summary>
    private static List<(int Value, int Dots)> Values(int from, int to, bool rest)
    {
        // (sixteenths, value, dots, the grid its start must lie on)
        (int Length, int Value, int Dots, int Grid)[] notes =
            [(16, 1, 0, 8), (12, 2, 1, 4), (8, 2, 0, 4), (6, 4, 1, 2), (4, 4, 0, 2), (3, 8, 1, 2), (2, 8, 0, 1), (1, 16, 0, 1)];
        (int Length, int Value, int Dots, int Grid)[] rests =
            [(16, 1, 0, 16), (8, 2, 0, 8), (4, 4, 0, 4), (2, 8, 0, 2), (1, 16, 0, 1)];
        var written = new List<(int, int)>();
        while (from < to)
        {
            var fit = (rest ? rests : notes).First(v => v.Length <= to - from && from % v.Grid == 0);
            written.Add((fit.Value, fit.Dots));
            from += fit.Length;
        }
        return written;
    }

    private static List<ImportNote> Notes(Event? sounding, int value, int dots, (int Fifths, bool Minor) key, bool drums,
        bool tieFrom, bool tieTo, string? word)
    {
        if (sounding == null)
            return [new ImportNote { IsRest = true, NoteValue = value, Dots = dots }];
        var notes = new List<ImportNote>();
        // Low to high, as a chord is written.
        foreach (int pitch in sounding.Pitches.Order())
        {
            var note = new ImportNote { NoteValue = value, Dots = dots, ChordWithPrev = notes.Count > 0, TieStart = tieTo && !drums, TieStop = tieFrom && !drums };
            if (drums)
                note.DrumName = DrumNames[pitch];
            else
                (note.Step, note.Alter, note.Octave) = Spell(pitch, key);
            notes.Add(note);
        }
        // A struck drum is not tied on: what is left of its span is silence.
        if (drums && tieFrom)
            return [new ImportNote { IsRest = true, NoteValue = value, Dots = dots }];
        if (word != null)
            notes[0].Lyrics.Add(new ImportLyric(1, word.TrimEnd('-'), word.EndsWith('-') ? "begin" : "single", false));
        return notes;
    }

    // ---- spelling ---------------------------------------------------------------------------

    private static readonly int[] StepClass = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The seven notes of a key signature: each letter with its accidental and its key.</summary>
    private static List<(int Step, int Alter, int Class)> ScaleOf(int fifths, bool minor)
    {
        int[] sharps = [3, 0, 4, 1, 5, 2, 6], flats = [6, 2, 5, 1, 4, 0, 3];
        var alters = new int[7];
        for (int i = 0; i < Math.Abs(fifths) && i < 7; i++)
            alters[fifths > 0 ? sharps[i] : flats[i]] = fifths > 0 ? 1 : -1;
        return Enumerable.Range(0, 7).Select(step => (step, alters[step], ((StepClass[step] + alters[step]) % 12 + 12) % 12)).ToList();
    }

    private static (int Step, int Alter, int Octave) Spell(int pitch, (int Fifths, bool Minor) key)
    {
        int pc = pitch % 12;
        var scale = ScaleOf(key.Fifths, key.Minor);
        (int Step, int Alter)? spelled = null;
        foreach (var (step, alter, cls) in scale)
            if (cls == pc)
                spelled = (step, alter);
        if (spelled == null)
        {
            // The raised sixth and seventh of the signature's minor key: the degree below, a
            // semitone up — C sharp in one flat, whether the file calls it F major or D minor
            // (Lily#'s own .mid states the major of every key).
            int tonic = ((key.Fifths * 7 + 9) % 12 + 12) % 12;
            if (pc == (tonic + 11) % 12 || pc == (tonic + 9) % 12)
                foreach (var (step, alter, cls) in scale)
                    if (cls == (pc + 11) % 12 && alter < 1)
                        spelled = (step, alter + 1);
        }
        // Outside the key: the plain letter where there is one (E in B flat major is E natural)…
        if (spelled == null && Array.IndexOf(StepClass, pc) is >= 0 and var plain)
            spelled = (plain, 0);
        if (spelled == null)
        {
            // …else a sharp in a sharp key (and C), a flat in a flat key.
            foreach (var (step, alter, cls) in scale)
                if (key.Fifths >= 0 ? cls == (pc + 11) % 12 && alter < 1 : cls == (pc + 1) % 12 && alter > -1)
                    spelled = (step, alter + (key.Fifths >= 0 ? 1 : -1));
            spelled ??= key.Fifths >= 0 ? (Array.IndexOf(StepClass, pc - 1), 1) : (Array.IndexOf(StepClass, pc + 1), -1);
        }
        var (s, a) = spelled.Value;
        return (s, a, (pitch - a - StepClass[s]) / 12 - 1);
    }

    // General MIDI's percussion keys as Lily#'s drum names (Music.PhraseSummary's table).
    private static readonly Dictionary<int, string> DrumNames = new()
    {
        [35] = "bda", [36] = "bd", [37] = "ss", [38] = "sn", [39] = "hc", [40] = "sne",
        [41] = "tomfl", [42] = "hh", [43] = "tomfh", [44] = "hhp", [45] = "toml", [46] = "hho",
        [47] = "tomml", [48] = "tommh", [49] = "cymc", [50] = "tomh", [51] = "cymr", [52] = "cymch",
        [53] = "rb", [54] = "tamb", [55] = "cyms", [56] = "cb", [57] = "cymcb", [59] = "cymrb",
    };
}
