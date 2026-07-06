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
/// Font weight and slant flags for drawn text.
/// </summary>
[Flags]
public enum FontStyle
{
    /// <summary>No style flags — upright, normal weight.</summary>
    Regular = 0,
    /// <summary>Bold weight.</summary>
    Bold = 1,
    /// <summary>Italic slant.</summary>
    Italic = 2,
    /// <summary>Bold weight combined with italic slant.</summary>
    BoldItalic = Bold | Italic,
}

/// <summary>
/// Horizontal alignment of drawn text relative to its anchor point.
/// </summary>
public enum TextAnchor
{
    /// <summary>Anchor text at its start (left-aligned in left-to-right text).</summary>
    Start,
    /// <summary>Anchor text at its horizontal center.</summary>
    Middle,
    /// <summary>Anchor text at its end (right-aligned in left-to-right text).</summary>
    End,
}

/// <summary>
/// Vertical anchor for <see cref="IDrawingContext.DrawText"/>. Selects which
/// part of the glyph extents the supplied <c>y</c> coordinate refers to.
/// </summary>
/// <remarks>
/// Maps to SVG <c>dominant-baseline</c>: <see cref="Baseline"/>=alphabetic
/// (default), <see cref="Middle"/>=central, <see cref="Hanging"/>=hanging.
/// </remarks>
public enum VerticalAnchor
{
    /// <summary>SVG default — y is the alphabetic baseline.</summary>
    Baseline,
    /// <summary>y is the visual midline (≈ cap-height / 2 above baseline).</summary>
    Middle,
    /// <summary>y is the top of the glyph extents.</summary>
    Hanging,
}
