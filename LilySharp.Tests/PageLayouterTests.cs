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

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class PageLayouterTests
{
    private static LayoutOptions CreateOptions(
        double pageHeight = 100,
        double pageWidth = 80,
        double marginTop = 5,
        double marginBottom = 5,
        double staffHeight = 4,
        double systemSpacing = 8,
        double topSystemPadding = 1)
    {
        return new LayoutOptions
        {
            PageHeight = pageHeight,
            PageWidth = pageWidth,
            MarginTop = marginTop,
            MarginBottom = marginBottom,
            StaffHeight = staffHeight,
            SystemSpacing = systemSpacing,
            TopSystemPadding = topSystemPadding,
            UseOptimalPageBreaking = true
        };
    }

    private static ImmutableArray<SystemLayout> CreateDummySystems(int count)
    {
        var builder = ImmutableArray.CreateBuilder<SystemLayout>(count);
        for (int i = 0; i < count; i++)
        {
            builder.Add(new SystemLayout(
                SystemIndex: i,
                Y: 0, // will be overwritten by PageLayouter
                Width: 76,
                PrefixWidth: 5,
                Measures: ImmutableArray<MeasureLayout>.Empty));
        }
        return builder.ToImmutable();
    }

    [Fact]
    public void PerSystemExtents_ProduceDifferentYPositions()
    {
        // Three systems with different skyline extents should get different spacing
        var options = CreateOptions(pageHeight: 200);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(3);

        // System 0: small extents, System 1: large down, System 2: large up
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 5.0),
            (upExtent: 4.0, downExtent: 1.0));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 3, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        Assert.Equal(3, pageSystems.Length);

        // Gap between system 0 and 1 should differ from gap between system 1 and 2
        // because extents differ
        double gap01 = pageSystems[1].Y - pageSystems[0].Y;
        double gap12 = pageSystems[2].Y - pageSystems[1].Y;

        // System 0 has small downExtent (1), system 1 has small upExtent (1)
        // System 1 has large downExtent (5), system 2 has large upExtent (4)
        // So gap12 should be larger than gap01
        Assert.True(gap12 > gap01,
            $"Expected gap12 ({gap12:F2}) > gap01 ({gap01:F2}) due to larger extents");
    }

    [Fact]
    public void ForceDistribution_StretchesSpacing()
    {
        // Two small systems on a large page should have spacing larger than minimum
        // Disable ragged-last-bottom to allow vertical justification
        var options = CreateOptions(pageHeight: 200, staffHeight: 4, systemSpacing: 8) with
        {
            PageBreaking = new PageBreakingParameters { RaggedLastBottom = false }
        };
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 0, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        double gap = pageSystems[1].Y - pageSystems[0].Y;

        // Minimum distance: skyline + padding
        // skylineDist = staffHeight + bottomExtent + nextTopExtent = 4 + 1 + 1 = 6
        // minDist = max(6, 8) + 1 = 9 (min-distance=8 from system-system default)
        // basicDist = max(12, 9) = 12
        // With positive force the gap should be larger than basicDist
        var spec = options.VerticalSpacing.SystemSystem;
        double skylineDist = options.StaffHeight + 1.0 + 1.0;
        double minGap = Math.Max(Math.Max(skylineDist, spec.MinimumDistance) + spec.Padding, spec.BasicDistance);
        Assert.True(gap > minGap,
            $"Expected gap ({gap:F2}) > minimum ({minGap:F2}) due to force stretching");
    }

    [Fact]
    public void OverfullPage_ClampsForce_MaintainsMinSpacing()
    {
        // Systems barely fit on page — force should be clamped to 0 (no compression)
        var options = CreateOptions(pageHeight: 60, staffHeight: 10, systemSpacing: 4, marginTop: 3, marginBottom: 3);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        // Large extents to fill the page
        var extents = ImmutableArray.Create(
            (upExtent: 3.0, downExtent: 6.0),
            (upExtent: 3.0, downExtent: 6.0));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 0, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        double gap = pageSystems[1].Y - pageSystems[0].Y;

        // Minimum gap: skylineDistance + padding
        // skylineDistance = staffHeight + bottomExtent + nextTopExtent = 10 + 6 + 3 = 19
        // minDistance = max(skylineDistance, spec.MinimumDistance) + spec.Padding = max(19, 8) + 1 = 20
        double skylineDist = options.StaffHeight + 6.0 + 3.0;
        double vs = options.VerticalSpacing.SystemSystem.MinimumDistance;
        double expectedMinGap = Math.Max(skylineDist, vs) + options.VerticalSpacing.SystemSystem.Padding;
        Assert.True(gap >= expectedMinGap - 0.01,
            $"Expected gap ({gap:F2}) >= minimum ({expectedMinGap:F2}), systems should not overlap");
    }

    [Fact]
    public void EmptySystems_ReturnsEmpty()
    {
        var options = CreateOptions();
        var layouter = new PageLayouter(options);
        var systems = ImmutableArray<SystemLayout>.Empty;
        var extents = ImmutableArray<(double, double)>.Empty;

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, headerHeight: 3, extents);

        Assert.Empty(pages);
    }

    // --- VerticalSpacingParameters tests ---

    [Fact]
    public void VerticalSpacingParameters_Default_MatchesLilyPond()
    {
        var p = VerticalSpacingParameters.Default;

        // LILYPOND-REF: ly/paper-defaults-init.ly:64-89
        Assert.Equal(12, p.SystemSystem.BasicDistance);
        Assert.Equal(8, p.SystemSystem.MinimumDistance);
        Assert.Equal(1, p.SystemSystem.Padding);
        Assert.Equal(60, p.SystemSystem.Stretchability);

        Assert.Equal(14, p.ScoreSystem.BasicDistance);
        Assert.Equal(120, p.ScoreSystem.Stretchability);

        Assert.Equal(5, p.MarkupSystem.BasicDistance);
        Assert.Equal(0.5, p.MarkupSystem.Padding);

        Assert.Equal(1, p.TopSystem.BasicDistance);
        Assert.Equal(1, p.TopSystem.Padding);

        Assert.Equal(1, p.LastBottom.BasicDistance);
        Assert.Equal(30, p.LastBottom.Stretchability);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_FirstOnPage_ReturnsTopSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: true, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.TopSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_SystemAfterTitle_ReturnsMarkupSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: true, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.MarkupSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_NewScore_ReturnsScoreSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: true);

        Assert.Equal(p.ScoreSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_NormalSystems_ReturnsSystemSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.SystemSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_TitleAfterSystem_ReturnsScoreMarkup()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: false, currentIsTitle: true,
            currentIsNewScore: false);

        Assert.Equal(p.ScoreMarkup.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_TitleAfterTitle_ReturnsMarkupMarkup()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false, isLastOnPage: false,
            prevIsTitle: true, currentIsTitle: true,
            currentIsNewScore: false);

        Assert.Equal(p.MarkupMarkup.BasicDistance, spec.BasicDistance);
    }

    // --- System skyline tests (G-1: build_system_skyline) ---

    [Fact]
    public void SystemSkylines_ProduceDifferentSpacingThanScalarExtents()
    {
        // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
        // When skylines have a narrow tall protrusion, Distance() should give a
        // different (potentially smaller) result than worst-case scalar extents.
        var options = CreateOptions(pageHeight: 200) with
        {
            PageBreaking = new PageBreakingParameters { RaggedLastBottom = false }
        };
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        // Scalar extents: large bottomExtent on system 0, large upExtent on system 1
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 8.0),
            (upExtent: 8.0, downExtent: 1.0));

        // Skylines: the tall parts are at different X positions, so they don't collide
        // System 0 DOWN: tall protrusion at X=[0,10], short elsewhere
        var sys0Down = new VerticalSkyline(VerticalDirection.Down);
        sys0Down.Merge(VerticalSkyline.FromBox(0, 10, 0, 8.0, VerticalDirection.Down));   // tall at left
        sys0Down.Merge(VerticalSkyline.FromBox(10, 70, 0, 2.0, VerticalDirection.Down));  // short at right

        // System 1 UP: tall protrusion at X=[60,70], short elsewhere
        var sys1Up = new VerticalSkyline(VerticalDirection.Up);
        sys1Up.Merge(VerticalSkyline.FromBox(0, 60, -2.0, 0, VerticalDirection.Up));      // short at left
        sys1Up.Merge(VerticalSkyline.FromBox(60, 70, -8.0, 0, VerticalDirection.Up));     // tall at right

        var skylines = ImmutableArray.Create(
            (up: new VerticalSkyline(VerticalDirection.Up), down: sys0Down),
            (up: sys1Up, down: new VerticalSkyline(VerticalDirection.Down)));

        var pagesWithSkylines = layouter.CreatePagesWithOptimalBreaking(systems, 0, extents, skylines);
        var pagesWithoutSkylines = layouter.CreatePagesWithOptimalBreaking(systems, 0, extents);

        double gapWithSkylines = pagesWithSkylines[0].Systems[1].Y - pagesWithSkylines[0].Systems[0].Y;
        double gapWithoutSkylines = pagesWithoutSkylines[0].Systems[1].Y - pagesWithoutSkylines[0].Systems[0].Y;

        // Skyline distance should be smaller because tall parts don't overlap in X
        // This means systems can be placed closer together
        Assert.True(gapWithSkylines <= gapWithoutSkylines,
            $"Skyline gap ({gapWithSkylines:F2}) should be <= scalar gap ({gapWithoutSkylines:F2})");
    }

    [Fact]
    public void SystemSkylines_NullFallsBackToScalarExtents()
    {
        // When systemSkylines is null, behavior should match the original scalar-only path
        var options = CreateOptions(pageHeight: 200);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        var extents = ImmutableArray.Create(
            (upExtent: 2.0, downExtent: 3.0),
            (upExtent: 2.0, downExtent: 3.0));

        var pagesNull = layouter.CreatePagesWithOptimalBreaking(systems, 0, extents, systemSkylines: null);
        var pagesOmitted = layouter.CreatePagesWithOptimalBreaking(systems, 0, extents);

        Assert.Equal(pagesNull[0].Systems[0].Y, pagesOmitted[0].Systems[0].Y, 3);
        Assert.Equal(pagesNull[0].Systems[1].Y, pagesOmitted[0].Systems[1].Y, 3);
    }

    [Fact]
    public void SystemSkylines_EmptySkylinesFallBackToScalar()
    {
        // LILYPOND-REF: lily/skyline.cc:529-533 Distance() returns -inf for empty skylines
        // PageLayouter should fall back to scalar when Distance() is -inf
        var options = CreateOptions(pageHeight: 200);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        var extents = ImmutableArray.Create(
            (upExtent: 2.0, downExtent: 3.0),
            (upExtent: 2.0, downExtent: 3.0));

        // Empty skylines — Distance() will return negative infinity
        var skylines = ImmutableArray.Create(
            (up: new VerticalSkyline(VerticalDirection.Up), down: new VerticalSkyline(VerticalDirection.Down)),
            (up: new VerticalSkyline(VerticalDirection.Up), down: new VerticalSkyline(VerticalDirection.Down)));

        var pagesWithEmpty = layouter.CreatePagesWithOptimalBreaking(systems, 0, extents, skylines);
        var pagesWithout = layouter.CreatePagesWithOptimalBreaking(systems, 0, extents);

        // Should produce same positioning as scalar-only
        Assert.Equal(pagesWithout[0].Systems[0].Y, pagesWithEmpty[0].Systems[0].Y, 3);
        Assert.Equal(pagesWithout[0].Systems[1].Y, pagesWithEmpty[0].Systems[1].Y, 3);
    }

    [Fact]
    public void SystemSkylines_CollidingProfilesEnforceMinDistance()
    {
        // When skyline profiles fully overlap in X, Distance() should produce
        // a result >= the scalar extent calculation
        var options = CreateOptions(pageHeight: 200, staffHeight: 4);
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 5.0),
            (upExtent: 5.0, downExtent: 1.0));

        // Full-width boxes matching the scalar extents
        var sys0Down = VerticalSkyline.FromBox(0, 70, 0, 5.0, VerticalDirection.Down);
        var sys1Up = VerticalSkyline.FromBox(0, 70, -5.0, 0, VerticalDirection.Up);

        var skylines = ImmutableArray.Create(
            (up: new VerticalSkyline(VerticalDirection.Up), down: sys0Down),
            (up: sys1Up, down: new VerticalSkyline(VerticalDirection.Down)));

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, 0, extents, skylines);

        double gap = pages[0].Systems[1].Y - pages[0].Systems[0].Y;
        var spec = options.VerticalSpacing.SystemSystem;
        double minGap = Math.Max(5.0 + 5.0, spec.MinimumDistance) + spec.Padding;

        Assert.True(gap >= minGap - 0.01,
            $"Gap ({gap:F2}) should enforce minimum distance ({minGap:F2})");
    }

    // --- In-note-system-padding tests (page-layout-problem.cc:483) ---

    [Fact]
    public void InNoteSystemPadding_DefaultValue_Is1Point5()
    {
        // LILYPOND-REF: ly/paper-defaults-init.ly — default 1.5 staff spaces
        var p = VerticalSpacingParameters.Default;
        Assert.Equal(1.5, p.InNoteSystemPadding);
    }

    [Fact]
    public void InNoteSystemPadding_IncreasesMinDistance_WhenSkylineDistancePlusPaddingExceedsSpec()
    {
        // LILYPOND-REF: lily/page-layout-problem.cc:483 in-note-system-padding
        // When skylineDistance + InNoteSystemPadding > spec-based minDistance,
        // the note padding should enforce a larger inter-system gap.
        var vsWithPadding = new VerticalSpacingParameters { InNoteSystemPadding = 10 };
        var vsWithout = new VerticalSpacingParameters { InNoteSystemPadding = 0 };

        var optionsWith = CreateOptions(pageHeight: 200) with { VerticalSpacing = vsWithPadding };
        var optionsWithout = CreateOptions(pageHeight: 200) with { VerticalSpacing = vsWithout };

        var systems = CreateDummySystems(2);
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var pagesWith = new PageLayouter(optionsWith)
            .CreatePagesWithOptimalBreaking(systems, 0, extents);
        var pagesWithout = new PageLayouter(optionsWithout)
            .CreatePagesWithOptimalBreaking(systems, 0, extents);

        double gapWith = pagesWith[0].Systems[1].Y - pagesWith[0].Systems[0].Y;
        double gapWithout = pagesWithout[0].Systems[1].Y - pagesWithout[0].Systems[0].Y;

        // skylineDistance = staffHeight + 1 + 1 = 6
        // With padding=10: noteDistance = 6+10 = 16, which exceeds spec minDistance (max(6,8)+1=9)
        // Without padding=0: noteDistance = 6+0 = 6, below spec minDistance
        Assert.True(gapWith > gapWithout,
            $"Gap with InNoteSystemPadding=10 ({gapWith:F2}) should be > without ({gapWithout:F2})");
    }

    [Fact]
    public void InNoteSystemPadding_NoEffect_WhenSpecMinDistanceAlreadyLarger()
    {
        // When spec-based minimum distance already exceeds skylineDistance + InNoteSystemPadding,
        // the note padding has no effect
        var vsSmall = new VerticalSpacingParameters { InNoteSystemPadding = 0.5 };
        var vsNone = new VerticalSpacingParameters { InNoteSystemPadding = 0 };

        // staffHeight=4, extents=(1,1): skylineDistance = 4+1+1 = 6
        // noteDistance = 6+0.5 = 6.5
        // spec minDistance = max(6, 8) + 1 = 9 → 9 > 6.5, so padding doesn't affect
        var optionsSmall = CreateOptions(pageHeight: 200) with { VerticalSpacing = vsSmall };
        var optionsNone = CreateOptions(pageHeight: 200) with { VerticalSpacing = vsNone };

        var systems = CreateDummySystems(2);
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var pagesSmall = new PageLayouter(optionsSmall)
            .CreatePagesWithOptimalBreaking(systems, 0, extents);
        var pagesNone = new PageLayouter(optionsNone)
            .CreatePagesWithOptimalBreaking(systems, 0, extents);

        double gapSmall = pagesSmall[0].Systems[1].Y - pagesSmall[0].Systems[0].Y;
        double gapNone = pagesNone[0].Systems[1].Y - pagesNone[0].Systems[0].Y;

        Assert.Equal(gapSmall, gapNone, 3);
    }

    [Fact]
    public void VerticalSpacing_CustomParameters_AffectSpacing()
    {
        // Tighter system-system spacing
        var customVs = new VerticalSpacingParameters
        {
            SystemSystem = new VerticalSpacingSpec
            {
                BasicDistance = 8,
                MinimumDistance = 6,
                Padding = 0.5,
                Stretchability = 30
            }
        };
        var options = CreateOptions(pageHeight: 200) with
        {
            VerticalSpacing = customVs,
            PageBreaking = new PageBreakingParameters { RaggedLastBottom = false }
        };
        var defaultOptions = CreateOptions(pageHeight: 200) with
        {
            PageBreaking = new PageBreakingParameters { RaggedLastBottom = false }
        };

        var layouterCustom = new PageLayouter(options);
        var layouterDefault = new PageLayouter(defaultOptions);
        var systems = CreateDummySystems(3);
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var customPages = layouterCustom.CreatePagesWithOptimalBreaking(systems, 0, extents);
        var defaultPages = layouterDefault.CreatePagesWithOptimalBreaking(systems, 0, extents);

        double customGap = customPages[0].Systems[1].Y - customPages[0].Systems[0].Y;
        double defaultGap = defaultPages[0].Systems[1].Y - defaultPages[0].Systems[0].Y;

        // Custom has smaller basic-distance (8 vs 12), so gap should be smaller
        Assert.True(customGap < defaultGap,
            $"Custom gap ({customGap:F2}) should be < default ({defaultGap:F2})");
    }
}
