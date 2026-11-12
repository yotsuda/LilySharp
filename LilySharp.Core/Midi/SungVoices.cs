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
/// <paramref name="LevelEnd"/> the factor at its end, where a hairpin crosses it; <paramref name="Inhale"/>
/// an audible breath before the note right after it (<c>@breath</c>; a rest is only silence);
/// <paramref name="Vibrato"/> a written vibrato's swing each way in cents and <paramref name="VibratoRate"/>
/// its swings a second (<c>@vibrato</c>, a trial: a DiffSinger voice sings it); <paramref name="Sing"/>
/// the settings an <c>@sing(…)</c> writes on it, as <c>Semantics.AnnotationValues.SingText</c>.
public sealed record SungNote(double Start, double Duration, double Pitch, string? Lyric, bool Melisma, bool Breath, int SourcePos, double? Level = null, double? LevelEnd = null, bool Inhale = false, bool Hyphen = false, double? Vibrato = null, double? VibratoRate = null, string? Sing = null);

/// <summary>A part's words on its notes, as a singing engine takes them: the score in seconds
/// that <c>cantoria</c> reads. <paramref name="Warnings"/> says what the engine will not sing
/// (syllables that are not kana).</summary>
/// <paramref name="Singer"/> is who the file says sings it, as written, or null for the engine's default.
/// <paramref name="Gender"/> is the gender of the engine's default voice for it (<see cref="Music.LyricSinger.GenderReaderOf"/>).
/// <paramref name="Track"/> is the name of the lyrics track it sings (its first words'), or null.
/// <paramref name="Key"/> tells this voice from the others of its part: the part's name for its first
/// <c>singer</c> line, <c>part#2</c> and so on for the rest (null is the part's name).
/// <paramref name="Sing"/> is how the voice sings, as its <c>singer</c> line's <c>with …</c> writes it: a name its
/// number or its word, by the names the voice's engine takes; null where the line sets nothing.
/// <paramref name="Line"/> is its place among the singer lines on these words of this part, from 0.
public sealed record SungVoice(string Part, string Language, IReadOnlyList<SungNote> Notes, IReadOnlyList<string> Warnings, string? Singer = null, string? Gender = null, string? Track = null, string? Key = null,
    IReadOnlyDictionary<string, (double? Number, string? Word)>? Sing = null, int Line = 0);

/// <summary>
/// The sung voices of an exported piece: one per part a placed lyrics row sings with a voice asked for (<see cref="Music.LyricSinger.SungReaderOf"/>). Which note a
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
/// out before it did, until a rest or until a section that sings no word at all (a Coda with no
/// words after the last chorus is the instrument's, not the last syllable's vowel held on); a
/// note after a rest with no words is not sung. A chord sings
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
        var lyrics = midi.Tracks.SelectMany(t => t.Lyrics).Where(l => l.Part != null && l.Voiced).ToList();
        // One set of voices for each TRACK a part sings (owner's decision 2026-11-12: a verse in
        // Japanese and a chorus in English — the singer lines of each track say their sections,
        // and the validator keeps two tracks of a part out of one section): the words, their
        // language and who sings them are the track's.
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (part, track) in lyrics.Select(l => (l.Part, l.Track)).Distinct())
        {
            // The track's language: its first words' (the first words at an onset win).
            var first = lyrics.First(l => l.Part == part && l.Track == track);
            string language = first.Language ?? Japanese;
            var words = new Dictionary<int, string>();
            var hyphens = new HashSet<int>();
            foreach (var l in lyrics)
                if (l.Part == part && l.Track == track && words.TryAdd(l.Tick, l.Sung ?? l.Text) && l.Hyphen)     // a reading (漢[かん]) sings かん
                    hyphens.Add(l.Tick);
            var line = Line(midi, part!, words);
            MoveWordsOntoGraces(line, words, hyphens);

            // A vowel is not carried into a section that sings no word (owner's report
            // 2026-11-12: a Coda of two bars with no words, right after the last chorus, was
            // sung "u— u— u—" on the chorus's last syllable): the notes of a phrase whose words
            // ran out are a melisma only as far as the section goes — or into one that has words
            // of its own, where a held first note (`_`) still reaches here wordless.
            var sectionStarts = midi.SectionStarts.Keys.ToList();
            var wordTicks = words.Keys.OrderBy(t => t).ToList();
            bool SectionHasWords(int tick)
            {
                if (sectionStarts.Count == 0)
                    return true;
                int at = sectionStarts.BinarySearch(tick);
                if (at < 0)
                    at = ~at - 1;
                if (at < 0)
                    return true;
                int from = sectionStarts[at], to = at + 1 < sectionStarts.Count ? sectionStarts[at + 1] : int.MaxValue;
                int first = wordTicks.BinarySearch(from);
                if (first < 0)
                    first = ~first;
                return first < wordTicks.Count && wordTicks[first] < to;
            }

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
                else if (singing && note.StartTick == prevEnd && SectionHasWords(note.StartTick))
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
                notes.Add(new SungNote(start, duration, note.Pitch + 0.5 * note.QuarterBend, lyric, melisma, breath, note.SourcePos, levels[i].Level, levels[i].End, joined && note.Inhale, lyric != null && hyphens.Contains(note.StartTick), note.Vibrato, note.VibratoRate, note.Sing));
                if (lyric != null && language == Japanese && !IsKana(lyric) && !strange.Contains(lyric, StringComparer.Ordinal))
                    strange.Add(lyric);
            }
            var warnings = new List<string>();
            if (strange.Count > 0)
                warnings.Add($"{strange.Count} syllable(s) are not kana, which the Japanese voice needs: "
                    + string.Join(" ", strange.Take(8)) + (strange.Count > 8 ? " …" : ""));
            // One voice for each `singer` line on these words, each sung by itself (docs/lyrics-singer-design.md §2).
            var singers = (first.Singer ?? "").Split(Music.LyricSinger.Joiner);
            // The voices of the part's tracks before this one keep their keys: this track's go on from them.
            int before = slots.GetValueOrDefault(part!);
            slots[part!] = before + singers.Length;
            // How each sings, beside who: the same order and number (LyricSinger.SettingsReaderOf).
            var settings = (first.Sing ?? "").Split(Music.LyricSinger.SettingsJoiner);
            var ticks = sung.Select(n => n.Note.StartTick).ToList();
            for (int s = 0; s < singers.Length; s++)
            {
                string? singer = singers[s].Length == 0 ? null : singers[s];
                string? gender = singer is "male" or "female" ? singer : first.Gender;
                var written = s < settings.Length ? Music.LyricSinger.SettingsOf(settings[s]) : null;
                // What of the part this line sings (its up / down / octave / in) rides with the
                // settings and is taken out here: the rest is the voice's engine's.
                var sing = written?.Where(p => !p.Key.StartsWith('\u0001')).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                var own = Placed(midi, notes, ticks, written);
                if (own.Count == 0)
                    continue;       // it sings in sections the piece does not play these words in
                int nth = before + s;
                voices.Add(new SungVoice(part!, language, own, warnings, singer, gender, first.Track, nth == 0 ? part : $"{part}#{nth + 1}",
                    sing is { Count: > 0 } ? sing : null, s));
            }
        }
        return voices;
    }

    /// <summary>
    /// Where a sung part is NOT sung (owner's decision 2026-11-12: "where nobody sings a
    /// section, the instrument plays"): for each part that has a voice, the stretches of the
    /// playing — one a section as it is played, in ticks, the end not in it — in which none of
    /// the part's voices has a note. Its instrument sounds there, the part being a voice
    /// everywhere else. A part with no voice at all is not here: its instrument stands in whole,
    /// by the rule that was.
    /// </summary>
    /// <remarks>
    /// BY SECTION, not by note: a bar of a sung section that has notes and no words stays the
    /// voice's silence, as it was (docs/lyrics-singer-design.md §8) — what is new is the section
    /// a singer line's <c>in</c> leaves to nobody.
    /// </remarks>
    public static IReadOnlyDictionary<string, List<(int From, int To)>> Unsung(MidiFile midi, IReadOnlyList<SungVoice> voices, Func<int, double> secondsAt)
    {
        var unsung = new Dictionary<string, List<(int From, int To)>>(StringComparer.Ordinal);
        if (voices.Count == 0 || midi.SectionStarts.Count == 0)
            return unsung;
        var starts = midi.SectionStarts.Keys.ToList();
        foreach (var part in voices.Select(v => v.Part).Distinct(StringComparer.Ordinal))
        {
            var onsets = voices.Where(v => v.Part == part).SelectMany(v => v.Notes).Select(n => n.Start).ToList();
            for (int s = 0; s < starts.Count; s++)
            {
                int from = starts[s], to = s + 1 < starts.Count ? starts[s + 1] : int.MaxValue;
                double a = secondsAt(from) - 1e-6, b = to == int.MaxValue ? double.MaxValue : secondsAt(to) - 1e-6;
                if (onsets.Any(t => t >= a && t < b))
                    continue;
                if (!unsung.TryGetValue(part, out var list))
                    unsung[part] = list = [];
                list.Add((from, to));
            }
        }
        return unsung;
    }

    /// <summary>
    /// WHICH of a part's voices sing each section, for the parts with several (a verse line and a
    /// chorus line, a lead and a harmony): the stretches of <see cref="Unsung"/>'s kind — a
    /// section as it is played, in ticks — that somebody sings, each with the keys
    /// (<see cref="SungVoice.Key"/>, the part's name for a null one) of the voices that have a
    /// note there. Found 2026-11-13: the editor learns who can sing on this machine AFTER the
    /// server has answered the notes (and a line nobody installed can sing is not sung), so the
    /// notes say whose each section is, and the editor leaves to the instrument a section all of
    /// whose singers are missing — the same answer <see cref="Unsung"/> gives for the voices
    /// that did sing, which is what <c>lysc wav --singer</c> asks. A part with one voice is not
    /// here: without it the part has no voice at all, and its instrument stands in whole.
    /// LILYSHARP-OWN.
    /// </summary>
    public static IReadOnlyDictionary<string, List<(int From, int To, string[] Keys)>> SungBy(MidiFile midi, IReadOnlyList<SungVoice> voices, Func<int, double> secondsAt)
    {
        var sung = new Dictionary<string, List<(int From, int To, string[] Keys)>>(StringComparer.Ordinal);
        if (midi.SectionStarts.Count == 0)
            return sung;
        var starts = midi.SectionStarts.Keys.ToList();
        foreach (var part in voices.GroupBy(v => v.Part, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var list = new List<(int From, int To, string[] Keys)>();
            for (int s = 0; s < starts.Count; s++)
            {
                int from = starts[s], to = s + 1 < starts.Count ? starts[s + 1] : int.MaxValue;
                double a = secondsAt(from) - 1e-6, b = to == int.MaxValue ? double.MaxValue : secondsAt(to) - 1e-6;
                var keys = part.Where(v => v.Notes.Any(n => n.Start >= a && n.Start < b)).Select(v => v.Key ?? v.Part).ToArray();
                if (keys.Length > 0)
                    list.Add((from, to, keys));
            }
            sung[part.Key] = list;
        }
        return sung;
    }

    /// <summary>The steps of a major scale above its tonic, in semitones.</summary>
    private static readonly int[] MajorScale = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>
    /// The notes one singer line sings of its part's (owner's decisions 2026-11-12,
    /// docs/lyrics-singer-design.md §12): those in the sections its <c>in</c> names, each moved by
    /// its <c>up N</c> / <c>down N</c> along the key in force there and by its <c>octave N</c>.
    /// With none of the three, the part's notes as they are (the same list).
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: no engine is asked — the notes are moved before any voice sees them, so
    /// every engine sings a harmony alike.
    /// A voice that comes in for a section breathes where it leaves it; a note held over from
    /// the section before, with no syllable of its own, is not its to sing.
    /// </remarks>
    private static IReadOnlyList<SungNote> Placed(MidiFile midi, List<SungNote> notes, List<int> ticks,
        IReadOnlyDictionary<string, (double? Number, string? Word)>? written)
    {
        if (written == null)
            return notes;
        int interval = written.TryGetValue(Music.LyricSinger.PlaceInterval, out var i) ? (int)(i.Number ?? 0) : 0;
        int octaves = written.TryGetValue(Music.LyricSinger.PlaceOctaves, out var o) ? (int)(o.Number ?? 0) : 0;
        var sections = written.TryGetValue(Music.LyricSinger.PlaceSections, out var names) && names.Word is { Length: > 0 } list
            ? list.Split(',').ToHashSet(StringComparer.Ordinal) : null;
        if (interval == 0 && octaves == 0 && sections == null)
            return notes;

        var own = new List<SungNote>(notes.Count);
        bool inside = false;
        for (int n = 0; n < notes.Count; n++)
        {
            var note = notes[n];
            if (sections != null && !sections.Contains(At(midi.SectionStarts, ticks[n]) ?? ""))
            {
                // Leaving its sections: the last note it sang ends its phrase.
                if (inside && own.Count > 0)
                    own[^1] = own[^1] with { Breath = true };
                inside = false;
                continue;
            }
            // Coming in on a note that only holds the syllable before: not this voice's.
            if (!inside && note.Lyric == null)
                continue;
            inside = true;
            double pitch = Step(note.Pitch, interval, At(midi.KeyStarts, ticks[n])) + 12 * octaves;
            own.Add(note with { Pitch = pitch });
        }
        return own;
    }

    /// <summary>The value in force at <paramref name="tick"/>: the last one begun at or before it.</summary>
    private static T? At<T>(SortedDictionary<int, T> starts, int tick)
    {
        T? found = default;
        foreach (var (start, value) in starts)
        {
            if (start > tick)
                break;
            found = value;
        }
        return found;
    }

    /// <summary>
    /// <paramref name="pitch"/> moved by <paramref name="interval"/> along the major scale whose
    /// tonic's pitch class is <paramref name="tonic"/>: 3 is a third above (two steps), −3 a third
    /// below; 0 and ±1 move nothing; a whole number of octaves moves the note as it is. A
    /// quarter-tone keeps its bend.
    /// </summary>
    /// <remarks>
    /// By the key SIGNATURE: a minor key steps along its natural scale.
    /// LILYSHARP-OWN, a reading and not a fact: a note that is NOT of the scale is taken for
    /// the degree it most often alters — a raised first, a lowered third, a raised fourth, a
    /// raised fifth (a minor key's leading note), a lowered seventh — and the voice lands on
    /// the scale's own note the interval away from that degree, the alteration not carried:
    /// over g-sharp in a minor a third above is b (the chord of e), over b-flat in C it is d.
    /// The pitch alone is known here, not how the note was spelled; where the reading is not
    /// the music's, the harmony is the writer's to write out as a part.
    /// </remarks>
    internal static double Step(double pitch, int interval, int tonic)
    {
        int steps = interval > 1 ? interval - 1 : interval < -1 ? interval + 1 : 0;
        if (steps == 0)
            return pitch;
        int whole = (int)Math.Floor(pitch);
        double bend = pitch - whole;
        int above = ((whole - tonic) % 12 + 12) % 12;
        int octave = (whole - tonic - above) / 12;
        if (steps % 7 == 0)
            return pitch + 12 * (steps / 7);
        int degree = Array.IndexOf(MajorScale, above);
        if (degree < 0)
            degree = above switch { 1 => 0, 3 => 2, 6 => 3, 8 => 4, _ => 6 };
        int target = degree + steps;
        int turns = (int)Math.Floor(target / 7.0);
        return tonic + 12 * (octave + turns) + MajorScale[target - 7 * turns] + bend;
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
    private static void MoveWordsOntoGraces(List<(MidiNote Note, int End)> line, Dictionary<int, string> words, HashSet<int> hyphens)
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
            {
                words[line[i].Note.StartTick] = word;
                if (hyphens.Remove(line[main].Note.StartTick))
                    hyphens.Add(line[i].Note.StartTick);
            }
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
