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
using LilySharp.Core.Rendering;

namespace LilySharp.Tests;

/// <summary>
/// Tests for outside-staff-priority stacking (G-2).
/// LILYPOND-REF: lily/axis-group-interface.cc:860-985 Axis_group_interface::skyline_spacing
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

        // Dynamic at X=20, device Y=6.0 below staff → YUp = ToUp(6, 2) = −4.0.
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 20, YUp: -4.0, Text: "f", SourcePosition: 0));

        // Hairpin spanning X=[18, 25], initially at device BaseY=5.2 (YUp = -5.2).
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 0, StartX: 18, EndX: 25,
                YUp: -5.2, StartOpening: 0, EndOpening: 0.333,
                Direction: HairpinDirection.Crescendo, SourcePosition: 0));

        var (_, adjHairpins, _, _) = OutsideStaffStacker.StackBelowStaff(ScoreTextMetrics.Bundled, 
            systems, dynamics, hairpins);

        // Hairpin should be pushed below the dynamic's bottom extent (down = smaller
        // YUp). Dynamic bottom = 6.0 + 0.3 (descent) = 6.3; required device Y =
        // 6.3 + 0.46 (padding) + 0.333 (half height) ≈ 7.09 → YUp ≈ -7.09.
        Assert.True(adjHairpins[0].YUp < -5.2,
            $"Hairpin YUp ({adjHairpins[0].YUp:F2}) should be pushed below original -5.2");
        Assert.True(adjHairpins[0].YUp <= -7.0,
            $"Hairpin YUp ({adjHairpins[0].YUp:F2}) should clear dynamic bottom + padding");
    }

    [Fact]
    public void HairpinNotAdjustedWhenNoOverlap()
    {
        // Hairpin and dynamic at different X positions should not interact
        var systems = CreateSingleSystem();

        // Dynamic at X=10, device Y=6.0 → YUp = ToUp(6, 2) = −4.0.
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 10, YUp: -4.0, Text: "p", SourcePosition: 0));

        // Hairpin at X=[40, 55] — far from dynamic
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 1, StartX: 40, EndX: 55,
                YUp: -5.2, StartOpening: 0, EndOpening: 0.333,
                Direction: HairpinDirection.Crescendo, SourcePosition: 0));

        var (_, adjHairpins, _, _) = OutsideStaffStacker.StackBelowStaff(ScoreTextMetrics.Bundled, 
            systems, dynamics, hairpins);

        // No X overlap, so hairpin Y should stay at StaffBottom + padding + halfHeight
        // (or its original value if that's larger)
        Assert.True(adjHairpins[0].YUp >= -5.2 - 0.5,
            $"Hairpin YUp ({adjHairpins[0].YUp:F2}) should not be significantly pushed when no overlap");
    }

    // (Former TextSpannerStacksBelowDynamicAndHairpin removed: text spanners now
    // stack ABOVE the staff via StackAboveStaff — LilyPond TextSpanner direction=UP
    // — covered by the SVG snapshot fixtures.)

    private static double PlacedCustomTextBaseline(
        ImmutableArray<SystemLayout> systems, params string[] texts)
    {
        var layouts = texts
            .Select(t => new CustomTextLayout(MeasureIndex: 0, X: 20, YUp: -4.0, Text: t,
                SourcePosition: 0))
            .ToImmutableArray();
        var (_, _, _, adjTexts, _, _, _, _, _) = OutsideStaffStacker.StackAboveStaff(ScoreTextMetrics.Bundled, 
            systems, systemSkylines: null,
            ImmutableArray<TupletBracketLayout>.Empty,
            ImmutableArray<TrillSpannerLayout>.Empty,
            ImmutableArray<BarNumberLayout>.Empty,
            ImmutableArray<OttavaBracketLayout>.Empty,
            layouts,
            ImmutableArray<VoltaBracketLayout>.Empty,
            ImmutableArray<MusicMarkLayout>.Empty);
        return adjTexts[^1].YUp;
    }

    [Fact]
    public void CustomTextDescent_ComesFromTheStringsOwnInk_NotAClassConstant()
    {
        // LILYPOND-REF: scm/define-grobs.scm:3800-3833 TextScript (the outside-staff-priority
        // block) — its Y-extent comes from the stencil, so the clearance under
        // the baseline is the STRING's own descent. Measured
        // (audit/lp-geometry/probes/textscript-ink.ly): a descender string's baseline
        // rides its own ink. The letter-class trio this rule replaced (a flat
        // 0.25 em descent) prices every string identically and reads 0 here — this is
        // the perturbation form of the ledger pair TXD/TXP, kept at model level so it
        // survives fixture edits.
        //
        // Each string is stacked as the SECOND text over the same first text, so both
        // sit on the EDGE constraint (frontier + 0.46 + own descent) with a frontier
        // high enough that the staff-padding refpoint floor cannot bind — over the bare
        // staff the no-descender string sits ON that floor instead, which is the
        // separate claim CustomTextWithoutDescender_SitsOnTheStaffPaddingFloor pins.
        var systems = CreateSingleSystem();

        double dolce = PlacedCustomTextBaseline(systems, "MMM", "dolce");
        double poco = PlacedCustomTextBaseline(systems, "MMM", "poco");

        double fs = LilySharp.Core.Svg.EngravingDefaults.TextScriptFontSize;
        double expected =
            LilySharp.Core.Rendering.TextFontMetrics.Ink(
                "dolce", fs, sans: false, LilySharp.Core.Rendering.FontStyle.Italic).Bottom
            - LilySharp.Core.Rendering.TextFontMetrics.Ink(
                "poco", fs, sans: false, LilySharp.Core.Rendering.FontStyle.Italic).Bottom;

        // The descender string's baseline sits higher by exactly the two inks' descent
        // difference — about 0.41 ss at em 2.2, and never 0.
        Assert.Equal(expected, poco - dolce, 6);
        Assert.True(poco - dolce > 0.3,
            $"a descender must lift the baseline (measured {poco - dolce:F6})");
    }

    [Fact]
    public void CustomTextWithoutDescender_SitsOnTheStaffPaddingFloor()
    {
        // LILYPOND-REF: lily/side-position-interface.cc:401-453 aligned_side — "Ensure
        // 'staff-padding' from my refpoint to the staff", with TextScript's
        // staff-padding 0.5 (scm/define-grobs.scm). Over a bare staff a no-descender
        // string's edge constraint (staff edge + 0.46 + descent ≈ 0.03) loses to the
        // floor, so the BASELINE lands exactly at staff ink edge + 0.5 — LilyPond's
        // TXD reads 2.550000 (ledger textscript.no-descender.staff-to-baseline, whose
        // whole residual was this floor's absence). In this harness no skyline seeds
        // the staff line's ink, so the edge constraint is even lower and the floor
        // binds a fortiori; the returned YUp is measured from the staff MIDDLE.
        var systems = CreateSingleSystem();

        double dolce = PlacedCustomTextBaseline(systems, "dolce");

        double floor = 2.0 + LilySharp.Core.Svg.EngravingDefaults.StaffLineThickness / 2.0
            + 0.5; // staff ink edge + TextScript staff-padding, both LilyPond's numbers
        Assert.Equal(floor, dolce, 6);
    }

    [Fact]
    public void EmptyInputsReturnUnchanged()
    {
        var systems = CreateSingleSystem();
        var emptyDyn = ImmutableArray<DynamicLayout>.Empty;
        var emptyHp = ImmutableArray<HairpinLayout>.Empty;
        var (d, h, _, _) = OutsideStaffStacker.StackBelowStaff(ScoreTextMetrics.Bundled, systems, emptyDyn, emptyHp);

        Assert.True(d.IsEmpty);
        Assert.True(h.IsEmpty);
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

        // Dynamic in system 0 at X=20, device Y=8.0 → YUp = ToUp(8, 2) = −6.0.
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 20, YUp: -6.0, Text: "ff", SourcePosition: 0));

        // Hairpin in system 1 at X=[20, 30] — same X but different system
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 1, StartX: 20, EndX: 30,
                YUp: -5.2, StartOpening: 0, EndOpening: 0.333,
                Direction: HairpinDirection.Crescendo, SourcePosition: 0));

        var (_, adjHairpins, _, _) = OutsideStaffStacker.StackBelowStaff(ScoreTextMetrics.Bundled, 
            systems, dynamics, hairpins);

        // Hairpin in system 1 should NOT be affected by dynamic in system 0
        Assert.True(adjHairpins[0].YUp >= -5.2 - 0.5,
            $"Hairpin in different system should not be pushed: YUp={adjHairpins[0].YUp:F2}");
    }
}
