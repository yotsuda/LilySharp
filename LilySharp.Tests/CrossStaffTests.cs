using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class CrossStaffTests
{
    // --- CrossStaffEngraver ---

    [Fact]
    public void CrossStaffEngraver_Calculate_EmptyInput()
    {
        var result = CrossStaffEngraver.Calculate(
            ImmutableArray<CrossStaffItem>.Empty, 0, 2);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void CrossStaffEngraver_Calculate_SingleStaff_ReturnsEmpty()
    {
        var items = ImmutableArray.Create(new CrossStaffItem(0, 1, 0, 0));
        var result = CrossStaffEngraver.Calculate(items, 0, 1);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void CrossStaffEngraver_Calculate_GrandStaff_TrebleToBase()
    {
        var items = ImmutableArray.Create(new CrossStaffItem(0, 2, 0, 0));
        var result = CrossStaffEngraver.Calculate(items, voiceStaffIndex: 0, staffCount: 2);
        Assert.Single(result);
        Assert.Equal(0, result[0].SourceStaffIndex); // treble
        Assert.Equal(1, result[0].TargetStaffIndex);  // bass
    }

    [Fact]
    public void CrossStaffEngraver_Calculate_GrandStaff_BassToTreble()
    {
        var items = ImmutableArray.Create(new CrossStaffItem(1, 0, 0, 0));
        var result = CrossStaffEngraver.Calculate(items, voiceStaffIndex: 1, staffCount: 2);
        Assert.Single(result);
        Assert.Equal(1, result[0].SourceStaffIndex); // bass
        Assert.Equal(0, result[0].TargetStaffIndex);  // treble
    }

    [Fact]
    public void CrossStaffEngraver_Calculate_MultipleItems()
    {
        var items = ImmutableArray.Create(
            new CrossStaffItem(0, 1, 0, 0),
            new CrossStaffItem(0, 3, 0, 0),
            new CrossStaffItem(1, 0, 0, 0));
        var result = CrossStaffEngraver.Calculate(items, voiceStaffIndex: 0, staffCount: 2);
        Assert.Equal(3, result.Length);
        Assert.All(result, r => Assert.Equal(1, r.TargetStaffIndex));
    }

    [Fact]
    public void CrossStaffEngraver_Calculate_ThreeStaves_Wrapping()
    {
        // With 3 staves and voice on staff 2, @cross goes to staff 0 (wrap)
        var items = ImmutableArray.Create(new CrossStaffItem(0, 0, 0, 0));
        var result = CrossStaffEngraver.Calculate(items, voiceStaffIndex: 2, staffCount: 3);
        Assert.Single(result);
        Assert.Equal(0, result[0].TargetStaffIndex);
    }

    [Fact]
    public void CrossStaffEngraver_BuildLookup()
    {
        var layouts = ImmutableArray.Create(
            new CrossStaffLayout(0, 1, 0, 1),
            new CrossStaffLayout(0, 3, 0, 1));
        var lookup = CrossStaffEngraver.BuildCrossStaffLookup(layouts);
        Assert.True(lookup.Contains((0, 1)));
        Assert.True(lookup.Contains((0, 3)));
        Assert.False(lookup.Contains((0, 2)));
    }

    [Fact]
    public void CrossStaffEngraver_GetTargetStaffIndex()
    {
        var layouts = ImmutableArray.Create(
            new CrossStaffLayout(0, 1, 0, 1),
            new CrossStaffLayout(1, 0, 0, 1));
        Assert.Equal(1, CrossStaffEngraver.GetTargetStaffIndex(layouts, 0, 1));
        Assert.Equal(1, CrossStaffEngraver.GetTargetStaffIndex(layouts, 1, 0));
        Assert.Equal(-1, CrossStaffEngraver.GetTargetStaffIndex(layouts, 0, 2));
    }

    // --- BeamGroup cross-staff detection ---

    [Fact]
    public void BeamGroup_IsCrossStaff_False_NoTargetStaff()
    {
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 1, 1, 0, 0),
            new BeamMember(CreateNote(2), 1, 1, 1, 2, 1));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.False(group.IsCrossStaff);
    }

    [Fact]
    public void BeamGroup_IsCrossStaff_True_WithTargetStaff()
    {
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 1, 1, 0, 0),
            new BeamMember(CreateNote(2), 1, 1, 1, 2, 1, targetStaffIndex: 1));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.True(group.IsCrossStaff);
    }

    [Fact]
    public void BeamGroup_IsCrossStaff_False_SingleMember()
    {
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 1, 1, 0, 0, targetStaffIndex: 1));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.False(group.IsCrossStaff);
    }

    // --- BeamLayout cross-staff ---

    [Fact]
    public void BeamLayout_IsCrossStaff_DelegatesFromGroup()
    {
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 1, 1, 0, 0),
            new BeamMember(CreateNote(2), 1, 1, 1, 2, 1, targetStaffIndex: 1));
        var group = new BeamGroup(members, 0, 0, true);
        var layout = new BeamLayout(group, 0, 0, 0, 10,
            ImmutableArray.Create(0.0, 10.0), 0,
            ImmutableArray.Create(0, 1));
        Assert.True(layout.IsCrossStaff);
        Assert.Equal(2, layout.MemberStaffIndices.Length);
    }

    [Fact]
    public void BeamLayout_MemberStaffIndices_DefaultEmpty()
    {
        var members = ImmutableArray.Create(
            new BeamMember(CreateNote(0), 1, 1, 1, 0, 0),
            new BeamMember(CreateNote(2), 1, 1, 1, 2, 1));
        var group = new BeamGroup(members, 0, 0, true);
        var layout = new BeamLayout(group, 0, 0, 0, 10,
            ImmutableArray.Create(0.0, 10.0));
        Assert.False(layout.IsCrossStaff);
        Assert.True(layout.MemberStaffIndices.IsEmpty);
    }

    // --- MeasureCollector integration ---

    [Fact]
    public void Collector_CrossStaff_Basic()
    {
        // @cross is a postfix annotation: c8 @cross means @cross attaches to c8
        var source = "c8 @cross d e @cross f";
        var tree = SyntaxTree.Parse(source);
        var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(2, score.CrossStaffItems.Length);
        Assert.Equal(0, score.CrossStaffItems[0].MeasureIndex);
        Assert.Equal(0, score.CrossStaffItems[0].ItemIndex); // c8 @cross → item 0
        Assert.Equal(0, score.CrossStaffItems[1].MeasureIndex);
        Assert.Equal(2, score.CrossStaffItems[1].ItemIndex); // e @cross → item 2
    }

    [Fact]
    public void Collector_CrossStaff_NoCrossAnnotation()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.CrossStaffItems.IsEmpty);
    }

    [Fact]
    public void Collector_CrossStaff_WithOtherAnnotations()
    {
        // @cross should be collected alongside other annotations (both on c8)
        var source = "c8 @cross @ff d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.CrossStaffItems);
        Assert.Equal(0, score.CrossStaffItems[0].ItemIndex); // c8 @cross → item 0
    }

    [Fact]
    public void Collector_CrossStaff_OnChord()
    {
        // @cross attaches to the preceding chord <c e g>8
        var source = "<c e g>8 @cross <d f a> <e g b> <f a c'>";
        var tree = SyntaxTree.Parse(source);
        var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.CrossStaffItems);
        Assert.Equal(0, score.CrossStaffItems[0].ItemIndex); // <c e g>8 @cross → item 0
    }

    [Fact]
    public void Collector_CrossStaff_AcrossMeasures()
    {
        // Use quarter notes: c4 d @cross e f fills 1 measure (4/4)
        // g @cross a b c' fills another measure
        var source = "c4 d @cross e f | g @cross a b c'";
        var tree = SyntaxTree.Parse(source);
        var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(2, score.CrossStaffItems.Length);
        Assert.Equal(0, score.CrossStaffItems[0].MeasureIndex);
        Assert.Equal(1, score.CrossStaffItems[0].ItemIndex); // d @cross → item 1
        Assert.Equal(1, score.CrossStaffItems[1].MeasureIndex);
        Assert.Equal(0, score.CrossStaffItems[1].ItemIndex); // g @cross → item 0
    }

    // --- Helper ---

    private static NoteItem CreateNote(int staffPos)
        => new(staffPos, new LilySharp.Core.Semantics.Fraction(1, 8), 0, null, false, 0);
}
