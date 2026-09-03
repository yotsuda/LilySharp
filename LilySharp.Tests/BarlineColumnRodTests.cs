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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The minimum a bar takes under compression is LilyPond's column rods, the bar-line pairs
/// included. Every expected number below is LilyPond 2.26.0's own rod, read off the paper
/// column's <c>minimum-distances</c> in a line compressed to its minimum
/// (scratch/p323/fx/m-*.ly, session 323): a rod is skyline distance + 0.1, and a skyline
/// distance is ink + the two extra-spacing-widths (0.1 each).
/// The bar priced here is HANDOFF §2 T7 F12's <c>fis,,2 fis,,8 fis,, r cis,</c>
/// (scratch/p322/fx/w-h8-bass-fis.lys): LilyPond's bar-to-bar minimum is 9.0432 where
/// Lily# priced 8.348, and the whole gap was four per-bar terms — the up-stem FLAG before
/// the bar line (−0.86), the bar line → note rod (−0.10), an eighth rest boxed as a
/// notehead (+0.30) and a half → eighth pair measured between head centres (−0.04).
/// </summary>
[Trait("Category", "Unit")]
public sealed class BarlineColumnRodTests
{
    private const string Head = """
        octave absolute
        key fis major
        time 4/4
        part m {
          clef bass
          section A {

        """;

    private const string Tail = """

          }
        }
        form main { A }
        score main { staff m }
        """;

    private static MultiStaffScore ScoreOf(string bar)
    {
        var tree = SyntaxTree.Parse(Head + bar + " | " + bar + " |" + Tail);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static Measure FirstBar(MultiStaffScore score)
        => score.PrimaryContentStaff.PrimaryVoice.Measures[0];

    /// <summary>The bar's timing springs, exactly as the break gate and the layout build them.</summary>
    private static (ImmutableSprings Springs, Measure Bar) TimingSprings(string bar)
    {
        var score = ScoreOf(bar);
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        var springs = new MeasureLayouter().CreateTimingSprings(
            measures[0], MultiStaffLayouter.CollectAllTimingsForMeasure(score, 0),
            SpacingRules.CalculateCommonShortestDuration(score),
            MultiStaffLayouter.CollectAllMeasuresAtIndex(score, 0), measures[1],
            MultiStaffLayouter.CollectStaffIndicesAtIndex(score, 0));
        return (new ImmutableSprings(springs), measures[0]);
    }

    private sealed record ImmutableSprings(System.Collections.Immutable.ImmutableArray<Spring> Items);

    [Fact]
    public void UpStemFlaggedEighth_ReachesTheBarLineByItsFlag()
    {
        // m-base.ly: the flagged eighth → bar line rod is 2.3674 = stem 1.2392 + flag 0.8282
        // + 0.1 + 0.1 + 0.1; its spring minimum is the rod less the spanner padding.
        var bar = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, r cis,"));
        var last = (NoteItem)bar.Items[^1];
        Assert.True(last.StemUp && !last.IsBeamed);
        var (skyMin, rod) = SpacingRules.NoteColumnToBarlineFloorPair(last);
        Assert.Equal(2.3674, rod, 4);
        Assert.Equal(2.2674, skyMin, 4);
    }

    [Fact]
    public void DownStemFlaggedEighth_StaysInsideItsHead()
    {
        // m-gis.ly: the down flag hangs at the head's LEFT edge and reaches 1.1318 — inside
        // the head's 1.3042 — so the rod is the head-only 1.6042. Positive control for the
        // flag being the term, not the eighth.
        var bar = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, r gis,"));
        var last = (NoteItem)bar.Items[^1];
        Assert.True(!last.StemUp && !last.IsBeamed);
        Assert.Equal(1.6042, SpacingRules.NoteColumnToBarlineFloorPair(last).Rod, 4);
    }

    [Fact]
    public void QuarterBeforeTheBarLine_IsHeadOnly()
    {
        // m-q.ly: no flag, no dot — 1.3042 + 0.3.
        var bar = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, cis,4"));
        Assert.Equal(1.6042, SpacingRules.NoteColumnToBarlineFloorPair(bar.Items[^1]).Rod, 4);
    }

    [Fact]
    public void EighthRestBeforeAnEighth_IsBoxedAsTheRestGlyph()
    {
        // m-base.ly: rest → note rod 1.300000 = the eighth rest's 1.0 + 0.3; a black-head
        // box priced it 1.6042.
        var bar = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, r cis,"));
        var rest = bar.Items[3];
        Assert.IsType<RestItem>(rest);
        Assert.Equal(1.3, SpacingRules.SeparationRodDistance(rest, bar.Items[4], 0), 4);
        Assert.Equal(1.2, SpacingRules.CalculateSkylineDistance(rest, bar.Items[4], 0), 4);
    }

    [Fact]
    public void HalfBeforeAnEighth_IsMeasuredFromTheColumnOrigin()
    {
        // m-base.ly: half → eighth rod 1.6774 = the half head's 1.3774 + 0.3. Between the
        // two heads' CENTRES it read 1.6408 — half of each width, not the left head's whole.
        var bar = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, r cis,"));
        Assert.Equal(1.6774, SpacingRules.SeparationRodDistance(bar.Items[0], bar.Items[1], 0), 4);
        Assert.Equal(1.6042, SpacingRules.SeparationRodDistance(bar.Items[1], bar.Items[2], 0), 4);
    }

    [Fact]
    public void BarLineToFirstNote_CarriesTheRod()
    {
        // m-base.ly: bar line origin → next column 0.49 = 0.19 of ink + 0.3; the spring
        // starts at the ink's right edge.
        var bar = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, r cis,"));
        var spring0 = SpacingRules.BarlineToFirstColumnSpring(new[] { bar.Items[0] }, fillsMeasure: false);
        Assert.Equal(0.3, spring0.MinDistance, 9);
        // The ideal and the strengths are Staff_spacing's, untouched by the rod.
        Assert.Equal(EngravingDefaults.BarLineToNextNoteSpace, spring0.IdealDistance, 9);
        Assert.Equal(0.4, spring0.InverseCompressStrength, 9);
    }

    [Fact]
    public void TheBar_CompressesToLilyPondsMinimum()
    {
        // The whole bar, bar line to bar line: 0.49 + 1.6774 + 1.6042 + 1.6042 + 1.3 + 2.3674
        // = 9.0432 (m-base.ly, every column at its rod). Lily# read 8.348 before the four
        // terms above were closed.
        var (springs, bar) = TimingSprings("fis,,2 fis,,8 fis,, r cis,");
        double min = springs.Items.Sum(s => s.MinDistance)
                     + SpacingRules.GetBarlineWidth(bar.StartBarline)
                     + SpacingRules.GetBarlineWidth(bar.EndBarline);
        Assert.Equal(9.0432, min, 3);
        Assert.Equal(new[] { 0.3, 1.6774, 1.6042, 1.6042, 1.3, 2.3674 },
                     springs.Items.Select(s => System.Math.Round(s.MinDistance, 4)).ToArray());
    }

    [Fact]
    public void FlaggedNoteOnTheLeft_TakesNoStemCorrection()
    {
        // note-spacing.cc:264-266: a flag hanging from the left stem returns before any
        // correction — toward a note and toward the bar line alike. The quarter in the same
        // seat (m-q.ly's bar) keeps its correction, so the zero is the gate and not an
        // absent stem.
        var flagged = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, r cis,"));
        var cis8 = flagged.Items[^1];
        Assert.Equal(0.0, SpacingRules.CalculateStemCorrectionToBarline(cis8, NoteSpacingParameters.Default), 9);
        Assert.Equal(0.0, SpacingRules.CalculateStemCorrection(cis8, flagged.Items[0], NoteSpacingParameters.Default), 9);

        var quarter = FirstBar(ScoreOf("fis,,2 fis,,8 fis,, cis,4"));
        Assert.NotEqual(0.0, SpacingRules.CalculateStemCorrectionToBarline(quarter.Items[^1], NoteSpacingParameters.Default));
    }

    [Fact]
    public void TheBar_NaturalWidthMatchesLilyPond()
    {
        // w-h8-bass-fis-lp.out: bar 2 spans 15.8432 bar line to bar line. The flag's
        // headroom (skyline 2.2674 + 0.3 = 2.5674 > the duration ideal) is part of it.
        var (springs, bar) = TimingSprings("fis,,2 fis,,8 fis,, r cis,");
        double ideal = springs.Items.Sum(s => s.IdealDistance)
                       + SpacingRules.GetBarlineWidth(bar.StartBarline)
                       + SpacingRules.GetBarlineWidth(bar.EndBarline);
        Assert.Equal(15.8432, ideal, 2);
        Assert.Equal(2.5674, springs.Items[^1].IdealDistance, 4);
    }
}
