using Xunit;
using Lilysharp.Core.Syntax;

namespace Lilysharp.Tests;

public class IncrementalParsingTests
{
    [Fact]
    public void WithChange_Insert_UpdatesTree()
    {
        var tree = SyntaxTree.Parse("c4 d4 e4");
        var newTree = tree.WithChange(TextChange.Insert(3, " f4"));
        
        Assert.Equal("c4  f4d4 e4", newTree.Text);
    }

    [Fact]
    public void WithChange_Delete_UpdatesTree()
    {
        var tree = SyntaxTree.Parse("c4 d4 e4");
        var newTree = tree.WithChange(TextChange.Delete(new TextSpan(3, 3))); // delete "d4 "
        
        Assert.Equal("c4 e4", newTree.Text);
    }

    [Fact]
    public void WithChange_Replace_UpdatesTree()
    {
        var tree = SyntaxTree.Parse("c4 d4 e4");
        var newTree = tree.WithChange(TextChange.Replace(new TextSpan(3, 2), "f8"));
        
        Assert.Equal("c4 f8 e4", newTree.Text);
    }

    [Fact]
    public void WithChanges_MultipleChanges_AppliedCorrectly()
    {
        var tree = SyntaxTree.Parse("c4 d4 e4");
        var newTree = tree.WithChanges(
            TextChange.Replace(new TextSpan(0, 2), "g2"),  // c4 -> g2
            TextChange.Replace(new TextSpan(6, 2), "a8")   // e4 -> a8
        );
        
        Assert.Equal("g2 d4 a8", newTree.Text);
    }

    [Fact]
    public void WithChanges_EmptyChanges_ReturnsSameTree()
    {
        var tree = SyntaxTree.Parse("c4 d4");
        var newTree = tree.WithChanges();
        
        Assert.Same(tree, newTree);
    }

    [Fact]
    public void WithChange_PreservesParseability()
    {
        var tree = SyntaxTree.Parse("relative c' { c d e f }");
        var newTree = tree.WithChange(TextChange.Replace(new TextSpan(14, 1), "c"));
        
        Assert.False(newTree.HasErrors);
        Assert.Equal("relative c' { c d e f }", newTree.Text);
    }

    [Fact]
    public void WithChange_InvalidEdit_ProducesErrors()
    {
        var tree = SyntaxTree.Parse("c4 d4 e4");
        var newTree = tree.WithChange(TextChange.Insert(3, "{ "));  // Unclosed brace
        
        Assert.True(newTree.HasErrors);
    }
}