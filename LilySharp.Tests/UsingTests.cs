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

using System.Collections.Generic;
using System.IO;
using LilySharp.Core.Parser;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// `using "file.lys"` expansion: a piece spread over many files merges into one
/// grid (partial-class style), with the main file kept as the prefix.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UsingTests
{
    // Resolve includes against an in-memory file set, keyed by file name.
    private static string Expand(string main, Dictionary<string, string> files)
        => UsingExpander.Expand(main, "C:/proj/main.lys",
            p => files.TryGetValue(Path.GetFileName(p), out var t) ? t : null);

    // Same, but keeping the include-resolution diagnostics.
    private static (string Text, IReadOnlyList<Diagnostic> Diagnostics) ExpandWithDiagnostics(
        string main, Dictionary<string, string> files)
    {
        var text = UsingExpander.Expand(main, "C:/proj/main.lys",
            p => files.TryGetValue(Path.GetFileName(p), out var t) ? t : null,
            out var diagnostics);
        return (text, diagnostics);
    }

    [Fact]
    public void Parses_UsingDirective()
    {
        var tree = SyntaxTree.Parse("using \"parts.lys\"\nscore { staff rh }\n");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    // ---- HasUsings looks at the root's children only, and may ----------------------
    //
    // It is asked on every keystroke — by the preview and, since 49aadc2c, by the
    // Problems panel — so it must not walk the tree. `DescendantNodes().OfType<T>()`
    // materializes a red wrapper for EVERY descendant just to type-test it: measured
    // 43,043 of them for `perf-plain1k` and 234,030 for `perf-fingbeam1k`, against 7 and
    // 5 root children. (234k is the very number RULES §1 session 153 records having
    // removed from the keystroke path.) These tests hold the claim that makes the cheap
    // spelling legal.

    [Theory]
    [InlineData("using \"a.lys\"\ntime 4/4\n")]
    [InlineData("time 4/4\npart m { clef treble }\n")]
    public void HasUsings_AgreesWithAWholeTreeScan(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(
            tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().Any(),
            UsingExpander.HasUsings(tree));
    }

    // ---- LYS0029: a `using` outside the top level -------------------------------------
    //
    // FIVE spellings put one where only the file level can hold it, and all five used to
    // consume it with a bare `Advance()` — which drops the tokens' WIDTH from the green
    // tree. A node's position is the running sum of the widths before it, so every source
    // offset after the directive slid left. Measured 2026-08-16 on the section spelling:
    // the tree spelled itself back 16 characters short, every data-pos in the SVG pointed
    // 16 early (52/55/57/59/61 — the offsets that book has with the line DELETED — for
    // notes truly at 68/71/73/75/77), and `check --pitches` read the directive's own
    // letters as the music (g, n, lys, s where the file says c d e f).
    //
    // ⚠️ Four were silent; the part header already reported LYS0025 and dropped the width
    // anyway. Reporting and keeping are different repairs.

    public static TheoryData<string> MisplacedUsings() => new()
    {
        "time 4/4\n\npart m { clef treble }\n\nsection A {\n  using \"n.lys\"\n  m { c4 d e f | }\n}\n",
        "time 4/4\n\npart m { clef treble }\n\nsection A { m { c4 d e f | } }\n\nform main { ~A }\n\nscore main { using \"n.lys\" staff m }\n",
        "form main { using \"n.lys\" ~A }\n",
        "part m { clef treble using \"n.lys\" }\n",
        "section A {\n  m { c4 using \"n.lys\" d e f | }\n}\n",
    };

    [Theory]
    [MemberData(nameof(MisplacedUsings))]
    public void AMisplacedUsing_KeepsItsWidth(string source)
        => Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());

    /// <summary>
    /// The invariant that keeps <see cref="UsingExpander.HasUsings"/>'s root-children-only
    /// spelling honest now that a directive CAN appear deeper: everything that spelling
    /// misses is reported and never expanded.
    /// </summary>
    [Theory]
    [MemberData(nameof(MisplacedUsings))]
    public void EveryUsingHasUsingsSkips_IsReported(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var root = tree.GetRoot();
        var missed = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Except(root.ChildNodes().OfType<UsingDirectiveSyntax>())
            .ToList();

        // ⚠️ Positive control. Without it this passes on a source that hides no directive
        // at all — an "every X is Y" claim is true of the empty set (RULES §5.4).
        Assert.NotEmpty(missed);

        // Skipped by the keystroke-path question, so never expanded...
        Assert.False(UsingExpander.HasUsings(tree));
        // ...and therefore each one has to say so.
        foreach (var m in missed)
        {
            int end = m.Position + m.ToFullString().Length;
            Assert.Contains(tree.Diagnostics, d =>
                d.Code == DiagnosticCodes.UsingMustBeTopLevel
                && d.Span.Start >= m.Position && d.Span.Start < end);
        }
    }

    /// <summary>
    /// The quantity the drop corrupted, stated directly: every node stands where it says
    /// it stands. This fails one character at a time and never at the node that lost the
    /// token, which is why the defect outlived a green suite. The control (same book, the
    /// directive moved to the top level) says the mapping is right to begin with.
    /// </summary>
    [Theory]
    [InlineData("time 4/4\n\npart m { clef treble }\n\nsection A {\n  using \"n.lys\"\n  m { c4 d e f | }\n}\n")]
    [InlineData("time 4/4\n\nusing \"n.lys\"\n\npart m { clef treble }\n\nsection A {\n  m { c4 d e f | }\n}\n")]
    public void AMisplacedUsing_DoesNotSlideTheNodesAfterIt(string source)
    {
        var root = SyntaxTree.Parse(source).GetRoot();
        Assert.Equal(source.Length, root.FullWidth);

        foreach (var node in root.DescendantNodes())
        {
            var text = node.ToFullString();
            Assert.True(
                node.Position + text.Length <= source.Length
                && source.AsSpan(node.Position, text.Length).SequenceEqual(text),
                $"{node.Kind} at {node.Position} spells [{text}] but the source there is "
                + $"[{source.Substring(node.Position, Math.Min(text.Length, source.Length - node.Position))}]");
        }
    }

    [Fact]
    public void MainText_StaysThePrefix()
    {
        // Positions in the file the user edits must not move, so the main text is
        // the literal prefix of the expanded source.
        const string main = "title \"x\"\nusing \"a.lys\"\nscore { staff p }\n";
        var expanded = Expand(main, new() { ["a.lys"] = "part p { clef treble }" });
        Assert.StartsWith(main, expanded);
        Assert.Contains("part p", expanded);
    }

    [Fact]
    public void Diamond_IncludesSharedFileOnce()
    {
        var expanded = Expand("using \"a.lys\"\nusing \"b.lys\"\n", new()
        {
            ["a.lys"] = "using \"c.lys\"\npart a { clef treble }",
            ["b.lys"] = "using \"c.lys\"\npart b { clef bass }",
            ["c.lys"] = "part c { clef alto }",
        });

        int first = expanded.IndexOf("part c");
        int last = expanded.LastIndexOf("part c");
        Assert.True(first >= 0 && first == last, "shared using c.lys must appear exactly once");
    }

    [Fact]
    public void Cycle_Terminates()
    {
        // a includes b, b includes a — expansion must not loop.
        var expanded = Expand("using \"a.lys\"\n", new()
        {
            ["a.lys"] = "using \"b.lys\"\npart a { clef treble }",
            ["b.lys"] = "using \"a.lys\"\npart b { clef bass }",
        });
        Assert.Contains("part a", expanded);
        Assert.Contains("part b", expanded);
    }

    [Fact]
    public void MissingFile_IsSkipped()
    {
        var expanded = Expand("title \"x\"\nusing \"nope.lys\"\n", new());
        Assert.Contains("title \"x\"", expanded);
    }

    // ---- LYS0028: a `using` that resolves to nothing is reported ------------------
    //
    // Skipping is the DESIGN (a missing include must never abort the render, because the
    // LSP preview resolves includes from disk on every keystroke). Skipping in SILENCE
    // was not: measured 2026-08-16, a book whose only difference was a misspelt include
    // passed `lysc check` as "No errors found." and drew — with data-pos masked —
    // character-for-character what the same book draws with the `using` line deleted.

    [Fact]
    public void MissingFile_IsReported()
    {
        var (_, diagnostics) = ExpandWithDiagnostics("title \"x\"\nusing \"nope.lys\"\n", new());

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UsingFileUnreadable, d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        // Name the path the author wrote, not the resolved absolute one: that is the text
        // they have to look at to see the typo.
        Assert.Contains("nope.lys", d.Message);
    }

    [Fact]
    public void MissingFile_IsReportedAtItsOwnPathToken()
    {
        // The whole point is to point at the line that IS wrong. Before this, the only
        // diagnostics a misspelt include produced were "Undefined section" / "Undefined
        // part" against lines that were CORRECT.
        const string main = "title \"x\"\nusing \"nope.lys\"\nscore { staff p }\n";
        var (_, diagnostics) = ExpandWithDiagnostics(main, new());

        var d = Assert.Single(diagnostics);
        Assert.Equal(main.IndexOf("\"nope.lys\""), d.Span.Start);
        Assert.Equal("\"nope.lys\"".Length, d.Span.Length);
    }

    [Fact]
    public void EmptyPath_IsReported()
    {
        // No state of the file system can make `using ""` right, so it is the same
        // silence with a different cause — and gets the same name rather than a second.
        var (_, diagnostics) = ExpandWithDiagnostics("using \"\"\ntitle \"x\"\n", new());

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UsingFileUnreadable, d.Code);
        Assert.Contains("empty path", d.Message);
    }

    [Fact]
    public void ResolvedInclude_ReportsNothing()
    {
        var (_, diagnostics) = ExpandWithDiagnostics("using \"a.lys\"\n",
            new() { ["a.lys"] = "part p { clef treble }" });

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AlreadyIncludedFile_ReportsNothing()
    {
        // The diamond: c.lys resolves twice and is appended once. It RESOLVED — the
        // dedupe is the feature, not a failure, and must stay quiet or every shared
        // include in a real piece would warn.
        var (_, diagnostics) = ExpandWithDiagnostics("using \"a.lys\"\nusing \"b.lys\"\n", new()
        {
            ["a.lys"] = "using \"c.lys\"\npart a { clef treble }",
            ["b.lys"] = "using \"c.lys\"\npart b { clef bass }",
            ["c.lys"] = "part c { clef alto }",
        });

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Cycle_ReportsNothing()
    {
        var (_, diagnostics) = ExpandWithDiagnostics("using \"a.lys\"\n", new()
        {
            ["a.lys"] = "using \"b.lys\"\npart a { clef treble }",
            ["b.lys"] = "using \"a.lys\"\npart b { clef bass }",
        });

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MissingFile_InsideAnInclude_IsReportedToo()
    {
        // The walk is depth-first, so a broken include inside a working one must not be
        // swallowed by the level above it.
        var (_, diagnostics) = ExpandWithDiagnostics("using \"a.lys\"\n", new()
        {
            ["a.lys"] = "using \"gone.lys\"\npart a { clef treble }",
        });

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCodes.UsingFileUnreadable, d.Code);
        Assert.Contains("gone.lys", d.Message);
    }

    [Fact]
    public void CrossFile_DuplicateCell_IsError()
    {
        // Two files both supply (section A x part p) — a duplicated cell across files.
        var expanded = Expand("using \"a.lys\"\nusing \"b.lys\"\nform main { A }\n", new()
        {
            ["a.lys"] = "part vln { clef treble section A { c4 d e f | } }",
            ["b.lys"] = "part vln { clef treble section A { g4 a b c | } }",
        });

        var v = new DuplicateCellValidator();
        v.Validate(SyntaxTree.Parse(expanded));
        Assert.Equal(DiagnosticCodes.DuplicateCell, Assert.Single(v.Diagnostics).Code);
    }

    [Fact]
    public void CrossFile_DistinctParts_AreClean()
    {
        var expanded = Expand("using \"rh.lys\"\nusing \"lh.lys\"\nform main { A }\n", new()
        {
            ["rh.lys"] = "part rh { clef treble section A { c'4 d' e' f' | } }",
            ["lh.lys"] = "part lh { clef bass    section A { c4 g, c, g, | } }",
        });

        var tree = SyntaxTree.Parse(expanded);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var v = new DuplicateCellValidator();
        v.Validate(tree);
        Assert.Empty(v.Diagnostics);
    }
}
