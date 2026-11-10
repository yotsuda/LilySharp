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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Music;

/// <summary>
/// The words a file does not write (docs/lyrics-singer-design.md §6): the reserved tracks
/// <c>hum</c> and <c>solfege</c>, made from the notes they follow.
/// </summary>
public static class GeneratedLyrics
{
    /// <summary>The track that hums. It sings and prints nothing of its own.</summary>
    public const string Hum = "hum";

    /// <summary>The track that sings, and can print, the note names.</summary>
    public const string Solfege = "solfege";

    /// <summary>What <see cref="Hum"/> sings on each note, and the language that word is in.</summary>
    public const string HumSyllable = "ん", HumLanguage = "ja";

    /// <summary>Whether <paramref name="track"/> is one of the generated tracks.</summary>
    public static bool IsGenerated(string? track) => track is Hum or Solfege;

    private static readonly string[] Japanese = ["ド", "レ", "ミ", "ファ", "ソ", "ラ", "シ"];
    private static readonly string[] English = ["do", "re", "mi", "fa", "sol", "la", "ti"];
    private static readonly string[] Chinese = ["哆", "来", "咪", "发", "唆", "拉", "西"];
    private static readonly string[] Korean = ["도", "레", "미", "파", "솔", "라", "시"];

    /// <summary>
    /// The movable-do name of a note: <paramref name="step"/> (0 = C … 6 = B) read against the key
    /// signature of <paramref name="sharps"/>, whose major tonic is do, so a minor key's tonic is la.
    /// An altered note takes the name of its letter.
    /// </summary>
    public static string SolfegeName(int step, int sharps, string language)
    {
        var names = language switch { "en" => English, "zh" => Chinese, "ko" => Korean, _ => Japanese };
        int tonic = Modulo(4 * sharps, 7);      // each sharp moves the tonic a fifth, four steps up
        return names[Modulo(step - tonic, 7)];
    }

    /// <summary>The word <paramref name="track"/> puts on a note or chord (a chord by its top
    /// note), or null where it has none: a note whose written pitch is not recorded.</summary>
    internal static string? TextOf(string track, MusicItem item, int sharps, string language)
    {
        if (track == Hum)
            return HumSyllable;
        int packed = item switch
        {
            NoteItem note => note.WrittenPitch,
            ChordItem { Notes.IsDefaultOrEmpty: false } chord => chord.Notes.MaxBy(n => n.Midi).WrittenPitch,
            _ => 0,
        };
        return packed == 0 ? null : SolfegeName(WrittenPitch.Step(packed), sharps, language);
    }

    private static int Modulo(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
