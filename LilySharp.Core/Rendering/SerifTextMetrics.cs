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
/// Per-glyph advance widths for serif text (Times New Roman Bold metrics),
/// used to size rehearsal-mark boxes, ottava labels, etc. without a font
/// engine. Far closer to reality than length×factor estimates, which drift
/// badly for narrow (ilf.) or wide (MWm) strings.
/// </summary>
public static class SerifTextMetrics
{
    /// <summary>Width of <paramref name="text"/> in staff spaces at the given font size.</summary>
    public static double MeasureBold(string text, double fontSize)
    {
        double totalAdvance = 0;
        foreach (char c in text)
            totalAdvance += AdvanceWidth(c);
        return totalAdvance / 1000.0 * fontSize;
    }

    /// <summary>Times New Roman Bold advance widths per 1000 em units.</summary>
    private static int AdvanceWidth(char c) => c switch
    {
        // East Asian wide characters (kana, CJK ideographs, Hangul,
        // full-width forms): rendered by a CJK fallback font at a full em
        // advance; half-width katakana at half an em.
        >= 'ᄀ' and <= 'ᅟ' => 1000, // Hangul Jamo
        >= '⺀' and <= '꓏' => 1000, // CJK radicals … kana … ideographs … Yi
        >= '가' and <= '힣' => 1000, // Hangul syllables
        >= '豈' and <= '﫿' => 1000, // CJK compatibility ideographs
        >= '＀' and <= '｠' => 1000, // full-width forms
        >= '｡' and <= 'ﾟ' => 500,  // half-width katakana
        // Uppercase
        'A' => 722, 'B' => 667, 'C' => 722, 'D' => 722, 'E' => 667,
        'F' => 611, 'G' => 778, 'H' => 778, 'I' => 389, 'J' => 500,
        'K' => 778, 'L' => 667, 'M' => 944, 'N' => 722, 'O' => 778,
        'P' => 611, 'Q' => 778, 'R' => 722, 'S' => 556, 'T' => 667,
        'U' => 722, 'V' => 722, 'W' => 1000, 'X' => 722, 'Y' => 722,
        'Z' => 667,
        // Lowercase
        'a' => 500, 'b' => 556, 'c' => 444, 'd' => 556, 'e' => 444,
        'f' => 333, 'g' => 500, 'h' => 556, 'i' => 278, 'j' => 333,
        'k' => 556, 'l' => 278, 'm' => 833, 'n' => 556, 'o' => 500,
        'p' => 556, 'q' => 556, 'r' => 444, 's' => 389, 't' => 333,
        'u' => 556, 'v' => 500, 'w' => 722, 'x' => 500, 'y' => 500,
        'z' => 444,
        // Digits
        '0' => 500, '1' => 500, '2' => 500, '3' => 500, '4' => 500,
        '5' => 500, '6' => 500, '7' => 500, '8' => 500, '9' => 500,
        // Common punctuation
        ' ' => 250, '.' => 250, ',' => 250, ':' => 333, ';' => 333,
        '-' => 333, '\'' => 278, '"' => 500, '(' => 333, ')' => 333,
        '!' => 333, '?' => 500,
        _ => 500, // fallback: median width
    };
}
