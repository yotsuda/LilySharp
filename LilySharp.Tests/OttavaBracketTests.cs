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
using LilySharp.Core.Rendering;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class OttavaBracketTests
{
    // --- OttavaType enum ---

    [Fact]
    public void OttavaType_HasExpectedValues()
    {
        Assert.Equal(0, (int)OttavaType.Ottava8va);
        Assert.Equal(1, (int)OttavaType.Ottava8vb);
        Assert.Equal(2, (int)OttavaType.Quindicesima15ma);
        Assert.Equal(3, (int)OttavaType.Quindicesima15mb);
    }

    // --- MusicMarkType ottava extensions ---

    [Fact]
    public void MusicMarkType_ParsesOttavaNames()
    {
        Assert.Equal(MusicMarkType.OttavaUp, MusicMarkItem.ParseMarkName("ottava"));
        Assert.Equal(MusicMarkType.OttavaDown, MusicMarkItem.ParseMarkName("ottava.bassa"));
        Assert.Equal(MusicMarkType.QuindicesUp, MusicMarkItem.ParseMarkName("quindicesima"));
        Assert.Equal(MusicMarkType.QuindicesDown, MusicMarkItem.ParseMarkName("quindicesima.bassa"));
        Assert.Equal(MusicMarkType.Loco, MusicMarkItem.ParseMarkName("loco"));
    }

    [Fact]
    public void MusicMarkItem_OttavaUp_IsAboveStaff()
    {
        var mark = new MusicMarkItem(MusicMarkType.OttavaUp, 0, 0);
        Assert.Equal(MusicMarkVertical.Above, mark.Vertical);
        Assert.Equal("8va", mark.Text);
    }

    [Fact]
    public void MusicMarkItem_OttavaDown_IsBelowStaff()
    {
        var mark = new MusicMarkItem(MusicMarkType.OttavaDown, 0, 0);
        Assert.Equal(MusicMarkVertical.Below, mark.Vertical);
        Assert.Equal("8vb", mark.Text);
    }

    // --- DetectOttavaBrackets ---

    [Fact]
    public void DetectOttavaBrackets_NoOttavaMarks_ReturnsEmpty()
    {
        var musicMarks = ImmutableArray<MusicMarkItem>.Empty;

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectOttavaBrackets_OnlyNonOttavaMarks_ReturnsEmpty()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Segno, 0, 0),
            new MusicMarkItem(MusicMarkType.Rit, 2, 0));

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectOttavaBrackets_OttavaFollowedByLoco_CreatesBracket()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.OttavaUp, 1, 42),
            new MusicMarkItem(MusicMarkType.Loco, 4, 0));

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.Single(result);
        Assert.Equal(OttavaType.Ottava8va, result[0].Type);
        Assert.Equal(1, result[0].StartMeasureIndex);
        Assert.Equal(3, result[0].EndMeasureIndex); // ends at measure before loco
        Assert.Equal(42, result[0].SourcePosition);
    }

    [Fact]
    public void DetectOttavaBrackets_OttavaWithNoEnd_ExtendsOneMore()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.OttavaUp, 3, 0));

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.Single(result);
        Assert.Equal(3, result[0].StartMeasureIndex);
        Assert.Equal(4, result[0].EndMeasureIndex);
    }

    [Fact]
    public void DetectOttavaBrackets_OttavaDown_CreatesOttava8vb()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.OttavaDown, 0, 0),
            new MusicMarkItem(MusicMarkType.Loco, 2, 0));

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.Single(result);
        Assert.Equal(OttavaType.Ottava8vb, result[0].Type);
    }

    [Fact]
    public void DetectOttavaBrackets_Quindicesima_Creates15ma()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.QuindicesUp, 0, 0),
            new MusicMarkItem(MusicMarkType.Loco, 3, 0));

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.Single(result);
        Assert.Equal(OttavaType.Quindicesima15ma, result[0].Type);
    }

    [Fact]
    public void DetectOttavaBrackets_LocoAlone_ReturnsEmpty()
    {
        // Loco by itself doesn't create a bracket
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.Loco, 5, 0));

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectOttavaBrackets_TwoOttavas_TwoBrackets()
    {
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.OttavaUp, 0, 0),
            new MusicMarkItem(MusicMarkType.OttavaDown, 3, 0),
            new MusicMarkItem(MusicMarkType.Loco, 5, 0));

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.Equal(2, result.Length);
        Assert.Equal(OttavaType.Ottava8va, result[0].Type);
        Assert.Equal(0, result[0].StartMeasureIndex);
        Assert.Equal(2, result[0].EndMeasureIndex); // ends at measure before next ottava
        Assert.Equal(OttavaType.Ottava8vb, result[1].Type);
        Assert.Equal(3, result[1].StartMeasureIndex);
        Assert.Equal(4, result[1].EndMeasureIndex); // ends at measure before loco
    }

    // --- OttavaBracketEngraver.Calculate ---

    private static ImmutableArray<MeasureLayout> CreateMeasureLayouts(int count, double measureWidth = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var items = ImmutableArray.Create(
                new ItemLayout(0, 1.0, 2.0),
                new ItemLayout(1, 5.0, 2.0));
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
    public void Calculate_8va_PositionsAboveStaff()
    {
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var brackets = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 0, 3, 42));

        var result = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, brackets, systems, measures);

        Assert.Single(result);
        var layout = result[0];
        Assert.True(layout.IsAbove);
        Assert.True(layout.YUp > 0, "8va should be above staff (positive Y-up)");
        Assert.Equal("8va", layout.Text);
        Assert.Equal(42, layout.SourcePosition);
    }

    [Fact]
    public void Calculate_8vb_PositionsBelowStaff()
    {
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var brackets = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8vb, 0, 3, 0));

        var result = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, brackets, systems, measures);

        Assert.Single(result);
        Assert.False(result[0].IsAbove);
        Assert.True(result[0].YUp < -4, "8vb should be below staff (Y-up < -staff height)");
        Assert.Equal("8vb", result[0].Text);
    }

    [Fact]
    public void Calculate_DashParameters_MatchLilyPond()
    {
        // LILYPOND-REF: scm/define-grobs.scm:2449 (dash-fraction . 0.3)
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var brackets = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 0, 3, 0));

        var result = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, brackets, systems, measures);

        Assert.Equal(0.3, result[0].DashFraction);
    }

    [Fact]
    public void Calculate_EdgeHeight_MatchesLilyPond()
    {
        // LILYPOND-REF: scm/define-grobs.scm:2451 (edge-height . (0 . 0.8))
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        var brackets = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 0, 3, 0));

        var result = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, brackets, systems, measures);

        Assert.Equal(0.8, result[0].EdgeHeight);
    }

    [Fact]
    public void Calculate_EmptyBrackets_ReturnsEmpty()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);

        var result = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, 
            ImmutableArray<OttavaBracketItem>.Empty, systems, measures);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void DetectOttavaBrackets_LocoOnOtherStaff_DoesNotTerminate()
    {
        // A grand staff: staff 0 runs an 8va (measures 0..), staff 1 has its own
        // loco at measure 2. The lower staff's loco must NOT end the upper 8va —
        // termination is per staff. Staff 0's bracket has no same-staff terminator,
        // so it extends one measure past its start.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.OttavaUp, 0, 0) { StaffIndex = 0 },
            new MusicMarkItem(MusicMarkType.Loco, 2, 0) { StaffIndex = 1 });

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.Single(result);
        Assert.Equal(0, result[0].StaffIndex);
        Assert.Equal(0, result[0].StartMeasureIndex);
        // No same-staff terminator -> extends one measure past the start,
        // rather than ending at the other staff's loco (measure 1).
        Assert.Equal(1, result[0].EndMeasureIndex);
    }

    [Fact]
    public void DetectOttavaBrackets_TerminatesWithinSameStaff()
    {
        // Two staves each with their own 8va + loco, interleaved by measure.
        var musicMarks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.OttavaUp, 0, 0) { StaffIndex = 0 },
            new MusicMarkItem(MusicMarkType.OttavaUp, 0, 0) { StaffIndex = 1 },
            new MusicMarkItem(MusicMarkType.Loco, 3, 0) { StaffIndex = 0 },
            new MusicMarkItem(MusicMarkType.Loco, 3, 0) { StaffIndex = 1 });

        var result = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);

        Assert.Equal(2, result.Length);
        // Each bracket ends at the measure before ITS OWN staff's loco (measure 2).
        Assert.All(result, b => Assert.Equal(2, b.EndMeasureIndex));
        Assert.Contains(result, b => b.StaffIndex == 0);
        Assert.Contains(result, b => b.StaffIndex == 1);
    }

    [Fact]
    public void Calculate_LowerStaff_OffsetsBracketToOwnStaff()
    {
        // With a staff Y offset supplied, a lower-staff bracket sits over/under
        // that staff (its Y shifts down by the offset), not the top staff.
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        Func<int, int, double> staffYByIndex = (_, staffIndex) => staffIndex == 1 ? 12.0 : 0.0;

        var top = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 0, 3, 0, StaffIndex: 0));
        var low = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 0, 3, 0, StaffIndex: 1));

        var topLayout = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, top, systems, measures, staffYByIndex)[0];
        var lowLayout = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, low, systems, measures, staffYByIndex)[0];

        Assert.Equal(1, lowLayout.StaffIndex);
        // Lower staff's 8va is 12 staff-spaces below the top staff's 8va.
        // Y-up: a 12-ss-lower staff has a 12-smaller Y-up.
        Assert.Equal(topLayout.YUp - 12.0, lowLayout.YUp, 3);
    }

    [Fact]
    public void Calculate_LowerStaff_8vb_OffsetsBelowOwnStaff()
    {
        // 8vb (below) on the lower staff hangs below THAT staff.
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        Func<int, int, double> staffYByIndex = (_, staffIndex) => staffIndex == 1 ? 12.0 : 0.0;

        var low = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8vb, 0, 3, 0, StaffIndex: 1));

        var lowLayout = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, low, systems, measures, staffYByIndex)[0];

        Assert.False(lowLayout.IsAbove);
        // Below-staff Y (staff bottom + padding) shifted down to the lower staff.
        Assert.True(lowLayout.YUp < -12.0, "8vb on staff 1 should be below that staff");
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);
        var brackets = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 10, 12, 0));

        var result = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, brackets, systems, measures);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_ResolvesStaffOffsetPerMeasure()
    {
        // The staff-Y resolver is keyed by (measureIndex, staffIndex): a bracket's
        // offset comes from the system its OWN measure falls in. Under hara-kiri a
        // staff's within-system Y differs between systems (a hidden upper staff shifts
        // the staves below it up), so two brackets on the SAME staff in different
        // measures must each pick up THEIR measure's offset — the fix for the old
        // system-0-only staffYByIndex, which gave every system the first system's Y.
        var measures = CreateMeasureLayouts(4);
        var systems = CreateSingleSystem(4);
        // Simulate a later system (measures >= 2) where the staff has shifted down 20.
        Func<int, int, double> staffYAt = (measureIndex, _) => measureIndex >= 2 ? 20.0 : 0.0;

        var early = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 0, 0, 0, StaffIndex: 0));
        var late = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 3, 3, 0, StaffIndex: 0));

        var earlyLayout = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, early, systems, measures, staffYAt)[0];
        var lateLayout = OttavaBracketEngraver.Calculate(ScoreTextMetrics.Bundled, late, systems, measures, staffYAt)[0];

        // Same staff, but the later bracket's measure resolves to the +20 offset.
        // Y-up: a 20-ss-lower system has a 20-smaller Y-up.
        Assert.Equal(earlyLayout.YUp - 20.0, lateLayout.YUp, 3);
    }
}
