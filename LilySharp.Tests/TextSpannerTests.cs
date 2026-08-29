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
using LilySharp.Core.Svg.Collector;
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
    public void DetectTextSpanners_AStartAndItsStop_MakeOneSpan()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 2, 42),
            new MusicMarkItem(MusicMarkType.TextSpanStop, 5, 60));

        var result = TextSpannerEngraver.DetectTextSpanners(musicMarks);

        var span = Assert.Single(result);
        Assert.Equal("rit.", span.Text);
        Assert.Equal(2, span.StartMeasureIndex);
        // The length is where the terminator STANDS. There is no default: this number is
        // the writer's, which is the whole reason the terminator exists.
        Assert.Equal(5, span.EndMeasureIndex);
        Assert.Equal(TextSpannerStyle.DashedLine, span.Style);
        // The span is the START's, so click-to-source lands on the word that is printed.
        Assert.Equal(42, span.SourcePosition);
    }

    [Fact]
    public void DetectTextSpanners_TheWordIsTheStarts_NotTheTypes()
    {
        // @accel, @rall and @textSpan("poco rit.") make the SAME mark and differ only in the
        // text they carry — MusicMarkType.TextSpanStart holds no word (see its remark), so a
        // span that printed "rit." for all of them would be reading a default, not the book.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "poco rit.", 0, 10),
            new MusicMarkItem(MusicMarkType.TextSpanStop, 2, 20));

        var span = Assert.Single(TextSpannerEngraver.DetectTextSpanners(musicMarks));

        Assert.Equal("poco rit.", span.Text);
    }

    [Fact]
    public void DetectTextSpanners_AStartNobodyClosed_DrawsNothingAndIsReported()
    {
        // LILYPOND-REF: lily/text-spanner-engraver.cc:117-127 Text_spanner_engraver::finalize — "unterminated text
        // spanner", then suicide(). The WORD goes with the line: an unclosed spanner is not
        // a shorter spanner, and until session 289 Lily# gave it a one-measure default that
        // nothing told the reader about.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 2, 42));

        var (spanners, unpaired) = TextSpannerEngraver.PairTextSpanners(musicMarks);

        Assert.True(spanners.IsEmpty);
        var warning = Assert.Single(unpaired);
        Assert.Equal(TextSpanPairingFault.Unterminated, warning.Fault);
        Assert.Equal(42, warning.SourcePosition);
    }

    [Fact]
    public void DetectTextSpanners_AStopWithNothingOpen_DrawsNothingAndIsReported()
    {
        // LILYPOND-REF: lily/text-spanner-engraver.cc:61-63 Text_spanner_engraver::process_music — "cannot find start of text
        // spanner".
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStop, 1, 77));

        var (spanners, unpaired) = TextSpannerEngraver.PairTextSpanners(musicMarks);

        Assert.True(spanners.IsEmpty);
        Assert.Equal(TextSpanPairingFault.StopWithNoStart, Assert.Single(unpaired).Fault);
    }

    [Fact]
    public void DetectTextSpanners_ASecondStartInsideAnOpenSpan_IsIgnoredAndReported()
    {
        // LILYPOND-REF: lily/text-spanner-engraver.cc:73-77 Text_spanner_engraver::process_music — "already have a text spanner".
        // The OPEN one keeps the span; spanners do not nest, so the second start is dropped
        // rather than replacing the first or opening a span inside it.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 0, 10),
            new MusicMarkItem(MusicMarkType.TextSpanStart, "accel.", 1, 20),
            new MusicMarkItem(MusicMarkType.TextSpanStop, 3, 30));

        var (spanners, unpaired) = TextSpannerEngraver.PairTextSpanners(musicMarks);

        var span = Assert.Single(spanners);
        Assert.Equal("rit.", span.Text);          // the FIRST one keeps the span
        Assert.Equal(0, span.StartMeasureIndex);
        Assert.Equal(3, span.EndMeasureIndex);
        var warning = Assert.Single(unpaired);
        Assert.Equal(TextSpanPairingFault.StartWhileOpen, warning.Fault);
        Assert.Equal(20, warning.SourcePosition);  // the dropped mark, not the open one
    }

    [Fact]
    public void DetectTextSpanners_TheSamePairPlayedTwice_MakesTwoSpansOfTheSameLength()
    {
        // A form that repeats a section contributes ONE MusicMarkItem PER PLAYING of the
        // same written mark — same SourcePosition, different measures. The defect this
        // answers (user report 2026-08-29, Untitled-6.lys) had the first spanner run to the
        // SECOND playing of itself, six bars against one: one source, two lengths.
        // Pairing in played order needs no rule against that — playing 1's stop is reached
        // before playing 2's start — which is why the guard that used to stand here is gone.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 0, 42),
            new MusicMarkItem(MusicMarkType.TextSpanStop, 1, 60),
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 4, 42),
            new MusicMarkItem(MusicMarkType.TextSpanStop, 5, 60));

        var (spanners, unpaired) = TextSpannerEngraver.PairTextSpanners(musicMarks);

        Assert.Equal(2, spanners.Length);
        Assert.True(unpaired.IsEmpty);
        Assert.Equal(0, spanners[0].StartMeasureIndex);
        Assert.Equal(1, spanners[0].EndMeasureIndex);
        Assert.Equal(4, spanners[1].StartMeasureIndex);
        Assert.Equal(5, spanners[1].EndMeasureIndex);
        Assert.Equal(
            spanners[0].EndMeasureIndex - spanners[0].StartMeasureIndex,
            spanners[1].EndMeasureIndex - spanners[1].StartMeasureIndex);
    }

    [Fact]
    public void DetectTextSpanners_AnUnclosedMarkPlayedTwice_IsReportedOncePerFault()
    {
        // ONE ROOT CAUSE, ONE DIAGNOSTIC: the reader forgot one terminator, not two, however
        // many times the form plays the bar it stands in. The two entries below are two
        // different faults at that one position — the second playing finds a span already
        // open, and the first is what is left unterminated at the end — not one fault twice.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 0, 42),
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 4, 42));

        var (_, unpaired) = TextSpannerEngraver.PairTextSpanners(musicMarks);

        Assert.Equal(2, unpaired.Length);
        Assert.All(unpaired, w => Assert.Equal(42, w.SourcePosition));
        Assert.Single(unpaired, w => w.Fault == TextSpanPairingFault.Unterminated);
        Assert.Single(unpaired, w => w.Fault == TextSpanPairingFault.StartWhileOpen);
    }

    [Fact]
    public void DetectTextSpanners_AStopInAnotherVoice_ClosesNothing()
    {
        // LILYPOND-REF: ly/engraver-init.ly:375 — Text_spanner_engraver stands in the Voice
        // context, so each voice holds its own open spanner. A terminator written in the
        // other voice reaches nothing, and BOTH marks are then unpaired.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 0, 10) { VoiceIndex = 0 },
            new MusicMarkItem(MusicMarkType.TextSpanStop, 3, 20) { VoiceIndex = 1 });

        var (spanners, unpaired) = TextSpannerEngraver.PairTextSpanners(musicMarks);

        Assert.True(spanners.IsEmpty);
        Assert.Equal(2, unpaired.Length);
        Assert.Single(unpaired, w => w.Fault == TextSpanPairingFault.StopWithNoStart);
        Assert.Single(unpaired, w => w.Fault == TextSpanPairingFault.Unterminated);
    }

    [Fact]
    public void DetectTextSpanners_TwoStavesPairIndependently()
    {
        // The staff filter that stood here before the terminator existed, kept: the staves
        // share score.MusicMarks, so a span opened on staff 1 must not be closed on staff 0.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.TextSpanStart, "rit.", 0, 10) { StaffIndex = 0 },
            new MusicMarkItem(MusicMarkType.TextSpanStart, "accel.", 1, 20) { StaffIndex = 1 },
            new MusicMarkItem(MusicMarkType.TextSpanStop, 2, 30) { StaffIndex = 1 },
            new MusicMarkItem(MusicMarkType.TextSpanStop, 4, 40) { StaffIndex = 0 });

        var (spanners, unpaired) = TextSpannerEngraver.PairTextSpanners(musicMarks);

        Assert.True(unpaired.IsEmpty);
        Assert.Equal(2, spanners.Length);
        var onStaff0 = Assert.Single(spanners, s => s.StaffIndex == 0);
        Assert.Equal(0, onStaff0.StartMeasureIndex);
        Assert.Equal(4, onStaff0.EndMeasureIndex);
        var onStaff1 = Assert.Single(spanners, s => s.StaffIndex == 1);
        Assert.Equal(1, onStaff1.StartMeasureIndex);
        Assert.Equal(2, onStaff1.EndMeasureIndex);
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
