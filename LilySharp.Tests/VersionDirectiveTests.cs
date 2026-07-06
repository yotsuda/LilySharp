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
/// The optional <c>version "…"</c> directive: a soft top-level marker that lets
/// future grammar revisions branch on a document's declared version. It must NOT
/// reserve the word <c>version</c>, and must preserve the parser's round-trip.
/// </summary>
[Trait("Category", "Unit")]
public class VersionDirectiveTests
{
    [Fact]
    public void ParsesTopLevelVersionDirective()
    {
        var tree = SyntaxTree.Parse("version \"1\"\npart m { section A { c4 } }\nscore \"s\" { staff m }");
        Assert.False(tree.HasErrors);
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
    public void VersionIsNotReserved_StillUsableAsAVariableName()
    {
        // `version` followed by `=` is a variable declaration, not the directive:
        // the soft keyword only fires before a string literal.
        var tree = SyntaxTree.Parse("version = { c4 d4 }");
        Assert.Null(tree.DeclaredVersion);
        Assert.Empty(tree.GetNodes<VersionDeclarationSyntax>());
        Assert.Single(tree.GetNodes<VariableDeclarationSyntax>());
    }

    [Fact]
    public void PreservesRoundTrip()
    {
        const string src = "version \"1\"\npart m { section A { cis'4 } }\nscore \"s\" { staff m }";
        var tree = SyntaxTree.Parse(src);
        Assert.Equal(src, tree.ToFullString());
    }
}
