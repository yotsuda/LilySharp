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
/// The EMPTY BAR — nothing but skips, or a bar a percent repeat covers — is spaced by
/// LilyPond as ONE breakable-pair spring between its two bar lines, because the skip's
/// column holds no grob and an unused column leaves the spacing problem
/// (SpacingRules.EmptyBar). These tests pin the RULE by perturbation (the spring is
/// linear in the meter's length over the common shortest duration, not the whole note's
/// logarithmic space) and the two spring systems' agreement, and check the derived
/// figure against the 2.26.0 measurements the port was made from.
/// </summary>
[Trait("Category", "Unit")]
public class EmptyBarSpacingTests
{
    private const double GlobalShortest = EngravingDefaults.BaseShortestDuration; // 3/16

    /// <summary>scratch/p332/t7/pc5r.lys — the control the port was measured on.</summary>
    private const string SkipBars = """
        octave absolute
        paper { raggedRight }
        part bassline {
          clef bass
          section A {
            c4 d e f | s1 | s1 | s1 | d4 e f g |
          }
        }
        form main { A }
        score main { staff bassline }
        """;

    /// <summary>scratch/p332/t7/pc6r.lys — the eighths book whose common shortest is 1/8.</summary>
    private const string SkipBarsUnderEighths = """
        octave absolute
        paper { raggedRight }
        part bassline {
          clef bass
          section A {
            c8 d e f g a b c' | s1 | s1 | s1 | d4 e f g |
          }
        }
        form main { A }
        score main { staff bassline }
        """;

    private static (System.Collections.Generic.List<Fraction> Timings,
                    System.Collections.Generic.List<Measure> AllMeasures,
                    Measure Primary, MultiStaffScore Score)
        Collect(string src, int measureIndex)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var timings = MultiStaffLayouter.CollectAllTimingsForMeasure(multi, measureIndex);
        var allMeasures = MultiStaffLayouter.CollectAllMeasuresAtIndex(multi, measureIndex);
        var primary = multi.PrimaryContentStaff.PrimaryVoice.Measures[measureIndex];
        return (timings, allMeasures, primary, multi);
    }

    /// <summary>The timing-column chain as the layout and the break gate both read it.</summary>
    private static System.Collections.Immutable.ImmutableArray<Spring> ColumnSprings(
        string src, int measureIndex, double globalShortest)
    {
        var (timings, allMeasures, primary, score) = Collect(src, measureIndex);
        var springs = new MeasureLayouter()
            .CreateTimingSprings(primary, timings, globalShortest, allMeasures);
        return MultiStaffLayouter.ApplySharedColumnReservations(
            score, measureIndex, springs, primary, timings, allMeasures, globalShortest);
    }

    /// <summary>
    /// LilyPond's spring for the pair, bar line to bar line, from the formula the port
    /// transcribes: min_dist + spacing-increment × (mlen / gs) × 0.8.
    /// LILYPOND-REF: lily/spacing-basic.cc:44-56 standard_breakable_column_spacing.
    /// </summary>
    private static double LilyPondBarToBar(double globalShortest, Fraction measureLength)
        => SpacingRules.MmrRodMinimumDistance(BarlineType.Single, null)
           + EngravingDefaults.SpacingIncrement * (measureLength.ToDouble() / globalShortest) * 0.8;

    [Fact]
    public void ASkipBar_IsOneBreakablePairSpring_AfterRigidZeroLegs()
    {
        var (timings, _, _, _) = Collect(SkipBars, 1);
        var springs = ColumnSprings(SkipBars, 1, GlobalShortest);

        // The column contract holds — one onset, so two legs — but every leg before the
        // last is the deleted column: rigid and zero-length in every quantity.
        Assert.Equal(timings.Count + 1, springs.Length);
        for (int i = 0; i < springs.Length - 1; i++)
        {
            Assert.Equal(0.0, springs[i].IdealDistance);
            Assert.Equal(0.0, springs[i].MinDistance);
            Assert.Equal(0.0, springs[i].InverseStretchStrength);
            Assert.Equal(0.0, springs[i].InverseCompressStrength);
        }

        // The last leg is LilyPond's pair, re-framed by the left bar line's drawn width.
        double bw = SpacingRules.GetBarlineWidth(BarlineType.Single);
        var last = springs[^1];
        double space = EngravingDefaults.SpacingIncrement * (1.0 / GlobalShortest) * 0.8;
        Assert.Equal(LilyPondBarToBar(GlobalShortest, new Fraction(1, 1)) - bw, last.IdealDistance, 9);
        Assert.Equal(SpacingRules.MmrRodMinimumDistance(BarlineType.Single, null) - bw, last.MinDistance, 9);
        // set_inverse_stretch_strength (space), and the default compress strength is
        // ideal − min = space too (lily/spring.cc:204-210).
        Assert.Equal(space, last.InverseStretchStrength, 9);
        Assert.Equal(space, last.InverseCompressStrength, 9);

        // MEASURED, 2.26.0, scratch/p332/t7/pc5r (ragged, BarLine X): 5.51 bar line to
        // bar line for each skip bar. Lily# priced it 6.39 before the port.
        Assert.Equal(5.51, last.IdealDistance + bw, 2);
    }

    [Fact]
    public void ASkipBar_IsLinearInTheCommonShortest_NotAWholeNotesLogSpace()
    {
        // The RULE, by perturbation: halve the common shortest (3/16 → 1/8) and the bar
        // grows by exactly incr × 0.8 × (1/(1/8) − 1/(3/16)); a whole note's duration
        // space would grow by incr × log2 (3/2) instead. The zero legs do not move.
        var at316 = ColumnSprings(SkipBars, 1, 3.0 / 16);
        var at18 = ColumnSprings(SkipBars, 1, 1.0 / 8);
        double bw = SpacingRules.GetBarlineWidth(BarlineType.Single);

        double growth = at18[^1].IdealDistance - at316[^1].IdealDistance;
        Assert.Equal(EngravingDefaults.SpacingIncrement * 0.8 * (8.0 - 16.0 / 3), growth, 9);
        Assert.Equal(at18[^1].InverseStretchStrength - at316[^1].InverseStretchStrength, growth, 9);
        Assert.Equal(at316[^1].MinDistance, at18[^1].MinDistance, 12);

        // MEASURED, 2.26.0, scratch/p332/t7/pc6r (the eighths book): 8.07 bar line to bar
        // line. And its common shortest IS 1/8 — the skips cast no vote (below).
        Assert.Equal(8.07, at18[^1].IdealDistance + bw, 2);
    }

    [Fact]
    public void ASkipBar_PricesTheSameOnBothSpringSystems()
    {
        var (_, _, primary, _) = Collect(SkipBars, 1);
        var column = ColumnSprings(SkipBars, 1, GlobalShortest);
        var item = SpacingRules.CreateSpringsForMeasure(primary, GlobalShortest);

        // The item estimate has one leg per item slot, the column system one per onset;
        // for a lone skip both are one, and the chains agree in every sum.
        Assert.Equal(column.Length, item.Length);
        Assert.Equal(column.Sum(s => s.IdealDistance), item.Sum(s => s.IdealDistance), 9);
        Assert.Equal(column.Sum(s => s.MinDistance), item.Sum(s => s.MinDistance), 9);
        Assert.Equal(column.Sum(s => s.InverseStretchStrength), item.Sum(s => s.InverseStretchStrength), 9);
        Assert.Equal(column.Sum(s => s.InverseCompressStrength), item.Sum(s => s.InverseCompressStrength), 9);
    }

    [Fact]
    public void StaffSkips_CastNoVoteForTheCommonShortest()
    {
        // LILYPOND-REF: lily/spacing-engraver.cc:176-197 add_starter_duration — a column
        // votes with the rhythmic grobs it holds, and a skip holds none. Three skip bars
        // beside one bar of eighths and one of quarters: the eighth wins the tie (the
        // shorter duration on equal counts), so gs = 1/8 — not the whole note the skips
        // would have voted three times over.
        var tree = SyntaxTree.Parse(SkipBarsUnderEighths);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Equal(1.0 / 8, SpacingRules.CalculateCommonShortestDuration(score), 12);

        // Control: the same three bars written as quarters DO vote, and outvote the eighth.
        var control = SyntaxTree.Parse(SkipBarsUnderEighths.Replace("s1 | s1 | s1 |", "c4 d e f | c4 d e f | c4 d e f |"));
        var controlScore = new MeasureCollector().CollectMultiStaff(control, RenderSpecParser.FindFirst(control)!);
        Assert.Equal(3.0 / 16, SpacingRules.CalculateCommonShortestDuration(controlScore), 12);
    }

    [Fact]
    public void ADrawnRest_KeepsItsColumn()
    {
        // `r1` is a Rest grob in the musical column: the bar is USED and spaced as a bar of
        // music (bar line → rest through Staff_spacing, rest → bar line through
        // Note_spacing), so its first leg is the BarLine space-alist gap, not a zero.
        var springs = ColumnSprings(SkipBars.Replace("| s1 |", "| r1 |"), 1, GlobalShortest);
        Assert.True(springs[0].IdealDistance > 0.5,
            $"a rested bar keeps its bar-line → column spring: {springs[0].IdealDistance}");
    }

    [Fact]
    public void APercentCoveredBar_IsAnEmptyBar_ButABeatSlashBarIsNot()
    {
        // The covered bar of a measure-wide percent repeat holds a spacer run and its sign
        // is a spanner between the bar-line columns — an empty bar, priced as the skip bar
        // is. MEASURED, 2.26.0, scratch/p332/t7 pc3 against pc5: 5.51 for both.
        const string covered = """
            octave absolute
            part bassline {
              clef bass
              section A { c4 d e f | repeat percent 2 { g,4 a, b, c } | d4 e f g | }
            }
            form main { A }
            score main { staff bassline }
            """;
        var coveredSprings = ColumnSprings(covered, 2, GlobalShortest);
        double bw = SpacingRules.GetBarlineWidth(BarlineType.Single);
        Assert.Equal(0.0, coveredSprings[0].IdealDistance);
        Assert.Equal(5.51, coveredSprings[^1].IdealDistance + bw, 2);

        // A body shorter than a bar is repeated as BEAT SLASHES, and a RepeatSlash is a
        // rhythmic grob in the musical column: the bar holding nothing but slashes is
        // USED and keeps its ordinary chain.
        // LILYPOND-REF: scm/define-grobs.scm:2909-2918 RepeatSlash rhythmic-grob-interface.
        const string slashes = """
            octave absolute
            part bassline {
              clef bass
              section A { repeat percent 8 { c8 d } | d4 e f g | }
            }
            form main { A }
            score main { staff bassline }
            """;
        var (_, _, _, slashScore) = Collect(slashes, 1);
        Assert.True(slashScore.PercentRepeats.Any(p => p.IsBeatSlash && p.MeasureIndex == 1),
            "the second bar should hold beat slashes");
        var slashSprings = ColumnSprings(slashes, 1, GlobalShortest);
        Assert.True(slashSprings[0].IdealDistance > 0.5,
            $"a bar of beat slashes keeps its bar-line → column spring: {slashSprings[0].IdealDistance}");
    }

    /// <summary>test/tab-percent-blank-bars, staff only — a THREE-bar slash body.</summary>
    private const string SlashBody = """
        octave absolute
        time 4/4
        part bl {
          clef bass
          section A {
            repeat percent 2 { a,,8 a,, a,, a,, c c c c | d,4 d, d, d, | g,2 g, | }
            r1 |
          }
        }
        form main { A }
        score main { staff bl }
        """;

    [Fact]
    public void ABarInsideARepetition_CannotBreak_AndTakesTheDurationSpaceBranch()
    {
        // A three-bar body repeats as ONE RepeatSlash event that sounds for three bars, so
        // the two bar lines INSIDE the repetition forbid a line break (LilyPond's
        // Forbid_line_break_engraver) — and a bar beside a non-breakable bar line is priced
        // by standard_breakable_column_spacing's OTHER branch: min_dist + the duration space
        // of the bar, not the linear formula.
        var tree = SyntaxTree.Parse(SlashBody);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        Assert.Equal(7, measures.Length);
        // Bars 4-6 are the repetition: the slash bar and two blank bars. The bar lines after
        // bar 4 and bar 5 are inside the event; the one after bar 6 ends it.
        Assert.Equal(BreakPermission.Forbid, measures[3].LineBreakPermission);
        Assert.Equal(BreakPermission.Forbid, measures[4].LineBreakPermission);
        Assert.NotEqual(BreakPermission.Forbid, measures[5].LineBreakPermission);
        Assert.NotEqual(BreakPermission.Forbid, measures[2].LineBreakPermission);

        // The piece's common shortest is the eighth (a tie of one bar each, shortest wins;
        // the slash and the blank bars cast no vote).
        double gs = SpacingRules.CalculateCommonShortestDuration(score);
        Assert.Equal(1.0 / 8, gs, 12);

        // Blank bar 5 (both bar lines forbidden) and blank bar 6 (left forbidden, right
        // allowed) both take the duration-space branch: 0.39 + (2 + log2 8) × 1.2.
        // MEASURED, 2.26.0, scratch/p333/fx/tab-percent-blank-bars ALLCOL: 6.39 and 6.39,
        // column to column, against 8.07 for the linear branch at the same gs.
        double bw = SpacingRules.GetBarlineWidth(BarlineType.Single);
        foreach (int bar in new[] { 4, 5 })
        {
            var springs = ColumnSprings(SlashBody, bar, gs);
            Assert.Equal(0.0, springs[0].IdealDistance);
            double expected = SpacingRules.MmrRodMinimumDistance(BarlineType.Single, null)
                              + SpacingRules.CalculateDurationSpace(new Fraction(1, 1), gs);
            Assert.Equal(expected - bw, springs[^1].IdealDistance, 9);
            Assert.Equal(6.39, springs[^1].IdealDistance + bw, 2);
            // The default strengths of Spring (ideal, min_dist): stretch = ideal (LilyPond's
            // column-to-column ideal, not the re-framed one), compress = ideal − min.
            Assert.Equal(expected, springs[^1].InverseStretchStrength, 9);
            Assert.Equal(expected - SpacingRules.MmrRodMinimumDistance(BarlineType.Single, null),
                springs[^1].InverseCompressStrength, 9);
        }

        // Control: the slash bar itself holds a RepeatSlash grob — used, ordinary chain.
        var slashBar = ColumnSprings(SlashBody, 3, gs);
        Assert.True(slashBar[0].IdealDistance > 0.5,
            $"the slash bar keeps its bar-line → column spring: {slashBar[0].IdealDistance}");
    }

    [Fact]
    public void ADoublePercentPair_ForbidsTheBreakBetweenItsBars_AndBothTakeTheDurationSpace()
    {
        // scratch/p332/t7/pc4r.lys: a two-bar body repeated three times. The bar line
        // between the two bars of every DoublePercent pair is forbidden (its engraver says
        // "Prevent breaks over percent sign"), so BOTH bars of the pair take the
        // duration-space branch — at gs 3/16, 0.39 + 5.30 = 5.69 column to column — and
        // the sign centred on that bar line reaches 1.878823 into each of them.
        // MEASURED, 2.26.0, scratch/p333/fx/pc4r ALLCOL + dp-settings: 5.69 / 9.26 column
        // to column (min_dist 0.39 / 3.96 = the sign's 3.757645 + 0.2), which in the
        // bar-line frame — the sign's left half in the first bar, its right half in the
        // second — is 7.57 / 7.38 bar line to bar line, LilyPond's own PROBEBAR figures.
        const string pair = """
            octave absolute
            paper { raggedRight }
            part bassline {
              clef bass
              section A {
                c4 d e f | repeat percent 3 { g,4 a, b, c | d4 e f g | } a4 b c d |
              }
            }
            form main { A }
            score main { staff bassline }
            """;
        var tree = SyntaxTree.Parse(pair);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        Assert.Equal(8, measures.Length);
        // Pairs at bars 4-5 and 6-7: the break after bar 4 and after bar 6 is forbidden,
        // the one after bar 5 (between the pairs) and after bar 7 is not.
        Assert.Equal(BreakPermission.Forbid, measures[3].LineBreakPermission);
        Assert.NotEqual(BreakPermission.Forbid, measures[4].LineBreakPermission);
        Assert.Equal(BreakPermission.Forbid, measures[5].LineBreakPermission);
        Assert.NotEqual(BreakPermission.Forbid, measures[6].LineBreakPermission);
        // The body's music is the first iteration; the break after ITS bars stays free.
        Assert.NotEqual(BreakPermission.Forbid, measures[1].LineBreakPermission);

        double bw = SpacingRules.GetBarlineWidth(BarlineType.Single);
        double half = PercentRepeatEngraver.DoublePercentInkWidth(1.0) / 2;
        double min0 = SpacingRules.MmrRodMinimumDistance(BarlineType.Single, null);
        double space = SpacingRules.CalculateDurationSpace(new Fraction(1, 1), GlobalShortest);
        var first = ColumnSprings(pair, 3, GlobalShortest);
        var second = ColumnSprings(pair, 4, GlobalShortest);
        // First bar: the right column's left reach grows by the sign's half width.
        Assert.Equal(min0 + half + space - bw, first[^1].IdealDistance, 9);
        Assert.Equal(7.57, first[^1].IdealDistance + bw, 2);
        // Second bar: the left column's right reach is the sign's half width + 0.1 (past
        // the bar line's own 0.19 + 0.1).
        Assert.Equal(half + 0.1 + 0.1 + space - bw, second[^1].IdealDistance, 9);
        Assert.Equal(7.38, second[^1].IdealDistance + bw, 2);
        // ...and the linear branch would have said 5.51: the two branches are told apart.
        Assert.NotEqual(5.51, System.Math.Round(first[^1].IdealDistance + bw, 2));
    }

    [Fact]
    public void TheDoublePercentSign_IsAsWideAsLilyPondsStencil()
    {
        // MEASURED, 2.26.0 (scratch/p333/fx dp-settings.ly, DoublePercentRepeat X-extent):
        // 3.757645 on a notation staff (52.437745 .. 56.195390) and 5.636468 on a
        // four-string tab at staff-space 1.5 (27.332634 .. 32.969101). The geometry is the
        // renderer's own — one stencil, drawn and reserved.
        Assert.Equal(3.757645, PercentRepeatEngraver.DoublePercentInkWidth(1.0), 6);
        Assert.Equal(5.636468, PercentRepeatEngraver.DoublePercentInkWidth(1.5), 6);
        // The sign never widens the group by its dots: the dot's overlap (0.75·ss) exceeds
        // its diameter, so the extent is the slash group's alone.
        var g = PercentRepeatEngraver.Geometry(isBeatSlash: false, slashCount: 0, isDouble: true, 1.0);
        Assert.True(g.DotKern > 2 * EngravingDefaults.RepeatDotRadius);
        Assert.Equal(g.SlashInk + g.PairGap, g.GroupWidth, 12);
    }

    [Fact]
    public void AChordSymbolOverASkip_KeepsTheColumn()
    {
        // LILYPOND-REF: scm/define-grobs.scm:837-855 ChordName rhythmic-grob-interface —
        // the symbol is a grob in the column, so the bar it names is used even when the
        // staff under it holds nothing but a skip.
        const string named = """
            octave absolute
            part melody { clef treble }
            section Main {
              melody { c4 d e f | s1 | c4 d e f | }
              chords prog { C | F | C | }
            }
            form main { Main }
            score main "x" { chords prog staff melody }
            """;
        var springs = ColumnSprings(named, 1, GlobalShortest);
        Assert.True(springs[0].IdealDistance > 0.0,
            $"a chord symbol keeps the skip bar's columns: {springs[0].IdealDistance}");

        // ...and without the symbol, the same bar is empty.
        var bare = ColumnSprings(named.Replace("C | F | C |", "C | | C |"), 1, GlobalShortest);
        Assert.Equal(0.0, bare[0].IdealDistance);
    }
}
