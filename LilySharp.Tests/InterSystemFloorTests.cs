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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The distance between two SYSTEMS is written between their spaceable staves, and what
/// stands between the staves of one system cannot change which staves those are.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:936-939 distribute_loose_lines — its two
/// position arguments are <c>last_spaceable_line_translation</c> and
/// <c>-solution_[spring_idx]</c>, so the inter-system distance runs from the previous
/// system's <c>last_spaceable_line</c> to the next system's first. That line is a fact about
/// the system's ALIGNMENT (:943-944 records it as the walk passes each spaceable staff);
/// nothing there asks how many non-spaceable lines the system happens to hold.
/// <para>
/// ⚠️ THE DEFECT THIS PINS (user report, session 257, `scratch/ベースタブLy/Untitled-6.lys`).
/// <c>MultiStaffLayouter.StaffSprings</c> declined to spring a staff pair when a line
/// between them was not a LYRICS row, so a book written
/// <c>staff / chords / lyrics / staff</c> produced a system with NO staff spring at all —
/// and <c>LayoutEngine.CreatePages</c>' <c>OriginToChainEnd</c> reads an empty spring list
/// as "this system's chain ends at its FIRST staff". The inter-system floor was then written
/// between the two systems' FIRST refpoints, so the second staff and everything under it fell
/// outside the quantity: MEASURED 6.860000 where the pair's own basic-distance is 12.000000,
/// with the next system's section label and bar number printed through the instrument name.
/// </para>
/// <para>
/// ⚠️ WHY NO EXISTING NET SAW IT, and it is the reason this file exists rather than a corpus
/// book. A row written directly under its staff, or directly over the staff below, FOLDS into
/// that staff (<c>RenderSpecParser.FoldAdjacentRows</c>) and stops being a line of the
/// alignment — so the decline needs TWO rows between the same pair to fire at all, and no book
/// in the 81-book LP regression corpus or the 572 tracked ones has two. The corpus reads
/// 0 / 81 moved across this fix.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class InterSystemFloorTests
{
    /// <summary>Two systems, two staves each, with <paramref name="rows"/> between them.</summary>
    private static string Book(string rows) => $$"""
        time 4/4
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | break }
          section B { g'4 g f f | e e d2 | }
        }
        chords prog {
          section A { C | F }
          section B { Am | G }
        }
        lyrics verse {
          section A { one two three four | five six sev- en | }
          section B { eight nine ten e- | le- ven twelve | }
        }
        form main { A B }
        score main {
        {{rows}}
        }
        """;

    // Every arrangement of ONE pair of staves with rows between them. The last two are the
    // ones the decline fired on: two rows survive the fold, so something really does stand
    // between the pair in the alignment.
    private const string Plain = "  staff melody\n  staff melody";
    private const string Chords = "  staff melody\n  chords prog as names\n  staff melody";
    private const string Lyrics = "  staff melody\n  lyrics verse sings melody\n  staff melody";
    private const string ChordsThenLyrics =
        "  staff melody\n  chords prog as names\n  lyrics verse sings melody\n  staff melody";
    private const string LyricsThenChords =
        "  staff melody\n  lyrics verse sings melody\n  chords prog as names\n  staff melody";

    /// <summary>
    /// System 1's LAST staff to system 2's FIRST — the distance
    /// <c>system-system-spacing</c> governs.
    /// </summary>
    /// <remarks>
    /// ⚠️ REFPOINT TO REFPOINT, and read off the drawn staves rather than off the layout, so
    /// the reading cannot share a frame bug with the thing it is watching. Each system has two
    /// staves, so page-order refpoints 1 and 2 bracket the system boundary.
    /// </remarks>
    private static double InterSystemDistance(string rows)
    {
        var g = RenderedGeometry.Render(Book(rows));
        var refpoints = g.StaffRefpoints();
        Assert.True(refpoints.Count >= 4,
            $"expected two systems of two staves, found {refpoints.Count} staves:\n{g.Describe()}");
        return refpoints[2] - refpoints[1];
    }

    /// <summary>
    /// The floor is the same whatever stands between the staves — including nothing.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ASSERTION IS THE AGREEMENT, not a literal. What the rows change is the system's
    /// own HEIGHT, which is not inside this quantity at all; a reading that moved with them
    /// would be a reading taken in the origin frame, which is exactly the defect. Pinning the
    /// number instead would go red the day a chord symbol's ink legitimately floors the pair,
    /// and would say nothing about the frame.
    /// </remarks>
    [Fact]
    public void TheInterSystemFloor_IsBlindToWhatStandsBetweenTheStaves()
    {
        double plain = InterSystemDistance(Plain);

        foreach (var (name, rows) in new[]
                 {
                     (nameof(Chords), Chords),
                     (nameof(Lyrics), Lyrics),
                     (nameof(ChordsThenLyrics), ChordsThenLyrics),
                     (nameof(LyricsThenChords), LyricsThenChords),
                 })
        {
            Assert.True(
                System.Math.Abs(InterSystemDistance(rows) - plain) < 1e-6,
                $"{name}: the systems are {InterSystemDistance(rows):F6} apart where the same "
                + $"book with no rows between the staves reads {plain:F6}. The rows are inside "
                + "the system, not between the two systems.");
        }

        // …and it is not below the spring's own ideal, which is what says the floor is being
        // applied in the refpoint frame at all — a floor written in the ORIGIN frame lands
        // under the ideal exactly as far as the system's own body is tall.
        // LILYPOND-REF: ly/paper-defaults-init.ly:62-65 system-system-spacing, whose
        // basic-distance is 12 and whose minimum-distance is 8.
        Assert.True(plain >= VerticalSpacingParameters.Default.SystemSystem.BasicDistance - 1e-6,
            $"the systems are {plain:F6} apart, under the pair's basic-distance "
            + $"{VerticalSpacingParameters.Default.SystemSystem.BasicDistance:F6}");
    }

    /// <summary>
    /// …and the invariant underneath it: a system with two spaceable staves always carries a
    /// staff spring, so nothing downstream has to guess which end its chain leaves from.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE PROOF THAT LETS <c>OriginToChainEnd</c> READ <c>ToLast</c>
    /// UNCONDITIONALLY. It used to answer <c>ToFirst</c> when the spring list was empty, on
    /// the account that "a system contributes one chain node per staff SPRING, so one with no
    /// springs left never reached its last staff". That is a statement about hara-kiri, where
    /// only ONE staff survives — and there <c>ToFirst</c> and <c>ToLast</c> are the same
    /// number, because the first surviving spaceable staff IS the last. The branch could only
    /// ever differ when a system had two spaceable staves and no spring, which was not a
    /// hara-kiri state at all but the decline above. <c>MultiStaffLayouter.StaffSprings</c>
    /// now springs every consecutive spaceable pair unconditionally, which this asserts.
    /// </remarks>
    [Theory]
    [InlineData(Plain)]
    [InlineData(Chords)]
    [InlineData(Lyrics)]
    [InlineData(ChordsThenLyrics)]
    [InlineData(LyricsThenChords)]
    public void EverySystemWithTwoSpaceableStaves_CarriesAStaffSpring(string rows)
    {
        var tree = SyntaxTree.Parse(Book(rows));
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);

        foreach (var page in layout.Pages)
        {
            foreach (var system in page.Systems)
            {
                int spaceable = system.StaffGroups
                    .SelectMany(g => g.Staves)
                    .Count(s => !s.IsHidden && !s.StaffAffinity.HasValue);
                if (spaceable < 2) continue;
                Assert.True(
                    system.StaffSprings.Length >= spaceable - 1,
                    $"a system with {spaceable} spaceable staves carries "
                    + $"{system.StaffSprings.Length} staff spring(s); the page's chain needs "
                    + "one per consecutive pair, and the inter-system floor reads the list to "
                    + "decide which staff its chain leaves from.");
            }
        }
    }
}
