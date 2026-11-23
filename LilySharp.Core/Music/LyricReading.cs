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
using System.Text;

namespace LilySharp.Core.Music;

/// <summary>
/// A READING glued to a lyric word — <c>漢[かん]</c>: the page prints the word (<c>漢</c>) and a
/// singing engine sings the reading (<c>かん</c>). Owner's decision 2026-11-08 (session 1138;
/// docs/singing-design.md §3): one place, one row — the file that prints the kanji is the file
/// that says how it is sung. LILYSHARP-OWN: LilyPond prints lyrics and sings nothing.
/// </summary>
/// <remarks>
/// <para>
/// The shape: <c>WORD[READING]</c>, glued, as many times as a syllable has words to read —
/// <c>大[おお]空[ぞら]</c> prints 大空 and sings おおぞら; a stretch with no reading sings as
/// written (<c>漢[かん]じ</c> sings かんじ). A reading is of the stretch right before it, so one
/// with nothing before it (<c>[かん]</c>, a <c>[</c> written after a space), an empty one, a
/// nested or unclosed one, or a <c>]</c> on its own is a malformed syllable (LYS4036, the
/// parser), and the syllable still prints as written. A reading that is not kana is a warning
/// in Japanese words (LYS4037, Semantics.LyricReadingKanaValidator): the Japanese voices sing kana. In another language a reading is what its voices read (长[chang], 넘[너]).
/// </para>
/// <para>
/// Who reads what: the page, the MusicXML <c>&lt;text&gt;</c>, the LilyPond twin and the
/// <c>.mid</c>'s lyric events carry the PRINTED word (a karaoke display shows the words as
/// written; neither MusicXML nor LilyPond has a reading); the sung voice
/// (<c>Midi.SungVoices</c> through <c>LyricEvent.Sung</c>) and the VSQX export sing the
/// reading. A quoted syllable (<c>"漢[かん]"</c>) is its words and nothing else, as ever.
/// </para>
/// </remarks>
public static class LyricReading
{
    /// <summary>True when <paramref name="raw"/> writes a bracket at all — the cheap test
    /// before <see cref="Split"/>.</summary>
    public static bool HasBrackets(string raw) => raw.IndexOfAny(['[', ']']) >= 0;

    /// <summary>
    /// The printed word and the sung word of a syllable as written, and — when its brackets are
    /// not a reading — why not. With no bracket, both are <paramref name="raw"/> and no problem.
    /// </summary>
    public static (string Printed, string Sung, string? Problem) Split(string raw)
    {
        var (printed, sung, _, problem) = Read(raw);
        return (printed, sung, problem);
    }

    /// <summary>
    /// The PHONEMES written at the end of a syllable — <c>병[/pp.y.eo.NG/]</c> — as a voice takes
    /// them, its symbols separated by spaces (<c>pp y eo NG</c>); null when the syllable writes
    /// none or its brackets are malformed.
    /// </summary>
    /// <remarks>
    /// Cantoria's request 1 (2026-11-22, its docs/repro/REQUESTS.md): a word a voice's dictionary
    /// or rules read wrong — the doubled consonant of 발병, an English stress, a Chinese word no
    /// table has — is sung by the voice's own symbols. LILYSHARP-OWN: LilyPond sings nothing.
    /// <para>
    /// The shape, as the session-1375 design gives it (docs/singing-design.md §3): <c>[/…/]</c>
    /// glued last on the syllable, after its word or its reading (<c>漢[かん][/k.a.N/]</c>: an
    /// engine that reads words sings かん, a DiffSinger voice the symbols), the symbols separated
    /// by <c>.</c> — a space ends a syllable. The symbols are THE VOICE'S: another singer may not
    /// know them, and says so. A melisma's notes carry none (the syllable is on its first note).
    /// The page, the MusicXML, the LilyPond twin, the .mid and the VSQX do not write them.
    /// </para>
    /// </remarks>
    public static string? Phonemes(string raw) => HasBrackets(raw) ? Read(raw).Phonemes : null;

    /// <summary>The opening of a phoneme bracket, <c>[/</c>, and its closing, <c>/]</c>.</summary>
    private const string PhonemesOpen = "[/", PhonemesClose = "/]";

    private static (string Printed, string Sung, string? Phonemes, string? Problem) Read(string raw)
    {
        if (!HasBrackets(raw))
            return (raw, raw, null, null);
        string? phonemes = null;
        int end = raw.Length;
        int open = raw.IndexOf(PhonemesOpen, StringComparison.Ordinal);
        if (open >= 0)
        {
            const string example = "e.g. 병[/pp.y.eo.NG/]";
            int close = raw.IndexOf(PhonemesClose, open + 2, StringComparison.Ordinal);
            string? problem = null;
            if (close < 0)
                problem = $"the phonemes in '{raw}' are not closed — write '/]' after them, separated by '.' (a space ends the syllable), {example}";
            else if (close + 2 != raw.Length)
                problem = $"the phonemes in '{raw}' must end the syllable — write [/…/] last, after the word and its reading, {example}";
            else if (open == 0)
                problem = $"the phonemes [/{raw[2..close]}/] in '{raw}' have no word before them — they are glued after the syllable they sing, {example}";
            else if (raw[(open + 2)..close] is var symbols && (symbols.Length == 0 || symbols.IndexOfAny(['[', ']']) >= 0
                         || symbols.Split('.').Any(s => s.Length == 0)))
                problem = symbols.IndexOfAny(['[', ']']) >= 0
                    ? $"a bracket inside the phonemes in '{raw}' — {example}"
                    : $"an empty phoneme in '{raw}' — write the voice's symbols separated by one '.', {example}";
            if (problem != null)
                return (raw, raw, null, problem);
            phonemes = raw[(open + 2)..close].Replace('.', ' ');
            end = open;
        }
        var (printed, sung, readingProblem) = SplitReadings(raw[..end], raw);
        return readingProblem != null ? (raw, raw, null, readingProblem) : (printed, sung, phonemes, null);
    }

    /// <summary>The readings of <paramref name="word"/> (the syllable less its phonemes);
    /// <paramref name="raw"/> is the syllable as written, for the message.</summary>
    private static (string Printed, string Sung, string? Problem) SplitReadings(string word, string raw)
    {
        if (!HasBrackets(word))
            return (word, word, null);
        var printed = new StringBuilder();
        var sung = new StringBuilder();
        var stretch = new StringBuilder();      // the printed chars since the last reading
        string? problem = null;
        int i = 0;
        while (i < word.Length)
        {
            char c = word[i];
            if (c == ']')
            {
                problem ??= $"a ']' with no '[' before it in '{raw}' — a reading is written WORD[READING], e.g. 漢[かん]";
                break;
            }
            if (c != '[')
            {
                stretch.Append(c);
                i++;
                continue;
            }
            int close = word.IndexOf(']', i + 1);
            int nextOpen = word.IndexOf('[', i + 1);
            if (close < 0)
            {
                problem ??= $"the reading in '{raw}' is not closed — write ']' after it, e.g. 漢[かん]";
                break;
            }
            if (nextOpen >= 0 && nextOpen < close)
            {
                problem ??= $"a '[' inside the reading in '{raw}' — one reading is one pair of brackets, e.g. 漢[かん]";
                break;
            }
            string reading = word[(i + 1)..close];
            if (stretch.Length == 0)
            {
                problem ??= $"the reading [{reading}] in '{raw}' has no word before it — a reading is glued after the word it reads, e.g. 漢[かん]";
                break;
            }
            if (reading.Length == 0)
            {
                problem ??= $"an empty reading in '{raw}' — write the kana between the brackets, e.g. 漢[かん]";
                break;
            }
            printed.Append(stretch);
            sung.Append(reading);
            stretch.Clear();
            i = close + 1;
        }
        if (problem != null)
            return (raw, raw, problem);
        printed.Append(stretch);
        sung.Append(stretch);
        return (printed.ToString(), sung.ToString(), null);
    }

    /// <summary>The readings written in <paramref name="raw"/>, in order — empty when it has
    /// none or its brackets are not a reading.</summary>
    public static List<string> Readings(string raw)
    {
        var list = new List<string>();
        if (!HasBrackets(raw) || Split(raw).Problem != null)
            return list;
        string word = raw.IndexOf(PhonemesOpen, StringComparison.Ordinal) is var p and >= 0 ? raw[..p] : raw;     // the phonemes are no reading
        for (int open = word.IndexOf('['); open >= 0; open = word.IndexOf('[', open + 1))
        {
            int close = word.IndexOf(']', open + 1);
            list.Add(word[(open + 1)..close]);
            open = close;
        }
        return list;
    }

    /// <summary>Hiragana, katakana (the long-vowel mark among them) and half-width katakana only —
    /// what the Japanese voices sing.</summary>
    public static bool IsKana(string text)
    {
        if (text.Length == 0)
            return false;
        foreach (char c in text)
            if (!(c is >= 'ぁ' and <= 'ゟ' or >= '゠' and <= 'ヿ' or >= 'ｦ' and <= 'ﾟ'))
                return false;
        return true;
    }
}
