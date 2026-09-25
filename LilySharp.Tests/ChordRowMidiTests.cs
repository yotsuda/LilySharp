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

using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>chords { }</c> row the score places sounds in the MIDI (owner decision 2026-09-25):
/// the WINDOW voicing (every tone in G3..F#4, a slash bass an octave below), on the row's
/// own track at 70% velocity, each symbol over exactly the span the page prints it, struck
/// again at every written symbol. A row the score does not place sounds nothing.
/// </summary>
public class ChordRowMidiTests
{
    private const int Bar = 1920; // 4/4 at 480 ticks a quarter

    private static MidiFile Export(string lys) => new MidiExporter().Export(SyntaxTree.Parse(lys));

    private static (int Start, int Length, int Pitch)[] ChordNotes(MidiFile midi, string row = "harmony")
        => midi.Tracks.Where(t => t.Name == row + " (chords)")
            .SelectMany(t => t.Notes)
            .Select(n => (n.StartTick, n.DurationTicks, n.Pitch))
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .ToArray();

    [Fact]
    public void ASectionRow_SoundsTheWindowVoicing_OnThePagesGrid()
    {
        var midi = Export("""
            time 4/4
            part melody
            section A { melody { c'4 d' e' f' | g'1 | } chords harmony { C . G7 . | Dm/F | } }
            form main { A }
            score main { chords harmony  staff melody }
            """);
        Assert.Equal(new[]
        {
            (0, 960, 55), (0, 960, 60), (0, 960, 64),                          // C: G3 C4 E4
            (960, 960, 55), (960, 960, 59), (960, 960, 62), (960, 960, 65),    // G7: G3 B3 D4 F4
            (Bar, Bar, 53), (Bar, Bar, 57), (Bar, Bar, 62), (Bar, Bar, 65),    // Dm/F: F3 | A3 D4 F4
        }, ChordNotes(midi));
        var track = midi.Tracks.Single(t => t.Name == "harmony (chords)");
        Assert.All(track.Notes, n => Assert.Equal(56, n.Velocity));
        Assert.NotEqual(midi.Tracks.Single(t => t.Name == "melody").Channel, track.Channel);
    }

    [Fact]
    public void ARowTheScoreDoesNotPlace_IsSilent()
    {
        var midi = Export("""
            time 4/4
            part melody
            section A { melody { c'4 d' e' f' | } chords harmony { C | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.Empty(ChordNotes(midi));
        Assert.Equal(4, midi.Tracks.Sum(t => t.Notes.Count));
    }

    [Fact]
    public void APartMajorTrack_SoundsAtEverySectionItsFormPlays()
    {
        var midi = Export("""
            time 4/4
            part melody
            section A { melody { c'1 | } }
            section B { melody { g'1 | } }
            chords harmony { section A { C | } section B { G | } }
            form main { A B A }
            score main { chords harmony  staff melody }
            """);
        Assert.Equal(new[]
        {
            (0, Bar, 55), (0, Bar, 60), (0, Bar, 64),                          // C
            (Bar, Bar, 55), (Bar, Bar, 59), (Bar, Bar, 62),                    // G: G3 B3 D4
            (2 * Bar, Bar, 55), (2 * Bar, Bar, 60), (2 * Bar, Bar, 64),        // C again
        }, ChordNotes(midi));
    }

    [Fact]
    public void ARomanEntry_ReadsTheKeyInForce()
    {
        var midi = Export("""
            time 4/4
            key g major
            part melody
            section A { melody { g'1 | d''1 | } chords harmony { I | V | } }
            form main { A }
            score main { chords harmony  staff melody }
            """);
        Assert.Equal(new[]
        {
            (0, Bar, 55), (0, Bar, 59), (0, Bar, 62),                          // G: G3 B3 D4
            (Bar, Bar, 57), (Bar, Bar, 62), (Bar, Bar, 66),                    // D: A3 D4 F#4
        }, ChordNotes(midi));
    }

    [Fact]
    public void AChordsOnlySection_TakesItsBars()
    {
        var midi = Export("""
            time 4/4
            part melody
            section Intro { chords harmony { C | F | } }
            section A { melody { c'1 | } }
            form main { Intro A }
            score main { chords harmony  staff melody }
            """);
        var melody = midi.Tracks.Single(t => t.Name == "melody").Notes.Single();
        Assert.Equal(2 * Bar, melody.StartTick);
        Assert.Equal(new[] { 0, Bar }, ChordNotes(midi).Select(n => n.Start).Distinct().ToArray());
    }
}
