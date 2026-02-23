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

using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Semantics.Binding;
using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class StructureExpanderTests
{
    [Fact]
    public void Expand_SimpleSections_ExpandsInOrder()
    {
        var source = @"
section A { melody { c4 d e f | } }
section B { melody { g4 a b c' | } }
structure { A B }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success, $"Symbol collection failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.Symbols.Structure);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // Count total notes across all parts
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(8, totalNotes);
    }

    [Fact]
    public void Expand_RepeatBlock_ExpandsTwice()
    {
        var source = @"
section A { melody { c4 d e f | } }
structure { |: A :| }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // A has 4 notes, repeated twice = 8 notes
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(8, totalNotes);
    }

    [Fact]
    public void Expand_RepeatWithAlternatives_ExpandsCorrectly()
    {
        // |: A [1. A1] [2. A2] :| should expand to: A, A1, A, A2
        var source = @"
section A { melody { c4 d e f | } }
section A1 { melody { g4 g g g | } }
section A2 { melody { a4 a a a | } }
structure { |: A [1. A1] [2. A2] :| }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success, $"Symbol collection failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // A(4) + A1(4) + A(4) + A2(4) = 16 notes
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(16, totalNotes);
    }

    [Fact]
    public void Expand_MultiplePartsInSection_ExpandsBoth()
    {
        var source = @"
section A {
  rightHand { c'4 d' e' f' | }
  leftHand { c4 g c g | }
}
structure { A }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success, $"Symbol collection failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // Total notes: rightHand(4) + leftHand(4) = 8
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(8, totalNotes);

        // Check specific parts exist
        Assert.Contains("rightHand", expanded.PartNames);
        Assert.Contains("leftHand", expanded.PartNames);
    }

    [Fact]
    public void Expand_UndefinedSection_ReportsError()
    {
        var source = @"
section A { melody { c4 d e f | } }
structure { A UndefinedSection }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Single(diagnostics);
        Assert.Contains("Undefined section", diagnostics[0].Message);
        Assert.Contains("UndefinedSection", diagnostics[0].Message);
    }

    [Fact]
    public void Expand_ThreeAlternatives_ExpandsThreeTimes()
    {
        // |: A [1. B] [2. C] [3. D] :| should expand to: A, B, A, C, A, D
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
section C { melody { e4 | } }
section D { melody { f4 | } }
structure { |: A [1. B] [2. C] [3. D] :| }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // A(1) + B(1) + A(1) + C(1) + A(1) + D(1) = 6 notes
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(6, totalNotes);
    }

    [Fact]
    public void Expand_MixedStructure_ExpandsInOrder()
    {
        // Intro, |: Verse [1. Bridge] [2. Chorus] :|
        // Should expand to: Intro, Verse, Bridge, Verse, Chorus
        var source = @"
section Intro { melody { c4 | } }
section Verse { melody { d4 | } }
section Bridge { melody { e4 | } }
section Chorus { melody { f4 | } }
structure { Intro |: Verse [1. Bridge] [2. Chorus] :| }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success, $"Symbol collection failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // Intro(1) + Verse(1) + Bridge(1) + Verse(1) + Chorus(1) = 5 notes
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(5, totalNotes);
    }

    [Fact]
    public void Expand_MultipleParts_ExpandsCorrectly()
    {
        var source = @"
section A {
    rightHand { c4 d | }
    leftHand { <c e>4 <d f> | }
}
structure { |: A :| }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);
        Assert.NotNull(result.Symbols.Structure);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);
        Assert.Contains("rightHand", expanded.PartNames);
        Assert.Contains("leftHand", expanded.PartNames);
    }

    [Fact]
    public void Expand_ConsecutiveRepeatBlocks_ExpandsBothCorrectly()
    {
        // |: A :| |: B :| should expand to: A, A, B, B
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
structure { |: A :| |: B :| }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // A(1) + A(1) + B(1) + B(1) = 4 notes
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(4, totalNotes);
    }

    [Fact]
    public void Expand_VerifyNoteOrder_ExpandsInCorrectSequence()
    {
        // Verify the actual order of notes after expansion
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
section C { melody { e4 | } }
structure { A B C }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        var melodyPart = expanded.PartNames.FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
        var notes = expanded.GetPart(melodyPart).OfType<NoteSyntax>().ToList();

        Assert.Equal(3, notes.Count);
        // Notes should be in order: c, d, e
        Assert.Equal("c", notes[0].Pitch.BaseName.ToString());
        Assert.Equal("d", notes[1].Pitch.BaseName.ToString());
        Assert.Equal("e", notes[2].Pitch.BaseName.ToString());
    }

    [Fact]
    public void Expand_RepeatWithAlternatives_VerifyOrder()
    {
        // |: A [1. B] [2. C] :| should produce: A, B, A, C in that order
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
section C { melody { e4 | } }
structure { |: A [1. B] [2. C] :| }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        var melodyPart = expanded.PartNames.FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
        var notes = expanded.GetPart(melodyPart).OfType<NoteSyntax>().ToList();

        Assert.Equal(4, notes.Count);
        // Order: A(c), B(d), A(c), C(e)
        Assert.Equal("c", notes[0].Pitch.BaseName.ToString());
        Assert.Equal("d", notes[1].Pitch.BaseName.ToString());
        Assert.Equal("c", notes[2].Pitch.BaseName.ToString());
        Assert.Equal("e", notes[3].Pitch.BaseName.ToString());
    }

    [Fact]
    public void Expand_EmptySection_HandlesGracefully()
    {
        var source = @"
section Empty { melody { } }
section A { melody { c4 | } }
structure { Empty A }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // Only A has 1 note
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(1, totalNotes);
    }

    [Fact]
    public void Expand_SingleSection_ExpandsOnce()
    {
        var source = @"
section OnlySection { melody { c4 d4 e4 f4 | } }
structure { OnlySection }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(4, totalNotes);
    }

    [Fact]
    public void Expand_WithNavigationMarks_IgnoresMarks()
    {
        // Navigation marks (@segno, @fine, etc.) should be ignored for now
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
structure { @segno A @fine B }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        // Should not error on navigation marks
        Assert.Empty(diagnostics);

        // Both sections should be expanded
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(2, totalNotes);
    }

    [Fact]
    public void Expand_RepeatedSameSectionMultipleTimes_ExpandsEachReference()
    {
        // A B A should expand A twice
        var source = @"
section A { melody { c4 | } }
section B { melody { d4 | } }
structure { A B A }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        // A(1) + B(1) + A(1) = 3 notes
        var totalNotes = expanded.PartNames.Sum(p => expanded.GetPart(p).Count(n => n is NoteSyntax));
        Assert.Equal(3, totalNotes);
    }

    [Fact]
    public void Expand_WithBarlines_IncludesBarlines()
    {
        var source = @"
section A { melody { c4 d4 | e4 f4 | } }
structure { A }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        var melodyPart = expanded.PartNames.FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
        var barlines = expanded.GetPart(melodyPart).Count(n => n is BarlineSyntax);

        Assert.Equal(2, barlines);
    }

    [Fact]
    public void Expand_WithRests_IncludesRests()
    {
        var source = @"
section A { melody { c4 r4 d4 r4 | } }
structure { A }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        var melodyPart = expanded.PartNames.FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
        var rests = expanded.GetPart(melodyPart).Count(n => n is RestSyntax);

        Assert.Equal(2, rests);
    }

    [Fact]
    public void Expand_WithChords_IncludesChords()
    {
        var source = @"
section A { melody { <c e g>4 <d f a>4 | } }
structure { A }";

        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);

        Assert.True(result.Success);

        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);

        Assert.Empty(diagnostics);

        var melodyPart = expanded.PartNames.FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
        var chords = expanded.GetPart(melodyPart).Count(n => n is ChordSyntax);

        Assert.Equal(2, chords);
    }


}
