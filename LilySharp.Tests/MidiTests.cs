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

[Trait("Category", "Integration")]
public class MidiTests
{
    [Fact]
    public void ExportSimpleNote()
    {
        var source = "c4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        Assert.Equal(2, midi.Tracks.Count); // conductor + main
        Assert.Single(midi.Tracks[1].Notes);
        Assert.Equal(60, midi.Tracks[1].Notes[0].Pitch); // C4 = MIDI 60
    }

    [Fact]
    public void ExportRelativeMode()
    {
        var source = "{ c d e f g a b c' }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        Assert.Equal(8, midi.Tracks[1].Notes.Count);
        // Default starts at C4 = MIDI 60
        Assert.Equal(60, midi.Tracks[1].Notes[0].Pitch); // c = C4
        Assert.Equal(62, midi.Tracks[1].Notes[1].Pitch); // d = D4
        Assert.Equal(64, midi.Tracks[1].Notes[2].Pitch); // e = E4
    }

    [Fact]
    public void ExportParallelVoices_AllVoicesSoundSimultaneously()
    {
        // << v1 \\ v2 >> written as `voice { } voice { }`. Every voice must sound
        // (regression: only voices[0] was exported), each starting at the block's
        // tick rather than appended after the previous voice.
        var source = "voice { c4 d4 } voice { e4 f4 }";
        var tree = SyntaxTree.Parse(source);
        var midi = new MidiExporter().Export(tree);
        var notes = midi.Tracks[1].Notes;

        Assert.Equal(4, notes.Count); // both voices, not just voice 1's two notes
        // Voice 2 restarts the relative-octave frame from before the block.
        Assert.Equal(new[] { 60, 62, 64, 65 }, notes.Select(n => n.Pitch).OrderBy(p => p).ToArray());
        // The two voices are simultaneous: beats 1 and 2 each carry two notes.
        Assert.Equal(new[] { 0, 0, 480, 480 }, notes.Select(n => n.StartTick).OrderBy(t => t).ToArray());
    }

    [Fact]
    public void ExportPhraseReference_OctaveMarkShiftsPitch()
    {
        // A phrase is a movable motif: intro' plays an octave above where intro
        // itself sounds, intro, an octave below. The reference's trailing marks
        // shift the fresh frame — same rule the SVG collector applies.
        var up = new MidiExporter().Export(SyntaxTree.Parse("phrase intro { c4 }\n{ intro' }"));
        var plain = new MidiExporter().Export(SyntaxTree.Parse("phrase intro { c4 }\n{ intro }"));
        var down = new MidiExporter().Export(SyntaxTree.Parse("phrase intro { c4 }\n{ intro, }"));

        Assert.Equal(60, plain.Tracks[1].Notes.Single().Pitch); // C4
        Assert.Equal(72, up.Tracks[1].Notes.Single().Pitch);    // C5
        Assert.Equal(48, down.Tracks[1].Notes.Single().Pitch);  // C3
    }

    [Fact]
    public void PhraseReference_AutoTransposesToAmbientKey()
    {
        // Parity with the SVG collector: a phrase written in the home key sounds
        // in the ambient key where referenced, by the nearest octave. Section B
        // modulates to G, so Lick (in C) sounds down a fourth — G3 A3 B3 G3 =
        // 55 57 59 55. Section A (home C) is untouched.
        static int[] Pitches(string src) =>
            new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes
                .Select(n => n.Pitch).ToArray();

        var pitches = Pitches("""
            key c major
            phrase Lick { c d e c }
            part m {
              section A { Lick }
              section B { key g major Lick }
            }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[] { 60, 62, 64, 60, /* G3 A3 B3 G3 */ 55, 57, 59, 55 }, pitches);
    }

    [Fact]
    public void SectionMajorKey_AppliesToTheSectionsPartBlocks()
    {
        // A section-major section states its own key beside its part blocks —
        //   section B { key g major  melody { … } }
        // — and it must apply to that block's music. Here the phrase auto-transposes to
        // the section key (C major section A stays put; G major section B drops to G).
        static int[] Pitches(string src) =>
            new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes
                .Select(n => n.Pitch).ToArray();

        var pitches = Pitches("""
            key c major
            phrase Lick { c d e c }
            section A { melody { Lick } }
            section B {
              key g major
              melody { Lick }
            }
            form main { A B }
            score main { staff melody }
            """);
        Assert.Equal(new[] { 60, 62, 64, 60, /* G3 A3 B3 G3 */ 55, 57, 59, 55 }, pitches);
    }

    [Fact]
    public void StandalonePartMajorSectionHeaderKey_AppliesToTheSection()
    {
        // A part-major layout can state a section's key once in a standalone header
        // parallel to the parts — `section A { key g major }`. It must apply to every
        // part playing A (here the phrase auto-transposes to G).
        static int[] Pitches(string src) =>
            new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes
                .Select(n => n.Pitch).ToArray();

        var pitches = Pitches("""
            key c major
            phrase Lick { c d e c }
            part melody { section A { Lick } }
            section A { key g major }
            form main { A }
            score main { staff melody }
            """);
        Assert.Equal(new[] { 55, 57, 59, 55 }, pitches);
    }

    [Fact]
    public void StandalonePartMajorSectionHeaderTempo_ChangesTheConductorTempo()
    {
        // A standalone part-major header can state a section's tempo parallel to the
        // parts — `section B { tempo 140 }`. On a NON-first section it must stamp a
        // conductor tempo change at that section's start tick (not at tick 0), so
        // playback speeds up there — matching the metronome mark the SVG prints.
        var midi = new MidiExporter().Export(SyntaxTree.Parse("""
            tempo 100
            part melody { section A { c4 d e f } section B { g4 a g f } }
            section B { tempo 140 }
            form main { A B }
            score main { staff melody }
            """));
        var conductor = midi.Tracks[0];
        // 140 BPM = 428571 μs/beat, applied where section B begins (after A's 4/4 bar).
        Assert.Contains(conductor.TempoChanges, tc => tc.MicrosecondsPerBeat == 428571 && tc.Tick > 0);
    }

    [Fact]
    public void StandalonePartMajorSectionHeaderTime_ChangesTheConductorMeter()
    {
        // Likewise a standalone header's meter — `section B { time 3/4 }` — must stamp a
        // conductor time-signature change at the section start, not only on the staff.
        var midi = new MidiExporter().Export(SyntaxTree.Parse("""
            time 4/4
            part melody { section A { c4 d e f } section B { g4 a g } }
            section B { time 3/4 }
            form main { A B }
            score main { staff melody }
            """));
        var conductor = midi.Tracks[0];
        Assert.Contains(conductor.TimeSignatures, ts => ts.Numerator == 3 && ts.Denominator == 4 && ts.Tick > 0);
    }

    [Fact]
    public void PhraseReference_InlineNotesAfterAreNotTransposed()
    {
        // The inline c after the phrase stays at written pitch (C4 = 60),
        // relative to the phrase's last written note — not shifted to the key.
        static int[] Pitches(string src) =>
            new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes
                .Select(n => n.Pitch).ToArray();

        var pitches = Pitches("""
            key c major
            phrase Lick { c d e c }
            part m {
              section A { key g major Lick c }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(new[] { 55, 57, 59, 55, /* inline c = C4 */ 60 }, pitches);
    }

    [Fact]
    public void ChordDegrees_SoundStackedOnTheRootInKey()
    {
        // <d 3 5 7,> = D4 + its 3rd/5th/7th in the key, the 7th an octave down:
        // C4 D4 F4 A4 → 62 65 69 60 (order = root then degrees).
        static int[] Pitches(string src) =>
            new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes
                .Select(n => n.Pitch).ToArray();

        Assert.Equal(new[] { 62, 65, 69, 60 }, Pitches("key c major\n<d 3 5 7,>2"));
    }

    [Fact]
    public void ChordDegrees_FollowTheKey()
    {
        // The same <d 3 5 7> sounds Dm7 in C (F natural) but D7 in G (F♯).
        static int[] Pitches(string src) =>
            new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes
                .Select(n => n.Pitch).ToArray();

        Assert.Equal(new[] { 62, 65, 69, 72 }, Pitches("key c major\n<d 3 5 7>1"));
        Assert.Equal(new[] { 62, 66, 69, 72 }, Pitches("key g major\n<d 3 5 7>1"));
    }

    [Fact]
    public void ChordDegrees_OmittedRoot_SoundTheTonicTriad()
    {
        // <1 3 5> with no root = the key's tonic triad: C E G in C major.
        var pitches = new MidiExporter().Export(SyntaxTree.Parse("key c major\n<1 3 5>2"))
            .Tracks[1].Notes.Select(n => n.Pitch).ToArray();
        Assert.Equal(new[] { 60, 64, 67 }, pitches);
    }

    [Fact]
    public void ExportWithDuration()
    {
        var source = "c4 c2 c1";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        Assert.Equal(3, midi.Tracks[1].Notes.Count);
        // Quarter = 480 ticks, Half = 960, Whole = 1920
        Assert.Equal(480, midi.Tracks[1].Notes[0].DurationTicks);
        Assert.Equal(960, midi.Tracks[1].Notes[1].DurationTicks);
        Assert.Equal(1920, midi.Tracks[1].Notes[2].DurationTicks);
    }

    [Fact]
    public void ExportWithRest()
    {
        var source = "c4 r4 d4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        Assert.Equal(2, midi.Tracks[1].Notes.Count);
        // First note at tick 0, second at tick 960 (after rest)
        Assert.Equal(0, midi.Tracks[1].Notes[0].StartTick);
        Assert.Equal(960, midi.Tracks[1].Notes[1].StartTick);
    }

    [Fact]
    public void ExportChord()
    {
        var source = "< c e g >4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        Assert.Equal(3, midi.Tracks[1].Notes.Count);
        // All start at same tick
        Assert.True(midi.Tracks[1].Notes.All(n => n.StartTick == 0));
    }

    [Fact]
    public void ExportAccidentals()
    {
        var source = "c cis des";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        Assert.Equal(3, midi.Tracks[1].Notes.Count);
        Assert.Equal(60, midi.Tracks[1].Notes[0].Pitch);  // C
        Assert.Equal(61, midi.Tracks[1].Notes[1].Pitch);  // C#
        Assert.Equal(61, midi.Tracks[1].Notes[2].Pitch);  // Db
    }

    [Fact]
    public void WriteValidMidiFile()
    {
        var source = "relative c' { c4 d e f | g a b c' }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        using var stream = new MemoryStream();
        midi.WriteTo(stream);

        // Check MIDI header
        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 14);
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'T', bytes[1]);
        Assert.Equal((byte)'h', bytes[2]);
        Assert.Equal((byte)'d', bytes[3]);
    }

    [Fact]
    public void ExportTupletTriplet()
    {
        // Triplet: 3 notes in the time of 2 quarter notes
        var source = "tuplet 3/2 { c4 d4 e4 }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Equal(3, notes.Count);

        // Each quarter note in triplet should be 2/3 of normal duration
        // Normal quarter = 480 ticks, triplet quarter = 480 * 2/3 = 320 ticks
        Assert.Equal(320, notes[0].DurationTicks);
        Assert.Equal(320, notes[1].DurationTicks);
        Assert.Equal(320, notes[2].DurationTicks);

        // Total duration should equal 2 quarter notes = 960 ticks
        Assert.Equal(0, notes[0].StartTick);
        Assert.Equal(320, notes[1].StartTick);
        Assert.Equal(640, notes[2].StartTick);
    }

    [Fact]
    public void ExportGraceNotes()
    {
        var source = "grace { c8 d } e4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var notes = midi.Tracks.Skip(1).First().Notes;

        // 2 grace notes + 1 main note = 3 notes total
        Assert.Equal(3, notes.Count);

        // Grace sounding time is 9/40 of the WRITTEN duration (LILYPOND-REF:
        // ly/articulate.ly ac:defaultGraceFactor = 9/40), not a fixed 1/32. The
        // c8 is an eighth (240 ticks at 480 PPQ); d threads the eighth, so both
        // graces sound round(9/40 * 240) = 54 ticks.
        Assert.Equal(54, notes[0].DurationTicks);
        Assert.Equal(54, notes[1].DurationTicks);
    }

    [Fact]
    public void ExportGraceNotes_StealTimeFromFollowingNote_KeepsMetricGrid()
    {
        // Grace notes steal their time from the FOLLOWING note (LilyPond's MIDI
        // convention). Each grace sounds 9/40 of its written eighth = round(9/40 *
        // 240) = 54 ticks (108 total for the two); e4 gives up those 108 ticks, so
        // the note AFTER the grace+note pair (f4) still lands on the downbeat one
        // quarter (480 ticks) later — the graces do NOT push the piece late.
        var source = "grace { c8 d } e4 f4";
        var tree = SyntaxTree.Parse(source);
        var midi = new MidiExporter().Export(tree);

        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Equal(4, notes.Count); // 2 graces + e + f

        // Graces at ticks 0 and 54, e4 at 108.
        Assert.Equal(0, notes[0].StartTick);
        Assert.Equal(54, notes[1].StartTick);
        Assert.Equal(108, notes[2].StartTick);
        // e4 (nominal 480) gives up 108 ticks -> sounds 372.
        Assert.Equal(372, notes[2].DurationTicks);
        // f4 stays on the grid at tick 480, not pushed late.
        Assert.Equal(480, notes[3].StartTick);
        Assert.Equal(480, notes[3].DurationTicks);
    }

    [Fact]
    public void ExportWithDynamics()
    {
        var source = @"c4@p d4@f e4@ff";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Equal(3, notes.Count);

        // p = 50, f = 95, ff = 110
        Assert.Equal(50, notes[0].Velocity);
        Assert.Equal(95, notes[1].Velocity);
        Assert.Equal(110, notes[2].Velocity);
    }

    [Fact]
    public void ExportWithStaccato()
    {
        var source = @"c4@staccato";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Single(notes);

        // Staccato = 50% duration, quarter note = 480 ticks, so 240 ticks
        Assert.Equal(240, notes[0].DurationTicks);
    }

    [Fact]
    public void ExportWithAccent()
    {
        var source = @"c4@accent";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var notes = midi.Tracks.Skip(1).First().Notes;
        Assert.Single(notes);

        // Accent adds 20 to velocity (default 80 -> 100)
        Assert.Equal(100, notes[0].Velocity);
    }

    [Fact]
    public void DynamicsParsing()
    {
        var source = @"c4@p d4@f";
        var tree = SyntaxTree.Parse(source);

        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));

        var notes = tree.GetNodes<NoteSyntax>().ToList();
        Assert.Equal(2, notes.Count);

        var articulations = notes[0].Articulations.ToList();
        Assert.Single(articulations);
        Assert.IsType<DynamicSyntax>(articulations[0]);
    }

    [Fact(Skip = "Phase 8: Lyrics MIDI export not yet implemented")]
    public void ExportWithLyrics()
    {
        var source = @"
{ c4 d e f }
lyrics { Hap -- py birth -- day }
";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var track = midi.Tracks.Skip(1).First();
        Assert.Equal(4, track.Notes.Count);

        // Lyrics should be added (may not sync perfectly with notes in this simple implementation)
        Assert.True(track.Lyrics.Count > 0);
    }

    [Fact]
    public void ExportTimeSignature()
    {
        var source = "time 3/4 c4 d e";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var conductorTrack = midi.Tracks[0];
        Assert.Contains(conductorTrack.TimeSignatures, ts => ts.Numerator == 3 && ts.Denominator == 4);
    }

    [Fact]
    public void ExportTempoDeclaration()
    {
        var source = "tempo 140 c4 d e";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);

        var conductorTrack = midi.Tracks[0];
        // 140 BPM = 428571 microseconds per beat
        Assert.Contains(conductorTrack.TempoChanges, tc => tc.MicrosecondsPerBeat == 428571);
    }

    [Fact]
    public void ExportTransposedPart_ShiftsSoundingPitchUp()
    {
        var source = @"
part clar { clef treble transpose d }
section Main { clar { c4 d e } }
form main { Main }
score main ""x"" { staff clar }";
        var tree = SyntaxTree.Parse(source);
        var midi = new MidiExporter().Export(tree);
        var notes = midi.Tracks[1].Notes;

        Assert.Equal(3, notes.Count);
        // transpose: d shifts every sounding pitch up a major 2nd (+2 semitones).
        Assert.Equal(62, notes[0].Pitch); // c (60) -> d
        Assert.Equal(64, notes[1].Pitch); // d (62) -> e
        Assert.Equal(66, notes[2].Pitch); // e (64) -> fis
    }

    [Fact]
    public void ExportTransposedPart_OctaveMarkGoesDown()
    {
        var source = @"
part lower { clef bass transpose c, }
section Main { lower { c4 d e } }
form main { Main }
score main ""x"" { staff lower }";
        var tree = SyntaxTree.Parse(source);
        var midi = new MidiExporter().Export(tree);
        var notes = midi.Tracks[1].Notes;

        Assert.Equal(3, notes.Count);
        // transpose: c, drops every sounding pitch one octave (-12 semitones).
        Assert.Equal(48, notes[0].Pitch); // c (60) -> c one octave down
        Assert.Equal(50, notes[1].Pitch); // d (62) -> d
        Assert.Equal(52, notes[2].Pitch); // e (64) -> e
    }

    [Fact]
    public void ExportMidPieceTempoChange()
    {
        var source = @"
tempo 120
time 4/4
part m { clef treble }
section Main { m { c4 d e f | tempo 160 g a b c } }
form main { Main }
score main ""x"" { staff m }";
        var tree = SyntaxTree.Parse(source);
        var midi = new MidiExporter().Export(tree);
        var tempos = midi.Tracks[0].TempoChanges; // conductor track

        Assert.Contains(tempos, t => t.Tick == 0 && t.MicrosecondsPerBeat == 500000);    // 120 BPM
        Assert.Contains(tempos, t => t.Tick == 1920 && t.MicrosecondsPerBeat == 375000); // 160 BPM at bar 2
    }
}
