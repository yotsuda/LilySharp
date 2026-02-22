using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

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

    [Theory]
    [InlineData("ped", MusicMarkType.SustainOn)]
    [InlineData("sustain", MusicMarkType.SustainOn)]
    [InlineData("ped.off", MusicMarkType.SustainOff)]
    [InlineData("sustain.off", MusicMarkType.SustainOff)]
    [InlineData("sost.ped", MusicMarkType.SostenutoOn)]
    [InlineData("sostenuto", MusicMarkType.SostenutoOn)]
    [InlineData("sost.ped.off", MusicMarkType.SostenutoOff)]
    [InlineData("sostenuto.off", MusicMarkType.SostenutoOff)]
    [InlineData("una.corda", MusicMarkType.UnaCordaOn)]
    [InlineData("tre.corde", MusicMarkType.UnaCordaOff)]
    public void ParseMarkName_PedalMarks(string name, MusicMarkType expected)
    {
        var result = MusicMarkItem.ParseMarkName(name);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
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

    // --- ParseMarkParts for pedal ---

    [Fact]
    public void ParseMarkParts_PedOff()
    {
        var parts = ImmutableArray.Create("ped", "off");
        var result = MusicMarkItem.ParseMarkParts(parts);
        Assert.NotNull(result);
        Assert.Equal(MusicMarkType.SustainOff, result.Value);
    }

    [Fact]
    public void ParseMarkParts_SostPed()
    {
        var parts = ImmutableArray.Create("sost", "ped");
        var result = MusicMarkItem.ParseMarkParts(parts);
        Assert.NotNull(result);
        Assert.Equal(MusicMarkType.SostenutoOn, result.Value);
    }

    [Fact]
    public void ParseMarkParts_UnaCorda()
    {
        var parts = ImmutableArray.Create("una", "corda");
        var result = MusicMarkItem.ParseMarkParts(parts);
        Assert.NotNull(result);
        Assert.Equal(MusicMarkType.UnaCordaOn, result.Value);
    }

    [Fact]
    public void ParseMarkParts_TreCorde()
    {
        var parts = ImmutableArray.Create("tre", "corde");
        var result = MusicMarkItem.ParseMarkParts(parts);
        Assert.NotNull(result);
        Assert.Equal(MusicMarkType.UnaCordaOff, result.Value);
    }
}
