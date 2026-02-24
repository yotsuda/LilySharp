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

using Xunit;
using LilySharp.Core.Svg.Layout;

namespace LilySharp.Tests;

/// <summary>
/// Tests for break alignment spacing (H-5).
/// LILYPOND-REF: lily/break-alignment-interface.cc
/// LILYPOND-REF: scm/define-grobs.scm break-align-orders, space-alist
/// </summary>
[Trait("Category", "Unit")]
public class BreakAlignSpacingTests
{
    // === Space-alist lookup tests ===

    [Fact]
    public void ClefToKeySignature_MinimumSpace_3_5()
    {
        // LILYPOND-REF: scm/define-grobs.scm:815 (key-signature . (minimum-space . 3.5))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.Clef, BreakAlignSymbol.KeySignature);
        Assert.Equal(SpacingStyle.MinimumSpace, entry.Style);
        Assert.Equal(3.5, entry.Value, 2);
    }

    [Fact]
    public void ClefToTimeSignature_MinimumSpace_4_2()
    {
        // LILYPOND-REF: scm/define-grobs.scm:816 (time-signature . (minimum-space . 4.2))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.Clef, BreakAlignSymbol.TimeSignature);
        Assert.Equal(SpacingStyle.MinimumSpace, entry.Style);
        Assert.Equal(4.2, entry.Value, 2);
    }

    [Fact]
    public void ClefToFirstNote_MinimumFixedSpace_5_0()
    {
        // LILYPOND-REF: scm/define-grobs.scm:817 (first-note . (minimum-fixed-space . 5.0))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.Clef, BreakAlignSymbol.FirstNote);
        Assert.Equal(SpacingStyle.MinimumFixedSpace, entry.Style);
        Assert.Equal(5.0, entry.Value, 2);
    }

    [Fact]
    public void KeySignatureToTimeSignature_ExtraSpace_1_15()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1834 (time-signature . (extra-space . 1.15))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.KeySignature, BreakAlignSymbol.TimeSignature);
        Assert.Equal(SpacingStyle.ExtraSpace, entry.Style);
        Assert.Equal(1.15, entry.Value, 2);
    }

    [Fact]
    public void KeySignatureToFirstNote_FixedSpace_2_5()
    {
        // LILYPOND-REF: scm/define-grobs.scm:1839 (first-note . (fixed-space . 2.5))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.KeySignature, BreakAlignSymbol.FirstNote);
        Assert.Equal(SpacingStyle.FixedSpace, entry.Style);
        Assert.Equal(2.5, entry.Value, 2);
    }

    [Fact]
    public void TimeSignatureToFirstNote_FixedSpace_2_0()
    {
        // LILYPOND-REF: scm/define-grobs.scm:3599 (first-note . (fixed-space . 2.0))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.TimeSignature, BreakAlignSymbol.FirstNote);
        Assert.Equal(SpacingStyle.FixedSpace, entry.Style);
        Assert.Equal(2.0, entry.Value, 2);
    }

    // === CalculateDistance tests ===

    [Fact]
    public void MinimumSpace_ReturnsValueWhenLargerThanExtent()
    {
        // Clef width (2.564) < minimum-space value (3.5)
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.MinimumSpace, 3.5),
            leftItemRightExtent: GlyphMetrics.GClefWidth,
            rightItemLeftExtent: 0);

        Assert.Equal(3.5, distance, 2);
    }

    [Fact]
    public void MinimumSpace_ReturnsExtentWhenLarger()
    {
        // Wide item (4.0) > minimum-space value (3.5)
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.MinimumSpace, 3.5),
            leftItemRightExtent: 4.0,
            rightItemLeftExtent: 0);

        Assert.True(distance > 3.5,
            $"MinimumSpace with wide item ({distance:F2}) should exceed value (3.5)");
    }

    [Fact]
    public void ExtraSpace_AddsValueToExtent()
    {
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.15),
            leftItemRightExtent: 2.2,
            rightItemLeftExtent: 0);

        Assert.Equal(3.35, distance, 2);
    }

    [Fact]
    public void FixedSpace_AddsValueToExtent()
    {
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.FixedSpace, 2.0),
            leftItemRightExtent: 2.1,
            rightItemLeftExtent: 0);

        Assert.Equal(4.1, distance, 2);
    }

    // === CalculatePrefixWidth integration tests ===

    [Fact]
    public void PrefixWidth_FirstSystem_NoKey_MatchesRenderer()
    {
        // C major, first system with 4/4 time
        // Renderer: clef(4.2) + timeSig + 2.0 = 4.2 + timeSigWidth + 2.0
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 0, false, true, 4, 4);

        double timeSigWidth = GlyphMetrics.GetTimeSigWidth(4, 4);
        double expected = 4.2 + timeSigWidth + 2.0;
        Assert.Equal(expected, width, 1);
    }

    [Fact]
    public void PrefixWidth_FirstSystem_WithKey_MatchesRenderer()
    {
        // D major (2 sharps), first system with 4/4 time
        // Renderer: 3.5 + 2*1.1 + 1.15 + timeSig + 2.0
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 2, true, true, 4, 4);

        double keyWidth = 2 * GlyphMetrics.GetKeySignatureAccidentalWidth(true);
        double timeSigWidth = GlyphMetrics.GetTimeSigWidth(4, 4);
        double expected = 3.5 + keyWidth + 1.15 + timeSigWidth + 2.0;
        Assert.Equal(expected, width, 1);
    }

    [Fact]
    public void PrefixWidth_Continuation_NoKey_UsesClefToFirstNote()
    {
        // C major, continuation line (no time sig)
        // Renderer: ClefToFirstNoteSpace = 5.0
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 0, false, false);

        Assert.Equal(5.0, width, 1);
    }

    [Fact]
    public void PrefixWidth_Continuation_WithKey_MatchesRenderer()
    {
        // D major (2 sharps), continuation line
        // Renderer: 3.5 + 2*1.1 + 2.5 (KeySignatureToFirstNoteSpace)
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 2, true, false);

        double keyWidth = 2 * GlyphMetrics.GetKeySignatureAccidentalWidth(true);
        double expected = 3.5 + keyWidth + 2.5;
        Assert.Equal(expected, width, 1);
    }

    [Fact]
    public void PrefixWidth_MoreKeySharps_Wider()
    {
        double width0 = SpacingRules.CalculatePrefixWidth(0, true);
        double width2 = SpacingRules.CalculatePrefixWidth(2, true);
        double width4 = SpacingRules.CalculatePrefixWidth(4, true);

        Assert.True(width2 > width0, "2 sharps wider than 0");
        Assert.True(width4 > width2, "4 sharps wider than 2");
    }

    [Fact]
    public void PrefixWidth_WithTimeSig_WiderThanWithout()
    {
        double withTime = SpacingRules.CalculatePrefixWidth(0, true);
        double withoutTime = SpacingRules.CalculatePrefixWidth(0, false);

        Assert.True(withTime > withoutTime,
            $"With time ({withTime:F2}) should be wider than without ({withoutTime:F2})");
    }

    // === Break-align-orders test ===

    [Fact]
    public void StartOfLineOrder_ClefBeforeKeyBeforeTime()
    {
        var order = BreakAlignSpacing.StartOfLineOrder;

        int clefIdx = Array.IndexOf(order, BreakAlignSymbol.Clef);
        int keyIdx = Array.IndexOf(order, BreakAlignSymbol.KeySignature);
        int timeIdx = Array.IndexOf(order, BreakAlignSymbol.TimeSignature);

        Assert.True(clefIdx < keyIdx, "Clef should come before key signature");
        Assert.True(keyIdx < timeIdx, "Key signature should come before time signature");
    }
}
