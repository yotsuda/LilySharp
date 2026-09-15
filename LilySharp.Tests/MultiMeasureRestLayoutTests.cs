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
        // LilyPond 2.26.0, audit/lpreg/pcmsh-r1.log:
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
        // measured on 2.26.0 (audit/lpreg/pcmsh-r1.log): R1*3 is ONE grob with bars=3
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
        // LilyPond's multi-measure-rest engraver runs in the Voice context, so a run —
        // which only forms when EVERY staff rests — prints one Multi_measure_rest per
        // resting staff (per voice that wrote the R, see AnMmrOutsideTheFirstVoice_…).
        // Verified on 2.24.4 (PianoStaff resting R1*4 in both staves: two church
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

    private const string VoicedRests =
        "r1 | voice { s1 } { R1 } | voice { R1 } { s1 } | voice { s2 s2 } { R1 } | " +
        "voice { R1 } { R1 } | voice { s1 | s1 | s1 | } { R1*3 | } R1 | r1 |";

    [Fact]
    public void AnMmrOutsideTheFirstVoice_FormsItsRunAndDrawsInItsVoice()
    {
        // The Multi_measure_rest grob is made in whichever VOICE wrote the R, and its rod and
        // its position come with it. Measured on 2.26.0 (scratch/p389/mmr v2.ly, the twin of
        // VoicedRests): bars 2-5 are 7.890 wide like a bare R1 and the three-bar run 12.660;
        // voice two's R draws low, voice one's high, `{ R1 } { R1 }` draws both. The grouping
        // used to read each staff's FIRST voice only, so an R under a first-voice skip (bars 2
        // and 4) lost its rod (7.69 / 6.69) and drew as a plain rest at the beat.
        // LILYPOND-REF: ly/engraver-init.ly:374 Multi_measure_rest_engraver
        var runs = Runs(Document("4/4", VoicedRests));
        Assert.Equal(new[] { (1, 1), (2, 1), (3, 1), (4, 1), (5, 3), (8, 1) },
            runs.Select(r => (r.StartMeasureIndex, r.Count)).ToArray());

        var tree = SyntaxTree.Parse(Document("4/4", VoicedRests));
        var multi = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);
        Assert.Equal(
            new[] { (1, 1, -1), (2, 1, 1), (3, 1, -1), (4, 1, 1), (4, 1, -1), (5, 3, -1), (8, 1, 0) },
            layout.MultiMeasureRestLayouts
                .Select(m => (m.StartMeasureIndex, m.MeasureCount, m.VoiceDirection))
                .OrderBy(t => t.StartMeasureIndex).ThenByDescending(t => t.VoiceDirection)
                .ToArray());

        // The positive control: a voice that SOUNDS in the bar keeps it out of any run.
        Assert.Empty(Runs(Document("4/4", "voice { c'1 } { R1 } |")));
    }

    [Fact]
    public void AVoicedChurchRest_MovesWithTheStaffPositionItSetFirst()
    {
        // church_rest SETS staff-position before its symbol loop, so each symbol then takes
        // staff_position_internal's override arm: a one-bar R1 in voice two at −4 (the
        // semibreve's voiced position), but in R1*3 the run's pos is the minim's (−4), the
        // breve sits there and the semibreve two positions HIGHER. Measured on 2.26.0
        // (scratch/p389/mmr v2.ly, staff middle 11.69): R1 13.69; R1*3 breve 13.69, whole 12.69.
        // LILYPOND-REF: lily/multi-measure-rest.cc:254-266 church_rest — staff-position set first
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(Document("4/4", "voice { s1 } { R1 } | voice { s1 | s1 | s1 | } { R1*3 | }")),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        double middle = BarLines(svg)[0].Top + 2.0;

        var wholes = GlyphsIn(svg, LilySharp.Core.Svg.EmmentalerGlyphs.RestWhole);
        var breve = Assert.Single(GlyphsIn(svg, LilySharp.Core.Svg.EmmentalerGlyphs.RestDoubleWhole));
        Assert.Equal(2, wholes.Count);
        Assert.Equal(middle + 2.0, wholes[0].Y, 2);   // R1, voice two
        Assert.Equal(middle + 2.0, breve.Y, 2);       // R1*3's breve
        Assert.Equal(middle + 1.0, wholes[1].Y, 2);   // R1*3's semibreve
    }

    [Fact]
    public void AnRAgainstPlayingMusic_DrawsItsOwnCentredSymbolEveryBar()
    {
        // A written R is a Multi_measure_rest whatever the rest of the score does: the grob is
        // made by the voice that wrote it. Where another staff or voice sounds in the bar nothing
        // compresses, so it is one symbol PER BAR, centred between that bar's bar lines, at its
        // voice's position, with no count. Measured on 2.26.0 (scratch/p389/mmr v3.ly, a playing
        // bass staff under): `R1 | R1*3` four centred whole rests, no count; `{ R1 } { g2 g }`
        // centred and high (staff middle −2); `{ c''2 c'' } { R1 }` centred and low (+2). Lily#
        // used to draw each of them as an ordinary whole rest at the start of the bar — and the
        // one it did centre, it centred by suppressing EVERY rest of the bar on every staff.
        // LILYPOND-REF: ly/engraver-init.ly:374 Multi_measure_rest_engraver
        var src = """
            time 4/4
            key c major
            part top
            part bot { clef bass }
            section Main {
              top { R1 | R1*3 | voice { R1 } { g2 g } | voice { c''2 c'' } { R1 } | r4 c'' c'' c'' | }
              bot { c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | R1 | }
            }
            form main { Main }
            score main "x" { staff top staff bot }
            """;
        var tree = SyntaxTree.Parse(src);
        var multi = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Empty(MultiMeasureRestEngraver.FindRuns(multi));

        var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);
        var symbols = layout.MultiMeasureRestLayouts
            .OrderBy(m => m.StaffIndex).ThenBy(m => m.StartMeasureIndex).ThenBy(m => m.VoiceIndex)
            .ToArray();
        int top = symbols[0].StaffIndex;
        Assert.Equal(
            new[] { (top, 0, 0, 0), (top, 1, 0, 0), (top, 2, 0, 0), (top, 3, 0, 0), (top, 4, 1, 0), (top, 5, -1, 1) },
            symbols.Where(m => m.StaffIndex == top)
                .Select(m => (m.StaffIndex, m.StartMeasureIndex, m.VoiceDirection, m.VoiceIndex)).ToArray());
        var bottom = Assert.Single(symbols, m => m.StaffIndex != top);
        Assert.Equal((6, 0, 0), (bottom.StartMeasureIndex, bottom.VoiceDirection, bottom.VoiceIndex));
        Assert.All(symbols, m => Assert.Equal(1, m.MeasureCount));

        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree,
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        // Bars 2-4 of the top staff (between its 1st..4th bar lines): each whole rest is centred
        // on its bar — the glyph's left edge half its width left of the midpoint, give or take
        // half a bar line (the symbol is centred between the lines' inner edges).
        var topBars = BarLines(svg).Where(b => b.Top == BarLines(svg)[0].Top).Select(b => b.X)
            .OrderBy(x => x).ToList();
        double topMiddle = BarLines(svg)[0].Top + 2.0;
        var topWholes = GlyphsIn(svg, LilySharp.Core.Svg.EmmentalerGlyphs.RestWhole)
            .Where(g => Math.Abs(g.Y - (topMiddle - 1.0)) < 0.01).ToList();
        for (int b = 0; b < 3; b++)
        {
            double mid = (topBars[b] + topBars[b + 1]) / 2.0;
            Assert.Contains(topWholes, g => Math.Abs(g.X + 0.75 - mid) < 0.2);
        }
        // The top staff's r4 shares bar 7 with the bottom staff's R1 and still prints.
        Assert.NotEmpty(GlyphsIn(svg, LilySharp.Core.Svg.EmmentalerGlyphs.RestQuarter));
    }

    [Fact]
    public void AnMmrCentresBetweenTheBreakAlignments_NotTheBarLines()
    {
        // A multi-measure rest centres between the bounding columns' BREAK ALIGNMENTS, not their
        // bar lines: spacing-pair is (break-alignment . break-alignment), so a key or time change
        // after the opening bar line pushes the left edge right, and a clef change before the
        // closing bar line pulls the right edge left. The column is the system's: a key change on
        // the OTHER staff moves the rest too. Measured on 2.26.0 from the plain centre:
        // scratch/p389/mmr v3.ly `R2.` after \time 3/4 +1.17, lpchk keysigspace.ly bar 2 (5 sharps
        // on the other staff) +5.14, cue-clef-manually.ly bar 2 (cue clef before bar 3) −1.16.
        // LILYPOND-REF: lily/multi-measure-rest.cc:44-61 Multi_measure_rest::bar_width
        var src = """
            time 4/4
            part top
            part bot { clef bass }
            section Main {
              top { R1 | R1 | R1 | R1 | time 3/4 R2. | }
              bot { c4 d e f | key d major c4 d e f | c4 d e f | clef treble c'4 d' e' f' | time 3/4 c'4 d' e' | }
            }
            form main { ~Main }
            score main "x" { staff top staff bot }
            """;
        var tree = SyntaxTree.Parse(src);
        var multi = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);
        var system = Assert.Single(layout.Systems);
        MeasureLayout Bar(int i) => system.Measures.Single(m => m.MeasureIndex == i);
        var rests = layout.MultiMeasureRestLayouts;
        int top = rests.Min(m => m.StaffIndex);
        MultiMeasureRestLayout Rest(int i) => rests.Single(m => m.StaffIndex == top && m.StartMeasureIndex == i);
        Assert.Equal(5, rests.Count(m => m.StaffIndex == top));

        // Plain edges: bar 3 opens on music (its start) and bar 1 closes on a bare bar line (its end).
        double plainStart = Rest(2).StartX - Bar(2).X;
        double plainEnd = Bar(0).X + Bar(0).Width - Rest(0).EndX;
        double StartReach(int i) => Rest(i).StartX - Bar(i).X - plainStart;
        double EndReach(int i) => Bar(i).X + Bar(i).Width - Rest(i).EndX - plainEnd;

        Assert.True(StartReach(1) > 1.0, $"key after bar 2's bar line: {StartReach(1)}");   // staff-bar → key 1.0 alone
        Assert.True(EndReach(2) > 0.7, $"clef before bar 4's bar line: {EndReach(2)}");     // clef → staff-bar 0.7 alone
        Assert.Equal(0.0, StartReach(3), 6);                                                 // that clef is NOT after the bar line
        Assert.True(StartReach(4) > 0.75, $"time after bar 5's bar line: {StartReach(4)}");  // staff-bar → time 0.75 alone
        Assert.Equal(0.0, EndReach(1), 6);
        Assert.Equal(0.0, EndReach(3), 6);
    }

    [Fact]
    public void TwoVoicesRestingOneRun_PrintOneCount()
    {
        // Each voice's Multi_measure_rest engraver makes its own MultiMeasureRestNumber, and the
        // STAFF merges them: Merge_mmrest_numbers_engraver keeps the first of equal texts and
        // suicides the rest. Measured on 2.26.0 (scratch/p389/cond/num.ly, `<< { R1*8 } \\
        // { R1*8 } >>` with the number's after-line-breaking dumped): ONE number reaches
        // after-line-breaking; cond-mmr.svg prints one "8" per compressed segment.
        // LILYPOND-REF: scm/scheme-engravers.scm:354-370 Merge_mmrest_numbers_engraver
        var src = Document("4/4", "voice { R1*3 | } { R1*3 | } c'1 |");
        var tree = SyntaxTree.Parse(src);
        var multi = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);

        var symbols = layout.MultiMeasureRestLayouts.OrderBy(m => m.VoiceIndex).ToArray();
        Assert.Equal(2, symbols.Length);
        Assert.All(symbols, m => Assert.Equal(3, m.MeasureCount));
        Assert.Equal(new[] { true, false }, symbols.Select(m => m.DrawsCount).ToArray());

        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree,
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        Assert.Single(GlyphsIn(svg, '3'));
    }

    /// <summary>Every bar line rect of an SVG page: its left X and the staff-top Y it starts at.</summary>
    private static List<(double X, double Top)> BarLines(string svg) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<rect x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" width=\"0.19\" height=\"4.00\"")
            .Select(m => (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>All music glyphs of one codepoint: (X, Y), X-sorted.</summary>
    private static List<(double X, double Y)> GlyphsIn(string svg, char c) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[3].Value[0] == c)
            .Select(m => (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(g => g.Item1).ToList();
}
