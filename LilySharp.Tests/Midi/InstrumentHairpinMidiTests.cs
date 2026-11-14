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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// An instrument plays a hairpin: each note is struck at the level the hairpin has reached
/// (MidiExporter.SwellInstruments; owner's decision 2026-11-14). Until then only a singer
/// swelled, and every note of an instrument's crescendo sounded at the level written before it.
/// </summary>
public class InstrumentHairpinMidiTests
{
    private const int P = (int)DynamicLevel.P, F = (int)DynamicLevel.F, Sfz = (int)DynamicLevel.SFZ;

    private static List<MidiNote> Notes(string src, string part) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).Where(n => n.Part == part)
            .OrderBy(n => n.StartTick).ToList();

    private static List<int> Velocities(string src, string part) => Notes(src, part).Select(n => n.Velocity).ToList();

    private static string OnePart(string music) =>
        "time 4/4\noctave absolute\npart fl { }\nsection A {\n  fl { " + music + " }\n}\nform { A }\nscore { staff fl }\n";

    private static int Along(int from, int to, int step, int steps) => (int)System.Math.Round(from + ((to - from) * (double)step / steps));

    [Fact]
    public void ACrescendo_ClimbsToTheLevelWrittenAfterIt()
    {
        var v = Velocities(OnePart("c''4@p@cresc d'' e'' f'' | g''4@f a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { P, Along(P, F, 1, 4), Along(P, F, 2, 4), Along(P, F, 3, 4), F, F, F, F }, v);
        Assert.True(v[0] < v[1] && v[1] < v[2] && v[2] < v[3] && v[3] < v[4]);
    }

    [Fact]
    public void AHairpinWithNoLevelAfterIt_GoesTwoDynamics_AndWhatItReachedStands()
    {
        // The decrescendo ends on the fifth note (`@!decresc`) with no level written: 16 down
        // over the four quarters before it, and the notes after it stay there.
        var v = Velocities(OnePart("c''4@f@decresc d'' e'' f'' | g''4@!decresc a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { F, Along(F, F - 16, 1, 4), Along(F, F - 16, 2, 4), Along(F, F - 16, 3, 4), F - 16, F - 16, F - 16, F - 16 }, v);
    }

    [Fact]
    public void TheWrittenLevel_StaysOnTheNote_ForTheSinger()
    {
        var notes = Notes(OnePart("c''4@p@cresc d'' e'' f'' | g''1@f |"), "fl");
        // The first note is struck at the level written, so nothing is kept beside it…
        Assert.Equal(0, notes[0].WrittenVelocity);
        // …and a note the hairpin moved remembers where it was written.
        Assert.Equal(P, notes[2].WrittenVelocity);
        Assert.Equal(P, SungVoices.Written(notes[2]));
        Assert.Equal(F, SungVoices.Written(notes[4]));
    }

    [Fact]
    public void AnAccentInsideAHairpin_IsStruckOverWhereTheHairpinHasCome_AndDoesNotEndIt()
    {
        // Until session 1232 the accented note was read as the level the crescendo was going
        // to: P, P+20, P, P, F — the hairpin ended on the second note.
        var v = Velocities(OnePart("c''4@p@cresc d''@accent e'' f'' | g''4@f a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { P, Along(P, F, 1, 4) + 20, Along(P, F, 2, 4), Along(P, F, 3, 4), F, F, F, F }, v);
        // A marcato on the way down (control: the same line without it).
        var down = Velocities(OnePart("c''4@f@decresc d'' e''@marcato f'' | g''4@p a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { F, Along(F, P, 1, 4), Along(F, P, 2, 4) + 30, Along(F, P, 3, 4), P, P, P, P }, down);
    }

    [Fact]
    public void AnAccentDynamicInsideAHairpin_IsOneNotes_AndTheHairpinGoesOn()
    {
        // `@sfz` is struck at its own number where the hairpin has come to a level under it.
        // Until session 1235 it kept the step it made at piano (34) over the level: 116 here.
        var v = Velocities(OnePart("c''4@p@cresc d'' e''@sfz f'' | g''4@f a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { P, Along(P, F, 1, 4), Sfz, Along(P, F, 3, 4), F, F, F, F }, v);
        // The hairpin's own first note may carry one too.
        var first = Velocities(OnePart("c''4@p@accent@cresc d'' e'' f'' | g''4@f a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { P + 20, Along(P, F, 1, 4), Along(P, F, 2, 4), Along(P, F, 3, 4), F, F, F, F }, first);
    }

    [Fact]
    public void AnAccentDynamic_IsReadAtTheLevelTheHairpinHasComeTo()
    {
        const int FF = (int)DynamicLevel.FF;
        // Where the crescendo has passed the sforzato's own number less the step, it is the
        // step over the level, as it is written in a loud passage (it was 127).
        int near = Along(P, FF, 3, 4);
        Assert.True(near + 12 > Sfz);
        var late = Velocities(OnePart("c''4@p@cresc d'' e'' f''@sfz | g''4@ff a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { P, Along(P, FF, 1, 4), Along(P, FF, 2, 4), near + 12, FF, FF, FF, FF }, late);
        // What the note's scripts add stays on top of it.
        var both = Velocities(OnePart("c''4@p@cresc d'' e''@sfz@accent f'' | g''4@f a'' b'' c''' |"), "fl");
        Assert.Equal(Sfz + 20, both[2]);
        // Written on an empty chord, for the next note.
        var held = Velocities(OnePart("c''4@p@cresc d'' <>@sfz e'' f'' | g''4@f a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { P, Along(P, F, 1, 4), Sfz, Along(P, F, 3, 4), F, F, F, F }, held);
        // On the way down it stays its own number too.
        var down = Velocities(OnePart("c''4@f@decresc d'' e''@sfz f'' | g''4@p a'' b'' c''' |"), "fl");
        Assert.Equal(new[] { F, Along(F, P, 1, 4), Sfz, Along(F, P, 3, 4), P, P, P, P }, down);
    }

    [Fact]
    public void AnAccentDynamicUnderASharedLevel_IsReadAtThatLevel()
    {
        const int PP = (int)DynamicLevel.PP;
        // A grand staff: the left hand writes no level and its `@sfz` is under the right hand's
        // `@pp`. It was the step made at the hand's own default, carried down: 104 − (90 − 62) = 76.
        string book = "time 4/4\noctave absolute\npart rh { }\npart lh { clef bass }\nsection A {\n" +
            "  rh { c'4@pp d' e' f' | }\n  lh { c4 c@sfz c c | }\n}\nform { A }\nscore { grandStaff { staff rh staff lh } }\n";
        Assert.Equal(new[] { PP, Sfz, PP, PP }, Velocities(book, "lh"));
        // The voices of a span, the same way.
        var lower = Notes(OnePart("voice { c''1@pp | } { c'4 c'@sfz c' c' | }"), "fl").Where(n => n.Voice == 1).Select(n => n.Velocity);
        Assert.Equal(new[] { PP, Sfz, PP, PP }, lower);
    }

    [Fact]
    public void AnAccentAfterAHairpinWithNoLevel_IsOverWhatTheHairpinReached()
    {
        int d = MidiExporter.DefaultVelocity;
        var v = Velocities(OnePart("c''4@cresc d'' e'' f'' | g''4@!cresc a''@accent b'' c''' |"), "fl");
        Assert.Equal(new[] { d, Along(d, d + 16, 1, 4), Along(d, d + 16, 2, 4), Along(d, d + 16, 3, 4), d + 16, d + 36, d + 16, d + 16 }, v);
    }

    [Fact]
    public void NoHairpin_NoChange()
    {
        Assert.Equal(new[] { P, P, P, P, F }, Velocities(OnePart("c''4@p d'' e'' f'' | g''1@f |"), "fl"));
    }

    [Fact]
    public void AGrandStaffsHands_ShareTheHairpin_AndABracketsPartsDoNot()
    {
        string Book(string group) =>
            "time 4/4\noctave absolute\npart rh { }\npart lh { clef bass }\nsection A {\n" +
            "  rh { c'4@p@cresc d' e' f' | g'1@f | }\n  lh { c4 c c c | c1 | }\n}\nform { A }\n" +
            "score { " + group + " { staff rh staff lh } }\n";
        var swell = new[] { P, Along(P, F, 1, 4), Along(P, F, 2, 4), Along(P, F, 3, 4), F };
        Assert.Equal(swell, Velocities(Book("grandStaff"), "rh"));
        Assert.Equal(swell, Velocities(Book("grandStaff"), "lh"));
        // Several instruments: the other part plays on at its own level.
        Assert.Equal(swell, Velocities(Book("staffGroup"), "rh"));
        Assert.Single(Velocities(Book("staffGroup"), "lh").Distinct());
    }

    [Fact]
    public void EveryVoiceOfThePart_FollowsItsHairpin()
    {
        var both = Notes(OnePart("voice { c''4@p@cresc d'' e'' f'' | g''1@f | } { c'2 c'2 | c'1 | }"), "fl");
        Assert.Equal(new[] { P, Along(P, F, 2, 4), F }, both.Where(n => n.Voice == 1).Select(n => n.Velocity));
        // With no level written, both voices stand at the default and the hairpin goes two dynamics up.
        int d = MidiExporter.DefaultVelocity;
        var v = Notes(OnePart("voice { c''4@cresc d'' e'' f'' | g''1@!cresc | } { c'2 c'2 | c'1 | }"), "fl");
        var lower = v.Where(n => n.Voice == 1).Select(n => n.Velocity).ToList();
        Assert.Equal(new[] { d, Along(d, d + 16, 2, 4), d + 16 }, lower);
    }
}
