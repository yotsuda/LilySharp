using System.Collections.Immutable;
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Tests;

/// <summary>
/// Tests for multi-voice rendering functionality.
/// </summary>
public class MultiVoiceRenderingTests
{
    [Fact]
    public void Score_SingleVoice_BackwardCompatible()
    {
        var note = new NoteItem(4, Fraction.Quarter, 0, null, false, 0);
        var measure = new Measure(
            ImmutableArray.Create<MusicItem>(note),
            BarlineType.None, BarlineType.Single, null, 0, 10);
        var voice = new Voice("test", ImmutableArray.Create(measure));
        
        var score = new Score(voice, new TimeSignature(4, 4), new KeySignature(0), "treble");
        
        // Should have one voice
        Assert.Single(score.Voices);
        Assert.Same(voice, score.Voice);
        Assert.False(score.IsMultiVoice);
    }
    
    [Fact]
    public void Score_MultipleVoices_Supported()
    {
        var note1 = new NoteItem(8, Fraction.Quarter, 0, null, false, 0);  // High note
        var note2 = new NoteItem(0, Fraction.Quarter, 0, null, false, 10); // Low note
        
        var measure1 = new Measure(
            ImmutableArray.Create<MusicItem>(note1),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        var measure2 = new Measure(
            ImmutableArray.Create<MusicItem>(note2),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        
        var voice1 = new Voice("upper", ImmutableArray.Create(measure1));
        var voice2 = new Voice("lower", ImmutableArray.Create(measure2));
        
        var score = new Score(
            ImmutableArray.Create(voice1, voice2),
            new TimeSignature(4, 4),
            new KeySignature(0),
            "treble");
        
        Assert.Equal(2, score.Voices.Length);
        Assert.Same(voice1, score.Voice);  // First voice is primary
        Assert.True(score.IsMultiVoice);
    }
    
    [Fact]
    public void VoiceCollector_MultiVoice_AppliesStemDirections()
    {
        var note1 = new NoteItem(8, Fraction.Quarter, 0, null, false, 0);
        var note2 = new NoteItem(0, Fraction.Quarter, 0, null, false, 10);
        
        var measure1 = new Measure(
            ImmutableArray.Create<MusicItem>(note1),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        var measure2 = new Measure(
            ImmutableArray.Create<MusicItem>(note2),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        
        var voice1 = new Voice("upper", ImmutableArray.Create(measure1));
        var voice2 = new Voice("lower", ImmutableArray.Create(measure2));
        
        var collector = new VoiceCollector();
        var columns = collector.Collect(ImmutableArray.Create(voice1, voice2), new TimeSignature(4, 4));
        
        Assert.Single(columns);
        Assert.Equal(2, columns[0].Entries.Length);
        
        // Voice 1 stems up, Voice 2 stems down
        Assert.True(columns[0].Entries[0].ForcedStemUp);
        Assert.False(columns[0].Entries[1].ForcedStemUp);
    }
    
    [Fact]
    public void NoteCollision_NoOverlap_NoShift()
    {
        var collision = new NoteCollision();
        
        // Notes far apart (staff positions 0 and 8)
        var upPositions = new[] { 8 };
        var downPositions = new[] { 0 };
        
        var result = collision.AnalyzeCollision(upPositions, downPositions, 4, 4, 0, 0);
        
        Assert.Equal(CollisionType.None, result.Type);
        Assert.Equal(0, result.UpStemXOffset);
        Assert.Equal(0, result.DownStemXOffset);
    }
    
    [Fact]
    public void NoteCollision_Adjacent_Shifts()
    {
        var collision = new NoteCollision();
        
        // Adjacent notes (staff positions 4 and 5)
        var upPositions = new[] { 5 };
        var downPositions = new[] { 4 };
        
        var result = collision.AnalyzeCollision(upPositions, downPositions, 4, 4, 0, 0);
        
        // Should detect close collision and shift
        Assert.NotEqual(CollisionType.None, result.Type);
    }
    
    [Fact]
    public void StemDirection_VoiceOverride()
    {
        // Middle line note - normally would have stem down
        int middleLinePosition = 4;
        
        // Without voice number - auto direction
        Assert.False(StemDirection.GetStemUp(middleLinePosition, null));
        
        // With voice 1 - always up
        Assert.True(StemDirection.GetStemUp(middleLinePosition, 1));
        
        // With voice 2 - always down
        Assert.False(StemDirection.GetStemUp(middleLinePosition, 2));
    }
}