using Xunit;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

public class IntegrationTests
{
    [Fact]
    public void ParseHappyBirthday()
    {
        var source = File.ReadAllText("../../../../samples/happy-birthday.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseFurElise()
    {
        var source = File.ReadAllText("../../../../samples/fur-elise.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void ParseMinuet()
    {
        var source = File.ReadAllText("../../../../samples/minuet.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        Assert.Equal(source, tree.ToFullString());
    }
}