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
    /// ⚠️ AN OCTAVE CLEF'S OCTAVE IS IN THE PITCH, NOT IN <c>transpose</c>. MusicXML reads a
    /// <c>&lt;pitch&gt;</c> under its clef, <c>clef-octave-change</c> included: a
    /// <c>treble_8</c> staff's middle line is B3, which is where the page draws a Lily# <c>b</c>
    /// and what it sounds. So the clef's octave moves the pitches, and <c>transpose</c> carries
    /// the INSTRUMENT's share alone (a bass, a piccolo). From 2026-08-17 to 2026-10-05 the
    /// pitches stayed as on the plain clef and <c>transpose</c> restated the clef's octave: the
    /// sound was right, but MuseScore — which honours the clef — drew a guitar's staff and
    /// fretted its TAB an octave high (LilySharp-Omr feedback #17). Before 2026-08-17 neither
    /// was there, and the guitar sounded an octave high everywhere.
    /// <para>
    /// Both directions are checked because the pair has to agree: the importer moves the
    /// pitches back by the clef's octave, and re-exporting the imported source has to produce
    /// the same attributes.
    /// </para>
    /// </remarks>
    [Theory]
    // header, expected written pitch, expected clef-octave-change, expected transpose
    [InlineData("instrument bass", "C3", null, -1)]      // the 8vb is the INSTRUMENT's
    [InlineData("instrument guitar", "C3", -1, null)]    // …and here the CLEF's: in the pitch
    [InlineData("clef treble_8", "C3", -1, null)]
    [InlineData("instrument piccolo", "C5", null, 1)]    // the other direction
    // Both sources at once: the clef's octave in the pitch, the instrument's in transpose.
    [InlineData("instrument bass clef bass_8", "C2", -1, -1)]
    [InlineData("clef bass", "C4", null, null)]           // the clef moves no pitch
    [InlineData("clef bass octave 3", "C3", null, null)]  // …the part's `octave` does
    [InlineData("clef treble", "C4", null, null)]
    public void TransposingPart_DeclaresItsShiftOnceAndSurvivesRoundTrip(
        string header, string writtenPitch, int? clefOctaveChange, int? octaveChange)
    {
        string source = $"part m {{ {header} }}\nsection A {{ m {{ c4 d e f | }} }}\n"
                        + "form { A }\nscore { staff m }";

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

    /// <summary>
    /// An octave clef and a <c>transpose</c> on one part are two octaves, each read as
    /// MusicXML says: the pitch under the clef, the transpose what it sounds beyond that — so
    /// the part comes in as <c>clef treble_8 transposition 8vb</c> and goes out as it came in.
    /// The control is the second part: an octave no clef word carries survives AS the property.
    /// </summary>
    /// <remarks>
    /// A document Lily# did not write, which the round-trip theory above cannot stand in for.
    /// Lily# wrote this spelling itself from 2026-08-17 to 2026-10-05 meaning ONE octave;
    /// those files are not read that way (owner's decision 2026-10-05: no compatibility with
    /// Lily#'s own older output while it has hardly any users).
    /// </remarks>
    [Fact]
    public void OctaveClefAndTranspose_AreTwoOctaves_EachReadAsMusicXmlSays()
    {
        const string xml = """
            <score-partwise version="4.0">
              <part-list>
                <score-part id="P1"><part-name>Guitar</part-name></score-part>
                <score-part id="P2"><part-name>Bass</part-name></score-part>
              </part-list>
              <part id="P1">
                <measure number="1">
                  <attributes>
                    <divisions>1</divisions>
                    <clef><sign>G</sign><line>2</line><clef-octave-change>-1</clef-octave-change></clef>
                    <transpose><diatonic>0</diatonic><chromatic>0</chromatic><octave-change>-1</octave-change></transpose>
                  </attributes>
                  <note><pitch><step>C</step><octave>4</octave></pitch><duration>1</duration><type>quarter</type></note>
                </measure>
              </part>
              <part id="P2">
                <measure number="1">
                  <attributes>
                    <divisions>1</divisions>
                    <clef><sign>F</sign><line>4</line></clef>
                    <transpose><diatonic>0</diatonic><chromatic>0</chromatic><octave-change>-1</octave-change></transpose>
                  </attributes>
                  <note><pitch><step>C</step><octave>3</octave></pitch><duration>1</duration><type>quarter</type></note>
                </measure>
              </part>
            </score-partwise>
            """;

        var (lys, _) = new MusicXmlImporter().Import(xml);

        Assert.Contains("clef treble_8 transposition 8vb", lys);
        Assert.Contains("clef bass transposition 8vb", lys);

        // Asked of the re-export rather than of the header, so the two halves of the reading
        // have to agree: each part goes out as it came in.
        var doc = XDocument.Parse(
            new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().ToString());
        var parts = doc.Descendants().Where(e => e.Name.LocalName == "part").ToList();
        int? Transpose(XElement part) => part.Descendants().FirstOrDefault(e => e.Name.LocalName == "transpose")
            is { } t ? int.Parse(t.Elements().First(e => e.Name.LocalName == "octave-change").Value) : null;
        string FirstPitch(XElement part) => string.Concat(part.Descendants()
            .First(e => e.Name.LocalName == "pitch").Elements().Select(e => e.Value));
        Assert.Equal(("C4", (int?)-1), (FirstPitch(parts[0]), Transpose(parts[0])));
        Assert.Equal(("C3", (int?)-1), (FirstPitch(parts[1]), Transpose(parts[1])));
    }

    /// <summary>
    /// A staff the score draws with one line says so, and its notes stand on that line
    /// (LilySharp-Omr feedback #18: MuseScore opened it with five lines, the notes under them).
    /// </summary>
    [Fact]
    public void AOneLineStaff_WritesItsLinesAndPutsItsNotesOnTheLine()
    {
        const string lys = """
            part perc { clef percussion }
            section A { perc { bd4 sn4 bd4 sn4 | } }
            form { A }
            score { staff perc as lines 1 }
            """;
        var doc = XDocument.Parse(new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().ToString());

        Assert.Equal("1", doc.Descendants().Single(e => e.Name.LocalName == "staff-lines").Value);
        var unpitched = doc.Descendants().Where(e => e.Name.LocalName == "unpitched").ToList();
        Assert.Equal(4, unpitched.Count);
        Assert.All(unpitched, u => Assert.Equal("B4", string.Concat(u.Elements().Select(e => e.Value))));
        // The control: five lines write no staff-details.
        var five = XDocument.Parse(new MusicXmlExporter().Export(
            SyntaxTree.Parse(lys.Replace(" as lines 1", ""))).ToXml().ToString());
        Assert.DoesNotContain(five.Descendants(), e => e.Name.LocalName == "staff-lines");
    }

    /// <summary>
    /// A part spelled the way MuseScore reads and writes an octave clef — the pitches under
    /// the clef, no <c>transpose</c> (a tenor line, a guitar) — imports with its notes where
    /// the page draws them: C3 under <c>treble_8</c> is a Lily# <c>c</c> on that clef
    /// (LilySharp-Omr feedback #17; it used to come in an octave low).
    /// </summary>
    [Fact]
    public void OctaveClefPart_WithThePitchUnderTheClef_ImportsAtThePlainClefsOctave()
    {
        const string xml = """
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Tenor</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes>
                    <divisions>1</divisions>
                    <clef><sign>G</sign><line>2</line><clef-octave-change>-1</clef-octave-change></clef>
                  </attributes>
                  <note><pitch><step>B</step><octave>3</octave></pitch><duration>4</duration><type>whole</type></note>
                </measure>
              </part>
            </score-partwise>
            """;

        var (lys, _) = new MusicXmlImporter().Import(xml);

        Assert.Contains("clef treble_8", lys);
        Assert.DoesNotContain("transposition", lys);
        // Re-exported, the same B3 under the same clef, nothing restated.
        var doc = XDocument.Parse(
            new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().ToString());
        Assert.Equal("B3", string.Concat(doc.Descendants().First(e => e.Name.LocalName == "pitch")
            .Elements().Select(e => e.Value)));
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName == "transpose");
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
                e'4@chord(C) e' f' g' | a'4@chord(Am) g' e' d' |
                f'4@chord(F) a' g' f' | e'4@chord(G7) d' c'2 |
              }
              lyrics words sings melody { Mu- sic fills the | air to- night so | ev- 'ry- one will | sing a- long | }
            }

            form { A }

            score "lead-sheet" { staff melody  lyrics words }
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
              melody { e'4@chord(C) e' f' g' | }
              lyrics words sings melody { Mu- sic fills the | }
            }
            form { A }
            score { staff melody  lyrics words }
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
              bass { c4@figuredBass(6) d4@figuredBass(6 4) e4@figuredBass(7 s) f4@figuredBass(_) | }
            }
            form { A }
            score { staff bass }
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
        Assert.Contains("@chord(C)", lys);      // harmony
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
        // A plain |: ... :| repeat with no endings. ⚠️ RE-SPELLED 2026-08-31 (LYS1034): the
        // repeat is in the form, because that is the only place it can be written — and the
        // round trip is the reason this test matters more than it did. The importer used to
        // write repeat barlines back into a flat section (LysWriter.BarlineBetween), which is
        // now a book Lily# refuses; it factors the repeat into sections and a form instead
        // (TryFactorPlainRepeats), and this is the test that walks that path.
        AssertRoundTrips("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 d' e' f' | g'4 a' b' c'' | } }
            form { |: A :| }
            score { staff melody }
            """);
    }

    [Fact]
    public void RelativeOctave_UnderVolta_PreservesPitches()
    {
        // Relative output now also covers the by-section volta layout (each section
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
        Assert.Contains("|: ~Body [1. ~End1] :| [2. ~End2]", lys);
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
        Assert.Contains("|: ~Body [1. ~End1] :| [2. ~End2]", lys);
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
        Assert.Contains("lyrics words sings tune { la |", lys[body..end1]);
        Assert.Contains("lyrics words sings tune { one |", lys[end1..end2]);
        Assert.Contains("lyrics words sings tune { two |", lys[end2..]);
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

    /// <summary>
    /// A grand staff's marks come back on their own hand, and its octave lines and pedals come
    /// back at all (2026-09-30): the import read neither &lt;octave-shift&gt; nor &lt;pedal&gt;,
    /// and a direction of staff 2 marked staff 1's note at the same beat — the left hand's
    /// <c>@f</c> came back on the right hand's first note.
    /// </summary>
    [Fact]
    public void AGrandStaffsMarks_ComeBackOnTheirOwnStaff_WithTheirLinesAndPedals()
    {
        var original = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section A {
              rh { c''4@quindicesima d'' e'' f''@!ottava@p | g''1 | g''1 | }
              lh { c4@f@ottava(bassa) d e f@!ottava | g,1@sustain | c1@!sustain | }
            }
            form { ~A }
            score { grandStaff { staff rh "Piano"  staff lh } }
            """);
        var xml = new MusicXmlExporter().Export(original).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        Assert.Contains("c''4@quindicesima d''4 e''4 f''4@!ottava@p |", lys);
        Assert.Contains("c4@f@ottava(bassa) d4 e4 f4@!ottava | g,1@sustain | c1@!sustain |", lys);
    }

    /// <summary>
    /// A staff's label survives the round trip, and a staff with none stays unlabelled
    /// (2026-09-30): the export writes a label-less staff's id as a part name marked
    /// <c>print-object="no"</c>, and the import writes back only the names a source prints —
    /// on a split grand staff, on its first staff, as it was written.
    /// </summary>
    [Fact]
    public void AStaffsLabel_ComesBack_AndAnUnlabelledStaffStaysUnlabelled()
    {
        var original = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            part fl { clef treble }
            section A { rh { c''1 | } lh { c1 | } fl { g''1 | } }
            form { ~A }
            score { grandStaff { staff rh "Piano"  staff lh }  staff fl }
            """);
        var xml = new MusicXmlExporter().Export(original).ToXml();
        Assert.Equal(new[] { "Piano:-", "fl:no" }, xml.Descendants("part-name")
            .Select(n => $"{n.Value}:{(string?)n.Attribute("print-object") ?? "-"}"));
        var (lys, _) = new MusicXmlImporter().Import(xml.ToString());
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        var staves = lys.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("staff ")).ToArray();
        Assert.Equal(new[] { "staff pianoRH \"Piano\"", "staff pianoLH", "staff fl" }, staves);
    }

    /// <summary>
    /// A grand staff labelled apart — two parts under a brace <c>&lt;part-group&gt;</c> in the
    /// export — comes back as one grand staff with both labels (2026-09-30: the import did not
    /// read the part-group, so it came back as two unrelated staves). A part outside the brace
    /// stays outside.
    /// </summary>
    [Fact]
    public void ABracePartGroup_ComesBackAsAGrandStaff()
    {
        var original = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            part fl { clef treble }
            section A { rh { c''1 | } lh { c1 | } fl { g''1 | } }
            form { ~A }
            score { grandStaff { staff rh "Right"  staff lh "Left" }  staff fl }
            """);
        var xml = new MusicXmlExporter().Export(original).ToXml().ToString();
        Assert.Contains("<group-symbol>brace</group-symbol>", xml);
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        var score = lys[lys.IndexOf("score", System.StringComparison.Ordinal)..]
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        Assert.Equal(new[] { "score \"imported\" {", "grandStaff {", "staff right \"Right\"",
            "staff left \"Left\"", "}", "staff fl", "}" }, score);
    }

    /// <summary>
    /// Every staff group is a <c>&lt;part-group&gt;</c> and comes back as written (2026-09-30:
    /// a <c>staffGroup</c> or <c>choirStaff</c> wrote nothing, and the import read no group):
    /// a staffGroup is a bracket with its bar lines through, a choirStaff one without, groups
    /// nest with numbers apart, and a grand staff merged into one part is a part inside its
    /// group, not a group.
    /// </summary>
    [Fact]
    public void StaffGroups_AreWrittenAndComeBackNested()
    {
        var original = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            part vn { clef treble }
            part sop { clef treble }
            part alt { clef treble }
            part fl { clef treble }
            section A { rh { c''1 | } lh { c1 | } vn { g'1 | } sop { e''1 | } alt { c''1 | } fl { g''1 | } }
            form { ~A }
            score {
              staffGroup { grandStaff { staff rh "Piano"  staff lh }  staff vn "Violin" }
              choirStaff { staff sop "S"  staff alt "A" }
              staff fl
            }
            """);
        var xml = new MusicXmlExporter().Export(original).ToXml();
        var list = xml.Descendants("part-list").Single().Elements().Select(e => e.Name == "part-group"
            ? $"{e.Attribute("type")!.Value}{e.Attribute("number")!.Value}:{e.Element("group-symbol")?.Value}:{e.Element("group-barline")?.Value}"
            : e.Element("part-name")!.Value);
        Assert.Equal(new[] { "start1:bracket:yes", "Piano", "Violin", "stop1::", "start1:bracket:no", "S", "A",
            "stop1::", "fl" }, list);

        var (lys, _) = new MusicXmlImporter().Import(xml.ToString());
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        var score = lys[lys.IndexOf("score", System.StringComparison.Ordinal)..]
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        Assert.Equal(new[] { "score \"imported\" {", "staffGroup {", "grandStaff {",
            "staff pianoRH \"Piano\"", "staff pianoLH", "}", "staff violin \"Violin\"", "}",
            // "S" and "A" lex as a spacer and a note: those parts take their index names.
            "choirStaff {", "staff part3 \"S\"", "staff part4 \"A\"", "}", "staff fl", "}" }, score);
    }

    /// <summary>A group of groups labelled apart nests its numbers: the outer bracket and the
    /// inner brace start on the same part, the brace closing first.</summary>
    [Fact]
    public void NestedGroups_StartingOnOnePart_AreNumberedApart()
    {
        var original = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            part vn { clef treble }
            section A { rh { c''1 | } lh { c1 | } vn { g'1 | } }
            form { ~A }
            score { staffGroup { grandStaff { staff rh "Right"  staff lh "Left" }  staff vn "Violin" } }
            """);
        var xml = new MusicXmlExporter().Export(original).ToXml();
        var list = xml.Descendants("part-list").Single().Elements().Select(e => e.Name == "part-group"
            ? $"{e.Attribute("type")!.Value}{e.Attribute("number")!.Value}:{e.Element("group-symbol")?.Value}"
            : e.Element("part-name")!.Value);
        Assert.Equal(new[] { "start1:bracket", "start2:brace", "Right", "Left", "stop2:", "Violin", "stop1:" }, list);
        var (lys, _) = new MusicXmlImporter().Import(xml.ToString());
        var score = lys[lys.IndexOf("score", System.StringComparison.Ordinal)..]
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        Assert.Equal(new[] { "score \"imported\" {", "staffGroup {", "grandStaff {",
            "staff right \"Right\"", "staff left \"Left\"", "}", "staff violin \"Violin\"", "}", "}" }, score);
    }

    /// <summary>A part joined to a grand staff by a brace is a part of its own: its clef
    /// changes come back (the split staves of ONE part take theirs from the staff instead).</summary>
    [Fact]
    public void APartUnderABrace_KeepsItsClefChanges()
    {
        var original = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section A { rh { c''1 | c''1 | } lh { c1 | clef treble c''1 | } }
            form { ~A }
            score { grandStaff { staff rh "Right"  staff lh "Left" } }
            """);
        var xml = new MusicXmlExporter().Export(original).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains("clef treble c", lys[lys.IndexOf("left {", System.StringComparison.Ordinal)..]);
    }

    /// <summary>A brace around a single one-staff part is no grand staff: the part stays a
    /// plain staff.</summary>
    [Fact]
    public void ABraceAroundOnePart_IsAPlainStaff()
    {
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list>
                <part-group type="start" number="1"><group-symbol>brace</group-symbol></part-group>
                <score-part id="P1"><part-name>Harp</part-name></score-part>
                <part-group type="stop" number="1"/>
              </part-list>
              <part id="P1"><measure number="1">
                <attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>
                  <clef><sign>G</sign><line>2</line></clef></attributes>
                <note><pitch><step>C</step><octave>5</octave></pitch><duration>4</duration><type>whole</type></note>
              </measure></part>
            </score-partwise>
            """);
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        Assert.DoesNotContain("grandStaff", lys);
        Assert.Contains("staff harp \"Harp\"", lys);
    }

    /// <summary>
    /// A stop written where MusicXML programs usually write it — after the last note it covers,
    /// at the bar's end — closes on the NEXT note, the one outside the line: on the bar's last
    /// note it would end the line a note early. A stop after the part's last note stays on that
    /// note (Lily# requires the end). A pedal stop closes the pedal that is down.
    /// </summary>
    [Fact]
    public void AStopAfterTheBarsLastNote_ClosesOnTheNextNote()
    {
        var (lys, _) = new MusicXmlImporter().Import("""
            <?xml version="1.0"?>
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Flute</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>1</divisions><time><beats>2</beats><beat-type>4</beat-type></time>
                    <clef><sign>G</sign><line>2</line></clef></attributes>
                  <direction><direction-type><octave-shift type="down" size="8"/></direction-type></direction>
                  <direction><direction-type><pedal type="sostenuto"/></direction-type></direction>
                  <note><pitch><step>C</step><octave>6</octave></pitch><duration>1</duration><type>quarter</type></note>
                  <note><pitch><step>D</step><octave>6</octave></pitch><duration>1</duration><type>quarter</type></note>
                  <direction><direction-type><octave-shift type="stop" size="8"/></direction-type></direction>
                </measure>
                <measure number="2">
                  <note><pitch><step>E</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                  <note><pitch><step>F</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                  <direction><direction-type><pedal type="stop"/></direction-type></direction>
                </measure>
              </part>
            </score-partwise>
            """);
        Assert.False(HasErrors(SyntaxTree.Parse(lys)), lys);
        Assert.Contains("c''4@ottava@sostenuto d''4 | e'4@!ottava f'4@!sostenuto |", lys);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>Since 2026-09-29 the exporter writes every direction at the bar's head WITH
    /// its offset, and the reader waits for the first note at or past that offset: a dynamic on
    /// beat 4 comes back on beat 4, and one on a part's last note comes back at all.</summary>
    [Fact]
    public void Dynamics_RoundTripAtTheirNotes()
    {
        const string lys = """
            octave absolute
            part melody { clef treble }
            section A { melody { c'4 d'@cresc e' f'@f | c'1@p | } }
            form { A }
            score { staff melody }
            """;
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().ToString();
        var (imported, _) = new MusicXmlImporter().Import(xml);
        Assert.False(HasErrors(SyntaxTree.Parse(imported)), imported);
        Assert.Matches(@"f'*4@f\b", imported);
        Assert.DoesNotMatch(@"c'*4@f\b", imported);
        Assert.Matches(@"c'*1@p\b", imported);
    }

    [Fact]
    public void Dynamics_ImportOntoTheFollowingNote()
    {
        // Real MusicXML interleaves a <direction> right before the note it marks (the Lily#
        // exporter writes them at the measure's head with an <offset> instead — the round
        // trip above). Each dynamic attaches to the next note as @p / @f.
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
        // hand-crafted MusicXML: a 2-beam single tremolo -> :16, plus @reverseTurn
        // (LilyPond's name for MusicXML's inverted-turn).
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
        Assert.Contains("@reverseTurn", lys);
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

    [Fact]
    public void StringNumbersAndFingering_SurviveRoundTrip()
    {
        // Until 2026-09-08 neither direction carried them: the exporter wrote no <technical>
        // <string>/<fingering>, and the importer read nothing under <technical>.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            c'4\3 d'4@finger(1) <e'\5 dis''\4>4 <c'@finger(2) e'\4>4 |
            """);
        var xml1 = new MusicXmlExporter().Export(tree).ToXml();
        var (lys, report) = new MusicXmlImporter().Import(xml1.ToString());

        var importedTree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(importedTree), $"imported .lys did not parse clean:\n{lys}");
        Assert.Equal(Signature(tree), Signature(importedTree));
        Assert.Empty(report.Warnings);
        // A single note's marks follow its duration; a member's sit inside the brackets.
        Assert.Contains("c'4\\3", lys);
        Assert.Contains("d'4@finger(1)", lys);
        Assert.Contains("<e'\\5 dis''\\4>4", lys);
        Assert.Contains("<c'@finger(2) e'\\4>4", lys);

        // Same <technical> content out of the imported source, note for note.
        var xml2 = new MusicXmlExporter().Export(importedTree).ToXml();
        Assert.Equal("3 f1 5 4 f2 4", TechnicalSignature(xml1));
        Assert.Equal(TechnicalSignature(xml1), TechnicalSignature(xml2));

        // A lettered fingering has no numeric spelling: dropped with a warning, not an error.
        var lettered = xml1.ToString().Replace("<fingering>1</fingering>", "<fingering>p</fingering>");
        var (lys2, report2) = new MusicXmlImporter().Import(lettered);
        Assert.DoesNotContain("@finger(1)", lys2);
        Assert.Contains(report2.Warnings, w => w.Contains("fingering 'p'"));
    }

    private static string TechnicalSignature(XDocument xml) =>
        string.Join(" ", xml.Descendants("technical").SelectMany(t =>
            t.Elements().Select(e => e.Name.LocalName == "fingering" ? "f" + e.Value : e.Value)));

    private static string NotationSignature(XDocument xml) =>
        string.Join(" ", xml.Descendants("notations").SelectMany(n =>
            n.Elements("articulations").Elements().Select(e => e.Name.LocalName)
            .Concat(n.Elements("ornaments").Elements().Select(e => e.Name.LocalName))
            .Concat(n.Elements("fermata").Select(_ => "fermata"))
            .Concat(n.Elements("slur").Select(s => "slur:" + (string?)s.Attribute("type")))));

    // NOTE: structure-level repeats (|: A :|) replay sections in the collector but
    // the exporter unrolls them to repeat BARLINES (section emitted once), so a
    // round-trip through XML can't match on replay count until the importer factors
    // repeats back into form { } (Phase 3). Deliberately not covered here yet.

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

    /// <summary>
    /// A multi-measure rest comes back as its N bars (the exporter writes every one since
    /// 2026-09-29 — HANDOFF §1.1 第662 ⑷) and a spacer comes back as a spacer: the importer
    /// reads <c>print-object="no"</c> on a rest as <c>s</c>, so the bar that printed nothing
    /// still prints nothing.
    /// </summary>
    [Fact]
    public void MultiMeasureRestAndSpacer_SurviveRoundTrip()
    {
        string original = MusicSource.Wrap("c'1 | R1*3 | s2 g'2 | r1 |", "octave absolute\ntime 4/4");
        AssertRoundTrips(original);

        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(original)).ToXml().ToString();
        var (importedLys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains("s2 g'2", importedLys);
        Assert.Equal(6, new MeasureCollector().Collect(SyntaxTree.Parse(importedLys)).Voice.Measures.Length);
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

    /// <summary>
    /// A grace group's slur — the <c>(</c> on its last note, closed on the main note — survives
    /// the round trip. Until 2026-09-30 neither side carried it: the export wrote the main
    /// note's stop with no start, and the import came back with a stray <c>)</c> (LYS4010).
    /// </summary>
    [Fact]
    public void GraceSlurIntoTheMainNote_RoundTrips()
    {
        const string music = "c'4 grace { d''16( } e''4) acciaccatura { f''8( } g''4) grace { a''16 b''16( } c'''4) |";
        string source = "octave absolute\ntime 4/4\npart m { clef treble }\n"
                        + $"section S {{ m {{ {music} }} }}\nform {{ ~S }}\nscore {{ staff m }}\n";
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();
        var (lys, report) = new MusicXmlImporter().Import(xml);
        Assert.Contains(music, lys);
        Assert.Empty(report.Warnings);
        Assert.DoesNotContain("warning", Diagnostics(SyntaxTree.Parse(lys)));
    }

    /// <summary>
    /// A grace chord comes back as one chord, with its slur, in both octave modes. Until
    /// 2026-09-30 each <c>&lt;chord/&gt;</c> member became a grace note of its own, one after
    /// another, and the slur on the chord was then not on the group's last note; and the
    /// relative spelling of ANY chord whose root needs marks was two octaves off from its
    /// second member on. Asked of the pitches the re-export writes, so the relative spelling
    /// is held to the same sounds.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraceChord_RoundTripsAsOneChord(bool relative)
    {
        // The second bar is the relative spelling's own case, no grace involved: a chord root
        // that needs marks (`c'''4 <e' g'>4` is `<e,, g,,>` — the root's marks are local, the
        // members stack above its bare-letter anchor, and the next note reads from the anchor).
        const string music = "c'4 grace { a''16 <b'' d'''>16( } c'''4) acciaccatura { <e' g'>8 } f'4 d'4 | c'''4 <e' g'>4 f'4 d'4 |";
        string source = "octave absolute\ntime 4/4\npart m { clef treble }\n"
                        + $"section S {{ m {{ {music} }} }}\nform {{ ~S }}\nscore {{ staff m }}\n";
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();
        var (lys, report) = new MusicXmlImporter().Import(xml, relativeOctave: relative);
        Assert.Empty(report.Warnings);
        if (!relative)
            Assert.Contains(music, lys);
        static string Pitches(string x) => string.Join(" ", XDocument.Parse(x).Descendants()
            .Where(e => e.Name.LocalName == "note")
            .Select(n => (n.Elements().Any(e => e.Name.LocalName == "chord") ? "+" : "")
                + string.Concat(n.Descendants().Where(e => e.Name.LocalName is "step" or "alter" or "octave").Select(e => e.Value))));
        string again = new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().ToString();
        Assert.Equal(Pitches(xml), Pitches(again));
    }

    /// <summary>
    /// Mid-piece changes of time, key, clef and tempo come back where they were written.
    /// Until 2026-09-30 only the opening time and key reached the file, so later bars were
    /// read against the first signature and the other changes were lost. In a part of two
    /// voices the change rides the first voice's stream — where the source wrote it — and the
    /// import reads exactly as the source does (that source warns about its own bar 1: a
    /// `time` inside one voice block is checked against the other voice's first bar too).
    /// The mark keeps the piece in one section: without one, each change opens a section of
    /// its own (<see cref="MusicXmlSectionCutTests"/>).
    /// </summary>
    [Theory]
    [InlineData("c'1@mark(\"A\") | time 3/4 c'2. | key d major d'2. | clef bass tempo 80 d2. | time 4/4 key c major clef treble c'1 |")]
    [InlineData("voice { c''1@mark(\"A\") | time 3/4 c''2. | } { c'1 | c'2. | }")]
    public void MidPieceChanges_RoundTrip(string music)
    {
        string source = "octave absolute\ntime 4/4\nkey c major\ntempo 100\npart m { clef treble }\n"
                        + $"section S {{ m {{ {music} }} }}\nform {{ ~S }}\nscore {{ staff m }}\n";
        var sourceTree = SyntaxTree.Parse(source);
        string xml = new MusicXmlExporter().Export(sourceTree).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        var tree = SyntaxTree.Parse(lys);
        Assert.False(HasErrors(tree), $"{lys}\n---\n{Diagnostics(tree)}");
        Assert.Contains(music, OneLine(lys));
        static IEnumerable<string> Messages(SyntaxTree t)
            => t.Diagnostics.Concat(SemanticValidation.Run(t)).Select(d => d.Message);
        Assert.Equal(Messages(sourceTree), Messages(tree));
    }

    /// <summary>
    /// A repeat pass that ends on a full bar closes it, as the page does — with or without a
    /// bar line written after the repeat. Until 2026-09-30 each pass ran on into the next:
    /// <c>repeat percent 2 { r2. | r2. } c'2. |</c> exported as <c>r | r r | r c</c>.
    /// </summary>
    [Theory]
    [InlineData("repeat percent 2 { r2. | r2. } c'2. |", "r2. | r2. | r2. | r2. | c'2. |")]
    [InlineData("repeat unfold 2 { c'2. | d'2. } | e'2. |", "c'2. | d'2. | c'2. | d'2. | e'2. |")]
    [InlineData("c'4 repeat unfold 2 { d'4 } | e'2. |", "c'4 d'4 d'4 | e'2. |")]
    public void RepeatPassEndingOnAFullBar_ClosesIt(string music, string expected)
    {
        string source = $"octave absolute\ntime 3/4\npart m {{ clef treble }}\n"
                        + $"section A {{ m {{ {music} }} }}\nform {{ ~A }}\nscore {{ staff m }}\n";
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains(expected, OneLine(lys));
    }

    /// <summary>The imported source with its line breaks read as spaces: the importer writes
    /// a few bars to a line (LysWriter.BarsPerLine), and these fixtures name a stretch of
    /// music, not where its lines fall.</summary>
    private static string OneLine(string lys)
        => System.Text.RegularExpressions.Regex.Replace(lys, @"\s*\n\s*", " ");

    /// <summary>
    /// A rest's post-events, a whole-measure rest and a multi-measure rest survive the round
    /// trip. Until 2026-09-30 the importer wrote every rest bare (`R1*4@p` came back as four
    /// `r1`, `r2@fermata` as `r2`), the exporter wrote a lone `R1` as a plain rest and left
    /// the tuplet ratio off a rest, so a bracket opening on one came back as a stray `}`.
    /// A whole-measure rest comes back as a bare `R` (owner's decision 2026-10-02,
    /// Music.BarRest), whatever duration it was written with — 5/4 has none to write.
    /// </summary>
    [Theory]
    [InlineData("time 4/4", "c'1 | R1*4@p | c'2 r2@fermata | R1 | r4@f c'4 c'2 |",
                "c'1 | R*4@p | c'2 r2@fermata | R | r4@f c'4 c'2 |")]
    [InlineData("time 3/4", "c'2. | R2.*3@mf | tuplet 3/2 { r8 c'8 c'8 } c'2 | r2.@chord(C) | R2. | R2.*2 | c'2. |",
                "c'2. | R*3@mf | tuplet 3/2 { r8 c'8 c'8 } c'2 | r2.@chord(C) | R | R*2 | c'2. |")]
    [InlineData("time 5/4", "c'4 d'4 e'4 f'4 g'4 | R*2@p | R | c'1 r4 |",
                "c'4 d'4 e'4 f'4 g'4 | R*2@p | R | c'1 r4 |")]
    public void RestsAndTheirPostEvents_RoundTrip(string time, string music, string expected)
    {
        // `~A`: no section label, which would come back as the first note's @mark.
        string source = $"octave absolute\n{time}\npart m {{ clef treble }}\n"
                        + $"section A {{ m {{ {music} }} }}\nform {{ ~A }}\nscore {{ staff m }}\n";
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains(expected, OneLine(lys));
    }

    /// <summary>
    /// The direction's text survives the round trip: <c>@text</c> (with its side),
    /// <c>@mark</c>, and the una corda pair. Until 2026-09-30 the importer read no
    /// &lt;words&gt; and no &lt;rehearsal&gt; at all.
    /// </summary>
    [Fact]
    public void TextRehearsalAndUnaCorda_RoundTrip()
    {
        const string music = "c'4@mark(\"Q\") d'4 e'4@text(\"dolce\").up f'4 | R@text(\"tacet\") | c'4@unaCorda d'4 e'4@treCorde f'4 |";
        string source = "octave absolute\ntime 4/4\npart m { clef treble }\n"
                        + $"section S {{ m {{ {music} }} }}\nform {{ ~S }}\nscore {{ staff m }}\n";
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains(music, lys);
    }

    /// <summary>
    /// Another program's words: several &lt;words&gt; in one direction-type are one text; a
    /// direction placed above is <c>.up</c>; a word paired with a jump is kept as text and the
    /// report says the jump belongs in the form.
    /// </summary>
    [Fact]
    public void PublishedWords_JoinAndKeepTheirSide_AndAJumpIsReported()
    {
        const string xml = """
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>1</divisions><time><beats>2</beats><beat-type>4</beat-type></time>
                    <clef><sign>G</sign><line>2</line></clef></attributes>
                  <direction placement="above"><direction-type><words>poco </words><words>rit.</words></direction-type></direction>
                  <note><pitch><step>C</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                  <direction placement="above"><direction-type><words>D.C. al Fine</words></direction-type><sound dacapo="yes"/></direction>
                  <note><pitch><step>D</step><octave>5</octave></pitch><duration>1</duration><type>quarter</type></note>
                </measure>
              </part>
            </score-partwise>
            """;
        var (lys, report) = new MusicXmlImporter().Import(xml);
        Assert.Contains("c'4@text(\"poco rit.\").up d'4@text(\"D.C. al Fine\").up |", lys);
        Assert.Contains(report.Warnings, w => w.Contains("D.C. al Fine") && w.Contains("form"));
    }

    /// <summary>
    /// A whole-measure rest from another program: MuseScore writes <c>&lt;rest measure="yes"/&gt;</c>
    /// with no <c>&lt;type&gt;</c>, others write <c>whole</c> in any meter. Either way the bar's
    /// length is the duration, and only a <c>multiple-rest</c> mark folds bars — two
    /// consecutive bar rests without one stay two (LilyPond's <c>R2. | R2.</c> is two events).
    /// </summary>
    [Fact]
    public void PublishedMeasureRests_TakeTheBarsLength_AndFoldOnlyUnderTheMark()
    {
        const string xml = """
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>A</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>2</divisions><time><beats>3</beats><beat-type>4</beat-type></time>
                    <clef><sign>G</sign><line>2</line></clef>
                    <measure-style><multiple-rest>2</multiple-rest></measure-style></attributes>
                  <note><rest measure="yes"/><duration>6</duration></note>
                </measure>
                <measure number="2"><note><rest measure="yes"/><duration>6</duration></note></measure>
                <measure number="3"><note><rest measure="yes"/><duration>6</duration><type>whole</type></note></measure>
                <measure number="4"><note><rest measure="yes"/><duration>6</duration><type>whole</type></note></measure>
              </part>
            </score-partwise>
            """;
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains("R*2 | R | R |", lys);
    }
}
