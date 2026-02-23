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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for LyricHyphenEngraver - hyphen and extender layout calculation.
/// </summary>
[Trait("Category", "Unit")]
public class LyricHyphenTests
{
    private static LyricLayout CreateLyricLayout(
        string text,
        int measureIndex,
        int itemIndex,
        double x,
        double y,
        double width,
        LyricConnectorType connectorType = LyricConnectorType.None,
        int verseNumber = 1)
    {
        var item = new LyricItem(text, measureIndex, itemIndex, connectorType, 0, verseNumber);
        return new LyricLayout(item, x, y, width);
    }

    private static ImmutableArray<SystemLayout> CreateSingleSystem(params (int measureIndex, double x, double width)[] measures)
    {
        var measureLayouts = measures.Select(m =>
            new MeasureLayout(m.measureIndex, m.x, m.width, ImmutableArray<ItemLayout>.Empty))
            .ToImmutableArray();

        var system = new SystemLayout(0, 0, 76, 0, measureLayouts);
        return ImmutableArray.Create(system);
    }

    [Fact]
    public void CalculateLayouts_NoConnectors_ReturnsEmpty()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hello", 0, 0, 5, 10, 3),
            CreateLyricLayout("World", 0, 1, 15, 10, 3)
        };
        var systems = CreateSingleSystem((0, 0, 30));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Empty(result);
    }

    [Fact]
    public void CalculateLayouts_SingleHyphen_CreatesHyphenLayout()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hap", 0, 0, 5, 10, 2, LyricConnectorType.Hyphen),
            CreateLyricLayout("py", 0, 1, 9, 10, 1.5)  // Narrow gap for single hyphen
        };
        var systems = CreateSingleSystem((0, 0, 20));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        Assert.Equal(LyricConnectorType.Hyphen, result[0].Type);
        Assert.True(result[0].Dashes.Length >= 1, "Should have at least one hyphen dash");
        Assert.False(result[0].CrossesSystemBreak);
    }
    [Fact]
    public void CalculateLayouts_WideGap_CreatesMultipleHyphens()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hap", 0, 0, 5, 10, 2, LyricConnectorType.Hyphen),
            CreateLyricLayout("py", 0, 1, 50, 10, 1.5)  // Very wide gap
        };
        var systems = CreateSingleSystem((0, 0, 60));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        Assert.Equal(LyricConnectorType.Hyphen, result[0].Type);
        // Wide gap should produce multiple hyphens
        Assert.True(result[0].Dashes.Length >= 2,
            $"Expected at least 2 hyphens for wide gap, got {result[0].Dashes.Length}");
    }

    [Fact]
    public void CalculateLayouts_Extender_CreatesExtenderLayout()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("star", 0, 0, 5, 10, 2.5, LyricConnectorType.Extender),
            CreateLyricLayout("", 0, 3, 25, 10, 0)  // Empty syllable after melisma
        };
        var systems = CreateSingleSystem((0, 0, 35));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        Assert.Equal(LyricConnectorType.Extender, result[0].Type);
        Assert.True(result[0].ExtenderStartX > 0);
        Assert.True(result[0].ExtenderEndX > result[0].ExtenderStartX);
        Assert.False(result[0].CrossesSystemBreak);
    }

    [Fact]
    public void CalculateLayouts_MultipleVerses_CalculatesIndependently()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            // Verse 1
            CreateLyricLayout("Twin", 0, 0, 5, 10, 2.5, LyricConnectorType.Hyphen, verseNumber: 1),
            CreateLyricLayout("kle", 0, 1, 15, 10, 2, verseNumber: 1),
            // Verse 2
            CreateLyricLayout("Bril", 0, 0, 5, 12, 2, LyricConnectorType.Hyphen, verseNumber: 2),
            CreateLyricLayout("liant", 0, 1, 15, 12, 3, verseNumber: 2)
        };
        var systems = CreateSingleSystem((0, 0, 30));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Equal(2, result.Length);
        // Both should be hyphens
        Assert.All(result, r => Assert.Equal(LyricConnectorType.Hyphen, r.Type));
    }

    [Fact]
    public void CalculateLayouts_TooNarrowGap_NoHyphen()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hap", 0, 0, 5, 10, 2, LyricConnectorType.Hyphen),
            CreateLyricLayout("py", 0, 1, 7, 10, 1.5)  // Very narrow gap
        };
        var systems = CreateSingleSystem((0, 0, 15));

        var result = engraver.CalculateLayouts(lyrics, systems);

        // Should return empty because gap is too narrow
        Assert.Empty(result);
    }

    [Fact]
    public void Parameters_DefaultValues_AreReasonable()
    {
        var params1 = LyricHyphenParameters.Default;

        Assert.True(params1.MinDashLength > 0);
        Assert.True(params1.MaxDashLength > params1.MinDashLength);
        Assert.True(params1.DashThickness > 0);
        Assert.True(params1.ExtenderThickness > 0);
    }

    [Fact]
    public void CalculateLayouts_EmptyInput_ReturnsEmpty()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>();
        var systems = CreateSingleSystem((0, 0, 30));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Empty(result);
    }
}
