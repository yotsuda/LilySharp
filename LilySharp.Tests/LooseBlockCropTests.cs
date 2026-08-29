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

using System;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The CROP of a content-sized single page is sized from where the loose block below the
/// last system is DRAWN, while the system's down EXTENT keeps reserving that block at its
/// alignment minimum — one quantity, two consumers, and they want different numbers.
/// </summary>
/// <remarks>
/// Lily# sizes a single page to its content rather than to the paper
/// (<c>LayoutEngine.CreatePages</c>, a declared divergence). Since 2026-08-29 the chain
/// that draws the last block on a page is solved into the PAPER
/// (<c>LayoutEngine.BuildLooseChainEnds</c>' page-edge branch), so its springs come to rest
/// at their ideal; the crop went on reading the ALIGNMENT MINIMUM, which is what LilyPond
/// reserves (lily/page-layout-problem.cc:593-599 <c>build_system_skyline</c>), and the
/// page's bottom white shrank by the difference — MEASURED 1.130041 on the ledger's book
/// TBL2 and 0.139 on <c>test/lyrics</c>.
/// <para>
/// ⚠️ TWO FACTS, BECAUSE ONE OF THEM PASSES FOR THE WRONG REASON. The obvious repair —
/// reserving the block at rest where the page reserves it, rather than beside — makes ⒜
/// green and is wrong: that same reservation is the system's DOWN silhouette for
/// system-system spacing, where LilyPond's really is the minimum, so it would push every
/// following system down by the same amount. ⒝ is the half that says the split happened
/// rather than the reservation moving, and it is stated on a book where the crop cannot see
/// it (two systems, so the gap is a real spring).
/// </para>
/// <para>
/// ⚠️ POSITIVE CONTROLS (run 2026-08-30, session 292, all reverted):
/// ⑴ with the crop reading the down extent again — the state this trip found — ⒜ fails at
/// 1.1300414861538499 and ⒝ stays green.
/// ⑵ with the at-rest profile folded into <c>perSystemLyricBands</c>, the profile that joins
/// the paging silhouette — ⒝ fails, the two systems 21.282041 apart against 20.152001, and
/// ⒜ stays green.
/// ⇒ So the pair separates the fix from its impostor, in both directions.
/// </para>
/// <para>
/// ⚠️ AND A THIRD POISON WAS INERT, WHICH IS WHERE THE QUANTITY ACTUALLY LIVES. Folding the
/// at-rest profile into the down EXTENT (<c>perSystemExtents</c>) instead of the band moved
/// NOTHING — both facts stayed green. The scalar down extent is not what floors an
/// inter-system pair on a book that has skylines: <c>LayoutUtilities.InterSystemPairMinimum</c>
/// prefers the X-aware <c>Distance()</c>, and the block reaches THAT through
/// <c>PagingAugmentProgram.Builder.AddLyricBand</c> — the profile, not the scalar. The
/// extent's down half is the fallback for a system with no silhouette. ⇒ ★ A poison aimed at
/// the wrong spelling of a quantity is green for a reason that has nothing to do with the
/// guard; what names the right spelling is asking which consumer the FLOOR reads.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LooseBlockCropTests
{
    private static ScoreLayout LayoutOf(string body)
    {
        var tree = SyntaxTree.Parse("octave absolute\n" + body);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(
            SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    /// <summary>The same music and the same words twice; only the OCTAVE differs, which is
    /// the one thing that changes the first spring's floor without changing a syllable.</summary>
    private static string SungBook(string pitches, string name) =>
        "key c major\n"
        + "part m { clef treble }\n"
        + $"section A {{ m {{ {pitches} | }} }}\n"
        + "lyrics v { section A { Twin- kle twin- kle | } }\n"
        + "form main { ~A }\n"
        + $"score main \"{name}\" {{\n  staff m\n  lyrics v sings m\n}}\n";

    /// <summary>
    /// How far the deepest syllable's BASELINE stands above the page foot, in page Y-up.
    /// The words are the same in both books, so the ink hanging below that baseline is the
    /// same number too, and a page cropped to leave exactly its bottom margin under the
    /// block reads the SAME height here whatever the block's springs did.
    /// </summary>
    private static double BaselineAboveThePageFoot(string body)
    {
        var layout = LayoutOf(body);
        Assert.Single(layout.Pages);
        Assert.Single(layout.AllSystems);
        // SystemLayout.Y is page Y-up (up from the page foot) and a lyric's YUp is measured
        // DOWN from its system's origin, so the sum is the baseline over the foot; the
        // deepest line is the smallest YUp.
        double deepest = layout.LyricLayouts.Min(l => l.YUp);
        return layout.AllSystems[0].Y + deepest;
    }

    /// <summary>
    /// ⒜ THE CROP FOLLOWS THE DRAWN BLOCK. A book whose first spring has room to reach its
    /// ideal and one whose own ink already floors it above that ideal must leave the same
    /// white under the same syllable — the second is where the implementation was already
    /// right, so the residual between them IS the defect.
    /// </summary>
    /// <remarks>
    /// The control's notes hang three ledger lines below the staff, and a tab's strings are
    /// the other book that does this (audit/lp-geometry TBL1, first spring floored at
    /// 6.120115 against the 5.500000 ideal): with the staff's own ink deeper than the ideal
    /// the spring cannot stretch and the reservation was the drawn distance all along.
    /// ⚠️ THE OCTAVE IS THE ONLY VARIABLE. The words, the font, the clef, the key and the
    /// bar are identical, so the ink below the baseline cancels exactly and no tolerance is
    /// standing in for an unmodelled term.
    /// </remarks>
    [Fact]
    public void TheCropLeavesItsMargin_WhereverTheBlocksSpringComesToRest()
    {
        double stretched = BaselineAboveThePageFoot(SungBook("g'4 a' g' a'", "CRPA"));
        double floored = BaselineAboveThePageFoot(SungBook("g,4 a, g, a,", "CRPB"));

        // WHAT IS LEFT IS THE PAGE-EDGE CHAIN'S OWN STRETCH TAIL, and it is here rather
        // than hidden in a round tolerance because it is the one term the crop knowingly
        // does not carry: the crop reserves the block at force 0 while the chain solves at
        // the tiny positive force the paper's slack implies, so the drawn syllable sits
        // `force × stretchability` below the reservation. MEASURED 1.451282664e-6 — the
        // same tail LilyPond publishes for this spring (5.500001451282664 against its
        // 5.500000 basic-distance), which is what it means to solve the same chain into the
        // same room. The defect this test was written for is 1.130041486, five orders of
        // magnitude out of this window.
        double tail = floored - stretched;
        Assert.InRange(tail, 0.0, 2e-6);
    }

    /// <summary>
    /// ⒝ AND THE RESERVATION DID NOT MOVE. The distance between two systems of a sung book
    /// is the inter-system spring, floored by the block's ALIGNMENT MINIMUM — the crop's
    /// second reading must not have leaked into it.
    /// </summary>
    /// <remarks>
    /// ⚠️ PINNED, NOT DERIVED, and deliberately: what this asserts is that a number DID NOT
    /// CHANGE on this trip, so restating its arithmetic here would only prove the restating
    /// right. The value is this tree's, and the poison that moves it — the at-rest profile
    /// handed to <c>perSystemLyricBands</c> — is named in the class remark. A legitimate
    /// change to the inter-system floor is expected to update it, with the ledger's
    /// <c>lyrics.band-floor.*</c> pair saying which way LilyPond moved.
    /// </remarks>
    [Fact]
    public void TheInterSystemFloorStillReadsTheAlignmentMinimum()
    {
        const int bars = 24;
        const int verses = 5;
        string music = string.Concat(Enumerable.Repeat("g'4 a' g' a' | ", bars));
        string words = string.Concat(Enumerable.Repeat("Twin- kle twin- kle | ", bars));
        // FIVE VERSES SO THE BLOCK BINDS. With one verse the block hangs 4.369960 under the
        // staff and the pair's own basic-distance 12.000000 wins the max outright, so the
        // gap answers 12.000000 whatever the reservation says and the poison passes through
        // it — MEASURED while getting this control wrong. The liveness assertion below is
        // what keeps that from happening again silently.
        string sung = string.Concat(
            Enumerable.Repeat($"  lyrics words sings m {{ {words}}}\n", verses));
        var layout = LayoutOf(
            "key c major\n"
            + "part m { clef treble }\n"
            + $"section A {{\n  m {{ {music}}}\n{sung}}}\n"
            + "form main { ~A }\n"
            + "score main \"CRPC\" {\n  staff m  lyrics words\n}\n");

        var systems = layout.AllSystems;
        Assert.True(systems.Length >= 2,
            $"the book must break into at least two systems; it made {systems.Length}");
        // Page Y-up, so the upper system's origin is the LARGER Y.
        double gap = systems[0].Y - systems[1].Y;
        // LIVENESS FIRST: the pair's own basic-distance is 12.000000
        // (ly/paper-defaults-init.ly:62-65 system-system-spacing), and a gap sitting AT it
        // is a gap the block never reached — the assertion below would then be green for a
        // reason that has nothing to do with what it is guarding.
        Assert.True(gap > 12.5,
            $"the block must FLOOR this pair, not the pair's basic-distance: gap {gap:F6}");
        Assert.Equal(20.152001, gap, 6);
    }
}
