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

        // EVERY spring, spring 0 included: the bar line → first column gap is built
        // by the one shared SpacingRules.BarlineToFirstColumnSpring.
        for (int i = 0; i < itemSprings.Length; i++)
            Assert.Equal(columnSprings[i].IdealDistance, itemSprings[i].IdealDistance, 9);

        // Spring 0 is the BarLine space-alist value (next-note 0.9), not the first
        // note's duration space — LilyPond reaches a bar line → note pair through
        // Staff_spacing, where duration never enters. Pin the shape so a regression
        // back to duration spacing (which read 3.6 here) is caught.
        Assert.Equal(EngravingDefaults.BarLineToNextNoteSpace, itemSprings[0].IdealDistance, 9);
        // semi-fixed-space IS stretchable — see BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance.
        Assert.Equal(EngravingDefaults.BarLineToNextNoteSpace / 2,
                     itemSprings[0].InverseStretchStrength, 9);

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
    public void BothSpringSystems_AgreeAcrossAMidMeasureChangeColumn()
    {
        // The measure in BothSpringSystems_AgreeOnEveryMusicalSpring has no change item, so
        // it could not see this: the two systems model a mid-measure change with DIFFERENT
        // topologies. The timing-column system has ONE spring (a zero-duration change shares
        // the next note's timing), the item system has TWO (a change gets its own slot). The
        // totals must still agree, or CalculateMeasureIdealWidth under-estimates exactly the
        // measures whose spacing is hardest and the line breaker packs them too tight.
        //
        // They did NOT agree before the change column was priced the way LilyPond prices it:
        // the column system reserved glyph + 2 x 0.5 on one spring while the item system put
        // a full duration space before the change AND the same reservation after it.
        var (timings, allMeasures, primary, _) = Collect("""
            time 4/4
            octave absolute
            part melody
            section Main { melody { c'4 d' clef bass e4 f4 | } }
            form main { Main }
            score main "x" { staff melody }
            """);
        var columnSprings = new MeasureLayouter()
            .CreateTimingSprings(primary, timings, 0.125, allMeasures);
        var itemSprings = SpacingRules.CreateSpringsForMeasure(primary, 0.125);

        // One extra item slot for the clef, and no extra column for it.
        Assert.Equal(columnSprings.Length + 1, itemSprings.Length);
        Assert.Equal(columnSprings.Sum(s => s.IdealDistance),
                     itemSprings.Sum(s => s.IdealDistance), 9);

        // And the split itself is LilyPond's, not an arbitrary halving: the gap from the
        // clef's own column origin to the next note is its ink width plus Clef.space-alist
        // (next-note . (extra-space . 1.0)). Measured on 2.24.4 as 3.146680 for clefs.F_change
        // — see audit/lp-geometry midmeasure.clef.clef-to-next-note.
        var clefColumn = primary.Items.Skip(2).ToList();
        Assert.Equal(GlyphMetrics.FClefChangeWidth + 1.0,
                     SpacingRules.MidMeasureChangeRightGap(clefColumn), 9);
    }

    [Fact]
    public void MmrRodMinimumDistance_ReservesBreakAlignedChangeAtRunBound()
    {
        // LP's Paper_column::minimum_distance across a multi-measure-rest run's bounding
        // columns is a Skyline::distance over the LEFT column's break-aligned grobs, so a
        // key / time change sitting at the run's bound reserves ITS OWN width, not just the
        // bar line's. This guards the skyline math directly.
        //
        // Reached by the live pipeline: MultiMeasureRestEngraver's run detection keeps a bar
        // whose only sounding content is the MMR rest IN the run even when break-aligned
        // changes precede it, so the run opens on that bar and the change rides its left
        // bound. End-to-end on `c'1 | key g major R1*5 | c'1`, Lily# renders one 5-bar church
        // rest spanning 17.14 bar-line to bar-line against LP 2.24.4's 17.134 (14.13 without
        // the key change, so the signature buys exactly the +3.0 this pins).
        //
        // Values pinned to LilyPond 2.24.4, read off the bounding NonMusicalPaperColumn's
        // grobs (ly:grob-relative-coordinate + X-extent + extra-spacing-width):
        //   bar line only : reach 0.19 + esw 0.1 = 0.29         -> 0.29 - (-0.1) = 0.390
        //   \key g \major : keysig box 1.19..2.29, esw 1.0      -> reach 3.29  -> 3.390
        //   \time 2/4     : timesig box 0.94..2.545, esw 0.8    -> reach 3.345 -> 3.445
        // LILYPOND-REF: lily/paper-column.cc:144-164 minimum_distance,
        // lily/separation-item.cc:120-190 boxes (extent + extra-spacing-width).
        static double RunBoundMinDist(string runMeasure)
        {
            string src = $$"""
                time 4/4
                key c major
                octave absolute
                part melody
                section Main { melody { c'1 | {{runMeasure}} | c'1 | } }
                form main { Main }
                score main "x" { staff melody }
                """;
            var tree = SyntaxTree.Parse(src);
            var spec = RenderSpecParser.FindFirst(tree);
            var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);
            var measures = multi.StaffGroups[0].PrimaryStaff.PrimaryVoice.Measures.ToList();
            int runIdx = measures.FindIndex(
                m => m.Items.Any(it => it is RestItem { IsMultiMeasure: true }));
            Assert.True(runIdx > 0, "fixture must open with a note bar before the run");
            var leftBound = SpacingRules.RunLeftBoundBarline(measures, runIdx);
            return SpacingRules.MmrRodMinimumDistance(leftBound, measures[runIdx].Items);
        }

        // Bar-line-only bound is unchanged from the old closed form (the plain-run path).
        Assert.Equal(0.390, RunBoundMinDist("R1*5"), 3);
        // A key change at the bound reserves the whole key signature: +3.0 ss over the bar
        // line alone. Lily# reproduces LP's 3.390 to the third decimal.
        Assert.Equal(3.390, RunBoundMinDist("key g major R1*5"), 3);
        // A time change likewise. LP grob geometry gives 3.445; Lily# lands at 3.440, the
        // ~0.005 gap being the time-signature glyph-metric residual (GetTimeSigWidth 1.60
        // vs LP's 1.6047), well under the visual threshold — see §3.7, recorded not fudged.
        Assert.InRange(RunBoundMinDist("time 2/4 R2*5"), 3.43, 3.45);
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
    public void BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance()
    {
        // This spring starts at a MID-LINE bar line, so LilyPond reads `next-note`,
        // not `first-note` — Staff_spacing::get_spacing only reaches for `first-note`
        // when break_status_dir != CENTER (start of a system).
        // LILYPOND-REF: scm/define-grobs.scm:301 BarLine space-alist
        //   (next-note . (semi-fixed-space . 0.9)); lily/staff-spacing.cc:147-153.
        //
        // semi-fixed-space splits that 0.9 into fixed = d/2 and ideal = fixed + d/2, and
        // leaves `is_stretchable` TRUE — only shrink-space and semi-shrink-space clear it —
        // so the spring's inverse stretch strength is ideal - fixed = 0.45. This test used
        // to assert 0, on the strength of a comment claiming the gap after a bar line never
        // stretches. It does. Measured on 2.24.4 with `c'4 d' e' f' | g'4 a' b' c''`,
        // bar-line ink right edge -> next notehead ink left edge:
        //   ragged-right          0.900000   (force 0, the natural length)
        //   justified, 120mm      1.996558
        //   justified, 180mm      3.091335
        // Solving force from those with strength 0.45 gives 2.43680 and 4.86963, and
        // feeding those forces back into the FIRST MUSICAL spring (natural 3.002257,
        // stretched to 7.140047 and 11.271114) yields the same 1.69805 both times — so
        // 0.45 is confirmed against an independent spring, not fitted to one measurement.
        // LILYPOND-REF: lily/staff-spacing.cc:164-180 (semi-fixed-space) and :200
        //   (stretchability = ideal - fixed), :218 set_inverse_stretch_strength.
        var (timings, allMeasures, primary, _) = Collect(OneMeasure);
        var springs = new MeasureLayouter().CreateTimingSprings(primary, timings, 0.125, allMeasures);
        Assert.Equal(EngravingDefaults.BarLineToNextNoteSpace / 2,
                     springs[0].InverseStretchStrength, precision: 9);
        Assert.Equal(EngravingDefaults.BarLineToNextNoteSpace, springs[0].IdealDistance, precision: 9);
    }

    [Fact]
    public void FirstColumn_MovesLessThanMusicalColumnsWhenMeasureStretches()
    {
        // Corollary of the semi-fixed bar-line spring: it is stretchable, so the first
        // column DOES move when the measure is stretched — but by much less than the
        // musical columns, because 0.45 is a far weaker stretch strength than a musical
        // spring's (1.69805 on the measured line above, i.e. ~3.8x stiffer here).
        // The former version of this test asserted the first column did not move at all,
        // which followed from the rigidity that BarlineToFirstNoteSpring_Stretches... has
        // now disproved against LilyPond.
        var (timings, allMeasures, primary, _) = Collect(OneMeasure);
        var layouter = new MeasureLayouter();
        var narrow = layouter.LayoutColumns(primary, 16, timings, 0.125, allMeasures);
        var wide = layouter.LayoutColumns(primary, 32, timings, 0.125, allMeasures);
        double firstShift = wide[0].X - narrow[0].X;
        double secondShift = wide[1].X - narrow[1].X;
        Assert.True(firstShift > 0,
            $"the semi-fixed bar-line spring is stretchable, so the first column must move: {firstShift}");
        Assert.True(secondShift > firstShift,
            $"musical springs must absorb MORE of the stretch than the bar-line spring: "
            + $"first={firstShift}, second={secondShift}");
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
