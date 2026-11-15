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
/// An accent dynamic written in one hand of a grand staff — <c>@sfz</c> and its kin, the attack
/// of an <c>@fp</c> — strikes the other hand too, where that hand starts a note at the same
/// moment (MidiExporter.ShareGrandStaffStrikes; owner's decision 2026-11-15). Until then the
/// hand it was written in struck alone.
/// </summary>
public class GrandStaffStrikeMidiTests
{
    private const int P = (int)DynamicLevel.P, F = (int)DynamicLevel.F, FF = (int)DynamicLevel.FF,
        Sfz = (int)DynamicLevel.SFZ, Rf = (int)DynamicLevel.RF, Fp = (int)DynamicLevel.FP;

    private static List<int> Velocities(string src, string part) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).Where(n => n.Part == part)
            .OrderBy(n => n.StartTick).Select(n => n.Velocity).ToList();

    private static string Hands(string rh, string lh, string group = "grandStaff") =>
        "time 4/4\noctave absolute\npart rh { }\npart lh { clef bass }\nsection A {\n" +
        "  rh { " + rh + " }\n  lh { " + lh + " }\n}\nform { A }\nscore { " + group + " { staff rh staff lh } }\n";

    private static int Along(int from, int to, int step, int steps) => (int)System.Math.Round(from + ((to - from) * (double)step / steps));

    [Fact]
    public void AnSfzInOneHand_StrikesTheOtherHandsNoteOfThatMoment()
    {
        string book = Hands("c'4@p d'@sfz e' f' |", "c4 c c c |");
        Assert.Equal(new[] { P, Sfz, P, P }, Velocities(book, "rh"));
        Assert.Equal(new[] { P, Sfz, P, P }, Velocities(book, "lh"));
        // Written in the left hand, it strikes the right.
        Assert.Equal(new[] { P, Sfz, P, P }, Velocities(Hands("c'4@p d' e' f' |", "c4 c@sfz c c |"), "rh"));
    }

    [Fact]
    public void AHandThatStartsNoNoteThere_PlaysOnAsItWas()
    {
        // The left hand holds a half note through the right hand's sfz on beat 2, and its next
        // note — a beat after the stroke — is at the level standing.
        Assert.Equal(new[] { P, P }, Velocities(Hands("c'4@p d'@sfz e' f' |", "c2 c2 |"), "lh"));
        // A rest under it.
        Assert.Equal(new[] { P, P }, Velocities(Hands("c'4@p d'@sfz e' f' |", "c4 r c2 |"), "lh"));
    }

    [Fact]
    public void TheAttackOfAnFp_StrikesTheOtherHand_AndTheLevelIsPianoInBoth()
    {
        string book = Hands("c'4@fp d' e' f' |", "c4 c c c |");
        Assert.Equal(new[] { Fp, P, P, P }, Velocities(book, "rh"));
        Assert.Equal(new[] { Fp, P, P, P }, Velocities(book, "lh"));
        // In a passage louder than its own number an fp is struck at the passage's level.
        string loud = Hands("c'4@ff d'@fp e' f' |", "c4 c c c |");
        Assert.True(FF > Fp);
        Assert.Equal(new[] { FF, FF, P, P }, Velocities(loud, "rh"));
        Assert.Equal(new[] { FF, FF, P, P }, Velocities(loud, "lh"));
    }

    [Fact]
    public void AHandsOwnAccentDynamicAtThatMoment_IsTheOneItPlays()
    {
        string book = Hands("c'4@p d'@sfz e' f' |", "c4 c@rf c c |");
        Assert.NotEqual(Sfz, Rf);
        Assert.Equal(new[] { P, Sfz, P, P }, Velocities(book, "rh"));
        Assert.Equal(new[] { P, Rf, P, P }, Velocities(book, "lh"));
    }

    [Fact]
    public void WhatTheOtherHandsNoteCarriesItself_StaysOnTopOfTheStroke()
    {
        // The left hand's own `@accent` on the struck note: as an sfz with an accent is in one hand.
        int both = Velocities(Hands("c'4@p d'@sfz@accent e' f' |", "c4 c c c |"), "rh")[1];
        Assert.True(both > Sfz);
        Assert.Equal(both, Velocities(Hands("c'4@p d'@sfz e' f' |", "c4 c@accent c c |"), "lh")[1]);
    }

    [Fact]
    public void InAHairpinTheHandsShare_TheStrokeIsReadWhereTheHairpinHasCome()
    {
        // Near a fortissimo an sfz is its step over the level, in both hands (InstrumentHairpinMidiTests).
        string book = Hands("c'4@p@cresc d' e' f'@sfz | g'1@ff |", "c4 c c c | c1 |");
        int near = Along(P, FF, 3, 4);
        Assert.True(near + 12 > Sfz);
        var swell = new[] { P, Along(P, FF, 1, 4), Along(P, FF, 2, 4), near + 12, FF };
        Assert.Equal(swell, Velocities(book, "rh"));
        Assert.Equal(swell, Velocities(book, "lh"));
    }

    [Fact]
    public void WrittenOnAnEmptyChord_ItStrikesTheOtherHandWhereTheNextNoteIs()
        => Assert.Equal(new[] { P, Sfz, P, P }, Velocities(Hands("c'4@p <>@sfz d' e' f' |", "c4 c c c |"), "lh"));

    [Fact]
    public void SeveralInstruments_KeepTheirOwn()
    {
        // A bracket is not one instrument: the other part hears nothing of it.
        Assert.Single(Velocities(Hands("c'4@p d'@sfz e' f' |", "c4 c c c |", "staffGroup"), "lh").Distinct());
        Assert.Single(Velocities(Hands("c'4@fp d' e' f' |", "c4 c c c |", "staffGroup"), "lh").Distinct());
    }

    [Fact]
    public void AnInstrumentBesideTheBrace_HearsNothingOfIt()
    {
        string book = "time 4/4\noctave absolute\npart fl { }\npart rh { }\npart lh { clef bass }\nsection A {\n" +
            "  fl { c''4@p d'' e'' f'' | }\n  rh { c'4@p d'@sfz e' f' | }\n  lh { c4 c c c | }\n}\nform { A }\n" +
            "score { staff fl grandStaff { staff rh staff lh } }\n";
        Assert.Equal(new[] { P, P, P, P }, Velocities(book, "fl"));
        Assert.Equal(new[] { P, Sfz, P, P }, Velocities(book, "lh"));
    }

    [Fact]
    public void TwoOtherHandsStrokesAtOneMoment_GiveTheLouder()
    {
        // Three staves under one brace (an organ): the first writes an rf, the second an sfz,
        // and the third, which writes neither, is struck with the louder — not the first met.
        string book = "time 4/4\noctave absolute\npart rh { }\npart mid { }\npart lh { clef bass }\nsection A {\n" +
            "  rh { c''4@p d''@rf e'' f'' | }\n  mid { c'4 d'@sfz e' f' | }\n  lh { c4 c c c | }\n}\nform { A }\n" +
            "score { grandStaff { staff rh staff mid staff lh } }\n";
        Assert.True(Sfz > Rf);
        Assert.Equal(new[] { P, Rf, P, P }, Velocities(book, "rh"));
        Assert.Equal(new[] { P, Sfz, P, P }, Velocities(book, "mid"));
        Assert.Equal(new[] { P, Sfz, P, P }, Velocities(book, "lh"));
    }

    [Fact]
    public void ACuesAccentDynamic_IsSilentLikeItsNotes()
        // A cue shows the player what another part plays and sounds nothing: neither does its sfz.
        => Assert.Equal(new[] { P, P, P, P }, Velocities(Hands("c'4@p cue { d'4@sfz } e' f' |", "c4 c c c |"), "lh"));

    [Fact]
    public void ASectionPlayedTwice_IsStruckInBothPlays()
    {
        string book = "time 4/4\noctave absolute\npart rh { }\npart lh { clef bass }\nsection A {\n" +
            "  rh { c'4@p d'@sfz e' f' | }\n  lh { c4 c c c | }\n}\nform { A A }\nscore { grandStaff { staff rh staff lh } }\n";
        Assert.Equal(new[] { P, Sfz, P, P, P, Sfz, P, P }, Velocities(book, "lh"));
    }
}
