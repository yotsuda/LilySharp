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
    public void StructureOrder_EmitsSectionsInStructureOrderWithReplay()
    {
        // The structure gives the played ORDER; a replayed section reappears.
        // (Previously the export dumped sections in declaration order, so 'A B A'
        // collapsed to just A, B.)
        var doc = Export("""
            octave absolute
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            section B { m { g'4 a' b' c'' | } }
            structure { A B A }
            score x { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(3, measures.Count);   // A, B, A — not 2
        string First(XElement m) => m.Descendants("note").First(n => n.Element("pitch") != null)
            .Element("pitch")!.Element("step")!.Value;
        Assert.Equal(new[] { "C", "G", "C" }, measures.Select(First));
    }

    [Fact]
    public void StructureRepeat_BracketsSpanWithForwardAndBackwardBarlines()
    {
        var doc = Export("""
            octave absolute
            part m { clef treble }
            section A { m { c'4 d' e' f' | } }
            section B { m { g'4 a' b' c'' | d''4 c'' b' a' | } }
            structure { A |: B :| }
            score x { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(3, measures.Count);   // A(1) + B(2)
        bool HasRepeat(XElement m, string loc, string dir) =>
            m.Elements("barline").Any(bl => bl.Attribute("location")?.Value == loc
                && bl.Element("repeat")?.Attribute("direction")?.Value == dir);
        Assert.True(HasRepeat(measures[1], "left", "forward"));    // B opens the repeat
        Assert.True(HasRepeat(measures[2], "right", "backward"));  // B closes it
        Assert.False(HasRepeat(measures[0], "left", "forward"));   // A is outside the repeat
    }

    [Fact]
    public void FiguredBass_ExportsFigureNumberWithAccidental()
    {
        var doc = Export("""
            octave absolute
            part m { clef treble }
            section A { m { c'4@fig(#6) d' e' f' | } }
            structure { A }
            score x { staff m }
            """);
        var figs = doc.Descendants("figured-bass").ToList();
        Assert.Single(figs);
        var figure = figs[0].Element("figure")!;
        Assert.Equal("6", figure.Element("figure-number")!.Value);
        Assert.Equal("sharp", figure.Element("suffix")!.Value);
    }

    [Fact]
    public void Tuplet_EmitsTupletBracketNotations()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { tuplet 3/2 { c'8 d' e' } r4 c'2 | } }
            structure { A }
            score x { staff m }
            """);
        var tuplets = doc.Descendants("tuplet").ToList();
        Assert.Equal(2, tuplets.Count);
        Assert.Equal("start", tuplets[0].Attribute("type")!.Value);
        Assert.Equal("stop", tuplets[1].Attribute("type")!.Value);
        // The bracket lives in <notations>; the duration math <time-modification> stays.
        Assert.All(tuplets, t => Assert.Equal("notations", t.Parent!.Name.LocalName));
        Assert.Equal(3, doc.Descendants("time-modification").Count());
    }

    [Fact]
    public void LeadingRepeatBar_DoesNotEmitEmptyMeasure()
    {
        var doc = Export("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { |: c'4 d' e' f' | g' a' b' c'' :| } }
            structure { A }
            score x { staff m }
            """);
        var measures = doc.Descendants("measure").ToList();
        Assert.Equal(2, measures.Count);   // NOT 3 — no spurious empty leading measure
        Assert.True(measures[0].Elements("barline").Any(b =>
            b.Element("repeat")?.Attribute("direction")?.Value == "forward"));
        Assert.Equal(4, measures[0].Elements("note").Count());   // the notes land in measure 1
    }

    [Fact]
    public void PercentRepeat_ExportsMeasureRepeatSign()
    {
        // A one-measure percent body exports the SIGN: repeated measures keep
        // their REAL notes under measure-style measure-repeat (hidden behind
        // the % by importers, full bars for strict ones).
        var doc = Export("""
            part m { clef treble }
            section A { m { repeat percent 2 { c'4 d' e' f' | } } }
            structure { A }
            score x { staff m }
            """);
        Assert.Equal(2, doc.Descendants("measure").Count());
        Assert.Equal(8, doc.Descendants("note").Count());
        Assert.Single(doc.Descendants("measure-repeat")
            .Where(m => (string?)m.Attribute("type") == "start"));
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
    public void DirectionFamily_ExportsWedgePedalOttavaRepeatHarmony()
    {
        var doc = Export("""
            octave absolute
            part pno { clef treble }
            section A { pno {
              |: c'4@ped@chord(Dm7) d'@cresc e' f'@ped(off) | g'1@f :|
              a'4@ottava b' a'@loco g' | c'1@chord(G7/B) |
            } }
            structure { A }
            score x { staff pno }
            """);
        Assert.Single(doc.Descendants("repeat").Where(r => (string?)r.Attribute("direction") == "forward"));
        Assert.Single(doc.Descendants("repeat").Where(r => (string?)r.Attribute("direction") == "backward"));
        Assert.Single(doc.Descendants("wedge").Where(w => (string?)w.Attribute("type") == "crescendo"));
        Assert.Single(doc.Descendants("wedge").Where(w => (string?)w.Attribute("type") == "stop"));
        Assert.Single(doc.Descendants("pedal").Where(p => (string?)p.Attribute("type") == "start"));
        Assert.Single(doc.Descendants("pedal").Where(p => (string?)p.Attribute("type") == "stop"));
        Assert.Single(doc.Descendants("octave-shift").Where(o => (string?)o.Attribute("type") == "down"));
        Assert.Single(doc.Descendants("octave-shift").Where(o => (string?)o.Attribute("type") == "stop"));
        var kinds = doc.Descendants("harmony").Select(h => h.Element("kind")!.Value).ToList();
        Assert.Equal(new[] { "minor-seventh", "dominant" }, kinds);
        Assert.Equal("B", doc.Descendants("bass-step").Single().Value);
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
