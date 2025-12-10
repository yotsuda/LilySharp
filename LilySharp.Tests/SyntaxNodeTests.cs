using Xunit;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

public class SyntaxNodeTests
{
    [Fact]
    public void GetRoot_ReturnsCompilationUnit()
    {
        var tree = SyntaxTree.Parse("c d e f");
        var root = tree.GetRoot();
        
        Assert.NotNull(root);
        Assert.IsType<CompilationUnitSyntax>(root);
        Assert.Equal(SyntaxKind.CompilationUnit, root.Kind);
    }

    [Fact]
    public void Position_IsCorrect()
    {
        var tree = SyntaxTree.Parse("c d e f");
        var root = tree.GetRoot();
        
        Assert.Equal(0, root.Position);
        Assert.Equal(7, root.FullWidth); // "c d e f"
    }

    [Fact]
    public void FindNode_ReturnsCorrectNode()
    {
        var tree = SyntaxTree.Parse("c d e f");
        
        // Find node at position 2 (should be "d" pitch or note)
        var node = tree.FindNode(2);
        
        Assert.NotNull(node);
        // FindNode returns the deepest node, which is the Pitch inside the Note
        Assert.True(node.Kind == SyntaxKind.PitchD || node.Kind == SyntaxKind.Note,
            $"Expected PitchD or Note, got {node.Kind}");
    }

    [Fact]
    public void DescendantNodes_ReturnsAllNodes()
    {
        var tree = SyntaxTree.Parse("c4 d e");
        var root = tree.GetRoot();
        
        var allNotes = root.DescendantNodes<NoteSyntax>().ToList();
        
        Assert.Equal(3, allNotes.Count);
    }

    [Fact]
    public void NoteSyntax_HasPitch()
    {
        var tree = SyntaxTree.Parse("cis''4");
        var note = tree.GetRoot().DescendantNodes<NoteSyntax>().First();
        
        Assert.NotNull(note.Pitch);
        Assert.Equal("cis", note.Pitch.PitchName);
        Assert.Equal(2, note.Pitch.OctaveOffset);
    }

    [Fact]
    public void NoteSyntax_HasDuration()
    {
        var tree = SyntaxTree.Parse("c4.");
        var note = tree.GetRoot().DescendantNodes<NoteSyntax>().First();
        
        Assert.NotNull(note.Duration);
        Assert.Equal(4, note.Duration.Value);
        Assert.Equal(1, note.Duration.DotCount);
    }

    [Fact]
    public void TextSpan_IsCorrect()
    {
        var tree = SyntaxTree.Parse("c d e");
        var notes = tree.GetRoot().DescendantNodes<NoteSyntax>().ToList();
        
        // "c" at position 0
        Assert.Equal(0, notes[0].Position);
        
        // "d" at position 2
        Assert.Equal(2, notes[1].Position);
        
        // "e" at position 4
        Assert.Equal(4, notes[2].Position);
    }

    [Fact]
    public void MusicBlock_HasItems()
    {
        var tree = SyntaxTree.Parse("{ c d e }");
        var block = tree.GetRoot().DescendantNodes<MusicBlockSyntax>().First();
        
        Assert.Equal(3, block.Items.Count());
    }

    [Fact]
    public void RelativeExpression_HasBasePitchAndBody()
    {
        var tree = SyntaxTree.Parse("relative c' { d e f }");
        var rel = tree.GetRoot().DescendantNodes<RelativeExpressionSyntax>().First();
        
        Assert.NotNull(rel.BasePitch);
        Assert.Equal("c", rel.BasePitch.PitchName);
        Assert.Equal(1, rel.BasePitch.OctaveOffset);
        
        Assert.NotNull(rel.Body);
        Assert.Equal(3, rel.Body.Items.Count());
    }

    [Fact]
    public void SlurSyntax_IsOpen()
    {
        var tree = SyntaxTree.Parse("c( d )");
        var slurs = tree.GetRoot().DescendantNodes<SlurSyntax>().ToList();
        
        Assert.Equal(2, slurs.Count);
        Assert.True(slurs[0].IsOpen);
        Assert.False(slurs[1].IsOpen);
    }

    [Fact]
    public void GetNodes_FindsAllOfType()
    {
        var tree = SyntaxTree.Parse("c4 r4 <e g>4 | d2");
        
        var notes = tree.GetNodes<NoteSyntax>().ToList();
        var rests = tree.GetNodes<RestSyntax>().ToList();
        var chords = tree.GetNodes<ChordSyntax>().ToList();
        var barlines = tree.GetNodes<BarlineSyntax>().ToList();
        
        Assert.Equal(2, notes.Count);
        Assert.Single(rests);
        Assert.Single(chords);
        Assert.Single(barlines);
    }

    [Fact]
    public void ComplexStructure_NavigatesCorrectly()
    {
        var tree = SyntaxTree.Parse(@"score {
    part Piano {
        relative c' { c d e f }
    }
}");
        var root = tree.GetRoot();
        
        var scores = root.DescendantNodes<ScoreDeclarationSyntax>().ToList();
        var parts = root.DescendantNodes<PartDeclarationSyntax>().ToList();
        var relatives = root.DescendantNodes<RelativeExpressionSyntax>().ToList();
        var notes = root.DescendantNodes<NoteSyntax>().ToList();
        
        Assert.Single(scores);
        Assert.Single(parts);
        Assert.Single(relatives);
        Assert.Equal(4, notes.Count);
    }

    [Fact]
    public void Parent_Navigation()
    {
        var tree = SyntaxTree.Parse("relative c' { d e }");
        var note = tree.GetRoot().DescendantNodes<NoteSyntax>().First();
        
        // Note -> MusicBlock -> RelativeExpression -> CompilationUnit
        Assert.NotNull(note.Parent);
        Assert.Equal(SyntaxKind.MusicBlock, note.Parent.Kind);
        Assert.NotNull(note.Parent.Parent);
        Assert.Equal(SyntaxKind.RelativeExpression, note.Parent.Parent.Kind);
    }
}