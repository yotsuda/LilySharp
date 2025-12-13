using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Semantics.Binding;
using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

public class BinderTests
{
    [Fact]
    public void Bind_SimpleNotes_ProducesBoundScore()
    {
        var source = "c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();
        
        var result = binder.Bind(tree, symbols);
        
        Assert.True(result.Success);
        Assert.Single(result.Voices);
        Assert.Single(result.PrimaryVoice.Measures);
        Assert.Equal(4, result.PrimaryVoice.Measures[0].Items.Length);
    }
    
    [Fact]
    public void Bind_NotesWithDurations_ResolvesDurations()
    {
        var source = "c4 d8 e16 f2 |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();
        
        var result = binder.Bind(tree, symbols);
        var measure = result.PrimaryVoice.Measures[0];
        
        var note0 = (BoundNote)measure.Items[0];
        var note1 = (BoundNote)measure.Items[1];
        var note2 = (BoundNote)measure.Items[2];
        var note3 = (BoundNote)measure.Items[3];
        
        Assert.Equal(new Fraction(1, 4), note0.BaseDuration);
        Assert.Equal(new Fraction(1, 8), note1.BaseDuration);
        Assert.Equal(new Fraction(1, 16), note2.BaseDuration);
        Assert.Equal(new Fraction(1, 2), note3.BaseDuration);
    }
    
    [Fact]
    public void Bind_Rests_ProducesBoundRests()
    {
        var source = "c4 r4 d4 r8 |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();
        
        var result = binder.Bind(tree, symbols);
        var measure = result.PrimaryVoice.Measures[0];
        
        Assert.IsType<BoundNote>(measure.Items[0]);
        Assert.IsType<BoundRest>(measure.Items[1]);
        Assert.IsType<BoundNote>(measure.Items[2]);
        Assert.IsType<BoundRest>(measure.Items[3]);
    }
    
    [Fact]
    public void Bind_Chord_ProducesBoundChord()
    {
        var source = "<c e g>4 |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();
        
        var result = binder.Bind(tree, symbols);
        var measure = result.PrimaryVoice.Measures[0];
        
        Assert.Single(measure.Items);
        var chord = Assert.IsType<BoundChord>(measure.Items[0]);
        Assert.Equal(3, chord.Notes.Count);
    }
    
    [Fact]
    public void Bind_MultipleMeasures_CreatesSeparateMeasures()
    {
        var source = "c4 d e f | g a b c' |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();
        
        var result = binder.Bind(tree, symbols);
        
        Assert.Equal(2, result.PrimaryVoice.Measures.Length);
        Assert.Equal(4, result.PrimaryVoice.Measures[0].Items.Length);
        Assert.Equal(4, result.PrimaryVoice.Measures[1].Items.Length);
    }
    
    [Fact]
    public void Bind_ExtractsMetadata()
    {
        var source = @"
title ""Test Song""
composer ""Test Composer""
tempo 120
time 3/4
key g major
c4 d e |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();
        
        var result = binder.Bind(tree, symbols);
        
        Assert.Equal("Test Song", result.Metadata.Title);
        Assert.Equal("Test Composer", result.Metadata.Composer);
        Assert.Equal(120, result.Metadata.Tempo);
        Assert.Equal(3, result.Metadata.TimeSignature.Beats);
        Assert.Equal(4, result.Metadata.TimeSignature.BeatType);
        Assert.Equal(1, result.Metadata.KeySignature.Sharps); // G major = 1 sharp
    }
    
    [Fact]
    public void Bind_HappyBirthday_ProducesValidScore()
    {
        var source = File.ReadAllText(@"C:\MyProj\LilySharp\samples\happy-birthday.lys");
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();
        
        var result = binder.Bind(tree, symbols);
        
        // Structure expansion is tested separately
        // For now just verify no binding errors
        Assert.True(result.Success);
    }
}

public class RelativePitchResolverTests
{
    [Fact]
    public void Resolve_StepUp_StaysInSameOctave()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);
        
        var source = "d4";
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();
        
        var pitch = resolver.Resolve(note.Pitch);
        
        Assert.Equal(1, pitch.Step); // D
        Assert.Equal(4, pitch.Octave); // Same octave
    }
    
    [Fact]
    public void Resolve_LargeIntervalUp_GoesDownOctave()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);
        
        // Jump from C to A (more than a fourth up in pitch space)
        var source = "a4";
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();
        
        var pitch = resolver.Resolve(note.Pitch);
        
        Assert.Equal(5, pitch.Step); // A
        Assert.Equal(3, pitch.Octave); // Down an octave (closest A)
    }
    
    [Fact]
    public void Resolve_OctaveMarker_OverridesDefault()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);
        
        var source = "a'4"; // Explicit octave up
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();
        
        var pitch = resolver.Resolve(note.Pitch);
        
        Assert.Equal(5, pitch.Step); // A
        Assert.Equal(4, pitch.Octave); // Same octave due to '
    }
    
    [Fact]
    public void Resolve_Accidentals_Preserved()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);
        
        var source = "fis4"; // F sharp
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();
        
        var pitch = resolver.Resolve(note.Pitch);
        
        Assert.Equal(3, pitch.Step); // F
        Assert.Equal(1, pitch.Alteration); // Sharp
    }
}

public class PitchTests
{
    [Fact]
    public void MidiNote_MiddleC_Is60()
    {
        var pitch = Pitch.MiddleC;
        Assert.Equal(60, pitch.MidiNote);
    }
    
    [Fact]
    public void MidiNote_A440_Is69()
    {
        var pitch = new Pitch(5, 4, 0); // A4
        Assert.Equal(69, pitch.MidiNote);
    }
    
    [Fact]
    public void StaffPosition_MiddleC_IsZero()
    {
        var pitch = Pitch.MiddleC;
        Assert.Equal(0, pitch.StaffPosition);
    }
    
    [Fact]
    public void StaffPosition_G4_Is4()
    {
        var pitch = new Pitch(4, 4, 0); // G4
        Assert.Equal(4, pitch.StaffPosition);
    }
    
    [Fact]
    public void FromName_CreatesCorrectPitch()
    {
        var pitch = Pitch.FromName('G', 4, 1); // G#4
        
        Assert.Equal(4, pitch.Step);
        Assert.Equal(4, pitch.Octave);
        Assert.Equal(1, pitch.Alteration);
        Assert.Equal("#", pitch.AccidentalString);
    }
}
