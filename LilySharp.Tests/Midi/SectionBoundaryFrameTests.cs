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
/// What a section boundary does to the two running defaults a bare letter reads — the
/// relative octave frame and the note value.
/// </summary>
/// <remarks>
/// <para>
/// The corpus states the rule: `test/section-octave-reset` is titled "Verifies that octave
/// resets to default at section boundaries", and "default" is the PART's own anchor, not a
/// fixed middle C — a bass part reopens at octave 3. The page does this, and so does the
/// MusicXML; until 2026-08-17 the MIDI walk carried the previous section's last note
/// instead, under a comment claiming it matched the collector. Measured on a bass part,
/// where the two rules are visibly different: page C3, MIDI C4.
/// </para>
/// <para>
/// ⚠️ THE NOTE VALUE IS THE SAME LANE and had the same defect: `section A { c2 d }` followed
/// by four bare letters gave four HALF notes in the MIDI and four quarters on the page — a
/// section twice as long as the one the other parts are playing.
/// </para>
/// <para>
/// ⚠️ Each case is paired with the same music in ONE section, where carrying the frame is
/// the right answer. A rule that reset everywhere would pass every case below on its own.
/// </para>
/// </remarks>
public class SectionBoundaryFrameTests
{
    private static List<MidiNote> ExportNotes(string source)
        => new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();

    private static string Book(string clef) => $$"""
        part m { clef {{clef}} }
        section A { m { c4 d e f | g2 g | } }
        section B { m { c4 d e f | } }
        form main { A B }
        score main { staff m }
        """;

    [Theory]
    [InlineData("treble", 60)]  // anchor octave 4
    [InlineData("bass", 48)]    // anchor octave 3 — the book that tells the two rules apart
    public void ASectionOpensAtThePartsOwnAnchor_NotWhereTheLastOneEnded(string clef, int expected)
    {
        var notes = ExportNotes(Book(clef));
        Assert.Equal(6 + 4, notes.Count);
        // Section A ends on g, a fifth above the anchor's c, so a carried frame would put
        // section B's bare c an octave UP (the nearest c to that g).
        Assert.Equal(expected, notes[6].Pitch);
    }

    [Fact]
    public void WithinOneSection_TheFrameIsStillCarried()
    {
        // The control. The same notes with the boundary removed: the c after g2 IS the
        // nearest c above it, and must stay there.
        var notes = ExportNotes("""
            part m { clef treble }
            section A { m { c4 d e f | g2 g | c4 d e f | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(72, notes[6].Pitch);
    }

    [Fact]
    public void ASectionOpensAtTheDefaultNoteValue_NotTheLastOneWritten()
    {
        // `c2 d` leaves a half note running. Section B's four bare letters are quarters on
        // the page and in the MusicXML; a carried value made the MIDI section twice as long
        // as the bar the other parts are playing.
        var notes = ExportNotes("""
            part m { clef treble }
            section A { m { c2 d | } }
            section B { m { c d e f | } }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(6, notes.Count);
        int half = notes[0].DurationTicks;
        Assert.All(notes.Skip(2), n => Assert.Equal(half / 2, n.DurationTicks));
    }

    [Fact]
    public void WithinOneSection_TheNoteValueIsStillCarried()
    {
        // The control for the pair above.
        var notes = ExportNotes("""
            part m { clef treble }
            section A { m { c2 d | e f | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(4, notes.Count);
        Assert.All(notes, n => Assert.Equal(notes[0].DurationTicks, n.DurationTicks));
    }
}
