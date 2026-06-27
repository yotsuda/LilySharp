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

namespace LilySharp.Core.Music;

/// <summary>
/// Maps a key signature to the diatonic spelling of each note letter — e.g. in
/// G major (one sharp) the letter <c>f</c> spells <c>fis</c>. Shared by the
/// renderer's accidental logic and by the editor's key-aware note completions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-engraver.cc
/// Sharp order: F C G D A E B (steps 3,0,4,1,5,2,6).
/// Flat order:  B E A D G C F (steps 6,2,5,1,4,0,3).
/// </remarks>
public static class KeySpelling
{
    private static readonly int[] SharpOrder = { 3, 0, 4, 1, 5, 2, 6 };
    private static readonly int[] FlatOrder = { 6, 2, 5, 1, 4, 0, 3 };

    // Major-key tonic -> sharps(+)/flats(-). The tonic carries its own accidental
    // suffix (fis, bes, …) as the parser spells it.
    private static readonly Dictionary<string, int> MajorSharps = new()
    {
        ["c"] = 0, ["g"] = 1, ["d"] = 2, ["a"] = 3, ["e"] = 4, ["b"] = 5,
        ["fis"] = 6, ["cis"] = 7,
        ["f"] = -1, ["bes"] = -2, ["ees"] = -3, ["aes"] = -4, ["des"] = -5, ["ges"] = -6, ["ces"] = -7
    };

    /// <summary>
    /// Number of sharps (positive) or flats (negative) for the given key, or null
    /// if the tonic is not a recognized key. Only <c>minor</c> shifts the relative
    /// major down three on the circle of fifths; any other mode is read as major.
    /// </summary>
    public static int? SharpsFor(string tonic, string mode)
    {
        if (!MajorSharps.TryGetValue(tonic.ToLowerInvariant(), out int sharps))
            return null;
        if (mode.ToLowerInvariant() == "minor")
            sharps -= 3;
        return sharps;
    }

    /// <summary>
    /// The alteration (-1 flat, 0 natural, +1 sharp) the key signature gives a
    /// diatonic step (c=0, d=1, … b=6).
    /// </summary>
    public static int Alteration(int step, int sharps)
    {
        if (sharps > 0)
        {
            for (int i = 0; i < sharps && i < SharpOrder.Length; i++)
                if (SharpOrder[i] == step) return 1;
        }
        else if (sharps < 0)
        {
            int flatCount = -sharps;
            for (int i = 0; i < flatCount && i < FlatOrder.Length; i++)
                if (FlatOrder[i] == step) return -1;
        }
        return 0;
    }

    /// <summary>The diatonic step index (0–6) of a note letter a–g.</summary>
    public static int StepOf(char letter) => char.ToLowerInvariant(letter) switch
    {
        'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3, 'g' => 4, 'a' => 5, 'b' => 6, _ => -1
    };

    /// <summary>
    /// Spells a note letter a–g as it sounds under the given key signature, using
    /// the parser's accidental suffixes (<c>is</c> sharp, <c>es</c> flat) — e.g.
    /// <c>('f', 1)</c> → <c>"fis"</c>, <c>('b', -1)</c> → <c>"bes"</c>.
    /// </summary>
    public static string SpellLetter(char letter, int sharps)
    {
        char lower = char.ToLowerInvariant(letter);
        int step = StepOf(lower);
        if (step < 0) return letter.ToString();
        return Alteration(step, sharps) switch
        {
            > 0 => lower + "is",
            < 0 => lower + "es",
            _ => lower.ToString()
        };
    }
}
