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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>score { }</c> may carry its own <c>structure { ... }</c> to render a
/// different arrangement of the same sections (e.g. a practice excerpt),
/// overriding the file's top-level structure for that score only.
/// </summary>
[Trait("Category", "Unit")]
public class StructureOverrideTests
{
    private const string Source = """
        part melody {
          clef treble
          section Intro { c4 d e f | }
          section Verse { g4 a b c' | }
          section Outro { c'4 b a g | }
        }
        structure { Intro Verse Outro }
        score full { staff melody }
        score practice {
          structure { Intro }
          staff melody
        }
        """;

    private static int MeasureCount(string scoreName)
    {
        var tree = SyntaxTree.Parse(Source);
        var spec = RenderSpecParser.FindByName(tree, scoreName)!;
        var score = new MeasureCollector().Collect(tree, "melody", spec.LocalStructure);
        return score.Voice.Measures.Length;
    }

    [Fact]
    public void ScoreWithoutLocalStructure_UsesTopLevel()
    {
        // full = Intro + Verse + Outro = 3 measures.
        Assert.Equal(3, MeasureCount("full"));
    }

    [Fact]
    public void ScoreLocalStructure_OverridesTopLevel()
    {
        // practice = Intro only = 1 measure.
        Assert.Equal(1, MeasureCount("practice"));
    }

    [Fact]
    public void TopLevelPlusScoreLocal_IsNotADuplicate()
    {
        var tree = SyntaxTree.Parse(Source);
        var validator = new StructureDeclarationValidator();
        validator.Validate(tree);
        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void TwoTopLevelStructures_AreFlagged()
    {
        var tree = SyntaxTree.Parse("""
            part melody { section A { c4 d e f | } }
            structure { A }
            structure { A }
            score x { staff melody }
            """);
        var validator = new StructureDeclarationValidator();
        validator.Validate(tree);
        var diag = Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.MultipleStructureDeclarations, diag.Code);
    }

    [Fact]
    public void TwoStructuresInOneScore_AreFlagged()
    {
        var tree = SyntaxTree.Parse("""
            part melody { section A { c4 d e f | } section B { g4 a b c' | } }
            score x {
              structure { A }
              structure { B }
              staff melody
            }
            """);
        var validator = new StructureDeclarationValidator();
        validator.Validate(tree);
        var diag = Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.MultipleStructureDeclarations, diag.Code);
    }
}
