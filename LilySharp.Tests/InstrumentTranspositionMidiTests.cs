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

using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A transposing instrument sounds at its concert pitch in MIDI: the played note is
/// the WRITTEN pitch shifted by the clef octave (treble_8) plus the resolved
/// <c>transposition</c> (an explicit property, an instrument preset, or a tuning
/// default). So the .mid matches what the instrument really produces — and the tab
/// fret, which recovers the same sounding pitch. Regression: MIDI ignored the
/// instrument octave, playing bass an octave above the real instrument.
/// </summary>
[Trait("Category", "Integration")]
public class InstrumentTranspositionMidiTests
{
    private static int FirstPitch(string body)
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\nkey c major\n" + body
            + "\nform main { A }\nscore main { staff x }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var midi = new MidiExporter().Export(tree);
        return midi.Tracks[1].Notes[0].Pitch;
    }

    [Fact]
    public void Bass_SoundsOctaveBelowWrittenBassClef()
    {
        // `instrument bass` = bass clef, initial octave 3, sounding 8vb. A bare `c`
        // prints as written C3 but sounds C2 (36) — the note a real bass produces on
        // the A string, 3rd fret. Without the shift MIDI played C3 (48).
        Assert.Equal(36, FirstPitch("part x { instrument bass section A { c1 | } }"));
    }

    [Fact]
    public void Guitar_SoundsOctaveBelowWrittenViaTreble8Clef()
    {
        // Standard guitar notation is treble_8: written C4 sounds C3 (48).
        Assert.Equal(48, FirstPitch("part x { instrument guitar section A { c1 | } }"));
    }

    [Fact]
    public void Piccolo_SoundsOctaveAboveWritten()
    {
        // The piccolo is written an octave below sounding: written C5 sounds C6 (84).
        Assert.Equal(84, FirstPitch("part x { instrument piccolo section A { c1 | } }"));
    }

    [Fact]
    public void ExplicitTransposition8vb_ShiftsPlaybackDownAnOctave()
    {
        // The explicit `transposition` property is the same knob the preset sets:
        // written C4 (default octave) sounds C3 (48).
        Assert.Equal(48, FirstPitch(
            "part x { clef bass transposition 8vb section A { c1 | } }"));
    }

    [Fact]
    public void ExplicitTransposition8va_ShiftsPlaybackUpAnOctave()
    {
        Assert.Equal(72, FirstPitch(
            "part x { transposition 8va section A { c1 | } }"));
    }

    [Fact]
    public void NonTransposingPart_PlaysWrittenPitch()
    {
        // A plain part (no instrument, clef, or transposition) is untouched: C4 = 60.
        Assert.Equal(60, FirstPitch("part x { section A { c1 | } }"));
    }
}
