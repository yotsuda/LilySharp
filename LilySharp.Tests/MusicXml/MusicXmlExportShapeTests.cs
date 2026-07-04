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
/// Shape assertions on the exported MusicXML — the "roundtrip lite" guard for
/// the exporter gaps found in the grammar audit: multi-voice measures must
/// MERGE (voice numbers + backup), repeats must unfold to their played
/// length, drum notes serialize unpitched, and lyric elisions split inside
/// one lyric element.
/// </summary>
public class MusicXmlExportShapeTests
{
    private static XDocument Export(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    [Fact]
    public void MultiVoice_MergesMeasuresWithBackup()
    {
        var doc = Export("""
            part pno { clef treble }
            section A {
              pno {
                voice { c'4 d' e' f' | g'1 | }
                voice { c4 c c c | c1 | }
              }
            }
            structure { A }
            score x { staff pno }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(2, measures.Count);               // NOT 4 (serialized voices)
        Assert.Equal(2, doc.Descendants("backup").Count());
        // Full-bar rewind: 4/4 at 24 divisions per quarter.
        Assert.All(doc.Descendants("backup"),
            b => Assert.Equal("96", b.Element("duration")!.Value));
        Assert.Equal(5, doc.Descendants("note").Count(n => n.Element("voice")?.Value == "1"));
        Assert.Equal(5, doc.Descendants("note").Count(n => n.Element("voice")?.Value == "2"));
    }

    [Fact]
    public void PercentRepeat_UnfoldsToPlayedLength()
    {
        var doc = Export("""
            part m { clef treble }
            section A { m { repeat percent 2 { c'4 d' e' f' | } } }
            structure { A }
            score x { staff m }
            """);
        Assert.Equal(2, doc.Descendants("measure").Count());
        Assert.Equal(8, doc.Descendants("note").Count());
    }

    [Fact]
    public void DrumNotes_SerializeUnpitchedWithNotehead()
    {
        var doc = Export("""
            part kit { clef percussion }
            section A { kit { bd4 sn hh hh | } }
            structure { A }
            score x { staff kit }
            """);
        Assert.Equal(4, doc.Descendants("unpitched").Count());
        Assert.Equal("percussion", doc.Descendants("clef").First().Element("sign")!.Value);
        // hh carries the cross head; bd/sn have none.
        Assert.Equal(2, doc.Descendants("notehead").Count(n => n.Value == "x"));
    }

    [Fact]
    public void LyricElision_SplitsInsideOneLyric()
    {
        var doc = Export("""
            part v { clef treble }
            section A {
              v { c'4 d' | }
              lyrics { glo ri~a | }
            }
            structure { A }
            score x { staff v }
            """);
        var lyric = doc.Descendants("lyric")
            .First(l => l.Elements("elision").Any());
        Assert.Equal(new[] { "ri", "a" }, lyric.Elements("text").Select(t => t.Value));
    }
}
