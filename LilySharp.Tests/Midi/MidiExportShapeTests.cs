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
            structure { A }
            score x { staff kit }
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
    public void QuarterTones_CarryQuarterBend()
    {
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { cih'4 c' deh' d' | } }
            structure { A }
            score x { staff m }
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
            structure { A }
            score x { staff kit }
            """);
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.Equal(9, n.Channel));
        Assert.Equal(new[] { 36, 42 }, notes.Select(n => n.Pitch).OrderBy(p => p));
        Assert.Equal(notes[0].StartTick, notes[1].StartTick);
    }
}
