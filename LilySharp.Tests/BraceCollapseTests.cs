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
/// Tests for bracket/brace collapse when staves are hidden via hara-kiri.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system-start-delimiter.cc:127-129 — collapse-height check
/// LILYPOND-REF: scm/define-grobs.scm SystemStartBrace collapse-height = 5
///
/// When the brace height (BraceBottom - BraceTop) is less than the collapse-height,
/// the brace delimiter is suppressed. For SystemStartBrace, collapse-height = 5 staff spaces.
/// A single staff (4 staff spaces) triggers collapse; two or more visible staves do not.
/// </remarks>
[Trait("Category", "Unit")]
public class BraceCollapseTests
{
    private static NoteItem MakeNote(int staffPosition = 0) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    private static RestItem MakeRest() =>
        new(Fraction.Quarter, 0, 0);

    private static Measure MakeNoteMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeNote(), MakeNote(), MakeNote(), MakeNote()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Measure MakeRestMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeRest(), MakeRest(), MakeRest(), MakeRest()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Staff CreateStaff(ClefType clef, Measure[] measures,
        bool removeEmpty = false, bool removeFirst = false) =>
        new(clef,
            ImmutableArray.Create(new Voice("v1", measures.ToImmutableArray())),
            RemoveEmpty: removeEmpty,
            RemoveFirst: removeFirst);

    /// <summary>
    /// Lays the groups out through the overload THE RENDER PATH USES — skylines decide the
    /// gaps, and hara-kiri is the measure range.
    /// </summary>
    /// <remarks>
    /// ⚠️ These tests used to call the skyline-less overload, which no production code has
    /// taken since 2026-07-27; their subject (which staves die, and whether the brace
    /// collapses around what is left) is DRAWN geometry, so it should be measured where it
    /// is drawn. What the switch buys is the entry point, not different numbers: brace
    /// collapse turns on how many staves survive and on the spec's basic-distance, and a
    /// skyline can only ever push the pair FURTHER apart — so every assertion below holds
    /// either way, and would have to be re-derived rather than relaxed if one ever did not.
    /// </remarks>
    private static ImmutableArray<StaffGroupLayout> LayoutAsRendered(
        MultiStaffLayouter layouter, MultiStaffScore score, bool isFirstSystem)
    {
        var items = ImmutableArray.CreateBuilder<ItemLayout>(4);
        for (int i = 0; i < 4; i++)
            items.Add(new ItemLayout(i, 5.0 + i * 4.0, 1.0));
        var measureLayouts = ImmutableArray.Create(
            new MeasureLayout(0, 0, 40, items.ToImmutable()));

        return layouter.LayoutStaffGroups(
            score, new SkylineBuilder(LayoutOptions.Default.StaffHeight), measureLayouts,
            0, 1, isFirstSystem);
    }

    [Fact]
    public void GrandStaff_TwoStaves_OneHidden_BraceHeightEqualsSingleStaff()
    {
        // 2-staff grand staff: treble (notes), bass (rests, RemoveEmpty)
        // In non-first system, bass is hidden → only treble remains
        // Brace height should be staffHeight (4) < collapse-height (5)
        var treble = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var bass = CreateStaff(ClefType.Bass, [MakeRestMeasure()], removeEmpty: true);
        var grandStaffGroup = StaffGroup.CreateGrandStaff(treble, bass);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaffGroup),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = LayoutAsRendered(layouter, score, isFirstSystem: false);

        Assert.Single(groups);
        var group = groups[0];
        Assert.NotNull(group.GrandStaffLayout);

        // Bass should be hidden
        Assert.False(group.Staves[0].IsHidden); // treble: visible
        Assert.True(group.Staves[1].IsHidden);  // bass: hidden

        // Brace height should equal a single staff height (4)
        double braceHeight = group.GrandStaffLayout!.TotalHeight;
        Assert.Equal(options.StaffHeight, braceHeight);

        // This height is less than collapse-height (5), so brace should be suppressed
        Assert.True(braceHeight < 5, $"Brace height ({braceHeight}) should be < 5 (collapse-height)");
    }

    [Fact]
    public void GrandStaff_TwoStaves_BothVisible_BraceHeightExceedsCollapseHeight()
    {
        // Both staves have notes → both visible
        // Brace height should exceed collapse-height (5)
        var treble = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var bass = CreateStaff(ClefType.Bass, [MakeNoteMeasure()]);
        var grandStaffGroup = StaffGroup.CreateGrandStaff(treble, bass);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaffGroup),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = LayoutAsRendered(layouter, score, isFirstSystem: false);

        var group = groups[0];
        double braceHeight = group.GrandStaffLayout!.TotalHeight;

        // With 2 visible staves, brace height should exceed collapse-height
        Assert.True(braceHeight >= 5, $"Brace height ({braceHeight}) should be >= 5");
    }

    [Fact]
    public void GrandStaff_ThreeStaves_OneHidden_BraceStillVisible()
    {
        // 3-staff grand staff (organ): treble (notes), middle (rests, hidden), pedal (notes)
        // Two staves remain visible → brace height > collapse-height
        var treble = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var middle = CreateStaff(ClefType.Treble, [MakeRestMeasure()], removeEmpty: true);
        var pedal = CreateStaff(ClefType.Bass, [MakeNoteMeasure()]);
        var grandStaffGroup = StaffGroup.CreateGrandStaff(treble, middle, pedal);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaffGroup),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = LayoutAsRendered(layouter, score, isFirstSystem: false);

        var group = groups[0];
        Assert.True(group.Staves[1].IsHidden);   // middle: hidden
        Assert.False(group.Staves[0].IsHidden);   // treble: visible
        Assert.False(group.Staves[2].IsHidden);   // pedal: visible

        double braceHeight = group.GrandStaffLayout!.TotalHeight;
        // Two visible staves with spacing → should exceed collapse-height
        Assert.True(braceHeight >= 5, $"Brace height ({braceHeight}) should be >= 5 (collapse-height)");
    }

    [Fact]
    public void GrandStaff_AllHidden_BraceHeightIsZero()
    {
        // All staves in grand staff are empty and have RemoveEmpty + RemoveFirst
        var treble = CreateStaff(ClefType.Treble, [MakeRestMeasure()],
            removeEmpty: true, removeFirst: true);
        var bass = CreateStaff(ClefType.Bass, [MakeRestMeasure()],
            removeEmpty: true, removeFirst: true);
        var grandStaffGroup = StaffGroup.CreateGrandStaff(treble, bass);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaffGroup),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = LayoutAsRendered(layouter, score, isFirstSystem: true);

        var group = groups[0];
        Assert.True(group.Staves[0].IsHidden);
        Assert.True(group.Staves[1].IsHidden);

        // All hidden → height 0
        Assert.Equal(0, group.Height);
    }

    [Fact]
    public void GrandStaff_BraceTopBottom_MatchVisibleStaves()
    {
        // 3-staff grand staff: first hidden, middle & last visible
        // BraceTop should be at middle staff Y, BraceBottom at last staff bottom
        var treble = CreateStaff(ClefType.Treble, [MakeRestMeasure()], removeEmpty: true);
        var middle = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var pedal = CreateStaff(ClefType.Bass, [MakeNoteMeasure()]);
        var grandStaffGroup = StaffGroup.CreateGrandStaff(treble, middle, pedal);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaffGroup),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = LayoutAsRendered(layouter, score, isFirstSystem: false);

        var group = groups[0];
        var gs = group.GrandStaffLayout!;

        Assert.True(group.Staves[0].IsHidden); // treble: hidden

        // First visible staff (middle) Y = BraceTop
        var firstVisible = group.Staves[1]; // middle
        Assert.Equal(firstVisible.Y, gs.BraceTop);

        // Last visible staff bottom = BraceBottom (staff.Y is Y-up ⇒ bottom = Y - Height)
        var lastVisible = group.Staves[2]; // pedal
        Assert.Equal(lastVisible.Y - lastVisible.Height, gs.BraceBottom);
    }
}
