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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class TextSpannerTests
{
    // --- TextSpannerStyle enum ---

    [Fact]
    public void TextSpannerStyle_HasExpectedValues()
    {
        Assert.Equal(0, (int)TextSpannerStyle.DashedLine);
        Assert.Equal(1, (int)TextSpannerStyle.Line);
        Assert.Equal(2, (int)TextSpannerStyle.None);
    }

    // --- DetectTextSpanners ---

    [Fact]
    public void DetectTextSpanners_NoRitAccel_ReturnsEmpty()
    {
        var musicMarks = ImmutableArray<MusicMarkItem>.Empty;

        var result = TextSpannerEngraver.DetectTextSpanners(musicMarks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectTextSpanners_OnlyNonRitMarks_ReturnsEmpty()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Segno, 0, 0),
            new MusicMarkItem(MusicMarkType.Fine, 4, 0));

        var result = TextSpannerEngraver.DetectTextSpanners(musicMarks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectTextSpanners_Rit_CreatesSpanner()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rit, 2, 42));

        var result = TextSpannerEngraver.DetectTextSpanners(musicMarks);

        Assert.Single(result);
        Assert.Equal("rit.", result[0].Text);
        Assert.Equal(2, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex); // extends to next measure
        Assert.Equal(TextSpannerStyle.DashedLine, result[0].Style);
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void DetectTextSpanners_Accel_CreatesSpanner()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Accel, 1, 0));

        var result = TextSpannerEngraver.DetectTextSpanners(musicMarks);

        Assert.Single(result);
        Assert.Equal("accel.", result[0].Text);
        Assert.Equal(1, result[0].StartMeasureIndex);
        Assert.Equal(2, result[0].EndMeasureIndex);
    }

    [Fact]
    public void DetectTextSpanners_RitFollowedByAccel_TwoSpanners()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Rit, 0, 0),
            new MusicMarkItem(MusicMarkType.Accel, 3, 0));

        var result = TextSpannerEngraver.DetectTextSpanners(musicMarks);

        Assert.Equal(2, result.Length);
        Assert.Equal("rit.", result[0].Text);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex); // ends at the next rit/accel
        Assert.Equal("accel.", result[1].Text);
        Assert.Equal(3, result[1].StartMeasureIndex);
        Assert.Equal(4, result[1].EndMeasureIndex); // extends to next measure
    }

    [Fact]
    public void DetectTextSpanners_CrescNotIncluded()
    {
        // Cresc/decresc are handled by HairpinEngraver, not TextSpannerEngraver
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Cresc, 0, 0),
            new MusicMarkItem(MusicMarkType.Decresc, 2, 0));

        var result = TextSpannerEngraver.DetectTextSpanners(musicMarks);

        Assert.True(result.IsEmpty);
    }

    // --- TextSpannerEngraver.Calculate ---

    private static ImmutableArray<MeasureLayout> CreateMeasureLayouts(int count, double measureWidth = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0),
                new ItemLayout(2, 9.0, 2.0));
            builder.Add(new MeasureLayout(i, i * measureWidth, measureWidth, items));
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<SystemLayout> CreateSingleSystem(int measureCount)
    {
        var measures = CreateMeasureLayouts(measureCount);
        return ImmutableArray.Create(new SystemLayout(0, 10.0, 200.0, 5.0, measures));
    }

    [Fact]
    public void Calculate_PositionsTextSpanner()
    {
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 0, 0, 2, 0, TextSpannerStyle.DashedLine, 42));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Single(result);
        var layout = result[0];
        Assert.Equal(0, layout.StartMeasureIndex);
        Assert.Equal("rit.", layout.Text);
        Assert.Equal(TextSpannerStyle.DashedLine, layout.Style);
        Assert.Equal(42, layout.SourcePosition);
        // StartX should be at measure[0] item[0] X
        Assert.Equal(1.0, layout.StartX, 2);
        // EndX should be before measure[2] item[0]
        Assert.True(layout.EndX > layout.StartX);
        // LineStartX should be after the text
        Assert.True(layout.LineStartX > layout.StartX);
    }

    [Fact]
    public void Calculate_DashParameters_MatchLilyPond()
    {
        // LILYPOND-REF: scm/define-grobs.scm:3513-3514
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 0, 0, 3, 0, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Single(result);
        Assert.Equal(3.0, result[0].DashPeriod);
        Assert.Equal(0.2, result[0].DashFraction);
    }

    [Fact]
    public void Calculate_Y_BelowStaff()
    {
        var measures = CreateMeasureLayouts(3);
        var systems = CreateSingleSystem(3);
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "accel.", 0, 0, 2, 0, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Single(result);
        // LILYPOND-REF: TextSpanner staff-padding = 0.8
        // device Y = StaffBottom(4.0) + StaffPadding(0.8) + TextAscent(1.0) = 5.8 → YUp = -5.8
        Assert.Equal(-5.8, result[0].YUp);
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 0, 0, 10, 0, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_EmptySpanners_ReturnsEmpty()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);

        var result = TextSpannerEngraver.Calculate(
            ImmutableArray<TextSpannerItem>.Empty, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_Y_PushedBelowOverlappingDynamics()
    {
        // Text spanner (priority 350) must be placed below dynamics (priority 250)
        // when they overlap horizontally in the same system.
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 0, 0, 2, 0, TextSpannerStyle.DashedLine, 0));

        // Dynamic at measure 0 item 0 — overlaps with text spanner's X range
        // X = measures[0].X + items[0].X = 0 + 1.0 = 1.0
        // 4th arg is YUp now: device 6.8 below the staff → ToUp(6.8, 2) = −4.8.
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(0, 0, 1.0, -4.8, "mf", 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, dynamics);

        Assert.Single(result);
        // The dynamic's bottom is its own GLYPH INK below the baseline, not a nominal
        // constant: "mf" is the union of the fetaText m and f, and f descends 0.692000.
        // dynamicBottom = 6.8 + 0.692000 = 7.492000
        // requiredY = 7.492000 + 0.46(BetweenLayerPadding) + 1.0(TextAscent) = 8.952000
        // (The 0.6 this used to assert was TextSpannerEngraver's own unsourced descent —
        // one of three different numbers Lily# kept for that single quantity.)
        Assert.True(result[0].YUp < -6.8,
            $"Text spanner YUp ({result[0].YUp}) should be below dynamic (YUp < -6.8)");
        Assert.Equal(-8.952, result[0].YUp, 3);
    }

    [Fact]
    public void Calculate_Y_PushedBelowDynamics_MultiSystem()
    {
        // Verify priority stacking works when spanner and dynamic are in system 1 (not 0)
        var measures = CreateMeasureLayouts(8);
        // Two systems: system 0 has measures 0-3, system 1 has measures 4-7
        var sys0measures = ImmutableArray.Create(measures[0], measures[1], measures[2], measures[3]);
        var sys1measures = ImmutableArray.Create(measures[4], measures[5], measures[6], measures[7]);
        var systems = ImmutableArray.Create(
            new SystemLayout(0, 10.0, 200.0, 5.0, sys0measures),
            new SystemLayout(1, 30.0, 200.0, 5.0, sys1measures));

        // Text spanner in system 1 (measure 4 to measure 6)
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 4, 0, 6, 0, TextSpannerStyle.DashedLine, 0));

        // Dynamic in same system (measure 4), device Y=6.8 → YUp = ToUp(6.8, 2) = −4.8.
        double dynX = measures[4].X + measures[4].Items[0].X;
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(4, 0, dynX, -4.8, "mf", 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, dynamics);

        Assert.Single(result);
        Assert.True(result[0].YUp < -6.8,
            $"Text spanner YUp ({result[0].YUp}) should be below dynamic (YUp < -6.8) in multi-system layout");
        // Same arithmetic as Calculate_Y_PushedBelowOverlappingDynamics: the "mf" ink
        // descends 0.692000, so 6.8 + 0.692 + 0.46 + 1.0 = 8.952000.
        Assert.Equal(-8.952, result[0].YUp, 3);
    }

    [Fact]
    public void Calculate_Y_DynamicInDifferentSystem_NoStacking()
    {
        // Dynamic in system 0 should NOT affect text spanner in system 1
        var measures = CreateMeasureLayouts(8);
        var sys0measures = ImmutableArray.Create(measures[0], measures[1], measures[2], measures[3]);
        var sys1measures = ImmutableArray.Create(measures[4], measures[5], measures[6], measures[7]);
        var systems = ImmutableArray.Create(
            new SystemLayout(0, 10.0, 200.0, 5.0, sys0measures),
            new SystemLayout(1, 30.0, 200.0, 5.0, sys1measures));

        // Text spanner in system 1 (measure 4 to measure 6)
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 4, 0, 6, 0, TextSpannerStyle.DashedLine, 0));

        // Dynamic in system 0 (measure 0) — different system, should not affect.
        // Device Y=9.0 → YUp = ToUp(9.0, 2) = −7.0.
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(0, 0, 1.0, -7.0, "ff", 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, dynamics);

        Assert.Single(result);
        // Should be at base Y since no overlapping dynamic in same system
        Assert.Equal(-5.8, result[0].YUp, 2);
    }

    [Fact]
    public void Calculate_LineStartX_AfterText()
    {
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 0, 0, 3, 0, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Single(result);
        // LineStartX should be after the text ("rit." = 4 chars * 0.55 + 0.5 padding)
        double expectedLineStart = result[0].StartX + 4 * 0.55 + 0.5;
        Assert.Equal(expectedLineStart, result[0].LineStartX, 2);
        Assert.True(result[0].LineStartX < result[0].EndX,
            "Line should have space to be drawn");
    }

    // --- Cross-system text spanner continuation ---
    // LILYPOND-REF: line-spanner.cc:577-600 broken pieces at system boundaries

    private static (ImmutableArray<MeasureLayout> measures, ImmutableArray<SystemLayout> systems)
        CreateTwoSystems()
    {
        var measures = CreateMeasureLayouts(8);
        var sys0measures = ImmutableArray.Create(measures[0], measures[1], measures[2], measures[3]);
        var sys1measures = ImmutableArray.Create(measures[4], measures[5], measures[6], measures[7]);
        var systems = ImmutableArray.Create(
            new SystemLayout(0, 10.0, 200.0, 5.0, sys0measures),
            new SystemLayout(1, 30.0, 200.0, 5.0, sys1measures));
        return (measures, systems);
    }

    [Fact]
    public void Calculate_CrossSystem_ProducesTwoLayouts()
    {
        // Spanner from measure 2 (system 0) to measure 5 (system 1)
        var (measures, systems) = CreateTwoSystems();
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 2, 0, 5, 0, TextSpannerStyle.DashedLine, 42));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Equal(2, result.Length);
        // First segment: starts at measure 2 with text
        Assert.Equal(2, result[0].StartMeasureIndex);
        Assert.Equal("rit.", result[0].Text);
        Assert.Equal(42, result[0].SourcePosition);
        // Second segment: starts at measure 4 (system 1 first measure) with no text
        Assert.Equal(4, result[1].StartMeasureIndex);
        Assert.Equal("", result[1].Text);
        Assert.Equal(42, result[1].SourcePosition);
    }

    [Fact]
    public void Calculate_CrossSystem_FirstSegment_EndsAtSystemEdge()
    {
        var (measures, systems) = CreateTwoSystems();
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 1, 0, 6, 0, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Equal(2, result.Length);
        // First segment endX should extend to system 0's last measure edge
        // Measure 3: X=60, Width=20, so right edge = 80 - 0.25 padding = 79.75
        Assert.True(result[0].EndX > 70.0, "First segment should extend toward system edge");
    }

    [Fact]
    public void Calculate_CrossSystem_ContinuationSegment_NoText_LineFromStart()
    {
        var (measures, systems) = CreateTwoSystems();
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "accel.", 1, 0, 6, 0, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Equal(2, result.Length);
        // Continuation segment: no text, so LineStartX == StartX
        Assert.Equal("", result[1].Text);
        Assert.Equal(result[1].StartX, result[1].LineStartX);
    }

    [Fact]
    public void Calculate_CrossSystem_LastSegment_EndsAtEndNote()
    {
        var (measures, systems) = CreateTwoSystems();
        // End at measure 5 item 1 (X = 5*20 + 5.0 = 105.0, minus padding)
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 1, 0, 5, 1, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Equal(2, result.Length);
        // Last segment endX should be at the end note position (not system edge)
        double expectedEndX = measures[5].X + measures[5].Items[1].X - 0.25; // BoundPadding
        Assert.Equal(expectedEndX, result[1].EndX, 2);
    }

    [Fact]
    public void Calculate_ThreeSystemSpanner_ProducesThreeLayouts()
    {
        var measures = CreateMeasureLayouts(12);
        var sys0 = ImmutableArray.Create(measures[0], measures[1], measures[2], measures[3]);
        var sys1 = ImmutableArray.Create(measures[4], measures[5], measures[6], measures[7]);
        var sys2 = ImmutableArray.Create(measures[8], measures[9], measures[10], measures[11]);
        var systems = ImmutableArray.Create(
            new SystemLayout(0, 10.0, 200.0, 5.0, sys0),
            new SystemLayout(1, 30.0, 200.0, 5.0, sys1),
            new SystemLayout(2, 50.0, 200.0, 5.0, sys2));

        // Spanner from measure 2 to measure 9 (spans all 3 systems)
        var spanners = ImmutableArray.Create(new TextSpannerItem(
            "rit.", 2, 0, 9, 0, TextSpannerStyle.DashedLine, 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, ImmutableArray<DynamicLayout>.Empty);

        Assert.Equal(3, result.Length);
        // First: has text
        Assert.Equal("rit.", result[0].Text);
        // Middle: no text
        Assert.Equal("", result[1].Text);
        // Last: no text
        Assert.Equal("", result[2].Text);
    }
}
