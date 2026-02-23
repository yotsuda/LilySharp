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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class HairpinTests
{
    // --- HairpinLayout constants ---

    [Fact]
    public void HairpinLayout_HasExpectedLilyPondDefaults()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1655 (height . 0.6666)
        // Opening = Height / 2 = 0.6666 / 2 = 0.3333
        var layout = new HairpinLayout(0, 0, 10, 5.2, 0.3333, HairpinDirection.Crescendo, 0);
        Assert.Equal(0.3333, layout.Opening);
        Assert.Equal(5.2, layout.Y);
    }

    // --- DetectHairpins ---

    [Fact]
    public void DetectHairpins_NoCrescMarks_ReturnsEmpty()
    {
        var musicMarks = ImmutableArray<MusicMarkItem>.Empty;
        var dynamics = ImmutableArray<DynamicItem>.Empty;

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectHairpins_CrescFollowedByDynamic_CreatesHairpin()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Cresc, 0, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.F, 2, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Crescendo, result[0].Direction);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(2, result[0].EndMeasureIndex);
        Assert.Equal(0, result[0].EndItemIndex);
    }

    [Fact]
    public void DetectHairpins_DecrescFollowedByDynamic_CreatesDecrescendo()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Decresc, 1, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.P, 3, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Decrescendo, result[0].Direction);
        Assert.Equal(1, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex);
    }

    [Fact]
    public void DetectHairpins_DimTreatedAsDecrescendo()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Dim, 0, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.PP, 1, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Decrescendo, result[0].Direction);
    }

    [Fact]
    public void DetectHairpins_NoDynamic_ExtendsToNextMeasure()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Cresc, 2, 0));
        var dynamics = ImmutableArray<DynamicItem>.Empty;

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Single(result);
        Assert.Equal(2, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex);
        Assert.Equal(0, result[0].EndItemIndex);
    }

    [Fact]
    public void DetectHairpins_TwoCrescMarks_CreatesTwoHairpins()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Cresc, 0, 0),
            new MusicMarkItem(MusicMarkType.Decresc, 2, 0));
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.FF, 4, 0, 0));

        var result = HairpinEngraver.DetectHairpins(musicMarks, dynamics);

        Assert.Equal(2, result.Length);
        Assert.Equal(HairpinDirection.Crescendo, result[0].Direction);
        Assert.Equal(HairpinDirection.Decrescendo, result[1].Direction);
    }

    // --- HairpinEngraver.Calculate ---

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
    public void Calculate_Crescendo_PositionsWedge()
    {
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 0, 42));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        var h = result[0];
        Assert.Equal(HairpinDirection.Crescendo, h.Direction);
        Assert.Equal(0, h.StartMeasureIndex);
        Assert.Equal(42, h.SourcePosition);
        // StartX: measure[0].X + item[0].X + item[0].Width + BoundPadding*0.5
        // = 0 + 1.0 + 2.0 + 0.5 = 3.5
        Assert.Equal(3.5, h.StartX, 2);
        // EndX: measure[2].X + item[0].X - BoundPadding*0.5
        // = 40 + 1.0 - 0.5 = 40.5
        Assert.Equal(40.5, h.EndX, 2);
    }

    [Fact]
    public void Calculate_Decrescendo_PositionsWedge()
    {
        var measures = CreateMeasureLayouts(3);
        var systems = CreateSingleSystem(3);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Decrescendo, 0, 1, 1, 2, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        Assert.Equal(HairpinDirection.Decrescendo, result[0].Direction);
    }

    [Fact]
    public void Calculate_MinimumLength_Enforced()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1659 (minimum-length . 2.0)
        // Create hairpin within same measure with very close start/end
        var items = ImmutableArray.Create(
            new ItemLayout(0, 1.0, 0.5),
            new ItemLayout(1, 2.0, 0.5));
        var measures = ImmutableArray.Create(
            new MeasureLayout(0, 0, 10, items));
        var systems = ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, measures));
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 0, 1, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        double length = result[0].EndX - result[0].StartX;
        Assert.True(length >= 2.0, $"Hairpin length {length:F2} should be >= 2.0 (minimum-length)");
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 10, 0, 0)); // endMeasure=10 is out of range

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_EmptyHairpins_ReturnsEmpty()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);

        var result = HairpinEngraver.Calculate(ImmutableArray<HairpinItem>.Empty, systems, measures);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_Opening_MatchesLilyPondHeight()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1655 (height . 0.6666)
        // Opening = height / 2
        var measures = CreateMeasureLayouts(3);
        var systems = CreateSingleSystem(3);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 0, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Single(result);
        Assert.Equal(0.6666 / 2.0, result[0].Opening, 4);
    }

    [Fact]
    public void Calculate_Y_AtDynamicLevel()
    {
        // Hairpins Y should be at the same level as dynamics (BaseY = 5.2)
        var measures = CreateMeasureLayouts(3);
        var systems = CreateSingleSystem(3);
        var hairpins = ImmutableArray.Create(new HairpinItem(
            HairpinDirection.Crescendo, 0, 0, 2, 0, 0));

        var result = HairpinEngraver.Calculate(hairpins, systems, measures);

        Assert.Equal(5.2, result[0].Y);
    }

    // --- HairpinDirection enum ---

    [Fact]
    public void HairpinDirection_HasCrescendoAndDecrescendo()
    {
        Assert.Equal(0, (int)HairpinDirection.Crescendo);
        Assert.Equal(1, (int)HairpinDirection.Decrescendo);
    }
}
