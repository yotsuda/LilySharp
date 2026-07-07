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

    [Fact]
    public void Parses_UsingDirective()
    {
        var tree = SyntaxTree.Parse("using \"parts.lys\"\nscore { staff rh }\n");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
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

    [Fact]
    public void CrossFile_DuplicateCell_IsError()
    {
        // Two files both supply (section A x part p) — a duplicated cell across files.
        var expanded = Expand("using \"a.lys\"\nusing \"b.lys\"\nstructure { A }\n", new()
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
        var expanded = Expand("using \"rh.lys\"\nusing \"lh.lys\"\nstructure { A }\n", new()
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
