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

namespace LilySharp.Core.Png;

/// <summary>
/// Options for PNG rendering.
/// </summary>
public sealed class PngRenderOptions
{
    /// <summary>
    /// Scale factor for output resolution. 1.0 = 96 DPI, 2.0 = 192 DPI, 3.0 = 288 DPI.
    /// </summary>
    public float Scale { get; init; } = 2.0f;

    /// <summary>
    /// PNG compression quality (0-100). Higher = better quality but larger file.
    /// </summary>
    public int Quality { get; init; } = 100;

    /// <summary>
    /// Optional font directory for Emmentaler font files.
    /// </summary>
    public string? FontDirectory { get; init; }

    /// <summary>Default options (2.0 scale, 192 DPI).</summary>
    public static PngRenderOptions Default => new();

    /// <summary>High-resolution preset (3.0 scale, 288 DPI).</summary>
    public static PngRenderOptions HighDpi => new() { Scale = 3.0f };

    /// <summary>Low-resolution preset (1.0 scale, 96 DPI).</summary>
    public static PngRenderOptions LowDpi => new() { Scale = 1.0f };
}
