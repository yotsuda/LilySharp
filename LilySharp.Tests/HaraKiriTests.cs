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
/// Tests for hara-kiri (empty staff auto-hiding).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hara-kiri-group-spanner.cc — core suicide decision logic
/// LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves / RemoveAllEmptyStaves
/// </remarks>
[Trait("Category", "Unit")]
public class HaraKiriTests
{
    private static NoteItem MakeNote(int staffPosition = 0) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    private static RestItem MakeRest() =>
        new(Fraction.Quarter, 0, 0);

    private static ChordItem MakeChord() =>
        new(ImmutableArray.Create(new ChordNoteInfo(0, null, false)),
            Fraction.Quarter, 0, 0);

    private static Measure MakeNoteMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeNote(), MakeNote(), MakeNote(), MakeNote()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Measure MakeRestMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeRest(), MakeRest(), MakeRest(), MakeRest()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Measure MakeChordMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeChord()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Staff CreateStaff(Measure[] measures, bool removeEmpty = false, bool removeFirst = false) =>
        new(ClefType.Treble,
            ImmutableArray.Create(new Voice("v1", measures.ToImmutableArray())),
            RemoveEmpty: removeEmpty,
            RemoveFirst: removeFirst);

    // --- IsStaffEmpty tests ---

    [Fact]
    public void IsStaffEmpty_WithNotes_ReturnsFalse()
    {
        var staff = CreateStaff([MakeNoteMeasure()]);
        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    [Fact]
    public void IsStaffEmpty_WithOnlyRests_ReturnsTrue()
    {
        var staff = CreateStaff([MakeRestMeasure()]);
        Assert.True(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    [Fact]
    public void IsStaffEmpty_WithChord_ReturnsFalse()
    {
        var staff = CreateStaff([MakeChordMeasure()]);
        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    [Fact]
    public void IsStaffEmpty_MixedMeasures_ChecksOnlyRange()
    {
        // Measures: [notes, rests, notes]
        var staff = CreateStaff([MakeNoteMeasure(), MakeRestMeasure(), MakeNoteMeasure()]);

        // Range [1,2) = only the rest measure → empty
        Assert.True(HaraKiri.IsStaffEmpty(staff, 1, 2));

        // Range [0,2) = includes note measure → not empty
        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 2));
    }

    [Fact]
    public void IsStaffEmpty_MultiVoice_AnyVoiceKeepsAlive()
    {
        // Voice 1: rests only, Voice 2: has notes
        var v1 = new Voice("v1", ImmutableArray.Create(MakeRestMeasure()));
        var v2 = new Voice("v2", ImmutableArray.Create(MakeNoteMeasure()));
        var staff = new Staff(ClefType.Treble, ImmutableArray.Create(v1, v2), RemoveEmpty: true);

        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    // --- ShouldHideStaff tests ---

    [Fact]
    public void ShouldHide_RemoveEmptyFalse_NeverHides()
    {
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: false);
        Assert.False(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: false));
    }

    [Fact]
    public void ShouldHide_RemoveEmpty_EmptyStaff_Hides()
    {
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: true);
        Assert.True(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: false));
    }

    [Fact]
    public void ShouldHide_RemoveEmpty_NonEmptyStaff_DoesNotHide()
    {
        var staff = CreateStaff([MakeNoteMeasure()], removeEmpty: true);
        Assert.False(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: false));
    }

    [Fact]
    public void ShouldHide_FirstSystem_RemoveFirstFalse_NeverHides()
    {
        // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — remove-first = false
        // First system always shows all staves unless remove-first = true
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: true, removeFirst: false);
        Assert.False(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: true));
    }

    [Fact]
    public void ShouldHide_FirstSystem_RemoveFirstTrue_CanHide()
    {
        // LILYPOND-REF: RemoveAllEmptyStaves sets both remove-empty and remove-first
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: true, removeFirst: true);
        Assert.True(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: true));
    }

    // --- Layout integration tests ---

    [Fact]
    public void LayoutStaffGroups_HidesEmptyStaff_InNonFirstSystem()
    {
        // Create 3-staff score: treble (notes), alto (rests), bass (notes)
        // Alto has RemoveEmpty=true
        var noteMeasures = new[] { MakeNoteMeasure(), MakeNoteMeasure() };
        var restMeasures = new[] { MakeRestMeasure(), MakeRestMeasure() };

        var trebleStaff = CreateStaff(noteMeasures);
        var altoStaff = CreateStaff(restMeasures, removeEmpty: true);
        var bassStaff = CreateStaff(noteMeasures);

        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(altoStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        // Non-first system, measures [0,2)
        var staffGroups = layouter.LayoutStaffGroups(score, 0, 0, 2, isFirstSystem: false);

        // Alto staff should be hidden
        Assert.Equal(3, staffGroups.Length);
        Assert.False(staffGroups[0].Staves[0].IsHidden); // treble: visible
        Assert.True(staffGroups[1].Staves[0].IsHidden);  // alto: hidden
        Assert.False(staffGroups[2].Staves[0].IsHidden); // bass: visible

        // Hidden staff should have Height=0
        Assert.Equal(0, staffGroups[1].Height);
    }

    [Fact]
    public void LayoutStaffGroups_ShowsEmptyStaff_InFirstSystem_WhenRemoveFirstFalse()
    {
        var restMeasures = new[] { MakeRestMeasure() };
        var noteMeasures = new[] { MakeNoteMeasure() };

        var trebleStaff = CreateStaff(noteMeasures);
        var altoStaff = CreateStaff(restMeasures, removeEmpty: true, removeFirst: false);
        var bassStaff = CreateStaff(noteMeasures);

        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(altoStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        // First system — should NOT hide even though empty
        var staffGroups = layouter.LayoutStaffGroups(score, 0, 0, 1, isFirstSystem: true);

        Assert.False(staffGroups[1].Staves[0].IsHidden); // alto: visible in first system
        Assert.True(staffGroups[1].Height > 0);
    }

    [Fact]
    public void LayoutStaffGroups_HiddenStaff_DoesNotAffectSpacing()
    {
        // When a staff is hidden, the gap between adjacent visible staves should be
        // smaller (no extra inter-group spacing for the hidden staff)
        var noteMeasures = new[] { MakeNoteMeasure() };
        var restMeasures = new[] { MakeRestMeasure() };

        var trebleStaff = CreateStaff(noteMeasures);
        var altoStaff = CreateStaff(restMeasures, removeEmpty: true);
        var bassStaff = CreateStaff(noteMeasures);

        var scoreWithHidden = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(altoStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var scoreWithoutMiddle = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        var groupsWithHidden = layouter.LayoutStaffGroups(scoreWithHidden, 0, 0, 1, isFirstSystem: false);
        var groupsWithout = layouter.LayoutStaffGroups(scoreWithoutMiddle, 0, 0, 1, isFirstSystem: false);

        // The bass staff Y should be similar (hidden staff contributes no height/gap)
        double bassYWithHidden = groupsWithHidden[2].Y;
        double bassYWithout = groupsWithout[1].Y;

        Assert.True(Math.Abs(bassYWithHidden - bassYWithout) < 0.1,
            $"Bass Y with hidden ({bassYWithHidden:F2}) should be ≈ Bass Y without ({bassYWithout:F2})");
    }

    [Fact]
    public void LayoutStaffGroups_AllStavesEmpty_HidesAll()
    {
        var restMeasures = new[] { MakeRestMeasure() };

        var staff1 = CreateStaff(restMeasures, removeEmpty: true, removeFirst: true);
        var staff2 = CreateStaff(restMeasures, removeEmpty: true, removeFirst: true);

        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(staff1),
                StaffGroup.CreateSingle(staff2)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        var staffGroups = layouter.LayoutStaffGroups(score, 0, 0, 1, isFirstSystem: true);

        Assert.True(staffGroups[0].Staves[0].IsHidden);
        Assert.True(staffGroups[1].Staves[0].IsHidden);
    }
}
