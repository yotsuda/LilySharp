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
        // (The key sits inside the m { } voice block so the voice walk sees it.)
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
}
