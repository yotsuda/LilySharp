using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Semantics.Binding;
using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

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
    public void Expand_Minuet_ExpandsCorrectly()
    {
        // Real-world test with minuet.lys structure
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\minuet.lys");
        
        var tree = SyntaxTree.Parse(source);
        var collector = new SymbolCollector();
        var result = collector.Collect(tree);
        
        Assert.True(result.Success, $"Symbol collection failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.Symbols.Structure);
        
        var diagnostics = new List<SemanticDiagnostic>();
        var expander = new StructureExpander(result.Symbols, diagnostics);
        var expanded = expander.Expand(result.Symbols.Structure!.DeclarationSyntax);
        
        Assert.Empty(diagnostics);
        
        // Should have rightHand and leftHand parts
        Assert.Contains("rightHand", expanded.PartNames);
        Assert.Contains("leftHand", expanded.PartNames);
        
        // Structure: |: A [1. A1] [2. A2] :| expands to A, A1, A, A2
        // Each section has notes for both parts
        var rightHandNotes = expanded.GetPart("rightHand").Count(n => n is NoteSyntax);
        var leftHandNotes = expanded.GetPart("leftHand").Count(n => n is NoteSyntax or ChordSyntax);
        
        Assert.True(rightHandNotes > 0, "rightHand should have notes");
        Assert.True(leftHandNotes > 0, "leftHand should have notes");
    }
}
