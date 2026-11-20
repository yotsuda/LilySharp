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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.Editing;
using LilySharp.Core.Midi;
using LilySharp.Core.Parser;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Export;

/// <summary>One play of a section, in the song's beats (the lower number of its time), upbeat included.</summary>
/// <param name="Name">The section's name.</param>
/// <param name="Beats">How long it lasts as it is played.</param>
/// <param name="Pickup">Beats of it that come before its first bar line (an upbeat); 0 when it starts on a bar.</param>
public sealed record SongPlay(string Name, double Beats, double Pickup);

/// <summary>A tempo as the song is played: from <paramref name="Beat"/> on, <paramref name="Bpm"/>
/// beats a minute, a beat being the lower number of the time in force there.</summary>
public sealed record SongTempo(double Beat, double Bpm);

/// <summary>A time as the song is played, from <paramref name="Beat"/> on.</summary>
public sealed record SongMeter(double Beat, int BeatsPerBar, int BeatUnit);

/// <summary>
/// What a program that keeps time with a song needs to know of it (<c>lysc timeline</c>): how fast,
/// in what time, which sections, in what order and how long each is as it is played. Its JSON
/// (camelCase) is an agreement with the programs that read it — Shosa dances to it — and its
/// version is <see cref="SongReading.Protocol"/>.
/// </summary>
public sealed class SongTimeline
{
    /// <summary>Why the song could not be read, for the person who asked; null when it was.</summary>
    public string? Error { get; set; }
    /// <summary>Beats per minute at the start, a beat being the lower number of the time.</summary>
    public double Tempo { get; set; }
    public int BeatsPerBar { get; set; } = 4;
    public int BeatUnit { get; set; } = 4;
    /// <summary>Every section the song has, played or not.</summary>
    public List<string> Sections { get; set; } = [];
    /// <summary>The sections as they are played: the order asked for, or the score's own.</summary>
    public List<SongPlay> Plays { get; set; } = [];
    /// <summary>The song's scores ("" is the unnamed one).</summary>
    public List<string> Scores { get; set; } = [];
    /// <summary>Every tempo as the song is played, the first at beat 0, each in the beat of the time in force there.</summary>
    public List<SongTempo> Tempos { get; set; } = [];
    /// <summary>Every time as the song is played, the first at beat 0.</summary>
    public List<SongMeter> Meters { get; set; } = [];
    /// <summary>What is sung, as Lily# sings it (Shosa's request 6, 2026-11-21: a dancer's mouth
    /// moves to it): one for each voice, in the order their parts first sing; empty where nobody sings.</summary>
    public List<SongSung> Sung { get; set; } = [];
}

/// <summary>
/// One voice as it is sung: the part whose notes it sings (<c>singer … on PART</c>; for a singer
/// the score prints on a staff of her own, the part her line names), the singer's own name
/// (<c>singer alice …</c>) or null, and its syllables in the order they are sung, each
/// [beat it starts at, beats the voice sounds it, the text as the voice is handed it]. A syllable
/// held over several notes (a melisma, <c>__</c>) lasts until its last note ends; a rest ends it.
/// </summary>
public sealed record SongSung(string Part, string? Singer, List<object[]> Syllables);

/// <summary>
/// A song as it will sound, read for a program that keeps time with it (<c>lysc timeline</c>,
/// <c>lysc notes</c>): the order of its sections and its tempo may be the caller's rather than
/// its own (<c>--order</c>, <c>--tempo</c>, which <c>lysc midi</c> and <c>lysc wav</c> take too).
/// </summary>
/// <remarks>
/// Brought over from Shosa's <c>shosa-music</c> (Shosa's request 1, 2026-11-20; the same author),
/// so that the program reading a song is the one that knows its grammar.
/// </remarks>
public sealed class SongReading
{
    /// <summary>
    /// The version of what <c>lysc timeline</c> and <c>lysc notes</c> write, given by
    /// <c>lysc --version --json</c>. Raised only when a field changes its meaning or goes away;
    /// a new field does not raise it (a reader ignores what it does not know).
    /// </summary>
    public const int Protocol = 1;

    /// <summary>The name of the form an asked order is written into the song as.</summary>
    private const string OrderForm = "LysPlayOrder__";

    public SongTimeline Timeline { get; } = new();
    public MidiFile? Midi { get; private set; }

    /// <summary>Reads the song at <paramref name="path"/>. Never throws: what went wrong is <see cref="SongTimeline.Error"/>.</summary>
    /// <param name="score">One of the song's scores; null or "" for its default.</param>
    /// <param name="order">Section names to play instead of the score's own form.</param>
    /// <param name="tempo">Beats per minute to start at instead of the song's own; a song that changes tempo keeps its proportions.</param>
    public static SongReading FromFile(string path, string? score, IReadOnlyList<string>? order, double? tempo = null)
    {
        var song = new SongReading();
        try
        {
            song.Read(path, score, order, tempo);
        }
        catch (Exception ex)
        {
            song.Timeline.Error = $"{Path.GetFileName(path)} could not be read: {ex.Message}";
        }
        return song;
    }

    /// <summary>
    /// The song with <paramref name="order"/> written into it as one more form, and that form:
    /// Lily# plays a form of its own tree, so every rule of how sections follow one another is
    /// its. Names the song has no section of are left out; null when none is left
    /// (<paramref name="error"/> says so).
    /// </summary>
    public static (SyntaxTree Tree, FormDeclarationSyntax Form)? WithOrder(
        string source, SyntaxTree tree, IReadOnlyList<string> order, string name, out string? error)
    {
        var sections = tree.GetNodes<SectionDeclarationSyntax>().Select(s => s.SectionName).Distinct().ToList();
        var known = order.Where(sections.Contains).ToList();
        if (known.Count == 0)
        {
            error = sections.Count == 0
                ? $"{name} has no section to play."
                : $"None of the sections asked for is in {name}. It has: {string.Join(", ", sections)}.";
            return null;
        }
        error = null;
        var ordered = SyntaxTree.Parse(source + $"\nform {OrderForm} {{ {string.Join(' ', known)} }}\n");
        return (ordered, ordered.GetNodes<FormDeclarationSyntax>().Last(f => f.NameText == OrderForm));
    }

    /// <summary>
    /// Every tempo of <paramref name="midi"/> scaled so that the first is <paramref name="bpm"/>
    /// (a beat being the lower number of the first time): the ticks, and so the beats, stay where they are.
    /// </summary>
    public static void Retime(MidiFile midi, double bpm)
    {
        double first = FirstTempo(midi);
        if (first <= 0 || bpm <= 0)
            return;
        double factor = bpm / first;
        foreach (var track in midi.Tracks)
        {
            for (int i = 0; i < track.TempoChanges.Count; i++)
                track.TempoChanges[i] = new TempoChange(track.TempoChanges[i].Tick, (int)Math.Round(track.TempoChanges[i].MicrosecondsPerBeat / factor));
        }
        // A song that says no tempo plays at MIDI's 120 a quarter: the asked one is written at its start.
        if (!midi.Tracks.Any(t => t.TempoChanges.Any(c => c.Tick == 0)) && midi.Tracks.Count > 0)
            midi.Tracks[0].TempoChanges.Insert(0, new TempoChange(0, (int)Math.Round(500000 / factor)));
    }

    /// <summary>The tempo at tick 0 in beats a minute, a beat being the lower number of the time there.</summary>
    private static double FirstTempo(MidiFile midi)
    {
        var times = midi.Tracks.SelectMany(t => t.TimeSignatures).Where(t => t.Tick == 0).ToList();
        int unit = times.Count > 0 ? times[^1].Denominator : 4;
        var tempos = midi.Tracks.SelectMany(t => t.TempoChanges).Where(t => t.Tick == 0).ToList();
        double microsecondsPerQuarter = tempos.Count > 0 ? tempos[^1].MicrosecondsPerBeat : 500000;
        return 60e6 / microsecondsPerQuarter * unit / 4;
    }

    private void Read(string path, string? score, IReadOnlyList<string>? order, double? tempo)
    {
        string name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            Timeline.Error = $"There is no file {name}.";
            return;
        }

        // As lysc reads a file: `using` first, then the staves the singers need.
        string source = File.ReadAllText(path);
        if (UsingExpander.HasUsings(source))
            source = UsingExpander.Expand(source, path, p => File.Exists(p) ? File.ReadAllText(p) : null, out _);
        var tree = SyntaxTree.Parse(source);
        if (SingerParts.Wanted(tree))
        {
            source = SingerParts.Expand(source, tree, out _);
            tree = SyntaxTree.Parse(source);
        }
        if (tree.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } error)
        {
            int line = 1 + source.AsSpan(0, Math.Min(error.Span.Start, source.Length)).Count('\n');
            Timeline.Error = $"{name} has errors (line {line}: {error.Message}). Check it with lysc check first.";
            return;
        }

        Timeline.Sections = tree.GetNodes<SectionDeclarationSyntax>().Select(s => s.SectionName).Distinct().ToList();
        Timeline.Scores = RenderSpecParser.FindAll(tree).Select(s => s.Name ?? "").Distinct().ToList();
        if (Timeline.Sections.Count == 0)
        {
            Timeline.Error = $"{name} has no section to play.";
            return;
        }

        FormDeclarationSyntax? orderForm = null;
        if (order is not null)
        {
            if (WithOrder(source, tree, order, name, out string? orderError) is not { } ordered)
            {
                Timeline.Error = orderError;
                return;
            }
            (tree, orderForm) = ordered;
        }

        RenderSpec? spec = null;
        if (!string.IsNullOrEmpty(score))
        {
            spec = RenderSpecParser.FindByName(tree, score);
            if (spec is null)
            {
                Timeline.Error = $"{name} has no score named '{score}'. It has: {string.Join(", ", Timeline.Scores.Select(s => s == "" ? "(the unnamed one)" : s))}.";
                return;
            }
        }

        var midi = new MidiExporter { Form = orderForm ?? spec?.Form, Score = spec }.Export(tree);
        if (tempo is { } asked)
            Retime(midi, asked);
        Midi = midi;
        Measure(midi);
        Timeline.Sung = SungOf(tree, spec, midi);
    }

    /// <summary>
    /// The voices <paramref name="midi"/> sings, as the WAV export and the preview sing them
    /// (<see cref="ScoreExport.SungVoicesOf"/>: which lines, which sections, which verse), their
    /// syllables in the timeline's beats. Counted from the voices' own seconds and back, so a note
    /// that gives up room for an <c>@breath</c> is as short here as it is sung.
    /// </summary>
    private List<SongSung> SungOf(SyntaxTree tree, RenderSpec? spec, MidiFile midi)
    {
        var voices = ScoreExport.SungVoicesOf(tree, spec, midi);
        if (voices.Count == 0)
            return [];
        var root = tree.GetRoot();
        var names = Music.LyricSinger.NamesReaderOf(root, spec ?? RenderSpecParser.PlayedSpec(tree, null, null));
        var lines = Music.LyricSinger.LinesOf(root);
        var seconds = MidiClock.SecondsOf(midi);
        int last = Math.Max(midi.EndTick, 1);
        // Seconds back to a beat: the tick they fall at (the clock only goes forward), then its beat.
        double BeatAt(double at)
        {
            int lo = 0, hi = last;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                if (seconds(mid) <= at) lo = mid; else hi = mid - 1;
            }
            double from = seconds(lo), to = seconds(lo + 1);
            double fraction = to > from ? Math.Clamp((at - from) / (to - from), 0, 1) : 0;
            return Beat(lo) + fraction * (Beat(lo + 1) - Beat(lo));
        }

        var sung = new List<SongSung>(voices.Count);
        foreach (var voice in voices)
        {
            string part = voice.Part;
            string? person = names(voice.Track, voice.Part, voice.Line);
            // A singer the score prints sings a staff made for her: the part is the one her line names.
            if (Music.LyricSinger.IsMadePart(root, part))
            {
                string her = part[..part.LastIndexOf(Music.LyricSinger.ScopedMark, StringComparison.Ordinal)];
                person ??= her;
                part = (lines.FirstOrDefault(l => l.Person == her && l.Track == voice.Track && l.Part != null)
                    ?? lines.FirstOrDefault(l => l.Person == her && l.Part != null))?.Part ?? her;
            }
            var syllables = new List<object[]>();
            double start = 0, end = 0;
            string? text = null;
            void Close()
            {
                if (text != null)
                    syllables.Add([Math.Round(BeatAt(start), 4), Math.Round(BeatAt(end) - BeatAt(start), 4), text]);
            }
            foreach (var note in voice.Notes)
            {
                if (note.Lyric != null && !note.Melisma)
                {
                    Close();
                    (text, start) = (note.Lyric, note.Start);
                }
                else if (text == null)
                {
                    continue;
                }
                end = note.Start + note.Duration;
            }
            Close();
            sung.Add(new SongSung(part, person, syllables));
        }
        return sung;
    }

    /// <summary>
    /// The times of the song in MIDI ticks, each with the beat it begins at. A beat is the note
    /// of the lower number of the time in force, so the ticks of a beat change with the time.
    /// </summary>
    private readonly List<(int Tick, double Beat, double TicksPerBeat, int Numerator, int Denominator)> _meters = [];

    private double Beat(int tick)
    {
        var meter = _meters.Count == 0 ? (Tick: 0, Beat: 0.0, TicksPerBeat: 480.0, Numerator: 4, Denominator: 4) : _meters.LastOrDefault(m => m.Tick <= tick, _meters[0]);
        return Math.Round(meter.Beat + (tick - meter.Tick) / meter.TicksPerBeat, 4);
    }

    private void Measure(MidiFile midi)
    {
        // Every track says the same times and tempos: one of each tick, and only real changes.
        double TicksPerBeat(int denominator) => midi.TicksPerQuarterNote * 4.0 / denominator;
        _meters.Add((0, 0, TicksPerBeat(4), 4, 4));
        foreach (var change in midi.Tracks.SelectMany(t => t.TimeSignatures).OrderBy(t => t.Tick))
        {
            var last = _meters[^1];
            if ((change.Numerator, change.Denominator) == (last.Numerator, last.Denominator))
                continue;
            var next = (change.Tick, Beat(change.Tick), TicksPerBeat(change.Denominator), change.Numerator, change.Denominator);
            if (change.Tick <= last.Tick)
                _meters[^1] = (last.Tick, last.Beat, next.Item3, next.Numerator, next.Denominator);
            else
                _meters.Add(next);
        }
        (Timeline.BeatsPerBar, Timeline.BeatUnit) = (_meters[0].Numerator, _meters[0].Denominator);
        Timeline.Meters = _meters.Select(m => new SongMeter(m.Beat, m.Numerator, m.Denominator)).ToList();

        // A tempo is counted in the beat of its time: where the time changes its beat, the
        // number changes though the music goes on as fast as before.
        var written = midi.Tracks.SelectMany(t => t.TempoChanges).OrderBy(t => t.Tick).ToList();
        double microsecondsPerQuarter = 500000;
        foreach (int tick in written.Select(t => t.Tick).Concat(_meters.Select(m => m.Tick)).Append(0).Distinct().Order())
        {
            foreach (var change in written.Where(t => t.Tick == tick))
                microsecondsPerQuarter = change.MicrosecondsPerBeat;
            int unit = _meters.Last(m => m.Tick <= tick).Denominator;
            double bpm = Math.Round(60e6 / microsecondsPerQuarter * unit / 4, 3);
            if (Timeline.Tempos.Count == 0 || Timeline.Tempos[^1].Bpm != bpm)
                Timeline.Tempos.Add(new SongTempo(Beat(tick), bpm));
        }
        Timeline.Tempo = Timeline.Tempos[0].Bpm;

        // The piece ends at its last bar line: a fermata or a ringing note may sound past it,
        // and a closing rest stops short of it.
        var starts = midi.SectionStarts.ToList();
        int closing = midi.BarTicks.Count > 0 ? midi.BarTicks.Max : 0;
        int end = starts.Count > 0 && closing > starts[^1].Key ? closing : midi.EndTick;
        for (int i = 0; i < starts.Count; i++)
        {
            int start = starts[i].Key, next = i + 1 < starts.Count ? starts[i + 1].Key : end;
            // An upbeat: the section's first bar line comes before a whole bar has gone by.
            var meter = _meters.Last(m => m.Tick <= start);
            int line = midi.BarTicks.Where(b => b > start && b < next).DefaultIfEmpty(start).First();
            bool upbeat = line - start < meter.Numerator * meter.TicksPerBeat - 0.5;
            Timeline.Plays.Add(new SongPlay(starts[i].Value, Math.Round(Beat(next) - Beat(start), 4), upbeat ? Math.Round(Beat(line) - Beat(start), 4) : 0));
        }
    }

    /// <summary>
    /// The notes of the song as it is played, in the order they start (at once: by pitch), each as
    /// [beat it starts at, beats it lasts, pitch (a MIDI number; .5 is a quarter tone),
    /// velocity (1-127), timbre (the preview's families, 0 piano … 8 voice; 9 is the drum kit,
    /// whose pitch is the General MIDI drum), program (the General MIDI instrument of its part,
    /// 0-127, as the exported MIDI has it; -1 when the part names none), channel (0-15; 9 is the
    /// drums)]. The last two are for a player with a sound font; one without reads the timbre.
    /// Beats are the timeline's, so a player that keeps its beat keeps the music with it.
    /// </summary>
    public List<double[]> Notes()
    {
        if (Midi is null)
            return [];
        return Midi.Tracks.SelectMany(t => t.Notes.Select(n => (Note: n, Program: t.Program ?? -1)))
            .Where(n => n.Note.Velocity > 0 && n.Note.DurationTicks > 0)
            .OrderBy(n => n.Note.StartTick).ThenBy(n => n.Note.Pitch)
            .Select(x =>
            {
                var n = x.Note;
                return new double[]
                {
                    Beat(n.StartTick), Math.Round(Beat(n.StartTick + n.DurationTicks) - Beat(n.StartTick), 4),
                    n.Pitch + n.QuarterBend * 0.5, n.Velocity, n.Channel == 9 ? 9 : n.Timbre, x.Program, n.Channel,
                };
            })
            .ToList();
    }
}
