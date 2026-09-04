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
/// A skip's column is UNUSED in LilyPond — it holds no grob — and leaves the spacing
/// problem, so the notes on either side of it become neighbours and the leg between them
/// (or from the last note to the bar line) spans the skip's time as a FRACTION of the
/// note's own spring. These tests pin the drop, the two spring systems' agreement over it,
/// and the figures against the 2.26.0 measurements the port was made from
/// (scratch/p333/ps, ALLCOL and ragged bar-line dumps).
/// </summary>
[Trait("Category", "Unit")]
public class SkipColumnSpacingTests
{
    private const double GlobalShortest = EngravingDefaults.BaseShortestDuration; // 3/16

    /// <summary>scratch/p333/ps/ps1.lys.</summary>
    private const string OneVoice = """
        octave absolute
        paper { raggedRight }
        part bassline {
          clef bass
          section A {
            c4 d e f | c4 s2. | c4 d e f | c8 s8 c8 s8 c4 s4 | c4 d e f |
          }
        }
        form main { A }
        score main { staff bassline }
        """;

    /// <summary>scratch/p333/ps/ps2.lys — one voice sustains through the other's skip.</summary>
    private const string TwoVoices = """
        octave absolute
        paper { raggedRight }
        part bassline {
          clef bass
          section A {
            voice { c4 d e f | c2 s2 | c4 d e f | }
            { s4 s s s | s4 e4 s2 | s4 s s s | }
          }
        }
        form main { A }
        score main { staff bassline }
        """;

    /// <summary>scratch/p333/ps/ps3.lys — the bar opens with a skip.</summary>
    private const string SkipOpened = """
        octave absolute
        paper { raggedRight }
        part bassline {
          clef bass
          section A {
            c4 d e f | s4 c4 d e | c4 d e f | s2 c4 d | c4 d e f |
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

    /// <summary>The timing-column chain as the layout and the break gate read it.</summary>
    private static System.Collections.Immutable.ImmutableArray<Spring> ColumnSprings(
        string src, int measureIndex)
    {
        var (timings, allMeasures, primary, score) = Collect(src, measureIndex);
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        var springs = new MeasureLayouter().CreateTimingSprings(
            primary, timings, GlobalShortest, allMeasures,
            measureIndex + 1 < measures.Length ? measures[measureIndex + 1] : null,
            MultiStaffLayouter.CollectStaffIndicesAtIndex(score, measureIndex),
            SpacingRules.RunLeftBoundBarline(measures, measureIndex));
        return MultiStaffLayouter.ApplySharedColumnReservations(
            score, measureIndex, springs, primary, timings, allMeasures, GlobalShortest);
    }

    /// <summary>Bar line to bar line, as the ragged probes read it: the content chain plus
    /// the bar's own end line.</summary>
    private static double BarToBar(string src, int measureIndex)
    {
        var (_, _, primary, _) = Collect(src, measureIndex);
        return ColumnSprings(src, measureIndex).Sum(s => s.IdealDistance)
               + SpacingRules.GetBarlineWidth(primary.EndBarline);
    }

    [Fact]
    public void ASkipAfterANote_HasNoColumn()
    {
        // `c4 s2.`: one kept onset. `c8 s8 c8 s8 c4 s4`: three.
        Assert.Equal(new[] { Fraction.Zero }, Collect(OneVoice, 1).Timings);
        Assert.Equal(
            new[] { Fraction.Zero, new Fraction(1, 4), new Fraction(1, 2) },
            Collect(OneVoice, 3).Timings);
        // The two-voice bar: the c2's onset and the e4's; the skips' onsets in the other
        // voice add nothing, whether a note sounds through them (LP regression
        // beam-skip.ly) or not.
        Assert.Equal(new[] { Fraction.Zero, new Fraction(1, 4) }, Collect(TwoVoices, 1).Timings);
        // Control: a bar with no skip keeps every onset.
        Assert.Equal(4, Collect(OneVoice, 0).Timings.Count);
    }

    [Fact]
    public void ANoteFollowedByASkip_RunsToTheBarAsAFractionOfItsOwnSpring()
    {
        // `c4 s2.`: the note's leg to the bar spans the whole bar — delta_t 1 over the
        // quarter's shortest_playing 1/4 is fraction 4 of the quarter's spring, refined by
        // the left head and the stem, NOT a whole note's logarithmic duration space.
        // MEASURED, 2.26.0, scratch/p333/ps/ps1: 12.75 bar line to bar line (1.23 + 11.52);
        // Lily# read 9.03 while the skip kept a column.
        var springs = ColumnSprings(OneVoice, 1);
        Assert.Equal(2, springs.Length);
        double quarter = SpacingRules.CalculateDurationSpace(Fraction.Quarter, GlobalShortest);
        // The base of the last leg is four quarters' space; the refinement adds the head
        // width term and the stem correction on top, so the leg lies within a head of it.
        Assert.InRange(springs[^1].IdealDistance, 4 * quarter - 1.5, 4 * quarter + 1.5);
        Assert.Equal(12.75, BarToBar(OneVoice, 1), 2);

        // `c8 s8 c8 s8 c4 s4`: three legs, each a fraction of its note's own spring
        // (2 × eighth, 2 × eighth, 2 × quarter). MEASURED: 15.16.
        Assert.Equal(15.16, BarToBar(OneVoice, 3), 2);

        // Two voices: e4 at beat two runs to the bar over the c2's and its own skips —
        // fraction 3 of the quarter (the c2 still sounds, but shortest_playing is the
        // shorter of the two). MEASURED: 11.811; Lily# 11.795 — a 0.017 residual left
        // where it is (the two-voice stem/head merge of the last leg, not this port's
        // quantity; the one-voice bars above are exact), asserted to the tenth so the
        // 2.4-space defect this port closed cannot come back unnoticed.
        Assert.Equal(11.81, BarToBar(TwoVoices, 1), 1);
    }

    [Fact]
    public void ABarOpeningWithASkip_TakesTheDurationSpaceBranchToItsFirstNote()
    {
        // `s4 c4 d e`: the bar line's neighbour is the note column a quarter later — a
        // dt != 0 pair with no Staff_spacing wish: min_dist + duration_space (1/4), default
        // strengths. MEASURED, 2.26.0, scratch/p333/ps/ps3 ALLCOL: 3.288 column to column
        // = 0.39 + 2.898; the bar 12.15. `s2 c4 d`: 4.488 = 0.39 + 4.098; the bar 10.35.
        double bw = SpacingRules.GetBarlineWidth(BarlineType.Single);
        var beatTwo = ColumnSprings(SkipOpened, 1);
        Assert.Equal(3.288, beatTwo[0].IdealDistance + bw, 3);
        Assert.Equal(12.15, BarToBar(SkipOpened, 1), 2);
        var beatThree = ColumnSprings(SkipOpened, 3);
        Assert.Equal(4.488, beatThree[0].IdealDistance + bw, 3);
        Assert.Equal(10.35, BarToBar(SkipOpened, 3), 2);
        // Default strengths: stretch = LilyPond's ideal (column frame), compress = ideal − min.
        Assert.Equal(beatTwo[0].IdealDistance + bw, beatTwo[0].InverseStretchStrength, 9);
        Assert.Equal(beatTwo[0].IdealDistance - beatTwo[0].MinDistance, beatTwo[0].InverseCompressStrength, 9);
    }

    [Fact]
    public void BothSpringSystems_AgreeOverADroppedSkipColumn()
    {
        // The single-measure estimate drops the same columns and prices the same legs.
        foreach (var (src, bar) in new[] { (OneVoice, 1), (OneVoice, 3), (SkipOpened, 1), (SkipOpened, 3) })
        {
            var (_, _, primary, _) = Collect(src, bar);
            var column = ColumnSprings(src, bar);
            var item = SpacingRules.CreateSpringsForMeasure(primary, GlobalShortest);
            Assert.Equal(column.Length, item.Length);
            for (int i = 0; i < column.Length; i++)
                Assert.Equal(column[i].IdealDistance, item[i].IdealDistance, 9);
        }
    }

    [Fact]
    public void ASkipAfterTheOnlyNote_DefeatsFullMeasureExtraSpace()
    {
        // LilyPond's fills_measure looks at the column of the next RANK, which for `c4 s2.`
        // is the skip's unused column, so the bar line → note spring gets NO
        // full-measure-extra-space. MEASURED: 1.23 = 0.19 + 0.9 + the stem correction,
        // where a whole note's bar reads 2.23.
        var springs = ColumnSprings(OneVoice, 1);
        Assert.True(springs[0].IdealDistance < EngravingDefaults.BarLineToNextNoteSpace + 0.5,
            $"no full-measure-extra-space after a skip-defeated fills_measure: {springs[0].IdealDistance}");
        var whole = ColumnSprings(OneVoice.Replace("| c4 s2. |", "| c1 |"), 1);
        Assert.True(whole[0].IdealDistance > EngravingDefaults.BarLineToNextNoteSpace + 0.9,
            $"a whole note's bar keeps it: {whole[0].IdealDistance}");
    }
}
