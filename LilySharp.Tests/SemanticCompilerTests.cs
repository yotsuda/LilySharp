using Xunit;
using LilySharp.Core.Semantics;

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
    public void Compile_SectionStructure_ProducesScore()
    {
        var source = @"
section A {
    melody { c4 d e f | }
    bass { <c e>4 <d f> <e g> <f a> | }
}
structure { |: A :| }
render score ""test.svg"" { staff treble { melody } }
";
        var compiler = new SemanticCompiler();

        var result = compiler.Compile(source);

        Assert.True(result.Success,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.Score);
        Assert.True(result.Score!.Voices.Length >= 1);
    }

    [Fact]
    public void Compile_TremoloNote_HasCorrectBeams()
    {
        var source = "c4:8 d4:16 e4:32 |";
        var compiler = new SemanticCompiler();

        var result = compiler.Compile(source);

        Assert.True(result.Success);
        Assert.NotNull(result.Score);
        var measure = result.Score!.Voice.Measures[0];
        Assert.Equal(3, measure.Items.Length);

        var note1 = measure.Items[0] as LilySharp.Core.Svg.Model.NoteItem;
        var note2 = measure.Items[1] as LilySharp.Core.Svg.Model.NoteItem;
        var note3 = measure.Items[2] as LilySharp.Core.Svg.Model.NoteItem;

        Assert.NotNull(note1);
        Assert.NotNull(note2);
        Assert.NotNull(note3);
        Assert.Equal(1, note1!.TremoloBeams);  // :8 = 1 beam
        Assert.Equal(2, note2!.TremoloBeams);  // :16 = 2 beams
        Assert.Equal(3, note3!.TremoloBeams);  // :32 = 3 beams
    }

    [Fact]
    public void Compile_TremoloChord_HasCorrectBeams()
    {
        var source = "<c e g>4:16 |";
        var compiler = new SemanticCompiler();

        var result = compiler.Compile(source);

        Assert.True(result.Success);
        Assert.NotNull(result.Score);
        var measure = result.Score!.Voice.Measures[0];
        Assert.Single(measure.Items);

        var chord = measure.Items[0] as LilySharp.Core.Svg.Model.ChordItem;
        Assert.NotNull(chord);
        Assert.Equal(2, chord!.TremoloBeams);  // :16 = 2 beams
    }
}
