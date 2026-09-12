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
    //
    // ⚠️⚠️⚠️ THREE TESTS STOOD HERE AND ASSERTED NOTHING (removed 2026-09-13).
    // They were named FeatheredBeam_RightGrow_ConvergesAtLeft,
    // FeatheredBeam_LeftGrow_ConvergesAtRight and NormalBeam_NoFeathering, and each one
    // computed `growDir > 0 ? 0.0 : 1.0` INSIDE THE TEST and asserted the answer. No
    // production code was called; the arithmetic was the test's own. They are why nobody
    // noticed what the perturbation sweep found the day it was pointed at annotation
    // operands: the feather is not drawn at all. The intended geometry they described is
    // kept here because it is the specification whoever implements this will need —
    //
    //   grow-direction RIGHT: the secondary beam levels converge at the LEFT end (level
    //   offset × 0.0) and fan out at the RIGHT (× 1.0); LEFT is the mirror; a normal beam
    //   holds every level parallel (× 1.0 at both ends).
    //   LILYPOND-REF: beam.cc:1039-1082 grow-direction  (the same citation BeamDetector
    //   carries at the line that reads FeatherDirection).
    //
    // What replaces them is the one honest question, in
    // VocabularyPerturbationTests.TheFeatheredBeamIsNotDrawn_AndThatIsTheDefect: does the
    // page change? It does not, and that test will go red the day it does.
}
