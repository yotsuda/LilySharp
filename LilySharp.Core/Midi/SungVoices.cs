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
public sealed record SungNote(double Start, double Duration, double Pitch, string? Lyric, bool Melisma, bool Breath, int SourcePos);

/// <summary>A part's words on its notes, as a singing engine takes them: the score in seconds
/// that <c>cantilena</c> reads. <paramref name="Warnings"/> says what the engine will not sing
/// (syllables that are not kana).</summary>
public sealed record SungVoice(string Part, string Language, IReadOnlyList<SungNote> Notes, IReadOnlyList<string> Warnings);

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
/// drums are not sung. A breath is after the last note before a gap, and after the last note.
/// The first words at an onset win: a second track on the same part (another verse printed
/// under the first, a translation) is not a second voice.
/// </para>
/// </remarks>
public static class SungVoices
{
    /// <summary>The tag of the language lyrics are in when nothing names one, the only one
    /// whose syllables are checked here (kana; <see cref="Music.LyricLanguage"/>).</summary>
    public const string Japanese = "ja";

    /// <summary>Every voice of <paramref name="midi"/>, in the order their parts first sing.</summary>
    public static List<SungVoice> Read(MidiFile midi, Func<int, double> secondsAt)
    {
        var voices = new List<SungVoice>();
        var lyrics = midi.Tracks.SelectMany(t => t.Lyrics).Where(l => l.Part != null).ToList();
        foreach (var part in lyrics.Select(l => l.Part!).Distinct(StringComparer.Ordinal))
        {
            // The part's language: its first words' (one voice a part; the first words at an onset win).
            string language = lyrics.First(l => l.Part == part).Language ?? Japanese;
            var words = new Dictionary<int, string>();
            foreach (var l in lyrics)
                if (l.Part == part)
                    words.TryAdd(l.Tick, l.Sung ?? l.Text);     // a reading (漢[かん]) sings かん
            var line = Line(midi, part, words);

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

            var notes = new List<SungNote>(sung.Count);
            var strange = new List<string>();
            for (int i = 0; i < sung.Count; i++)
            {
                var (note, end, lyric, melisma) = sung[i];
                bool breath = i == sung.Count - 1 || sung[i + 1].Note.StartTick > end;
                double start = secondsAt(note.StartTick);
                double duration = Math.Max(0.001, secondsAt(end) - start);
                notes.Add(new SungNote(start, duration, note.Pitch + 0.5 * note.QuarterBend, lyric, melisma, breath, note.SourcePos));
                if (lyric != null && language == Japanese && !IsKana(lyric) && !strange.Contains(lyric, StringComparer.Ordinal))
                    strange.Add(lyric);
            }
            var warnings = new List<string>();
            if (strange.Count > 0)
                warnings.Add($"{strange.Count} syllable(s) are not kana, which the Japanese voice needs: "
                    + string.Join(" ", strange.Take(8)) + (strange.Count > 8 ? " …" : ""));
            voices.Add(new SungVoice(part, language, notes, warnings));
        }
        return voices;
    }

    /// <summary>
    /// The part's line, each note with its end: at each onset the highest note of the voice the
    /// words stand on most (a chord sings its top), grace notes and drums left out, a note cut
    /// where the next begins.
    /// </summary>
    private static List<(MidiNote Note, int End)> Line(MidiFile midi, string part, Dictionary<int, string> words)
    {
        var all = midi.Tracks.SelectMany(t => t.Notes)
            .Where(n => n.Part == part && !n.IsGrace && n.Channel != 9 && n.DurationTicks > 0)
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
