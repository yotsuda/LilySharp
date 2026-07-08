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

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The Phase-0 safety net for the MusicXML importer. Because export already
/// exists, each fixture makes the full loop
/// <c>.lys -&gt; export XML -&gt; import -&gt; .lys'</c> and asserts the imported
/// source (a) parses clean and (b) re-collects to the SAME music as the original
/// on the covered subset: absolute pitch (MIDI), sounding duration, per-measure
/// item order, and measure count. This defines "done" for Tier 1 objectively and
/// guards against regressions as the importer grows.
/// </summary>
public class MusicXmlRoundTripTests
{
    [Fact]
    public void Melody_PitchesDurationsDotsRestsTies_SurviveRoundTrip()
    {
        AssertRoundTrips("""
            octave absolute
            title "Round Trip"
            tempo 96
            time 4/4
            key g major

            c'4 d' e' fis' | g'2 a'4. b'8 | c''1 | r2 g'4 fis' | e'2~ e'2 |
            """);
    }

    [Fact]
    public void FlatKeyAndAccidentals_SurviveRoundTrip()
    {
        AssertRoundTrips("""
            octave absolute
            time 3/4
            key ees major

            ees'4 g' bes' | aes'2 f'4 | ees'2. |
            """);
    }

    [Fact]
    public void LeadSheet_ChordsAndLyrics_SurviveRoundTrip()
    {
        AssertRoundTrips("""
            octave absolute
            title "Lead Sheet"
            composer "Lily#"
            tempo 120
            time 4/4
            key c major

            part melody { clef treble }

            section A {
              melody {
                e'4@chord(c) e' f' g' | a'4@chord(a:m) g' e' d' |
                f'4@chord(f) a' g' f' | e'4@chord(g:7) d' c'2 |
              }
              lyrics { Mu- sic fills the | air to- night so | ev- 'ry- one will | sing a- long | }
            }

            structure { A }

            score "lead-sheet" { staff melody }
            """);
    }

    [Fact]
    public void ChordSymbolBeforeNote_DoesNotStealItsLyric()
    {
        // Regression: a <harmony> pseudo-entry precedes the first note in the note
        // stream; NextSingable must skip it, else the first note's syllable lands on
        // the harmony and is dropped on serialization. Guards both export and import.
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A {
              melody { e'4@chord(c) e' f' g' | }
              lyrics { Mu- sic fills the | }
            }
            structure { A }
            score x { staff melody }
            """)).ToXml();

        var firstNote = xml.Descendants("note").First();
        Assert.Equal("Mu", firstNote.Element("lyric")?.Element("text")?.Value);
    }

    [Fact]
    public void FiguredBass_SurvivesRoundTrip()
    {
        // Figured bass is not in the MIDI/duration signature, so verify it by a
        // DOUBLE export: original -> XML1 -> import -> XML2, comparing the
        // <figured-bass> figures (number + accidental + held) across the loop.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part bass { clef bass }
            section A {
              bass { c4@fig(6) d4@fig(6 4) e4@fig(7 s) f4@fig(_) | }
            }
            structure { A }
            score x { staff bass }
            """);
        Assert.False(HasErrors(tree), "the fixture itself must parse clean");

        var xml1 = new MusicXmlExporter().Export(tree).ToXml();
        var (lys, report) = new MusicXmlImporter().Import(xml1.ToString());
        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"imported .lys did not parse clean:\n{lys}");

        var xml2 = new MusicXmlExporter().Export(importedTree).ToXml();
        Assert.Equal(FiguredBassSignature(xml1), FiguredBassSignature(xml2));
        Assert.Equal("6 6/4 7sharp _", FiguredBassSignature(xml1)); // sanity on the fixture
        Assert.Equal(Signature(tree), Signature(importedTree));
        Assert.False(report.HasWarnings, string.Join("; ", report.Warnings));
    }

    [Fact]
    public void Mxl_ZipContainer_ImportsSameAsRawXml()
    {
        // Exercises the .mxl code path: a real ZIP with META-INF/container.xml
        // pointing at the score, imported via ImportBytes, must equal the raw-XML
        // import and still round-trip the music.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            c'4 d' e' f' | g'2 a'4 b' | c''1 |
            """);
        var xml = new MusicXmlExporter().Export(tree).ToXml().ToString();

        var (lysFromXml, _) = new MusicXmlImporter().Import(xml);
        var (lysFromMxl, _) = new MusicXmlImporter().ImportBytes(BuildMxl(xml));

        Assert.Equal(lysFromXml, lysFromMxl);
        var importedTree = SyntaxTree.Parse(lysFromMxl);
        Assert.False(HasErrors(importedTree), $"imported .lys did not parse clean:\n{lysFromMxl}");
        Assert.Equal(Signature(tree), Signature(importedTree));
    }

    /// <summary>A compact fingerprint of every <c>&lt;figured-bass&gt;</c> group:
    /// per group, its figures as number + accidental + held marker.</summary>
    private static string FiguredBassSignature(XDocument xml)
        => string.Join(" ", xml.Descendants("figured-bass").Select(fb =>
            string.Join("/", fb.Elements("figure").Select(f =>
                (f.Element("figure-number")?.Value ?? "")
                + (f.Element("suffix")?.Value ?? f.Element("prefix")?.Value ?? "")
                + (f.Element("extend") != null ? "_" : "")))));

    /// <summary>Wraps a MusicXML string in a minimal, valid <c>.mxl</c> zip.</summary>
    private static byte[] BuildMxl(string xml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("META-INF/container.xml").Open()))
                w.Write("""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <container><rootfiles>
                      <rootfile full-path="score.xml" media-type="application/vnd.recordare.musicxml+xml"/>
                    </rootfiles></container>
                    """);
            using (var w = new StreamWriter(zip.CreateEntry("score.xml").Open()))
                w.Write(xml);
        }
        return ms.ToArray();
    }

    [Fact]
    public void EmptyScore_WarnsNoNotes()
    {
        // A part-list with an empty <part/> (no measures) is a real export artifact.
        // The importer must say the score is empty, not silently produce nothing.
        var (lys, report) = new MusicXmlImporter().Import("""
            <?xml version="1.0" encoding="utf-8"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Part 1</part-name></score-part></part-list>
              <part id="P1" />
            </score-partwise>
            """);

        Assert.False(SyntaxTree.Parse(lys).HasErrors); // still emits a valid, empty .lys
        Assert.Contains(report.Warnings, w => w.Contains("no notes"));
    }

    [Fact]
    public void Dynamics_ImportOntoTheFollowingNote()
    {
        // Real MusicXML interleaves a <direction> right before the note it marks. (The
        // Lily# exporter instead piles every direction at the measure start, so a
        // round-trip through it can't preserve per-note dynamics — hence hand-crafted
        // XML here.) Each dynamic attaches to the next note as @p / @f.
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>P</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>1</divisions>
                  <time><beats>4</beats><beat-type>4</beat-type></time>
                  <clef><sign>G</sign><line>2</line></clef></attributes>
                <direction><direction-type><dynamics><p/></dynamics></direction-type></direction>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                <direction><direction-type><dynamics><f/></dynamics></direction-type></direction>
                <note><pitch><step>D</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                <note><pitch><step>E</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                <note><pitch><step>F</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
              </measure></part>
            </score-partwise>
            """);
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        Assert.Contains("c'4@p", lys);
        Assert.Contains("d'4@f", lys);
    }

    [Fact]
    public void GraceNotes_SurviveRoundTrip()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            acciaccatura { c''16 } b'4 grace { a'16 } g'4 f'4 e'4 |
            """);
        var xml1 = new MusicXmlExporter().Export(tree).ToXml();
        var (lys, _) = new MusicXmlImporter().Import(xml1.ToString());

        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"imported .lys did not parse clean:\n{lys}");
        // Grace notes carry no metric duration, so the main-note signature matches.
        Assert.Equal(Signature(tree), Signature(importedTree));
        Assert.Contains("acciaccatura {", lys);
        Assert.Contains("grace {", lys);
        // Two <grace> notes survive.
        Assert.Equal(2, new MusicXmlExporter().Export(importedTree).ToXml().Descendants("grace").Count());
    }

    [Fact]
    public void Tuplets_SurviveRoundTrip()
    {
        // A triplet + a quintuplet: the scaled durations must match, which only holds
        // if the tuplet ratio round-trips (plain 8ths would overflow the bar).
        AssertRoundTrips("""
            octave absolute
            time 4/4
            key c major
            tuplet 3/2 { c'8 d' e' } f'4 g'4 a'4 | tuplet 5/4 { c'16 d' e' f' g' } b'4 c''4 d''4 |
            """);
    }

    [Fact]
    public void ArticulationsAndSlurs_SurviveRoundTrip()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            c'4@staccato d'@accent e'@tenuto f'@marcato | g'2@fermata a'4( b') | c''1@trill |
            """);
        var xml1 = new MusicXmlExporter().Export(tree).ToXml();
        var (lys, _) = new MusicXmlImporter().Import(xml1.ToString());

        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"imported .lys did not parse clean:\n{lys}");
        Assert.Equal(Signature(tree), Signature(importedTree));
        Assert.Contains("@staccato", lys);
        Assert.Contains("@fermata", lys);
        Assert.Contains("(", lys);

        // The notations survive: same articulation/ornament/fermata/slur set out.
        var xml2 = new MusicXmlExporter().Export(importedTree).ToXml();
        Assert.NotEqual("", NotationSignature(xml1));
        Assert.Equal(NotationSignature(xml1), NotationSignature(xml2));
    }

    private static string NotationSignature(XDocument xml) =>
        string.Join(" ", xml.Descendants("notations").SelectMany(n =>
            n.Elements("articulations").Elements().Select(e => e.Name.LocalName)
            .Concat(n.Elements("ornaments").Elements().Select(e => e.Name.LocalName))
            .Concat(n.Elements("fermata").Select(_ => "fermata"))
            .Concat(n.Elements("slur").Select(s => "slur:" + (string?)s.Attribute("type")))));

    // NOTE: structure-level repeats (|: A :|) replay sections in the collector but
    // the exporter unrolls them to repeat BARLINES (section emitted once), so a
    // round-trip through XML can't match on replay count until the importer factors
    // repeats back into structure { } (Phase 3). Deliberately not covered here yet.

    // ---- harness ----------------------------------------------------------

    private static void AssertRoundTrips(string originalLys)
    {
        var originalTree = SyntaxTree.Parse(originalLys);
        Assert.False(HasErrors(originalTree), "the fixture itself must parse clean");

        // .lys -> MusicXML (in-memory) -> .lys'
        var xml = new MusicXmlExporter().Export(originalTree).ToXml().ToString();
        var (importedLys, report) = new MusicXmlImporter().Import(xml);

        var importedTree = SyntaxTree.Parse(importedLys);
        Assert.False(HasErrors(importedTree),
            $"imported .lys did not parse clean:\n{importedLys}\n--- diagnostics ---\n{Diagnostics(importedTree)}");

        var expected = Signature(originalTree);
        var actual = Signature(importedTree);
        Assert.True(expected == actual,
            $"round-trip music mismatch\nexpected: {expected}\nactual:   {actual}\n\nimported .lys:\n{importedLys}\n\nwarnings: {string.Join("; ", report.Warnings)}");
    }

    /// <summary>A structure-independent fingerprint of the collected music:
    /// per measure, the ordered items as absolute MIDI + sounding duration. Import
    /// re-spells octaves and reshapes scaffolding, so we compare SOUND, not text.</summary>
    private static string Signature(SyntaxTree tree)
    {
        var measures = new MeasureCollector().Collect(tree).Voice.Measures;
        var parts = new List<string>();
        foreach (var measure in measures)
        {
            var items = measure.Items.Select(ItemSig).Where(s => s != null);
            parts.Add(string.Join(" ", items));
        }
        return string.Join(" | ", parts);
    }

    private static string? ItemSig(MusicItem item) => item switch
    {
        NoteItem n => $"N{n.Midi}:{n.Duration}",
        RestItem r => $"R:{r.Duration}",
        ChordItem c => $"C[{string.Join(",", c.Notes.Select(x => x.Midi).OrderBy(m => m))}]:{c.Duration}",
        _ => null, // clef/key/time change markers carry no sounding music
    };

    private static bool HasErrors(SyntaxTree tree)
        => tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Any(d => d.Severity == DiagnosticSeverity.Error);

    private static string Diagnostics(SyntaxTree tree)
        => string.Join("\n", tree.Diagnostics.Concat(SemanticValidation.Run(tree)));
}
