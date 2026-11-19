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
    private const int P = (int)DynamicLevel.P, F = (int)DynamicLevel.F, Sfz = (int)DynamicLevel.SFZ, Mf = (int)DynamicLevel.MF;

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

    /// <summary>
    /// A hairpin beginning where the one before it ended, with no level written between, starts
    /// from what that one reached (session 1326). Until then it started from the level written
    /// before the first: <c>@p … @cresc … @decresc … @p</c> climbed 70 75 81, fell back to 70 on
    /// the decrescendo's first note and went on down, 68 65 (Lab sessions/p1326/probe m1, m2).
    /// </summary>
    [Fact]
    public void AHairpinBeginningWhereAnotherEnded_StartsFromWhatThatOneReached()
    {
        int top = P + 16;
        // Up three quarters to the decrescendo's note, and down three to the p.
        var swell = Velocities(OnePart("e'4@p f' e'@cresc f' | c'4 d'@decresc e' f' | g'1@p |"), "fl");
        Assert.Equal(new[] { P, P, P, Along(P, top, 1, 3), Along(P, top, 2, 3), top, Along(top, P, 1, 3), Along(top, P, 2, 3), P }, swell);
        // The next written after a stop on the same note is the same pair.
        Assert.Equal(swell, Velocities(OnePart("e'4@p f' e'@cresc f' | c'4 d'@!cresc@decresc e' f' | g'1@p |"), "fl"));
        // Two crescendos on end go two dynamics each, and what the second reached stands.
        int d = MidiExporter.DefaultVelocity;
        var twice = Velocities(OnePart("e'4 f' e'@cresc f' | c'4 d'@cresc e' f'@!cresc | g'1 |"), "fl");
        Assert.Equal(new[] { d, d, d, Along(d, d + 16, 1, 3), Along(d, d + 16, 2, 3), d + 16, d + 24, d + 32, d + 32 }, twice);
        // A level written on the next one's first note is where that one starts (control).
        var level = Velocities(OnePart("e'4@p f' e'@cresc f' | c'4 d'@f@decresc e' f' | g'1@p |"), "fl");
        Assert.Equal(new[] { P, P, P, Along(P, F, 1, 3), Along(P, F, 2, 3), F, Along(F, P, 1, 3), Along(F, P, 2, 3), P }, level);
        // …and so is the same number written again there: subito, then down.
        const int PP = (int)DynamicLevel.PP;
        var again = Velocities(OnePart("e'4@p f' e'@cresc f' | c'4 d'@p@decresc e' f' | g'1@pp |"), "fl");
        Assert.Equal(new[] { P, P, P, Along(P, top, 1, 3), Along(P, top, 2, 3), P, Along(P, PP, 1, 3), Along(P, PP, 2, 3), PP }, again);
        // A third on the second lands on the level written after it, though it is the first's.
        var three = Velocities(OnePart("e'4@p f' e'@cresc f' | c'4 d'@cresc e'@decresc f' | g'1@p |"), "fl");
        Assert.Equal(new[] { P, P, P, Along(P, top, 1, 3), Along(P, top, 2, 3), top, top + 16, Along(top + 16, P, 1, 2), P }, three);
        // A hairpin later on starts from its own level, not from what stood where another began.
        var later = Velocities(OnePart("e'4@p f' e'@cresc f' | c'4 d'@decresc e' f' | g'4@p g' g'@cresc g'@!cresc |"), "fl");
        Assert.Equal(new[] { P, P, P + 16 }, later.Skip(8).Take(4).Where((_, k) => k != 0));
        // A chord's notes all start from it.
        var chords = Notes(OnePart("<e' g'>4@p@cresc <e' g'> <e' g'>@decresc <e' g'> | <e' g'>1@p |"), "fl");
        Assert.Equal(new[] { P, P, P + 8, P + 8, top, top, P + 8, P + 8, P, P }, chords.Select(n => n.Velocity));
    }

    /// <summary>
    /// A level written again in the number already standing is a level written: the hairpin
    /// before it stops there and the note sounds at it (session 1326, MidiNote.LevelWritten).
    /// Until then the walk told a written level by its number changing, so a decrescendo from
    /// <c>@p</c> ran through the next <c>@p</c> — that note at 66, the one after it at 60 (Lab
    /// sessions/p1326/probe m3).
    /// </summary>
    [Fact]
    public void ALevelWrittenAgainInTheSameNumber_StopsTheHairpin_AndSoundsAsWritten()
    {
        // Two dynamics down over the three quarters to the p, which is p.
        var v = Velocities(OnePart("c'4@p d'@decresc e' f' | g'1@p | g'1 |"), "fl");
        Assert.Equal(new[] { P, P, Along(P, P - 16, 1, 3), Along(P, P - 16, 2, 3), P, P }, v);
        // After a stop, what the hairpin reached stands up to it and not over it.
        var stopped = Velocities(OnePart("c'4@p@cresc d' e'@!cresc f' | g'1@p | g'1 |"), "fl");
        Assert.Equal(new[] { P, P + 8, P + 16, P + 16, P, P }, stopped);
        // …in the other hand of a grand staff too.
        string book = "time 4/4\noctave absolute\npart rh { }\npart lh { clef bass }\nsection A {\n" +
            "  rh { c'4@p d'@decresc e' f' | g'1 | }\n  lh { c1 | c1@p | }\n}\nform { A }\nscore { grandStaff { staff rh staff lh } }\n";
        Assert.Equal(new[] { P, P, Along(P, P - 16, 1, 3), Along(P, P - 16, 2, 3), P }, Velocities(book, "rh"));
        // The second part of a bracket reads its own levels (and the first's are not its).
        string bracket = "time 4/4\noctave absolute\npart rh { }\npart lh { clef bass }\nsection A {\n" +
            "  rh { c'1 | c'1 | c'1 | }\n  lh { c4@p d@decresc e f | g1@p | g1 | }\n}\nform { A }\nscore { staffGroup { staff rh staff lh } }\n";
        Assert.Equal(v, Velocities(bracket, "lh"));
        // A voice span opening is no level written: the hairpin goes on through it to the f.
        var through = Velocities(OnePart("c'4@p@cresc d' e' f' | voice { g'1 } { e'1 } | g'1@f |"), "fl");
        Assert.Equal(new[] { P, Along(P, F, 1, 8), Along(P, F, 2, 8), Along(P, F, 3, 8), Along(P, F, 4, 8), Along(P, F, 4, 8), F }, through);
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

    /// <summary>A percent repeat's repetition sounds none of its bars' dynamics — the page prints
    /// the sign and none there, and the crescendo runs on through it (owner's decision 2026-11-15,
    /// as the MusicXML writes it). Until 2026-11-17 the repeated bar started the crescendo again from
    /// its p on its third beat: 78 82 70 82 (Lab sessions/p1290/pctxml/q2).</summary>
    [Fact]
    public void APercentRepetition_SoundsNoneOfItsBarsDynamics_TheCrescendoRunsThrough()
    {
        var v = Velocities(OnePart("c''4@p d'' e''@cresc f'' | % | g''1@f |"), "fl");
        Assert.Equal(9, v.Count);
        Assert.Equal(new[] { P, P }, v.Take(2));
        for (int i = 2; i < 8; i++)
            Assert.True(v[i + 1] > v[i], $"note {i + 1} ({v[i + 1]}) after note {i} ({v[i]}): {string.Join(" ", v)}");
        Assert.Equal(F, v[8]);
        // A bar whose hairpin closes inside it: its repetition stands at the level reached.
        var closed = Velocities(OnePart("c''4@p@cresc d'' e'' f''@!cresc | % | g''1@f |"), "fl");
        Assert.Equal(Enumerable.Repeat(closed[3], 4), closed.Skip(4).Take(4));
    }

    /// <summary>Two voices writing hairpins of their own each play their own — as a voice that
    /// writes its own dynamic at a moment plays its own. Until 2026-11-17 the part read them as one
    /// line from the moment's first swell, and the crescendo above sank with the decrescendo below
    /// (Lab sessions/p1290/hpmidi/v2).</summary>
    [Fact]
    public void TwoVoicesWritingTheirOwnHairpins_EachPlayItsOwn()
    {
        var notes = Notes(OnePart("voice { c''4@mf@cresc d'' e'' f'' | g''1@f | } { c'4@mf@decresc b a g | c'1@p | }"), "fl");
        var upper = notes.Where(n => n.Voice == 0).Select(n => n.Velocity).ToList();
        var lower = notes.Where(n => n.Voice == 1).Select(n => n.Velocity).ToList();
        Assert.Equal(new[] { Mf, Along(Mf, F, 1, 4), Along(Mf, F, 2, 4), Along(Mf, F, 3, 4), F }, upper);
        Assert.Equal(new[] { Mf, Along(Mf, P, 1, 4), Along(Mf, P, 2, 4), Along(Mf, P, 3, 4), P }, lower);
    }

    private static string Kit(string music) =>
        "time 4/4\noctave absolute\npart kit { clef percussion }\nsection A {\n  kit { " + music + " }\n}\nform { A }\nscore { staff kit }\n";

    [Fact]
    public void ADrum_PlaysItsHairpin_AsAnyPart()
    {
        // Until 2026-11-19 no drum note carried the hairpin's mark: P P P F, where a pitched
        // part's four notes climb (MEASURED, Lab sessions/p1318/mid/p3.lys against p1.lys).
        int[] climb = [P, Along(P, F, 1, 3), Along(P, F, 2, 3), F];
        Assert.Equal(climb, Velocities(Kit("sn4@p@cresc sn sn sn@f |"), "kit"));
        // A bare value after a drum note is that hit again.
        Assert.Equal(climb, Velocities(Kit("sn4@p@cresc 4 4 4@f |"), "kit"));
        // A drum chord: both hits of each onset.
        Assert.Equal(climb.SelectMany(v => new[] { v, v }), Velocities(Kit("<sn hh>4@p@cresc <sn hh> <sn hh> <sn hh>@f |"), "kit"));
        // …its bare value and its `q` too.
        Assert.Equal(climb.SelectMany(v => new[] { v, v }), Velocities(Kit("<sn hh>4@p@cresc 4 4 4@f |"), "kit"));
        Assert.Equal(climb.SelectMany(v => new[] { v, v }), Velocities(Kit("<sn hh>4@p@cresc q q q@f |"), "kit"));
        // A hairpin opened on a bare value, a drum's and a drum chord's: the mark is that hit's.
        Assert.Equal(climb, Velocities(Kit("sn4@p 4@p@cresc 4 4 | sn4@f r4 r2 |"), "kit").Skip(1));
        Assert.Equal(climb.SelectMany(v => new[] { v, v }), Velocities(Kit("<sn hh>4@p 4@p@cresc 4 4 | <sn hh>4@f r4 r2 |"), "kit").Skip(2));
        // A hairpin opened on a `q` of a drum chord.
        Assert.Equal(climb.SelectMany(v => new[] { v, v }), Velocities(Kit("<sn hh>4@p q@p@cresc q q | q4@f r4 r2 |"), "kit").Skip(2));
    }

    [Fact]
    public void AChartsTupletHit_EndsTheOtherLinesHairpin_AtItsOwnMoment()
    {
        // The f stands on the tuplet's second hit, a third of a half into the bar's second
        // half: the hi-hat's eighth before it is still on the way and the quarter after it has
        // arrived (the page's reading since session 1313; the MIDI was not measured then).
        var notes = Notes(Kit("drums { { hh2@p hh8@cresc hh hh4 } { bd2 tuplet 3/2 { bd4 bd@f bd } } } |"), "kit");
        int quarter = notes[0].DurationTicks / 2;
        int At(int tick, int key) => notes.Single(n => n.StartTick == tick && n.Pitch == key).Velocity;
        Assert.Equal(P, At(2 * quarter, 42));
        Assert.InRange(At(2 * quarter + quarter / 2, 42), P + 1, F - 1);
        Assert.Equal(F, At(2 * quarter + 2 * quarter / 3, 36));
        Assert.Equal(F, At(3 * quarter, 42));
    }

    [Fact]
    public void AChordRepetition_PlaysItsOwnMarks()
    {
        // Until 2026-11-19 a `q` read none of what was written on it: the f was drawn and never
        // sounded, nor stood for the notes after it, and a staccato was held its full length
        // (MEASURED, Lab sessions/p1318/mid/q1.lys: four chords at p, 480 ticks each).
        var notes = Notes(OnePart("<c' e'>4@p q q@f q | q4@accent q@staccato q q |"), "fl");
        Assert.Equal(new[] { P, P, F, F, F + 20, F, F, F }, notes.Where(n => n.Pitch == 60).Select(n => n.Velocity));
        var lengths = notes.Where(n => n.Pitch == 60).Select(n => n.DurationTicks).ToList();
        Assert.Equal(lengths[0] / 2, lengths[5]);
        Assert.Equal(lengths[0], lengths[6]);
    }
}
