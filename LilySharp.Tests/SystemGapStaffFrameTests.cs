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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The pair's <c>system-system-spacing</c> numbers are stated between the PREVIOUS system's
/// last spaceable staff and the next system's first — not between the systems' origins — so a
/// system taller than the numbers themselves must still be floored by them.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: ly/paper-defaults-init.ly:62-65 system-system-spacing — basic-distance 12,
/// minimum-distance 8, padding 1; lily/page-layout-problem.cc:1120-1126 build_system_skyline
/// — first_spaceable_dy / last_spaceable_dy, the conversion that puts those numbers in the
/// staff-to-staff frame.
/// </para>
/// <para>
/// ⚠️ THE DEFECT THIS GUARDS WAS INVISIBLE AT ONE STAFF, WHICH IS WHY 572 BOOKS MISSED IT.
/// <c>CreatePages</c>'s single-page path floored an ORIGIN-to-origin distance by those
/// numbers. A one-staff system's body is 4.000000 and 12.000000 - 4.000000 is the 8.000000
/// LilyPond draws, so the two frames agree there BY ARITHMETIC ACCIDENT. At two staves the
/// body is 13.000000, <c>Math.Max(12, …)</c> can no longer bind, and nothing at all stands
/// under the skyline term.
/// </para>
/// <para>
/// ⚠️ IT TAKES AN INDENT TO SHOW, and that half is NOT a defect: the next system's rehearsal
/// mark and bar number stand in the indent COLUMN, where the first system has no staff and
/// therefore no silhouette, so <c>Distance()</c> finds no obstruction and lets the system
/// rise. LilyPond does exactly the same — MEASURED on 2.26.0 with a tall mark, the first
/// system pair collapses to the floor while the identical A→B pair later in the same score
/// reads 13.934200. What LilyPond does NOT do is fall through the floor.
/// </para>
/// <para>
/// MEASURED on 2.26.0 (audit/lp-geometry/probes/system-indent-floor.ly): the collapsed pair
/// reads 8.000000 between staff lines at ONE, TWO and THREE staves alike — 12.000000 between
/// the two staves' refpoints, with the system body outside the quantity. Lily# read
/// 3.050000 at two and at three staves, and printed the mark's box through the instrument
/// name on the reported book (scratch/ベースタブLy/Untitled-6.lys, user report 2026-08-25).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class SystemGapStaffFrameTests
{
    // The reported book's shape, reduced: one part on N staves, so every system carries an
    // instrument name and therefore an indent, and a section label leads system 2.
    private const string Head = """
        key c major
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | break}
          section B { g'4 g f f | e e d2 | break}
        }
        form main { A B A B }
        """;

    private static string Render(int staffCount)
    {
        var staves = string.Join("\n", Enumerable.Repeat("  staff melody", staffCount));
        var tree = SyntaxTree.Parse($"{Head}\nscore main {{\n{staves}\n}}\n");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    // LilyPond's floor for this pair, MEASURED: basic-distance 12.000000 between the two
    // staves' refpoints, which is 8.000000 between the staff lines that face each other.
    private const double LilyPondFloor = 8.0;

    /// <summary>
    /// The collapsed first pair sits on LilyPond's floor, and the floor does not move when the
    /// system gets taller — which is the whole claim, since the broken frame's answer DID.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TheCollapsedPair_SitsOnLilyPondsFloor_WhateverTheSystemsHeight(int staffCount)
    {
        var gaps = StaffLineGeometry.Gaps(Render(staffCount));
        // Within a system the staves sit 5.000 apart, so the system gaps are every
        // staffCount-th entry: the first is the pair that collapses into the indent column.
        double firstSystemGap = gaps[staffCount - 1];
        Assert.Equal(LilyPondFloor, firstSystemGap, 6);
    }

    /// <summary>
    /// …and the floor is a FLOOR. Where the systems' silhouettes really do face each other —
    /// the later pairs, whose upper system has no indent — the skyline still decides, above it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS ARM IS THE ONE THAT GOES RED FOR THE OPPOSITE MISTAKE. Flooring hard enough to
    /// fix the report is easy; flooring so hard that every pair becomes the constant 8.000000
    /// is a different bug with the same green. The two later pairs are 8.570000 and 8.200000
    /// and they differ from EACH OTHER, which no constant can produce.
    /// </remarks>
    [Fact]
    public void TheLaterPairs_AreStillDecidedByTheirSkylines()
    {
        var gaps = StaffLineGeometry.Gaps(Render(2));
        double second = gaps[3], third = gaps[5];
        Assert.True(second > LilyPondFloor,
            $"the second system pair fell to the floor ({second:F3})");
        Assert.True(third > LilyPondFloor,
            $"the third system pair fell to the floor ({third:F3})");
        Assert.True(System.Math.Abs(second - third) > 1e-3,
            $"both later pairs read {second:F3} — a constant, not a silhouette");
    }

    /// <summary>
    /// CONTROL: a one-staff score is untouched. Its body is four staff spaces, so the origin
    /// frame and the staff frame coincide and the conversion is the identity — which is
    /// exactly why the corpus never caught the defect, and why this arm pins it.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT CAN GO RED, AND NOT ON THE POISON YOU WOULD REACH FOR FIRST (HANDOFF bone 2 — an
    /// arm that cannot go red is not a control). MEASURED, all three poisons:
    /// <list type="bullet">
    /// <item>revert the conversion → arms 1 and 2 red, this one GREEN;</item>
    /// <item>shift the conversion by 0.5 → arms 1 and 2 red, this one GREEN;</item>
    /// <item>clamp every pair to basic-distance → this one and
    ///   <see cref="TheLaterPairs_AreStillDecidedByTheirSkylines"/> red, arms 1 and 2 GREEN.</item>
    /// </list>
    /// The first two cannot reach it and that is a FACT ABOUT THE FIX, not a weakness: with one
    /// staff <c>ToLast</c> and <c>ToFirst</c> are the same offset, so the conversion is
    /// identically zero and no edit to it can move this book by any amount. What this arm
    /// watches is the opposite mistake — flooring hard enough to fix the report is easy, and
    /// flooring so hard that the skyline stops deciding is a different bug with the same green.
    /// ⚠️ The two poisons' red sets are DISJOINT, which is what says the two claims are
    /// independent rather than one claim written twice.
    /// It asserts the SHAPE — both pairs above the floor and unequal — rather than two
    /// literals, because the numbers come from the mark and bar-number metrics and a text
    /// metric is not a constant across platforms.
    /// </remarks>
    [Fact]
    public void ASingleStaffScore_IsDecidedByItsSkylineAtEveryPair()
    {
        var gaps = StaffLineGeometry.Gaps(Render(1));
        Assert.Equal(3, gaps.Count);
        foreach (var g in gaps)
            Assert.True(g > LilyPondFloor,
                $"a one-staff pair fell to the floor ({g:F3}); this score never reached it");
        Assert.True(gaps.Distinct().Count() > 1,
            "every one-staff pair reads the same number — the skyline stopped deciding");
    }
}
