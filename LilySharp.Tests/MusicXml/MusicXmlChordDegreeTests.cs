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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// MusicXML parity for scale-degree chords: the exported pitches match the
/// rendered/sounded ones — degrees stacked on the root by diatonic steps in the
/// key, spelled letter-preserving.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MusicXmlChordDegreeTests
{
    private static (string step, int octave)[] Pitches(string source)
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
        return xml.Descendants("pitch")
            .Select(p => (p.Element("step")!.Value, int.Parse(p.Element("octave")!.Value)))
            .ToArray();
    }

    [Fact]
    public void Degrees_ExportStackedOnTheRoot()
    {
        // <d 3 5 7,> in C major → D4 F4 A4 C4 (the 7th an octave down).
        Assert.Equal(
            new[] { ("D", 4), ("F", 4), ("A", 4), ("C", 4) },
            Pitches("key c major\n<d 3 5 7,>2"));
    }

    [Fact]
    public void GluedSharp_ExportsWithTheAccidental()
    {
        // <d 3is 5 7> → D F♯ A C: the raised 3rd is spelled F with alter +1.
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse("key c major\n<d 3is 5 7>1")).ToXml();
        var third = xml.Descendants("pitch").ElementAt(1); // root, THEN degrees
        Assert.Equal("F", third.Element("step")!.Value);
        Assert.Equal("1", third.Element("alter")!.Value);
    }
}
