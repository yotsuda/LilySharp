using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

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
        Assert.Equal(4, result[0].EndMeasureIndex);
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
        Assert.Equal(3, result[0].EndMeasureIndex); // ends at next ottava
        Assert.Equal(OttavaType.Ottava8vb, result[1].Type);
        Assert.Equal(3, result[1].StartMeasureIndex);
        Assert.Equal(5, result[1].EndMeasureIndex); // ends at loco
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

        var result = OttavaBracketEngraver.Calculate(brackets, systems, measures);

        Assert.Single(result);
        var layout = result[0];
        Assert.True(layout.IsAbove);
        Assert.True(layout.Y < 0, "8va should be above staff (negative Y)");
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

        var result = OttavaBracketEngraver.Calculate(brackets, systems, measures);

        Assert.Single(result);
        Assert.False(result[0].IsAbove);
        Assert.True(result[0].Y > 4, "8vb should be below staff (Y > staff height)");
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

        var result = OttavaBracketEngraver.Calculate(brackets, systems, measures);

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

        var result = OttavaBracketEngraver.Calculate(brackets, systems, measures);

        Assert.Equal(0.8, result[0].EdgeHeight);
    }

    [Fact]
    public void Calculate_EmptyBrackets_ReturnsEmpty()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);

        var result = OttavaBracketEngraver.Calculate(
            ImmutableArray<OttavaBracketItem>.Empty, systems, measures);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculate_OutOfRangeMeasure_Skipped()
    {
        var measures = CreateMeasureLayouts(2);
        var systems = CreateSingleSystem(2);
        var brackets = ImmutableArray.Create(new OttavaBracketItem(
            OttavaType.Ottava8va, 10, 12, 0));

        var result = OttavaBracketEngraver.Calculate(brackets, systems, measures);

        Assert.True(result.IsEmpty);
    }
}
