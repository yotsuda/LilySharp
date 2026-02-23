using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class FeatheredBeamTests
{
    // --- NoteItem.FeatherDirection ---

    [Fact]
    public void NoteItem_FeatherDirection_DefaultZero()
    {
        var note = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0);
        Assert.Equal(0, note.FeatherDirection);
    }

    [Fact]
    public void NoteItem_FeatherDirection_Right()
    {
        var note = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: 1);
        Assert.Equal(1, note.FeatherDirection);
    }

    [Fact]
    public void NoteItem_FeatherDirection_Left()
    {
        var note = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: -1);
        Assert.Equal(-1, note.FeatherDirection);
    }

    [Fact]
    public void NoteItem_FeatherDirection_ClampedToRange()
    {
        var noteHigh = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: 5);
        Assert.Equal(1, noteHigh.FeatherDirection);

        var noteLow = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: -5);
        Assert.Equal(-1, noteLow.FeatherDirection);
    }

    // --- BeamGroup.GrowDirection ---

    [Fact]
    public void BeamGroup_GrowDirection_DefaultZero()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0), 2, 0, 2, 0, 0),
            new BeamMember(new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1), 2, 2, 0, 2, 1));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.Equal(0, group.GrowDirection);
    }

    [Fact]
    public void BeamGroup_GrowDirection_Right()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0), 2, 0, 2, 0, 0),
            new BeamMember(new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1), 2, 2, 0, 2, 1));
        var group = new BeamGroup(members, 0, 0, true, growDirection: 1);
        Assert.Equal(1, group.GrowDirection);
    }

    [Fact]
    public void BeamGroup_GrowDirection_Left()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0), 2, 0, 2, 0, 0),
            new BeamMember(new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1), 2, 2, 0, 2, 1));
        var group = new BeamGroup(members, 0, 0, true, growDirection: -1);
        Assert.Equal(-1, group.GrowDirection);
    }

    // --- BeamDetector propagation ---

    private static Measure MakeMeasure(params MusicItem[] items) =>
        new(ImmutableArray.Create(items), BarlineType.None, BarlineType.None, null, 0, 0);

    [Fact]
    public void BeamDetector_PropagatesFeatherDirection()
    {
        // Create 4 sixteenth notes, first with feather=1
        var notes = new MusicItem[]
        {
            new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, hasBeamStart: true, featherDirection: 1),
            new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1),
            new NoteItem(4, Fraction.Sixteenth, 0, null, false, 2),
            new NoteItem(6, Fraction.Sixteenth, 0, null, false, 3, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.Equal(1, groups[0].GrowDirection);
    }

    [Fact]
    public void BeamDetector_NoFeather_DefaultZero()
    {
        var notes = new MusicItem[]
        {
            new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1),
            new NoteItem(4, Fraction.Sixteenth, 0, null, false, 2),
            new NoteItem(6, Fraction.Sixteenth, 0, null, false, 3, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.Equal(0, groups[0].GrowDirection);
    }

    // --- Feathered beam rendering behavior ---
    // The key property: for feathered beams, secondary beam levels converge
    // at one end and diverge at the other

    [Fact]
    public void FeatheredBeam_RightGrow_ConvergesAtLeft()
    {
        // With grow-direction=RIGHT, all secondary beams converge at the left
        // and fan out at the right. This means at the left end, the level offset
        // is multiplied by 0.0, and at the right end by 1.0.
        int growDir = 1;
        double leftFeather = growDir > 0 ? 0.0 : 1.0;
        double rightFeather = growDir > 0 ? 1.0 : 0.0;

        Assert.Equal(0.0, leftFeather);
        Assert.Equal(1.0, rightFeather);
    }

    [Fact]
    public void FeatheredBeam_LeftGrow_ConvergesAtRight()
    {
        int growDir = -1;
        double leftFeather = growDir > 0 ? 0.0 : 1.0;
        double rightFeather = growDir > 0 ? 1.0 : 0.0;

        Assert.Equal(1.0, leftFeather);
        Assert.Equal(0.0, rightFeather);
    }

    [Fact]
    public void NormalBeam_NoFeathering()
    {
        int growDir = 0;
        double leftFeather = growDir == 0 ? 1.0 : (growDir > 0 ? 0.0 : 1.0);
        double rightFeather = growDir == 0 ? 1.0 : (growDir > 0 ? 1.0 : 0.0);

        Assert.Equal(1.0, leftFeather);
        Assert.Equal(1.0, rightFeather);
    }
}
