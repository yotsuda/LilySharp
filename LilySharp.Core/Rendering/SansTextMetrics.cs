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

namespace LilySharp.Core.Rendering;

/// <summary>
/// Per-glyph advance widths for sans-serif text (Helvetica/Arial Bold metrics —
/// identical width tables, and what a generic <c>sans-serif</c> resolves to on
/// most platforms). Chord names render in sans bold, so their layout footprint
/// must be measured with SANS advances; the serif table under-measured wide
/// sans strings ("Gm7♭5") by up to ~10%. Same structure as
/// <see cref="SerifTextMetrics"/>.
/// </summary>
public static class SansTextMetrics
{
    /// <summary>Width of <paramref name="text"/> in staff spaces at the given font size.</summary>
    public static double MeasureBold(string text, double fontSize)
    {
        double totalAdvance = 0;
        foreach (char c in text)
            totalAdvance += AdvanceWidth(c);
        return totalAdvance / 1000.0 * fontSize;
    }

    /// <summary>Helvetica Bold advance widths per 1000 em units (AFM values).</summary>
    private static int AdvanceWidth(char c) => c switch
    {
        // East Asian wide characters: same fallback behaviour as the serif table.
        >= 'ᄀ' and <= 'ᅟ' => 1000, // Hangul Jamo
        >= '⺀' and <= '꓏' => 1000, // CJK radicals … kana … ideographs … Yi
        >= '가' and <= '힣' => 1000, // Hangul syllables
        >= '豈' and <= '﫿' => 1000, // CJK compatibility ideographs
        >= '＀' and <= '｠' => 1000, // full-width forms
        >= '｡' and <= 'ﾟ' => 500,  // half-width katakana
        // Uppercase
        'A' => 722, 'B' => 722, 'C' => 722, 'D' => 722, 'E' => 667,
        'F' => 611, 'G' => 778, 'H' => 722, 'I' => 278, 'J' => 556,
        'K' => 722, 'L' => 611, 'M' => 833, 'N' => 722, 'O' => 778,
        'P' => 667, 'Q' => 778, 'R' => 722, 'S' => 667, 'T' => 611,
        'U' => 722, 'V' => 667, 'W' => 944, 'X' => 667, 'Y' => 667,
        'Z' => 611,
        // Lowercase
        'a' => 556, 'b' => 611, 'c' => 556, 'd' => 611, 'e' => 556,
        'f' => 333, 'g' => 611, 'h' => 611, 'i' => 278, 'j' => 278,
        'k' => 556, 'l' => 278, 'm' => 889, 'n' => 611, 'o' => 611,
        'p' => 611, 'q' => 611, 'r' => 389, 's' => 556, 't' => 333,
        'u' => 611, 'v' => 556, 'w' => 778, 'x' => 556, 'y' => 556,
        'z' => 500,
        // Digits
        '0' => 556, '1' => 556, '2' => 556, '3' => 556, '4' => 556,
        '5' => 556, '6' => 556, '7' => 556, '8' => 556, '9' => 556,
        // Punctuation that appears in chord symbols and labels
        ' ' => 278, '.' => 278, ',' => 278, ':' => 333, ';' => 333,
        '-' => 333, '\'' => 238, '"' => 474, '(' => 333, ')' => 333,
        '!' => 333, '?' => 611, '#' => 556, '+' => 584, '/' => 278,
        // Chord-quality symbols (rendered by a music-aware fallback face;
        // measured against DejaVu Sans Bold, the common sans with coverage).
        '♭' => 390, '♯' => 460, '♮' => 390, '°' => 400, 'ø' => 611,
        'Δ' => 685,
        _ => 556, // fallback: median width
    };
}
