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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A line standing between two spaceable staves does not break their page spring: LilyPond
/// springs between CONSECUTIVE SPACEABLE elements and pushes everything else onto
/// <c>loose_lines</c>, so a bare <c>lyrics</c> row is what the pair's floor is measured OVER,
/// never a reason for the pair to have no spring at all.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/page-layout-problem.cc:660-672 <c>append_system</c> — the loop that
/// makes one spring per consecutive spaceable pair; :919-925 — the run of loose lines
/// collected between two of them; :1173-1177 <c>is_spaceable</c> — the one property that
/// decides which a line is.
/// </para>
/// <para>
/// ⚠️ THE DEFECT THIS GUARDS WAS INVISIBLE TO 572 BOOKS AND TO EVERY FIXTURE.
/// <c>MultiStaffLayouter.StaffSprings</c> paired <c>flat[i]</c> with <c>flat[i + 1]</c> and
/// dropped the pair when either end was non-spaceable — the same walk as LilyPond's only
/// while nothing ever stands between two staves. A bare row IS its own element of the
/// alignment (a note-bound verse is not: it is ink hanging off its staff), so
/// <c>staff / lyrics / staff</c> left the system with NO staff spring, and a system with no
/// spring hands <c>LayoutEngine.CreatePages</c> its FIRST staff as the end of its chain
/// (<c>OriginToChainEnd</c>). The inter-system floor was then written between the two
/// systems' FIRST staves, and everything below the first one — the second staff and the row
/// — fell outside the quantity.
/// </para>
/// <para>
/// MEASURED on 2.26.0, book <c>audit/lpreg/lyhygrace.ly</c> (<c>staff / row / staff / row</c>,
/// four systems, page-vertical's <c>PROBEV</c> dump): all three inter-system distances are
/// 12.000000 between the previous system's LAST spaceable staff's reference point and the
/// next system's FIRST — that is 8.000000 between the staff lines that face each other, the
/// same floor <see cref="SystemGapStaffFrameTests"/> measures without a row. Lily# read
/// 10.400000 / 11.570000 / 11.570000 there, and on the reported book
/// (scratch/ベースタブLy/Untitled-6.lys, user report 2026-08-25) it drew the two staves
/// 0.470000 apart — through each other.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LyricsRowStaffSpringTests
{
    // Two staves and three systems, so there are two inter-system pairs to read and the
    // second is not the one the indent column collapses (SystemGapStaffFrameTests' arm 2).
    private const string Head = """
        key c major
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | break}
          section B { g'4 g f f | e e d2 | break}
        }
        lyrics verse {
          section A { Twin- kle twin- kle | lit- tle star | }
          section B { Up a- bove the | world so high | }
        }
        form main { A B A }
        """;

    private static string Render(string scoreBody)
    {
        var tree = SyntaxTree.Parse($"{Head}\nscore main {{\n{scoreBody}\n}}\n");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    /// <summary>The score with the row between its two staves, and the same score without it.</summary>
    private const string WithRow = "  staff melody\n  lyrics verse\n  staff melody";
    private const string WithoutRow = "  staff melody\n  staff melody";

    // MEASURED on LilyPond 2.26.0 — see the class remark. 12.000000 between the two staves'
    // reference points is 8.000000 between the lines that face each other.
    private const double LilyPondFloor = 8.0;

    /// <summary>
    /// Every gap between two staves of DIFFERENT systems, in page order. With two staves per
    /// system those are the odd entries: gap 0 is inside system 0, gap 1 crosses to system 1.
    /// </summary>
    private static List<double> BetweenSystems(string svg)
        => StaffLineGeometry.Gaps(svg).Where((_, i) => i % 2 == 1).ToList();

    /// <summary>
    /// The claim. A row between the staves does not put the systems through each other, and
    /// the floor it lands on is the one LilyPond draws for the same arrangement.
    /// </summary>
    [Fact]
    public void ARowBetweenTwoStaves_DoesNotTakeTheSystemsFloor()
    {
        var gaps = BetweenSystems(Render(WithRow));
        Assert.Equal(2, gaps.Count);
        foreach (double g in gaps)
            Assert.True(g >= LilyPondFloor - 1e-6,
                $"a system pair fell through LilyPond's floor ({g:F6} < {LilyPondFloor:F6})");
    }

    /// <summary>
    /// CONTROL: the same score with the row deleted reads the same floor. The change is a
    /// walk over what stands BETWEEN two staves, so a pair with nothing between it must be
    /// untouched — and 572 books say it is.
    /// </summary>
    [Fact]
    public void TheSameScoreWithoutTheRow_ReadsTheSameFloor()
    {
        var withRow = BetweenSystems(Render(WithRow));
        var without = BetweenSystems(Render(WithoutRow));
        Assert.Equal(withRow.Count, without.Count);
        foreach (double g in without)
            Assert.True(g >= LilyPondFloor - 1e-6,
                $"the rowless control fell through the floor ({g:F6})");
    }

    /// <summary>
    /// …and the row is still THERE. This is the arm that goes red for the opposite mistake:
    /// clearing the overlap by pricing the pair as if nothing stood between it would leave
    /// the two staves at their plain staff-to-staff distance and draw the syllables through
    /// the lower one.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT ASSERTS THE ORDER, NOT A LITERAL — the room a row needs comes from a text
    /// metric, and a text metric is not a constant across platforms
    /// (<see cref="SystemGapStaffFrameTests"/>'s control makes the same choice for the same
    /// reason). What is platform-independent is that a row takes room and no row takes none.
    /// <para>
    /// ⚠️ NEITHER POISON FOR THE FIX ABOVE REACHES THIS ARM, and it is written down rather
    /// than left to be assumed (HANDOFF bone 2 — an arm nobody has seen red is not yet a
    /// control). MEASURED, both:
    /// <list type="bullet">
    /// <item>revert the consecutive-spaceable walk →
    ///   <see cref="ARowBetweenTwoStaves_DoesNotTakeTheSystemsFloor"/> red, this GREEN;</item>
    /// <item>spring the pair but hand the walk no rows (the floor priced as if nothing stood
    ///   between) → the two <c>lyrics.row.between-staves.two-verse.*.staff-staff-inside</c>
    ///   ledger entries red, every arm here GREEN — that floor binds only where a page
    ///   COMPRESSES, and this book does not.</item>
    /// </list>
    /// The reason is that the two staves were always DRAWN at the walk's distance: what the
    /// missing spring cost was the PAGE, not the placement. So this arm watches the
    /// placement, which no edit in this change touches — it is here for the OPPOSITE
    /// mistake, the one that clears an overlap by making the row stop taking room, and that
    /// lives in <c>SelectInterGroupSpec</c> / <c>StackStaves</c> rather than in the spring.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRowStillTakesItsRoomInsideTheSystem()
    {
        double inside(string body) => StaffLineGeometry.Gaps(Render(body))[0];
        double withRow = inside(WithRow), without = inside(WithoutRow);
        Assert.True(withRow > without + 1.0,
            $"the row between the staves took no room ({withRow:F6} against {without:F6})");
    }

    /// <summary>
    /// The two spellings of one arrangement put their syllables in the SAME place. LilyPond
    /// reads a bare row and a <c>sings</c> verse of the same syllables line for line the same
    /// (page-layout-problem.cc:919-925 asks nothing about what bound them), so the run it
    /// solves is one run — and since 2026-08-25 both spellings go through one walk in Lily#
    /// too, at one verse and at two.
    /// </summary>
    /// <remarks>
    /// ⚠️ AN EARLIER VERSION OF THIS ARM ASSERTED THE STAFF DISTANCE AND SAID WIDENING IT TO
    /// TWO VERSES "WOULD ASSERT A DEFECT". Both halves have moved. The syllables agree at two
    /// verses now, which is what this asserts; the STAFF distance still does not (14.970000
    /// against 13.170000 on this pair) and that is the OTHER half of the island — the room a
    /// row is given is still <c>MultiStaffLayouter.GetStaffHeight</c>'s nominal band rather
    /// than the walk's own answer, exactly as <c>TextRowVerseSpacing</c>'s remark says. So the
    /// arm reads BASELINES, which is the quantity that closed, and names the one that did not
    /// rather than averaging the two into a weaker claim.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ABareRowAndASungVerse_PutTheirSyllablesInTheSamePlace(int verses)
    {
        var bare = Syllables(RowScore(verses, sings: false));
        var sung = Syllables(RowScore(verses, sings: true));
        Assert.Equal(verses, bare.Count);
        Assert.Equal(bare.Count, sung.Count);
        for (int i = 0; i < bare.Count; i++)
            Assert.Equal(bare[i], sung[i], 6);
    }

    /// <summary>
    /// ...and the step between two of them is the alignment's, not a band's. LilyPond's
    /// <c>nonstaff-nonstaff-spacing</c> declares minimum-distance 2.8
    /// (ly/engraver-init.ly:653-657) and the chain lands on it here.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE LITERAL IS LILYPOND'S OWN DECLARATION, not a measured Lily# number, which is
    /// why it may be written down: the two verses' ink does not reach 2.8 apart, so the
    /// spring sits on its floor and the reading is the property. MEASURED against 2.26.0 as
    /// ledger <c>lyrics.row.between-staves.two-verse.verse-step</c>, which reads exact.
    /// ⚠️ IT WAS 4.600000 UNTIL 2026-08-25 — <c>MultiStaffLayouter.TextRowVerseSpacing</c>'s
    /// flat 3.2 band plus the row's own nominal height — because a row between two staves was
    /// never an element of the run those staves bound.
    /// </remarks>
    [Fact]
    public void TwoVersesBetweenTwoStaves_AreSteppedByTheAlignment()
    {
        var bare = Syllables(RowScore(2, sings: false));
        Assert.Equal(2, bare.Count);
        Assert.Equal(2.8, bare[1] - bare[0], 6);
    }

    /// <summary>One staff, N verses of lyrics between it and a second staff, one system.</summary>
    private static string RowScore(int verses, bool sings)
    {
        var tracks = string.Join("\n", Enumerable.Range(1, verses)
            .Select(v => $"  lyrics v{v}{(sings ? " sings melody" : "")}"));
        var blocks = string.Join("\n", Enumerable.Range(1, verses)
            .Select(v => $"lyrics v{v} {{ section A {{ Twin- kle twin- kle | lit- tle star | }} }}"));
        return "key c major\n"
            + "part melody { clef treble\n  section A { c4 c g' g | a a g2 | }\n}\n"
            + blocks + "\nform main { A }\n"
            + "score main {\n  staff melody\n" + tracks + "\n  staff melody\n}\n";
    }

    /// <summary>The syllable baselines standing between the system's two staves.</summary>
    private static List<double> Syllables(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        string svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        var staves = StaffLineGeometry.Staves(svg);
        Assert.Equal(2, staves.Count);
        return StaffLineGeometry.Baselines(svg, staves[0].Bottom, staves[1].Top);
    }
}
