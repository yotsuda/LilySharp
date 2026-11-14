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
    private const int P = (int)DynamicLevel.P, F = (int)DynamicLevel.F;

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
        // No level is written, so both voices stand at the default and the hairpin goes two
        // dynamics up. (A level written in one voice does not yet reach the other by its moment:
        // the second voice is walked after the first and opens at the level the first ended on.)
        int d = MidiExporter.DefaultVelocity;
        var v = Notes(OnePart("voice { c''4@cresc d'' e'' f'' | g''1@!cresc | } { c'2 c'2 | c'1 | }"), "fl");
        var lower = v.Where(n => n.Voice == 1).Select(n => n.Velocity).ToList();
        Assert.Equal(new[] { d, Along(d, d + 16, 2, 4), d + 16 }, lower);
    }
}
