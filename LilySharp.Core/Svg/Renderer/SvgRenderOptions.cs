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

namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Options for SVG rendering.
/// </summary>
public sealed class SvgRenderOptions
{
    /// <summary>
    /// If true, embed font data as Base64 in the SVG.
    /// </summary>
    public bool EmbedFont { get; init; }

    /// <summary>
    /// If true, omit @font-face from SVG (font defined externally in HTML).
    /// </summary>
    public bool OmitFontFace { get; init; }

    /// <summary>
    /// Directory containing font files (used when EmbedFont is true).
    /// </summary>
    public string? FontDirectory { get; init; }

    /// <summary>
    /// Default options (reference font by name, requires font installed on system).
    /// </summary>
    public static SvgRenderOptions Default => new();

    /// <summary>
    /// Export mode: embed font as Base64 for standalone SVG.
    /// </summary>
    public static SvgRenderOptions Export(string? fontDirectory = null) => new()
    {
        EmbedFont = true,
        FontDirectory = fontDirectory
    };

    /// <summary>
    /// Preview mode: omit @font-face (font defined externally in HTML).
    /// </summary>
    public static SvgRenderOptions Preview() => new()
    {
        OmitFontFace = true
    };
}
