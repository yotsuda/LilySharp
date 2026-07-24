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
using LilySharp.Core.Svg;
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
    public void ClefToKeySignature_ExtraSpace_0_82()
    {
        // LILYPOND-REF: scm/define-grobs.scm:918 (key-signature . (extra-space . 0.82))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.Clef, BreakAlignSymbol.KeySignature);
        Assert.Equal(SpacingStyle.ExtraSpace, entry.Style);
        Assert.Equal(0.82, entry.Value, 2);
    }

    [Fact]
    public void ClefToTimeSignature_ExtraSpace_1_52()
    {
        // LILYPOND-REF: scm/define-grobs.scm:920 (time-signature . (extra-space . 1.52))
        var entry = BreakAlignSpacing.GetSpacing(BreakAlignSymbol.Clef, BreakAlignSymbol.TimeSignature);
        Assert.Equal(SpacingStyle.ExtraSpace, entry.Style);
        Assert.Equal(1.52, entry.Value, 2);
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
            leftItemRightExtent: GlyphMetrics.GClefWidth);

        Assert.Equal(3.5, distance, 2);
    }

    [Fact]
    public void MinimumSpace_ReturnsExtentWhenLarger()
    {
        // Wide item (4.0) > minimum-space value (3.5)
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.MinimumSpace, 3.5),
            leftItemRightExtent: 4.0);

        Assert.True(distance > 3.5,
            $"MinimumSpace with wide item ({distance:F2}) should exceed value (3.5)");
    }

    [Fact]
    public void ExtraSpace_AddsValueToExtent()
    {
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.15),
            leftItemRightExtent: 2.2);

        Assert.Equal(3.35, distance, 2);
    }

    [Fact]
    public void FixedSpace_AddsValueToExtent()
    {
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.FixedSpace, 2.0),
            leftItemRightExtent: 2.1);

        Assert.Equal(4.1, distance, 2);
    }

    [Fact]
    public void SemiFixedSpace_IsLeftExtentPlusValue()
    {
        // LILYPOND-REF: staff-spacing.cc:176-179 — ideal = leftRight + distance.
        // Independent of the RIGHT item's left extent (the old half-half formula used it).
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.SemiFixedSpace, 1.3),
            leftItemRightExtent: 2.0);

        Assert.Equal(3.3, distance, 2);
    }

    [Fact]
    public void SemiShrinkSpace_IsLeftExtentPlusValue()
    {
        // LILYPOND-REF: staff-spacing.cc:193-196 — same ideal as semi-fixed
        // (leftRight + distance); the old 0.8/0.6 factors were ungrounded.
        double distance = BreakAlignSpacing.CalculateDistance(
            new SpacingEntry(SpacingStyle.SemiShrinkSpace, 1.3),
            leftItemRightExtent: 2.0);

        Assert.Equal(3.3, distance, 2);
    }

    // === CalculatePrefixWidth integration tests ===

    [Fact]
    public void PrefixWidth_FirstSystem_NoKey_MatchesRenderer()
    {
        // C major, first system with 4/4 time. The prefix ends at the
        // time signature's INK; the 2.0 first-note distance is carried by
        // the first measure's leading spring (FirstNoteSpring), not here.
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 0, false, true, 4, 4);

        double timeSigWidth = GlyphMetrics.GetTimeSigWidth(4, 4);
        // LeftEdge→Clef 0.8 opens the prefix, then Clef→TimeSignature extra-space 1.52.
        double expected = EngravingDefaults.ClefGlyphXOffset + GlyphMetrics.GClefWidth + 1.52 + timeSigWidth;
        Assert.Equal(expected, width, 1);
        Assert.Equal((2.0, 1.0),
            BreakAlignSpacing.FirstNoteSpring(0, includeTimeSignature: true, GlyphMetrics.GClefWidth));
    }

    [Fact]
    public void PrefixWidth_FirstSystem_WithKey_MatchesRenderer()
    {
        // D major (2 sharps), first system with 4/4 time — ink end only.
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 2, true, true, 4, 4);

        double keyWidth = 2 * GlyphMetrics.GetKeySignatureAccidentalWidth(true);
        double timeSigWidth = GlyphMetrics.GetTimeSigWidth(4, 4);
        // LeftEdge→Clef 0.8, then Clef→KeySig extra-space 0.82, KeySig→TimeSig extra-space 1.15.
        double expected = EngravingDefaults.ClefGlyphXOffset + GlyphMetrics.GClefWidth + 0.82 + keyWidth + 1.15 + timeSigWidth;
        Assert.Equal(expected, width, 1);
    }

    [Fact]
    public void PrefixWidth_Continuation_NoKey_UsesClefToFirstNote()
    {
        // C major, continuation line (no time sig): the prefix is the clef
        // ink alone; the rigid 5.0 clef→first-note distance lives in the
        // leading spring.
        // LILYPOND-REF: Clef space-alist (first-note . (minimum-fixed-space . 5.0))
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 0, false, false);

        Assert.Equal(EngravingDefaults.ClefGlyphXOffset + GlyphMetrics.GClefWidth, width, 1);
        // minimum-fixed-space 5.0 is measured from the clef's LEFT ink and absorbs the
        // clef width, so the prefix (the clef width) plus the leading spring sum to 5.0 —
        // the width is not added AFTER the prefix. LILYPOND-REF staff-spacing.cc:183-187.
        var (clefGap, clefMin) =
            BreakAlignSpacing.FirstNoteSpring(0, includeTimeSignature: false, GlyphMetrics.GClefWidth);
        Assert.Equal(5.0, GlyphMetrics.GClefWidth + clefGap, 6);
        Assert.Equal(clefGap, clefMin);
    }

    [Fact]
    public void PrefixWidth_Continuation_WithKey_MatchesRenderer()
    {
        // D major (2 sharps), continuation line — ink end only; the 2.5
        // key→first-note distance lives in the leading spring.
        double width = BreakAlignSpacing.CalculatePrefixWidth(
            GlyphMetrics.GClefWidth, 2, true, false);

        double keyWidth = 2 * GlyphMetrics.GetKeySignatureAccidentalWidth(true);
        // LeftEdge→Clef 0.8 opens the prefix, then Clef→KeySignature extra-space 0.82.
        double expected = EngravingDefaults.ClefGlyphXOffset + GlyphMetrics.GClefWidth + 0.82 + keyWidth;
        Assert.Equal(expected, width, 1);
        Assert.Equal((2.5, 1.25),
            BreakAlignSpacing.FirstNoteSpring(2, includeTimeSignature: false, GlyphMetrics.GClefWidth));
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

    // === SolvePrefixColumns: the ported break-align column table ===

    [Fact]
    public void SolvePrefixColumns_Right_MatchesCalculatePrefixWidth()
    {
        // CalculatePrefixWidth is now just the column table's right edge, so they must agree
        // across the clef-only / +key / +key+time cases (equivalence of the refactor).
        foreach (var (keys, time) in new[] { (0, false), (0, true), (2, false), (2, true), (4, true) })
        {
            var cols = BreakAlignSpacing.SolvePrefixColumns(GlyphMetrics.GClefWidth, keys, keys > 0, time);
            double width = BreakAlignSpacing.CalculatePrefixWidth(GlyphMetrics.GClefWidth, keys, keys > 0, time);
            Assert.Equal(width, cols.Right, 9);
        }
    }

    [Fact]
    public void SolvePrefixColumns_ColumnsAreOrderedAndSpaced()
    {
        // Clef opens at the LeftEdge->Clef offset; key and time each sit strictly right of it.
        var cols = BreakAlignSpacing.SolvePrefixColumns(GlyphMetrics.GClefWidth, 2, keySharps: true, includeTimeSignature: true);
        Assert.Equal(EngravingDefaults.ClefGlyphXOffset, cols.ClefX, 9);
        Assert.True(cols.HasKey && cols.HasTime);
        Assert.True(cols.KeyX > cols.ClefX, "key right of clef");
        Assert.True(cols.TimeX > cols.KeyX, "time right of key");
        Assert.True(cols.Right >= cols.TimeX, "prefix ends at/after the time column");
    }

    [Fact]
    public void SolvePrefixColumns_WiderKeyPushesSharedTimeColumn()
    {
        // The break-align generalisation: a WIDER key (union extent across staves) moves the
        // shared time column right, so a transposed part's key aligns every staff's meter.
        double timeNoKey = BreakAlignSpacing.SolvePrefixColumns(GlyphMetrics.GClefWidth, 0, false, true).TimeX;
        double timeTwoSharp = BreakAlignSpacing.SolvePrefixColumns(GlyphMetrics.GClefWidth, 2, true, true).TimeX;
        double timeFourSharp = BreakAlignSpacing.SolvePrefixColumns(GlyphMetrics.GClefWidth, 4, true, true).TimeX;
        Assert.True(timeTwoSharp > timeNoKey, "a key pushes the time column right of the no-key case");
        Assert.True(timeFourSharp > timeTwoSharp, "a wider key pushes it further");
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
