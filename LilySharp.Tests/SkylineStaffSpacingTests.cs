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

/// <summary>
/// Tests for skyline-based staff spacing in MultiStaffLayouter.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
/// </remarks>
[Trait("Category", "Unit")]
public class SkylineStaffSpacingTests
{
    private static readonly LayoutOptions DefaultOptions = LayoutOptions.Default;
    private static readonly MeasureLayouter MeasureLayouter = new();
    private static readonly double StaffHeight = DefaultOptions.StaffHeight; // 4.0

    /// <summary>
    /// Creates a simple grand staff score with treble/bass and given notes.
    /// </summary>
    private static MultiStaffScore CreateGrandStaffScore(
        ImmutableArray<MusicItem> trebleItems,
        ImmutableArray<MusicItem> bassItems)
    {
        var trebleMeasure = new Measure(trebleItems, BarlineType.None, BarlineType.Single, null, 0, 0);
        var bassMeasure = new Measure(bassItems, BarlineType.None, BarlineType.Single, null, 0, 0);

        var trebleVoice = new Voice("treble", ImmutableArray.Create(trebleMeasure));
        var bassVoice = new Voice("bass", ImmutableArray.Create(bassMeasure));

        var trebleStaff = Staff.Create(ClefType.Treble, trebleVoice);
        var bassStaff = Staff.Create(ClefType.Bass, bassVoice);
        var grandStaff = StaffGroup.CreateGrandStaff(trebleStaff, bassStaff);

        return new MultiStaffScore(
            ImmutableArray.Create(grandStaff),
            new TimeSignature(4, 4),
            KeySignature.CMajor);
    }

    /// <summary>
    /// Creates a simple measure layout for testing.
    /// </summary>
    private static ImmutableArray<MeasureLayout> CreateSimpleMeasureLayouts(int itemCount)
    {
        var items = ImmutableArray.CreateBuilder<ItemLayout>(itemCount);
        for (int i = 0; i < itemCount; i++)
            items.Add(new ItemLayout(i, 5.0 + i * 4.0, 1.0));
        return ImmutableArray.Create(new MeasureLayout(0, 0, 40, items.ToImmutable()));
    }

    [Fact]
    public void SkylineSpacing_SimpleNotes_UsesAtLeastBasicDistance()
    {
        // Notes in the middle of the staff → no collision, should use basic-distance
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(2, Fraction.Quarter, 0, null, false, 0));

        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(-2, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(2);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        double height = layouter.CalculateSystemHeight(score, skylineBuilder, measureLayouts);
        double heightFixed = layouter.CalculateSystemHeight(score);

        // Skyline-based height should be >= fixed height (it uses max of skyline + padding and basic-distance)
        Assert.True(height >= heightFixed - 0.01,
            $"Skyline height ({height:F2}) should be >= fixed height ({heightFixed:F2})");
    }

    [Fact]
    public void SkylineSpacing_ExtremeLedgerLines_IncreasesGap()
    {
        // Notes with extreme ledger lines (below treble, above bass) should force larger gap
        // Treble: very low note (staff position -8 → 4 ledger lines below staff)
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(-8, Fraction.Quarter, 0, null, false, 0));

        // Bass: very high note (staff position 10 → 3 ledger lines above staff)
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(10, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(1);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        double skylineHeight = layouter.CalculateSystemHeight(score, skylineBuilder, measureLayouts);
        double fixedHeight = layouter.CalculateSystemHeight(score);

        // With extreme ledger lines, skyline-based spacing should be larger than fixed
        Assert.True(skylineHeight >= fixedHeight,
            $"Skyline height ({skylineHeight:F2}) should be >= fixed height ({fixedHeight:F2}) with extreme notes");
    }

    [Fact]
    public void SkylineSpacing_LayoutStaffGroups_ProducesValidLayout()
    {
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(1);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        var groups = layouter.LayoutStaffGroups(score, skylineBuilder, measureLayouts);

        Assert.Single(groups);
        var grandStaff = groups[0];
        Assert.NotNull(grandStaff.GrandStaffLayout);
        Assert.Equal(2, grandStaff.GrandStaffLayout!.Staves.Length);

        // staff.Y is Y-up: the second staff sits BELOW the first, i.e. at least a full
        // staff height further down ⇒ its Y is SMALLER by more than staffHeight.
        double firstY = grandStaff.GrandStaffLayout.Staves[0].Y;
        double secondY = grandStaff.GrandStaffLayout.Staves[1].Y;
        Assert.True(secondY < firstY - StaffHeight,
            $"Second staff Y ({secondY:F2}) should be < first staff Y ({firstY:F2}) − staffHeight ({StaffHeight})");
    }

    [Fact]
    public void SkylineSpacing_FallsBackGracefully_WhenSkylineEmpty()
    {
        // Empty measures → skylines will be empty → should fall back to fixed formula
        var trebleItems = ImmutableArray<MusicItem>.Empty;
        var bassItems = ImmutableArray<MusicItem>.Empty;

        var trebleMeasure = new Measure(trebleItems, BarlineType.None, BarlineType.Single, null, 0, 0);
        var bassMeasure = new Measure(bassItems, BarlineType.None, BarlineType.Single, null, 0, 0);

        var trebleVoice = new Voice("treble", ImmutableArray.Create(trebleMeasure));
        var bassVoice = new Voice("bass", ImmutableArray.Create(bassMeasure));

        var trebleStaff = Staff.Create(ClefType.Treble, trebleVoice);
        var bassStaff = Staff.Create(ClefType.Bass, bassVoice);
        var grandStaff = StaffGroup.CreateGrandStaff(trebleStaff, bassStaff);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaff),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(0);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        double skylineHeight = layouter.CalculateSystemHeight(score, skylineBuilder, measureLayouts);
        double fixedHeight = layouter.CalculateSystemHeight(score);

        // With empty skylines, should fall back to the same as fixed formula
        Assert.Equal(fixedHeight, skylineHeight, 2);
    }

    // --- Pure height estimation ---
    // LILYPOND-REF: lily/axis-group-interface.cc:138-173

    [Fact]
    public void PureSystemHeight_IncludesLooseLineExtents()
    {
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);

        double baseHeight = layouter.CalculateSystemHeight(score);

        // With loose line extents (e.g., lyrics below, tempo above)
        double pureHeight = layouter.CalculatePureSystemHeight(score, looseDownExtent: 3.0, looseUpExtent: 2.5);

        Assert.Equal(baseHeight + 5.5, pureHeight, 3);
    }

    [Fact]
    public void PureSystemHeight_ZeroExtents_EqualsBaseHeight()
    {
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);

        double baseHeight = layouter.CalculateSystemHeight(score);
        double pureHeight = layouter.CalculatePureSystemHeight(score, 0, 0);

        Assert.Equal(baseHeight, pureHeight, 3);
    }
}
