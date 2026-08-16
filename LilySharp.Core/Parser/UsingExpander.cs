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
/// directive simply stays inert), so a missing using never aborts the render — but
/// it no longer does so in silence: the skip is REPORTED as
/// <see cref="DiagnosticCodes.UsingFileUnreadable"/>, whose remarks carry the
/// measurement that made the case for naming it.
/// </remarks>
public static class UsingExpander
{
    /// <summary>True if the text contains at least one <c>using</c> directive.</summary>
    public static bool HasUsings(string text) => HasUsings(SyntaxTree.Parse(text));

    /// <summary>True if an ALREADY-PARSED tree contains at least one <c>using</c> directive.</summary>
    /// <remarks>
    /// <para>
    /// The overload exists so a caller holding the tree does not pay for a second full
    /// parse. That caller is the LSP, and it is ON THE KEYSTROKE PATH: the preview asks
    /// this on every refresh, and the Problems panel asks too.
    /// </para>
    /// <para>
    /// ⚠️ So it must not walk the tree. A <c>using</c> is reachable only from
    /// <c>Parser.ParseTopLevelItem</c> (<c>Parser.cs</c>'s <c>UsingKeyword =>
    /// ParseUsingDirective()</c> arm), so a directive is always a DIRECT CHILD of the
    /// compilation unit and the root's children are the whole search space — the same
    /// argument <see cref="Semantics.PartTranspose.Read(SyntaxNode, string)"/> makes about
    /// part declarations. <c>DescendantNodes().OfType&lt;T&gt;()</c> would materialize a
    /// red wrapper for EVERY descendant just to type-test it, which the tree's own remark
    /// prices at ~13 ms (plain1k) / ~76 ms (fingbeam1k) per enumeration — per keystroke,
    /// for a question whose answer lives in the first twenty nodes.
    /// </para>
    /// </remarks>
    public static bool HasUsings(SyntaxTree tree)
        => tree.GetRoot().ChildNodes().OfType<UsingDirectiveSyntax>().Any();

    /// <summary>
    /// Expand includes starting from <paramref name="mainText"/> (the in-memory
    /// content of <paramref name="mainPath"/>). <paramref name="readFile"/> returns
    /// the text of an included file, or null if it cannot be read.
    /// </summary>
    public static string Expand(string mainText, string mainPath, Func<string, string?> readFile)
        => Expand(mainText, mainPath, readFile, out _);

    /// <summary>
    /// Expand includes and also report the directives that resolved to nothing.
    /// </summary>
    /// <param name="diagnostics">
    /// One warning per <c>using</c> whose file could not be read (or whose path is empty).
    /// A directive whose file was already included is NOT reported — it resolved, and the
    /// dedupe is the point.
    /// </param>
    /// <remarks>
    /// Spans are in COMBINED-source coordinates, which is what every caller parses and
    /// what its diagnostics are already measured against. For the file the reader is
    /// editing that is the same as its own coordinates (it is the prefix); for a directive
    /// inside an INCLUDED file the span points into the appended region, exactly as a
    /// syntax error in that file already does. Attributing those to their own file is a
    /// property of the whole include design, not of this warning.
    /// </remarks>
    public static string Expand(string mainText, string mainPath, Func<string, string?> readFile,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        var bag = new DiagnosticBag();
        Walk(mainPath, mainText, visited, sb, readFile, bag);
        diagnostics = bag.ToList();
        return sb.ToString();
    }

    private static void Walk(string path, string? text, HashSet<string> visited,
        StringBuilder sb, Func<string, string?> readFile, DiagnosticBag bag)
    {
        if (text == null)
            return;

        string full;
        try { full = Path.GetFullPath(path); }
        catch { full = path; }
        if (!visited.Add(full))
            return; // already included (dedupe / cycle break)

        // Where this text lands in the combined source, so a directive's span inside it
        // can be reported against the string the caller will parse.
        int baseOffset = sb.Length;
        sb.Append(text);
        if (text.Length > 0 && text[^1] != '\n')
            sb.Append('\n');

        var dir = Path.GetDirectoryName(full) ?? string.Empty;
        foreach (var (usingPath, span) in FindUsings(text))
        {
            var combined = new TextSpan(baseOffset + span.Start, span.Length);

            // An empty path names nothing at all, and no state of the file system can make
            // it right — it is the same silence with a different cause, so it gets the same
            // name rather than a second one.
            if (usingPath.Length == 0)
            {
                bag.Warning(combined, DiagnosticCodes.UsingFileUnreadable,
                    "a `using` with an empty path includes nothing — name the file to include.");
                continue;
            }

            var resolved = Path.IsPathRooted(usingPath) ? usingPath : Path.Combine(dir, usingPath);

            // Read here rather than inside Walk, so "unreadable" and "already included"
            // stay distinguishable: both make Walk return at once, and only the first is
            // a mistake.
            var included = readFile(resolved);
            if (included == null)
            {
                bag.Warning(combined, DiagnosticCodes.UsingFileUnreadable,
                    $"cannot read \"{usingPath}\" — this `using` is ignored, so every part, "
                    + "section and setting it declares is absent.");
                continue;
            }

            Walk(resolved, included, visited, sb, readFile, bag);
        }
    }

    // Direct children only, for the same reason HasUsings gives: the parser can only
    // place a `using` at the top level, so this is the whole search space and the two
    // readers of that fact stay spelt the same way.
    private static IEnumerable<(string Path, TextSpan Span)> FindUsings(string text)
        => SyntaxTree.Parse(text).GetRoot().ChildNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(d => (d.Path, d.PathToken.Span))
            .ToList();
}
