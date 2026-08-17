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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// Which members of a tied chord the document says are tied — every start paired with a
/// stop of the SAME notehead, and no member carrying half a pair.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ A TIE IS A PAIR, so every case here counts BOTH ends. The exporter used to mark all
/// members of the arriving chord as stops and all members of the leaving chord as starts,
/// which is right exactly when the two chords hold the same pitches; on
/// `&lt;c f g c&gt;~ &lt;c e g c&gt;` it wrote a stop on the e that nothing started and a
/// start on the f that nothing stopped. Counting only stops would call that correct.
/// </para>
/// <para>
/// The rule is the corpus's own: `test/feature-tour` says 「&lt;c e g&gt;~ &lt;c e g&gt; で
/// マッチするピッチ全てがタイに。一部不一致なら共通分のみ」, and the MIDI walk sustains
/// exactly those members (<c>ChordTieMidiTests</c>). This file is the same rule asked of
/// the other output — the two were measured disagreeing on 2 of 566 books.
/// </para>
/// </remarks>
public class MusicXmlChordTieTests
{
    private static List<XElement> Notes(string body)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part v { }
            section Main { v { {{body}} } }
            form main { ~Main }
            score main { staff ~v }
            """);
        return new MusicXmlExporter().Export(tree).ToXml()
            .Descendants("note").Where(n => n.Element("rest") == null).ToList();
    }

    private static string Pitch(XElement note)
        => note.Element("pitch")!.Element("step")!.Value + note.Element("pitch")!.Element("octave")!.Value;

    private static bool Has(XElement note, string type)
        => note.Elements("tie").Any(t => (string?)t.Attribute("type") == type);

    [Fact]
    public void EqualChords_TieEveryMember_AndTheControlTiesNone()
    {
        var control = Notes("<c' e'>2 <c' e'>2 |");
        Assert.All(control, n => Assert.False(Has(n, "start") || Has(n, "stop")));

        var notes = Notes("<c' e'>2 ~ <c' e'>2 |");
        Assert.Equal(4, notes.Count);
        Assert.Equal(new[] { "C5", "E5", "C5", "E5" }, notes.Select(Pitch));
        Assert.All(notes.Take(2), n => Assert.True(Has(n, "start")));
        Assert.All(notes.Skip(2), n => Assert.True(Has(n, "stop")));
        // Neither end carries the other's mark: the first chord starts and does not stop.
        Assert.All(notes.Take(2), n => Assert.False(Has(n, "stop")));
    }

    [Fact]
    public void PartlyMatchingChords_TieOnlyTheSharedNoteheads_AtBothEnds()
    {
        var notes = Notes("<g b d>2~ <g b e>2 |");
        Assert.Equal(6, notes.Count);
        var first = notes.Take(3).ToList();
        var second = notes.Skip(3).ToList();
        Assert.Equal(new[] { "G4", "B4", "D4" }, first.Select(Pitch));
        Assert.Equal(new[] { "G4", "B4", "E4" }, second.Select(Pitch));

        // g and b are held across.
        Assert.True(Has(first[0], "start"));
        Assert.True(Has(first[1], "start"));
        Assert.True(Has(second[0], "stop"));
        Assert.True(Has(second[1], "stop"));

        // The d has nothing to continue into, so its start is RETRACTED — a start with no
        // stop is the same broken pair as a stop with no start, seen from the other end.
        Assert.False(Has(first[2], "start"));
        // And the e begins a new sound rather than ending a tie it was never part of.
        Assert.False(Has(second[2], "stop"));
    }

    [Fact]
    public void EveryTieMarkIsPaired_AcrossTheWholeBook()
    {
        // The invariant, stated once over a book that mixes single notes, equal chords,
        // partly-matching chords and a `q`: as many starts as stops, per pitch.
        var notes = Notes("""
            c'2 ~ c'2 | <c' e'>2~ <c' e'>2 | <g b d>2~ <g b e>2 | <c' e'>2~ q2 |
            """);
        var starts = notes.Where(n => Has(n, "start")).Select(Pitch).OrderBy(p => p).ToList();
        var stops = notes.Where(n => Has(n, "stop")).Select(Pitch).OrderBy(p => p).ToList();

        Assert.NotEmpty(starts);           // the book must actually reach the branch
        Assert.Equal(starts, stops);
    }
}
