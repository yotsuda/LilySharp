// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PartCombineTests
{
    private static readonly TimeSignature FourFour = new(4, 4);

    // Helper to create a NoteItem
    private static NoteItem Note(int staffPosition, Fraction duration) =>
        new(staffPosition, duration, 0, null, false, 0);

    // Helper to create a RestItem
    private static RestItem Rest(Fraction duration) =>
        new(duration, 0, 0);

    // Helper to create a Measure from items
    private static Measure MakeMeasure(params MusicItem[] items) =>
        new(ImmutableArray.Create(items), BarlineType.None, BarlineType.None, null, 0, 0);

    // Helper to create a Voice with measures
    private static Voice MakeVoice(string name, params Measure[] measures) =>
        new(name, ImmutableArray.Create(measures));

    // --- Analyzer: Classify Tests ---

    [Fact]
    public void Analyze_BothResting_ReturnsSilence()
    {
        var v1 = MakeVoice("1", MakeMeasure(Rest(Fraction.Quarter), Rest(Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Rest(Fraction.Quarter), Rest(Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Silence, result[0].State);
    }

    [Fact]
    public void Analyze_SameNotes_ReturnsUnison()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Unison, result[0].State);
    }

    [Fact]
    public void Analyze_DifferentNotes_ReturnsApart()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(4, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(-2, Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Apart, result[0].State);
    }

    [Fact]
    public void Analyze_Voice1Only_ReturnsSolo1()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Rest(Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Solo1, result[0].State);
    }

    [Fact]
    public void Analyze_Voice2Only_ReturnsSolo2()
    {
        var v1 = MakeVoice("1", MakeMeasure(Rest(Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Solo2, result[0].State);
    }

    [Fact]
    public void Analyze_StateChange_EmitsMultipleItems()
    {
        // First item: unison, second item: solo1
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Quarter), Rest(Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Equal(2, result.Length);
        Assert.Equal(PartCombineState.Unison, result[0].State);
        Assert.Equal(PartCombineState.Solo1, result[1].State);
    }

    [Fact]
    public void Analyze_NoStateChange_EmitsSingleItem()
    {
        // All items the same state (all unison)
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter), Note(0, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Quarter), Note(0, Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Unison, result[0].State);
    }

    [Fact]
    public void Analyze_DifferentDurations_NotUnison()
    {
        // Same pitch, different duration => apart
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Half)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Apart, result[0].State);
    }

    [Fact]
    public void Analyze_AcrossMeasures()
    {
        // Measure 0: unison, Measure 1: apart
        var v1 = MakeVoice("1",
            MakeMeasure(Note(0, Fraction.Quarter)),
            MakeMeasure(Note(4, Fraction.Quarter)));
        var v2 = MakeVoice("2",
            MakeMeasure(Note(0, Fraction.Quarter)),
            MakeMeasure(Note(-2, Fraction.Quarter)));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Equal(2, result.Length);
        Assert.Equal(PartCombineState.Unison, result[0].State);
        Assert.Equal(0, result[0].MeasureIndex);
        Assert.Equal(PartCombineState.Apart, result[1].State);
        Assert.Equal(1, result[1].MeasureIndex);
    }

    [Fact]
    public void Analyze_EmptyVoices_ReturnsEmpty()
    {
        var v1 = MakeVoice("1");
        var v2 = MakeVoice("2");

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.True(result.IsEmpty);
    }

    // --- Calculate Layout Tests ---

    [Fact]
    public void Calculate_Unison_ProducesA2Text()
    {
        var items = ImmutableArray.Create(
            new PartCombineItem(PartCombineState.Unison, 0, 0));
        var measureLayouts = ImmutableArray.Create(
            new MeasureLayout(0, 10.0, 20.0,
                ImmutableArray.Create(new ItemLayout(0, 2.0, 4.0))));

        var layouts = PartCombineAnalyzer.Calculate(items, measureLayouts);

        Assert.Single(layouts);
        Assert.Equal("a2", layouts[0].Text);
        Assert.Equal(12.0, layouts[0].X); // measure.X + item.X = 10 + 2
    }

    [Fact]
    public void Calculate_Solo1_ProducesSoloText()
    {
        var items = ImmutableArray.Create(
            new PartCombineItem(PartCombineState.Solo1, 0, 0));
        var measureLayouts = ImmutableArray.Create(
            new MeasureLayout(0, 5.0, 20.0,
                ImmutableArray.Create(new ItemLayout(0, 1.0, 4.0))));

        var layouts = PartCombineAnalyzer.Calculate(items, measureLayouts);

        Assert.Single(layouts);
        Assert.Equal("Solo", layouts[0].Text);
    }

    [Fact]
    public void Calculate_Solo2_ProducesSoloIIText()
    {
        var items = ImmutableArray.Create(
            new PartCombineItem(PartCombineState.Solo2, 0, 0));
        var measureLayouts = ImmutableArray.Create(
            new MeasureLayout(0, 5.0, 20.0,
                ImmutableArray.Create(new ItemLayout(0, 1.0, 4.0))));

        var layouts = PartCombineAnalyzer.Calculate(items, measureLayouts);

        Assert.Single(layouts);
        Assert.Equal("Solo II", layouts[0].Text);
    }

    [Fact]
    public void Calculate_Apart_ProducesNoText()
    {
        var items = ImmutableArray.Create(
            new PartCombineItem(PartCombineState.Apart, 0, 0));
        var measureLayouts = ImmutableArray.Create(
            new MeasureLayout(0, 5.0, 20.0,
                ImmutableArray.Create(new ItemLayout(0, 1.0, 4.0))));

        var layouts = PartCombineAnalyzer.Calculate(items, measureLayouts);

        Assert.True(layouts.IsEmpty);
    }

    [Fact]
    public void Calculate_Silence_ProducesNoText()
    {
        var items = ImmutableArray.Create(
            new PartCombineItem(PartCombineState.Silence, 0, 0));
        var measureLayouts = ImmutableArray.Create(
            new MeasureLayout(0, 5.0, 20.0,
                ImmutableArray.Create(new ItemLayout(0, 1.0, 4.0))));

        var layouts = PartCombineAnalyzer.Calculate(items, measureLayouts);

        Assert.True(layouts.IsEmpty);
    }

    [Fact]
    public void Calculate_EmptyItems_ReturnsEmpty()
    {
        var layouts = PartCombineAnalyzer.Calculate(
            ImmutableArray<PartCombineItem>.Empty,
            ImmutableArray<MeasureLayout>.Empty);

        Assert.True(layouts.IsEmpty);
    }

    [Fact]
    public void Calculate_DefaultItems_ReturnsEmpty()
    {
        var layouts = PartCombineAnalyzer.Calculate(
            default,
            ImmutableArray<MeasureLayout>.Empty);

        Assert.True(layouts.IsEmpty);
    }

    // --- Chord Unison Tests ---

    [Fact]
    public void Analyze_ChordUnison_SamePitches()
    {
        var chord1 = new ChordItem(
            ImmutableArray.Create(new ChordNoteInfo(0, null, false), new ChordNoteInfo(4, null, false)),
            Fraction.Quarter, 0, 0, 0);
        var chord2 = new ChordItem(
            ImmutableArray.Create(new ChordNoteInfo(0, null, false), new ChordNoteInfo(4, null, false)),
            Fraction.Quarter, 0, 0, 0);

        var v1 = MakeVoice("1", MakeMeasure(chord1));
        var v2 = MakeVoice("2", MakeMeasure(chord2));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Unison, result[0].State);
    }

    [Fact]
    public void Analyze_ChordDifferentPitches_ReturnsApart()
    {
        var chord1 = new ChordItem(
            ImmutableArray.Create(new ChordNoteInfo(0, null, false), new ChordNoteInfo(4, null, false)),
            Fraction.Quarter, 0, 0, 0);
        var chord2 = new ChordItem(
            ImmutableArray.Create(new ChordNoteInfo(0, null, false), new ChordNoteInfo(2, null, false)),
            Fraction.Quarter, 0, 0, 0);

        var v1 = MakeVoice("1", MakeMeasure(chord1));
        var v2 = MakeVoice("2", MakeMeasure(chord2));

        var result = PartCombineAnalyzer.Analyze(v1, v2);

        Assert.Single(result);
        Assert.Equal(PartCombineState.Apart, result[0].State);
    }
}
