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

using System.Linq;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Any word glued to <c>@!</c> (or to a form-level <c>@</c>) is a mark NAME, whatever the lexer
/// took it for — so the annotation validator names it, and the parser never reports a token
/// kind (HANDOFF §2 R12⒡, session 571).
/// </summary>
/// <remarks>
/// Parser.Form ExpectMarkName held a list of three extra kinds (IntegerLiteral, RestS, PitchF):
/// <c>@!f</c> drew the validator's "nothing of that name can be ended" while <c>@!mf</c>,
/// <c>@!p</c> and <c>@!r</c> drew "Expected 'Identifier', found 'DynamicMF'".
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MarkNameWordTests
{
    private static string Book(string mark) =>
        "part m { section A { c4" + mark + " d e f | } }\nform main { A }\nscore main { staff m }\n";

    [Theory]
    [InlineData("mf")] [InlineData("p")] [InlineData("r")]
    [InlineData("f")] [InlineData("s")] [InlineData("6")] [InlineData("rit")]
    public void AGluedWordAfterTheTerminatorIsItsName(string word)
    {
        var tree = SyntaxTree.Parse(Book("@!" + word));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var mark = Assert.Single(tree.GetRoot().DescendantNodes<MusicMarkSyntax>());
        Assert.Equal(word, mark.MarkName);
    }

    [Fact]
    public void ASpacedWordIsNotTaken()
    {
        // `@! r4` — the rest stays music; only the missing name is reported.
        var tree = SyntaxTree.Parse(Book("@! r4"));
        Assert.Contains(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotEmpty(tree.GetRoot().DescendantNodes<RestSyntax>());
    }
}
