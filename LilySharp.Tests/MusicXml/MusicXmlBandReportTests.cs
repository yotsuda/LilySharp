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
/// The smaller items of the band-score report (LilySharp-Omr
/// docs/repro/musicxml-exporter-bugs.md): what the file says where the page draws nothing —
/// a key on a drum staff (#9), a metronome the source never stated (#7), a clef restated at a
/// section that changes none (#12) — and the C a written 4/4 is drawn as (#8).
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlBandReportTests
{
    private static XDocument Export(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    private const string Band =
        "octave absolute\ntime 4/4\nkey g major\n"
        + "part vo { clef treble }\npart ba { clef bass }\npart kit { clef percussion }\n"
        + "section A {\n vo { b'1 | } ba { e,1 | } kit { bd4 sn bd sn | }\n}\n"
        + "section B {\n vo { a'1 | } ba { d,1 | } kit { bd4 sn bd sn | }\n}\n"
        + "form main { A B }\nscore main { staff vo\n staff ba\n staff kit }";

    private static XElement Part(XDocument doc, int index) => doc.Descendants("part").ElementAt(index);

    [Fact]
    public void ADrumPart_CarriesNoKey()
    {
        var doc = Export(Band);
        Assert.Empty(Part(doc, 2).Descendants("key"));
        Assert.Equal("1", Part(doc, 0).Descendants("key").Single().Element("fifths")!.Value);
    }

    /// <summary>Measured on the 2026-09-29 build (the report's #12): section B's first bar
    /// restated the vocal and bass clefs. Fixed by the per-part written-attributes record
    /// (40dd2e93); kept here so it stays fixed.</summary>
    [Fact]
    public void ASectionThatChangesNoClef_RestatesNone()
    {
        var doc = Export(Band);
        foreach (var part in doc.Descendants("part"))
            Assert.Single(part.Descendants("clef"));
    }

    [Fact]
    public void NoStatedTempo_SoundsTheDefault_AndPrintsNoMetronome()
    {
        var doc = Export(Band);
        Assert.Empty(doc.Descendants("metronome"));
        Assert.All(doc.Descendants("part"), p =>
            Assert.Equal("120", p.Element("measure")!.Element("sound")!.Attribute("tempo")!.Value));
    }

    [Fact]
    public void AStatedTempo_IsItsMetronome()
    {
        var doc = Export("tempo 96\n" + Band);
        Assert.Equal(3, doc.Descendants("metronome").Count());
        Assert.All(doc.Descendants("per-minute"), m => Assert.Equal("96", m.Value));
    }

    [Theory]
    [InlineData("4/4", "common")]
    [InlineData("2/2", "cut")]
    [InlineData("3/4", null)]
    [InlineData("4/2", null)]
    public void ATimeSignature_CarriesTheSymbolThePageDraws(string time, string? symbol)
    {
        var doc = Export(Band.Replace("time 4/4", "time " + time).Replace("bd4 sn bd sn", "bd1"));
        Assert.Equal(symbol, (string?)doc.Descendants("time").First().Attribute("symbol"));
    }
}
