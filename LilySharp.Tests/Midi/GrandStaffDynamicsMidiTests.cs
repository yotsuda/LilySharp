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
/// The hands of a grand staff share their dynamics in the playback: a dynamic written in either
/// staff sounds in both from its moment on (MidiExporter.ShareGrandStaffDynamics). A bracket is
/// several instruments and keeps one level per part.
/// </summary>
/// <remarks>
/// WHY IT EXISTS. The owner's report, 2026-10-14: samples/nocturne.lys's closing chord "is not
/// played". It was — at `@pp`, velocity 35, under a left hand that writes no dynamic at all and
/// sounded every bar at the default 80. A piano score writes one dynamic between the staves for
/// both hands; the owner's decision is that a grand staff's hands share it.
/// </remarks>
public class GrandStaffDynamicsMidiTests
{
    private static string Book(string group, string rh, string lh) => $$"""
        time 4/4
        part rh { clef treble }
        part lh { clef bass }
        section A {
          rh { {{rh}} }
          lh { {{lh}} }
        }
        form { A }
        score {
          {{group}} {
            staff rh
            staff lh
          }
        }
        """;

    private static List<int> Velocities(string src, string part) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).Where(n => n.Part == part)
            .OrderBy(n => n.StartTick).Select(n => n.Velocity).ToList();

    [Fact]
    public void AGrandStaffsLeftHand_FollowsTheRightHandsDynamics()
    {
        var src = Book("grandStaff", "c''1@p | d''1@f | e''1@pp |", "c1 | d1 | e1 |");
        Assert.Equal(Velocities(src, "rh"), Velocities(src, "lh"));
        Assert.Equal(new[] { (int)DynamicLevel.P, (int)DynamicLevel.F, (int)DynamicLevel.PP }, Velocities(src, "lh"));
    }

    [Fact]
    public void ABracketsParts_KeepTheirOwnDynamics()
    {
        var src = Book("staffGroup", "c''1@p | d''1@f | e''1@pp |", "c1 | d1 | e1 |");
        Assert.All(Velocities(src, "lh"), v => Assert.Equal(90, v));
    }

    // Two voices of one hand that write different dynamics at one moment each keep their own;
    // a dynamic the other hand writes later is then both voices'. Until session 1235 the hand
    // was read as standing at the last dynamic logged (the second voice's forte), and the
    // piano voice was moved by the distance from THAT: 70 + (86 − 95) = 61, under the mf shared.
    [Fact]
    public void AHandsVoicesAtDifferentLevels_BothTakeTheOtherHandsLaterDynamic()
    {
        var src = Book("grandStaff", "voice { c''1@p | c''1 | } { e'1@f | e'1 | }", "c1 | c1@mf |");
        var notes = new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).Where(n => n.Part == "rh").OrderBy(n => n.StartTick).ThenBy(n => n.Voice).ToList();
        Assert.Equal(new[] { (int)DynamicLevel.P, (int)DynamicLevel.F, (int)DynamicLevel.MF, (int)DynamicLevel.MF },
            notes.Select(n => n.Velocity));
    }

    // The voices of a span share the part's dynamics by the moment they are written at
    // (MidiExporter.ShareSpanDynamics). Until 2026-11-14 the second voice, walked after the
    // first, opened at the level the first one ENDED on and played both bars forte.
    [Fact]
    public void TheVoicesOfASpan_ShareTheirDynamicsByTheMoment()
    {
        static string One(string music) =>
            "time 4/4\noctave absolute\npart p { }\nsection A {\n  p { " + music + " }\n}\nform { A }\nscore { staff p }\n";
        List<int> Voice(string music, int voice) => new MidiExporter().Export(SyntaxTree.Parse(One(music))).Tracks
            .SelectMany(t => t.Notes).Where(n => n.Voice == voice).OrderBy(n => n.StartTick).Select(n => n.Velocity).ToList();
        const int p = (int)DynamicLevel.P, f = (int)DynamicLevel.F, pp = (int)DynamicLevel.PP;

        // Written in the upper voice: the lower one follows, bar by bar.
        const string upper = "voice { c''1@p | d''1@f | } { c'1 | d'1 | } e'1 |";
        Assert.Equal(new[] { p, f, f }, Voice(upper, 0));
        Assert.Equal(new[] { p, f }, Voice(upper, 1));
        // Written in the lower voice: the upper one follows, and the level stands after the span.
        const string lower = "voice { c''1 | d''1 | } { c'1@p | d'1@f | } e'1 |";
        Assert.Equal(new[] { p, f, f }, Voice(lower, 0));
        Assert.Equal(new[] { p, f }, Voice(lower, 1));
        // Both write one at the same moment: each plays its own, and the later single mark is both's.
        const string each = "voice { c''1@f | d''1 | } { c'1@p | d'1@pp | }";
        Assert.Equal(new[] { f, pp }, Voice(each, 0));
        Assert.Equal(new[] { p, pp }, Voice(each, 1));
        // No dynamic in the span: the level standing before it, in every voice.
        const string none = "b'1@p | voice { c''1 | } { c'1 | }";
        Assert.Equal(new[] { p, p }, Voice(none, 0));
        Assert.Equal(new[] { p }, Voice(none, 1));
    }

    [Fact]
    public void AtOneMoment_AHandsOwnDynamicWins()
    {
        // Both hands write a dynamic on the downbeat: each plays its own.
        var src = Book("grandStaff", "c''1@f |", "c1@p |");
        Assert.Equal((int)DynamicLevel.F, Velocities(src, "rh").Single());
        Assert.Equal((int)DynamicLevel.P, Velocities(src, "lh").Single());
    }

    [Fact]
    public void AnAccent_StaysAboveTheSharedLevel()
    {
        // The left hand's accent lifts its note above the level the right hand set, by what an
        // accent adds to the left hand's own level.
        var src = Book("grandStaff", "c''1@pp |", "c2 c2@accent |");
        var lh = Velocities(src, "lh");
        Assert.Equal((int)DynamicLevel.PP, lh[0]);
        Assert.True(lh[1] > lh[0]);
    }
}
