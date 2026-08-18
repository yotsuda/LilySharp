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

    /// <summary>
    /// A grand staff whose UPPER part is transposed: it prints its own D-major signature
    /// (2 sharps) beside a concert-pitch C-major staff, so the engraved key column belongs to
    /// no score-level key at all. LilyPond's own key column here is the union of the staves'
    /// signatures (probe TSA dumps KEY ext 2.200000 over a score whose key is C major).
    /// </summary>
    private const string TransposedGrandStaff = """
        time 4/4
        key c major
        part upper { clef treble transpose d }
        part lower { clef bass }
        section Main {
          upper { c'4 d e f | g2 e | }
          lower { c4 d e f | g2 e | }
        }
        form main { Main }
        score main "x" { grandStaff { staff upper staff lower } }
        """;

    /// <summary>
    /// The break gate and the layout price a line start from ONE key model. On a score where
    /// only one staff engraves a signature — a transposed part beside a concert one — the two
    /// used to disagree by 2.650000, the gate booking the score key (C major, nothing at all)
    /// where the layout books the union of the staves' own signatures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LilyPond has ONE model — its breaker solves the real springs
    /// (lily/constrained-breaking.cc), so "what the breaker books for a line start" and "what
    /// the layout books" are the same quantity BY CONSTRUCTION, and a Lily# difference is a
    /// Lily# defect with no LilyPond measurement needed (docs/HANDOFF.md section 5.2.1-2).
    /// That is why this is a test and not a ledger point: the gate's only visible consequence
    /// is a line BREAK, which flips inside a 2.650000 window, so a corpus entry built on one
    /// would swing on a thousandth of a staff space and measure the fixture, not the engine.
    /// </para>
    /// <para>
    /// The number the two agree ON is LilyPond's: probe TSA puts the meter's ink right edge at
    /// 9.353400 (TIME anchor 7.653400 + ext 1.700000), and the ledger holds the same line start
    /// through the drawn geometry as <c>line-start.clef-to-time.mixed-key-grand-staff</c>
    /// = 6.853400. ⚠️ Which is also why the fix had to move the GATE up rather than the layout
    /// down — the ledger point is what says which side was right.
    /// </para>
    /// </remarks>
    [Fact]
    public void BreakGateAndLayout_PriceTheSameLineStart()
    {
        var (_, _, _, score) = Collect(TransposedGrandStaff);

        double clefWidth = SpacingRules.MaxClefWidth(score);
        double activeKeyInk = SpacingRules.WidestActiveKeyInk(score, 0);
        double layout = BreakAlignSpacing.CalculatePrefixWidth(
            clefWidth, activeKeyInk, includeTimeSignature: true,
            score.TimeSignature.NumeratorText, score.TimeSignature.DenominatorText);

        // The line start LilyPond gives this score, to the digit.
        Assert.Equal(9.353400, layout, precision: 6);

        // The gate reads the same model — asserted through SystemBreaker's own inputs rather
        // than by repeating its arithmetic, so a future edit that points the gate at some
        // third key cannot pass this by coincidence.
        Assert.Equal(layout, SystemBreaker.GateFirstPrefixWidth(score, clefWidth), precision: 6);

        // ⚠️ The score-level key is NOT that model here, and this is the assertion that would
        // have failed before the fix: C major books nothing, leaving the gate 2.650000 short —
        // the key's 2.200000 plus the Clef→Key and Key→Time gaps, which only open when a
        // signature is engraved.
        double scoreKeyModel = SpacingRules.CalculatePrefixWidth(
            clefWidth, score.LeadingKey, includeTimeSignature: true,
            score.TimeSignature.NumeratorText, score.TimeSignature.DenominatorText);
        Assert.Equal(2.650000, layout - scoreKeyModel, precision: 6);
    }

    /// <summary>
    /// A key change that OPENS a continuation system. Its signature is engraved in that
    /// system's PREFIX (and, as a courtesy, after the previous line's final bar line) — never
    /// as a column inside bar one. Measure index 4 opens system 2.
    /// </summary>
    private const string KeyChangeOpensSystemTwo = """
        time 4/4
        key a major
        octave absolute
        part m { clef bass }
        section S1 { m { a,4 b, cis d | a,4 b, cis d | a,4 b, cis d | a,4 b, cis d | break } }
        section S2 { key ees major
          m { aes,4 bes, c des | aes,4 bes, c des | aes,4 bes, c des | aes,4 bes, c des | } }
        form main { S1 S2 }
        score main "x" { staff m }
        """;

    /// <summary>The control: the SAME music and the SAME signature on system 2, declared up
    /// front, so no change lands on the break.</summary>
    private const string SameKeyThroughout = """
        time 4/4
        key ees major
        octave absolute
        part m { clef bass }
        section S1 { m { aes,4 bes, c des | aes,4 bes, c des | aes,4 bes, c des | aes,4 bes, c des | break } }
        section S2 {
          m { aes,4 bes, c des | aes,4 bes, c des | aes,4 bes, c des | aes,4 bes, c des | } }
        form main { S1 S2 }
        score main "x" { staff m }
        """;

    /// <summary>
    /// LilyPond's invariant, MEASURED: a key change that opens a system costs that system's
    /// line start NOTHING. On 2.26.0 the two probes below (audit/lp-geometry/probes/
    /// line-start-key-change.ly, scores A and B) put system 2's ink at the SAME places —
    /// blob starts 0.79 / 3.03 / 4.33 / 6.17 / 9.57 / 15.02 / 20.47 ss from the staff origin
    /// in both — because the new signature is engraved break-aligned in the prefix exactly
    /// like a reprinted one.
    /// <para>
    /// Lily# broke it in TWO places, and this asserts the head width half: the prefix
    /// reservation walked only measures BEFORE the system, so it booked the OUTGOING key
    /// (3 sharps, 3.30) while the renderer drew the incoming one (3 flats, 2.76) — 0.54 of
    /// line start nobody engraves.
    /// </para>
    /// </summary>
    [Fact]
    public void KeyChangeOpeningASystem_ReservesTheSignatureItDraws()
    {
        var (_, _, _, changed) = Collect(KeyChangeOpensSystemTwo);
        var (_, _, _, control) = Collect(SameKeyThroughout);

        var a = MultiStaffLayouter.SolveLineStartPrefix(changed, 4, isFirstSystem: false);
        var b = MultiStaffLayouter.SolveLineStartPrefix(control, 4, isFirstSystem: false);

        Assert.NotNull(a.LeadingKeyChange);   // the regime this test exists for
        Assert.Null(b.LeadingKeyChange);
        Assert.Equal(b.Columns.Right, a.Columns.Right, precision: 9);
    }

    /// <summary>
    /// The other half: a hoisted change is not charged to bar one. <c>ownFixedFloor</c> is
    /// Lily#'s own device — the measure's spring-0 minimum folds in leading grace / lyric
    /// widths that LilyPond keeps in separate paper columns — but a change engraved in the
    /// PREFIX is not in the measure at all, so it must not floor the line-start spring.
    /// Asserted by PERTURBING the floor rather than by pinning a number: with the change
    /// hoisted the floor is ignored, without it the floor bites. Leaving it in charged the
    /// cancellation+signature a second time and pushed the first note 5.51 ss right on
    /// scratch/repro.lys bar 9.
    /// </summary>
    [Fact]
    public void AHoistedChange_DoesNotChargeBarOneForItsColumn()
    {
        var (_, _, _, changed) = Collect(KeyChangeOpensSystemTwo);
        var (_, _, _, control) = Collect(SameKeyThroughout);

        // ownFixedFloor is a lower bound on each wish's FIXED distance, so it lands on the
        // spring's IDEAL — not on its minimum, which MinimumDistanceAtLineStart owns.
        static double IdealFor(MultiStaffScore score, double floor) =>
            MultiStaffLayouter.LineStartSpringForLine(
                score, 4, isFirstSystem: false, new Spring(0.0, floor, 1.0)).IdealDistance;

        // Hoisted: the measure's own minimum is not consulted, so moving it changes nothing.
        Assert.Equal(IdealFor(changed, 0.0), IdealFor(changed, 40.0), precision: 9);

        // Not hoisted: the same perturbation DOES move the spring — proof the assertion above
        // is about the hoist and not about an inert parameter.
        Assert.NotEqual(IdealFor(control, 0.0), IdealFor(control, 40.0), precision: 9);
    }

    /// <summary>
    /// The control for the test above: with NO transposed part every model reads the same key,
    /// so the score-level key agrees too. Without this, "the score key was 2.65 short" could be
    /// read as a constant offset rather than as the missing per-staff signature it is.
    /// </summary>
    [Fact]
    public void BreakGateAndLayout_AlreadyAgreeWhenNoStaffCarriesItsOwnKey()
    {
        var (_, _, _, score) = Collect("""
            time 4/4
            key d major
            part upper { clef treble }
            part lower { clef bass }
            section Main {
              upper { d'4 e fis g | a2 fis | }
              lower { d4 e fis g | a2 fis | }
            }
            form main { Main }
            score main "x" { grandStaff { staff upper staff lower } }
            """);

        double clefWidth = SpacingRules.MaxClefWidth(score);
        double layout = BreakAlignSpacing.CalculatePrefixWidth(
            clefWidth, SpacingRules.WidestActiveKeyInk(score, 0), includeTimeSignature: true,
            score.TimeSignature.NumeratorText, score.TimeSignature.DenominatorText);

        Assert.Equal(layout, SystemBreaker.GateFirstPrefixWidth(score, clefWidth), precision: 6);
        Assert.Equal(
            layout,
            SpacingRules.CalculatePrefixWidth(
                clefWidth, score.LeadingKey, includeTimeSignature: true,
                score.TimeSignature.NumeratorText, score.TimeSignature.DenominatorText),
            precision: 6);
    }

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
        // A time change likewise. LP grob geometry gives 3.445; Lily# lands at 3.440. The
        // ~0.005 gap is the TimeSignature grob width, and it is not a wrong constant:
        // ly:time-signature::print builds a MARKUP, so LilyPond measures the digits with
        // the text layout engine (1.604735) while its own music-font path reports exactly
        // Lily#'s 1.600000 for the same glyph. Recorded, not fudged — see the ledger's
        // barline.next.time-change-to-notehead for what has been ruled out.
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
        // a half note's open head (1.377346) is wider than a quarter's (1.304212), so the
        // half gap exceeds the quarter gap by the duration increment PLUS that
        // head-width difference.
        //
        // The head's INK extent, not its advance — that is what note-spacing.cc:68 reads
        // (g->extent (col, X_AXIS)[RIGHT]). The two differ, and this expectation used the
        // advance while the implementation used it too, so the pair agreed without either
        // being LilyPond's number.
        double durationDelta = SpacingRules.CalculateDurationSpace(new Fraction(1, 2), bsd)
                             - SpacingRules.CalculateDurationSpace(new Fraction(1, 4), bsd);
        double headDelta = GlyphMetrics.GetNoteheadBBox(2).Right - GlyphMetrics.GetNoteheadBBox(4).Right;
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
        //
        // The bound was noteWidth / 2 until the fills_measure predicate was widened to
        // LilyPond's (SpacingRules.IsMusicalColumn), which hands an R1 bar the same
        // full-measure-extra-space 1.0 a whole note gets. That is LilyPond's own
        // behaviour, measured on 2.24.4 by overriding
        // NonMusicalPaperColumn.full-measure-extra-space to 0 over
        // `c'4 d' e' f' | R1 | r1 | c'4 d' e' f'`: the R1 bar narrows by exactly
        // 1.000000, as does the r1 bar. So "under half" was never LilyPond's claim —
        // LilyPond's own R1 bar is 7.890000 against 13.525735 for four quarters, a
        // ratio of 0.583. What follows is a coarse guard on the compact rod still
        // being applied — remove the rod and the rest bar prices like an ordinary
        // one, far above either bound — and NOT a pin: the quantity here is a spring
        // sum while LilyPond's figure is a bar width, so the two are not the same
        // measurement and the threshold must not be read as a ported constant.
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
        Assert.True(restWidth < noteWidth * 0.6,
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

    /// <summary>
    /// The break gate prices a tab bar WITH its fret-digit spacing floors. The floors
    /// (SpacingRules.ApplyTabChordSpacing) protect a Lily# enlargement of LilyPond's tiny
    /// digits (TabConstants.FretFontSize), and on straight eighths they BIND — a digit's
    /// advance plus the inter-column gap exceeds an eighth's duration space — so a gate
    /// that omits them under-books exactly the bars tab books are made of. That was a
    /// real drift: the gate carried a hand-mirrored copy of the layout's reservation
    /// list, the tab entry was missing from the copy, and six bars of bass-tab eighths
    /// packed onto one system whose digits ran to x=125.03 on a 119.50 page
    /// (test/tab-line-break holds the drawn geometry). Both consumers now read ONE list
    /// — MultiStaffLayouter.ApplySharedColumnReservations — and the gate is asserted
    /// through its own output, so a re-introduced per-caller copy cannot pass this by
    /// coincidence.
    /// </summary>
    [Fact]
    public void BreakGate_PricesTabFretDigitFloors()
    {
        var (timings, allMeasures, primary, score) = Collect("""
            part melody {
              instrument bass
              section A { a8 a a a a a a a | a8 a a a a a a a | }
            }
            form main { A }
            score main "x" { tab melody }
            """);

        var next = score.PrimaryContentStaff.PrimaryVoice.Measures[1];
        var bare = new MeasureLayouter().CreateTimingSprings(primary, timings, 0.125, allMeasures, next);
        var reserved = MultiStaffLayouter.ApplySharedColumnReservations(
            score, 0, bare, primary, timings, allMeasures);

        // The floors actually bite here, so the equality below is not two equal sums
        // agreeing about nothing.
        Assert.True(reserved.Sum(s => s.MinDistance) > bare.Sum(s => s.MinDistance),
            $"digit floors must widen straight-eighth columns: "
            + $"bare={bare.Sum(s => s.MinDistance):F3}, reserved={reserved.Sum(s => s.MinDistance):F3}");

        // And the gate books the RESERVED bar (plus its bar lines), not the bare one.
        var gate = SystemBreaker.ComputeMultiStaffSpringData(score, 0.125)[0];
        double barlines = SpacingRules.GetBarlineWidth(primary.StartBarline)
                        + SpacingRules.GetBarlineWidth(primary.EndBarline);
        Assert.Equal(reserved.Sum(s => s.MinDistance) + barlines, gate.MinWidth, precision: 9);
        Assert.Equal(reserved.Sum(s => s.IdealDistance) + barlines, gate.IdealWidth, precision: 9);
    }

    /// <summary>The three-voice book of test/dot-cross-voice-spacing — a dotted half in
    /// voice three under eighths in voice two, the two mechanisms of that fixture.</summary>
    private const string DottedThirdVoice = """
        part melody {
          section A { voice { g''2( g8) eis fis g } { e8 d e e e fis r4 }  { cis2. r4 } }
        }
        form main { A }
        score main "x" { staff melody }
        """;

    /// <summary>
    /// automatic_shift, not a cascade: voice three's clash group takes the
    /// <c>else if (Stem::is_valid_stem) offset += 0.5</c> clause — half the DOWN group's
    /// first head — because its heads neither overlap nor cross voice one's.
    /// MEASURED on LilyPond 2.26.0 (the fixture's twin): cis' sits 0.652 right of the
    /// column, and 0.652 = 0.5 × the eighth's 1.3042 head. The old hand-rolled "+1 head
    /// width per later same-direction voice" put it at 1.3042 — exactly double — under a
    /// LILYPOND-REF that named automatic_shift without porting its clauses.
    /// LILYPOND-REF: lily/note-collision.cc:539-581 (the group loop), :427-437 (× the
    ///   down group's first support head).
    /// </summary>
    [Fact]
    public void ThirdVoiceShift_IsAutomaticShiftsHalfHead_NotACascade()
    {
        var (_, _, _, score) = Collect(DottedThirdVoice);
        var voices = score.StaffGroups[0].Staves[0].Voices;
        Assert.Equal(3, voices.Length);

        var offsets = ElementCoordinator.ComputeVoiceOffsets(voices).VoiceOffsets;

        // cis2. is voice 3, item 0 of measure 0. The down group's first head is the
        // e8 — a BLACK head — so the shift is half ITS ink width.
        double cisShift = offsets[new VoiceItemKey(0, 3, 0)];
        Assert.Equal(0.5 * GlyphMetrics.GetNoteheadBBox(8).Width, cisShift, precision: 9);

        // Voices one and two stay on the column (the pin only chases negative amounts).
        Assert.False(offsets.ContainsKey(new VoiceItemKey(0, 1, 0)));
        Assert.False(offsets.ContainsKey(new VoiceItemKey(0, 2, 0)));
    }

    /// <summary>
    /// The cross-voice column floor: the shifted cis2.'s DOT reaches into the next
    /// eighth column, which belongs to voice two alone — no single voice occupies both
    /// columns, so the per-voice rod loop cannot see the pair and the dot used to print
    /// straight through the d's head (same Y row, overlapping X). The floor is the
    /// staff-frame skyline distance with the collision shifts applied
    /// (SpacingRules.ApplyCrossVoiceColumnSpacing), and merge_springs' headroom rides
    /// on top of the raised minimum.
    /// LILYPOND-REF: lily/separation-item.cc:120-190 (boxes carry the shifts);
    ///   lily/note-spacing.cc:78-83 (the spring minimum); lily/spring.cc:122 (min + 0.3).
    /// MEASURED (2.26.0 twin): the first eighth gap is 3.33 against the measure's plain
    /// 2.50, and removing the dot (cis2) collapses it to 2.51 — the push is the dot's.
    /// </summary>
    [Fact]
    public void CrossVoiceDotReach_FloorsTheNextColumnsSpring()
    {
        var (timings, allMeasures, primary, score) = Collect(DottedThirdVoice);

        var bare = new MeasureLayouter().CreateTimingSprings(primary, timings, 0.125, allMeasures);
        var reserved = MultiStaffLayouter.ApplySharedColumnReservations(
            score, 0, bare, primary, timings, allMeasures);

        // Spring 1 is the t=0 → t=1/8 pair the dot crosses. The floor must BITE — this
        // is not two equal numbers agreeing — and the ideal must carry the headroom
        // above the raised minimum, so the drawn gap clears the dot by LilyPond's 0.3.
        Assert.True(reserved[1].MinDistance > bare[1].MinDistance,
            $"the dot's cross-voice reach must floor the spring: "
            + $"bare={bare[1].MinDistance:F3}, reserved={reserved[1].MinDistance:F3}");
        // Both of LilyPond's constraints, in their order: the ideal is the SKYLINE
        // distance + 0.3 (merge_springs' headroom) and the final minimum is the ROD,
        // the same distance + 0.1 — so a bound pair always shows ideal − min = 0.2.
        Assert.True(reserved[1].IdealDistance > bare[1].IdealDistance,
            $"the headroom must ride the raised minimum into the ideal: "
            + $"bare={bare[1].IdealDistance:F3}, reserved={reserved[1].IdealDistance:F3}");
        Assert.Equal(SpacingRules.SpringHeadroom - SpacingRules.SeparationRodPadding,
            reserved[1].IdealDistance - reserved[1].MinDistance, precision: 9);

        // Control: the same measure's later eighth-to-eighth pairs carry no cross-voice
        // reach, so the floor leaves them exactly as the per-voice loop priced them.
        Assert.Equal(bare[2].MinDistance, reserved[2].MinDistance, precision: 9);
    }

    // --- A CHANGE COLUMN IS ONE GROB PER KIND, NOT ONE PER STAFF (session 206) ---

    /// <summary>
    /// The same music and the same key change, on one / two / three staves.
    /// </summary>
    private static string KeyChangeOnStaves(int staffCount) => $$"""
        time 4/4
        section Main { melody { c4 c g' g | key a major g'4 g f f | } }
        form main { Main }
        score main "x" {{{StaffRows(staffCount)}}
        """;

    /// <summary>
    /// The same music with the key change struck MID-measure instead of at the bar line, so
    /// the change gets its own non-musical column rather than riding the measure's opening.
    /// </summary>
    private static string MidMeasureKeyChangeOnStaves(int staffCount) => $$"""
        time 4/4
        section Main { melody { c4 c key a major g'4 g | } }
        form main { Main }
        score main "x" {{{StaffRows(staffCount)}}
        """;

    /// <summary>
    /// The body of the score block: a bare staff for one, a grand staff for more.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOT a one-staff grandStaff for the baseline — that builds no staff group at all in
    /// Lily# and the collector throws, which is how the first draft of these tests failed
    /// against a fix that was already working.
    /// </remarks>
    private static string StaffRows(int staffCount) =>
        staffCount == 1
            ? " staff melody }"
            : "\n  grandStaff {\n"
              + string.Join("\n", Enumerable.Repeat("    staff melody", staffCount))
              + "\n  }\n}";

    private static double[] ColumnIdeals(string src, int measureIndex)
    {
        var (timings, allMeasures, primary, _) = Collect(src, measureIndex);
        return new MeasureLayouter()
            .CreateTimingSprings(primary, timings, 0.125, allMeasures)
            .Select(s => s.IdealDistance)
            .ToArray();
    }

    /// <summary>
    /// ⚠️ A COLUMN'S ITEM LIST IS AGGREGATED ACROSS STAVES, so a key change opening a measure
    /// of a grand staff arrives in it once per staff. They are not grobs side by side: every
    /// staff prints its own signature at the SAME x and the column is one signature wide.
    /// Charging each of them made the column grow by one signature per extra staff —
    /// bar-line-to-first-note 1.64 ss on one staff, 4.94 on two, 8.24 on three for a 3-sharp
    /// change — which an owner's two-staff book showed as a blank after the change and read
    /// as space reserved for a time signature that was never drawn (there is no meter change
    /// in that book at all).
    /// </summary>
    /// <remarks>
    /// LILYPOND, MEASURED: audit/lp-geometry/probes/key-column-staves.ly, scores KS1/KS2/KS3.
    /// One, two and three staves put the bar line (14.645044999134612), the KeySignature
    /// (15.835044999134611, ext 3.3) and the following note head (22.635044999134614) at the
    /// same x to twelve digits, and the lower staves' own dumps confirm each really engraves
    /// a signature there.
    /// <para>
    /// Asserted as staff-count INDEPENDENCE rather than a pinned number, because that is what
    /// LilyPond's reading says: the quantity is whatever one signature costs, and it must not
    /// know how many staves printed one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void AKeyChangeOpeningAMeasure_CostsTheSameOnAnyNumberOfStaves(int staffCount)
    {
        double[] one = ColumnIdeals(KeyChangeOnStaves(1), measureIndex: 1);
        double[] many = ColumnIdeals(KeyChangeOnStaves(staffCount), measureIndex: 1);

        Assert.Equal(one.Length, many.Length);
        for (int i = 0; i < one.Length; i++)
            Assert.Equal(one[i], many[i], precision: 9);
    }

    /// <summary>
    /// The same rule for a change that stands in its own MID-MEASURE column. The surplus
    /// showed up on the other side there — the column being wider pushed its ORIGIN right, so
    /// the space grew BEFORE the signature rather than after it — but it is one defect and
    /// one fix (SpacingRules.IsFirstChangeOfItsKind).
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void AMidMeasureKeyChange_CostsTheSameOnAnyNumberOfStaves(int staffCount)
    {
        double[] one = ColumnIdeals(MidMeasureKeyChangeOnStaves(1), measureIndex: 0);
        double[] many = ColumnIdeals(MidMeasureKeyChangeOnStaves(staffCount), measureIndex: 0);

        Assert.Equal(one.Length, many.Length);
        for (int i = 0; i < one.Length; i++)
            Assert.Equal(one[i], many[i], precision: 9);
    }
}

