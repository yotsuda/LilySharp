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

using System.Globalization;

namespace LilySharp.Core.Rendering;

/// <summary>
/// sRGB color with optional alpha. Used by <see cref="IDrawingContext"/>
/// to specify fill and stroke colors. <c>null</c> in any context method
/// means "use the renderer default" (typically opaque black).
/// </summary>
public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Opaque black.</summary>
    public static readonly Color Black = new(0, 0, 0);

    /// <summary>Opaque white.</summary>
    public static readonly Color White = new(255, 255, 255);

    /// <summary>
    /// Parses "#rgb", "#RRGGBB", "#RRGGBBAA", or a CSS/X11 named color, THROWING on
    /// an empty or unrecognized spec (unlike <see cref="ColorParser.Parse"/>, which
    /// returns null). The hex formats and name table are shared with that parser via
    /// <see cref="ColorParser.TryParse"/>; the only difference is this contract plus
    /// "black" → opaque <see cref="Black"/> (the shared core returns null for "black",
    /// which the renderer wants so backends supply their own default black).
    /// </summary>
    public static Color Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException("Color spec is empty", nameof(spec));

        if (string.Equals(spec, "black", StringComparison.OrdinalIgnoreCase))
            return Black;

        return ColorParser.TryParse(spec)
            ?? throw new FormatException($"Unrecognised color: {spec}");
    }

    /// <summary>Renders as "#RRGGBB" (ignoring alpha) or "#RRGGBBAA" if A &lt; 255.</summary>
    public string ToHex() => A == 255
        ? $"#{R:X2}{G:X2}{B:X2}"
        : $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
