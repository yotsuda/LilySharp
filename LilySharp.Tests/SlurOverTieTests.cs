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

using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A slur is scored around the TIE it covers: LilyPond's Slur_engraver acknowledges every
/// Tie into the open slur's encompass-objects, so the bow clears the tie's stencil by
/// extra-encompass-free-distance and keeps its ends slur-tie-extrema-min-distance from the
/// tie's. The owner's report of 2026-09-23 (samples/nocturne.lys bar 4,
/// <c>tuplet 3/2 { b8( cis d } e4~ e8 d cis b)</c>): the slur ran through the tie's apex,
/// one whole 0.5 ss below LilyPond's — and LilyPond's curve for the same bar WITHOUT the
/// tie was Lily#'s to the hundredth (Lab sessions/p536/nocturne/notie.ly), so the tie was
/// the whole difference.
/// Every pin is LilyPond 2.26.0's own print of the twin <c>lysc ly</c> writes from this
/// source (Lab sessions/p537/fixture/lp.svg), in staff spaces above the middle line.
/// LILYPOND-REF: lily/slur-engraver.cc:79 acknowledge_extra_object (tie);
/// LILYPOND-REF: lily/slur.cc:365-387 auxiliary_acknowledge_extra_object;
/// LILYPOND-REF: lily/slur-scoring.cc:850-884 get_extra_encompass_infos;
/// LILYPOND-REF: lily/slur-configuration.cc:348-459 score_extra_encompass.
/// </summary>
[Trait("Category", "Unit")]
public class SlurOverTieTests
{
    // samples/nocturne.lys, right hand only.
    private const string Source = """
        time 4/4
        key d major
        part rh { clef treble }
        section Main {
          partial 4
          rh {
            a'8( cis |
            fis2 e8 d e fis |
            d4. cis8 b2) |
            tuplet 3/2 { b8( cis d } e4~ e8 d cis b) |
            grace { b16 } a4( cis8 e g4 fis8 e) |
            <fis a>4.( <e g>8 <d fis>4 <cis e>) |
            <b d>2( <bes d>2) |
            tuplet 3/2 { a,8( b a } fis4 e8 g fis e) |
            <a d fis>1 |.
          }
        }
        form main { ~Main }
        score main { staff rh }
        """;

    private readonly record struct Bow(double X0, double Y0, double CX1, double CY1,
        double CX2, double CY2, double X3, double Y3);

    /// <summary>Every drawn bow (slur or tie) as its centreline's four points, Y in staff
    /// spaces ABOVE the middle line of the single staff.</summary>
    private static (List<Bow> Bows, double MiddleY) Bows(string svg)
    {
        var lineYs = Regex.Matches(svg,
                "<line x1=\"0\\.05\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(y => y).ToList();
        Assert.Equal(5, lineYs.Count);
        double middle = lineYs[2];

        var bows = new List<Bow>();
        foreach (Match m in Regex.Matches(svg,
            "<path d=\"M ([-\\d.]+),([-\\d.]+) C ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+) C"))
        {
            double P(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            bows.Add(new Bow(P(1), middle - P(2), P(3), middle - P(4), P(5), middle - P(6), P(7), middle - P(8)));
        }
        return (bows, middle);
    }

    [Fact]
    public void ASlurOverATie_ClearsTheTie_AsLilyPondScoresIt()
    {
        var svg = LiveRender.SvgFromRenderSpec(Source);
        var (bows, _) = Bows(svg);

        // The tie is the narrowest bow; bar 4's slur is the narrowest bow that spans it.
        var tie = bows.MinBy(b => b.X3 - b.X0);
        var slur = bows.Where(b => b.X0 < tie.X0 && b.X3 > tie.X3).MinBy(b => b.X3 - b.X0);

        // The tie is where it always was — the slur moves, not the tie (the tie is solved
        // first and never looks at a slur). LP: endpoints −2.5000, controls −3.0620.
        const double eps = 0.011;   // the SVG prints two decimals
        Assert.InRange(tie.Y0, 2.5 - eps, 2.5 + eps);
        Assert.InRange(tie.CY1, 3.062 - eps, 3.062 + eps);

        // LP's bar-4 slur: endpoints at 2.195 above the middle, both controls at 3.7694.
        // Before the tie joined the extra set the same slur sat a whole 1.0 ss lower in
        // this spacing (its ends on 1.20 — measured with the tie block switched off,
        // Lab sessions/p537/fixture/ls-poison.svg), through the tie's apex.
        Assert.InRange(slur.Y0, 2.195 - eps, 2.195 + eps);
        Assert.InRange(slur.Y3, 2.195 - eps, 2.195 + eps);
        Assert.InRange(slur.CY1, 3.7694 - eps, 3.7694 + eps);
        Assert.InRange(slur.CY2, 3.7694 - eps, 3.7694 + eps);

        // ...and the bow's midpoint clears the tie's apex by at least
        // extra-encompass-free-distance (0.3): the ink no longer crosses.
        double slurMid = 0.125 * slur.Y0 + 0.375 * slur.CY1 + 0.375 * slur.CY2 + 0.125 * slur.Y3;
        double tieApex = 0.125 * tie.Y0 + 0.375 * tie.CY1 + 0.375 * tie.CY2 + 0.125 * tie.Y3;
        Assert.True(slurMid - tieApex >= 0.3,
            $"slur midpoint {slurMid:F3} against tie apex {tieApex:F3}");
    }

    /// <summary>
    /// The slur scorer's note columns carry the HEAD GLYPH's own box, as LilyPond's
    /// <c>get_encompass_info</c> reads the extremal head's extent — not a nominal half
    /// space. The 0.045 between the two (0.545 against 0.5) is enough to change which
    /// candidate wins: with the nominal box the slur of <c>a4( cis8 e g4 fis8 e)</c> (the
    /// bar after the tie) took its left end on 2.045, where LilyPond's variance term
    /// prices that candidate 1.74 against the flat 2.545 pair's 1.60 — and the long slur
    /// of the first three bars sat 0.07 lower at its controls for the same reason.
    /// LILYPOND-REF: lily/slur-scoring.cc:137-144 get_encompass_info — ei.head_ =
    ///   h->extent (common_[Y_AXIS], Y_AXIS)[dir_];
    /// LILYPOND-REF: lily/slur-configuration.cc:306-345 score_encompass — the variance.
    /// Pins are LilyPond 2.26.0's for the same twin (Lab sessions/p537/fixture/lp.svg);
    /// both engines print the sandwich's OUTER curve first, so the control pins compare
    /// outer to outer, as the tie test above does.
    /// </summary>
    [Fact]
    public void TheEncompassedHeads_AreTheirGlyphBoxes_AsLilyPondScoresThem()
    {
        var svg = LiveRender.SvgFromRenderSpec(Source);
        var (bows, _) = Bows(svg);
        var ordered = bows.OrderBy(b => b.X0).ToList();
        const double eps = 0.011;

        // The first bow by X is the pickup-to-bar-3 slur: ends 1.045 / 1.195 above the
        // middle, outer controls 3.9700 / 4.0831 (with 0.5-boxes: 3.90 / 4.01).
        var first = ordered[0];
        Assert.InRange(first.Y0, 1.045 - eps, 1.045 + eps);
        Assert.InRange(first.Y3, 1.195 - eps, 1.195 + eps);
        Assert.InRange(first.CY1, 3.970 - eps, 3.970 + eps);
        Assert.InRange(first.CY2, 4.0831 - eps, 4.0831 + eps);

        // The slur after the tied bar, a4( … e): LP's flat pair, both ends 2.545, outer
        // controls 3.9909. With 0.5-boxes the left end sat on 2.045.
        var tie = bows.MinBy(b => b.X3 - b.X0);
        var after = ordered.First(b => b.X0 > tie.X3);
        Assert.InRange(after.Y0, 2.545 - eps, 2.545 + eps);
        Assert.InRange(after.Y3, 2.545 - eps, 2.545 + eps);
        Assert.InRange(after.CY1, 3.9909 - eps, 3.9909 + eps);
        Assert.InRange(after.CY2, 3.9909 - eps, 3.9909 + eps);
    }

    /// <summary>
    /// The DOWN slur of the last full bar, <c>tuplet 3/2 { a,8( b a } fis4 e8 g fis e)</c>,
    /// pinned on BOTH curves of its sandwich. A bow's path is two cubics; LilyPond writes
    /// the outer one first, this engine the +Y one first (the outer of an UP bow, the
    /// INNER of a down bow), so the first curves of the two prints are not the same curve
    /// on a down bow. Read as the pair, the pins are exact: outer 3.3551 / 4.3826 and
    /// inner 3.2359 / 4.2634 below the middle, ends 1.545 / 3.045 — which is what
    /// session 537 had booked as a 0.11 residual from comparing first curve to first curve.
    /// LILYPOND-REF: lily/lookup.cc:395-415 Lookup::slur (…, dash_details) — back and
    ///   curve, the interior controls ± 0.5·curvethick;
    /// LILYPOND-REF: lily/lookup.cc:484-502 bezier_sandwich — top curve, then the bottom one back.
    /// </summary>
    [Fact]
    public void TheDownSlur_MatchesLilyPond_OnBothCurvesOfItsSandwich()
    {
        var svg = LiveRender.SvgFromRenderSpec(Source);
        var lineYs = Regex.Matches(svg,
                "<line x1=\"0\\.05\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(y => y).ToList();
        double middle = lineYs[2];

        // Every bow with both cubics; the down slur is the one whose controls lie BELOW
        // its ends (device y larger), the last such by X.
        var pattern = new Regex(
            "<path d=\"M ([-\\d.]+),([-\\d.]+) C ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+)"
            + " C ([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+)");
        (double X0, double Y0, double Y3, double A1, double A2, double B1, double B2)? down = null;
        foreach (Match m in pattern.Matches(svg))
        {
            double P(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            if (P(4) > P(2) && (down is null || P(1) > down.Value.X0))
                down = (P(1), P(2), P(8), P(4), P(6), P(10), P(12));
        }
        Assert.NotNull(down);
        var d = down.Value;
        const double eps = 0.011;
        Assert.InRange(d.Y0 - middle, 1.545 - eps, 1.545 + eps);
        Assert.InRange(d.Y3 - middle, 3.045 - eps, 3.045 + eps);
        // The two cubics, whichever order they were written in: the farther pair is the
        // outer curve, the nearer the inner.
        double outer1 = Math.Max(d.A1, d.B2), inner1 = Math.Min(d.A1, d.B2);
        double outer2 = Math.Max(d.A2, d.B1), inner2 = Math.Min(d.A2, d.B1);
        Assert.InRange(outer1 - middle, 3.3551 - eps, 3.3551 + eps);
        Assert.InRange(outer2 - middle, 4.3826 - eps, 4.3826 + eps);
        Assert.InRange(inner1 - middle, 3.2359 - eps, 3.2359 + eps);
        Assert.InRange(inner2 - middle, 4.2634 - eps, 4.2634 + eps);
    }
}
