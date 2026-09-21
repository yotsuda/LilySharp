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
/// Where a system's SPRUNG STAVES land once the page has solved its chain — asserted against
/// the page's own solution, not by comparing one render to another.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:896-914 <c>find_system_offsets</c> — one solution
/// entry per spaceable staff, and each staff is translated by ITS OWN entry.
/// <para>
/// ⚠️ WHY THIS FILE EXISTS. <c>PageLayouter.RespaceStaves</c> holds the one shift in locals and
/// spills into a <c>Dictionary</c> only when a SECOND distinct staff index arrives — the arm a
/// three-staff system takes. Session 450 poisoned that arm (the Dictionary is built without the
/// FIRST spring's entry), predicted RED, and got GREEN. Session 453 counted the arm instead of
/// re-reading it: over one suite run it is BUILT 1,949 times and READ 19 times, and in 17 of
/// those the first spring's shift is not zero — so the arm runs, and the poison really does
/// move staves. What was missing was an OBSERVER. The only tests that reach it are
/// <c>VocabularyPerturbationTests.EveryPaperKeyMovesThePage</c> and
/// <c>EverySpacingSubKeyMovesThePage</c>, and both assert only that two books DIFFER
/// (<c>Signature(a) != Signature(b)</c>): a poison that moves both sides by the same amount
/// leaves that inequality standing. A relative net can never see a systematic error.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PageStaffRespacingTests
{
    private const double StaffHeight = 4;

    /// <summary>Refpoint to refpoint, as the alignment placed the staves.</summary>
    private const double LaidOutGap = 10;

    /// <summary>A staff's refpoint, the way <c>MultiStaffLayouter.StaffRefpoint</c> reads it.</summary>
    private static double Refpoint(StaffLayout staff) => staff.Y - staff.Height / 2.0;

    /// <summary>
    /// One system of THREE spaceable staves — two springs, which is what sends
    /// <c>RespaceStaves</c> down the many-case.
    /// </summary>
    private static ImmutableArray<SystemLayout> ThreeStaffSystem()
    {
        var staves = ImmutableArray.Create(
            new StaffLayout(0, ClefType.Treble, Y: 0, Height: StaffHeight),
            new StaffLayout(1, ClefType.Bass, Y: -LaidOutGap, Height: StaffHeight),
            new StaffLayout(2, ClefType.Treble, Y: -2 * LaidOutGap, Height: StaffHeight));
        var group = new StaffGroupLayout(
            StaffGroupType.StaffGroup, staves,
            Y: 0, Height: 2 * LaidOutGap + StaffHeight, GrandStaffLayout: null);

        // The two springs are IDENTICAL, so one force solves them to the same length. That
        // equality is what the assertion below reads: it is a property of the solution, not a
        // number copied out of a run.
        var spec = new VerticalSpacingSpec
        {
            BasicDistance = LaidOutGap,
            MinimumDistance = 8,
            Padding = 0,
            Stretchability = 60,
        };
        var springs = ImmutableArray.Create(
            new StaffSpring(0, 1, spec, LaidOutGap),
            new StaffSpring(1, 2, spec, LaidOutGap));

        return ImmutableArray.Create(new SystemLayout(
            SystemIndex: 0,
            Y: 0,
            Width: 76,
            PrefixWidth: 5,
            Measures: ImmutableArray<MeasureLayout>.Empty,
            StaffGroups: ImmutableArray.Create(group),
            Indent: 0,
            StaffSprings: springs));
    }

    /// <summary>
    /// Every sprung staff lands at the distance the page solved for it — the FIRST one too.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>RaggedLastBottom</c> IS TURNED OFF, and without that this test asserts nothing.
    /// It defaults to TRUE (LilyPond's own default, ly/paper-defaults-init.ly:56), a one-page
    /// book IS its last page, and a ragged page keeps its laid-out spacing — so every spring
    /// would solve to the distance it came in at, <c>allStill</c> would fire, and
    /// <c>RespaceStaves</c> would return before reaching the arm under test. The same trap
    /// <c>VocabularyPerturbationTests.FilledPageBook</c> names for the paper keys.
    /// </remarks>
    [Fact]
    public void ThreeSprungStaves_EachLandAtTheDistanceThePageSolved()
    {
        var options = new LayoutOptions
        {
            PageHeight = 100,
            PageWidth = 80,
            MarginTop = 5,
            MarginBottom = 5,
            StaffHeight = StaffHeight,
            SystemSpacing = 8,
            UseOptimalPageBreaking = true,
            PageBreaking = PageBreakingParameters.Default with { RaggedLastBottom = false },
        };
        var pages = new PageLayouter(options).CreatePagesWithOptimalBreaking(
            ThreeStaffSystem(),
            header: null,
            ImmutableArray.Create((upExtent: 1.0, downExtent: 1.0)));

        var placed = Assert.Single(Assert.Single(pages).Systems).StaffGroups[0].Staves;
        Assert.Equal(3, placed.Length);
        double gap01 = Refpoint(placed[0]) - Refpoint(placed[1]);
        double gap12 = Refpoint(placed[1]) - Refpoint(placed[2]);

        // ⑴ The page had room and the chain took it. Without this the test passes on a page
        //    that never respaced anything, which is the vacuous reading of ⑵.
        Assert.True(gap01 > LaidOutGap + 1e-6,
            $"the page did not stretch its staff springs: gap01 {gap01:F6} is still the "
            + $"laid-out {LaidOutGap:F6} — the fixture, not the write-back, is what this "
            + "test would then be measuring.");

        // ⑵ Two identical springs at one force are the same length. The first staff's own
        //    shift has to be written back for that to hold; drop it and the first pair keeps
        //    its laid-out gap while the second absorbs the whole stretch.
        Assert.Equal(gap01, gap12, 9);
    }
}
