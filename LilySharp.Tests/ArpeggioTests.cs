using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ArpeggioTests
{
    // --- ChordItem.HasArpeggio ---

    [Fact]
    public void ChordItem_HasArpeggio_DefaultFalse()
    {
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, null, false),
            new ChordNoteInfo(4, null, false));
        var chord = new ChordItem(notes, Fraction.Quarter, 0, 0);
        Assert.False(chord.HasArpeggio);
    }

    [Fact]
    public void ChordItem_HasArpeggio_True()
    {
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, null, false),
            new ChordNoteInfo(4, null, false));
        var chord = new ChordItem(notes, Fraction.Quarter, 0, 0, hasArpeggio: true);
        Assert.True(chord.HasArpeggio);
    }

    // --- ArpeggioItem ---

    [Fact]
    public void ArpeggioItem_StoresValues()
    {
        var item = new ArpeggioItem(
            MeasureIndex: 2,
            ItemIndex: 1,
            MinStaffPosition: -2,
            MaxStaffPosition: 6,
            SourcePosition: 42);

        Assert.Equal(2, item.MeasureIndex);
        Assert.Equal(1, item.ItemIndex);
        Assert.Equal(-2, item.MinStaffPosition);
        Assert.Equal(6, item.MaxStaffPosition);
        Assert.Equal(42, item.SourcePosition);
    }

    // --- Score.Arpeggios ---

    [Fact]
    public void Score_Arpeggios_DefaultEmpty()
    {
        var measures = ImmutableArray.Create(
            new Measure(ImmutableArray<MusicItem>.Empty, BarlineType.None, BarlineType.None, null, 0, 0));
        var score = new Score(
            new Voice("default", measures),
            new TimeSignature(4, 4),
            new KeySignature(0),
            "treble");
        Assert.True(score.Arpeggios.IsEmpty);
    }

    [Fact]
    public void Score_Arpeggios_PassedCorrectly()
    {
        var measures = ImmutableArray.Create(
            new Measure(ImmutableArray<MusicItem>.Empty, BarlineType.None, BarlineType.None, null, 0, 0));
        var arps = ImmutableArray.Create(new ArpeggioItem(0, 0, -2, 4, 10));
        var score = new Score(
            new Voice("default", measures),
            new TimeSignature(4, 4),
            new KeySignature(0),
            "treble",
            arpeggios: arps);
        Assert.Single(score.Arpeggios);
        Assert.Equal(10, score.Arpeggios[0].SourcePosition);
    }

    // --- ArpeggioEngraver.Calculate ---

    private static ImmutableArray<MeasureLayout> CreateMeasureLayouts(int count, double measureWidth = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0),
                new ItemLayout(2, 9.0, 2.0));
            builder.Add(new MeasureLayout(i, i * measureWidth, measureWidth, items));
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<SystemLayout> CreateSingleSystem(int measureCount)
    {
        var measures = CreateMeasureLayouts(measureCount);
        return ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, measures));
    }

    [Fact]
    public void Calculate_BasicArpeggio_ReturnsLayout()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var arpeggios = ImmutableArray.Create(new ArpeggioItem(
            MeasureIndex: 0, ItemIndex: 0, MinStaffPosition: -2, MaxStaffPosition: 4, SourcePosition: 42));

        var result = ArpeggioEngraver.Calculate(arpeggios, systems, ml, 4.0);

        Assert.Single(result);
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void Calculate_ArpeggioX_LeftOfChord()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        // Chord at item 1, X=5.0 in measure 0
        var arpeggios = ImmutableArray.Create(new ArpeggioItem(
            MeasureIndex: 0, ItemIndex: 1, MinStaffPosition: -2, MaxStaffPosition: 4, SourcePosition: 0));

        var result = ArpeggioEngraver.Calculate(arpeggios, systems, ml, 4.0);

        Assert.Single(result);
        // X should be to the left of the chord (item X=5.0 - padding)
        Assert.True(result[0].X < 5.0, $"Arpeggio X ({result[0].X:F2}) should be left of chord X (5.0)");
    }

    [Fact]
    public void Calculate_TopY_LessThan_BottomY()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var arpeggios = ImmutableArray.Create(new ArpeggioItem(
            MeasureIndex: 0, ItemIndex: 0, MinStaffPosition: -4, MaxStaffPosition: 4, SourcePosition: 0));

        var result = ArpeggioEngraver.Calculate(arpeggios, systems, ml, 4.0);

        Assert.Single(result);
        Assert.True(result[0].TopY < result[0].BottomY,
            $"TopY ({result[0].TopY:F2}) should be less than BottomY ({result[0].BottomY:F2})");
    }

    [Fact]
    public void Calculate_SpansNoteRange()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        // Chord spanning from staff position -4 to 4 (4 staff spaces)
        var arpeggios = ImmutableArray.Create(new ArpeggioItem(
            MeasureIndex: 0, ItemIndex: 0, MinStaffPosition: -4, MaxStaffPosition: 4, SourcePosition: 0));

        var result = ArpeggioEngraver.Calculate(arpeggios, systems, ml, 4.0);

        Assert.Single(result);
        double height = result[0].BottomY - result[0].TopY;
        // Note range is 8 staff positions = 4 staff spaces + 0.6 extension
        Assert.True(height > 4.0, $"Height ({height:F2}) should span at least the note range");
    }

    [Fact]
    public void Calculate_EmptyArpeggios_ReturnsEmpty()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();

        var result = ArpeggioEngraver.Calculate(
            ImmutableArray<ArpeggioItem>.Empty, systems, ml, 4.0);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var arpeggios = ImmutableArray.Create(new ArpeggioItem(
            MeasureIndex: 10, ItemIndex: 0, MinStaffPosition: 0, MaxStaffPosition: 4, SourcePosition: 0));

        var result = ArpeggioEngraver.Calculate(arpeggios, systems, ml, 4.0);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_MultipleArpeggios_AllReturned()
    {
        var systems = CreateSingleSystem(3);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var arpeggios = ImmutableArray.Create(
            new ArpeggioItem(0, 0, -2, 4, 10),
            new ArpeggioItem(1, 1, 0, 6, 20));

        var result = ArpeggioEngraver.Calculate(arpeggios, systems, ml, 4.0);

        Assert.Equal(2, result.Length);
        Assert.Equal(10, result[0].SourcePosition);
        Assert.Equal(20, result[1].SourcePosition);
    }
}
