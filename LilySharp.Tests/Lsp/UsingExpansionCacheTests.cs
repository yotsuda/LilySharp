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
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The using-expansion snapshot (2026-08-26 review, appendix F finding 4): a book
/// WITH includes used to pay the full expansion — a re-parse of the main text, every
/// include, and the combined result — on every arrival, and two arrive per keystroke
/// (the preview, then the debounced Problems panel). ExpandUsings now records every
/// readFile answer and replays it on the next call: same answers ⇒ the parsed tree is
/// reused; ANY answer changed (a sibling saved, a file deleted, a nested include
/// touched) ⇒ recompute, because the deliberate spec is invalidation by CONTENT.
/// The poison "reuse without replaying the reads" turns the change-detection nets red.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UsingExpansionCacheTests
{
    private const string Main =
        """
        using "parts.lys"

        time 4/4

        form main { ~A }

        score main { staff melody }
        """;

    private const string Parts =
        """
        part melody { clef treble }

        section A {
          melody { c4 d e f | }
        }
        """;

    /// <summary>Calls ExpandUsings the way the server does, counting the reads.</summary>
    private static (SyntaxTree Tree, IReadOnlyList<Diagnostic> Diagnostics) Expand(
        string main, Dictionary<string, string> files, List<string>? readLog = null)
        => LilySharpLanguageServer.ExpandUsings(main, SyntaxTree.Parse(main),
            "C:/proj/main.lys",
            p =>
            {
                readLog?.Add(Path.GetFileName(p));
                return files.TryGetValue(Path.GetFileName(p), out var t) ? t : null;
            });

    // Each test that wants a cache MISS on its first call must use a text instance no
    // other test shares — the cache is keyed on the string instance, and consts here
    // are interned per assembly. A runtime-built copy is a distinct instance.
    private static string FreshCopyOf(string text) => string.Concat(text, "");

    [Fact]
    public void UnchangedIncludes_ReuseTheParsedTree()
    {
        var main = FreshCopyOf(Main);
        var files = new Dictionary<string, string> { ["parts.lys"] = Parts };
        var log = new List<string>();

        var first = Expand(main, files, log);
        int computeReads = log.Count;
        var second = Expand(main, files, log);

        // The SAME tree object: nothing was re-parsed. The second call still reads the
        // include (that is the price of content-based invalidation, and the point).
        Assert.Same(first.Tree, second.Tree);
        Assert.Same(first.Diagnostics, second.Diagnostics);
        Assert.True(log.Count > computeReads, "the replay must re-read the includes");
    }

    [Fact]
    public void AnEditedInclude_Recomputes_AndTheNewContentIsInTheTree()
    {
        var main = FreshCopyOf(Main);
        var files = new Dictionary<string, string> { ["parts.lys"] = Parts };

        var first = Expand(main, files);
        files["parts.lys"] = Parts.Replace("section A", "section Different");
        var second = Expand(main, files);

        Assert.NotSame(first.Tree, second.Tree);
        Assert.Contains("section Different", second.Tree.Text, StringComparison.Ordinal);
        // And back: content-equal again reuses the recomputed snapshot.
        var third = Expand(main, files);
        Assert.Same(second.Tree, third.Tree);
    }

    [Fact]
    public void ANestedIncludeEdit_AlsoRecomputes()
    {
        // main -> parts -> deep: the recorder captures every read the expander makes,
        // nested ones included, so touching only the DEEP file must invalidate.
        var main = FreshCopyOf(Main);
        var partsWithNested = "using \"deep.lys\"\n\n" + Parts;
        var files = new Dictionary<string, string>
        {
            ["parts.lys"] = partsWithNested,
            ["deep.lys"] = "part bass { clef bass }\n",
        };

        var first = Expand(main, files);
        Assert.Contains("part bass", first.Tree.Text, StringComparison.Ordinal);

        files["deep.lys"] = "part cello { clef bass }\n";
        var second = Expand(main, files);

        Assert.NotSame(first.Tree, second.Tree);
        Assert.Contains("part cello", second.Tree.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingIncludeAppearing_Recomputes_AndTheWarningClears()
    {
        // The recorded answer for an unreadable file is null; the file appearing on
        // disk is a changed answer like any other.
        var main = FreshCopyOf(Main);
        var files = new Dictionary<string, string>();

        var first = Expand(main, files);
        Assert.Contains(first.Diagnostics, d => d.Code == DiagnosticCodes.UsingFileUnreadable);
        // A cache hit while it is still missing keeps reporting (same snapshot).
        var stillMissing = Expand(main, files);
        Assert.Same(first.Diagnostics, stillMissing.Diagnostics);

        files["parts.lys"] = Parts;
        var resolved = Expand(main, files);
        Assert.DoesNotContain(resolved.Diagnostics, d => d.Code == DiagnosticCodes.UsingFileUnreadable);
        Assert.Contains("section A", resolved.Tree.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ADifferentBasePath_DoesNotReuseTheOtherPathsSnapshot()
    {
        // Same text instance, different document location: the includes resolve to
        // different absolute paths, so the snapshot must not cross over.
        var main = FreshCopyOf(Main);
        var asked = new List<string>();
        string? ReadFrom(string p) { asked.Add(p); return Parts; }

        LilySharpLanguageServer.ExpandUsings(main, SyntaxTree.Parse(main), "C:/proj/a/main.lys", ReadFrom);
        int afterFirst = asked.Count;
        LilySharpLanguageServer.ExpandUsings(main, SyntaxTree.Parse(main), "C:/proj/b/main.lys", ReadFrom);

        Assert.Contains(asked.Take(afterFirst), p => p.Replace('\\', '/').Contains("/a/"));
        Assert.Contains(asked.Skip(afterFirst), p => p.Replace('\\', '/').Contains("/b/"));
    }

    [Fact]
    public void WithNoIncludes_TheIdentityPathStaysUncached()
    {
        // No usings: the tree handed in comes straight back (no snapshot machinery).
        const string plain = "time 4/4\n\nscore main { staff nope }\n";
        var tree = SyntaxTree.Parse(plain);
        var (expanded, diagnostics) = LilySharpLanguageServer.ExpandUsings(
            plain, tree, "C:/proj/main.lys", _ => null);

        Assert.Same(tree, expanded);
        Assert.Empty(diagnostics);
    }
}
