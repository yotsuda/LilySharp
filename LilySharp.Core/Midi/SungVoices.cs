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
using System.Linq;

namespace LilySharp.Core.Midi;

/// <summary>One note a voice sings, in seconds (docs/singing-design.md §3): its syllable, or
/// a melisma carrying the previous syllable's vowel on; a breath after it where the phrase ends.
/// <paramref name="SourcePos"/> is the note's, as the playback list carries it.</summary>
/// <paramref name="Level"/> is the note's loudness as a factor on the sung audio, null for 1;
/// <paramref name="LevelEnd"/> the factor at its end, where a hairpin crosses it.
public sealed record SungNote(double Start, double Duration, double Pitch, string? Lyric, bool Melisma, bool Breath, int SourcePos, double? Level = null, double? LevelEnd = null);

/// <summary>A part's words on its notes, as a singing engine takes them: the score in seconds
/// that <c>cantilena</c> reads. <paramref name="Warnings"/> says what the engine will not sing
/// (syllables that are not kana).</summary>
/// <paramref name="Singer"/> is who the file says sings it, as written, or null for the engine's default.
public sealed record SungVoice(string Part, string Language, IReadOnlyList<SungNote> Notes, IReadOnlyList<string> Warnings, string? Singer = null);

/// <summary>
/// The sung voices of an exported piece: one per part a placed lyrics row sings. Which note a
/// syllable stands on is the page's answer (<c>MidiExporter.SingLyrics</c>, from
/// <c>Svg.Collector.SungSyllables</c>), and the seconds are the playback's (<see cref="MidiClock"/>)
/// — nothing is counted again here (the 2026-11-04 lesson: a file that counted syllables itself
/// drifted from the page). LILYSHARP-OWN: LilyPond writes no sound.
/// </summary>
/// <remarks>
/// <para>
/// The rules, as docs/singing-design.md §3 states them: a note whose onset carries words takes
/// them — the words as SUNG: a syllable with a reading (<c>漢[かん]</c>, <c>LyricEvent.Sung</c>)
/// sings its kana while the file prints its kanji; an empty text at an onset is a held note (<c>_</c>, a detached <c>~</c>) and a melisma;
/// a note with no words that begins exactly where the sung note before it ends is a melisma too
/// (a slur's, a tie the export did not merge) — ⚠️ so are the notes of a phrase whose words ran
/// out before it did, until a rest; a note after a rest with no words is not sung. A chord sings
/// its top note; a part in two voices sings the voice its words stand on most; grace notes and
/// drums are not sung. A breath is after the last note before a gap, after the last note, and after a note marked <c>@breath</c>.
/// The first words at an onset win: a second track on the same part (another verse printed
/// under the first, a translation) is not a second voice.
/// </para>
/// </remarks>
public static class SungVoices
{
    /// <summary>The tag of the language lyrics are in when nothing names one, the only one
    /// whose syllables are checked here (kana; <see cref="Music.LyricLanguage"/>).</summary>
    public const string Japanese = "ja";

    /// <summary>The time an <c>@breath</c> takes from the note it stands on, at most a third of the note.</summary>
    private const double BreathSeconds = 0.25;

    /// <summary>Every voice of <paramref name="midi"/>, in the order their parts first sing.</summary>
    public static List<SungVoice> Read(MidiFile midi, Func<int, double> secondsAt)
    {
        var voices = new List<SungVoice>();
        var lyrics = midi.Tracks.SelectMany(t => t.Lyrics).Where(l => l.Part != null).ToList();
        foreach (var part in lyrics.Select(l => l.Part!).Distinct(StringComparer.Ordinal))
        {
            // The part's language: its first words' (one voice a part; the first words at an onset win).
            var first = lyrics.First(l => l.Part == part);
            string language = first.Language ?? Japanese;
            var words = new Dictionary<int, string>();
            foreach (var l in lyrics)
                if (l.Part == part)
                    words.TryAdd(l.Tick, l.Sung ?? l.Text);     // a reading (漢[かん]) sings かん
            var line = Line(midi, part, words);
            MoveWordsOntoGraces(line, words);

            var sung = new List<(MidiNote Note, int End, string? Lyric, bool Melisma)>();
            bool singing = false;
            int prevEnd = -1;
            foreach (var (note, end) in line)
            {
                if (words.TryGetValue(note.StartTick, out var text))
                {
                    // ⚠️ An empty text has no observer: the page files no syllable for `_` or a
                    // detached `~` (SungSyllables), so a held note reaches here wordless and is a
                    // melisma by adjacency below (session 1125 poisoned this arm: green). It is
                    // kept for a file's own lyric events, where a blank is a vowel held on.
                    bool held = text.Length == 0;
                    sung.Add((note, end, held ? null : text, held));
                    singing = true;
                }
                else if (singing && note.StartTick == prevEnd)
                {
                    sung.Add((note, end, null, true));
                }
                else
                {
                    singing = false;
                }
                prevEnd = end;
            }
            if (sung.Count == 0)
                continue;

            var levels = Levels(sung.Select(s => s.Note).ToList(), sung.Select(s => s.End).ToList(), secondsAt);
            var notes = new List<SungNote>(sung.Count);
            var strange = new List<string>();
            for (int i = 0; i < sung.Count; i++)
            {
                var (note, end, lyric, melisma) = sung[i];
                bool joined = i < sung.Count - 1 && sung[i + 1].Note.StartTick <= end;
                bool breath = !joined || note.BreathAfter;
                double start = secondsAt(note.StartTick);
                double duration = Math.Max(0.001, secondsAt(end) - start);
                // `@breath` between two sung notes: the room to breathe comes out of this note, so
                // the next one still starts on its beat.
                if (joined && note.BreathAfter)
                    duration -= Math.Min(BreathSeconds, duration / 3);
                notes.Add(new SungNote(start, duration, note.Pitch + 0.5 * note.QuarterBend, lyric, melisma, breath, note.SourcePos, levels[i].Level, levels[i].End));
                if (lyric != null && language == Japanese && !IsKana(lyric) && !strange.Contains(lyric, StringComparer.Ordinal))
                    strange.Add(lyric);
            }
            var warnings = new List<string>();
            if (strange.Count > 0)
                warnings.Add($"{strange.Count} syllable(s) are not kana, which the Japanese voice needs: "
                    + string.Join(" ", strange.Take(8)) + (strange.Count > 8 ? " …" : ""));
            voices.Add(new SungVoice(part, language, notes, warnings, first.Singer));
        }
        return voices;
    }

    /// <summary>How far a hairpin that names no end goes, in MIDI velocity: about two dynamics.</summary>
    private const int HairpinReach = 16;

    /// <summary>The loudness of each note, as a factor on the sung audio (owner's request 2026-11-09,
    /// session 1183; the engines do not sing louder for a marking). The velocity the instruments
    /// play at, on the square law their volume follows, 1 being the level of a part with no marking:
    /// a voice keeps its balance with them. A hairpin goes from its first note to the first level
    /// written after it, or two dynamics on when none follows; null where there is nothing to do.</summary>
    private static (double? Level, double? End)[] Levels(IReadOnlyList<MidiNote> notes, IReadOnlyList<int> ends, Func<int, double> secondsAt)
    {
        double Factor(double velocity) => Math.Min(2, Math.Pow(Math.Clamp(velocity, 1, 127) / MidiExporter.DefaultVelocity, 2));
        double? Written(double factor) => Math.Abs(factor - 1) < 1e-6 ? null : Math.Round(factor, 4);

        var at = notes.Select(n => (double)n.Velocity).ToArray();
        var after = new double?[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].Swell == 0)
                continue;
            int stop = i + 1;
            while (stop < notes.Count && notes[stop].Velocity == notes[i].Velocity && notes[stop].Swell == 0 && !notes[stop].SwellEnds)
                stop++;
            bool landsOnALevel = stop < notes.Count && Math.Sign(notes[stop].Velocity - notes[i].Velocity) == notes[i].Swell;
            double target = landsOnALevel ? notes[stop].Velocity : notes[i].Velocity + (notes[i].Swell * HairpinReach);
            double from = secondsAt(notes[i].StartTick);
            double until = stop < notes.Count ? secondsAt(notes[stop].StartTick) : secondsAt(ends[^1]);
            double Along(double t) => notes[i].Velocity + ((target - notes[i].Velocity) * Math.Clamp((t - from) / Math.Max(1e-6, until - from), 0, 1));
            for (int k = i; k < stop && k < notes.Count; k++)
            {
                at[k] = Along(secondsAt(notes[k].StartTick));
                after[k] = Along(secondsAt(ends[k]));
            }
            // With no level to land on, what the hairpin reached stands until one is written.
            if (!landsOnALevel)
                for (int k = stop; k < notes.Count && notes[k].Velocity == notes[i].Velocity && notes[k].Swell == 0; k++)
                    at[k] = target;
            i = stop - 1;
        }
        var levels = new (double? Level, double? End)[notes.Count];
        bool any = at.Any(v => Written(Factor(v)) != null) || after.Any(v => v != null);
        if (!any)
            return levels;
        for (int i = 0; i < notes.Count; i++)
            levels[i] = (Math.Round(Factor(at[i]), 4), after[i] is { } end ? Math.Round(Factor(end), 4) : null);
        return levels;
    }

    /// <summary>A leading grace is sung on the syllable of the note it leads to: the word moves to the
    /// run's first note, and the rest of the run and the note itself carry its vowel on.</summary>
    private static void MoveWordsOntoGraces(List<(MidiNote Note, int End)> line, Dictionary<int, string> words)
    {
        for (int i = 0; i < line.Count; i++)
        {
            bool leading = line[i].Note is { IsGrace: true, IsAfterGrace: false };
            if (!leading || (i > 0 && line[i - 1].Note.IsGrace))
                continue;
            int main = i;
            while (main < line.Count && line[main].Note.IsGrace)
                main++;
            if (main < line.Count && line[main - 1].End == line[main].Note.StartTick && words.Remove(line[main].Note.StartTick, out var word))
                words[line[i].Note.StartTick] = word;
        }
    }

    /// <summary>
    /// The part's line, each note with its end: at each onset the highest note of the voice the
    /// words stand on most (a chord sings its top), drums left out, a note cut where the next
    /// begins. Grace notes are of the line: they are sung.
    /// </summary>
    private static List<(MidiNote Note, int End)> Line(MidiFile midi, string part, Dictionary<int, string> words)
    {
        var all = midi.Tracks.SelectMany(t => t.Notes)
            .Where(n => n.Part == part && n.Channel != 9 && n.DurationTicks > 0)
            .ToList();
        int voice = all.Where(n => words.ContainsKey(n.StartTick))
            .GroupBy(n => n.Voice)
            .OrderByDescending(g => g.Select(n => n.StartTick).Distinct().Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefault();
        var tops = all.Where(n => n.Voice == voice)
            .GroupBy(n => n.StartTick)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderByDescending(n => n.Pitch).ThenByDescending(n => n.DurationTicks).First())
            .ToList();
        var line = new List<(MidiNote, int)>(tops.Count);
        for (int i = 0; i < tops.Count; i++)
        {
            int end = tops[i].StartTick + tops[i].DurationTicks;
            if (i + 1 < tops.Count)
                end = Math.Min(end, tops[i + 1].StartTick);
            line.Add((tops[i], end));
        }
        return line;
    }

    /// <summary>Hiragana, katakana (the long-vowel mark among them) and half-width katakana only
    /// (<see cref="Music.LyricReading.IsKana"/>, the one definition — the parser's reading check
    /// uses it too).</summary>
    public static bool IsKana(string text) => Music.LyricReading.IsKana(text);
}
