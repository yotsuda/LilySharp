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

using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The pointwise dynamic-support machinery (session 37's port; ledger
/// staff.staff.dynamic-*): the label's own outline as my_dim, heads and REAL thin
/// stems as extent boxes, and the below-side collision pass. Asserted as MECHANISM,
/// with the font's own numbers, not LilyPond's — a scalar support edge cannot pass
/// these, whatever value it takes (HANDOFF §5.4).
/// </summary>
[Trait("Category", "Unit")]
public class DynamicSupportPointwiseTests
{
    // The DSQ/DMF texture's support, in the staff-middle Y-up frame: staff floor,
    // a deep head's extent box at the drawn head's X, a forced-down REAL stem as a
    // thin sliver at the head's left edge. Symbolic depths (not LilyPond's) — the
    // mechanism is what is under test.
    private const double HeadBottom = -4.5;
    private const double StemTip = -6.5;
    private const double HeadAdvance = 1.304;

    private static VerticalSkyline SupportDown(bool withStem)
    {
        var down = VerticalSkyline.FromBox(
            double.NegativeInfinity, double.PositiveInfinity,
            -2.05, -2.05, VerticalDirection.Down);
        down.Merge(VerticalSkyline.FromBox(
            0.0, HeadAdvance, HeadBottom, HeadBottom + 1.0, VerticalDirection.Down));
        if (withStem)
            down.Merge(VerticalSkyline.FromBox(
                0.0, 0.13, StemTip, -4.0, VerticalDirection.Down));
        return down;
    }

    // LabelComposition_IsAdvancePlusKern_NotMeasuredWidths lived here until 2026-08-05.
    // Its claim (a label's width is the RAW advances plus the raw kerns) was falsified by
    // the probe that had always backed it: LilyPond snaps each glyph's KERNED advance to a
    // device pixel. The composition's observers are now DynamicLabelWidthTests, against all
    // twenty labels of dynamic-text-x.ly rather than the two spelled out here.

    [Fact]
    public void LabelOutline_ExtremesAreTheLettersOwnInk()
    {
        // The composed outline's extremes are the letter boxes' — the same ink
        // TryGetDynamicInk answers from — so the pointwise profile and the box model
        // agree at the extremes and differ only in WHERE the ink is.
        Assert.True(GlyphMetrics.TryGetDynamicInk("f", out double bottom, out double top));
        var (up, down) = DynamicOutline.Place("f", 0.0, 0.0)!.Value;
        Assert.Equal(top, up.MaxHeight(), 6);
        Assert.Equal(bottom, down.MaxHeight(), 6);
    }

    [Fact]
    public void PointwiseDistance_HeadWinsUnderF_StemBindsUnderFff()
    {
        // ONE computation, two landings (ledger dynamic-head-support vs
        // dynamic-stem-binding): under a narrow \f the stem's thin sliver sits beside
        // the f's low LEFT outline and the HEAD binds; under a wide \fff the same
        // sliver sits under tall ink and the STEM binds. A scalar support edge cannot
        // produce both, whatever value it takes — that is what these two asserts pin.
        var support = SupportDown(withStem: true);
        double xLabel = HeadAdvance / 2.0;   // centred on the head, LilyPond's anchor

        var myF = DynamicEngraver.LabelSkylines("f", expressive: false, xLabel, -0.6);
        var myFff = DynamicEngraver.LabelSkylines("fff", expressive: false, xLabel, -0.6);

        Assert.True(GlyphMetrics.TryGetDynamicInk("f", out _, out double fTop));
        double headBound = (fTop - 0.6) - HeadBottom;   // the f's peak against the head box

        double dF = myF.Up.Distance(support);
        double dFff = myFff.Up.Distance(support);

        // \f: the HEAD-family landing — within a few hundredths of the peak-on-head
        // chain. Two named hairs live inside the window, both pointwise by nature:
        // the f's ascender peak sits at ~1.29 from its pen, a shade past the head
        // box's right edge in the advance frame (so the binding samples the outline
        // just OFF the peak, a few 1e-2 shy), and the left tail grazes the stem
        // sliver (the Pango-centering family, 1e-3). What the window must exclude —
        // and does, by an order of magnitude — is the STEM's 2.0 term.
        Assert.InRange(dF, headBound - 0.06, headBound + 0.01);
        // \fff: the stem binds — roughly (StemTip depth − head depth) deeper, minus
        // the outline's local profile at the stem's X. Far outside any scalar's reach.
        Assert.True(dFff > dF + 1.5,
            $"the wide label must bind on the stem: d(fff)={dFff:F6} vs d(f)={dF:F6}");
    }

    [Fact]
    public void BelowCollisionMove_BeamFacePushes_ThinStemTucks()
    {
        // The below-side outside-staff pass (ledger dynamic-beam-avoid): a full-width
        // beam face pushes the label to face + 0.46 EXACTLY, while the thin stem alone
        // does not move a label already clear by the side-position padding 0.6.
        Assert.True(GlyphMetrics.TryGetDynamicInk("f", out _, out double fTop));
        double xLabel = HeadAdvance / 2.0;
        // The quiet side-position landing: label top at head bottom − 0.6.
        double quietBaseline = HeadBottom - 0.6 - fTop;
        var my = DynamicEngraver.LabelSkylines("f", expressive: false, xLabel, quietBaseline);

        // Thin stem only: no move (the f's left tail sits below the sliver, pointwise).
        Assert.Equal(0.0,
            DynamicEngraver.BelowCollisionMove(SupportDown(withStem: true), my.Up, 0.46), 9);

        // A full-width beam face under the whole label: the label's PEAK binds, so the
        // move lands the top at face − 0.46, exact arithmetic.
        const double beamFace = -6.74;
        var withBeam = SupportDown(withStem: false);
        withBeam.Merge(VerticalSkyline.FromBox(
            -1.0, 3.0, beamFace, beamFace + 0.48, VerticalDirection.Down));
        double move = DynamicEngraver.BelowCollisionMove(withBeam, my.Up, 0.46);
        double topAfter = quietBaseline + fTop + move;
        Assert.Equal(beamFace - 0.46, topAfter, 9);
    }
}
