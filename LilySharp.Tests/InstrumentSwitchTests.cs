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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>instrument piccolo</c> in the music — a player changes instrument (docs/eval4-features-design.md
/// §1; grammar evaluation 4's "1", owner's go-ahead 2026-11-15; evaluation 5's B3). From there
/// the part sounds the preset — its program and its transposition — and is checked against its
/// range; the page prints its name over the change; the notes, the clef and the key stay as
/// written. MEASURED before (2026-11-22, Lab sessions/p1368/instr): `instrument` in the music was
/// an error ("not something a music block can hold"), and a player who doubled took a part a
/// staff.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InstrumentSwitchTests
{
    private static string Book(string music, string header = "", string score = "score { staff fl }") => $$"""
        octave absolute
        {{header}}
        part fl { instrument flute }
        section S { fl { {{music}} } }
        form { S }
        {{score}}
        """;

    private static List<MidiNote> Notes(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks.SelectMany(t => t.Notes)
            .Where(n => n.Part == "fl").OrderBy(n => n.StartTick).ToList();

    private static List<Diagnostic> Said(string src)
    {
        var tree = SyntaxTree.Parse(src);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    [Fact]
    public void TheNotesAfterTheChangeSoundTheNewInstrument()
    {
        // A flute's c'' is C5 (72); the piccolo's sounds an octave higher (84); back to the flute.
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book("c''1 | R1 | instrument piccolo c''1 | instrument flute c''1 |")));
        var track = midi.Tracks.Single(t => t.Name == "fl");
        var notes = track.Notes.OrderBy(n => n.StartTick).ToList();
        Assert.Equal(new[] { 72, 84, 72 }, notes.Select(n => n.Pitch));
        // Flute 73, piccolo 72 (General MIDI, 0-based).
        Assert.Equal(new int?[] { 73, 72, 73 }, notes.Select(track.ProgramOf));
        Assert.Equal(new string?[] { null, "piccolo", null }, notes.Select(n => n.Instrument));
    }

    [Fact]
    public void TheMidiFileChangesTheProgramWhereTheInstrumentChanges()
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book("c''1 | instrument piccolo c''1 | instrument flute c''1 |")));
        using var stream = new MemoryStream();
        midi.WriteTo(stream);
        var bytes = stream.ToArray();
        int Count(params byte[] needle) => Enumerable.Range(0, bytes.Length - needle.Length + 1)
            .Count(i => needle.Select((b, k) => bytes[i + k] == b).All(x => x));
        Assert.Equal(1, Count(0xC0, 72));   // the piccolo
        Assert.Equal(2, Count(0xC0, 73));   // the flute at the start and after it
    }

    [Fact]
    public void AChangeHoldsIntoTheNextSection()
    {
        string src = """
            octave absolute
            part fl { instrument flute }
            section A { fl { c''1 | instrument piccolo c''1 | } }
            section B { fl { c''1 | } }
            form { A B }
            score { staff fl }
            """;
        Assert.Equal(new[] { 72, 84, 84 }, Notes(src).Select(n => n.Pitch));
    }

    [Fact]
    public void AChangeInARepeatIsPlayedOnEveryPass()
    {
        // The change rides its notes through the repeat as played.
        Assert.Equal(new[] { 72, 84, 72, 84 }, Notes(Book("|: instrument flute c''1 | instrument piccolo c''1 :|")).Select(n => n.Pitch));
    }

    [Fact]
    public void ATransposingInstrumentSoundsItsTransposition()
    {
        // A B-flat clarinet's written c'' sounds B-flat 4 (70).
        Assert.Equal(new[] { 72, 70 }, Notes(Book("c''1 | instrument clarinet c''1 |")).Select(n => n.Pitch));
    }

    [Theory]
    [InlineData("instrument piccolo", "Piccolo")]
    [InlineData("instrument piccolo \"Picc.\"", "Picc.")]
    [InlineData("instrument clarinet", "Clarinet")]
    public void ThePagePrintsTheNameOverTheChange(string change, string name)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book($"c''1 | R1 | {change} c''1 |")));
        Assert.Contains(">" + name + "<", svg);
    }

    [Fact]
    public void TheNotesAreDrawnAsWritten()
    {
        static string Ink(string svg) => Regex.Replace(Regex.Replace(svg, @"\s*<text[^>]*>Piccolo</text>", ""),
            @"\s?data-(pos|span|start|alt)=""[^""]*""", "");
        string with = SvgGenerator.Generate(SyntaxTree.Parse(Book("c''1 | instrument piccolo d''4 e'' f'' g'' |")));
        string without = SvgGenerator.Generate(SyntaxTree.Parse(Book("c''1 | d''4 e'' f'' g'' |")));
        Assert.Equal(Ink(without), Ink(with));
    }

    [Fact]
    public void TheExportsWriteTheName()
    {
        string src = Book("c''1 | instrument piccolo c''1 |");
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(src));
        Assert.Contains("<>^\\markup { \\italic \"Piccolo\" } c''1", ly);
        Assert.DoesNotContain("InstrumentSwitch", ly);
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().ToString();
        Assert.Contains("<words>Piccolo</words>", xml);
    }

    /// <summary>MusicXML: the part lists the instrument it changes to, the change is a
    /// &lt;sound&gt;&lt;instrument-change&gt; with the program, the notes after it name it, and a
    /// part typed as written states its new transposition — through the next section, and back.</summary>
    [Fact]
    public void MusicXmlChangesTheInstrumentAndItsTransposition()
    {
        string src = """
            octave absolute
            part fl { instrument flute }
            section A { fl { c''1 | instrument clarinet c''1 | } }
            section B { fl { c''1 | instrument flute c''1 | } }
            form { A B }
            score { staff fl }
            """;
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml();
        var scorePart = doc.Descendants("score-part").Single();
        Assert.Equal(new[] { "P1-I1", "P1-I2" }, scorePart.Elements("score-instrument").Select(e => (string)e.Attribute("id")!));
        Assert.Equal(new[] { "Clarinet" }, scorePart.Elements("score-instrument").Skip(1).Select(e => (string)e.Element("instrument-name")!));
        var changes = doc.Descendants("instrument-change").Select(e => (string)e.Attribute("id")!).ToList();
        Assert.Equal(new[] { "P1-I2", "P1-I1" }, changes);
        Assert.Equal("72", (string)doc.Descendants("sound").First(s => s.Element("instrument-change") != null).Element("midi-instrument")!.Element("midi-program")!);
        // Bars 2 and 3 sound the clarinet (bar 3 is the next section's), bar 4 the flute again.
        var notes = doc.Descendants("note").ToList();
        Assert.Equal(new string?[] { null, "P1-I2", "P1-I2", null }, notes.Select(n => (string?)n.Element("instrument")?.Attribute("id")));
        var transposes = doc.Descendants("transpose").Select(t => (int)t.Element("chromatic")!).ToList();
        Assert.Equal(new[] { -2, 0 }, transposes);
    }

    // Four systems' worth of whole notes, with `change` before the bar after the first 15.
    private static string LongBook(string change, string names = "\"Flute\" \"Fl.\"", string back = "") => $$"""
        octave absolute
        part fl {{names}} { instrument flute }
        section S { fl { {{string.Concat(Enumerable.Repeat("c''1 | ", 15))}} R1 | {{change}} {{string.Concat(Enumerable.Repeat("c''1 | ", 15))}} {{back}} {{string.Concat(Enumerable.Repeat("c''1 | ", 15))}} } }
        form { S }
        score { staff fl }
        """;

    private static int Count(string svg, string text) => Regex.Matches(svg, ">" + Regex.Escape(text) + "<").Count;

    /// <summary>A later system is headed by the short name of the instrument in force at its
    /// first bar: the change's, once it has been made (docs/eval4-features-design.md §1).</summary>
    [Fact]
    public void TheSystemsAfterTheChangeAreHeadedByItsShortName()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(LongBook("instrument piccolo \"Piccolo\" \"Picc.\"")));
        Assert.True(Count(svg, "Picc.") >= 2, "the systems after the change name the piccolo");
        Assert.True(Count(svg, "Fl.") >= 1, "the system before it names the flute");
        // No system names the flute after a piccolo-headed one.
        int lastFlute = svg.LastIndexOf(">Fl.<", System.StringComparison.Ordinal);
        int firstPiccolo = svg.IndexOf(">Picc.<", System.StringComparison.Ordinal);
        Assert.True(lastFlute < firstPiccolo);
    }

    [Fact]
    public void AChangeBackToThePartsOwnInstrumentTakesItsNameAgain()
    {
        string src = LongBook("instrument piccolo \"Piccolo\" \"Picc.\"", back: "instrument flute");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(src));
        Assert.True(svg.LastIndexOf(">Fl.<", System.StringComparison.Ordinal) > svg.IndexOf(">Picc.<", System.StringComparison.Ordinal));
        Assert.DoesNotContain(Said(src), d => d.Code == DiagnosticCodes.InstrumentChangeHasNoShortName);
    }

    [Fact]
    public void AChangeThatWritesNoShortNameLeavesTheLaterSystemsUnnamed_AndSaysSo()
    {
        string src = LongBook("instrument piccolo");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(src));
        Assert.Equal(0, Count(svg, "Picc."));
        // Fewer systems are headed "Fl." than in the same book with no change: the later ones none.
        int withoutChange = Count(SvgGenerator.Generate(SyntaxTree.Parse(LongBook(""))), "Fl.");
        Assert.True(Count(svg, "Fl.") < withoutChange, $"{Count(svg, "Fl.")} of {withoutChange}");
        Assert.Contains(Said(src), d => d.Code == DiagnosticCodes.InstrumentChangeHasNoShortName && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void APartThatWritesNoShortNameIsNotAskedForOne()
        => Assert.DoesNotContain(Said(LongBook("instrument piccolo", names: "")), d => d.Code == DiagnosticCodes.InstrumentChangeHasNoShortName);

    [Fact]
    public void AnUnknownInstrumentIsTheHeadersError()
    {
        var said = Said(Book("c''1 | instrument piccollo c''1 |"));
        Assert.Contains(said, d => d.Code == DiagnosticCodes.UnknownSymbolCase && d.Message.Contains("Did you mean 'piccolo'?"));
    }

    [Fact]
    public void AMissingInstrumentIsSaid()
        => Assert.Contains(Said(Book("c''1 | instrument | c''1 |")), d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("'instrument' has no instrument"));

    [Theory]
    // Typed at concert pitch: the transposed page would have to move.
    [InlineData("c''1 | instrument piccolo c''1 |", "pitch concert", "score { staff fl }")]
    [InlineData("c''1 | instrument clarinet c''1 |", "pitch concert", "score { staff fl }")]
    // Typed as written, a concert score prints it: only a chromatic difference moves that page.
    [InlineData("c''1 | instrument clarinet c''1 |", "", "score full pitch concert { staff fl }")]
    public void AChangeThePageCannotFollowIsAnError(string music, string header, string score)
        => Assert.Contains(Said(Book(music, header, score)), d => d.Code == DiagnosticCodes.InstrumentChangeMovesThePage);

    [Theory]
    [InlineData("c''1 | instrument clarinet c''1 |", "", "score { staff fl }")]            // as written, for the player
    [InlineData("c''1 | instrument piccolo c''1 |", "", "score full pitch concert { staff fl }")]   // octaves only
    [InlineData("c''1 | instrument flute c''1 |", "pitch concert", "score { staff fl }")]   // the part's own
    public void AChangeThePageFollowsSaysNothing(string music, string header, string score)
        => Assert.DoesNotContain(Said(Book(music, header, score)), d => d.Severity == DiagnosticSeverity.Error);

    [Fact]
    public void TheRangeIsTheNewInstruments()
    {
        // A flute's c' (C4) is in its range, and below the piccolo's (D5 up) once it changes.
        var said = Said(Book("c'1 | instrument piccolo c'1 |"));
        var range = said.Where(d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange).ToList();
        Assert.Single(range);
        Assert.Contains("Piccolo", range[0].Message);
    }
}
