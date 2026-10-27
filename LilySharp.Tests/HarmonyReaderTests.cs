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
using LilySharp.Core.Harmony;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chords the parts sound, read beat by beat (owner's decisions 2026-10-26,
/// docs/harmony-analysis-design.md — the reading Add Chord Track is to write from).
/// </summary>
[Trait("Category", "Unit")]
public class HarmonyReaderTests
{
    private const int Q = 480;   // a quarter

    /// <summary>4/4 beats over <paramref name="bars"/> bars, the first of each accented.</summary>
    private static List<(int Tick, bool Accent)> Beats(int bars)
        => [.. Enumerable.Range(0, bars * 4).Select(i => (i * Q, i % 4 == 0))];

    /// <summary>A chord of MIDI pitches held from beat <paramref name="beat"/> for
    /// <paramref name="beats"/> beats.</summary>
    private static IEnumerable<SoundingNote> Held(int beat, int beats, params int[] pitches)
        => pitches.Select(p => new SoundingNote(beat * Q, beats * Q, p));

    private static string[] Symbols(IReadOnlyList<ChordReading> r) => [.. r.Select(c => c.Symbol(0))];

    [Fact]
    public void BlockChords_ReadAsTheirNames_OneABar()
    {
        var notes = Held(0, 4, 48, 52, 55, 60)            // C
            .Concat(Held(4, 4, 53, 57, 60, 65))            // F
            .Concat(Held(8, 4, 43, 59, 62, 65))            // G7 (G B D F)
            .Concat(Held(12, 4, 48, 52, 55, 64)).ToList(); // C
        var r = HarmonyReader.Read(notes, Beats(4), 16 * Q);
        Assert.Equal(["C", "F", "G7", "C"], Symbols(r));
        Assert.Equal([0, 4 * Q, 8 * Q, 12 * Q], r.Select(c => c.StartTick));
    }

    [Fact]
    public void APassingToneStruckInsideABeat_DoesNotChangeTheChord()
    {
        // C major held in the left hand; the melody walks e d c with the d an eighth on the off-beat.
        var notes = Held(0, 4, 48, 55).Concat(
        [
            new SoundingNote(0, Q, 64), new SoundingNote(Q, Q / 2, 64), new SoundingNote(Q + Q / 2, Q / 2, 62),
            new SoundingNote(2 * Q, 2 * Q, 60),
        ]).ToList();
        Assert.Equal(["C"], Symbols(HarmonyReader.Read(notes, Beats(1), 4 * Q)));
    }

    [Fact]
    public void TwoChordsInABar_AreReadAtTheBeatTheyChangeOn()
    {
        var notes = Held(0, 2, 45, 57, 60, 64)             // Am
            .Concat(Held(2, 2, 38, 57, 62, 65)).ToList();   // Dm
        var r = HarmonyReader.Read(notes, Beats(1), 4 * Q);
        Assert.Equal(["Am", "Dm"], Symbols(r));
        Assert.Equal(2 * Q, r[1].StartTick);
    }

    [Fact]
    public void AMelodyToneOverATriad_KeepsTheTriad_NotASeventhThatSwallowsIt()
    {
        // C in the bass and the chord, an A held in the melody: C (the A a melody tone), not Am7/C.
        var notes = Held(0, 4, 48, 52, 55).Concat(Held(0, 4, 69)).ToList();
        Assert.Equal(["C"], Symbols(HarmonyReader.Read(notes, Beats(1), 4 * Q)));
    }

    [Fact]
    public void AWalkingBassUnderOneChord_IsOneChord_NotAChordANote()
    {
        // F held above a bass walking f f a f (lesson 11's bar 3): one F, not F then Dm/A.
        var notes = Held(0, 4, 60, 65, 69)
            .Concat([new SoundingNote(0, Q, 41), new SoundingNote(Q, Q, 41), new SoundingNote(2 * Q, Q, 45), new SoundingNote(3 * Q, Q, 41)])
            .ToList();
        Assert.Equal(["F"], Symbols(HarmonyReader.Read(notes, Beats(1), 4 * Q)));
    }

    [Fact]
    public void ALowestNoteThatIsNotTheRoot_IsTheSlashBass()
    {
        var notes = Held(0, 4, 52, 55, 60, 64).ToList();   // C over E
        Assert.Equal(["C/E"], Symbols(HarmonyReader.Read(notes, Beats(1), 4 * Q)));
    }

    [Fact]
    public void WhereNothingSounds_NoChordIsRead_AndOneLineIsMarkedAlone()
    {
        var notes = Held(0, 4, 48, 52, 55).Concat(Held(8, 1, 67)).Concat(Held(9, 1, 64)).ToList();
        var r = HarmonyReader.Read(notes, Beats(3), 12 * Q);
        Assert.Equal("C", r[0].Symbol(0));
        Assert.Contains(r, c => c.Root < 0);                   // bar 2: silence
        Assert.True(r[^1].Monophonic);                         // bar 3: one note at a time
    }

    [Fact]
    public void FromAScore_ThePartsAreReadAndTheChordsRowLeftOut()
    {
        // The piano's left hand plays C then G; the chords row writes F and F — it is not read.
        const string source = """
            part lh { clef bass }
            chords prog { section A { F | F | } }
            section A { lh { <c' e g>1 | <g, b d>1 | } }
            form { A }
            score {
              chords prog
              staff lh
            }
            """;
        var tree = SyntaxTree.Parse(source);
        var midi = new MidiExporter().Export(tree);
        var beats = MetronomeBeats.Of(midi, midi.TicksPerQuarterNote, out int end);
        var parts = HarmonyReader.Read(HarmonyReader.NotesOf(midi, chordRows: false), beats, end);
        Assert.Equal(["C", "G"], Symbols(parts));
        // The row's own reading (its voicing keeps to a window, so its bass is not the chord's):
        // the root and the quality are what a written chord is compared by.
        var row = Assert.Single(HarmonyReader.Read(HarmonyReader.NotesOf(midi, chordRows: true), beats, end));
        Assert.Equal((5, LilySharp.Core.Music.ChordQuality.Major), (row.Root, row.Quality));
    }
}
