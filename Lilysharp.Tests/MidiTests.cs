using Lilysharp.Core.Midi;
using Lilysharp.Core.Syntax;
using Xunit;

namespace Lilysharp.Tests;

public class MidiTests
{
    [Fact]
    public void ExportSimpleNote()
    {
        var source = "c4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        Assert.Equal(2, midi.Tracks.Count); // conductor + main
        Assert.Single(midi.Tracks[1].Notes);
        Assert.Equal(60, midi.Tracks[1].Notes[0].Pitch); // C4 = MIDI 60
    }
    
    [Fact]
    public void ExportRelativeMode()
    {
        var source = "relative c' { c d e f g a b c' }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        Assert.Equal(8, midi.Tracks[1].Notes.Count);
        Assert.Equal(72, midi.Tracks[1].Notes[0].Pitch); // c = C5 (relative to c')
        Assert.Equal(74, midi.Tracks[1].Notes[1].Pitch); // d = D5
        Assert.Equal(76, midi.Tracks[1].Notes[2].Pitch); // e = E5
    }
    
    [Fact]
    public void ExportWithDuration()
    {
        var source = "c4 c2 c1";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        Assert.Equal(3, midi.Tracks[1].Notes.Count);
        // Quarter = 480 ticks, Half = 960, Whole = 1920
        Assert.Equal(480, midi.Tracks[1].Notes[0].DurationTicks);
        Assert.Equal(960, midi.Tracks[1].Notes[1].DurationTicks);
        Assert.Equal(1920, midi.Tracks[1].Notes[2].DurationTicks);
    }
    
    [Fact]
    public void ExportWithRest()
    {
        var source = "c4 r4 d4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        Assert.Equal(2, midi.Tracks[1].Notes.Count);
        // First note at tick 0, second at tick 960 (after rest)
        Assert.Equal(0, midi.Tracks[1].Notes[0].StartTick);
        Assert.Equal(960, midi.Tracks[1].Notes[1].StartTick);
    }
    
    [Fact]
    public void ExportChord()
    {
        var source = "< c e g >4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        Assert.Equal(3, midi.Tracks[1].Notes.Count);
        // All start at same tick
        Assert.True(midi.Tracks[1].Notes.All(n => n.StartTick == 0));
    }
    
    [Fact]
    public void ExportAccidentals()
    {
        var source = "c cis des";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        Assert.Equal(3, midi.Tracks[1].Notes.Count);
        Assert.Equal(60, midi.Tracks[1].Notes[0].Pitch);  // C
        Assert.Equal(61, midi.Tracks[1].Notes[1].Pitch);  // C#
        Assert.Equal(61, midi.Tracks[1].Notes[2].Pitch);  // Db
    }
    
    [Fact]
    public void WriteValidMidiFile()
    {
        var source = "relative c' { c4 d e f | g a b c' }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        using var stream = new MemoryStream();
        midi.WriteTo(stream);
        
        // Check MIDI header
        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 14);
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'T', bytes[1]);
        Assert.Equal((byte)'h', bytes[2]);
        Assert.Equal((byte)'d', bytes[3]);
    }
    
    [Fact]
    public void ExportSampleFile()
    {
        var source = File.ReadAllText("../../../../samples/happy-birthday.lys");
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors);
        
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        Assert.True(midi.Tracks[1].Notes.Count > 0);
    }

    [Fact]
    public void ExportTupletTriplet()
    {
        // Triplet: 3 notes in the time of 2 quarter notes
        var source = "tuplet 3/2 { c4 d4 e4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Equal(3, notes.Count);
        
        // Each quarter note in triplet should be 2/3 of normal duration
        // Normal quarter = 480 ticks, triplet quarter = 480 * 2/3 = 320 ticks
        Assert.Equal(320, notes[0].DurationTicks);
        Assert.Equal(320, notes[1].DurationTicks);
        Assert.Equal(320, notes[2].DurationTicks);
        
        // Total duration should equal 2 quarter notes = 960 ticks
        Assert.Equal(0, notes[0].StartTick);
        Assert.Equal(320, notes[1].StartTick);
        Assert.Equal(640, notes[2].StartTick);
    }

    [Fact]
    public void ExportGraceNotes()
    {
        var source = "grace { c8 d } e4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var notes = midi.Tracks.Skip(1).First().Notes;
        
        // 2 grace notes + 1 main note = 3 notes total
        Assert.Equal(3, notes.Count);
        
        // Grace notes should have short duration (1/32 = 60 ticks at 480 PPQ)
        Assert.Equal(60, notes[0].DurationTicks);
        Assert.Equal(60, notes[1].DurationTicks);
    }

    [Fact]
    public void ExportWithDynamics()
    {
        var source = @"c4\p d4\f e4\ff";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Equal(3, notes.Count);
        
        // p = 50, f = 95, ff = 110
        Assert.Equal(50, notes[0].Velocity);
        Assert.Equal(95, notes[1].Velocity);
        Assert.Equal(110, notes[2].Velocity);
    }

    [Fact]
    public void ExportWithStaccato()
    {
        var source = @"c4@staccato";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Single(notes);
        
        // Staccato = 50% duration, quarter note = 480 ticks, so 240 ticks
        Assert.Equal(240, notes[0].DurationTicks);
    }

    [Fact]
    public void ExportWithAccent()
    {
        var source = @"c4@accent";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Single(notes);
        
        // Accent adds 20 to velocity (default 80 -> 100)
        Assert.Equal(100, notes[0].Velocity);
    }

    [Fact]
    public void DynamicsParsing()
    {
        var source = @"c4\p d4\f";
        var tree = SyntaxTree.Parse(source);
        
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        
        var notes = tree.GetNodes<NoteSyntax>().ToList();
        Assert.Equal(2, notes.Count);
        
        var articulations = notes[0].Articulations.ToList();
        Assert.Single(articulations);
        Assert.IsType<DynamicSyntax>(articulations[0]);
    }

    [Fact]
    public void ExportWithLyrics()
    {
        var source = @"
{ c4 d e f }
lyrics { Hap -- py birth -- day }
";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var track = midi.Tracks.Skip(1).First();
        Assert.Equal(4, track.Notes.Count);
        
        // Lyrics should be added (may not sync perfectly with notes in this simple implementation)
        Assert.True(track.Lyrics.Count > 0);
    }

    [Fact]
    public void ExportTimeSignature()
    {
        var source = "time 3/4 c4 d e";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var conductorTrack = midi.Tracks[0];
        Assert.Contains(conductorTrack.TimeSignatures, ts => ts.Numerator == 3 && ts.Denominator == 4);
    }

    [Fact]
    public void ExportTempoDeclaration()
    {
        var source = "tempo 140 c4 d e";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        
        var conductorTrack = midi.Tracks[0];
        // 140 BPM = 428571 microseconds per beat
        Assert.Contains(conductorTrack.TempoChanges, tc => tc.MicrosecondsPerBeat == 428571);
    }
}