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

    /// <summary>
    /// The breaker prices a system in LilyPond's PURE frame: the outer staves' refpoints as
    /// placed, the last one taken back up by the pairs' squeeze, and the body at that minimum
    /// — not the drawn body with a nominal half staff at each end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:562 fill_line_details — refpoint_extent_ is
    /// pure_refpoint_extent, the outer spaceable staves at get_pure_minimum_translations.
    /// The numbers are a staff-plus-tab system's (audit/lp-geometry
    /// page.staff-tab.compressed.staves-on-first-page): body 14 = 2 + 9 + 3 as drawn, the
    /// tab's refpoint 3.0 above its outer string, the pair at basic 9 over a minimum of 8.
    /// Poisoned by dropping either term of the frame, the nominal pair (−2, −12) and body 14
    /// come back — the reading that turned a page LilyPond fills.
    /// </remarks>
    [Fact]
    public void BuildSystemDetails_PricesTheBodyAndRefpointsInThePureFrame()
    {
        var layouter = new PageLayouter(LayoutOptions.Default);
        var frame = new BreakerRefpointFrame(ToFirst: 2.0, ToLastAtMinimum: 10.0, StaffCompression: 1.0);

        var d = layouter.BuildSystemDetails(
            1, staffHeight: 14.0, topExtent: 2.311, bottomExtent: 0.54,
            shape: null, BreakPermission.Allow, frame);

        Assert.Equal(13.0, d.StaffHeight, 9);            // 14 drawn, pair squeezed 9 → 8
        Assert.Equal(1.0, d.StaffCompression, 9);
        Assert.Equal(2.311 + 13.0 + 0.54, d.Height, 9);
        Assert.Equal(-2.0, d.RefpointExtentUp, 9);
        Assert.Equal(-10.0, d.RefpointExtentDown, 9);   // 2 + 8 to the tab's middle string
        Assert.Equal(d.Height + LayoutOptions.Default.VerticalSpacing.SystemSystem.BasicDistance,
            d.InverseHooke, 9);

        // Without a frame the nominal five-line reading stands, as every hand-built detail
        // in PageBreakerTests assumes.
        var nominal = layouter.BuildSystemDetails(
            1, staffHeight: 14.0, topExtent: 2.311, bottomExtent: 0.54, shape: null, BreakPermission.Allow);
        Assert.Equal(14.0, nominal.StaffHeight, 9);
        Assert.Equal(0.0, nominal.StaffCompression, 9);
        Assert.Equal(-2.0, nominal.RefpointExtentUp, 9);
        Assert.Equal(-12.0, nominal.RefpointExtentDown, 9);
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

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, header: new HeaderBand(3, null, null), extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        Assert.Equal(3, pageSystems.Length);

        // Gap between system 0 and 1 should differ from gap between system 1 and 2
        // because extents differ
        // system.Y is page Y-up (W2-core): the earlier (upper) system has the LARGER
        // Y, so the inter-system distance is the previous-minus-next difference.
        double gap01 = pageSystems[0].Y - pageSystems[1].Y;
        double gap12 = pageSystems[1].Y - pageSystems[2].Y;

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

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, header: null, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        double gap = pageSystems[0].Y - pageSystems[1].Y; // Y-up (W2-core): upper system has larger Y

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

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, header: null, extents);

        Assert.Single(pages);
        var pageSystems = pages[0].Systems;
        double gap = pageSystems[0].Y - pageSystems[1].Y; // Y-up (W2-core): upper system has larger Y

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

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, header: new HeaderBand(3, null, null), extents);

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

        Assert.Equal(6, p.TopSystem.BasicDistance);
        Assert.Equal(1, p.TopSystem.Padding);

        Assert.Equal(1, p.LastBottom.BasicDistance);
        Assert.Equal(30, p.LastBottom.Stretchability);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_FirstOnPage_ReturnsTopSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: true,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.TopSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_SystemAfterTitle_ReturnsMarkupSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false,
            prevIsTitle: true, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.MarkupSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_NewScore_ReturnsScoreSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: true);

        Assert.Equal(p.ScoreSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_NormalSystems_ReturnsSystemSystem()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false,
            prevIsTitle: false, currentIsTitle: false,
            currentIsNewScore: false);

        Assert.Equal(p.SystemSystem.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_TitleAfterSystem_ReturnsScoreMarkup()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false,
            prevIsTitle: false, currentIsTitle: true,
            currentIsNewScore: false);

        Assert.Equal(p.ScoreMarkup.BasicDistance, spec.BasicDistance);
    }

    [Fact]
    public void VerticalSpacing_SelectSpec_TitleAfterTitle_ReturnsMarkupMarkup()
    {
        var p = VerticalSpacingParameters.Default;

        var spec = p.SelectSpec(
            isFirstOnPage: false,
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
            // Ragged: these tests observe the NATURAL gaps; justification would
            // stretch every configuration to the same page-filling total.
            PageBreaking = new PageBreakingParameters { RaggedBottom = true }
        };
        var layouter = new PageLayouter(options);
        var systems = CreateDummySystems(2);

        // Scalar extents: large bottomExtent on system 0, large upExtent on system 1
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 8.0),
            (upExtent: 8.0, downExtent: 1.0));

        // Skylines: the tall parts are at different X positions, so they don't collide.
        // FromBox's internal height comes from -yBottom for a DOWN building and from
        // +yTop for an UP building, so the extent must go in THAT slot (a DOWN protrusion
        // is a NEGATIVE Y-up bottom, an UP protrusion a POSITIVE Y-up top) — putting it in
        // the other one collapses every building to height 0 and makes this test vacuous.
        // System 0 DOWN: tall protrusion at X=[0,10], short elsewhere
        var sys0Down = new VerticalSkyline(VerticalDirection.Down);
        sys0Down.Merge(VerticalSkyline.FromBox(0, 10, -8.0, 0, VerticalDirection.Down));   // tall (h=8) at left
        sys0Down.Merge(VerticalSkyline.FromBox(10, 70, -2.0, 0, VerticalDirection.Down));  // short (h=2) at right

        // System 1 UP: tall protrusion at X=[60,70], short elsewhere
        var sys1Up = new VerticalSkyline(VerticalDirection.Up);
        sys1Up.Merge(VerticalSkyline.FromBox(0, 60, 0, 2.0, VerticalDirection.Up));      // short (h=2) at left
        sys1Up.Merge(VerticalSkyline.FromBox(60, 70, 0, 8.0, VerticalDirection.Up));     // tall (h=8) at right

        var skylines = ImmutableArray.Create(
            (up: new VerticalSkyline(VerticalDirection.Up), down: sys0Down),
            (up: sys1Up, down: new VerticalSkyline(VerticalDirection.Down)));

        var pagesWithSkylines = layouter.CreatePagesWithOptimalBreaking(systems, null, extents, skylines);
        var pagesWithoutSkylines = layouter.CreatePagesWithOptimalBreaking(systems, null, extents);

        // Y-up (W2-core): upper system has the larger Y, so distance = prev − next.
        double gapWithSkylines = pagesWithSkylines[0].Systems[0].Y - pagesWithSkylines[0].Systems[1].Y;
        double gapWithoutSkylines = pagesWithoutSkylines[0].Systems[0].Y - pagesWithoutSkylines[0].Systems[1].Y;

        // The tall parts don't overlap in X, so the per-X skyline distance is STRICTLY
        // smaller than the scalar worst case (which stacks max-down against max-up), and
        // the systems pack closer together. Strict inequality guards against the whole
        // skyline collapsing to zero (which would make the comparison vacuous).
        Assert.True(gapWithSkylines < gapWithoutSkylines,
            $"Skyline gap ({gapWithSkylines:F2}) should be < scalar gap ({gapWithoutSkylines:F2})");
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

        var pagesNull = layouter.CreatePagesWithOptimalBreaking(systems, null, extents, systemSkylines: null);
        var pagesOmitted = layouter.CreatePagesWithOptimalBreaking(systems, null, extents);

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

        var pagesWithEmpty = layouter.CreatePagesWithOptimalBreaking(systems, null, extents, skylines);
        var pagesWithout = layouter.CreatePagesWithOptimalBreaking(systems, null, extents);

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

        var pages = layouter.CreatePagesWithOptimalBreaking(systems, null, extents, skylines);

        double gap = pages[0].Systems[0].Y - pages[0].Systems[1].Y; // Y-up (W2-core): upper system has larger Y
        var spec = options.VerticalSpacing.SystemSystem;
        double minGap = Math.Max(5.0 + 5.0, spec.MinimumDistance) + spec.Padding;

        Assert.True(gap >= minGap - 0.01,
            $"Gap ({gap:F2}) should enforce minimum distance ({minGap:F2})");
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
            // Ragged: these tests observe the NATURAL gaps; justification would
            // stretch every configuration to the same page-filling total.
            PageBreaking = new PageBreakingParameters { RaggedBottom = true }
        };
        var defaultOptions = CreateOptions(pageHeight: 200) with
        {
            // Ragged: these tests observe the NATURAL gaps; justification would
            // stretch every configuration to the same page-filling total.
            PageBreaking = new PageBreakingParameters { RaggedBottom = true }
        };

        var layouterCustom = new PageLayouter(options);
        var layouterDefault = new PageLayouter(defaultOptions);
        var systems = CreateDummySystems(3);
        var extents = ImmutableArray.Create(
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0),
            (upExtent: 1.0, downExtent: 1.0));

        var customPages = layouterCustom.CreatePagesWithOptimalBreaking(systems, null, extents);
        var defaultPages = layouterDefault.CreatePagesWithOptimalBreaking(systems, null, extents);

        // Y-up (W2-core): upper system has the larger Y, so distance = prev − next.
        double customGap = customPages[0].Systems[0].Y - customPages[0].Systems[1].Y;
        double defaultGap = defaultPages[0].Systems[0].Y - defaultPages[0].Systems[1].Y;

        // Custom has smaller basic-distance (8 vs 12), so gap should be smaller
        Assert.True(customGap < defaultGap,
            $"Custom gap ({customGap:F2}) should be < default ({defaultGap:F2})");
    }
}
