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
        // Y = StaffBottom(4.0) + StaffPadding(0.8) + TextAscent(1.0) = 5.8
        Assert.Equal(5.8, result[0].Y);
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
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(0, 0, 1.0, 6.8, "mf", 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, dynamics);

        Assert.Single(result);
        // dynamicBottom = 6.8 + 0.6(DynamicTextDescent) = 7.4
        // requiredY = 7.4 + 0.46(BetweenLayerPadding) + 1.0(TextAscent) = 8.86
        Assert.True(result[0].Y > 6.8,
            $"Text spanner Y ({result[0].Y}) should be below dynamic Y (6.8)");
        Assert.Equal(8.86, result[0].Y, 2);
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

        // Dynamic in same system (measure 4), with Y=6.8
        double dynX = measures[4].X + measures[4].Items[0].X;
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(4, 0, dynX, 6.8, "mf", 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, dynamics);

        Assert.Single(result);
        Assert.True(result[0].Y > 6.8,
            $"Text spanner Y ({result[0].Y}) should be below dynamic Y (6.8) in multi-system layout");
        Assert.Equal(8.86, result[0].Y, 2);
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

        // Dynamic in system 0 (measure 0) — different system, should not affect
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(0, 0, 1.0, 9.0, "ff", 0));

        var result = TextSpannerEngraver.Calculate(spanners, systems, measures, dynamics);

        Assert.Single(result);
        // Should be at base Y since no overlapping dynamic in same system
        Assert.Equal(5.8, result[0].Y, 2);
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
}
