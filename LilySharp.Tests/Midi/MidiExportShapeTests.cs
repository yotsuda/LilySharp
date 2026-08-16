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

namespace LilySharp.Tests.Midi;

/// <summary>
/// Shape assertions on exported MIDI notes — the guard for the positional-
/// argument class of regressions (a new MidiNote record slot once silently
/// swallowed the drum path's SourceOrdinal).
/// </summary>
public class MidiExportShapeTests
{
    private static List<MidiNote> ExportNotes(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var file = new MidiExporter().Export(tree);
        return file.Tracks.SelectMany(t => t.Notes).ToList();
    }

    [Fact]
    public void DrumNotes_CarryChannel10AndOrdinals()
    {
        var notes = ExportNotes("""
            part kit { clef percussion }
            section A { kit { bd4 bd sn bd | } }
            form main { A }
            score main { staff kit }
            """);
        Assert.Equal(4, notes.Count);
        Assert.All(notes, n => Assert.Equal(9, n.Channel));
        Assert.Equal(new[] { 36, 36, 38, 36 }, notes.Select(n => n.Pitch));
        // Ordinals count printed copies per source position — every note here
        // is engraved once, so all ordinals are 0 (the swallowed-argument bug
        // put NextOrdinal's result into QuarterBend instead).
        Assert.All(notes, n => Assert.Equal(0, n.SourceOrdinal));
        Assert.All(notes, n => Assert.Equal(0, n.QuarterBend));
        Assert.All(notes, n => Assert.Equal(9, n.Timbre));
    }

    [Fact]
    public void PartMajor_MultipleParts_WithChordsDeclaredLast_PlaysEveryPart()
    {
        // Part-major layout declares `section A` once per part. A structure
        // reference must play EVERY part concurrently — even when a chords part
        // is declared last. Previously the last-declared section won the name, so
        // earlier parts were silently dropped, and a trailing chords part left
        // the whole score with zero playable notes.
        var notes = ExportNotes("""
            octave absolute
            part melody { clef treble section A { c'4 d' e' f' | } }
            part bass { clef bass section A { c4 d e f | } }
            chords harmony { section A { c1 | } }
            form main { A }
            score main { staff melody with chords harmony
            staff bass }
            """);

        // Both parts sound: melody c'=C5 (72), bass c=C4 (60) — the chords part
        // itself emits no MIDI notes.
        Assert.Contains(notes, n => n.Pitch == 72); // melody — silently dropped before the fix
        Assert.Contains(notes, n => n.Pitch == 60); // bass
        // Concurrent: both parts' first note starts at the same tick.
        Assert.Equal(
            notes.Where(n => n.Pitch == 72).Min(n => n.StartTick),
            notes.Where(n => n.Pitch == 60).Min(n => n.StartTick));
    }

    [Fact]
    public void QuarterTones_CarryQuarterBend()
    {
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { cih'4 c' deh' d' | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(4, notes.Count);
        Assert.Equal(new[] { 1, 0, -1, 0 }, notes.Select(n => n.QuarterBend));
        // The written pitch stays integral; the bend carries the 50 cents.
        Assert.Equal(new[] { 72, 72, 74, 74 }, notes.Select(n => n.Pitch));
    }

    [Fact]
    public void DrumChord_PlaysEveryMemberOnChannel10()
    {
        var notes = ExportNotes("""
            part kit { clef percussion }
            section A { kit { <bd hh>4 r r r | } }
            form main { A }
            score main { staff kit }
            """);
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.Equal(9, n.Channel));
        Assert.Equal(new[] { 36, 42 }, notes.Select(n => n.Pitch).OrderBy(p => p));
        Assert.Equal(notes[0].StartTick, notes[1].StartTick);
    }

    // A phrase reference's trailing marks move the frame its body sounds in. Relative
    // mode moves the running frame; absolute mode has none, so the shift lands on the
    // absolute anchor (_partAbsoluteBase here, OctaveBase in the collector). Until
    // 2026-08-16 this walker did the relative half only, and in silence — as did the
    // page and the MusicXML; only the LilyPond twin warned.

    private static string PhraseBook(string reference, bool absolute) => $$"""
        {{(absolute ? "octave absolute" : "")}}
        part m { clef treble }
        phrase Lick { c4 d e f }
        section A { m { {{reference}} | } }
        form main { A }
        score main { staff m }
        """;

    [Theory]
    [InlineData("Lick", new[] { 60, 62, 64, 65 })]
    [InlineData("Lick'", new[] { 72, 74, 76, 77 })]
    [InlineData("Lick,", new[] { 48, 50, 52, 53 })]
    public void AnAbsoluteReferencesMarks_MoveWhatSounds(string reference, int[] pitches)
    {
        Assert.Equal(pitches, ExportNotes(PhraseBook(reference, absolute: true)).Select(n => n.Pitch));
    }

    [Theory]
    [InlineData("Lick")]
    [InlineData("Lick'")]
    [InlineData("Lick,")]
    public void TheTwoOctaveModes_PlayTheSameReference(string reference)
    {
        Assert.Equal(
            ExportNotes(PhraseBook(reference, absolute: false)).Select(n => n.Pitch),
            ExportNotes(PhraseBook(reference, absolute: true)).Select(n => n.Pitch));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReferencesMarkMovesWhatSounds_InBothModes(bool absolute)
    {
        // Positive control for the pair above, which on its own is satisfied by two modes
        // that both ignore the mark.
        Assert.NotEqual(
            ExportNotes(PhraseBook("Lick", absolute)).Select(n => n.Pitch),
            ExportNotes(PhraseBook("Lick'", absolute)).Select(n => n.Pitch));
        Assert.NotEqual(
            ExportNotes(PhraseBook("Lick", absolute)).Select(n => n.Pitch),
            ExportNotes(PhraseBook("Lick,", absolute)).Select(n => n.Pitch));
    }
}
