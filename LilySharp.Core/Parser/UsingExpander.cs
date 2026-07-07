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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Parser;

/// <summary>
/// Resolves <c>using "file.lys"</c> directives into a single combined source,
/// giving a piece spread over many files (partial-class style: each file
/// contributes parts/sections that merge into one grid).
/// </summary>
/// <remarks>
/// The main text is kept as the PREFIX of the combined source so every position
/// in the file the user is editing is preserved exactly (editor &lt;-&gt; preview
/// sync). Each included file's text is appended once, depth-first, deduplicated by
/// full path; using cycles terminate. A path that cannot be read is skipped (its
/// directive simply stays inert), so a missing using never aborts the render.
/// </remarks>
public static class UsingExpander
{
    /// <summary>True if the text contains at least one <c>using</c> directive.</summary>
    public static bool HasUsings(string text)
        => SyntaxTree.Parse(text).GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().Any();

    /// <summary>
    /// Expand includes starting from <paramref name="mainText"/> (the in-memory
    /// content of <paramref name="mainPath"/>). <paramref name="readFile"/> returns
    /// the text of an included file, or null if it cannot be read.
    /// </summary>
    public static string Expand(string mainText, string mainPath, Func<string, string?> readFile)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        Walk(mainPath, mainText, visited, sb, readFile);
        return sb.ToString();
    }

    private static void Walk(string path, string? text, HashSet<string> visited,
        StringBuilder sb, Func<string, string?> readFile)
    {
        if (text == null)
            return;

        string full;
        try { full = Path.GetFullPath(path); }
        catch { full = path; }
        if (!visited.Add(full))
            return; // already included (dedupe / cycle break)

        sb.Append(text);
        if (text.Length > 0 && text[^1] != '\n')
            sb.Append('\n');

        var dir = Path.GetDirectoryName(full) ?? string.Empty;
        foreach (var usingPath in FindUsingPaths(text))
        {
            var resolved = Path.IsPathRooted(usingPath) ? usingPath : Path.Combine(dir, usingPath);
            Walk(resolved, readFile(resolved), visited, sb, readFile);
        }
    }

    private static IEnumerable<string> FindUsingPaths(string text)
        => SyntaxTree.Parse(text).GetRoot().DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(d => d.Path)
            .Where(p => p.Length > 0)
            .ToList();
}
