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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A transposing part read from MusicXML keeps its transposition: the document's
/// <c>&lt;transpose&gt;</c> is the part header's <c>transposition</c> — the pitch a written
/// middle C sounds, since the property takes one (2026-11-15). Until then only whole octaves
/// were stated; a B♭ clarinet's part came in at written pitch with a warning, and played a tone
/// high.
/// </summary>
public class MusicXmlTransposingImportTests
{
    private static string Book(string header)
        => $"octave absolute\ntime 4/4\npart p {{ {header} }}\n"
            + "section A { p { c'4 d' e' f' | g'1 | } }\nform { A }\nscore { staff p }\n";

    private static int[] Sounds(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        return [.. new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree).Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch)];
    }

    private static string HeaderOf(string lys)
        => lys.Replace("\r", "").Split('\n').First(l => l.StartsWith("part ", System.StringComparison.Ordinal));

    /// <summary>What Lily# writes for a transposing part comes back as that transposition, with no
    /// warning, and the part read back sounds what the part written did.</summary>
    [Theory]
    [InlineData("instrument clarinet", "transposition bes }")]
    [InlineData("instrument trumpet", "transposition bes }")]
    [InlineData("instrument clarinet-a", "transposition a }")]
    [InlineData("instrument horn", "transposition f }")]
    [InlineData("instrument alto-sax", "transposition ees }")]
    [InlineData("instrument tenor-sax", "transposition bes, }")]
    [InlineData("instrument baritone-sax", "transposition ees, }")]
    [InlineData("clef treble transposition g", "transposition g }")]
    [InlineData("clef treble transposition ees'", "transposition ees' }")]
    [InlineData("instrument clarinet transposition bes,", "transposition bes, }")]
    // The octaves keep their markers.
    [InlineData("instrument piccolo", "transposition 8va }")]
    [InlineData("clef treble transposition 15mb", "transposition 15mb }")]
    public void ATransposingPart_ComesBackWithItsTransposition(string header, string comesBack)
    {
        string written = Book(header);
        var (lys, report) = new MusicXmlImporter().Import(new MusicXmlExporter().Export(SyntaxTree.Parse(written)).ToXml().ToString());
        Assert.Empty(report.Warnings);
        Assert.EndsWith(comesBack, HeaderOf(lys));
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        Assert.Equal(Sounds(written), Sounds(lys));
        // …and not what it would sound read at written pitch.
        Assert.NotEqual(Sounds(Book("clef treble")), Sounds(lys));
    }

    /// <summary>Another program's B♭ part: the notes stay as written and the header states the
    /// shift.</summary>
    [Fact]
    public void AnotherProgramsTransposingPart_KeepsItsNotesAndStatesItsShift()
    {
        const string Xml = "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>Clarinet</part-name></score-part></part-list>"
            + "<part id=\"P1\"><measure number=\"1\"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>"
            + "<clef><sign>G</sign><line>2</line></clef><transpose><diatonic>-1</diatonic><chromatic>-2</chromatic></transpose></attributes>"
            + "<note><pitch><step>D</step><octave>4</octave></pitch><duration>4</duration><type>whole</type></note></measure></part></score-partwise>";
        var (lys, report) = new MusicXmlImporter().Import(Xml);
        Assert.Empty(report.Warnings);
        Assert.EndsWith("{ clef treble transposition bes }", HeaderOf(lys));
        Assert.Contains("d'1", lys);
        Assert.Equal([60], Sounds(lys));   // the written D4 sounds C4
    }

    // ---- a score that prints each transposing part in its player's key ----

    private static string Whole(string step, int octave)
        => $"<note><pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>4</duration><type>whole</type></note>";

    /// <summary>A part of four bars, one note each, whose key is stated at bars 1, 3 and 4.</summary>
    private static string Part(string id, string transpose, int[] fifths, string[] steps)
    {
        string Key(int f) => $"<key><fifths>{f}</fifths></key>";
        return $"<part id=\"{id}\"><measure number=\"1\"><attributes><divisions>1</divisions>{Key(fifths[0])}"
            + "<time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef>"
            + transpose + $"</attributes>{Whole(steps[0], 5)}</measure>"
            + $"<measure number=\"2\">{Whole(steps[1], 5)}</measure>"
            + $"<measure number=\"3\"><attributes>{Key(fifths[1])}</attributes>{Whole(steps[2], 5)}</measure>"
            + $"<measure number=\"4\"><attributes>{Key(fifths[2])}</attributes>{Whole(steps[3], 5)}</measure></part>";
    }

    private const string InBFlat = "<transpose><diatonic>-1</diatonic><chromatic>-2</chromatic></transpose>";
    private static readonly string Flute = Part("{0}", "", [0, 1, 0], ["C", "D", "G", "C"]);
    private static readonly string Clarinet = Part("{0}", InBFlat, [2, 3, 2], ["D", "E", "A", "D"]);

    private static string Score(params (string Name, string Part)[] parts)
        => "<score-partwise version=\"4.0\"><part-list>"
            + string.Concat(parts.Select((p, i) => $"<score-part id=\"P{i + 1}\"><part-name>{p.Name}</part-name></score-part>"))
            + "</part-list>" + string.Concat(parts.Select((p, i) => string.Format(p.Part, "P" + (i + 1)))) + "</score-partwise>";

    /// <summary>Each part's key statements read back from MusicXML written again: the fifths.</summary>
    private static string[] KeysWritten(string lys)
        => [.. new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml().Descendants("part")
            .Select(part => string.Join(" ", part.Descendants("fifths").Select(f => f.Value)))];

    /// <summary>
    /// A B♭ clarinet printed in D major beside a flute in C: the file is in the flute's key and
    /// the clarinet's own is its header's — its home, where each of its sections opens — with no
    /// diagnostic in the file read. (Until 2026-11-15 the clarinet's key was written into its
    /// music at its first bar, which LYS2023 warned about; the page read it rightly.) A change
    /// both parts make is written in each part's music, and a return to the opening keys writes
    /// nothing: each part is back in its home.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]    // the transposing part first: the file's key is still the flute's
    public void ATransposingPartsOwnKey_IsItsHeadersKey(bool clarinetFirst)
    {
        string xml = clarinetFirst ? Score(("Clarinet", Clarinet), ("Flute", Flute)) : Score(("Flute", Flute), ("Clarinet", Clarinet));
        var (read, report) = new MusicXmlImporter().Import(xml);
        string lys = read.Replace("\r", "");
        Assert.Empty(report.Warnings);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Select(d => d.Message));

        Assert.Contains("\nkey c major\n", lys);
        Assert.Contains("part flute { clef treble }", lys);
        Assert.Contains("part clarinet { clef treble transposition bes key d major }", lys);
        // The home keys are written once, in the header; the change of the third bar in each part.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(lys, "key d major"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(lys, "key c major"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(lys, "key g major"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(lys, "key a major"));

        // Written to MusicXML again, each part states the keys it was read with.
        string[] keys = KeysWritten(lys);
        Assert.Equal(clarinetFirst ? ["2 3 2", "0 1 0"] : ["0 1 0", "2 3 2"], keys);
        // …and the clarinet sounds the flute's notes.
        var sounds = Sounds(lys);
        Assert.Equal(8, sounds.Length);
        Assert.Equal(sounds.Take(4), sounds.Skip(4));
    }

    /// <summary>A part with no signature first, beside a part in a key: the file's key is the
    /// keyed part's, and <c>key none</c> is the first part's own.</summary>
    [Fact]
    public void APartWithNoSignatureFirst_IsNotTheFilesKey()
    {
        static string One(string id, string key, string step)
            => $"<part id=\"{id}\"><measure number=\"1\"><attributes><divisions>1</divisions>{key}"
                + "<time><beats>4</beats><beat-type>4</beat-type></time><clef><sign>G</sign><line>2</line></clef></attributes>"
                + Whole(step, 4) + "</measure></part>";
        string xml = "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>Free</part-name></score-part>"
            + "<score-part id=\"P2\"><part-name>Keyed</part-name></score-part></part-list>"
            + One("P1", "<key />", "C") + One("P2", "<key><fifths>-1</fifths></key>", "F") + "</score-partwise>";
        string lys = new MusicXmlImporter().Import(xml).Item1.Replace("\r", "");
        Assert.Contains("\nkey f major\n", lys);
        Assert.Contains("part free { clef treble key none }", lys);
        Assert.Contains("part keyed { clef treble }", lys);
        Assert.Equal(["", "-1"], KeysWritten(lys));
    }

    /// <summary>The pitch a shift is written as reads back as that shift, over every shift a part
    /// could state.</summary>
    [Fact]
    public void TheWrittenPitch_IsTheShiftsInverse()
    {
        for (int semitones = -48; semitones <= 48; semitones++)
            Assert.Equal(semitones, InstrumentDefaults.TranspositionOfPitch(InstrumentDefaults.TranspositionPitchOf(semitones)));
        Assert.Equal("c'", InstrumentDefaults.TranspositionPitchOf(0));
        Assert.Equal("bes", InstrumentDefaults.TranspositionPitchOf(-2));
        Assert.Equal("bes,", InstrumentDefaults.TranspositionPitchOf(-14));
        Assert.Equal("ees'", InstrumentDefaults.TranspositionPitchOf(3));
        Assert.Equal("b", InstrumentDefaults.TranspositionPitchOf(-1));
        Assert.Equal("cis'", InstrumentDefaults.TranspositionPitchOf(1));
    }
}
