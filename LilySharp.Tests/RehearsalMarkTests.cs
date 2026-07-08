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
public class RehearsalMarkTests
{
    // --- MusicMarkType.Rehearsal ---

    [Fact]
    public void MusicMarkType_Rehearsal_Exists()
    {
        var type = MusicMarkType.Rehearsal;
        Assert.Equal("Rehearsal", type.ToString());
    }

    // --- ParseMarkName ---

    [Theory]
    [InlineData("mark.A", MusicMarkType.Rehearsal)]
    [InlineData("mark.B", MusicMarkType.Rehearsal)]
    [InlineData("mark.1", MusicMarkType.Rehearsal)]
    [InlineData("Mark.C", MusicMarkType.Rehearsal)]
    [InlineData("MARK.Z", MusicMarkType.Rehearsal)]
    public void ParseMarkName_RehearsalMark_ReturnsRehearsal(string name, MusicMarkType expected)
    {
        var result = MusicMarkItem.ParseMarkName(name);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("mark.")]  // No character after "mark."
    [InlineData("mark")]   // No dot
    public void ParseMarkName_InvalidRehearsal_ReturnsNull(string name)
    {
        var result = MusicMarkItem.ParseMarkName(name);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("segno", MusicMarkType.Segno)]
    [InlineData("coda", MusicMarkType.Coda)]
    [InlineData("fine", MusicMarkType.Fine)]
    public void ParseMarkName_OtherMarks_StillWork(string name, MusicMarkType expected)
    {
        var result = MusicMarkItem.ParseMarkName(name);
        Assert.NotNull(result);
        Assert.Equal(expected, result.Value);
    }

    // --- ParseRehearsalText ---

    [Theory]
    [InlineData("mark.A", "A")]
    [InlineData("mark.B", "B")]
    [InlineData("mark.1", "1")]
    [InlineData("mark.abc", "abc")]           // case preserved — no forced upper-casing
    [InlineData("MARK.z", "z")]               // case preserved
    [InlineData("mark.\"Solo\"", "Solo")]     // quoted free-text label kept as written
    [InlineData("mark.\"D.S.\"", "D.S.")]     // quoted: symbols and case preserved
    public void ParseRehearsalText_ExtractsText(string name, string expected)
    {
        var result = MusicMarkItem.ParseRehearsalText(name);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseRehearsalText_ShortString_ReturnsAsIs()
    {
        var result = MusicMarkItem.ParseRehearsalText("foo");
        Assert.Equal("foo", result);
    }

    // --- MusicMarkItem constructors ---

    [Fact]
    public void MusicMarkItem_StandardConstructor_SetsDefaults()
    {
        var item = new MusicMarkItem(MusicMarkType.Fine, 3, 100);

        Assert.Equal(MusicMarkType.Fine, item.Type);
        Assert.Equal("Fine", item.Text);
        Assert.Equal(MusicMarkPosition.End, item.Position);
        Assert.Equal(MusicMarkVertical.Above, item.Vertical);
        Assert.False(item.IsSymbol);
        Assert.Equal(3, item.MeasureIndex);
        Assert.Equal(100, item.SourcePosition);
    }

    [Fact]
    public void MusicMarkItem_CustomText_ForRehearsal()
    {
        var item = new MusicMarkItem(MusicMarkType.Rehearsal, "A", 0, 42);

        Assert.Equal(MusicMarkType.Rehearsal, item.Type);
        Assert.Equal("A", item.Text);
        Assert.Equal(MusicMarkPosition.Beginning, item.Position);
        Assert.Equal(MusicMarkVertical.Above, item.Vertical);
        Assert.False(item.IsSymbol);
        Assert.Equal(0, item.MeasureIndex);
        Assert.Equal(42, item.SourcePosition);
    }

    [Fact]
    public void MusicMarkItem_Rehearsal_PositionIsBeginning()
    {
        var item = new MusicMarkItem(MusicMarkType.Rehearsal, "B", 1, 0);
        Assert.Equal(MusicMarkPosition.Beginning, item.Position);
    }

    [Fact]
    public void MusicMarkItem_Rehearsal_VerticalIsAbove()
    {
        var item = new MusicMarkItem(MusicMarkType.Rehearsal, "C", 2, 0);
        Assert.Equal(MusicMarkVertical.Above, item.Vertical);
    }

    // --- ParseMarkParts ---

    [Fact]
    public void ParseMarkParts_RehearsalMark()
    {
        var parts = ImmutableArray.Create("mark", "A");
        var result = MusicMarkItem.ParseMarkParts(parts);
        Assert.NotNull(result);
        Assert.Equal(MusicMarkType.Rehearsal, result.Value);
    }

    // --- MusicMarkEngraver.Calculate for Rehearsal ---

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
    public void Calculate_RehearsalMark_ReturnsLayout()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "A", 0, 42));

        var result = MusicMarkEngraver.Calculate(null, marks, systems, ml);

        Assert.Single(result);
        Assert.Equal(MusicMarkType.Rehearsal, result[0].MarkType);
        Assert.Equal("A", result[0].Text);
        Assert.False(result[0].IsSymbol);
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void Calculate_RehearsalMark_XAtBeginning()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "B", 1, 0));

        var result = MusicMarkEngraver.Calculate(null, marks, systems, ml);

        Assert.Single(result);
        // Rehearsal marks are at beginning, so X should be near start of measure 1
        double measureStart = ml[1].X;
        Assert.True(result[0].X > measureStart && result[0].X < measureStart + 2.0,
            $"X ({result[0].X:F2}) should be near start of measure ({measureStart:F2})");
    }

    [Fact]
    public void Calculate_RehearsalMark_YAboveStaff()
    {
        var systems = CreateSingleSystem(2);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "C", 0, 0));

        var result = MusicMarkEngraver.Calculate(null, marks, systems, ml);

        Assert.Single(result);
        // Y should be negative (above staff)
        Assert.True(result[0].Y < 0, $"Y ({result[0].Y:F2}) should be above staff (negative)");
    }

    [Fact]
    public void Calculate_MultipleRehearsalMarks()
    {
        var systems = CreateSingleSystem(3);
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rehearsal, "A", 0, 10),
            new MusicMarkItem(MusicMarkType.Rehearsal, "B", 2, 20));

        var result = MusicMarkEngraver.Calculate(null, marks, systems, ml);

        Assert.Equal(2, result.Length);
        Assert.Equal("A", result[0].Text);
        Assert.Equal("B", result[1].Text);
    }
}
