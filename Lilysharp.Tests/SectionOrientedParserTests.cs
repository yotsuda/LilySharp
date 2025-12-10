using Lilysharp.Core.Syntax;
using Xunit;

namespace Lilysharp.Tests;

public class SectionOrientedParserTests
{
    [Fact]
    public void ParseVariableDeclaration_NewStyle()
    {
        var source = "guitar_riff = { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseSectionDeclaration_Simple()
    {
        var source = @"
section Intro {
    guitar { c4 d e f }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseSectionDeclaration_WithKeyAndTempo()
    {
        var source = @"
section A {
    key c major
    tempo 120
    guitar { c4 d e f }
    bass { c,4 g, c, g, }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseStructureDeclaration_Simple()
    {
        var source = @"
structure {
    Intro
    A
    B
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseStructureDeclaration_WithNavigation()
    {
        var source = @"
structure {
    Intro
    segno
    A
    fine
    B
    ds al fine
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseRenderDeclaration_Staff()
    {
        var source = @"
render full ""output.svg"" {
    staff { guitar }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseRenderDeclaration_Tab()
    {
        var source = @"
render guitarTab ""guitar.svg"" {
    staff { guitar }
    tab guitar { guitar }
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseRenderDeclaration_Midi()
    {
        var source = @"
render audio ""song.mid"" {
    guitar channel:1 instrument:25
    bass channel:2 instrument:33
}";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
    
    [Fact]
    public void ParseCompleteFile_NewStyle()
    {
        var source = @"
title ""Test Song""
tempo 120
time 4/4
key c major

guitar_riff = { c4 d e f | g a b c' }

section Intro {
    guitar { guitar_riff }
    bass { c,4 g, c, g, }
}

section A {
    guitar { e4 f g a }
    bass { e,4 b, e, b, }
}

structure {
    Intro
    A
}

render full ""test.svg"" {
    staff { guitar }
    staff bass { bass }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }
}