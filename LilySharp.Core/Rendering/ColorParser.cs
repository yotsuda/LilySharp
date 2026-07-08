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

namespace LilySharp.Core.Rendering;

/// <summary>
/// Parses a grob color override string into a <see cref="Color"/>, or null when it
/// is empty, unrecognized, or the no-op "black" (backends use their own default
/// black). Accepts <c>#rgb</c> / <c>#rrggbb</c> hex and a subset of CSS/X11 names.
/// Extracted from SharedRenderer so color handling is one small, testable unit.
/// </summary>
/// <remarks>LILYPOND-REF: scm/output-lib.scm — x11-color mapping.</remarks>
internal static class ColorParser
{
    public static Color? Parse(string s)
    {
        // Hex literal: #rgb / #rrggbb
        if (s.Length >= 4 && s[0] == '#')
        {
            ReadOnlySpan<char> hex = s.AsSpan(1);
            if (hex.Length == 3 &&
                TryParseHexNibble(hex[0], out int r3) &&
                TryParseHexNibble(hex[1], out int g3) &&
                TryParseHexNibble(hex[2], out int b3))
            {
                return new Color((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
            }
            if (hex.Length == 6 &&
                TryParseHexByte(hex[0], hex[1], out int r6) &&
                TryParseHexByte(hex[2], hex[3], out int g6) &&
                TryParseHexByte(hex[4], hex[5], out int b6))
            {
                return new Color((byte)r6, (byte)g6, (byte)b6);
            }
            return null;
        }
        // Named color (subset of CSS / X11)
        return s.ToLowerInvariant() switch
        {
            "black" => null,           // default — let backends use their own black
            "red" => new Color(255, 0, 0),
            "green" => new Color(0, 128, 0),
            "blue" => new Color(0, 0, 255),
            "yellow" => new Color(255, 255, 0),
            "cyan" => new Color(0, 255, 255),
            "magenta" => new Color(255, 0, 255),
            "white" => new Color(255, 255, 255),
            "gray" or "grey" => new Color(128, 128, 128),
            "orange" => new Color(255, 165, 0),
            "purple" => new Color(128, 0, 128),
            "brown" => new Color(165, 42, 42),
            _ => null,
        };
    }

    private static bool TryParseHexNibble(char c, out int v)
    {
        if (c >= '0' && c <= '9') { v = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { v = 10 + c - 'a'; return true; }
        if (c >= 'A' && c <= 'F') { v = 10 + c - 'A'; return true; }
        v = 0; return false;
    }

    private static bool TryParseHexByte(char hi, char lo, out int v)
    {
        v = 0;
        if (!TryParseHexNibble(hi, out int h)) return false;
        if (!TryParseHexNibble(lo, out int l)) return false;
        v = (h << 4) | l;
        return true;
    }
}
