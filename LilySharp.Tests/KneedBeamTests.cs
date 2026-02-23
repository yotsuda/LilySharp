using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class KneedBeamTests
{
    // --- BeamMember.MemberStemUp ---

    [Fact]
    public void BeamMember_MemberStemUp_DefaultTrue()
    {
        var member = new BeamMember(
            new NoteItem(0, Fraction.Eighth, 0, null, false, 0), 1, 0, 1, 0, 0);
        Assert.True(member.MemberStemUp);
    }

    [Fact]
    public void BeamMember_MemberStemUp_SetFalse()
    {
        var member = new BeamMember(
            new NoteItem(0, Fraction.Eighth, 0, null, false, 0), 1, 0, 1, 0, 0, memberStemUp: false);
        Assert.False(member.MemberStemUp);
    }

    // --- BeamGroup.IsKnee ---

    [Fact]
    public void BeamGroup_IsKnee_UniformDirection_False()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(-2, Fraction.Eighth, 0, null, false, 0), 1, 0, 1, -2, 0, true),
            new BeamMember(new NoteItem(-4, Fraction.Eighth, 0, null, false, 1), 1, 1, 0, -4, 1, true));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.False(group.IsKnee);
    }

    [Fact]
    public void BeamGroup_IsKnee_MixedDirections_True()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(-6, Fraction.Eighth, 0, null, false, 0), 1, 0, 1, -6, 0, true),
            new BeamMember(new NoteItem(6, Fraction.Eighth, 0, null, false, 1), 1, 1, 0, 6, 1, false));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.True(group.IsKnee);
    }

    [Fact]
    public void BeamGroup_IsKnee_SingleMember_False()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(0, Fraction.Eighth, 0, null, false, 0), 1, 0, 0, 0, 0, true));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.False(group.IsKnee);
    }

    // --- Auto-knee gap detection ---

    private static Measure MakeMeasure(params MusicItem[] items) =>
        new(ImmutableArray.Create(items), BarlineType.None, BarlineType.None, null, 0, 0);

    [Fact]
    public void BeamDetector_SmallGap_NoKnee()
    {
        // Notes close together: staff positions 0, 2, 4 (gap = 2, below threshold 5.5)
        var notes = new MusicItem[]
        {
            new NoteItem(0, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(2, Fraction.Eighth, 0, null, false, 1),
            new NoteItem(4, Fraction.Eighth, 0, null, false, 2, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.False(groups[0].IsKnee, "Small gap should not create knee");
    }

    [Fact]
    public void BeamDetector_LargeGap_UnifiedDirection()
    {
        // Notes far apart: staff positions -6 and 6 (gap = 12, exceeds threshold 5.5)
        // Kneed beam rendering is not yet implemented, so all members use group direction.
        var notes = new MusicItem[]
        {
            new NoteItem(-6, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(6, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.False(groups[0].IsKnee, "Kneed beam disabled — all members use unified direction");
    }

    [Fact]
    public void BeamDetector_LargeGap_AllMembersUseGroupDirection()
    {
        // Even with large gap, all members use the group's unified stem direction
        // (kneed beam rendering not yet implemented)
        var notes = new MusicItem[]
        {
            new NoteItem(-6, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(6, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        var group = groups[0];
        Assert.False(group.IsKnee);
        // All members should have the same direction as the group
        foreach (var member in group.Members)
        {
            Assert.Equal(group.StemUp, member.MemberStemUp);
        }
    }

    [Fact]
    public void BeamDetector_NonKneed_AllMembersSameDirection()
    {
        // All notes below middle line → all stem up
        var notes = new MusicItem[]
        {
            new NoteItem(-2, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(-4, Fraction.Eighth, 0, null, false, 1),
            new NoteItem(-3, Fraction.Eighth, 0, null, false, 2, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        var group = groups[0];
        Assert.False(group.IsKnee);
        // All members should have the same direction as the group
        foreach (var member in group.Members)
        {
            Assert.Equal(group.StemUp, member.MemberStemUp);
        }
    }

    [Fact]
    public void BeamDetector_ThreeNotes_LargeGap_UnifiedDirection()
    {
        // Three notes with large gap: low, high, low
        // Kneed beam disabled — all members use group direction
        var notes = new MusicItem[]
        {
            new NoteItem(-6, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(6, Fraction.Eighth, 0, null, false, 1),
            new NoteItem(-6, Fraction.Eighth, 0, null, false, 2, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.False(groups[0].IsKnee);
        // All members use group direction (average of -6, 6, -6 = -2 → stem up)
        foreach (var member in groups[0].Members)
        {
            Assert.Equal(groups[0].StemUp, member.MemberStemUp);
        }
    }

    [Fact]
    public void BeamDetector_BoundaryGap_NoKnee()
    {
        // Gap of exactly 5 (below 5.5 threshold): positions 0 and 5
        var notes = new MusicItem[]
        {
            new NoteItem(0, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(5, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.False(groups[0].IsKnee, "Gap of 5 (below 5.5) should not trigger knee");
    }

    [Fact]
    public void BeamDetector_AboveThreshold_UnifiedDirection()
    {
        // Gap above threshold (7 > 5.5) but kneed beam disabled — unified direction
        var notes = new MusicItem[]
        {
            new NoteItem(-1, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(6, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.False(groups[0].IsKnee, "Kneed beam disabled — all members use unified direction");
    }
}
