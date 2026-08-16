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
/// MusicXML parity for phrase auto-transpose: a movable phrase written in the
/// score's home key is respelled into the ambient key where it is referenced,
/// by the nearest octave — matching the SVG collector and MIDI exporter.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MusicXmlPhraseAutoTransposeTests
{
    private static (string step, int octave)[] Pitches(string source)
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        return xml.Descendants("pitch")
            .Select(p => (p.Element("step")!.Value, int.Parse(p.Element("octave")!.Value)))
            .ToArray();
    }

    [Fact]
    public void PhraseReference_AutoTransposesToAmbientKey()
    {
        // Section B modulates to G; Lick (written in C) is respelled down a fourth
        // (nearest octave) to G3 A3 B3 G3. Section A (home) stays C4 D4 E4 C4.
        // (The key sits inside the m { } block so the voice walk sees it.)
        var pitches = Pitches("""
            key c major
            phrase Lick { c d e c }
            section A { m { Lick } }
            section B { m { key g major Lick } }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[]
        {
            ("C", 4), ("D", 4), ("E", 4), ("C", 4),
            ("G", 3), ("A", 3), ("B", 3), ("G", 3),
        }, pitches);
    }

    [Fact]
    public void ReferenceInHomeKey_IsNotTransposed()
    {
        // Ambient equals home → exact no-op, phrase exported as written.
        var pitches = Pitches("""
            key c major
            phrase Lick { c d e c }
            section A { m { Lick } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(new[] { ("C", 4), ("D", 4), ("E", 4), ("C", 4) }, pitches);
    }

    // A reference's TRAILING MARKS are the other half of "movable": they move the frame
    // the body is read in. Relative mode moves the running frame, absolute mode the
    // anchor (_octaveAnchor here, OctaveBase in the collector). Until 2026-08-16 this
    // walker did the relative half only, silently — as did the collector and the MIDI
    // exporter; only the LilyPond twin said anything.

    private static string Book(string reference, bool absolute) => $$"""
        {{(absolute ? "octave absolute" : "")}}
        key c major
        phrase Lick { c d e c }
        section A { m { {{reference}} } }
        form main { A }
        score main { staff m }
        """;

    [Theory]
    [InlineData("Lick", 4)]
    [InlineData("Lick'", 5)]
    [InlineData("Lick,", 3)]
    public void AnAbsoluteReferencesMarks_MoveTheExportedOctave(string reference, int octave)
    {
        var pitches = Pitches(Book(reference, absolute: true));
        Assert.NotEmpty(pitches);
        Assert.All(pitches, p => Assert.Equal(octave, p.octave));
    }

    [Theory]
    [InlineData("Lick")]
    [InlineData("Lick'")]
    [InlineData("Lick,")]
    public void TheTwoOctaveModes_ExportTheSameReference(string reference)
    {
        Assert.Equal(Pitches(Book(reference, absolute: false)), Pitches(Book(reference, absolute: true)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReferencesMarkMovesTheExport_InBothModes(bool absolute)
    {
        // Positive control for the pair above: two modes that both ignored the mark would
        // agree with each other, which is all that test can see on its own.
        Assert.NotEqual(Pitches(Book("Lick", absolute)), Pitches(Book("Lick'", absolute)));
        Assert.NotEqual(Pitches(Book("Lick", absolute)), Pitches(Book("Lick,", absolute)));
    }
}
