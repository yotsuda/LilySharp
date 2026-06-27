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
        structure { Main }
        score "x" { staff melody }
        """;

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
        double expectedDelta = SpacingRules.CalculateDurationSpace(new Fraction(1, 2), bsd)
                             - SpacingRules.CalculateDurationSpace(new Fraction(1, 4), bsd);
        Assert.True(halfGap > quarterGap, $"half {halfGap} must exceed quarter {quarterGap}");
        Assert.Equal(expectedDelta, halfGap - quarterGap, precision: 6);
        Assert.Equal(springs[2].IdealDistance, springs[3].IdealDistance, precision: 6);
    }

    [Fact]
    public void BarlineToFirstNoteSpring_IsRigid()
    {
        // LILYPOND-REF: scm/define-grobs.scm BarLine space-alist —
        // (first-note . (semi-shrink-space . 1.3)): the gap after a barline
        // NEVER stretches under line justification.
        var (timings, allMeasures, primary, _) = Collect(OneMeasure);
        var springs = new MeasureLayouter().CreateTimingSprings(primary, timings, 0.125, allMeasures);
        Assert.Equal(0, springs[0].InverseStretchStrength, precision: 9);
        Assert.True(springs[0].IdealDistance >= EngravingDefaults.BarLineToFirstNoteSpace - 1e-9);
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
            structure { Main }
            score "x" { staff melody }
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
            structure { Main }
            score "x" { staff rh staff lh }
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
