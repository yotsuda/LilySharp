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
    /// …and the later pairs — whose upper system has no indent, so the silhouettes really do
    /// face each other — read the SAME floor, because the label's drawn box fits under it.
    /// That is LilyPond's own answer for this shape: the SIFO probes read a flat 12.000000
    /// refpoint-to-refpoint on every pair, and only the tall-mark books (SIF1–SIF3, no Lily#
    /// spelling) read anything else (21.434270).
    /// </summary>
    /// <remarks>
    /// ⚠️ UNTIL 2026-08-27 THIS ARM ASSERTED THE OPPOSITE — "above the floor and unequal
    /// (8.570000 / 8.200000)" — and those numbers were not a silhouette deciding: they were
    /// the paging reservation's flat mark envelope carrying 0.800000 of air over the drawn
    /// box (the ledger's lyrics.chord-row.marked.*.gap-second +0.241073 island, and
    /// system.indent-floor.one-staff's +0.200000). The arm had pinned Lily#'s divergence
    /// from LilyPond as if it were the mechanism. The opposite mistake — flooring so hard
    /// the skyline stops deciding — is watched by the ledger's above-floor pairs
    /// (system.tuplet-bracket-*/slur-*/tie-*/beam-* at 13.1–13.8), which a clamp-to-basic
    /// poison turns red while every arm here stays green.
    /// </remarks>
    [Fact]
    public void TheLaterPairs_ReadTheSameFloor_TheDrawnBoxFitsUnderIt()
    {
        var gaps = StaffLineGeometry.Gaps(Render(2));
        Assert.Equal(LilyPondFloor, gaps[3], 6);
        Assert.Equal(LilyPondFloor, gaps[5], 6);
    }

    /// <summary>
    /// CONTROL: a one-staff score. Its body is four staff spaces, so the origin frame and
    /// the staff frame coincide and the conversion is the identity — which is exactly why
    /// the corpus never caught the frame defect, and why this arm pins the frame's identity
    /// case. Every pair reads the floor: LilyPond's ledgered answer for this book is a flat
    /// 12.000000 (system.indent-floor.one-staff, exact since 2026-08-27).
    /// </summary>
    /// <remarks>
    /// ⚠️ UNTIL 2026-08-27 THIS ARM ASSERTED "above the floor and unequal", and what stood
    /// above the floor was not a silhouette: it was the paging reservation's flat mark
    /// envelope carrying 0.800000 of air over the label's drawn box, i.e. the divergence
    /// the ledger recorded as system.indent-floor.one-staff +0.200000 ("it closes when
    /// that excess does" — it did). The frame poisons (revert / shift the conversion)
    /// still cannot reach this book — with one staff <c>ToLast</c> and <c>ToFirst</c> are
    /// the same offset — and the clamp-to-basic poison is watched by the ledger's
    /// above-floor pairs (system.tuplet-bracket-*/slur-*/tie-*/beam-*), not here.
    /// </remarks>
    [Fact]
    public void ASingleStaffScore_ReadsTheFloorAtEveryPair()
    {
        var gaps = StaffLineGeometry.Gaps(Render(1));
        Assert.Equal(3, gaps.Count);
        foreach (var g in gaps)
            Assert.Equal(LilyPondFloor, g, 6);
    }
}
