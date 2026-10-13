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
/// A bar of the wrong length re-syncs at its bar line: the bars after it sound where the
/// other parts' bars do, as the page lays them out (MidiExporter.AlignBarLine).
/// </summary>
/// <remarks>
/// WHY IT EXISTS. The owner's report, 2026-10-14 (Lab sessions/p904/sync): `c'4 d e | g2 g
/// c,8 | e1` beside a correct part — a bar a beat short, then one an eighth long — played its
/// last bar an eighth early against the other part, and every bar after a miscount stayed out
/// of step until the section ended. The page has always put bar N under bar N; the MIDI
/// counted durations. Each case pairs the miscounted part with the bar line the other part
/// (or the meter) says it should reach.
/// </remarks>
public class BarLineSyncMidiTests
{
    private const int Bar = 1920; // a 4/4 bar at 480 ticks per quarter

    private static List<MidiNote> Notes(string src, string part) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).Where(n => n.Part == part).OrderBy(n => n.StartTick).ToList();

    private static string Book(string p1, string p2) => $$"""
        time 4/4
        part p1 { clef treble }
        part p2 { clef treble }
        section A {
          p1 { {{p1}} }
          p2 { {{p2}} }
        }
        form { A }
        score {
          staff p1
          staff p2
        }
        """;

    [Fact]
    public void TheOwnersBook_ItsLastBar_SoundsWithTheOtherPart()
    {
        var src = Book("c'4 d e f | g2 g | c1", "c'4 d e  | g2 g c,8 | e1");
        var p1 = Notes(src, "p1");
        var p2 = Notes(src, "p2");
        Assert.Equal(2 * Bar, p1[^1].StartTick);
        // The short first bar is filled out (the other part's bar is the full one), and the
        // eighth that runs past the second bar line is not played.
        Assert.Equal(new[] { 0, 480, 960, Bar, Bar + 960, 2 * Bar }, p2.Select(n => n.StartTick));
    }

    [Fact]
    public void AShortBar_IsFilledWithSilence()
    {
        var notes = Notes(Book("c'1 | d1 | e1", "c'1 | d2. | e1"), "p2");
        Assert.Equal(new[] { 0, Bar, 2 * Bar }, notes.Select(n => n.StartTick));
    }

    [Fact]
    public void ANoteSoundingAcrossTheBarLine_StopsThere()
    {
        var notes = Notes(Book("c'1 | d1", "c'2. d2 | e1"), "p2");
        Assert.Equal(new[] { 0, 1440, Bar }, notes.Select(n => n.StartTick));
        Assert.True(notes[1].StartTick + notes[1].DurationTicks <= Bar);
    }

    [Fact]
    public void APickupEveryPartWrites_IsLeftAPickup()
    {
        var notes = Notes(Book("g4 | c'1", "g4 | c'1"), "p2");
        Assert.Equal(new[] { 0, 480 }, notes.Select(n => n.StartTick));
    }

    [Fact]
    public void ADeclaredPickup_IsTheFirstBarsLength()
    {
        var src = """
            time 4/4
            section A { partial 4
              p1 { g4 | c'1 | d1 }
              p2 { g4 a8 | c'1 | d1 }
            }
            form { A }
            """;
        var p2 = Notes(src, "p2");
        Assert.Equal(new[] { 0, 480, 2400 }, p2.Select(n => n.StartTick));
    }

    [Fact]
    public void AMissingBarLine_KeepsEveryNote_AndPadsToTwoBars()
    {
        // The owner's 銀河鉄道999: `… cis, break fis,,8 …` with no `|` before the break —
        // 15/8 is two bars with a bar line missing, not one bar to cut seven notes from.
        var notes = Notes(Book("c'1 | d | e | f",
            "c'1 | d8 d d d d d d e e e e e e e e | f1"), "p2");
        Assert.Equal(17, notes.Count);
        Assert.Equal(3 * Bar, notes[^1].StartTick);
    }

    [Fact]
    public void AMultiBarRest_IsNotCutToOneBar()
    {
        var notes = Notes(Book("c'1 | d | e | f", "c'1 | R1*2 | f1"), "p2");
        Assert.Equal(new[] { 0, 3 * Bar }, notes.Select(n => n.StartTick));
    }

    [Fact]
    public void AShortLastBar_TheNextSectionCompletes_IsLeftShort()
    {
        // The owner's Disco Inferno: section A closes on half a bar, bar line written, and
        // A_1 opens with the other half — one bar of the music, not a short one.
        var src = """
            time 4/4
            part bass { clef bass }
            section A { bass { c1 | c4 a, | break } }
            section B { bass { bes,4 c | f1 } }
            form { A B }
            score { staff bass }
            """;
        var notes = Notes(src, "bass");
        Assert.Equal(new[] { 0, Bar, Bar + 480, Bar + 960, Bar + 1440, 2 * Bar }, notes.Select(n => n.StartTick));
    }

    [Fact]
    public void AnEmptyChord_TakesNoTime()
    {
        // `<>` carries marks and occupies no time (Semantics.MeasureDurations); the MIDI gave
        // it a beat, and the bar line then cut the bar's last note.
        var notes = Notes(Book("c'4 d e f | g1", "c'4 <>@sfz d e f | g1"), "p2");
        Assert.Equal(new[] { 0, 480, 960, 1440, Bar }, notes.Select(n => n.StartTick));
    }

    [Fact]
    public void ALaterVoiceOfASpan_ResyncsWithoutTouchingTheFirst()
    {
        var notes = Notes(Book("c'1 | d1 | e1 | f1", "c'1 | voice { c''1 | d1 } { e'2. | f1 } | g1"), "p2");
        // The upper voice is whole; the lower voice's short bar is filled out.
        Assert.Equal(new[] { 0, Bar, Bar, 2 * Bar, 2 * Bar, 3 * Bar }, notes.Select(n => n.StartTick));
        Assert.All(notes, n => Assert.True(n.DurationTicks > 0));
    }
}
