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
using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Tests;

/// <summary>
/// Tests for outside-staff-priority stacking (G-2).
/// LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside_staff_axis_group
/// </summary>
[Trait("Category", "Unit")]
public class OutsideStaffStackerTests
{
    private static ImmutableArray<SystemLayout> CreateSingleSystem()
    {
        var measures = ImmutableArray.Create(
            new MeasureLayout(0, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(1, 35, 30, ImmutableArray<ItemLayout>.Empty));

        return ImmutableArray.Create(
            new SystemLayout(SystemIndex: 0, Y: 10, Width: 70, PrefixWidth: 5,
                Measures: measures));
    }

    [Fact]
    public void HairpinPushedBelowOverlappingDynamic()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1270 DynamicLineSpanner.outside-staff-priority = 250
        // Dynamic and hairpin share priority 250; hairpin should avoid overlapping dynamic
        var systems = CreateSingleSystem();

        // Dynamic at X=20, Y=6.0 (below staff)
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 20, Y: 6.0, Text: "f", SourcePosition: 0));

        // Hairpin spanning X=[18, 25], initially at BaseY=5.2
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 0, StartX: 18, EndX: 25,
                Y: 5.2, StartOpening: 0, EndOpening: 0.333,
                Direction: HairpinDirection.Crescendo, SourcePosition: 0));

        var textSpanners = ImmutableArray<TextSpannerLayout>.Empty;

        var (_, adjHairpins, _) = OutsideStaffStacker.StackBelowStaff(
            systems, dynamics, hairpins, textSpanners);

        // Hairpin should be pushed below the dynamic's bottom extent
        // Dynamic bottom = 6.0 + 0.3 (descent) = 6.3
        // Required Y = 6.3 + 0.46 (padding) + 0.333 (half height) ≈ 7.09
        Assert.True(adjHairpins[0].Y > 5.2,
            $"Hairpin Y ({adjHairpins[0].Y:F2}) should be pushed below original 5.2");
        Assert.True(adjHairpins[0].Y >= 7.0,
            $"Hairpin Y ({adjHairpins[0].Y:F2}) should clear dynamic bottom + padding");
    }

    [Fact]
    public void HairpinNotAdjustedWhenNoOverlap()
    {
        // Hairpin and dynamic at different X positions should not interact
        var systems = CreateSingleSystem();

        // Dynamic at X=10
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 10, Y: 6.0, Text: "p", SourcePosition: 0));

        // Hairpin at X=[40, 55] — far from dynamic
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 1, StartX: 40, EndX: 55,
                Y: 5.2, StartOpening: 0, EndOpening: 0.333,
                Direction: HairpinDirection.Crescendo, SourcePosition: 0));

        var (_, adjHairpins, _) = OutsideStaffStacker.StackBelowStaff(
            systems, dynamics, hairpins, ImmutableArray<TextSpannerLayout>.Empty);

        // No X overlap, so hairpin Y should stay at StaffBottom + padding + halfHeight
        // (or its original value if that's larger)
        Assert.True(adjHairpins[0].Y <= 5.2 + 0.5,
            $"Hairpin Y ({adjHairpins[0].Y:F2}) should not be significantly pushed when no overlap");
    }

    [Fact]
    public void TextSpannerStacksBelowDynamicAndHairpin()
    {
        // LILYPOND-REF: scm/define-grobs.scm:3472 TextSpanner.outside-staff-priority = 350
        // Text spanner (priority 350) should stack below dynamics+hairpins (priority 250)
        var systems = CreateSingleSystem();

        // Dynamic at X=20, Y=6.0
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 20, Y: 6.0, Text: "mf", SourcePosition: 0));

        // Hairpin at X=[22, 35], will be pushed below dynamic
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 0, StartX: 22, EndX: 35,
                Y: 5.2, StartOpening: 0, EndOpening: 0.333,
                Direction: HairpinDirection.Crescendo, SourcePosition: 0));

        // Text spanner overlapping with both dynamic and hairpin
        var textSpanners = ImmutableArray.Create(
            new TextSpannerLayout(StartMeasureIndex: 0, StartX: 18, EndX: 40,
                LineStartX: 22, Y: 5.5, Text: "rit.", Style: TextSpannerStyle.DashedLine,
                DashPeriod: 2.0, DashFraction: 0.4, SourcePosition: 0));

        var (_, adjHairpins, adjSpanners) = OutsideStaffStacker.StackBelowStaff(
            systems, dynamics, hairpins, textSpanners);

        // Text spanner should be below the hairpin
        Assert.True(adjSpanners[0].Y > adjHairpins[0].Y,
            $"TextSpanner Y ({adjSpanners[0].Y:F2}) should be below hairpin Y ({adjHairpins[0].Y:F2})");
    }

    [Fact]
    public void EmptyInputsReturnUnchanged()
    {
        var systems = CreateSingleSystem();
        var emptyDyn = ImmutableArray<DynamicLayout>.Empty;
        var emptyHp = ImmutableArray<HairpinLayout>.Empty;
        var emptyTs = ImmutableArray<TextSpannerLayout>.Empty;

        var (d, h, t) = OutsideStaffStacker.StackBelowStaff(systems, emptyDyn, emptyHp, emptyTs);

        Assert.True(d.IsEmpty);
        Assert.True(h.IsEmpty);
        Assert.True(t.IsEmpty);
    }

    [Fact]
    public void DifferentSystemsDoNotInterfere()
    {
        // Elements in different systems should not affect each other
        var measures0 = ImmutableArray.Create(
            new MeasureLayout(0, 5, 30, ImmutableArray<ItemLayout>.Empty));
        var measures1 = ImmutableArray.Create(
            new MeasureLayout(1, 5, 30, ImmutableArray<ItemLayout>.Empty));

        var systems = ImmutableArray.Create(
            new SystemLayout(SystemIndex: 0, Y: 10, Width: 70, PrefixWidth: 5, Measures: measures0),
            new SystemLayout(SystemIndex: 1, Y: 30, Width: 70, PrefixWidth: 5, Measures: measures1));

        // Dynamic in system 0 at X=20
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 20, Y: 8.0, Text: "ff", SourcePosition: 0));

        // Hairpin in system 1 at X=[20, 30] — same X but different system
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 1, StartX: 20, EndX: 30,
                Y: 5.2, StartOpening: 0, EndOpening: 0.333,
                Direction: HairpinDirection.Crescendo, SourcePosition: 0));

        var (_, adjHairpins, _) = OutsideStaffStacker.StackBelowStaff(
            systems, dynamics, hairpins, ImmutableArray<TextSpannerLayout>.Empty);

        // Hairpin in system 1 should NOT be affected by dynamic in system 0
        Assert.True(adjHairpins[0].Y <= 5.2 + 0.5,
            $"Hairpin in different system should not be pushed: Y={adjHairpins[0].Y:F2}");
    }
}
