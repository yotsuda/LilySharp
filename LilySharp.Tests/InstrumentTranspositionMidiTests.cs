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

    // --- The chromatic transposers: written pitch in, transposed pitch heard ---

    /// <summary>
    /// A part written the way its player reads it SOUNDS where the instrument really is.
    /// The page is untouched — this is the sounding side only; writing at concert pitch and
    /// having the part transposed for you is the other convention and is not implemented
    /// (InstrumentDefaults.ConcertPitchIsNotImplemented).
    /// </summary>
    [Theory]
    [InlineData("flute", 72)]        // control: sounds as written
    [InlineData("oboe", 72)]         // control
    [InlineData("trumpet-c", 72)]    // control: the C trumpet does not transpose
    [InlineData("clarinet", 70)]     // in B♭, a major 2nd down
    [InlineData("trumpet", 70)]      // in B♭
    [InlineData("clarinet-a", 69)]   // in A, a minor 3rd down
    [InlineData("horn", 65)]         // in F, a perfect 5th down
    [InlineData("french-horn", 65)]
    [InlineData("soprano-sax", 70)]  // in B♭
    [InlineData("alto-sax", 63)]     // in E♭, a major 6th down
    [InlineData("tenor-sax", 58)]    // in B♭, a major 9th down
    [InlineData("baritone-sax", 51)] // in E♭, an octave and a major 6th down
    public void AChromaticTransposer_SoundsWhereTheInstrumentIs(string preset, int expected)
        // ⚠️ `octave absolute` on purpose: a preset also moves the RELATIVE anchor (a flute
        // part anchors at 5), and that would mix a second variable into a test about the
        // sounding shift. Absolute pins the written note at C5 = 72 for every row, so the
        // only thing that can move the answer is the transposition being measured.
        => Assert.Equal(expected, FirstPitch(
            $"octave absolute\npart x {{ instrument {preset} section A {{ c'1 | }} }}"));

    /// <summary>
    /// ⚠️ Every saxophone is named after a VOICE range, and the MIDI timbre families are
    /// substring tests read in order — so 'alto-sax' matched "alto" and played as a choir
    /// until the reed test moved above the voice test. The control is the voice itself,
    /// which must still be a voice.
    /// </summary>
    [Theory]
    [InlineData("alto-sax", 2)]
    [InlineData("tenor-sax", 2)]
    [InlineData("soprano-sax", 2)]
    [InlineData("baritone-sax", 2)]
    [InlineData("voice-alto", 8)]     // the control
    [InlineData("voice-tenor", 8)]    // the control
    public void ASaxophoneIsAReed_NotAVoice(string preset, int expectedTimbre)
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\nkey c major\n"
            + $"part x {{ instrument {preset} section A {{ c'1 | }} }}"
            + "\nform main { A }\nscore main { staff x }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var midi = new MidiExporter().Export(tree);
        Assert.Equal(expectedTimbre, midi.Tracks[1].Notes[0].Timbre);
    }

    [Fact]
    public void Bass_SoundsOctaveBelowWrittenBassClef()
    {
        // `instrument bass` = bass clef, initial octave 3, sounding 8vb. A bare `c`
        // prints as written C3 but sounds C2 (36) — the note a real bass produces on
        // the A string, 3rd fret. Without the shift MIDI played C3 (48).
        Assert.Equal(36, FirstPitch("part x { instrument bass section A { c1 | } }"));
    }

    /// <summary>
    /// A part's bare letters play at the octave they PRINT, which for a non-treble clef is
    /// not 4 — and the clef is the step the MIDI exporter used to be missing.
    /// </summary>
    /// <remarks>
    /// The page's chain is <c>octave N</c> &gt; instrument preset &gt; the clef's own octave
    /// (InstrumentDefaults.AnchorOctave). MEASURED against the page rather than assumed: the
    /// SVG for <c>part m { clef bass }</c> draws the four notes at staff positions −1, 0, +1,
    /// +2 with no ledger line, i.e. C3 D3 E3 F3 — while MIDI played C4.
    /// <para>
    /// ⚠️ The last row is the CONTROL and it is the one that matters: <c>octave absolute</c>
    /// anchors at middle C whatever the clef ("the clef default is deliberately NOT used
    /// here" — OctaveContext), so giving the relative seed its clef step must NOT move it.
    /// One field served both anchors and this row is what caught it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("part x { clef bass section A { c1 | } }", 48)]         // prints C3, plays C3
    [InlineData("part x { clef treble section A { c1 | } }", 60)]
    [InlineData("part x { clef alto section A { c1 | } }", 48)]
    [InlineData("part x { clef bass octave 4 section A { c1 | } }", 60)] // explicit wins
    [InlineData("octave absolute part x { clef bass section A { c1 | } }", 60)] // ← control
    public void PartOctaveAnchor_FollowsTheClefInRelativeModeOnly(string source, int expected)
        => Assert.Equal(expected, FirstPitch(source));

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
        // The explicit `transposition` property is the same knob the preset sets.
        // ⚠️ The written pitch here is C3, not C4: a `clef bass` part anchors its bare
        // letters at octave 3, which is what the page prints. This assertion used to read
        // 48 and said "written C4 (default octave)" — it was pinning the MIDI exporter's
        // missing clef step, not the transposition. Written C3 (48) sounds C2 (36).
        Assert.Equal(36, FirstPitch(
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
