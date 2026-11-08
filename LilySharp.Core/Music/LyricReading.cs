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

using System.Collections.Generic;
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
/// (LYS4037): the Japanese voices sing kana, and nothing else reads a reading yet.
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
        if (!HasBrackets(raw))
            return (raw, raw, null);
        var printed = new StringBuilder();
        var sung = new StringBuilder();
        var stretch = new StringBuilder();      // the printed chars since the last reading
        string? problem = null;
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
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
            int close = raw.IndexOf(']', i + 1);
            int nextOpen = raw.IndexOf('[', i + 1);
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
            string reading = raw[(i + 1)..close];
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
        for (int open = raw.IndexOf('['); open >= 0; open = raw.IndexOf('[', open + 1))
        {
            int close = raw.IndexOf(']', open + 1);
            list.Add(raw[(open + 1)..close]);
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
