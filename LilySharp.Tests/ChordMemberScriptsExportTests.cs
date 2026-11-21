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
using System.Xml.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A script written on one member of a chord (<c>&lt;e''@accent g''&gt;4</c>): the page draws it
/// over the chord as it draws the chord's own, MIDI plays it on the whole chord (each kind once),
/// MusicXML writes it on that member's <c>&lt;note&gt;</c>. Until session 1347 neither MIDI nor
/// (for a full-size chord) MusicXML had it (Lab sessions/p1347/grace g3, g6, g7).
/// </summary>
[Trait("Category", "Unit")]
public class ChordMemberScriptsExportTests
{
    private static string Book(string bars)
        => "octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m { " + bars + " } }\nform { ~A }\nscore { staff m }\n";

    /// <summary>Each note as key:velocity:length, by onset then key.</summary>
    private static string Midi(string bars)
        => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(Book(bars))).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => $"{n.Pitch}:{n.Velocity}:{n.DurationTicks}"));

    [Theory]
    // A member's accent accents the chord.
    [InlineData("c''4 <e''@accent g''>4 r2 | c''1 |", "72:90:480 76:110:480 79:110:480 72:90:1920")]
    // …over the chord's own staccato.
    [InlineData("c''4 <e''@accent g''>4@staccato r2 | c''1 |", "72:90:480 76:110:240 79:110:240 72:90:1920")]
    // A member's fermata holds the chord.
    [InlineData("c''4 <e'' g''@fermata>4 r2 | c''1 |", "72:90:480 76:90:720 79:90:720 72:90:1920")]
    // Each kind once: two members' accents accent the chord once.
    [InlineData("c''4 <e''@accent g''@accent>4 r2 | c''1 |", "72:90:480 76:110:480 79:110:480 72:90:1920")]
    // …and a kind the chord writes itself is not played twice.
    [InlineData("c''4 <e''@accent g''>4@accent r2 | c''1 |", "72:90:480 76:110:480 79:110:480 72:90:1920")]
    // A grace chord's member: the grace's own 27 ticks, halved by a staccato (13).
    [InlineData("c''2 grace { <e''@accent g''>16 } d''2 | c''1 |", "72:90:960 76:110:27 79:110:27 74:90:933 72:90:1920")]
    [InlineData("c''2 grace { <e''@staccato g''>16 } d''2 | c''1 |", "72:90:960 76:90:13 79:90:13 74:90:933 72:90:1920")]
    public void InMidi_AMembersScriptsSoundOnTheChord(string bars, string expected)
        => Assert.Equal(expected, Midi(bars));

    /// <summary>The same as the chord's own script, written there.</summary>
    [Theory]
    [InlineData("c''4 <e''@staccato g'' b''>4 r2 | c''1 |", "c''4 <e'' g'' b''>4@staccato r2 | c''1 |")]
    [InlineData("c''4 <c'' e''@staccato g''@marcato>4 r2 | c''1 |", "c''4 <c'' e'' g''>4@staccato@marcato r2 | c''1 |")]
    public void InMidi_AsTheChordsOwn(string onMember, string onChord)
        => Assert.Equal(Midi(onChord), Midi(onMember));

    /// <summary>Each note's <c>&lt;notations&gt;</c> content, in document order.</summary>
    private static string Xml(string bars)
        => string.Join(" ", XDocument.Parse(new MusicXmlExporter().Export(SyntaxTree.Parse(Book(bars))).ToXml().ToString())
            .Descendants("note").Select(n => "note" + (n.Element("notations") is { } x
                ? "[" + string.Join(",", x.Elements().Select(e => e.Name.LocalName
                    + (e.HasElements ? "(" + string.Join(",", e.Elements().Select(y => y.Name.LocalName)) + ")" : ""))) + "]"
                : "")));

    [Theory]
    [InlineData("c''4 <e''@accent g''>4 r2 | c''1 |", "note note[articulations(accent)] note note note")]
    // The member's and the chord's in one <articulations> on the host.
    [InlineData("c''4 <e''@accent g''>4@staccato r2 | c''1 |", "note note[articulations(accent,staccato)] note note note")]
    [InlineData("c''4 <e'' g''@fermata>4 r2 | c''1 |", "note note note[fermata] note note")]
    [InlineData("c''4 <c'' e''@staccato g''@marcato>4 r2 | c''1 |", "note note note[articulations(staccato)] note[articulations(strong-accent)] note note")]
    // An empty chord's scripts stay the host's, once.
    [InlineData("c''4 <>@tenuto <e'' g''@accent>4 r2 | c''1 |", "note note[articulations(tenuto)] note[articulations(accent)] note note")]
    public void InMusicXml_TheMembersScriptsAreOnItsNote(string bars, string expected)
        => Assert.Equal(expected, Xml(bars));
}
