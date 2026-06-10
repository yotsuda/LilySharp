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
    public void BeamDetector_LargeGap_IsKnee()
    {
        // Staff positions -8 and 8: widened head extents [-9,-7] / [7,9] leave an
        // interior gap of 14 positions (7 SS) > 5.5 + beam height (5.74 SS).
        // LILYPOND-REF: beam.cc:968-1056 consider_auto_knees
        var notes = new MusicItem[]
        {
            new NoteItem(-8, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(8, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.True(groups[0].IsKnee, "Large gap should create kneed beam");
    }

    [Fact]
    public void BeamDetector_LargeGap_PerMemberStemDirections()
    {
        // LILYPOND-REF: beam.cc:968-1056 consider_auto_knees
        // Stems point INTO the gap: heads below the gap center stem up,
        // heads above stem down.
        var notes = new MusicItem[]
        {
            new NoteItem(-8, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(8, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        var group = groups[0];
        Assert.True(group.IsKnee);
        // Note at -8 (below the gap) → stem up; note at 8 (above) → stem down
        Assert.True(group.Members[0].MemberStemUp);
        Assert.False(group.Members[1].MemberStemUp);
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
    public void BeamDetector_ThreeNotes_LargeGap_KneedPerMember()
    {
        // Three notes with large gap: low, high, low
        // LILYPOND-REF: beam.cc:968-1056 consider_auto_knees
        var notes = new MusicItem[]
        {
            new NoteItem(-8, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(8, Fraction.Eighth, 0, null, false, 1),
            new NoteItem(-8, Fraction.Eighth, 0, null, false, 2, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.True(groups[0].IsKnee);
        // Low notes → stem up, high note → stem down
        Assert.True(groups[0].Members[0].MemberStemUp);   // -8
        Assert.False(groups[0].Members[1].MemberStemUp);   // 8
        Assert.True(groups[0].Members[2].MemberStemUp);    // -8
    }

    [Fact]
    public void BeamDetector_BoundaryGap_NoKnee()
    {
        // Gap of 5 staff positions = 2.5 staff spaces (below 5.5 staff spaces threshold)
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
    public void BeamDetector_GapBelowThresholdInStaffSpaces_NoKnee()
    {
        // Gap of 7 staff positions = 3.5 staff spaces (below threshold 5.5 staff spaces)
        // LILYPOND-REF: define-grobs.scm:437 auto-knee-gap = 5.5 (staff spaces)
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
        Assert.False(groups[0].IsKnee, "3.5 staff spaces < 5.5 threshold");
    }

    [Fact]
    public void BeamDetector_GapMustExceedKneeGapPlusBeamHeight()
    {
        // The knee threshold is auto-knee-gap (5.5 SS) PLUS the beam stack
        // height (thickness/2 = 0.24 SS for a single 8th beam), compared
        // strictly. A widened-extent gap of exactly 5.5 SS must NOT knee.
        // LILYPOND-REF: beam.cc:1033-1042
        var atKneeGap = new MusicItem[]
        {
            // positions -6 / 7: widened extents [-7,-5] / [6,8] → gap 11 = 5.5 SS
            new NoteItem(-6, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(7, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(
            new Voice("default", ImmutableArray.Create(MakeMeasure(atKneeGap))),
            new TimeSignature(4, 4));
        Assert.NotEmpty(groups);
        Assert.False(groups[0].IsKnee, "5.5 SS gap == auto-knee-gap but <= +beam height");

        var overThreshold = new MusicItem[]
        {
            // positions -6 / 8: gap 12 positions = 6 SS > 5.74 SS
            new NoteItem(-6, Fraction.Eighth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(8, Fraction.Eighth, 0, null, false, 1, hasBeamEnd: true),
        };
        groups = detector.DetectBeamGroups(
            new Voice("default", ImmutableArray.Create(MakeMeasure(overThreshold))),
            new TimeSignature(4, 4));
        Assert.NotEmpty(groups);
        Assert.True(groups[0].IsKnee, "6 SS gap > 5.5 + 0.24 SS threshold");
    }
}
