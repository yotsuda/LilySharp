using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

public class SectionOrientedTests
{
    [Fact]
    public void ParseVariableDeclaration()
    {
        var source = "guitar_riff = { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseSectionDeclaration()
    {
        var source = """
            section Intro {
                guitar { c4 d e f }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseSectionWithMultipleParts()
    {
        var source = """
            section A {
                guitar { c4 d e f }
                bass { c,4 g, c, g, }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseSectionWithKeyAndTempo()
    {
        var source = """
            section Intro {
                key c major
                tempo 120
                guitar { c4 d e f }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseStructureDeclaration()
    {
        var source = """
            section A { guitar { c4 } }
            structure {
                A
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseStructureWithNavigationMarks()
    {
        var source = """
            section A { guitar { c4 } }
            section B { guitar { d4 } }
            structure {
                A
                segno
                B
                dc al fine
                fine
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseRenderDeclaration()
    {
        var source = """
            section A { guitar { c4 } }
            structure { A }
            render full "output.svg" {
                staff { guitar }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseRenderWithTabAndStaff()
    {
        var source = """
            section A { guitar { c4 d e f } }
            structure { A }
            render guitarPart "guitar.pdf" {
                staff { guitar }
                tab guitar { guitar }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseRenderMidi()
    {
        var source = """
            section A { guitar { c4 } }
            structure { A }
            render audio "song.mid" {
                guitar channel:1 instrument:25
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
    
    [Fact]
    public void ParseCompleteFile()
    {
        var source = """
            title "Test Song"
            tempo 120
            time 4/4
            key c major
            
            guitar_riff = { c4 d e f }
            
            section Intro {
                guitar { guitar_riff }
                bass { c,4 g, c, g, }
            }
            
            section A {
                key g major
                guitar { g4 a b c' }
                bass { g,4 d, g, d, }
            }
            
            structure {
                Intro
                A
                fine
            }
            
            render full "test.svg" {
                staff { guitar }
                tab guitar { guitar }
                staff bass { bass }
            }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
}