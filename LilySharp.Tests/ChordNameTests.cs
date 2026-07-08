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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ChordNameTests
{
    // --- ParseChordName ---

    [Fact]
    public void ParseChordName_SimpleChord()
    {
        var result = ChordNameItem.ParseChordName("chord.C");
        Assert.NotNull(result);
        Assert.Equal("C", result);
    }

    [Fact]
    public void ParseChordName_MinorSeventh()
    {
        var result = ChordNameItem.ParseChordName("chord.Cm7");
        Assert.NotNull(result);
        Assert.Equal("Cm7", result);
    }

    [Fact]
    public void ParseChordName_FlatRoot()
    {
        var result = ChordNameItem.ParseChordName("chord.Bb7");
        Assert.NotNull(result);
        Assert.Equal("B\u266D7", result);  // B♭7
    }

    [Fact]
    public void ParseChordName_FlatRoot_Eb()
    {
        var result = ChordNameItem.ParseChordName("chord.Ebmaj7");
        Assert.NotNull(result);
        Assert.Equal("E\u266Dmaj7", result);  // E♭maj7
    }

    [Fact]
    public void ParseChordName_NaturalB()
    {
        // "B" alone should NOT be converted to flat
        var result = ChordNameItem.ParseChordName("chord.B");
        Assert.NotNull(result);
        Assert.Equal("B", result);
    }

    [Fact]
    public void ParseChordName_SuspendedFourth()
    {
        var result = ChordNameItem.ParseChordName("chord.Csus4");
        Assert.NotNull(result);
        Assert.Equal("Csus4", result);
    }

    [Fact]
    public void ParseChordName_Diminished()
    {
        var result = ChordNameItem.ParseChordName("chord.Cdim");
        Assert.NotNull(result);
        Assert.Equal("Cdim", result);
    }

    [Fact]
    public void ParseChordName_Augmented()
    {
        var result = ChordNameItem.ParseChordName("chord.Caug");
        Assert.NotNull(result);
        Assert.Equal("Caug", result);
    }

    [Fact]
    public void ParseChordName_MultiPartJoined()
    {
        // If tokenized as separate parts: @chord(Am 7) → MarkName "chord.Am.7"
        var result = ChordNameItem.ParseChordName("chord.Am.7");
        Assert.NotNull(result);
        Assert.Equal("Am7", result);  // Parts joined without dots
    }

    [Fact]
    public void ParseChordName_SharpRoot()
    {
        // @chord(C#m7) → MarkName "chord.C.#.m7" (the '#' lexes as its own token)
        var result = ChordNameItem.ParseChordName("chord.C.#.m7");
        Assert.NotNull(result);
        Assert.Equal("C♯m7", result);  // C♯m7
    }

    [Fact]
    public void ParseChordName_SharpTension()
    {
        var result = ChordNameItem.ParseChordName("chord.G7.#.9");
        Assert.NotNull(result);
        Assert.Equal("G7♯9", result);  // G7♯9
    }

    [Fact]
    public void ParseChordName_SharpRootWithFlatTension()
    {
        // F#m7b5 keeps the flat literal (as before) and sharps the root.
        var result = ChordNameItem.ParseChordName("chord.F.#.m7b5");
        Assert.NotNull(result);
        Assert.Equal("F♯m7b5", result);  // F♯m7b5
    }

    [Fact]
    public void ParseChordName_NotChord_ReturnsNull()
    {
        Assert.Null(ChordNameItem.ParseChordName("segno"));
        Assert.Null(ChordNameItem.ParseChordName("fig.6"));
        Assert.Null(ChordNameItem.ParseChordName("mark.A"));
    }

    [Fact]
    public void ParseChordName_Empty_ReturnsNull()
    {
        Assert.Null(ChordNameItem.ParseChordName("chord."));
    }

    // --- ChordNameEngraver ---

    [Fact]
    public void ChordNameEngraver_Calculate_EmptyInput()
    {
        var result = ChordNameEngraver.Calculate(
            ImmutableArray<ChordNameItem>.Empty,
            ImmutableArray<SystemLayout>.Empty,
            ImmutableArray<MeasureLayout>.Empty);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void ChordNameEngraver_Calculate_ProducesLayout()
    {
        var chordNames = ImmutableArray.Create(
            new ChordNameItem("Cm7", 0, 0, 0));

        var itemLayout = new ItemLayout(0, 2.0, 1.0);
        var measureLayout = new MeasureLayout(0, 5.0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0, ImmutableArray.Create(measureLayout));

        var result = ChordNameEngraver.Calculate(
            chordNames,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(measureLayout));

        Assert.Single(result);
        Assert.Equal(0, result[0].MeasureIndex);
        Assert.Equal(7.0, result[0].X, 1);  // measureX(5.0) + itemX(2.0)
        Assert.Equal("Cm7", result[0].ChordText);
    }

    [Fact]
    public void ChordNameEngraver_Calculate_YIsAboveStaff()
    {
        var chordNames = ImmutableArray.Create(
            new ChordNameItem("C", 0, 0, 0));

        var itemLayout = new ItemLayout(0, 0, 1.0);
        var measureLayout = new MeasureLayout(0, 0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0, ImmutableArray.Create(measureLayout));

        var result = ChordNameEngraver.Calculate(
            chordNames,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(measureLayout));

        Assert.Single(result);
        Assert.True(result[0].Y < 0, "Y should be negative (above staff)");
    }

    // --- MeasureCollector integration ---

    [Fact]
    public void Collector_ChordName_SingleChord()
    {
        var source = "c4 @chord(C) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        var cn = score.ChordNames[0];
        Assert.Equal(0, cn.MeasureIndex);
        Assert.Equal(0, cn.ItemIndex);
        Assert.Equal("C", cn.ChordText);
    }

    [Fact]
    public void Collector_ChordName_MinorSeventh()
    {
        var source = "c4 @chord(Cm7) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("Cm7", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void Collector_ChordName_FlatRoot()
    {
        var source = "c4 @chord(Bb7) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("B\u266D7", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void Collector_ChordName_SharpChord()
    {
        var source = "c4 @chord(C#m7) d e f";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("C♯m7", score.ChordNames[0].ChordText);  // C♯m7
    }

    [Fact]
    public void Collector_ChordName_SharpTension()
    {
        var source = "c4 @chord(G7#9) d e f";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("G7♯9", score.ChordNames[0].ChordText);  // G7♯9
    }

    [Fact]
    public void Collector_ChordName_MultipleChords()
    {
        var source = "c4 @chord(C) d @chord(Am) e @chord(F) f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(3, score.ChordNames.Length);
        Assert.Equal("C", score.ChordNames[0].ChordText);
        Assert.Equal("Am", score.ChordNames[1].ChordText);
        Assert.Equal("F", score.ChordNames[2].ChordText);
    }

    [Fact]
    public void Collector_ChordName_NoChords()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.ChordNames.IsEmpty);
    }

    [Fact]
    public void Collector_ChordName_WithFiguredBass_BothCollected()
    {
        // Chord name and figured bass on the same note
        var source = "c4 @chord(C) @fig(6) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Single(score.FiguredBasses);
        Assert.Equal("C", score.ChordNames[0].ChordText);
        Assert.Equal(6, score.FiguredBasses[0].Figures[0].Number);
    }
}
