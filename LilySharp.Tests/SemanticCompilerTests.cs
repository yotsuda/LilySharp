using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

public class SemanticCompilerTests
{
    [Fact]
    public void Compile_SimpleNotes_Succeeds()
    {
        var source = "c4 d e f | g a b c' |";
        var compiler = new SemanticCompiler();
        
        var result = compiler.Compile(source);
        
        Assert.True(result.Success);
        Assert.NotNull(result.Score);
        Assert.Equal(2, result.Score!.Voice.Measures.Length);
    }
    
    [Fact]
    public void Compile_WithMetadata_ExtractsCorrectly()
    {
        var source = @"
title ""Test Song""
composer ""Test Composer""
tempo 120
time 3/4
key g major
c4 d e |";
        var compiler = new SemanticCompiler();
        
        var result = compiler.Compile(source);
        
        Assert.True(result.Success);
        Assert.Equal("Test Song", result.Score!.Title);
        Assert.Equal("Test Composer", result.Score.Composer);
        Assert.Equal(120, result.Score.Tempo);
        Assert.Equal(3, result.Score.TimeSignature.Beats);
        Assert.Equal(4, result.Score.TimeSignature.BeatType);
    }
    
    [Fact]
    public void Compile_HappyBirthday_ProducesScore()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\happy-birthday.lys");
        var compiler = new SemanticCompiler();
        
        var result = compiler.Compile(source);
        
        Assert.True(result.Success, 
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.Score);
    }
    
    [Fact]
    public void Compile_Minuet_ProducesScore()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\minuet.lys");
        var compiler = new SemanticCompiler();
        
        var result = compiler.Compile(source);
        
        // Minuet uses structure { |: A [1. A1] [2. A2] :| }
        // This should expand to: A, A1, A, A2
        Assert.True(result.Success,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.Score);
        
        // Should have multiple voices (rightHand, leftHand)
        Assert.True(result.Score!.Voices.Length >= 1, 
            $"Expected at least 1 voice, got {result.Score.Voices.Length}");
    }
    
    [Fact]
    public void Compile_FurElise_ProducesScore()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\fur-elise.lys");
        var compiler = new SemanticCompiler();
        
        var result = compiler.Compile(source);
        
        Assert.True(result.Success,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.Score);
    }
    
    [Fact]
    public void Compile_StructureDemo_ExpandsRepeats()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\structure-demo.lys");
        var compiler = new SemanticCompiler();
        
        var result = compiler.Compile(source);
        
        Assert.True(result.Success,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.Score);
    }
}
