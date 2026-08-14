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
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PedalTests
{
    // --- PedalType enum ---

    [Fact]
    public void PedalType_HasExpectedValues()
    {
        Assert.Equal(0, (int)PedalType.Sustain);
        Assert.Equal(1, (int)PedalType.Sostenuto);
        Assert.Equal(2, (int)PedalType.UnaCorda);
    }

    // --- MusicMarkType pedal entries ---

    // LilyPond's own names (ly/spanners-init.ly), one word each — case-insensitive
    // like every other annotation name.
    [Theory]
    [InlineData("sustainOn", MusicMarkType.SustainOn)]
    [InlineData("sustainOff", MusicMarkType.SustainOff)]
    [InlineData("sostenutoOn", MusicMarkType.SostenutoOn)]
    [InlineData("sostenutoOff", MusicMarkType.SostenutoOff)]
    [InlineData("unaCorda", MusicMarkType.UnaCordaOn)]
    [InlineData("treCorde", MusicMarkType.UnaCordaOff)]
    [InlineData("sustainon", MusicMarkType.SustainOn)]
    [InlineData("UNACORDA", MusicMarkType.UnaCordaOn)]
    public void ParseMarkName_PedalMarks(string name, MusicMarkType expected)
    {
        var result = MusicMarkItem.ParseMarkName(name);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }

    // ONE spelling per pedal (grammar audit B-5), and it is LilyPond's. The
    // short forms and the argument spellings are rejected, not silently mapped:
    // a pedal event carries only START/STOP, so there was never an argument to
    // put a state in ('@ped(off)'), and the noun-continuation spellings
    // ('@una(corda)') used the same parentheses for something else entirely.
    [Theory]
    [InlineData("ped")]
    [InlineData("ped.off")]
    [InlineData("sost")]
    [InlineData("sost.off")]
    [InlineData("una.corda")]
    [InlineData("tre.corde")]
    [InlineData("sustain")]
    [InlineData("sustain.off")]
    [InlineData("sostenuto")]
    [InlineData("sostenuto.off")]
    [InlineData("trillspan.start")]
    [InlineData("trillspan.stop")]
    public void ParseMarkName_RemovedSpellings_AreUnknown(string name)
    {
        Assert.Null(MusicMarkItem.ParseMarkName(name));
    }

    // --- MusicMarkItem text for pedals ---

    [Theory]
    [InlineData(MusicMarkType.SustainOn, "Ped.")]
    [InlineData(MusicMarkType.SustainOff, "*")]
    [InlineData(MusicMarkType.SostenutoOn, "Sost. Ped.")]
    [InlineData(MusicMarkType.SostenutoOff, "*")]
    [InlineData(MusicMarkType.UnaCordaOn, "una corda")]
    [InlineData(MusicMarkType.UnaCordaOff, "tre corde")]
    public void MusicMarkItem_PedalText(MusicMarkType type, string expectedText)
    {
        var item = new MusicMarkItem(type, 0, 0);
        Assert.Equal(expectedText, item.Text);
    }

    // --- Pedal marks position and vertical ---

    [Theory]
    [InlineData(MusicMarkType.SustainOn)]
    [InlineData(MusicMarkType.SustainOff)]
    [InlineData(MusicMarkType.SostenutoOn)]
    [InlineData(MusicMarkType.SostenutoOff)]
    [InlineData(MusicMarkType.UnaCordaOn)]
    [InlineData(MusicMarkType.UnaCordaOff)]
    public void MusicMarkItem_PedalPosition_Beginning(MusicMarkType type)
    {
        var item = new MusicMarkItem(type, 0, 0);
        Assert.Equal(MusicMarkPosition.Beginning, item.Position);
    }

    [Theory]
    [InlineData(MusicMarkType.SustainOn)]
    [InlineData(MusicMarkType.SustainOff)]
    [InlineData(MusicMarkType.SostenutoOn)]
    [InlineData(MusicMarkType.SostenutoOff)]
    [InlineData(MusicMarkType.UnaCordaOn)]
    [InlineData(MusicMarkType.UnaCordaOff)]
    public void MusicMarkItem_PedalVertical_Below(MusicMarkType type)
    {
        var item = new MusicMarkItem(type, 0, 0);
        Assert.Equal(MusicMarkVertical.Below, item.Vertical);
    }

    // --- PedalBracketItem ---

    [Fact]
    public void PedalBracketItem_StoresValues()
    {
        var item = new PedalBracketItem(PedalType.Sustain, 0, 3, 42);
        Assert.Equal(PedalType.Sustain, item.Type);
        Assert.Equal(0, item.StartMeasureIndex);
        Assert.Equal(3, item.EndMeasureIndex);
        Assert.Equal(42, item.SourcePosition);
    }

    // --- DetectPedalBrackets ---

    [Fact]
    public void DetectPedalBrackets_EmptyMarks_ReturnsEmpty()
    {
        var result = PedalEngraver.DetectPedalBrackets(ImmutableArray<MusicMarkItem>.Empty);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectPedalBrackets_SustainOnOff_CreatesBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10),
            new MusicMarkItem(MusicMarkType.SustainOff, 3, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Single(result);
        Assert.Equal(PedalType.Sustain, result[0].Type);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex);
        Assert.Equal(10, result[0].SourcePosition);
    }

    [Fact]
    public void DetectPedalBrackets_SostenutoOnOff_CreatesBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SostenutoOn, 1, 10),
            new MusicMarkItem(MusicMarkType.SostenutoOff, 4, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Single(result);
        Assert.Equal(PedalType.Sostenuto, result[0].Type);
        Assert.Equal(1, result[0].StartMeasureIndex);
        Assert.Equal(4, result[0].EndMeasureIndex);
    }

    [Fact]
    public void DetectPedalBrackets_UnaCordaOnOff_CreatesBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.UnaCordaOn, 2, 10),
            new MusicMarkItem(MusicMarkType.UnaCordaOff, 5, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Single(result);
        Assert.Equal(PedalType.UnaCorda, result[0].Type);
    }

    [Fact]
    public void DetectPedalBrackets_OnWithoutOff_NoBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectPedalBrackets_OffWithoutOn_NoBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOff, 3, 20));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectPedalBrackets_ConsecutiveOnOn_EndsPreviousBracket()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10),
            new MusicMarkItem(MusicMarkType.SustainOn, 2, 20),
            new MusicMarkItem(MusicMarkType.SustainOff, 4, 30));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Equal(2, result.Length);
        // First bracket: measure 0→2
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(2, result[0].EndMeasureIndex);
        // Second bracket: measure 2→4
        Assert.Equal(2, result[1].StartMeasureIndex);
        Assert.Equal(4, result[1].EndMeasureIndex);
    }

    [Fact]
    public void DetectPedalBrackets_MultiplePedalTypes_Independent()
    {
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10),
            new MusicMarkItem(MusicMarkType.UnaCordaOn, 1, 20),
            new MusicMarkItem(MusicMarkType.SustainOff, 3, 30),
            new MusicMarkItem(MusicMarkType.UnaCordaOff, 4, 40));

        var result = PedalEngraver.DetectPedalBrackets(marks);

        Assert.Equal(2, result.Length);
        // Sustain bracket
        var sustain = result.First(b => b.Type == PedalType.Sustain);
        Assert.Equal(0, sustain.StartMeasureIndex);
        Assert.Equal(3, sustain.EndMeasureIndex);
        // Una corda bracket
        var unaCorda = result.First(b => b.Type == PedalType.UnaCorda);
        Assert.Equal(1, unaCorda.StartMeasureIndex);
        Assert.Equal(4, unaCorda.EndMeasureIndex);
    }

    // --- PedalEngraver.Calculate ---

    private static ImmutableArray<MeasureLayout> CreateMeasureLayouts(int count, double measureWidth = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0));
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
    public void Calculate_BasicBracket_ReturnsLayout()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 42));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void Calculate_BracketStartX_LessThanEndX()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        Assert.True(result[0].StartX < result[0].EndX,
            $"StartX ({result[0].StartX:F2}) should be less than EndX ({result[0].EndX:F2})");
    }

    [Fact]
    public void Calculate_BracketY_BelowStaff()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        // Staff bottom is at 4.0 staff spaces, bracket should be below
        Assert.True(result[0].Y > 4.0, $"Y ({result[0].Y:F2}) should be below staff bottom (4.0)");
    }

    [Fact]
    public void Calculate_EdgeHeight_Positive()
    {
        var systems = CreateSingleSystem(4);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Single(result);
        Assert.True(result[0].EdgeHeight > 0, "EdgeHeight should be positive");
    }

    [Fact]
    public void Calculate_EmptyBrackets_ReturnsEmpty()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();

        var result = PedalEngraver.Calculate(
            ImmutableArray<PedalBracketItem>.Empty, systems, ml);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 10, 0));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_MultipleBrackets_AllReturned()
    {
        var systems = CreateSingleSystem(6);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var brackets = ImmutableArray.Create(
            new PedalBracketItem(PedalType.Sustain, 0, 2, 10),
            new PedalBracketItem(PedalType.Sustain, 3, 5, 20));

        var result = PedalEngraver.Calculate(brackets, systems, ml);

        Assert.Equal(2, result.Length);
        Assert.Equal(10, result[0].SourcePosition);
        Assert.Equal(20, result[1].SourcePosition);
    }
}
