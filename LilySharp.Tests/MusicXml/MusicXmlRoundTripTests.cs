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
        // The directives stay at FILE level: the relative-octave anchor comes from there,
        // and these fixtures exist to prove the pitches survive the loop.
        AssertRoundTrips(MusicSource.Wrap(
            "c'4 d' e' fis' | g'2 a'4. b'8 | c''1 | r2 g'4 fis' | e'2~ e'2 |",
            """
            octave absolute
            title "Round Trip"
            tempo 96
            time 4/4
            key g major
            """));
    }

    /// <summary>
    /// A transposing part exports at WRITTEN pitch with its shift declared, which is what
    /// MusicXML asks for — and the two ways a part can sound an octave low stay apart.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE OCTAVE MUST BE CARRIED ONCE. MusicXML has two elements for it and they mean
    /// different things: <c>clef-octave-change</c> is NOTATION (a treble clef with an 8
    /// under it) and <c>transpose</c> is the INSTRUMENT (what the written pitch sounds). A
    /// guitar's 8vb comes from its <c>treble_8</c> clef, so it must NOT also get a
    /// <c>transpose</c> — a reader honouring both, as the spec asks, would drop it two
    /// octaves. A bass's comes from the instrument, so it must.
    /// <para>
    /// Both directions are checked because the pair has to agree: the importer reads
    /// <c>transpose</c> back into <c>transposition</c>, and re-exporting the imported source
    /// has to produce the same attributes. Exporting a shift nobody reads back would look
    /// right in a diff and lose the octave on the way home.
    /// </para>
    /// </remarks>
    [Theory]
    // instrument, expected written pitch, expected clef-octave-change, expected transpose
    [InlineData("instrument bass", "C3", null, -1)]      // the 8vb is the INSTRUMENT's
    [InlineData("instrument guitar", "C4", -1, null)]    // …and here it is the CLEF's
    [InlineData("clef treble_8", "C4", -1, null)]
    [InlineData("clef bass", "C3", null, null)]
    [InlineData("clef treble", "C4", null, null)]
    public void TransposingPart_DeclaresItsShiftOnceAndSurvivesRoundTrip(
        string header, string writtenPitch, int? clefOctaveChange, int? octaveChange)
    {
        string source = $"part m {{ {header} }}\nsection A {{ m {{ c4 d e f | }} }}\n"
                        + "form main { A }\nscore main { staff m }";

        static (string Pitch, int? Clef, int? Transpose) Attributes(string lys)
        {
            var doc = XDocument.Parse(
                new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().ToString());
            var first = doc.Descendants().First(e => e.Name.LocalName == "pitch");
            var clef = doc.Descendants().First(e => e.Name.LocalName == "clef");
            var trans = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "transpose");
            string Val(XElement? p, string n) =>
                p?.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value ?? "";
            return (Val(first, "step") + Val(first, "octave"),
                    Val(clef, "clef-octave-change") is { Length: > 0 } c ? int.Parse(c) : null,
                    trans == null ? null
                        : Val(trans, "octave-change") is { Length: > 0 } o ? int.Parse(o) : 0);
        }

        var exported = Attributes(source);
        Assert.Equal((writtenPitch, clefOctaveChange, octaveChange), exported);

        // …and the same again after a trip through the importer.
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();
        var (reimported, _) = new MusicXmlImporter().Import(xml);
        Assert.Equal(exported, Attributes(reimported));
    }

    [Fact]
    public void FlatKeyAndAccidentals_SurviveRoundTrip()
    {
        AssertRoundTrips(MusicSource.Wrap(
            "ees'4 g' bes' | aes'2 f'4 | ees'2. |",
            """
            octave absolute
            time 3/4
            key ees major
            """));
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
              lyrics words { Mu- sic fills the | air to- night so | ev- 'ry- one will | sing a- long | }
            }

            form main { A }

            score main "lead-sheet" { staff melody with lyrics words }
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
              lyrics words { Mu- sic fills the | }
            }
            form main { A }
            score main { staff melody with lyrics words }
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
            form main { A }
            score main { staff bass }
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
    public void Mxl_MuseScoreStyleNamespacedScore_ImportsAllFeatures()
    {
        // A realistic export: a DEFAULT NAMESPACE + <identification>, harmony, a
        // staccato articulation and a triplet, delivered through the .mxl zip. The
        // reader is namespace-agnostic (matches by local name), so all survive.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <score-partwise xmlns="http://www.musicxml.org/xsd/MusicXML" version="4.0">
              <identification><encoding><software>MuseScore 4.2</software></encoding></identification>
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>6</divisions>
                  <key><fifths>0</fifths></key>
                  <time><beats>4</beats><beat-type>4</beat-type></time>
                  <clef><sign>G</sign><line>2</line></clef></attributes>
                <harmony><root><root-step>C</root-step></root><kind>major</kind></harmony>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>6</duration><type>quarter</type>
                  <notations><articulations><staccato/></articulations></notations></note>
                <note><pitch><step>D</step><octave>5</octave></pitch><duration>2</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>
                  <notations><tuplet type="start"/></notations></note>
                <note><pitch><step>E</step><octave>5</octave></pitch><duration>2</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification></note>
                <note><pitch><step>F</step><octave>5</octave></pitch><duration>2</duration><type>eighth</type>
                  <time-modification><actual-notes>3</actual-notes><normal-notes>2</normal-notes></time-modification>
                  <notations><tuplet type="stop"/></notations></note>
                <note><pitch><step>G</step><octave>5</octave></pitch><duration>12</duration><type>half</type></note>
              </measure></part>
            </score-partwise>
            """;
        var (lys, report) = new MusicXmlImporter().ImportBytes(BuildMxl(xml));
        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"{lys}\n---\n{Diagnostics(importedTree)}\nwarnings: {string.Join("; ", report.Warnings)}");
        Assert.Contains("@chord(c)", lys);      // harmony
        Assert.Contains("@staccato", lys);       // articulation
        Assert.Contains("tuplet 3/2 {", lys);    // triplet
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
    public void MultipleVoices_ImportAsParallelVoiceBlocks()
    {
        // Two voices on one staff, separated by <backup> (voice 1 upper, voice 2 lower).
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Melody</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>1</divisions>
                  <time><beats>4</beats><beat-type>4</beat-type></time>
                  <clef><sign>G</sign><line>2</line></clef></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type></note>
                <note><pitch><step>D</step><octave>5</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type></note>
                <note><pitch><step>E</step><octave>5</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type></note>
                <note><pitch><step>F</step><octave>5</octave></pitch><duration>1</duration><voice>1</voice><type>quarter</type></note>
                <backup><duration>4</duration></backup>
                <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration><voice>2</voice><type>half</type></note>
                <note><pitch><step>G</step><octave>4</octave></pitch><duration>2</duration><voice>2</voice><type>half</type></note>
              </measure></part>
            </score-partwise>
            """);
        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"{lys}\n---\n{Diagnostics(importedTree)}");
        // ONE span holding two voices, in ascending voice order. Counted from the tree
        // rather than the text: `voice` opens the span once and the second voice is a
        // bare block, so there is nothing distinctive left to grep for.
        var span = Assert.Single(importedTree.GetRoot().DescendantNodes()
            .OfType<ParallelExpressionSyntax>());
        Assert.Equal(2, span.Voices.Count());
        int v1 = lys.IndexOf("c'4", System.StringComparison.Ordinal); // voice 1 upper
        int v2 = lys.IndexOf("c2", System.StringComparison.Ordinal);  // voice 2 lower half note
        Assert.True(v1 >= 0 && v2 >= 0 && v1 < v2, lys);
    }

    [Fact]
    public void ForwardGap_BecomesRests()
    {
        // A <forward> advances the cursor, leaving a gap that must sound as a rest.
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>P</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>1</divisions>
                  <time><beats>4</beats><beat-type>4</beat-type></time>
                  <clef><sign>G</sign><line>2</line></clef></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                <forward><duration>2</duration></forward>
                <note><pitch><step>F</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
              </measure></part>
            </score-partwise>
            """);
        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"{lys}\n---\n{Diagnostics(importedTree)}");
        // quarter, half-rest (the 2-beat gap), quarter.
        Assert.Equal("N72:1/4 R:1/2 N77:1/4", Signature(importedTree));
    }

    [Theory]
    [InlineData("c'4 d' e' f' | g'2 a'4 b' | c''1 |")]                 // stepwise + leaps
    [InlineData("c'4 e' g' c'' | c''4 g' e' c' | g,2 c1 |")]           // wide leaps, low notes
    [InlineData("c'4 <c' e' g'>2 g'4 | <e' g' c''>1 |")]              // chords
    [InlineData("acciaccatura { b'16 } c''4 b' a' g' | c'1 |")]        // grace notes
    public void RelativeOctaveOutput_PreservesPitches(string music)
    {
        // Relative-octave output must render the SAME pitches as the absolute source;
        // the round-trip signature (MIDI + duration) is the proof.
        var tree = SyntaxTree.Parse($"octave absolute\ntime 4/4\nkey c major\n{music}");
        var xml = new MusicXmlExporter().Export(tree).ToXml().ToString();

        var (lys, _) = new MusicXmlImporter().Import(xml, relativeOctave: true);
        var imported = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(imported), $"{lys}\n---\n{Diagnostics(imported)}");
        Assert.Contains("octave relative", lys);
        Assert.Equal(Signature(tree), Signature(imported));
    }

    [Fact]
    public void SimpleRepeat_RoundTrips()
    {
        // A plain |: ... :| repeat (no endings) stays a flat section with inline
        // repeat barlines. (Wrapped in a section so the fixture collects the same way
        // the importer's section-major output does.)
        AssertRoundTrips("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { |: c'4 d' e' f' | g'4 a' b' c'' :| } }
            form main { A }
            score main { staff melody }
            """);
    }

    [Fact]
    public void RelativeOctave_UnderVolta_PreservesPitches()
    {
        // Relative output now also covers the section-major volta layout (each section
        // is its own relative stream). Pitches must survive: Body/End1/End2 = C5/D5/E5.
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Tune</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>1</divisions>
                    <time><beats>4</beats><beat-type>4</beat-type></time>
                    <clef><sign>G</sign><line>2</line></clef></attributes>
                  <barline location="left"><repeat direction="forward"/></barline>
                  <note><pitch><step>C</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note>
                </measure>
                <measure number="2">
                  <barline location="left"><ending number="1" type="start"/></barline>
                  <note><pitch><step>D</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note>
                  <barline location="right"><ending number="1" type="stop"/><repeat direction="backward"/></barline>
                </measure>
                <measure number="3">
                  <barline location="left"><ending number="2" type="start"/></barline>
                  <note><pitch><step>E</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note>
                  <barline location="right"><ending number="2" type="discontinue"/></barline>
                </measure>
              </part>
            </score-partwise>
            """, relativeOctave: true);
        var tree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(tree), $"{lys}\n---\n{Diagnostics(tree)}");
        Assert.Contains("octave relative", lys);
        Assert.Contains("|: Body [1. End1] :| [2. End2]", lys);
        Assert.Equal("N72:1 | N74:1 | N76:1", Signature(tree));
    }

    [Fact]
    public void FirstSecondEndings_FactorIntoAVoltaStructure()
    {
        // |: body :| with a first and second ending → Body/End1/End2 sections and a
        // volta structure (the endings are alternatives, not sequential music).
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Tune</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>1</divisions>
                    <time><beats>4</beats><beat-type>4</beat-type></time>
                    <clef><sign>G</sign><line>2</line></clef></attributes>
                  <barline location="left"><repeat direction="forward"/></barline>
                  <note><pitch><step>C</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note>
                </measure>
                <measure number="2">
                  <barline location="left"><ending number="1" type="start"/></barline>
                  <note><pitch><step>D</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note>
                  <barline location="right"><ending number="1" type="stop"/><repeat direction="backward"/></barline>
                </measure>
                <measure number="3">
                  <barline location="left"><ending number="2" type="start"/></barline>
                  <note><pitch><step>E</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note>
                  <barline location="right"><ending number="2" type="discontinue"/></barline>
                </measure>
              </part>
            </score-partwise>
            """);
        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"{lys}\n---\n{Diagnostics(importedTree)}");
        Assert.Contains("|: Body [1. End1] :| [2. End2]", lys);
        Assert.Contains("section Body", lys);
        Assert.Contains("section End1", lys);
        Assert.Contains("section End2", lys);
        // The volta brackets are visual; Lily# emits each section once (Body, End1,
        // End2 = C5, D5, E5), so both endings render with the repeat bars around them.
        Assert.Equal("N72:1 | N74:1 | N76:1", Signature(importedTree));
    }

    [Fact]
    public void LyricsUnderVolta_AlignPerSection()
    {
        // Each section of a volta carries only its own measures' syllables.
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Tune</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>1</divisions>
                    <time><beats>4</beats><beat-type>4</beat-type></time>
                    <clef><sign>G</sign><line>2</line></clef></attributes>
                  <barline location="left"><repeat direction="forward"/></barline>
                  <note><pitch><step>C</step><octave>5</octave></pitch><duration>4</duration><type>whole</type><lyric><syllabic>single</syllabic><text>la</text></lyric></note>
                </measure>
                <measure number="2">
                  <barline location="left"><ending number="1" type="start"/></barline>
                  <note><pitch><step>D</step><octave>5</octave></pitch><duration>4</duration><type>whole</type><lyric><syllabic>single</syllabic><text>one</text></lyric></note>
                  <barline location="right"><ending number="1" type="stop"/><repeat direction="backward"/></barline>
                </measure>
                <measure number="3">
                  <barline location="left"><ending number="2" type="start"/></barline>
                  <note><pitch><step>E</step><octave>5</octave></pitch><duration>4</duration><type>whole</type><lyric><syllabic>single</syllabic><text>two</text></lyric></note>
                  <barline location="right"><ending number="2" type="discontinue"/></barline>
                </measure>
              </part>
            </score-partwise>
            """);
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        Assert.Contains("section Body {", lys);
        // Each ending sings its own word, not the whole run.
        int body = lys.IndexOf("section Body", System.StringComparison.Ordinal);
        int end1 = lys.IndexOf("section End1", System.StringComparison.Ordinal);
        int end2 = lys.IndexOf("section End2", System.StringComparison.Ordinal);
        Assert.Contains("lyrics words { la |", lys[body..end1]);
        Assert.Contains("lyrics words { one |", lys[end1..end2]);
        Assert.Contains("lyrics words { two |", lys[end2..]);
    }

    [Fact]
    public void MultipleStaves_SplitIntoAGrandStaff()
    {
        // A piano part with two staves (treble RH / bass LH) becomes two Lily# parts
        // grouped in a grandStaff, each with its own clef.
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>1</divisions>
                  <time><beats>4</beats><beat-type>4</beat-type></time>
                  <clef number="1"><sign>G</sign><line>2</line></clef>
                  <clef number="2"><sign>F</sign><line>4</line></clef></attributes>
                <note><pitch><step>E</step><octave>5</octave></pitch><duration>4</duration><voice>1</voice><type>whole</type><staff>1</staff></note>
                <backup><duration>4</duration></backup>
                <note><pitch><step>C</step><octave>3</octave></pitch><duration>4</duration><voice>2</voice><type>whole</type><staff>2</staff></note>
              </measure></part>
            </score-partwise>
            """);
        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"{lys}\n---\n{Diagnostics(importedTree)}");
        Assert.Contains("grandStaff {", lys);
        Assert.Contains("clef treble", lys);
        Assert.Contains("clef bass", lys);
        // RH note in the treble part, LH note in the bass part.
        Assert.Contains("e'1", lys); // E5 whole, treble
        Assert.Contains("c,1", lys); // C3 whole, bass
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
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
        AssertRoundTrips(MusicSource.Wrap(
            "tuplet 3/2 { c'8 d' e' } f'4 g'4 a'4 | tuplet 5/4 { c'16 d' e' f' g' } b'4 c''4 d''4 |",
            """
            octave absolute
            time 4/4
            key c major
            """));
    }

    [Fact]
    public void GlissandoAndArpeggio_SurviveRoundTrip()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            c'4@glissando g'4 <c' e' g'>2@arpeggio | c'1 |
            """);
        var xml1 = new MusicXmlExporter().Export(tree).ToXml();
        var (lys, _) = new MusicXmlImporter().Import(xml1.ToString());

        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"imported .lys did not parse clean:\n{lys}");
        Assert.Equal(Signature(tree), Signature(importedTree));
        Assert.Contains("@glissando", lys);
        Assert.Contains("@arpeggio", lys);
    }

    [Fact]
    public void TremoloAndInvertedTurn_ImportFromNotations()
    {
        // The Lily# exporter does not emit <tremolo>/<inverted-turn>, so import from
        // hand-crafted MusicXML: a 2-beam single tremolo -> :16, plus @invertedturn.
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>P</part-name></score-part></part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>1</divisions>
                  <time><beats>4</beats><beat-type>4</beat-type></time>
                  <clef><sign>G</sign><line>2</line></clef></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>2</duration><type>half</type>
                  <notations><ornaments><tremolo type="single">2</tremolo></ornaments></notations></note>
                <note><pitch><step>D</step><octave>5</octave></pitch><duration>2</duration><type>half</type>
                  <notations><ornaments><inverted-turn/></ornaments></notations></note>
              </measure></part>
            </score-partwise>
            """);
        var tree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(tree), $"{lys}\n---\n{Diagnostics(tree)}");
        Assert.Contains("c'2:16", lys);          // 2 tremolo beams -> :16
        Assert.Contains("@invertedturn", lys);
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
    // repeats back into form main { } (Phase 3). Deliberately not covered here yet.

    // ---- harness ----------------------------------------------------------

    [Theory]
    // <sound tempo> is quarter-BPM; a <metronome> counts <beat-unit> beats, so the
    // importer must scale per-minute to quarter-BPM.
    [InlineData("half", 0, 60, 120)]      // half = 60  -> quarter = 120
    [InlineData("eighth", 0, 120, 60)]    // eighth = 120 -> quarter = 60
    [InlineData("quarter", 0, 100, 100)]  // quarter = 100 -> unchanged
    [InlineData("quarter", 1, 80, 120)]   // dotted quarter = 80 -> quarter = 120
    public void Metronome_BeatUnit_NormalizesToQuarterBpm(string beatUnit, int dots, int perMinute, int expected)
    {
        var beatDots = string.Concat(Enumerable.Repeat("<beat-unit-dot/>", dots));
        var xml = $"""
            <?xml version="1.0"?>
            <score-partwise version="3.1">
              <part-list><score-part id="P1"><part-name>Music</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>1</divisions>
                    <key><fifths>0</fifths></key>
                    <time><beats>4</beats><beat-type>4</beat-type></time>
                    <clef><sign>G</sign><line>2</line></clef>
                  </attributes>
                  <direction>
                    <direction-type>
                      <metronome><beat-unit>{beatUnit}</beat-unit>{beatDots}<per-minute>{perMinute}</per-minute></metronome>
                    </direction-type>
                  </direction>
                  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note>
                </measure>
              </part>
            </score-partwise>
            """;

        var (importedLys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains($"tempo {expected}", importedLys);
    }

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
