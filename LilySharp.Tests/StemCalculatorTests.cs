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
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class StemCalculatorTests
{
    [Fact]
    public void StemDetails_Default_MatchesLilyPondDefineGrobs()
    {
        var d = StemDetails.Default;

        // LILYPOND-REF: define-grobs.scm:3121-3141
        Assert.Equal(new[] { 3.5, 3.5, 3.5, 4.25, 5.0, 6.0, 7.0, 8.0, 9.0 }, d.Lengths);
        Assert.Equal(new[] { 3.26, 3.5, 3.6 }, d.BeamedLengths);
        Assert.Equal(new[] { 1.83, 1.5, 1.25 }, d.BeamedMinimumFreeLengths);
        Assert.Equal(new[] { 2.0, 1.25 }, d.BeamedExtremeMinimumFreeLengths);
        Assert.Equal(new[] { 1.0, 0.5, 0.25 }, d.StemShorten);
        Assert.Equal(1.0, d.LengthFraction);
    }

    [Fact]
    public void CalculateStemEndY_QuarterOnTheMiddleLine_IsShortened_BecauseLilyPondsTestIncludesZero()
    {
        // A head sitting ON the middle line. LilyPond's guard is
        //   stem.cc:522   if (dir && dir * hp[dir] >= 0)
        // and the comparison is >= 0, so the unnatural-direction shortening of
        // stem.cc:519-555 DOES fire on position 0. This test used to assert 3.5 here — the
        // raw details.lengths entry — which is what Lily# produced and NOT what LilyPond
        // draws. That is the shape HANDOFF 5.4 names: an expectation pinned to the
        // implementation rather than to LilyPond.
        //
        // The expected number is LilyPond's own arithmetic, in the half-spaces stem.cc works
        // in (:516 length, :530 shorten-property, :534 quarter_stem_length, :541-554):
        //   length           = 2 * lengths[0]                 = 7
        //   shorten-property = 2 * stem-shorten[0] = 2 * 1.0  = 2
        //   shortening-step  = min(max(0.25, 2/6), 0.5)       = 1/3
        //   which-step       = min(1, 7 - 2*staff_rad - 2) + |0| = min(1, 1) + 0 = 1
        //   shorten          = min(max(0, 1/3 * 1), 2)        = 1/3
        //   length - shorten = 20/3 half-spaces               = 10/3 staff spaces
        // Confirmed against 2.26.0: audit/lp-geometry/probes/page-vertical.ly books JSS,
        // JSSC and JSK all put the ink below the last staff's refpoint at 3.333333, the
        // bass staff's middle-line down stem.
        // LILYPOND-REF: lily/stem.cc:519-555, scm/define-grobs.scm:3448,3452.
        double stemAttachY = 4.0; // middle of 4-space staff at systemY=2
        double systemY = 0.0;

        double endY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: 0);

        double stemLength = stemAttachY - endY;
        Assert.Equal(10.0 / 3.0, stemLength, 9);
    }

    [Fact]
    public void CalculateStemEndY_QuarterOnTheNaturalSide_IsNotShortened()
    {
        // The falsifier for the entry above: one staff position BELOW the middle line with
        // the stem pointing UP is the natural direction, dir * hp[dir] = 1 * -1 < 0, so
        // stem.cc:522 does not fire and the stem keeps the full details.lengths entry. If a
        // future edit widened the guard instead of matching LilyPond's `>= 0`, this catches
        // it — the two tests together pin the boundary rather than the value.
        // LILYPOND-REF: lily/stem.cc:521-522.
        double stemAttachY = 4.5;
        double systemY = 0.0;

        double endY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: -1);

        Assert.Equal(StemDetails.Default.Lengths[0], stemAttachY - endY, 9);
    }

    [Fact]
    public void CalculateStemEndY_32ndNote_UsesLongerStem()
    {
        // 32nd note at middle of staff
        double stemAttachY = 2.0;
        double systemY = 0.0;

        double endY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 5, staffPosition: 0);

        // 32nd note should use 4.25 staff spaces (index 3 in lengths array)
        double stemLength = stemAttachY - endY;
        Assert.True(stemLength >= 4.25 - 0.5, $"32nd stem should be longer, got {stemLength}");
    }

    [Fact]
    public void CalculateStemEndY_StemExtendesToMiddleLine()
    {
        // Note below staff (staff position -4 = 2 below bottom line)
        // systemY = 0, staffMiddle at Y=2
        double stemAttachY = 4.0; // bottom of staff
        double systemY = 0.0;

        double endY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: -4);

        // Stem should reach at least the middle line (Y=2)
        Assert.True(endY <= 2.0 + 0.01, $"Stem should reach middle line Y=2, got {endY}");
    }

    [Fact]
    public void CalculateStemEndY_UnnaturalDirection_Shortened()
    {
        // Note above middle line with stem up (unnatural direction)
        double systemY = 0.0;
        double stemAttachY = 1.0; // above middle line

        double naturalEndY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: 2); // above middle, stem up = unnatural

        double normalEndY = StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp: true, systemY,
            durationLog: 2, staffPosition: -2); // below middle, stem up = natural

        // Unnatural direction should have shorter stem
        double unnaturalLength = stemAttachY - naturalEndY;
        double normalLength = stemAttachY - normalEndY;
        Assert.True(unnaturalLength <= normalLength,
            $"Unnatural direction stem ({unnaturalLength}) should be <= natural ({normalLength})");
    }

    [Fact]
    public void CalculateBeamedStemInfo_Returns_ValidInfo()
    {
        var info = StemCalculator.CalculateBeamedStemInfo(
            headPosition: 0,
            stemUp: true,
            beamCount: 1);

        // Ideal Y should be positive (above staff center for stem up)
        Assert.True(info.IdealY > 0, $"Ideal Y should be > 0 for stem up, got {info.IdealY}");
        Assert.True(info.StemUp);
    }

    [Fact]
    public void CalculateBeamedStemInfo_MoreBeams_LongerStem()
    {
        var info1 = StemCalculator.CalculateBeamedStemInfo(
            headPosition: 0, stemUp: true, beamCount: 1);

        var info3 = StemCalculator.CalculateBeamedStemInfo(
            headPosition: 0, stemUp: true, beamCount: 3);

        // More beams should result in longer ideal stem
        Assert.True(info3.IdealY >= info1.IdealY,
            $"3 beams ({info3.IdealY}) should need >= length than 1 beam ({info1.IdealY})");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    [InlineData(16, 4)]
    [InlineData(32, 5)]
    public void GetDurationLog_ReturnsCorrectValues(int noteValue, int expectedLog)
    {
        Assert.Equal(expectedLog, StemCalculator.GetDurationLog(noteValue));
    }

    // ─── The cue stem's length, against LilyPond's own dumps ────────────────────────────
    // The three tests below are the two halves of one law and its falsifier, measured off
    // LilyPond 2.24.4 in audit/lp-geometry/probes/voice-boundary-spacing.ly section E
    // (score CSL-CUE, `\override Stem.after-line-breaking` dumping stem-end-position and
    // length). ⚠️ They are NOT arithmetic done here and asserted here: every expected number
    // below appears in that probe's transcript. The ledger point cue.barline.prev.cue-head
    // watches the same law through the SPACING; these watch it in the length itself, which is
    // what the drawing reads.
    // LILYPOND-REF: ly/engraver-init.ly CueVoice — \override Stem.length-fraction = #(magstep -4);
    // LILYPOND-REF: lily/stem.cc:557 internal_calc_stem_end_position — the one line that reads
    //   the length-fraction property, after the shortening and before the middle-line rule.

    [Fact]
    public void CalculateStemEndY_CueOnTheMiddleLine_IsTheShortenedLengthTimesTheFraction()
    {
        // ONLY a middle-line note can show the law: everywhere else the "reach the middle
        // line" rule is what sets the end (see the next test). LilyPond dumps this stem's
        // length as 4.199736832982911 half-spaces from the head's centre, which is
        // 6.666666666666667 (= 7 − 1/3 of shortening) × magstep(−4) — equal AS DOUBLES.
        const double staffTopDown = 0.0;
        double middleDown = staffTopDown + 2.0;

        double endY = StemCalculator.CalculateStemEndY(
            middleDown, stemUp: false, staffTopDown,
            durationLog: 2, staffPosition: 0,
            EngravingDefaults.CueStemDetails);

        // Half-spaces, the frame LilyPond reports `length` in.
        double lengthHalfSpaces = (endY - middleDown) * 2.0;
        Assert.Equal(4.199736832982911, lengthHalfSpaces, 12);

        // ⚠️ AND THIS IS WHERE A FLOOR WOULD SHOW. In staff spaces the stem is
        // 2.099868416491456, BELOW the 2.5 that CalculateStemEndY used to clamp to — a floor
        // Lily# had invented and LilyPond does not have (stem.cc:481-596 bounds `length`
        // nowhere). Full size it never fired, because the shortest length the rule can
        // produce is 3.5 − 1.0 = 2.5 exactly; a length-fraction is what made it live, and it
        // was deleted rather than scaled. If one is ever reintroduced, this assertion is what
        // it will trip over.
        Assert.True(lengthHalfSpaces / 2.0 < 2.5);

        // ✔ VERIFIED TO GUARD, not assumed to: with the floor still in place and left
        // unscaled, THIS test fails and cue.barline.prev.cue-head STAYS GREEN — the bar-line
        // book's cue note is above the staff, where the middle-line rule sets the end and a
        // floor is invisible. The ledger point cannot see the length law's floor behaviour at
        // all; this is the observer that can.
    }

    [Fact]
    public void CalculateStemEndY_CueAboveTheStaff_IsFlooredAtTheMiddleLine()
    {
        // g′′ (staff position +5), stem down. Scaled, the length alone would be
        // 7 × magstep(−4) = 4.409723674632057 half-spaces and would stop at
        // +0.590276325367943 — LilyPond stops it at 0.000000000000000, because a stem on a
        // note outside the staff reaches the MIDDLE LINE (stem.cc:591-593). That rule is
        // INACTIVE at full size (7 half-spaces already carries g′′ past the middle) and
        // becomes active the moment the length is scaled, so a port that took the fraction
        // and not the rule would be short by 0.59 half-spaces on the register cues live in.
        const double staffTopDown = 0.0;
        double middleDown = staffTopDown + 2.0;
        double headDown = middleDown - 5 * 0.5;

        double endY = StemCalculator.CalculateStemEndY(
            headDown, stemUp: false, staffTopDown,
            durationLog: 2, staffPosition: 5,
            EngravingDefaults.CueStemDetails);

        Assert.Equal(middleDown, endY, 12);
    }

    [Fact]
    public void CalculateStemEndY_FullSizeAboveTheStaff_ClearsTheMiddleLine_TheFalsifier()
    {
        // The control for the test above, and the reason its floor is invisible at full size:
        // the SAME note full size ends at −2.0 staff positions (LilyPond's CSL-CTL dump), a
        // whole staff space PAST the middle line, so the floor never fires. If it were firing
        // at full size too, the previous test would prove nothing about the fraction.
        const double staffTopDown = 0.0;
        double middleDown = staffTopDown + 2.0;
        double headDown = middleDown - 5 * 0.5;

        double endY = StemCalculator.CalculateStemEndY(
            headDown, stemUp: false, staffTopDown,
            durationLog: 2, staffPosition: 5);

        Assert.Equal(middleDown + 1.0, endY, 12);
    }
}
