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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests detection of multi-measure rest spans in the layout pipeline.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob detection
/// </remarks>
[Trait("Category", "Unit")]
public class MultiMeasureRestLayoutTests
{
    private static ScoreLayout BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return engine.Layout(score);
    }

    [Fact]
    public void NoRests_ProducesNoMmrLayout()
    {
        var layout = BuildLayout("c4 d e f |");
        Assert.Empty(layout.MultiMeasureRestLayouts);
    }

    [Fact]
    public void SingleR1_ProducesOneMmrSpanCount1()
    {
        var layout = BuildLayout("R1 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(0, mmr.StartMeasureIndex);
        Assert.Equal(1, mmr.MeasureCount);
        Assert.True(mmr.UseChurchRest);
    }

    [Fact]
    public void R1Star4_ProducesOneMmrSpanCount4()
    {
        var layout = BuildLayout("R1*4 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(0, mmr.StartMeasureIndex);
        Assert.Equal(4, mmr.MeasureCount);
        Assert.True(mmr.UseChurchRest);
    }

    [Fact]
    public void R1Star12_ExceedsExpandLimit_UsesBigRest()
    {
        // 12 > ExpandLimit (10), so big_rest should be selected.
        var layout = BuildLayout("R1*12 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(12, mmr.MeasureCount);
        Assert.False(mmr.UseChurchRest);
    }

    [Fact]
    public void MmrAtBoundary10_StaysChurchRest()
    {
        // 10 == ExpandLimit, still church_rest (≤ ExpandLimit).
        var layout = BuildLayout("R1*10 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        Assert.Equal(10, layout.MultiMeasureRestLayouts[0].MeasureCount);
        Assert.True(layout.MultiMeasureRestLayouts[0].UseChurchRest);
    }

    [Fact]
    public void RestAndMusic_BoundaryStopsRun()
    {
        // R1*3 then notes: MMR run is exactly 3.
        var layout = BuildLayout("R1*3 c4 d e f |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        Assert.Equal(3, layout.MultiMeasureRestLayouts[0].MeasureCount);
    }

    [Fact]
    public void MusicThenRest_RunStartsAfterMusic()
    {
        // First measure has notes, then 2 measures of rest.
        var layout = BuildLayout("c4 d e f | R1*2 |");
        Assert.Single(layout.MultiMeasureRestLayouts);
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.Equal(1, mmr.StartMeasureIndex);
        Assert.Equal(2, mmr.MeasureCount);
    }

    [Fact]
    public void ConsecutiveR1_StayThreeSeparateOneBarRests()
    {
        // ONE written rest event is ONE Multi_measure_rest. \compressMMRests compresses an
        // N-measure event; it does not fuse rests that were written separately. This test
        // used to assert the opposite (one three-bar run) — replaced by the measurement.
        // LilyPond 2.26.0, scratch/lpreg/pcmsh-r1.log:
        //   \compressMMRests { R1 | R1 | R1 }  ->  3 grobs, bars=1 each, NO MMNUM
        // LILYPOND-REF: lily/multi-measure-rest-engraver.cc process_music — one spanner
        // per written event; LILYPOND-REF: lily/parser.yy:3117-3120 MULTI_MEASURE_REST.
        var layout = BuildLayout("R1 | R1 | R1 |");
        Assert.Equal(3, layout.MultiMeasureRestLayouts.Length);
        Assert.Equal(new[] { 0, 1, 2 },
            layout.MultiMeasureRestLayouts.Select(m => m.StartMeasureIndex).ToArray());
        Assert.All(layout.MultiMeasureRestLayouts, m => Assert.Equal(1, m.MeasureCount));
    }

    [Fact]
    public void R1Star3AndThreeWrittenR1_AreTheSameSilenceAndDifferentRests()
    {
        // The identity pair: both spellings are three bars of silence, so any rule that
        // grouped by "every staff rests here" makes them identical. LilyPond does not —
        // measured on 2.26.0 (scratch/lpreg/pcmsh-r1.log): R1*3 is ONE grob with bars=3
        // and MMNUM "3"; R1|R1|R1 is THREE grobs with bars=1 and no MMNUM. The engine's
        // difference between the two IS the ported rule, so this pair is what fails if
        // the written-event mark ever stops reaching FindRuns.
        var fused = Runs(Document("4/4", "R1*3 |"));
        var written = Runs(Document("4/4", "R1 | R1 | R1 |"));

        var one = Assert.Single(fused);
        Assert.Equal(3, one.Count);

        Assert.Equal(3, written.Length);
        Assert.All(written, r => Assert.Equal(1, r.Count));
    }

    [Fact]
    public void TwoWrittenR1Star2_StayTwoRunsOfTwo()
    {
        // The positive control against "the fix just re-counts N": each written event keeps
        // its OWN measure count, so two `R1*2`s are two two-bar runs — neither fused into
        // one four-bar run (the old behaviour) nor shattered into four one-bar rests (what
        // a mark leaking onto the 2nd..Nth expansion copies would produce).
        var runs = Runs(Document("4/4", "R1*2 | R1*2 |"));
        Assert.Equal(2, runs.Length);
        Assert.Equal(new[] { 0, 2 }, runs.Select(r => r.StartMeasureIndex).ToArray());
        Assert.All(runs, r => Assert.Equal(2, r.Count));
    }

    [Fact]
    public void MmrSpan_HasSensibleXRange()
    {
        var layout = BuildLayout("R1*3 |");
        var mmr = layout.MultiMeasureRestLayouts[0];
        Assert.True(mmr.EndX > mmr.StartX,
            $"MMR span EndX must be greater than StartX, got {mmr.StartX}/{mmr.EndX}");
    }

    /// <summary>Run grouping straight from the engraver, over a full score document.</summary>
    private static System.Collections.Immutable.ImmutableArray<MmrRun> Runs(string body)
    {
        var tree = SyntaxTree.Parse(body);
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);
        return MultiMeasureRestEngraver.FindRuns(multi);
    }

    private static string Document(string meter, string music) => $$"""
        time {{meter}}
        key c major
        part melody
        section Main { melody { {{music}} } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Theory]
    [InlineData("4/4", "c4 d e f", "R1*3")]
    [InlineData("3/4", "c4 d e", "R2.*3")]
    [InlineData("2/4", "c4 d", "R2*3")]
    public void FullMeasureRest_CollapsesInAnyMeter(string meter, string filler, string run)
    {
        // A multi-measure rest fills its BAR, and the bar is the prevailing meter — not a
        // whole note. LilyPond 2.24.4 renders each of these as one "3" church rest; Lily#
        // used to demand a whole-note rest, leaving every non-4/4 run as individual rests.
        // LILYPOND-REF: lily/multi-measure-rest-engraver.cc process_music.
        var runs = Runs(Document(meter, $"{filler} | {run} | {filler} |"));
        var only = Assert.Single(runs);
        Assert.Equal(1, only.StartMeasureIndex);
        Assert.Equal(3, only.Count);
    }

    [Fact]
    public void TimeChangeAtBound_OpensTheRunAndStaysInIt()
    {
        // LilyPond hangs a time change on the run's opening column as a break-aligned grob,
        // so the bar carrying it stays IN the run (count includes it) rather than being
        // ejected as a lone rest. Verified on 2.24.4: `\time 2/4 R2*3` after a 4/4 bar
        // renders one "3" church rest with the signature on its left bound.
        var runs = Runs(Document("4/4", "c4 d e f | time 2/4 R2*3 | g4 a |"));
        var only = Assert.Single(runs);
        Assert.Equal(1, only.StartMeasureIndex);
        Assert.Equal(3, only.Count);
    }

    [Fact]
    public void AllStavesResting_EachStaffGetsItsOwnSymbol()
    {
        // LilyPond's multi-measure-rest engraver runs in the Staff context, so a run —
        // which only forms when EVERY staff rests — prints one Multi_measure_rest per
        // staff. Verified on 2.24.4 (PianoStaff resting R1*4 in both staves: two church
        // rests, two counts). Lily# emitted one symbol for the whole system while
        // suppressing the per-bar rest glyphs on all staves, blanking the lower one.
        // LILYPOND-REF: lily/multi-measure-rest-engraver.cc.
        var src = """
            time 4/4
            key c major
            part melody
            part lh { clef bass }
            section Main {
              melody { c4 d e f | R1*4 | g4 a b c | }
              lh { c2 c | R1*4 | c2 c | }
            }
            form main { Main }
            score main "x" { staff melody staff lh }
            """;
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);

        // One run, two staves — and the two symbols sit at DIFFERENT heights, so this
        // cannot pass by emitting the same layout twice.
        Assert.Equal(2, layout.MultiMeasureRestLayouts.Length);
        Assert.All(layout.MultiMeasureRestLayouts, m =>
        {
            Assert.Equal(1, m.StartMeasureIndex);
            Assert.Equal(4, m.MeasureCount);
        });
        Assert.NotEqual(layout.MultiMeasureRestLayouts[0].Y,
                        layout.MultiMeasureRestLayouts[1].Y);
    }

    [Fact]
    public void ClefChangeAtBound_OpensTheRunAndStaysInIt()
    {
        // A clef change is break-aligned exactly like key and time, so the bar carrying it
        // stays IN the run. Verified on LilyPond 2.24.4: `\clef bass R1*5` after a 4/4 bar
        // renders a single "5" church rest. Lily# used to eject that bar, printing a lone
        // whole rest plus a "4" church rest.
        // The clef GLYPH is drawn on the other side of the bar line from key/time
        // (scm/define-grobs.scm:650-664 puts clef before staff-bar); that is a separate,
        // still-open drawing divergence and does not affect the grouping asserted here.
        var runs = Runs(Document("4/4", "c4 d e f | clef bass R1*5 | g4 a b c |"));
        var only = Assert.Single(runs);
        Assert.Equal(1, only.StartMeasureIndex);
        Assert.Equal(5, only.Count);
    }

    [Fact]
    public void ClefChangePartwayThroughRests_SplitsTheRun()
    {
        // The other half of the rule: a clef change PART WAY through a rest sequence starts
        // a fresh run rather than riding an existing bound. Verified on LilyPond 2.24.4:
        // `R1*2 \clef bass R1*3` renders "2" then "3", the bass clef drawn just before the
        // bar line between them — the same split key and time produce.
        var runs = Runs(Document("4/4", "c4 d e f | R1*2 | clef bass R1*3 | g4 a b c |"));
        Assert.Equal(2, runs.Length);
        Assert.Equal(1, runs[0].StartMeasureIndex);
        Assert.Equal(2, runs[0].Count);
        Assert.Equal(3, runs[1].StartMeasureIndex);
        Assert.Equal(3, runs[1].Count);
    }

    [Fact]
    public void ChangePartwayThroughRests_SplitsTheRun()
    {
        // A change PART WAY through a rest sequence starts a fresh run instead of riding an
        // existing one's bound. Verified on LilyPond 2.24.4: `R1*2 \key g\major R1*3`
        // renders "2" then "3", the signature on the between-column.
        var runs = Runs(Document("4/4", "c4 d e f | R1*2 | key g major R1*3 | g4 a b c |"));
        Assert.Equal(2, runs.Length);
        Assert.Equal(1, runs[0].StartMeasureIndex);
        Assert.Equal(2, runs[0].Count);
        Assert.Equal(3, runs[1].StartMeasureIndex);
        Assert.Equal(3, runs[1].Count);
    }
}
