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
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class GlissandoTests
{
    // Helper to create a Measure with minimal required parameters
    private static Measure MakeMeasure(params MusicItem[] items) =>
        new(ImmutableArray.Create(items), BarlineType.None, BarlineType.None, null, 0, 0);

    // Helper to create a simple Score from measures
    private static Score MakeScore(params Measure[] measures) =>
        new(new Voice("default", ImmutableArray.Create(measures)),
            new TimeSignature(4, 4),
            new KeySignature(0),
            "treble");

    // --- GlissandoStyle enum ---

    [Fact]
    public void GlissandoStyle_HasExpectedValues()
    {
        Assert.Equal(0, (int)GlissandoStyle.Line);
        Assert.Equal(1, (int)GlissandoStyle.Zigzag);
    }

    // --- NoteItem.HasGlissando ---

    [Fact]
    public void NoteItem_HasGlissando_DefaultFalse()
    {
        var note = new NoteItem(0, Fraction.Quarter, 0, null, false, 0);
        Assert.False(note.HasGlissando);
    }

    [Fact]
    public void NoteItem_HasGlissando_True()
    {
        var note = new NoteItem(0, Fraction.Quarter, 0, null, false, 0, hasGlissando: true);
        Assert.True(note.HasGlissando);
    }

    // --- @glissando parsing through the collector ---

    [Theory]
    [InlineData("g4@glissando c e b |")]
    public void Collect_GlissandoArticulation_SetsHasGlissando(string source)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        var first = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);
        Assert.True(first.HasGlissando, "@glissando should set HasGlissando");

        var glissandos = new GlissandoDetector().DetectGlissandos(score);
        Assert.Single(glissandos);
    }

    // --- GlissandoDetector ---

    [Fact]
    public void DetectGlissandos_NoGlissando_ReturnsEmpty()
    {
        var score = MakeScore(MakeMeasure(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(2, Fraction.Quarter, 0, null, false, 10)));

        var detector = new GlissandoDetector();
        var result = detector.DetectGlissandos(score);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectGlissandos_SingleGlissando_DetectsCorrectly()
    {
        var score = MakeScore(MakeMeasure(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 42, hasGlissando: true),
            new NoteItem(4, Fraction.Quarter, 0, null, false, 50)));

        var detector = new GlissandoDetector();
        var result = detector.DetectGlissandos(score);

        Assert.Single(result);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(0, result[0].StartItemIndex);
        Assert.Equal(0, result[0].StartStaffPosition);
        Assert.Equal(0, result[0].EndMeasureIndex);
        Assert.Equal(1, result[0].EndItemIndex);
        Assert.Equal(4, result[0].EndStaffPosition);
        Assert.Equal(GlissandoStyle.Line, result[0].Style);
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void DetectGlissandos_CrossMeasure_DetectsCorrectly()
    {
        var score = MakeScore(
            MakeMeasure(
                new NoteItem(-2, Fraction.Quarter, 0, null, false, 10, hasGlissando: true)),
            MakeMeasure(
                new NoteItem(6, Fraction.Quarter, 0, null, false, 20)));

        var detector = new GlissandoDetector();
        var result = detector.DetectGlissandos(score);

        Assert.Single(result);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(1, result[0].EndMeasureIndex);
        Assert.Equal(-2, result[0].StartStaffPosition);
        Assert.Equal(6, result[0].EndStaffPosition);
    }

    [Fact]
    public void DetectGlissandos_NoNextNote_ReturnsEmpty()
    {
        var score = MakeScore(MakeMeasure(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0, hasGlissando: true)));

        var detector = new GlissandoDetector();
        var result = detector.DetectGlissandos(score);

        // No next note to connect to
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectGlissandos_SkipsRests_FindsNextNote()
    {
        var score = MakeScore(MakeMeasure(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0, hasGlissando: true),
            new RestItem(Fraction.Quarter, 0, 5),
            new NoteItem(4, Fraction.Quarter, 0, null, false, 10)));

        var detector = new GlissandoDetector();
        var result = detector.DetectGlissandos(score);

        Assert.Single(result);
        Assert.Equal(0, result[0].StartItemIndex);
        Assert.Equal(2, result[0].EndItemIndex); // skipped the rest
    }

    [Fact]
    public void DetectGlissandos_TwoConsecutive_TwoGlissandos()
    {
        var score = MakeScore(MakeMeasure(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0, hasGlissando: true),
            new NoteItem(4, Fraction.Quarter, 0, null, false, 10, hasGlissando: true),
            new NoteItem(8, Fraction.Quarter, 0, null, false, 20)));

        var detector = new GlissandoDetector();
        var result = detector.DetectGlissandos(score);

        Assert.Equal(2, result.Length);
        Assert.Equal(0, result[0].StartStaffPosition);
        Assert.Equal(4, result[0].EndStaffPosition);
        Assert.Equal(4, result[1].StartStaffPosition);
        Assert.Equal(8, result[1].EndStaffPosition);
    }

    // --- GlissandoEngraver.Calculate ---

    private static ImmutableArray<MeasureLayout> CreateMeasureLayouts(int count, double measureWidth = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0),
                new ItemLayout(2, 9.0, 2.0),
                new ItemLayout(3, 13.0, 2.0));
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
    public void Calculate_BasicGlissando_ReturnsLayout()
    {
        var systems = CreateSingleSystem(2);
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 0,
            StartItemIndex: 0,
            StartStaffPosition: 0,
            EndMeasureIndex: 0,
            EndItemIndex: 1,
            EndStaffPosition: 4,
            Style: GlissandoStyle.Line,
            SourcePosition: 42));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.Single(result);
        Assert.Equal(42, result[0].SourcePosition);
        Assert.Equal(GlissandoStyle.Line, result[0].Style);
    }

    [Fact]
    public void Calculate_StartX_LessThan_EndX()
    {
        var systems = CreateSingleSystem(2);
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 0,
            StartItemIndex: 0,
            StartStaffPosition: 0,
            EndMeasureIndex: 0,
            EndItemIndex: 2,
            EndStaffPosition: 4,
            Style: GlissandoStyle.Line,
            SourcePosition: 0));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.Single(result);
        Assert.True(result[0].StartX < result[0].EndX, "Start X should be less than End X");
    }

    [Fact]
    public void Calculate_AscendingGlissando_StartY_GreaterThan_EndY()
    {
        // Staff position 0 (middle) to 4 (above) = ascending
        // Y is inverted: higher staff position = lower Y value
        var systems = CreateSingleSystem(2);
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 0,
            StartItemIndex: 0,
            StartStaffPosition: 0,
            EndMeasureIndex: 0,
            EndItemIndex: 2,
            EndStaffPosition: 4,
            Style: GlissandoStyle.Line,
            SourcePosition: 0));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.Single(result);
        Assert.True(result[0].StartY > result[0].EndY,
            $"Ascending glissando: startY ({result[0].StartY:F2}) should be > endY ({result[0].EndY:F2})");
    }

    [Fact]
    public void Calculate_DescendingGlissando_StartY_LessThan_EndY()
    {
        // Staff position 4 (above) to 0 (middle) = descending
        var systems = CreateSingleSystem(2);
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 0,
            StartItemIndex: 0,
            StartStaffPosition: 4,
            EndMeasureIndex: 0,
            EndItemIndex: 2,
            EndStaffPosition: 0,
            Style: GlissandoStyle.Line,
            SourcePosition: 0));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.Single(result);
        Assert.True(result[0].StartY < result[0].EndY,
            $"Descending glissando: startY ({result[0].StartY:F2}) should be < endY ({result[0].EndY:F2})");
    }

    [Fact]
    public void Calculate_EmptyGlissandos_ReturnsEmpty()
    {
        var systems = CreateSingleSystem(2);

        var result = GlissandoEngraver.Calculate(
            ImmutableArray<GlissandoItem>.Empty, systems, 4.0);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var systems = CreateSingleSystem(2);
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 10,
            StartItemIndex: 0,
            StartStaffPosition: 0,
            EndMeasureIndex: 11,
            EndItemIndex: 0,
            EndStaffPosition: 4,
            Style: GlissandoStyle.Line,
            SourcePosition: 0));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_GapApplied_EndpointsShortened()
    {
        var systems = CreateSingleSystem(2);
        // Items at X=1.0 and X=9.0, so raw distance is 8.0 staff spaces
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 0,
            StartItemIndex: 0,
            StartStaffPosition: 0,
            EndMeasureIndex: 0,
            EndItemIndex: 2,
            EndStaffPosition: 0,
            Style: GlissandoStyle.Line,
            SourcePosition: 0));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.Single(result);
        // startX should be > raw startX (1.0 + padding), endX should be < raw endX (9.0 - padding)
        Assert.True(result[0].StartX > 1.0, "Gap/padding should shift start X forward");
        Assert.True(result[0].EndX < 9.0, "Gap/padding should shift end X backward");
    }

    [Fact]
    public void Calculate_GapApplied_AlongLineDirection_AdjustsY()
    {
        // LILYPOND-REF: lily/line-spanner.cc:457 — gap applied along line direction
        // For a steep ascending glissando, the gap should adjust Y as well as X
        var systems = CreateSingleSystem(2);
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 0,
            StartItemIndex: 0,
            StartStaffPosition: -4,   // Low note (below staff)
            EndMeasureIndex: 0,
            EndItemIndex: 2,
            EndStaffPosition: 8,      // High note (above staff) — ascending
            Style: GlissandoStyle.Line,
            SourcePosition: 0));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.Single(result);
        // System Y = 10.0, staffHeight = 4.0, staffMiddleY = 12.0
        // For ascending glissando: startY > endY (Y increases downward)
        // Gap should make startY decrease (move up slightly) and endY increase (move down slightly)
        double systemY = 10.0;
        double staffMiddleY = systemY + 4.0 / 2.0; // = 12.0
        double rawStartY = staffMiddleY - (-4) / 2.0; // = 14.0 (low note = high Y)
        double rawEndY = staffMiddleY - 8 / 2.0;       // = 8.0 (high note = low Y)

        // After gap along line direction, start Y should move toward end (decrease)
        Assert.True(result[0].StartY < rawStartY,
            $"Start Y ({result[0].StartY:F2}) should be < raw ({rawStartY:F2}) — gap moves along line");
        // After gap, end Y should move toward start (increase)
        Assert.True(result[0].EndY > rawEndY,
            $"End Y ({result[0].EndY:F2}) should be > raw ({rawEndY:F2}) — gap moves along line");
    }

    [Fact]
    public void Calculate_CrossMeasureGlissando_UsesCorrectMeasureX()
    {
        var systems = CreateSingleSystem(3);
        // Start in measure 0 item 1, end in measure 1 item 0
        var glissandos = ImmutableArray.Create(new GlissandoItem(
            StartMeasureIndex: 0,
            StartItemIndex: 1,
            StartStaffPosition: 0,
            EndMeasureIndex: 1,
            EndItemIndex: 0,
            EndStaffPosition: 4,
            Style: GlissandoStyle.Line,
            SourcePosition: 0));

        var result = GlissandoEngraver.Calculate(glissandos, systems, 4.0);

        Assert.Single(result);
        // Measure 0: X=0, item 1: X=5.0. Measure 1: X=20, item 0: X=1.0
        // So raw startX ~5.0, raw endX ~21.0
        Assert.True(result[0].StartX > 5.0, "Start should be in measure 0");
        Assert.True(result[0].EndX < 21.0, "End should be in measure 1");
        Assert.True(result[0].EndX > result[0].StartX);
    }
}
