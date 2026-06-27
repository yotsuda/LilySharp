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
/// Part-major form (`part X { section A { music } }`) and the (section x part)
/// cell-uniqueness rule shared with section-major form.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PartMajorTests
{
    private static string?[] Labels(string source)
    {
        // Pin the voice the way the render pipeline does (SvgGenerator passes the
        // selected render's part); the no-arg default only infers it from `staff`.
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source), "bl");
        return score.Voice.Measures.Select(m => m.SectionLabel).ToArray();
    }

    private const string PartMajor = """
        part bl { clef bass tuning bass
          section Intro { c4 d e f | }
          section Verse { g4 a b c | }
        }
        structure { Intro Verse }
        score "x" { tab bl }
        """;

    private const string SectionMajor = """
        part bl { clef bass tuning bass }
        section Intro { bl { c4 d e f | } }
        section Verse { bl { g4 a b c | } }
        structure { Intro Verse }
        score "x" { tab bl }
        """;

    [Fact]
    public void PartMajor_Parses_WithoutErrors()
    {
        var tree = SyntaxTree.Parse(PartMajor);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void PartMajor_RendersSectionsInStructureOrder()
    {
        Assert.Equal(new[] { "Intro", "Verse" }, Labels(PartMajor));
    }

    [Fact]
    public void PartMajor_IsEquivalentToSectionMajor()
    {
        // The two orientations describe the same grid, so they must collect the
        // same section sequence for the part.
        Assert.Equal(Labels(SectionMajor), Labels(PartMajor));
    }

    [Fact]
    public void PartMajor_NoStructure_UsesDeclarationOrder()
    {
        var labels = Labels("""
            part bl { clef bass tuning bass
              section Intro { c4 d e f | }
              section Verse { g4 a b c | }
            }
            score "x" { tab bl }
            """);
        Assert.Equal(new[] { "Intro", "Verse" }, labels);
    }

    [Fact]
    public void PartMajor_TwoParts_GrandStaff_Parses()
    {
        var tree = SyntaxTree.Parse("""
            part rh { clef treble  section A { c'4 d' e' f' | } }
            part lh { clef bass     section A { c4 g, c, g, | } }
            structure { A }
            score "x" { staff rh  staff lh }
            """);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void DuplicateCell_PartMajor_SameSectionTwice_IsError()
    {
        var v = new DuplicateCellValidator();
        v.Validate(SyntaxTree.Parse(
            "part bl { section A { c4 } section A { d4 } }\nscore { tab bl }\n"));
        Assert.Equal(DiagnosticCodes.DuplicateCell, Assert.Single(v.Diagnostics).Code);
    }

    [Fact]
    public void DuplicateCell_SectionMajor_SamePartTwice_IsError()
    {
        var v = new DuplicateCellValidator();
        v.Validate(SyntaxTree.Parse(
            "section A { bl { c4 } bl { d4 } }\nscore { tab bl }\n"));
        Assert.Equal(DiagnosticCodes.DuplicateCell, Assert.Single(v.Diagnostics).Code);
    }

    [Fact]
    public void DistinctCells_DifferentParts_SameSection_AreClean()
    {
        // (A, rh) and (A, lh) are different cells — a section gathers many parts.
        var v = new DuplicateCellValidator();
        v.Validate(SyntaxTree.Parse("""
            part rh { section A { c'4 } }
            part lh { section A { c4 } }
            structure { A }
            score "x" { staff rh  staff lh }
            """));
        Assert.Empty(v.Diagnostics);
    }
}
