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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using Position = LilySharp.Lsp.Protocol.Position;

namespace LilySharp.Tests;

/// <summary>
/// <c>chord(SYMBOL SHAPE)</c> — a chord FROM A SHAPE (owner's decisions 2026-09-28, HANDOFF §2 K5):
/// the shape's sounding strings on the tuning of the part that plays it (its fretted instrument,
/// else the guitar), ABSOLUTE, each note with its string number; the relative frame after it is
/// its lowest sounding note; notes only — a bare <c>@chord</c> on it names the item's symbol and
/// draws its shape.
/// </summary>
[Trait("Category", "Unit")]
public class ShapeChordItemTests
{
    private static string Book(string header, string music, string score = "staff gt", string top = "")
        => top + $$"""

        part gt { {{header}} }
        section A { gt { {{music}} } }
        form main { A }
        score main { {{score}} }
        """;

    private static ChordSyntax Item(string book)
        => SyntaxTree.Parse(book).GetNodes<ChordSyntax>().First(c => c.IsShapeChord);

    private static IReadOnlyList<Diagnostic> Diagnostics(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static int[] MidiPitches(string book)
        => new MidiExporter().Export(SyntaxTree.Parse(book)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => n.Pitch).ToArray();

    private static MultiStaffScore Collected(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    private static List<MusicItem> Items(MultiStaffScore score, bool tab = false)
        => [.. score.EnumerateStaves().First(s => s.Staff.IsTab == tab).Staff
            .PrimaryVoice.Measures.SelectMany(m => m.Items)];

    private static string Names(List<ShapeNote> notes) => string.Join(" ", notes.Select(n => n.SoundingName));

    private static List<ShapeNote> Notes(string words, TuningType tuning, int shift = 0, int keySharps = 0)
        => [.. ShapeChords.Notes(ChordAnnotation.Parse(words.Split(' ')), tuning, shift, keySharps)];

    // ================================================================ the parse

    [Theory]
    [InlineData("chord(C x32013)1", "C", "x32013", 1, 0, false)]
    [InlineData("chord(Cm7 x3x546)2@chord", "Cm7", "x3x546", 2, 0, false)]
    [InlineData("chord(C xx-10-12-13-12)4.~", "C", "xx-10-12-13-12", 4, 1, true)]
    [InlineData("chord(x32013)8", "x32013", null, 8, 0, false)]
    public void TheItem_ParsesItsWordsAndTheTailOfAChord(string music, string first, string? second,
        int value, int dots, bool tie)
    {
        string book = Book("instrument guitar", music + " |");
        var tree = SyntaxTree.Parse(book);
        Assert.Equal(book, tree.GetRoot().ToFullString());
        var item = tree.GetNodes<ChordSyntax>().Single();
        Assert.True(item.IsShapeChord);
        Assert.False(item.IsEmpty);
        Assert.Equal(first, item.ShapeArguments[0].Text);
        Assert.Equal(second, item.ShapeArguments.Length > 1 ? item.ShapeArguments[1].Text : null);
        Assert.Equal(value, item.Duration!.Value);
        Assert.Equal(dots, item.Duration.DotCount);
        Assert.Equal(tie, item.Articulations.OfType<TieSyntax>().Any()
                          || tree.GetNodes<TieSyntax>().Any());
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Under a capo (2026-09-29) the shape is pressed above it: every string sounds the
    /// capo's fret higher — x32010 at capo 3 is E♭ major, on the page, in the MIDI and the twin.</summary>
    [Fact]
    public void UnderACapo_TheShapeSoundsAboveTheCapo()
    {
        string book = Book("instrument guitar", "chord(Eb x32010)1 |", top: "layout { chordDiagrams guitar capo 3 }");
        Assert.Equal(new[] { 51, 55, 58, 63, 67 }, MidiPitches(book));   // E♭3 G3 B♭3 E♭4 G4
        Assert.DoesNotContain(Diagnostics(book), d => d.Severity != DiagnosticSeverity.Info);
        Assert.Contains("<ees\\5 g\\4 bes\\3 ees\\2 g\\1>1", new LilyPondExporter().Export(SyntaxTree.Parse(book)));
    }

    /// <summary>`<>` and `chord(…)` are the same node: the MeasureDurations and every chord arm
    /// see a chord that takes its time — the bar adds up with no warning.</summary>
    [Fact]
    public void ABarOfItems_AddsUp()
        => Assert.DoesNotContain(Diagnostics(Book("instrument guitar",
                "chord(C x32013)2 chord(G 320003)4 chord(Am x02210) |")),
            d => d.Severity != DiagnosticSeverity.Info);

    /// <summary>Owner's decision 2026-09-28: the shape is REQUIRED for now — `chord(C)` warns,
    /// naming the fix, and keeps its time as a SPACER so the bar still adds up.</summary>
    [Fact]
    public void NoShape_WarnsNamingTheFix_AndIsASpacer()
    {
        string book = Book("instrument guitar", "c'2 chord(C)2 |");
        var warning = Assert.Single(Diagnostics(book), d => d.Code == DiagnosticCodes.ShapeChordNoShape);
        Assert.Contains("write a shape: chord(C x32010)", warning.Message);
        Assert.Contains("spacer", warning.Message);
        Assert.DoesNotContain(Diagnostics(book), d => d.Code.StartsWith("LYS2", StringComparison.Ordinal));
        var items = Items(Collected(book));
        Assert.True(Assert.IsType<RestItem>(items[1]).IsSpacer);
        Assert.Equal(new[] { 60 + 12 - 12 }, MidiPitches(book));   // c' (C5 written) sounds C4; the spacer nothing
        Assert.Contains("s2", new LilyPondExporter().Export(SyntaxTree.Parse(book)));
        // The symbol's own default is the example when there is one.
        Assert.Contains("chord(Am x02210)",
            Assert.Single(Diagnostics(Book("instrument guitar", "chord(Am)1 |")),
                d => d.Code == DiagnosticCodes.ShapeChordNoShape).Message);
    }

    [Fact]
    public void OctaveMarksAfterTheItem_AreRefused_AndIgnored()
    {
        string book = Book("instrument guitar", "chord(C x32013)'1 |");
        var error = Assert.Single(Diagnostics(book), d => d.Code == DiagnosticCodes.ShapeChordOctaveMarks);
        Assert.Contains("remove the marks", error.Message);
        Assert.Equal(0, Item(book).ChordOctaveOffset);
        Assert.Equal(MidiPitches(Book("instrument guitar", "chord(C x32013)1 |")), MidiPitches(book));
    }

    [Fact]
    public void TheShapesDiagnosticsApply()
    {
        // A shape of the wrong length for every tuning: LYS1038 at the word, and no shape at all.
        var wrong = Diagnostics(Book("instrument guitar", "chord(C x32)1 |"));
        Assert.Contains(wrong, d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn && d.Message.Contains("'x32' has 3"));
        Assert.Contains(wrong, d => d.Code == DiagnosticCodes.ShapeChordNoShape);
        // A five-string shape is a real length — but not the guitar's.
        Assert.Contains("none of its shapes fits 'guitar' (6 strings)",
            Assert.Single(Diagnostics(Book("instrument guitar", "chord(C x3201)1 |")),
                d => d.Code == DiagnosticCodes.ShapeChordNoShape).Message);
        // The case and the dash errors.
        Assert.Contains(Diagnostics(Book("instrument guitar", "chord(C X32010)1 |")),
            d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn && d.Message.Contains("write 'x32010'"));
        Assert.Contains(Diagnostics(Book("instrument guitar", "chord(C x-32010-)1 |")),
            d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn && d.Message.Contains("starts or ends with '-'"));
        // LYS1039: the shape against the symbol, the fix spelled as the item.
        var mismatch = Assert.Single(Diagnostics(Book("instrument guitar", "chord(Cm x32010)1 |")),
            d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        Assert.Contains("chord(C x32010)", mismatch.Message);
        // No LYS1039 when the shape plays the symbol.
        Assert.DoesNotContain(Diagnostics(Book("instrument guitar", "chord(C x32013)1 |")),
            d => d.Code == DiagnosticCodes.ChordShapeMismatch);
    }

    [Fact]
    public void ChordIsReservedInMusic_APhraseCannotBeNamedIt()
    {
        Assert.Contains(Diagnostics("phrase chord { c4 d e f | }\n" + Book("instrument guitar", "chord |")),
            d => d.Code == DiagnosticCodes.PhraseNameUnreachable && d.Message.Contains("'chord'"));
        Assert.Contains(Diagnostics(Book("instrument guitar", "chord c1 |")),
            d => d.Code == DiagnosticCodes.ExpectedToken && d.Message.Contains("chord(C x32010)4"));
        // A PART may still be named chord: no music stream reads a part name.
        Assert.DoesNotContain(Diagnostics("""
            part chord { instrument guitar }
            section A { chord { c1 | } }
            form main { A }
            score main { staff chord }
            """), d => d.Severity == DiagnosticSeverity.Error);
    }

    // ================================================================ the pitches

    /// <summary>x32013 on the standard guitar, by hand: A string fret 3 = C3, D fret 2 = E3,
    /// G open = G3, B fret 1 = C4, high E fret 3 = G4; string numbers 5 4 3 2 1.</summary>
    [Fact]
    public void OnTheGuitar_TheShapeSoundsItsStrings()
    {
        var notes = Notes("C x32013", TuningType.Guitar);
        Assert.Equal("C3 E3 G3 C4 G4", Names(notes));
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, notes.Select(n => n.StringNumber));
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, notes.Select(n => n.SoundingMidi));
        // A guitar part (treble_8: written an octave up) writes them an octave up, and plays
        // them where they sound.
        string book = Book("instrument guitar", "chord(C x32013)1 |");
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, MidiPitches(book));
        var chord = Assert.IsType<ChordItem>(Items(Collected(book))[0]);
        Assert.Equal(new[] { 60, 64, 67, 72, 79 }, chord.Notes.Select(n => n.Midi).OrderBy(m => m));
        // With no instrument the guitar's strings are written where they sound.
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, MidiPitches(Book("clef treble", "chord(C x32013)1 |")));
        Assert.Equal(new[] { 48, 52, 55, 60, 67 },
            Assert.IsType<ChordItem>(Items(Collected(Book("clef treble", "chord(C x32013)1 |")))[0])
                .Notes.Select(n => n.Midi).OrderBy(m => m));
    }

    /// <summary>The ukulele (re-entrant G4 C4 E4 A4): 0003 is G4 C4 E4 C5 — its strings, not a
    /// guitar's; and the drop-D guitar's sixth string is D2.</summary>
    [Fact]
    public void OnAUkuleleAndADropDPart_TheirOwnStrings()
    {
        Assert.Equal(new[] { 60, 64, 67, 72 },
            MidiPitches(Book("instrument ukulele", "chord(C 0003)1 |")));
        Assert.Equal("G4 C4 E4 C5", Names(Notes("C 0003", TuningType.Ukulele)));
        Assert.Equal(new[] { 38, 45, 50, 57, 62, 66 },
            MidiPitches(Book("tuning guitardropd", "chord(D 000232)1 |")));
        // A six-string shape on the ukulele part: no shape for its four strings — a spacer.
        Assert.Contains(Diagnostics(Book("instrument ukulele", "chord(C x32010)1 |")),
            d => d.Code == DiagnosticCodes.ShapeChordNoShape && d.Message.Contains("'ukulele' (4 strings)"));
        Assert.Empty(MidiPitches(Book("instrument ukulele", "chord(C x32010)1 |")));
    }

    /// <summary>A chord tone is spelled as the symbol spells it (Cm7's third is E♭ and its seventh
    /// B♭, never D♯ / A♯); any other note in the written key.</summary>
    [Fact]
    public void Spelling_FollowsTheSymbolThenTheKey()
    {
        Assert.Equal("C3 C4 Eb4 Bb4", Names(Notes("Cm7 x3x546", TuningType.Guitar)));
        // x32012: F♯4 on the top string is no tone of C — the key spells it.
        Assert.Equal("F#4", Notes("C x32012", TuningType.Guitar, keySharps: 0)[^1].SoundingName);
        Assert.Equal("Gb4", Notes("C x32012", TuningType.Guitar, keySharps: -1)[^1].SoundingName);
        // The page spells what the item names: E♭ and B♭ print flats.
        var chord = Assert.IsType<ChordItem>(Items(Collected(Book("instrument guitar", "chord(Cm7 x3x546)1 |")))[0]);
        Assert.Equal(2, chord.Notes.Count(n => n.Accidental == "flat"));
    }

    // ================================================================ the frame

    /// <summary>Owner's decision 2026-09-28: the item hands on its LOWEST sounding note — in
    /// relative mode the next note is read from it; in absolute mode nothing changes. The item
    /// itself is where the strings are, whatever the frame.</summary>
    [Fact]
    public void TheFrame_AfterTheItem_IsItsLowestNote()
    {
        // g'' puts the frame high up; the item ignores it, and the d after it reads from the
        // item's lowest note (written C4 on a guitar part) — D4 written, D3 sounding.
        string relative = Book("instrument guitar", "g''1 | chord(C x32013)1 | d1 |");
        int[] played = MidiPitches(relative);
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, played[1..6]);
        Assert.Equal(50, played[^1]);
        // Without the item the same d reads from g'': a different pitch — the rule is observed.
        Assert.NotEqual(50, MidiPitches(Book("instrument guitar", "g''1 | c1 | d1 |"))[^1]);
        // The ukulele's lowest note is its C string, not its first (G) string.
        Assert.Equal(62, MidiPitches(Book("instrument ukulele", "chord(C 0003)1 | d1 |"))[^1]);
        // Absolute: nothing moves — d is D at the base octave, and the item the same notes.
        string absolute = Book("instrument guitar", "chord(C x32013)1 | d1 |", top: "octave absolute");
        int[] fixedPlayed = MidiPitches(absolute);
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, fixedPlayed[..5]);
        Assert.Equal(62 - 12, fixedPlayed[^1]);   // d = D4 written, D3 sounding
        // The page and MusicXML read the same frame.
        Assert.Equal("D4", (ResolvedPitches.ForFile(SyntaxTree.Parse(relative)) ?? [])[^1].Pitch);
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(relative)).ToXml().ToString();
        var last = Regex.Matches(xml, @"<step>(\w)</step>\s*<octave>(\d)</octave>").Last();
        Assert.Equal(("D", "4"), (last.Groups[1].Value, last.Groups[2].Value));
    }

    /// <summary>Owner's decision 2026-09-30: a phrase whose body OPENS with the item hands on the
    /// item's lowest note as written — what the item itself hands on — and the reference's marks
    /// do not move it (the item is absolute). Until then the anchor walk found no root on the
    /// item and anchored on the body's NEXT note, so editing the phrase's second bar moved the
    /// note after every reference (a' → G3, e' → G4).</summary>
    [Fact]
    public void APhraseOpeningWithTheItem_HandsOnTheItemsLowestNote()
    {
        static string Song(string second, string reference = "P")
            => Book("instrument guitar", $"g''1 | {reference} | g1 |",
                top: $"phrase P {{ chord(C x32013)1 | {second} | }}");
        static (string Page, int Midi, string Xml) After(string book)
        {
            var tree = SyntaxTree.Parse(book);
            var xml = Regex.Matches(new MusicXmlExporter().Export(tree).ToXml().ToString(),
                @"<step>(\w)</step>\s*<octave>(\d)</octave>").Last();
            return ((ResolvedPitches.ForFile(tree) ?? [])[^1].Pitch, MidiPitches(book)[^1],
                xml.Groups[1].Value + xml.Groups[2].Value);
        }
        // The item's lowest note is written C4 on a guitar part: g reads G3 (sounding G2, 43).
        foreach (string second in new[] { "a'1", "e'1", "f''1", "r1" })
            Assert.Equal(("G3", 43, "G3"), After(Song(second)));
        Assert.Equal(("G3", 43, "G3"), After(Song("e'1", "P'")));
        Assert.Equal(("G3", 43, "G3"), After(Song("e'1", "P,")));
        // A grace body naming such a phrase hands the same note on to the grace after it.
        string grace = Book("instrument guitar", "g''1 | grace { Q g16 } c1 |",
            top: "phrase Q { chord(C x32013)16 e'16 }");
        int[] played = MidiPitches(grace);
        Assert.Equal(43, played[^2]);
    }

    // ================================================================ strings and the tab

    [Fact]
    public void EveryNoteCarriesItsString_SoTheTabShowsTheShape()
    {
        string book = Book("instrument guitar", "chord(C x32013)1 |", "staff gt  tab gt");
        var score = Collected(book);
        var staff = Assert.IsType<ChordItem>(Items(score)[0]);
        Assert.Equal(new int?[] { 5, 4, 3, 2, 1 },
            staff.Notes.OrderBy(n => n.Midi).Select(n => n.StringNumber));
        var tab = Assert.IsType<ChordItem>(Items(score, tab: true)[0]);
        // The fret each string shows: the sounding pitch (written less the guitar's octave)
        // less the open string.
        int[] open = LilySharp.Core.Tablature.Tunings.Guitar;
        var frets = tab.Notes.OrderByDescending(n => n.StringNumber)
            .Select(n => n.Midi - 12 - open[6 - n.StringNumber!.Value]);
        Assert.Equal(new[] { 3, 2, 0, 1, 3 }, frets);
    }

    // ================================================================ @chord on the item

    /// <summary>The item makes NOTES only; a bare @chord on it takes the item's symbol and shape
    /// — the name and the diagram, as @chord(SYMBOL SHAPE) would; a symbol-less item names from
    /// its notes; an explicit @chord(…) is its own.</summary>
    [Fact]
    public void ABareAtChordOnTheItem_NamesItAndDrawsItsShape()
    {
        Assert.Empty(Collected(Book("instrument guitar", "chord(Cm7 x3x546)1 |")).ChordNames);
        string book = Book("instrument guitar", "chord(Cm7 x3x546)2@chord chord(x32010)2@chord |");
        var names = Collected(book).ChordNames.OrderBy(c => c.Timing.ToDouble()).ToList();
        Assert.Equal(new[] { "Cm7", "C" }, names.Select(n => n.ChordText));
        var tree = SyntaxTree.Parse(book);
        var laid = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        Assert.Contains(laid.ArticulationLayouts, a => a.Glyph == "frame:x3x546");
        Assert.Contains(laid.ArticulationLayouts, a => a.Glyph == "frame:x32010");
        // Explicit: its own words.
        Assert.Equal("G", Assert.Single(Collected(Book("instrument guitar", "chord(C x32013)1@chord(G) |")).ChordNames).ChordText);
        // No warning for the bare mark: it names what the item names.
        Assert.DoesNotContain(Diagnostics(book), d => d.Severity != DiagnosticSeverity.Info);
    }

    // ================================================================ the outputs

    [Fact]
    public void Midi_PlaysTheShape_TiesAndDynamicsAsOnAChord()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(
                Book("instrument guitar", "chord(C x32013)2~ chord(C x32013)2 | chord(G 320003)1@ff |")))
            .Tracks.SelectMany(t => t.Notes).ToList();
        // The tie extends each string: five notes of a whole bar, then G's six.
        Assert.Equal(11, notes.Count);
        Assert.All(notes.Take(5), n => Assert.Equal(notes.Last().DurationTicks, n.DurationTicks));
        Assert.All(notes.Skip(5), n => Assert.True(n.Velocity > notes[0].Velocity));
    }

    /// <summary>In a grace body and a tuplet the item is the same notes, and hands on the same
    /// frame, in all four outputs (the twin's nested buffers carry the part: measured, a grace
    /// body wrote the guitar's strings an octave under the page until they did).</summary>
    [Fact]
    public void InAGraceAndATuplet_TheSameNotes()
    {
        string book = Book("instrument guitar",
            "grace { chord(C x32013)8 } c'2 tuplet 3/2 { chord(C x32013)4 chord(G 320003) r } |");
        int[] played = MidiPitches(book);
        Assert.Equal(new[] { 48, 52, 55, 60, 67, 60 }, played[..6]);   // the c' after it: C5 written
        Assert.Equal(new[] { 48, 52, 55, 60, 67, 43, 47, 50, 55, 59, 67 }, played[6..]);
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();
        Assert.Equal(16, Regex.Matches(xml, @"<string>\d</string>").Count);
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        Assert.Contains(@"\grace { <c\5 e\4 g\3 c\2 g'\1>8 } c'2", ly);
        Assert.Contains(@"\tuplet 3/2 { <c,\5 e\4 g\3 c\2 g'\1>4 <g\6 b\5 d\4 g\3 b\2 g'\1>", ly);
    }

    [Fact]
    public void MusicXml_WritesTheNotesWithTheirStrings()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(
            Book("instrument guitar", "chord(Cm7 x3x546)1 |"))).ToXml().ToString();
        Assert.Equal(new[] { "5", "3", "2", "1" },
            Regex.Matches(xml, @"<string>(\d)</string>").Select(m => m.Groups[1].Value));
        Assert.Equal(new[] { "C4", "C5", "E-15", "B-15" },
            Regex.Matches(xml, @"<step>(\w)</step>(?:\s*<alter>(-?\d)</alter>)?\s*<octave>(\d)</octave>")
                .Select(m => m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value));
        Assert.Equal(3, Regex.Matches(xml, "<chord ?/>").Count);
    }

    /// <summary>The twin writes the notes out, lowest first, each with its string number, and its
    /// relative chain lands where Lily#'s does (LilyPond reads the next note from the chord's
    /// first note, which is the lowest). Compiled by LilyPond 2.26.0 by hand (2026-09-28).</summary>
    [Fact]
    public void TheTwin_WritesTheChordOutWithStringNumbers()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(
            Book("instrument guitar", "g''1 | chord(C x32013)1 | d1 | chord(Cm7 x3x546)1 |")));
        Assert.Contains(@"<c,,\5 e\4 g\3 c\2 g'\1>1", ly);
        Assert.Contains(@"d1 |", ly);
        Assert.Contains(@"<c\5 c'\3 ees\2 bes'\1>1", ly);   // read from the d, D4 written
        Assert.Contains(@"\omit StringNumber", ly);
        string fixedLy = new LilyPondExporter().Export(SyntaxTree.Parse(
            Book("instrument guitar", "chord(C x32013)1 |", top: "octave absolute")));
        Assert.Contains(@"<c\5 e\4 g\3 c'\2 g'\1>1", fixedLy);
    }

    // ================================================================ the editor

    private static string? HoverAt(string doc, string needle, int into = 2)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///shape-chord.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = doc, Version = 1, LanguageId = "lilysharp" },
        });
        int at = doc.IndexOf(needle, StringComparison.Ordinal) + into;
        int line = doc[..at].Count(ch => ch == '\n');
        int col = at - (doc.LastIndexOf('\n', at - 1) + 1);
        return server.Hover(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
        })?.Contents.Value;
    }

    [Fact]
    public void Hover_ListsTheSoundingNotesAndTheShape()
    {
        string doc = Book("instrument guitar", "chord(C x32013)2 chord(C)2 |");
        string? hover = HoverAt(doc, "chord(C x");
        Assert.NotNull(hover);
        Assert.Contains("**Chord from a shape** `C`", hover);
        Assert.Contains("guitar: `x32013` — C3  E3  G3  C4  G4", hover);
        Assert.Contains("nothing sounds", HoverAt(doc, "chord(C)"));
    }

    /// <summary>The hover spells a tone that is no chord tone as the key in force spells it, as the
    /// page does — x32012's top string is F♯ in C and G♭ under a flat key. Until 2026-09-30 the
    /// hover spelled it in C whatever the key.</summary>
    [Fact]
    public void Hover_SpellsANonChordToneInTheKey()
    {
        Assert.Contains("F♯4", HoverAt(Book("instrument guitar", "chord(C x32012)1 |"), "chord(C x"));
        string flat = Book("instrument guitar\n  key f major", "chord(C x32012)1 |");
        string? hover = HoverAt(flat, "chord(C x");
        Assert.Contains("G♭4", hover);
        Assert.DoesNotContain("F♯", hover);
        // …as the page prints the same note.
        var chord = Assert.IsType<ChordItem>(Items(Collected(flat))[0]);
        Assert.Equal(("flat", 0), (chord.Notes.Single(n => n.Accidental != null).Accidental,
            chord.Notes.Count(n => n.Accidental == "sharp")));
    }

    private static (string After, StepResponse Response) Step(string text, int caret, int dir)
    {
        var uri = new Uri("file:///shape-step.lys");
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 3 },
        });
        var response = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Selections = [new StepSelection { Start = caret, End = caret }],
            Direction = dir,
        });
        string after = text;
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            after = after[..e.Start] + e.NewText + after[e.End..];
        return (after, response);
    }

    /// <summary>Ctrl+Shift+Up/Down on the item steps its shape through the @chord order, on the
    /// PART's tuning, and sounds it; Down at the default keeps it (the shape is required).</summary>
    [Fact]
    public void TheStep_StepsTheItemsShape_AndSoundsIt()
    {
        string Doc(string words) => Book("instrument guitar", $"chord({words})1 |");
        string text = Doc("C");
        var (after, up) = Step(text, text.IndexOf("chord(C", StringComparison.Ordinal) + 7, +1);
        Assert.Equal(Doc("C x32010"), after);
        Assert.StartsWith("C: shape 1 of", up.Message);
        Assert.Equal(new[] { 48, 52, 55, 60, 64 }, up.Pitches);
        text = Doc("C x32010");
        var (next, _) = Step(text, text.IndexOf("x32010", StringComparison.Ordinal) + 2, +1);
        Assert.NotEqual(text, next);
        Assert.Matches(@"chord\(C [x0-9-]+\)1", next);
        var (same, down) = Step(text, text.IndexOf("x32010", StringComparison.Ordinal) + 2, -1);
        Assert.Equal(text, same);
        Assert.False(down.Fallback);
        Assert.Contains("keeps its shape", down.Message);
        // On a ukulele part it steps the ukulele's shapes.
        string uke = Book("instrument ukulele", "chord(C)1 |");
        Assert.Equal(Book("instrument ukulele", "chord(C 0003)1 |"),
            Step(uke, uke.IndexOf("chord(C", StringComparison.Ordinal) + 7, +1).After);
    }

    [Fact]
    public void Completion_InsideTheItem_IsTheChordSymbols()
    {
        Assert.True(LilySharpLanguageServer.IsInsideShapeChordItem("gt { chord(", 11));
        Assert.True(LilySharpLanguageServer.IsInsideChordAnnotation("gt { chord(C ", 13));
        Assert.False(LilySharpLanguageServer.IsInsideShapeChordItem("gt { c4@chord(", 14));
        Assert.False(LilySharpLanguageServer.IsInsideShapeChordItem("gt { chord(C) ", 14));
    }
}
