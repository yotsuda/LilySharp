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

using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The optional <c>version 1</c> directive: a top-level marker that lets future
/// grammar revisions branch on a document's declared version. The value is a bare
/// number (like <c>time</c>/<c>tempo</c>/<c>key</c>), not a quoted string. Must
/// parse, expose the version, and preserve the parser's round-trip.
/// </summary>
[Trait("Category", "Unit")]
public class VersionDirectiveTests
{
    [Fact]
    public void ParsesTopLevelVersionDirective()
    {
        var tree = SyntaxTree.Parse("version 1\npart m { section A { c4 } }\nscore \"s\" { staff m }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Equal("1", tree.DeclaredVersion);
        Assert.Single(tree.GetNodes<VersionDeclarationSyntax>());
    }

    [Fact]
    public void NoDirectiveMeansNullVersion()
    {
        var tree = SyntaxTree.Parse("part m { section A { c4 } }\nstructure { A }\nscore \"s\" { staff m }");
        Assert.False(tree.HasErrors);
        Assert.Null(tree.DeclaredVersion);
    }

    [Fact]
    public void VersionValueReadsBackVerbatim()
    {
        var tree = SyntaxTree.Parse("version 2\npart m { section A { c4 } }\nscore \"s\" { staff m }");
        Assert.Equal("2", tree.GetNodes<VersionDeclarationSyntax>().Single().Version);
    }

    [Fact]
    public void PreservesRoundTrip()
    {
        const string src = "version 1\npart m { section A { cis'4 } }\nscore \"s\" { staff m }";
        var tree = SyntaxTree.Parse(src);
        Assert.Equal(src, tree.ToFullString());
    }

    [Fact]
    public void QuotedVersion_IsRejectedWithAPointer()
    {
        // 'version "1"' (a LilyPond habit) is an error — the value is a bare number.
        var tree = SyntaxTree.Parse("version \"1\"\npart m { section A { c4 } }\nscore \"s\" { staff m }");
        Assert.True(tree.HasErrors);
        var diag = System.Linq.Enumerable.Single(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.VersionNumberNotQuoted);
        Assert.Contains("version 1", diag.Message);
        // Recovers: the declared version is still read (quotes stripped).
        Assert.Equal("1", tree.DeclaredVersion);
    }
}
