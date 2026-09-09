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
/// A column pair that two VOICES of one staff occupy — one voice on the left column, the
/// other on the right — is spanned by no NoteSpacing wish in LilyPond (each voice's
/// engraver chains its own rhythmic grobs), so it is priced by the wishless spring — the
/// bare duration ideal, minimum 0 — and floored by the column ROD alone: no left-head
/// refinement, no skyline minimum, no merge_springs headroom. A skip is not an endpoint
/// of a wish either (it engraves no grob). And a beam the voice props turn round carries
/// its pure tip on the side its stems now point. All three MEASURED on LilyPond 2.26.0
/// against test/beam-over-stem (scratch/p361/lp/bos.lys, its NoteSpacing wishes, column
/// skylines and columns dumped), whose bar 2 read +0.33 before this.
/// LILYPOND-REF: lily/note-spacing-engraver.cc:31-37 (the wish chain is a per-Voice
///   member); lily/spacing-spanner.cc:380-391 musical_column_spacing (springs.empty ():
///   min 0, the base spring); lily/spacing-spanner.cc:228-297 set_column_rods.
/// </summary>
[Trait("Category", "Unit")]
public class CrossVoiceColumnSpacingTests
{
    /// <summary>test/beam-over-stem, verbatim: bar 2 is `b8 b s2.` under `s16 d''4 s8. s2`.</summary>
    private const string BeamOverStem = """
        octave absolute
        part mel { clef treble }
        section S {
          mel {
            voice { b8 b s2. | b8 b s2. | b8 b s2. | }
            { s16 d''16 d'' d'' s2. | s16 d''4 s8. s2 | s1 | }
          }
        }
        form main { S }
        score main "beam-over-stem" { staff mel }
        """;

    /// <summary>Voice two's quarter starts where voice one's is a SKIP: the pair c'4 → c'4
    /// is voice one's note into voice two's, and the skip beside the second is no wish.</summary>
    private const string SkipBesideNote = """
        octave absolute
        part mel { clef treble }
        section S {
          mel {
            voice { c'4 s4 c'2 | }
            { s4 c'4 s2 | }
          }
        }
        form main { S }
        score main { staff mel }
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
        double gs = SpacingRules.CalculateCommonShortestDuration(score);
        var springs = new MeasureLayouter().CreateTimingSprings(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled, primary, timings, gs, allMeasures,
            measureIndex + 1 < measures.Length ? measures[measureIndex + 1] : null,
            SpacingRules.RunLeftBoundBarline(measures, measureIndex));
        return MultiStaffLayouter.ApplySharedColumnReservations(
            score, measureIndex, springs, primary, timings, allMeasures, gs);
    }

    /// <summary>Bar line to bar line at force 0, as the ragged probe reads it.</summary>
    private static double BarToBar(string src, int measureIndex)
    {
        var (_, _, primary, _) = Collect(src, measureIndex);
        return ColumnSprings(src, measureIndex).Sum(s => s.Length(0))
               + SpacingRules.GetBarlineWidth(primary.EndBarline);
    }

    private static double BlackHeadRight => GlyphMetrics.GetNoteheadBBox(8).Right;

    /// <summary>
    /// b8 → d''4, a sixteenth apart, no wish: the ideal is the bare fraction of the eighth's
    /// spring (½ × 2.4 at global shortest 1/8) and the minimum is the ROD over the b8's head
    /// and the d''4's down stem — 0.1 + (1.3042 + 0.1) − (−0.1) = 1.6042, LilyPond's
    /// 40.248671 − 38.644471. The wish pricing gave 1.8042 here.
    /// </summary>
    [Fact]
    public void CrossVoicePair_IsTheBareIdealFlooredByTheRod()
    {
        var springs = ColumnSprings(BeamOverStem, 1);
        // bar line → b8 → d''4 → b8 → bar line
        Assert.Equal(4, springs.Length);

        double eighthSpace = SpacingRules.CalculateDurationSpace(new Fraction(1, 8), 0.125);
        Assert.Equal(0.5 * eighthSpace, springs[1].IdealDistance, precision: 9);
        double rod = BlackHeadRight + 2 * SpacingRules.DefaultExtraSpacingWidth
                     + SpacingRules.SeparationRodPadding;
        Assert.Equal(rod, springs[1].MinDistance, precision: 9);
        Assert.Equal(1.604200, springs[1].Length(0), precision: 6);
    }

    /// <summary>
    /// d''4 → b8, the sixteenth after: no wish either way (voice two's next grob is the bar
    /// line), so no left-head refinement — the bare 1.2, LilyPond's 41.448671 − 40.248671.
    /// The refinement (1.3042) was smuggled in by voice two's `s8.` counting as an endpoint.
    /// </summary>
    [Fact]
    public void CrossVoicePair_TakesNoLeftHeadRefinement()
    {
        var springs = ColumnSprings(BeamOverStem, 1);
        double eighthSpace = SpacingRules.CalculateDurationSpace(new Fraction(1, 8), 0.125);
        Assert.Equal(0.5 * eighthSpace, springs[2].IdealDistance, precision: 9);
        Assert.Equal(1.200000, springs[2].Length(0), precision: 6);
    }

    /// <summary>
    /// A skip standing beside a note is no wish endpoint: voice one's c'4 into voice two's
    /// c'4 keeps the bare quarter ideal, without the +0.1042 the head width would add.
    /// </summary>
    [Fact]
    public void ASkipIsNotAWishEndpoint()
    {
        var springs = ColumnSprings(SkipBesideNote, 0);
        var (_, _, _, score) = Collect(SkipBesideNote, 0);
        double gs = SpacingRules.CalculateCommonShortestDuration(score);
        double quarterSpace = SpacingRules.CalculateDurationSpace(new Fraction(1, 4), gs);
        Assert.Equal(quarterSpace, springs[1].IdealDistance, precision: 9);
    }

    /// <summary>
    /// The beam \voiceOne turns UP carries an UP pure tip: the last b8's band starts at the
    /// stem's attachment above the head and reaches its tip above it, so the correction into
    /// the bar line is (4 − 0.3724) / 7 × 0.5 × 0.5 = 0.1296 — LilyPond's, whose b8 → bar
    /// line is 17.033757 (58.482428 − 41.448671). The down tip the pitches baked read the
    /// band (−6.75 .. 0.37) and 0.1562 here.
    /// </summary>
    [Fact]
    public void ATurnedBeam_CarriesItsPureTipOnTheStemsSide()
    {
        var (_, _, primary, _) = Collect(BeamOverStem, 2);
        var last = primary.Items.OfType<NoteItem>().Last();
        Assert.True(last.IsBeamed);
        Assert.True(last.StemUp);
        var band = SpacingRules.StemSpacingInfo(last)!.Value;
        Assert.True(band.StemMin > 0, $"the up band must start above the head: {band.StemMin}");
        Assert.True(band.StemMax > band.StemMin + 4, $"the tip must reach past the staff: {band.StemMax}");
        double expected = (4.0 - band.StemMin) / 7.0
                          * NoteSpacingParameters.Default.StemSpacingCorrection * 0.5;
        Assert.Equal(expected,
            SpacingRules.CalculateStemCorrectionToBarline(last, NoteSpacingParameters.Default),
            precision: 9);

        var springs = ColumnSprings(BeamOverStem, 2);
        Assert.Equal(17.033757, springs[^1].Length(0), precision: 3);
    }

    /// <summary>The two bars against LilyPond's bar-line X differences: 20.927957 and
    /// 20.627957 (the ragged twin, PROBEBAR 37.554471 / 58.482428 / 79.110385).</summary>
    [Theory]
    [InlineData(1, 20.927957)]
    [InlineData(2, 20.627957)]
    public void BeamOverStemBars_MatchLilyPond(int measureIndex, double lilypond)
    {
        Assert.Equal(lilypond, BarToBar(BeamOverStem, measureIndex), precision: 2);
    }

    /// <summary>test/collision's voiceCollision: half-note seconds, the down voice a head
    /// width right.</summary>
    private const string HalfNoteSeconds = """
        time 4/4
        key c major
        part melody
        section Main {
          melody { voice { e2 f | g2 a | } { d2 e | f2 g | } }
        }
        form main { ~Main }
        score main "collision" { staff melody }
        """;

    /// <summary>Ledger book TSU's bar: whole-note seconds (`a1` under `b1`), stemless, so the
    /// distant-half collide of wholes is a FULL collide and the UP voice moves one head.</summary>
    private const string WholeNoteSeconds = """
        octave absolute
        time 12/4
        key c major
        part melody
        section Main {
          melody {
            voice { a1 tuplet 3/2 { d'''1 d'''1 d'''1 } | a1 tuplet 3/2 { d'''1 d'''1 d'''1 } | }
            { b1 b1 b1 | b1 b1 b1 | }
          }
        }
        form main { ~Main }
        score main "TSU" { staff melody }
        """;

    /// <summary>
    /// The wish's head-width refinement reads the head in the COLUMN frame — collision shift
    /// included — averaged over the pair's wishes, on the closing spring into the bar line
    /// too: every voice's last note files a wish naming the bar-line column. LilyPond's
    /// second bar of half-note seconds is 11.086 (29.714811 − 18.628735), each of its two legs
    /// carrying the down voice's 1.3774 over two wishes; without the closing leg's share the
    /// bar read 10.40.
    /// LILYPOND-REF: lily/note-spacing.cc:46-77 (left_head_end in the column frame);
    ///   lily/note-spacing-engraver.cc:109-120 (the command column joins the last wish).
    /// </summary>
    [Fact]
    public void ShiftedHeads_RefineTheClosingSpringToo()
    {
        var (_, _, _, score) = Collect(HalfNoteSeconds, 1);
        var shifts = SpacingRules.VoiceCollisionShiftsOf(score.StaffGroups[0].Staves[0]);
        // The down voice (voice 2) is a half head right; the up voice stays.
        Assert.Equal(GlyphMetrics.GetNoteheadBBox(2).Width, shifts[new VoiceItemKey(1, 2, 0)], precision: 6);
        Assert.False(shifts.ContainsKey(new VoiceItemKey(1, 1, 0)));
        Assert.Equal(11.086, BarToBar(HalfNoteSeconds, 1), precision: 2);
    }

    /// <summary>
    /// A whole-note second is a FULL collide (note-collision.cc:191-193, "like full_ for
    /// wholes and longer"): the UP voice moves 2 × 0.5 × the whole head = 1.962, as LilyPond
    /// draws ledger book TSU (the up head's X-extent in its column is (1.962 . 3.924)). The
    /// duration-log test was spelt on Lily#'s denominator until 2026-09-10, so a whole took
    /// the 0.4 distant-half shift, 1.5696.
    /// </summary>
    [Fact]
    public void WholeNoteSecond_IsAFullCollide_UpVoiceMovesAWholeHead()
    {
        var (_, _, _, score) = Collect(WholeNoteSeconds, 0);
        var shifts = SpacingRules.VoiceCollisionShiftsOf(score.StaffGroups[0].Staves[0]);
        Assert.Equal(GlyphMetrics.GetNoteheadBBox(1).Width, shifts[new VoiceItemKey(0, 1, 0)], precision: 6);
        Assert.False(shifts.ContainsKey(new VoiceItemKey(0, 2, 0)));
    }
}
