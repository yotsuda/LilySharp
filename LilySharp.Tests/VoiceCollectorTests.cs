using System.Collections.Immutable;
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Tests;

public class VoiceCollectorTests
{
    [Fact]
    public void SingleVoice_SingleNote_ProducesOneColumn()
    {
        var note = new NoteItem(
            staffPosition: 4,
            baseDuration: Fraction.Quarter,
            dots: 0,
            accidental: null,
            needsLedgerLines: false,
            sourcePosition: 0);
        
        var measure = new Measure(
            ImmutableArray.Create<MusicItem>(note),
            BarlineType.None,
            BarlineType.Single,
            sectionLabel: null,
            sourceStart: 0,
            sourceEnd: 10);
        
        var voice = new Voice("test", ImmutableArray.Create(measure));
        var score = new Score(voice, new TimeSignature(4, 4), new KeySignature(0), "treble");
        
        var collector = new VoiceCollector();
        var columns = collector.Collect(score);
        
        Assert.Single(columns);
        Assert.Single(columns[0].Entries);
        Assert.Equal(1, columns[0].Entries[0].VoiceId);
        Assert.Same(note, columns[0].Entries[0].Item);
    }
    
    [Fact]
    public void SingleVoice_Rest_IsSkipped()
    {
        var rest = new RestItem(Fraction.Quarter, dots: 0, sourcePosition: 0);
        
        var measure = new Measure(
            ImmutableArray.Create<MusicItem>(rest),
            BarlineType.None,
            BarlineType.Single,
            sectionLabel: null,
            sourceStart: 0,
            sourceEnd: 10);
        
        var voice = new Voice("test", ImmutableArray.Create(measure));
        var score = new Score(voice, new TimeSignature(4, 4), new KeySignature(0), "treble");
        
        var collector = new VoiceCollector();
        var columns = collector.Collect(score);
        
        Assert.Empty(columns);
    }
    
    [Fact]
    public void SingleVoice_MultipleMeasures_TracksIndices()
    {
        var note1 = new NoteItem(4, Fraction.Quarter, 0, null, false, 0);
        var note2 = new NoteItem(5, Fraction.Quarter, 0, null, false, 10);
        
        var measure1 = new Measure(
            ImmutableArray.Create<MusicItem>(note1),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        var measure2 = new Measure(
            ImmutableArray.Create<MusicItem>(note2),
            BarlineType.None, BarlineType.Single, null, 5, 15);
        
        var voice = new Voice("test", ImmutableArray.Create(measure1, measure2));
        var score = new Score(voice, new TimeSignature(4, 4), new KeySignature(0), "treble");
        
        var collector = new VoiceCollector();
        var columns = collector.Collect(score);
        
        Assert.Equal(2, columns.Length);
        Assert.Equal(0, columns[0].MeasureIndex);
        Assert.Equal(1, columns[1].MeasureIndex);
    }
    
    [Fact]
    public void SingleVoice_Chord_ProducesOneColumn()
    {
        var chord = new ChordItem(
            ImmutableArray.Create(
                new ChordNoteInfo(0, null, false),
                new ChordNoteInfo(4, null, false),
                new ChordNoteInfo(7, null, false)),
            Fraction.Quarter,
            dots: 0,
            sourcePosition: 0);
        
        var measure = new Measure(
            ImmutableArray.Create<MusicItem>(chord),
            BarlineType.None, BarlineType.Single, null, 0, 10);
        
        var voice = new Voice("test", ImmutableArray.Create(measure));
        var score = new Score(voice, new TimeSignature(4, 4), new KeySignature(0), "treble");
        
        var collector = new VoiceCollector();
        var columns = collector.Collect(score);
        
        Assert.Single(columns);
        Assert.IsType<ChordItem>(columns[0].Entries[0].Item);
    }
    
    [Fact]
    public void MultipleVoices_SameTimePosition_GroupedInColumn()
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
        
        var collector = new VoiceCollector();
        var columns = collector.Collect(ImmutableArray.Create(voice1, voice2), new TimeSignature(4, 4));
        
        Assert.Single(columns);
        Assert.Equal(2, columns[0].Entries.Length);
        
        // Voice 1 should have stems up (default), Voice 2 should have stems down
        var entries = columns[0].Entries;
        Assert.Equal(1, entries[0].VoiceId);
        Assert.True(entries[0].ForcedStemUp);
        Assert.Equal(2, entries[1].VoiceId);
        Assert.False(entries[1].ForcedStemUp);
    }
    
    [Fact]
    public void MultipleVoices_DifferentTimePositions_SeparateColumns()
    {
        // Voice 1: quarter note at beat 1
        // Voice 2: two eighth notes at beats 1 and 2
        var quarterNote = new NoteItem(8, Fraction.Quarter, 0, null, false, 0);
        var eighthNote1 = new NoteItem(0, Fraction.Eighth, 0, null, false, 10);
        var eighthNote2 = new NoteItem(2, Fraction.Eighth, 0, null, false, 20);
        
        var measure1 = new Measure(
            ImmutableArray.Create<MusicItem>(quarterNote),
            BarlineType.None, BarlineType.Single, null, 0, 5);
        var measure2 = new Measure(
            ImmutableArray.Create<MusicItem>(eighthNote1, eighthNote2),
            BarlineType.None, BarlineType.Single, null, 0, 30);
        
        var voice1 = new Voice("upper", ImmutableArray.Create(measure1));
        var voice2 = new Voice("lower", ImmutableArray.Create(measure2));
        
        var collector = new VoiceCollector();
        var columns = collector.Collect(ImmutableArray.Create(voice1, voice2), new TimeSignature(4, 4));
        
        // Should have 2 columns:
        // - Time 0: quarter from voice1, eighth from voice2
        // - Time 1/8: eighth from voice2 only
        Assert.Equal(2, columns.Length);
        
        // First column has both voices
        Assert.Equal(2, columns[0].Entries.Length);
        
        // Second column has only voice 2
        Assert.Single(columns[1].Entries);
        Assert.Equal(2, columns[1].Entries[0].VoiceId);
    }
}