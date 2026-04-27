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
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color White = new(255, 255, 255);

    /// <summary>Parses "#RRGGBB", "#RRGGBBAA", or a CSS named color (limited subset).</summary>
    public static Color Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException("Color spec is empty", nameof(spec));

        if (spec.StartsWith('#'))
        {
            var hex = spec.AsSpan(1);
            if (hex.Length == 6 || hex.Length == 8)
            {
                byte r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte a = hex.Length == 8
                    ? byte.Parse(hex.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : (byte)255;
                return new Color(r, g, b, a);
            }
        }

        return spec.ToLowerInvariant() switch
        {
            "black" => Black,
            "white" => White,
            "red" => new Color(255, 0, 0),
            "green" => new Color(0, 128, 0),
            "blue" => new Color(0, 0, 255),
            _ => throw new FormatException($"Unrecognised color: {spec}")
        };
    }

    /// <summary>Renders as "#RRGGBB" (ignoring alpha) or "#RRGGBBAA" if A &lt; 255.</summary>
    public string ToHex() => A == 255
        ? $"#{R:X2}{G:X2}{B:X2}"
        : $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
