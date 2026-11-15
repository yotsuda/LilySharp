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
