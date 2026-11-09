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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class CrossStaffTests
{
    // --- Which staff ---

    [Theory]
    [InlineData(0, 2, 1)]   // the grand staff's other staff, either way
    [InlineData(1, 2, 0)]
    [InlineData(0, 3, 1)]   // the next one down of more
    [InlineData(2, 3, 1)]   // the last goes up, not round to the first
    public void TargetStaffOf_IsTheOtherOrTheNextDown(int staff, int count, int target) =>
        Assert.Equal(target, CrossStaffEngraver.TargetStaffOf(staff, count));

    /// <summary>OMR's report (REQUESTS #24): a voice over a piano, the left hand's @cross.</summary>
    private const string SongWithPiano = """
        time 4/4
        part voice_ { clef treble }
        part rh { clef treble }
        part lh { clef bass }
        section A {
          voice_ { c''1 | }
          rh { c''2 c''2 | }
          lh { g'4@cross e'4@cross c4 e4 | }
        }
        form { A }
        score {
          staff voice_
          grandStaff {
            staff rh
            staff lh
          }
        }
        """;

    [Fact]
    public void AGrandStaffsHand_CrossesToTheOtherHand_NotToTheStaffBesideThePiano()
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(SongWithPiano);
        var (score, _) = LilySharp.Core.Svg.SvgGenerator.CollectAndLayout(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree).FirstOrDefault());
        var staves = score.EnumerateStaves().Select(s => s.Staff).ToList();
        Assert.Equal(3, staves.Count);
        Assert.Empty(staves[0].CrossedIn);
        Assert.Equal(2, staves[1].CrossedIn.Length);
    }

    [Fact]
    public void TheLilyPondTwin_ChangesToTheOtherHandToo()
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(SongWithPiano);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\change Staff = \"rh\"", ly);
        Assert.DoesNotContain("\\change Staff = \"voice_\"", ly);
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
        var layout = new BeamLayout(group, 0, 0, 0, 10, leftStemX: 0, rightStemX: 10,
            ImmutableArray.Create(0.0, 10.0), staffIndex: 0, systemIndex: 0,
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
        var layout = new BeamLayout(group, 0, 0, 0, 10, leftStemX: 0, rightStemX: 10,
            ImmutableArray.Create(0.0, 10.0), staffIndex: 0, systemIndex: 0);
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
        => new(staffPos, new LilySharp.Core.Semantics.Fraction(1, 8), 0, null, 0);
}
