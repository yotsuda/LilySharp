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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Guards the export-correctness fixes: MIDI must merge tied same-pitch notes
/// into one sustained note (not re-articulate); MusicXML must emit the matching
/// tie-stop; and MusicXML relative-octave resolution must use the nearest-octave
/// rule so it agrees with MIDI on leaps.
/// </summary>
public sealed class ExportTieAndOctaveTests
{
    // ---- MIDI ties ----

    [Fact]
    public void Midi_TiedSamePitch_MergesIntoOneSustainedNote()
    {
        var tree = SyntaxTree.Parse("c4~ c4");
        var midi = new MidiExporter().Export(tree);

        var notes = midi.Tracks[1].Notes;
        Assert.Single(notes);                                       // not two re-articulated notes
        Assert.Equal(2 * midi.TicksPerQuarterNote, notes[0].DurationTicks); // two quarters sustained
    }

    [Fact]
    public void Midi_TieChain_MergesAll()
    {
        var tree = SyntaxTree.Parse("c4~ c4~ c4");
        var midi = new MidiExporter().Export(tree);

        var notes = midi.Tracks[1].Notes;
        Assert.Single(notes);
        Assert.Equal(3 * midi.TicksPerQuarterNote, notes[0].DurationTicks);
    }

    [Fact]
    public void Midi_TieToDifferentPitch_DoesNotMerge()
    {
        // A tie to a different pitch is not a real tie; we must not collapse it.
        var tree = SyntaxTree.Parse("c4~ d4");
        var midi = new MidiExporter().Export(tree);

        Assert.Equal(2, midi.Tracks[1].Notes.Count);
    }

    // ---- MusicXML ties ----

    [Fact]
    public void MusicXml_Tie_EmitsStartAndStop()
    {
        var tree = SyntaxTree.Parse("c4~ c4");
        var xml = new MusicXmlExporter().Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal(2, notes.Count);
        Assert.True(notes[0].TieStart, "first note should start the tie");
        Assert.True(notes[1].TieStop, "second note should stop the tie");
    }

    // ---- MusicXML relative octave (nearest rule, agrees with MIDI) ----

    [Fact]
    public void MusicXml_RelativeOctave_LeapDownAFourth_MatchesMidi()
    {
        // c then g: nearest g is a fourth BELOW (LilyPond relative rule), so g is
        // octave 3, not 4. Pre-fix MusicXML used additive offset and put g at 4.
        var tree = SyntaxTree.Parse("{ c g }");

        var xml = new MusicXmlExporter().Export(tree);
        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal(4, notes[0].Octave);  // c4
        Assert.Equal(3, notes[1].Octave);  // g3 (down a fourth)

        // MIDI agrees: c = 60 (C4), g = 55 (G3).
        var midi = new MidiExporter().Export(tree);
        var mnotes = midi.Tracks[1].Notes;
        Assert.Equal(60, mnotes[0].Pitch);
        Assert.Equal(55, mnotes[1].Pitch);
    }

    // ---- octave absolute: exporters agree with the renderer ----

    // cis'(C#5) c''(C6) c'(C5) e'(E5) — absolute, stateless (4 + '/, offset).
    private const string AbsoluteSource = @"
octave absolute
part melody { clef treble }
section A { melody { cis'4 c'' c' e' } }
structure { A }
score ""t"" { staff { melody } }
";

    [Fact]
    public void MusicXml_OctaveAbsolute_ResolvesFromFixedC4Anchor()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(AbsoluteSource));
        var octaves = xml.Parts[0].Measures.SelectMany(m => m.Notes).Select(n => n.Octave!.Value).ToArray();
        Assert.Equal(new[] { 5, 6, 5, 5 }, octaves); // C#5, C6, C5, E5
    }

    [Fact]
    public void Midi_OctaveAbsolute_ResolvesFromFixedC4Anchor()
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(AbsoluteSource));
        var pitches = midi.Tracks[1].Notes.OrderBy(n => n.StartTick).Select(n => n.Pitch).ToArray();
        Assert.Equal(new[] { 73, 84, 72, 76 }, pitches); // C#5, C6, C5, E5
    }

    [Fact]
    public void Chord_RelativeOctave_IsReckonedFromPreviousNoteInChord()
    {
        // LilyPond: within <c g e>, g is relative to c (→ G3, down a fourth) and e
        // is relative to g (→ E3, down a third), NOT relative to the first note c
        // (which would give E4). Matches the renderer/collector.
        var midi = new MidiExporter().Export(SyntaxTree.Parse("<c g e>4"));
        var pitches = midi.Tracks[1].Notes.OrderBy(n => n.StartTick).Select(n => n.Pitch).ToList();
        Assert.Equal(new[] { 60, 55, 52 }, pitches); // C4, G3, E3 (e from g, not c)
    }
}
