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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class StructureDeclarationValidatorTests
{
    private static string Wrap(string structures) => $@"
title ""t""
time 4/4
part melody
section A {{ melody {{ c'4 d e f | }} }}
section B {{ melody {{ g'4 a b c | }} }}
{structures}
score {{ staff {{ melody }} }}
";

    [Fact]
    public void SingleStructure_NoError()
    {
        var tree = SyntaxTree.Parse(Wrap("structure { A B }"));
        var validator = new StructureDeclarationValidator();
        validator.Validate(tree);
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void NoStructure_NoError()
    {
        // Omitting structure is valid — sections play in declaration order.
        var tree = SyntaxTree.Parse(Wrap(""));
        var validator = new StructureDeclarationValidator();
        validator.Validate(tree);
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void TwoStructures_ReportsOneErrorOnTheSecond()
    {
        var tree = SyntaxTree.Parse(Wrap("structure { A B }\nstructure { B A }"));
        var validator = new StructureDeclarationValidator();
        validator.Validate(tree);

        Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.MultipleStructureDeclarations, validator.Diagnostics[0].Code);
    }

    [Fact]
    public void ThreeStructures_FlagsEveryExtra()
    {
        var tree = SyntaxTree.Parse(Wrap("structure { A }\nstructure { B }\nstructure { A B }"));
        var validator = new StructureDeclarationValidator();
        validator.Validate(tree);

        // First is the effective one; the 2nd and 3rd are flagged.
        Assert.Equal(2, validator.Diagnostics.Count);
        Assert.All(validator.Diagnostics,
            d => Assert.Equal(DiagnosticCodes.MultipleStructureDeclarations, d.Code));
    }
}
