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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LilyPond spacing INVARIANTS, asserted as relations (not pinned pixels).
/// Each test encodes a rule from the LilyPond source so that spacing
/// regressions are caught structurally instead of by eyeballing scores —
/// whack-a-mole prevention.
/// </summary>
[Trait("Category", "Unit")]
public class SpacingInvariantTests
{
    private static (List<Fraction> Timings, List<Measure> AllMeasures, Measure Primary, MultiStaffScore Score)
        Collect(string src, int measureIndex = 0)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var timings = MultiStaffLayouter.CollectAllTimingsForMeasure(multi, measureIndex);
        var allMeasures = MultiStaffLayouter.CollectAllMeasuresAtIndex(multi, measureIndex);
        var primary = multi.StaffGroups[0].PrimaryStaff.PrimaryVoice.Measures[measureIndex];
        return (timings, allMeasures, primary, multi);
    }

    private const string OneMeasure = """
        time 4/4
        section Main { melody { c2 d4 e | } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Fact]
    public void BothSpringSystems_AgreeOnEveryMusicalSpring()
    {
        // Lily# builds springs two ways and they must not drift: the timing-column
        // system (MeasureLayouter.CreateTimingSprings — union columns, drives the
        // real layout and the multi-staff break gate) and the item system
        // (SpacingRules.CreateSpringsForMeasure — single measure, feeds
        // CalculateMeasureIdealWidth / the greedy breaker / KnuthPlassBreaker's
        // own spring data).
        //
        // The bar line stands in for the right-hand stem on the last spring, so both
        // must carry stem_dir_correction there. CreateSpring only corrects between
        // two items, so the item system silently lost it until the correction was
        // applied explicitly; a monophonic measure is the single-wish case, where
        // merging one wish returns it unchanged and the two must land on the SAME
        // number.
        // LILYPOND-REF: lily/note-spacing.cc:111 + :243-264.
        var (timings, allMeasures, primary, _) = Collect(OneMeasure);
        var columnSprings = new MeasureLayouter()
            .CreateTimingSprings(primary, timings, 0.125, allMeasures);
        var itemSprings = SpacingRules.CreateSpringsForMeasure(primary, 0.125);

        Assert.Equal(columnSprings.Length, itemSprings.Length);

        // Spring 0 (bar line → first column) is KNOWINGLY excluded: the column
        // system models it as LilyPond does — BarLine's space-alist `next-note`
        // (0.9, rigid, staff-spacing.cc) — while the item system still prices it as
        // a note's duration space (3.6 for a quarter). LilyPond never uses duration
        // space across a bar line, so the item system is wrong there and the two
        // disagree by ~2.7 ss. Porting it needs the skyline / grace / change-prefix
        // minimums too, so it is left as a separate step; this bound documents the
        // remaining gap instead of hiding it.
        for (int i = 1; i < itemSprings.Length; i++)
            Assert.Equal(columnSprings[i].IdealDistance, itemSprings[i].IdealDistance, 9);

        // And both corrections are actually LIVE here, so the equality above is not
        // two zeroes agreeing: `e` is a stemmed quarter, so the spring into the bar
        // line must exceed the bare duration space (stem correction), and an inner
        // spring must exceed it too (left head width, +0.104 for a black head).
        double bare = SpacingRules.CalculateDurationSpace(Fraction.Quarter, 0.125);
        Assert.True(itemSprings[^1].IdealDistance > bare,
            $"the bar-line spring must carry the stem correction: "
            + $"bare={bare}, toBarline={itemSprings[^1].IdealDistance}");
        Assert.True(itemSprings[^2].IdealDistance > bare,
            $"an inner spring must carry the left-head refinement: "
            + $"bare={bare}, inner={itemSprings[^2].IdealDistance}");
    }

    [Fact]
    public void SpringDurations_FollowGetDurationSpace()
    {
        // LILYPOND-REF: lily/spacing-options.cc get_duration_space — doubling
        // a duration adds one spacing increment; the spring between two
        // columns takes ITS OWN segment duration (a half-note gap must be one
        // increment wider than a quarter-note gap, never equal).
        var (timings, allMeasures, primary, _) = Collect(OneMeasure);
        double bsd = 0.125;
        var springs = new MeasureLayouter().CreateTimingSprings(primary, timings, bsd, allMeasures);

        // springs: [barline→c2] [c2→d4 (half)] [d4→e4 (quarter)] [e4→end (quarter)]
        Assert.Equal(4, springs.Length);
        double halfGap = springs[1].IdealDistance;
        double quarterGap = springs[2].IdealDistance;
        // The ideal also carries the LEFT column's head width (note-spacing.cc:77):
        // a half note's open head (1.376) is wider than a quarter's (1.304), so the
        // half gap exceeds the quarter gap by the duration increment PLUS that
        // head-width difference.
        double durationDelta = SpacingRules.CalculateDurationSpace(new Fraction(1, 2), bsd)
                             - SpacingRules.CalculateDurationSpace(new Fraction(1, 4), bsd);
        double headDelta = GlyphMetrics.GetNoteheadAdvance(2) - GlyphMetrics.GetNoteheadAdvance(4);
        double expectedDelta = durationDelta + headDelta;
        Assert.True(halfGap > quarterGap, $"half {halfGap} must exceed quarter {quarterGap}");
        Assert.Equal(expectedDelta, halfGap - quarterGap, precision: 6);
        // The spring INTO the bar line is NOT the plain quarter gap: LilyPond runs
        // stem_dir_correction there too, with the bar line standing in for the
        // right-hand stem, so the directions are opposite by construction and the
        // correction (halved for a bar) widens the gap.
        // Measured on LilyPond 2.24.4, `c'2 d'4 e' | c'2 d'4 e'`, column to column:
        //   c'2 -> d'4      4.275
        //   d'4 -> e'4      3.002
        //   e'4 -> bar     3.239   (+0.237)
        // LILYPOND-REF: lily/note-spacing.cc:111 + :243-264.
        Assert.True(springs[3].IdealDistance > springs[2].IdealDistance,
            $"the spring into the bar line must carry the stem correction: "
            + $"quarter={springs[2].IdealDistance}, toBarline={springs[3].IdealDistance}");
    }

    [Fact]
    public void BarlineToFirstNoteSpring_IsRigid()
    {
        // LILYPOND-REF: scm/define-grobs.scm:301 BarLine space-alist —
        // (next-note . (semi-fixed-space . 0.9)): the gap after a bar line NEVER
        // stretches under line justification.
        // This spring starts at a MID-LINE bar line, so LilyPond reads `next-note`,
        // not `first-note` — Staff_spacing::get_spacing only reaches for `first-note`
        // when break_status_dir != CENTER (start of a system).
        // LILYPOND-REF: lily/staff-spacing.cc:147-153.
        var (timings, allMeasures, primary, _) = Collect(OneMeasure);
        var springs = new MeasureLayouter().CreateTimingSprings(primary, timings, 0.125, allMeasures);
        Assert.Equal(0, springs[0].InverseStretchStrength, precision: 9);
        Assert.True(springs[0].IdealDistance >= EngravingDefaults.BarLineToNextNoteSpace - 1e-9);
    }

    [Fact]
    public void FirstColumn_DoesNotMoveWhenMeasureStretches()
    {
        // Corollary of the rigid barline spring: solving the same measure at
        // 1x and 2x width must keep the first column's X identical.
        var (timings, allMeasures, primary, _) = Collect(OneMeasure);
        var layouter = new MeasureLayouter();
        var narrow = layouter.LayoutColumns(primary, 16, timings, 0.125, allMeasures);
        var wide = layouter.LayoutColumns(primary, 32, timings, 0.125, allMeasures);
        Assert.Equal(narrow[0].X, wide[0].X, precision: 6);
        Assert.True(wide[1].X > narrow[1].X, "musical springs must absorb ALL the stretch");
    }

    [Fact]
    public void FullMeasureRest_CompactsOnCombinedPath()
    {
        // LILYPOND-REF: lily/multi-measure-rest.cc set_spacing_rods — full
        // measure rests use a compact rod. The combined-timings path (used by
        // BOTH the line breaker and the multi-staff layout) must apply it,
        // or breaking and layout disagree about measure widths.
        var src = """
            time 4/4
            section Main { melody { R1 | c4 d e f | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var (timings, allMeasures, primary, _) = Collect(src, measureIndex: 0);
        var springs = new MeasureLayouter().CreateTimingSprings(primary, timings, 0.125, allMeasures);
        var (timings1, allMeasures1, primary1, _) = Collect(src, measureIndex: 1);
        var noteSprings = new MeasureLayouter().CreateTimingSprings(primary1, timings1, 0.125, allMeasures1);

        double restWidth = springs.Sum(s => s.IdealDistance);
        double noteWidth = noteSprings.Sum(s => s.IdealDistance);
        Assert.True(restWidth < noteWidth / 2,
            $"compact MMR measure ({restWidth:F2}) must be far narrower than a 4-note measure ({noteWidth:F2})");
    }

    [Fact]
    public void MultiStaffBreaking_PricesByAllStaves()
    {
        // A measure whose PRIMARY staff rests but whose second staff is dense
        // must price (much) wider than an all-staves-resting measure —
        // otherwise the breaker packs lines wherever the top staff is silent.
        // LILYPOND-REF: lily/paper-column.cc — columns aggregate all staves.
        var src = """
            time 4/4
            part rh
            part lh
            section Main {
              rh { R1 | R1 | }
              lh { c8 d e f g a b c | R1 | }
            }
            form main { Main }
            score main "x" { staff rh staff lh }
            """;
        var (timings0, all0, primary0, _) = Collect(src, measureIndex: 0);
        var (timings1, all1, primary1, _) = Collect(src, measureIndex: 1);
        var layouter = new MeasureLayouter();
        double densePriced = layouter.CreateTimingSprings(primary0, timings0, 0.125, all0).Sum(s => s.IdealDistance);
        double restPriced = layouter.CreateTimingSprings(primary1, timings1, 0.125, all1).Sum(s => s.IdealDistance);
        Assert.True(densePriced > restPriced * 2,
            $"rest-against-8ths measure ({densePriced:F2}) must price like its dense staff, not its resting one ({restPriced:F2})");
    }
}
