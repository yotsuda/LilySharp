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

using System.IO;

namespace LilySharp.Core.Rendering;

/// <summary>
/// Locates the bundled Emmentaler font directory. Single source of truth shared by
/// the PNG/PDF/SVG generators, the CLI, and the LSP server (each formerly carried
/// its own near-identical probe with a slightly different candidate list).
/// </summary>
public static class FontLocator
{
    // Superset of every caller's former candidate list, in priority order.
    private static readonly string[] Candidates =
    {
        Path.Combine(AppContext.BaseDirectory, "fonts"),
        Path.Combine(AppContext.BaseDirectory, "Fonts"),
        Path.Combine(AppContext.BaseDirectory, "..", "fonts"),
        "fonts",
        "../fonts",
        "editors/vscode/media/fonts",
    };

    /// <summary>
    /// Returns the absolute path to the directory holding <c>emmentaler-20.otf</c>
    /// or <c>emmentaler-20.woff2</c>, or null if none of the candidates exist.
    /// </summary>
    public static string? Find()
    {
        foreach (var c in Candidates)
        {
            if (Directory.Exists(c) &&
                (File.Exists(Path.Combine(c, "emmentaler-20.otf")) ||
                 File.Exists(Path.Combine(c, "emmentaler-20.woff2"))))
                return Path.GetFullPath(c);
        }
        return null;
    }

    /// <summary>
    /// Resolves a specific bundled font file (e.g. <c>emmentaler-20.otf</c>) to an
    /// absolute path, or null if not found. Probes an optional caller-supplied
    /// override directory first, then the shared candidate directories, then the
    /// app base directory itself. Single source of truth for the PDF/PNG/SVG
    /// drawing contexts (each formerly carried its own {Fonts, fonts, ""} probe).
    /// </summary>
    public static string? ResolveFile(string fileName, string? overrideDir = null)
    {
        if (!string.IsNullOrEmpty(overrideDir))
        {
            var p = Path.Combine(overrideDir, fileName);
            if (File.Exists(p)) return p;
        }
        foreach (var c in Candidates)
        {
            var p = Path.Combine(c, fileName);
            if (File.Exists(p)) return p;
        }
        var baseFile = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(baseFile) ? baseFile : null;
    }
}
