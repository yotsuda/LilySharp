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
using LilySharp.Core.Editing;
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
/// <c>diagram(SYMBOL SHAPE)</c> — a chord FROM A SHAPE (owner's decisions 2026-09-28, HANDOFF §2 K5):
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
        form { A }
        score { {{score}} }
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
    [InlineData("diagram(C x32013)1", "C", "x32013", 1, 0, false)]
    [InlineData("diagram(Cm7 x3x546)2@chord", "Cm7", "x3x546", 2, 0, false)]
    [InlineData("diagram(C xx-10-12-13-12)4.~", "C", "xx-10-12-13-12", 4, 1, true)]
    [InlineData("diagram(x32013)8", "x32013", null, 8, 0, false)]
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

    /// <summary>Owner's decision 2026-10-31 (docs/chord-syntax-design.md): the word says what is
    /// seen. <c>chord(…)</c> is the item that prints its NAME and the diagram of the shape written
    /// with it; <c>diagram(…)</c> the diagram alone. Both sound the same notes; spread in a
    /// group, the name and the diagram stand once; the page, the twin and the MusicXML agree.</summary>
    [Fact]
    public void TheChordItem_PrintsItsName_AndTheDiagramOfItsShape()
    {
        string book = Book("instrument guitar", "chord(F 133211)2 diagram(F 133211)2 |");
        Assert.True(Item(book).ShowsName);
        Assert.DoesNotContain(Diagnostics(book), d => d.Severity != DiagnosticSeverity.Info);
        Assert.Equal(new[] { "F" }, Collected(book).ChordNames.Select(n => n.ChordText));
        Assert.Equal(new[] { "133211", "133211" }, Frames(book));
        Assert.Equal(MidiPitches(Book("instrument guitar", "diagram(F 133211)2 diagram(F 133211)2 |")), MidiPitches(book));
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        Assert.Contains("\\new ChordNames", ly);
        Assert.Equal(2, Regex.Matches(ly, "fret-diagram-terse").Count);
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();
        Assert.Single(Regex.Matches(xml, "<harmony"));
        Assert.Single(Regex.Matches(xml, "<frame>"));

        // A shape alone names itself from its notes.
        Assert.Equal("Am", Assert.Single(Collected(Book("instrument guitar", "chord(x02210)1 |")).ChordNames).ChordText);
        // Spread in a group: once.
        string spread = Book("instrument guitar", "<< chord(G 320003) >>1 |");
        Assert.Single(Collected(spread).ChordNames);
        Assert.Single(Frames(spread));
        Assert.Single(Regex.Matches(new MusicXmlExporter().Export(SyntaxTree.Parse(spread)).ToXml().ToString(), "<harmony"));
        // An @chord on it names it once, as written.
        Assert.Single(Collected(Book("instrument guitar", "chord(Am x02210)1@chord |")).ChordNames);
    }

    private static List<string?> Frames(string book)
    {
        var tree = SyntaxTree.Parse(book);
        return [.. LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).Articulations
            .Where(a => a.Type == ArticulationType.FretFrame).OrderBy(a => a.SourcePosition).Select(a => a.FrameSpec)];
    }

    /// <summary>Owner's decision 2026-10-30: the item draws its own shape's diagram — on the page
    /// and in the twin — unless an <c>@chord</c>/<c>@diagram</c> on it draws one already, or the
    /// score turns diagrams off.</summary>
    [Fact]
    public void TheItem_DrawsItsShape_OnceAndUnlessDiagramsAreOff()
    {
        string book = Book("instrument guitar", "diagram(C x32010)2 diagram(x02210)2 |");
        Assert.Equal(new[] { "x32010", "x02210" }, Frames(book));
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        Assert.Contains("\\fret-diagram-terse \"x;3;2;o;1;o;\"", ly);
        Assert.Contains("\\fret-diagram-terse \"x;o;2;2;1;o;\"", ly);
        Assert.Single(Frames(Book("instrument guitar", "diagram(Am x02210)1@chord |")));
        // Spread in << >>: once, over its first (lowest) note — on the page and in the twin.
        string spread = Book("instrument guitar", "<< diagram(G 320003) >>1 |");
        Assert.Equal(new[] { "320003" }, Frames(spread));
        string spreadLy = new LilyPondExporter().Export(SyntaxTree.Parse(spread));
        Assert.Single(Regex.Matches(spreadLy, "fret-diagram-terse"));
        Assert.Contains("g4\\6-\\tweak", spreadLy);
        Assert.Empty(Frames(Book("instrument guitar", "diagram(C x32010)1 |", top: "layout { chordDiagrams none }\n")));
    }

    /// <summary>Owner's decisions 2026-10-31 (docs/chord-syntax-design.md Q1): with no shape
    /// written the item sounds. A chord(…) item shows a name and no diagram, so it plays its chord's
    /// notes close, root up, by middle C — on every part, a guitar's too (revised the same day: it
    /// played six strings no diagram showed). A diagram(…) item on a fretted part plays, and draws,
    /// the usual shape of its instrument, pressed above the capo.</summary>
    [Fact]
    public void WithNoShape_TheItemSoundsTheUsualVoicing()
    {
        string guitar = Book("instrument guitar", "chord(F)2 diagram(G)2 |");
        Assert.DoesNotContain(Diagnostics(guitar), d => d.Code == DiagnosticCodes.ShapeChordNoShape);
        // F A C (F3 A3 C4 sounding: the guitar is written an octave up), then G's 320003.
        Assert.Equal(new[] { 53, 57, 60, 43, 47, 50, 55, 59, 67 }, MidiPitches(guitar));
        Assert.Equal(new[] { "F" }, Collected(guitar).ChordNames.Select(n => n.ChordText));
        Assert.Equal(new[] { "320003" }, Frames(guitar));      // chord(F) writes no shape: no diagram
        // No string numbers on the close notes: the tab chooses their strings.
        Assert.Contains("<f a c>2 ", new LilyPondExporter().Export(SyntaxTree.Parse(guitar)));

        string capo = Book("instrument guitar", "chord(Eb)1 diagram(Eb)1 |", top: "capo 3");
        // chord(Eb): E♭ G B♭ close; diagram(Eb): C's x32010 pressed above the capo.
        Assert.Equal(new[] { 51, 55, 58, 51, 55, 58, 63, 67 }, MidiPitches(capo));

        string piano = Book("clef treble", "chord(F)2 diagram(G)2 | chord(C/E)1 |");
        Assert.DoesNotContain(Diagnostics(piano), d => d.Code == DiagnosticCodes.ShapeChordNoShape);
        // chord(F) F A C close; diagram(G) the guitar's 320003 it draws (it plays what it shows, on any
        // part — owner's review 2026-10-31); chord(C/E) E below C E G.
        Assert.Equal(new[] { 65, 69, 72, 43, 47, 50, 55, 59, 67, 52, 60, 64, 67 }, MidiPitches(piano));
        Assert.DoesNotContain("\\1", new LilyPondExporter().Export(SyntaxTree.Parse(piano)).Split("chordmode")[0]
            .Split('\n').Where(l => l.Contains("<f")).DefaultIfEmpty("").First());
    }

    /// <summary>Under a capo (2026-09-29) the shape is pressed above it: every string sounds the
    /// capo's fret higher — x32010 at capo 3 is E♭ major, on the page, in the MIDI and the twin.</summary>
    [Fact]
    public void UnderACapo_TheShapeSoundsAboveTheCapo()
    {
        string book = Book("instrument guitar", "diagram(Eb x32010)1 |", top: "capo 3");
        Assert.Equal(new[] { 51, 55, 58, 63, 67 }, MidiPitches(book));   // E♭3 G3 B♭3 E♭4 G4
        Assert.DoesNotContain(Diagnostics(book), d => d.Severity != DiagnosticSeverity.Info);
        Assert.Contains("<ees\\5 g\\4 bes\\3 ees\\2 g\\1>1", new LilyPondExporter().Export(SyntaxTree.Parse(book)));
    }

    /// <summary>`<>` and `diagram(…)` are the same node: the MeasureDurations and every chord arm
    /// see a chord that takes its time — the bar adds up with no warning.</summary>
    [Fact]
    public void ABarOfItems_AddsUp()
        => Assert.DoesNotContain(Diagnostics(Book("instrument guitar",
                "diagram(C x32013)2 diagram(G 320003)4 diagram(Am x02210) |")),
            d => d.Severity != DiagnosticSeverity.Info);

    /// <summary>A shape the part's tuning cannot play — written for another instrument only — warns,
    /// naming the fix, and keeps its time as a SPACER so the bar still adds up. (A shape left out
    /// altogether is the usual one since 2026-10-31: WithNoShape_TheItemSoundsTheUsualVoicing.)</summary>
    [Fact]
    public void NoShape_WarnsNamingTheFix_AndIsASpacer()
    {
        string book = Book("instrument guitar", "c'2 diagram(C 0003)2 |");
        var warning = Assert.Single(Diagnostics(book), d => d.Code == DiagnosticCodes.ShapeChordNoShape);
        Assert.Contains("write a shape: diagram(C x32010)", warning.Message);
        Assert.Contains("spacer", warning.Message);
        Assert.DoesNotContain(Diagnostics(book), d => d.Code.StartsWith("LYS2", StringComparison.Ordinal));
        var items = Items(Collected(book));
        Assert.True(Assert.IsType<RestItem>(items[1]).IsSpacer);
        Assert.Equal(new[] { 60 + 12 - 12 }, MidiPitches(book));   // c' (C5 written) sounds C4; the spacer nothing
        Assert.Contains("s2", new LilyPondExporter().Export(SyntaxTree.Parse(book)));
        // The symbol's own default is the example when there is one.
        Assert.Contains("diagram(Am x02210)",
            Assert.Single(Diagnostics(Book("instrument guitar", "diagram(Am 2000)1 |")),
                d => d.Code == DiagnosticCodes.ShapeChordNoShape).Message);
    }

    [Fact]
    public void OctaveMarksAfterTheItem_AreRefused_AndIgnored()
    {
        string book = Book("instrument guitar", "diagram(C x32013)'1 |");
        var error = Assert.Single(Diagnostics(book), d => d.Code == DiagnosticCodes.ShapeChordOctaveMarks);
        Assert.Contains("remove the marks", error.Message);
        Assert.Equal(0, Item(book).ChordOctaveOffset);
        Assert.Equal(MidiPitches(Book("instrument guitar", "diagram(C x32013)1 |")), MidiPitches(book));
    }

    [Fact]
    public void TheShapesDiagnosticsApply()
    {
        // A shape of the wrong length for every tuning: LYS1038 at the word, and no shape at all.
        var wrong = Diagnostics(Book("instrument guitar", "diagram(C x32)1 |"));
        Assert.Contains(wrong, d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn && d.Message.Contains("'x32' has 3"));
        Assert.Contains(wrong, d => d.Code == DiagnosticCodes.ShapeChordNoShape);
        // A five-string shape is a real length — but not the guitar's.
        Assert.Contains("none of its shapes fits 'guitar' (6 strings)",
            Assert.Single(Diagnostics(Book("instrument guitar", "diagram(C x3201)1 |")),
                d => d.Code == DiagnosticCodes.ShapeChordNoShape).Message);
        // The case and the dash errors.
        Assert.Contains(Diagnostics(Book("instrument guitar", "diagram(C X32010)1 |")),
            d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn && d.Message.Contains("write 'x32010'"));
        Assert.Contains(Diagnostics(Book("instrument guitar", "diagram(C x-32010-)1 |")),
            d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn && d.Message.Contains("starts or ends with '-'"));
        // LYS1039: the shape against the symbol, the fix spelled as the item.
        var mismatch = Assert.Single(Diagnostics(Book("instrument guitar", "diagram(Cm x32010)1 |")),
            d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        Assert.Contains("diagram(C x32010)", mismatch.Message);
        // No LYS1039 when the shape plays the symbol.
        Assert.DoesNotContain(Diagnostics(Book("instrument guitar", "diagram(C x32013)1 |")),
            d => d.Code == DiagnosticCodes.ChordShapeMismatch);
    }

    [Fact]
    public void DiagramIsReservedInMusic_APhraseCannotBeNamedIt()
    {
        Assert.Contains(Diagnostics("phrase diagram { c4 d e f | }\n" + Book("instrument guitar", "diagram |")),
            d => d.Code == DiagnosticCodes.PhraseNameUnreachable && d.Message.Contains("'diagram'"));
        Assert.Contains(Diagnostics(Book("instrument guitar", "diagram c1 |")),
            d => d.Code == DiagnosticCodes.ExpectedToken && d.Message.Contains("diagram(C x32010)4"));
        // A PART may still be named chord: no music stream reads a part name.
        Assert.DoesNotContain(Diagnostics("""
            part chord { instrument guitar }
            section A { chord { c1 | } }
            form { A }
            score { staff chord }
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
        // A guitar part (treble_8) writes them where they sound, as LilyPond's treble_8 does
        // (owner's decision 2026-10-28), and plays them there.
        string book = Book("instrument guitar", "diagram(C x32013)1 |");
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, MidiPitches(book));
        var chord = Assert.IsType<ChordItem>(Items(Collected(book))[0]);
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, chord.Notes.Select(n => n.Midi).OrderBy(m => m));
        // With no instrument the guitar's strings are written where they sound.
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, MidiPitches(Book("clef treble", "diagram(C x32013)1 |")));
        Assert.Equal(new[] { 48, 52, 55, 60, 67 },
            Assert.IsType<ChordItem>(Items(Collected(Book("clef treble", "diagram(C x32013)1 |")))[0])
                .Notes.Select(n => n.Midi).OrderBy(m => m));
    }

    /// <summary>The ukulele (re-entrant G4 C4 E4 A4): 0003 is G4 C4 E4 C5 — its strings, not a
    /// guitar's; and the drop-D guitar's sixth string is D2.</summary>
    [Fact]
    public void OnAUkuleleAndADropDPart_TheirOwnStrings()
    {
        Assert.Equal(new[] { 60, 64, 67, 72 },
            MidiPitches(Book("instrument ukulele", "diagram(C 0003)1 |")));
        Assert.Equal("G4 C4 E4 C5", Names(Notes("C 0003", TuningType.Ukulele)));
        Assert.Equal(new[] { 38, 45, 50, 57, 62, 66 },
            MidiPitches(Book("tuning guitarDropD", "diagram(D 000232)1 |")));
        // A six-string shape on the ukulele part: no shape for its four strings — a spacer.
        Assert.Contains(Diagnostics(Book("instrument ukulele", "diagram(C x32010)1 |")),
            d => d.Code == DiagnosticCodes.ShapeChordNoShape && d.Message.Contains("'ukulele' (4 strings)"));
        Assert.Empty(MidiPitches(Book("instrument ukulele", "diagram(C x32010)1 |")));
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
        var chord = Assert.IsType<ChordItem>(Items(Collected(Book("instrument guitar", "diagram(Cm7 x3x546)1 |")))[0]);
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
        // item's lowest note (C3: treble_8 is written at sounding pitch since the owner's
        // decision 2026-10-28) — D3.
        string relative = Book("instrument guitar", "g''1 | diagram(C x32013)1 | d1 |");
        int[] played = MidiPitches(relative);
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, played[1..6]);
        Assert.Equal(50, played[^1]);
        // Without the item the same d reads from g'': a different pitch — the rule is observed.
        Assert.NotEqual(50, MidiPitches(Book("instrument guitar", "g''1 | c1 | d1 |"))[^1]);
        // The ukulele's lowest note is its C string, not its first (G) string.
        Assert.Equal(62, MidiPitches(Book("instrument ukulele", "diagram(C 0003)1 | d1 |"))[^1]);
        // Absolute: nothing moves — d is D at the base octave, and the item the same notes.
        string absolute = Book("instrument guitar", "diagram(C x32013)1 | d1 |", top: "octave absolute");
        int[] fixedPlayed = MidiPitches(absolute);
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, fixedPlayed[..5]);
        Assert.Equal(62 - 12, fixedPlayed[^1]);   // d = D3, written and sounding
        // The page and MusicXML read the same frame (MusicXML writes it under the guitar's
        // treble_8 clef, which is the octave it sounds: D3).
        Assert.Equal("D3", (ResolvedPitches.ForFile(SyntaxTree.Parse(relative)) ?? [])[^1].Pitch);
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(relative)).ToXml().ToString();
        var last = Regex.Matches(xml, @"<step>(\w)</step>\s*<octave>(\d)</octave>").Last();
        Assert.Equal(("D", "3"), (last.Groups[1].Value, last.Groups[2].Value));
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
                top: $"phrase P {{ diagram(C x32013)1 | {second} | }}");
        static (string Page, int Midi, string Xml) After(string book)
        {
            var tree = SyntaxTree.Parse(book);
            var xml = Regex.Matches(new MusicXmlExporter().Export(tree).ToXml().ToString(),
                @"<step>(\w)</step>\s*<octave>(\d)</octave>").Last();
            return ((ResolvedPitches.ForFile(tree) ?? [])[^1].Pitch, MidiPitches(book)[^1],
                xml.Groups[1].Value + xml.Groups[2].Value);
        }
        // The item's lowest note is C3 on a guitar part (treble_8 is written at sounding pitch,
        // owner's decision 2026-10-28): g reads G2 (43), which is what MusicXML writes too.
        foreach (string second in new[] { "a'1", "e'1", "f''1", "r1" })
            Assert.Equal(("G2", 43, "G2"), After(Song(second)));
        Assert.Equal(("G2", 43, "G2"), After(Song("e'1", "P'")));
        Assert.Equal(("G2", 43, "G2"), After(Song("e'1", "P,")));
        // A grace body naming such a phrase hands the same note on to the grace after it.
        string grace = Book("instrument guitar", "g''1 | grace { Q g16 } c1 |",
            top: "phrase Q { diagram(C x32013)16 e'16 }");
        int[] played = MidiPitches(grace);
        Assert.Equal(43, played[^2]);
    }

    // ================================================================ in << >>

    /// <summary>Owner's decision 2026-09-30: a <c>diagram(…)</c> member of <c>&lt;&lt; &gt;&gt;</c>
    /// is SPREAD — its notes, lowest first, each one member of the broken chord, dividing the
    /// group's total with the other members. Until then the parser refused it.</summary>
    [Fact]
    public void InAGroup_TheShapeIsSpread_LowestFirst()
    {
        // x32013 on a guitar: C3 E3 G3 C4 G4 sounding, five eighths of a quintuplet.
        string alone = Book("instrument guitar", "<< diagram(C x32013) >>2 r2 |");
        Assert.DoesNotContain(Diagnostics(alone), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, MidiPitches(alone));
        var notes = Items(Collected(alone)).OfType<ChordItem>().ToList();
        Assert.Equal(5, notes.Count);
        Assert.All(notes, c => Assert.Single(c.Notes));
        Assert.Equal(new int?[] { 5, 4, 3, 2, 1 }, notes.Select(c => c.Notes[0].StringNumber));
        Assert.Equal(new[] { 48, 52, 55, 60, 67 }, notes.Select(c => c.Notes[0].Midi));   // written where they sound (2026-10-28)
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(alone)).ToXml().ToString();
        Assert.Equal(5, Regex.Matches(xml, @"<actual-notes>5</actual-notes>").Count);
        Assert.Equal(new[] { "5", "4", "3", "2", "1" },
            Regex.Matches(xml, @"<string>(\d)</string>").Select(m => m.Groups[1].Value));
        // The item's diagram stands on its first note (2026-10-30).
        string aloneLy = new LilyPondExporter().Export(SyntaxTree.Parse(alone));
        Assert.Contains(@"\tuplet 5/4 { c8\5-", aloneLy);
        Assert.Contains(@" e8\4 g8\3 c8\2 g'8\1 }", aloneLy);

        // Mixed: c, then G's six strings (its share dot on its LAST note), then e — nine shares;
        // e stacks above the group's root c, and the d after the group reads from it.
        string mixed = Book("instrument guitar", "<< c diagram(G 320003) . e >>2 d2 |");
        int[] played = MidiPitches(mixed);
        Assert.Equal(new[] { 48, 43, 47, 50, 55, 59, 67, 52, 50 }, played);
        var mixedItems = Items(Collected(mixed));
        Assert.Equal(new Fraction(1, 9), mixedItems[6].Duration);   // the dotted last G note: two of nine shares of the half
        Assert.Equal(new Fraction(1, 18), mixedItems[5].Duration);
        // A shape first is the root: the group's frame is its lowest note, as the item's is.
        Assert.Equal(new[] { 43, 47, 50, 55, 59, 67, 48 },
            MidiPitches(Book("instrument guitar", "<< diagram(G 320003) >>2 c2 |")));
    }

    /// <summary>The spread's edges: a shape with no notes on the tuning is a silence of its share
    /// (LYS1040), a member's slur marks land on the spread's first and last notes, a bare
    /// <c>@chord</c> names the notes it sounds, and a duration glued to the member is refused.</summary>
    [Fact]
    public void InAGroup_TheSpreadsEdges()
    {
        string unshaped = Book("instrument guitar", "<< c diagram(C 0003) e >>2 r2 |");
        Assert.Equal(new[] { 48, 52 }, MidiPitches(unshaped));
        var items = Items(Collected(unshaped));
        Assert.True(Assert.IsType<RestItem>(items[1]).IsSpacer);
        Assert.Contains(@"\tuplet 3/2 { c4 s4 e4 }", new LilyPondExporter().Export(SyntaxTree.Parse(unshaped)));

        var slurred = Items(Collected(Book("instrument guitar", "<< diagram(C x32010)( g) >>2 r2 |")));
        Assert.True(Assert.IsType<ChordItem>(slurred[0]).HasSlurStart);
        Assert.False(Assert.IsType<ChordItem>(slurred[4]).HasSlurStart);
        Assert.True(Assert.IsType<NoteItem>(slurred[5]).HasSlurEnd);

        var named = Collected(Book("instrument guitar", "<< diagram(x02210) >>2@chord r2 |")).ChordNames;
        Assert.Equal("Am", Assert.Single(named).ChordText);

        Assert.Contains(Diagnostics(Book("instrument guitar", "<< diagram(C x32010)4 >>2 r2 |")),
            d => d.Code == DiagnosticCodes.DurationInsideChord);

        // The octave-mode conversion keeps every note where it was, both ways.
        string mixed = Book("instrument guitar", "<< diagram(G 320003) c e >>2 d2 | << b diagram(C x32010) >>1 |");
        var absolute = LilySharp.Core.Editing.OctaveModeConverter.Convert(mixed, LilySharp.Core.Editing.OctaveMode.Absolute);
        Assert.Null(absolute.Error);
        Assert.Equal(MidiPitches(mixed), MidiPitches(absolute.NewText!));
        var back = LilySharp.Core.Editing.OctaveModeConverter.Convert(absolute.NewText!, LilySharp.Core.Editing.OctaveMode.Relative);
        Assert.Null(back.Error);
        Assert.Equal(MidiPitches(mixed), MidiPitches(back.NewText!));
    }

    // ================================================================ strings and the tab

    [Fact]
    public void EveryNoteCarriesItsString_SoTheTabShowsTheShape()
    {
        string book = Book("instrument guitar", "diagram(C x32013)1 |", "staff gt  tab gt");
        var score = Collected(book);
        var staff = Assert.IsType<ChordItem>(Items(score)[0]);
        Assert.Equal(new int?[] { 5, 4, 3, 2, 1 },
            staff.Notes.OrderBy(n => n.Midi).Select(n => n.StringNumber));
        var tab = Assert.IsType<ChordItem>(Items(score, tab: true)[0]);
        // The fret each string shows: the sounding pitch (the written one: treble_8 is written at
        // sounding pitch since the owner's decision 2026-10-28) less the open string.
        int[] open = LilySharp.Core.Tablature.Tunings.Guitar;
        var frets = tab.Notes.OrderByDescending(n => n.StringNumber)
            .Select(n => n.Midi - open[6 - n.StringNumber!.Value]);
        Assert.Equal(new[] { 3, 2, 0, 1, 3 }, frets);
    }

    // ================================================================ @chord on the item

    /// <summary>The item makes NOTES only; a bare @chord on it takes the item's symbol and shape
    /// — the name and the diagram, as @chord(SYMBOL SHAPE) would; a symbol-less item names from
    /// its notes; an explicit @chord(…) is its own.</summary>
    [Fact]
    public void ABareAtChordOnTheItem_NamesItAndDrawsItsShape()
    {
        Assert.Empty(Collected(Book("instrument guitar", "diagram(Cm7 x3x546)1 |")).ChordNames);
        string book = Book("instrument guitar", "diagram(Cm7 x3x546)2@chord diagram(x32010)2@chord |");
        var names = Collected(book).ChordNames.OrderBy(c => c.Timing.ToDouble()).ToList();
        Assert.Equal(new[] { "Cm7", "C" }, names.Select(n => n.ChordText));
        var tree = SyntaxTree.Parse(book);
        var laid = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        Assert.Contains(laid.ArticulationLayouts, a => a.Glyph == "frame:x3x546");
        Assert.Contains(laid.ArticulationLayouts, a => a.Glyph == "frame:x32010");
        // Explicit: its own words.
        Assert.Equal("G", Assert.Single(Collected(Book("instrument guitar", "diagram(C x32013)1@chord(G) |")).ChordNames).ChordText);
        // No warning for the bare mark: it names what the item names.
        Assert.DoesNotContain(Diagnostics(book), d => d.Severity != DiagnosticSeverity.Info);
    }

    /// <summary>An item's diagram stands where the chord changes, and again at each system's head
    /// (owner's decision 2026-10-31, docs/chord-syntax-design.md Q2): the page drops a repeat on
    /// its own breaks; the twin asks LilyPond's breaks the same question.</summary>
    [Fact]
    public void AnItemsDiagram_StandsWhereTheChordChanges_AndAtEachSystemsHead()
    {
        string book = Book("instrument guitar",
            "chord(F 133211)4 chord(F 133211)4 diagram(G 320003)4 diagram(G 320003)4 | diagram(G 320003)1 | break "
            + "diagram(G 320003)2 c'2@chord(G) | diagram(G 320003)1 |");
        var tree = SyntaxTree.Parse(book);
        var collected = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var frames = collected.Articulations.Where(a => a.Type == ArticulationType.FretFrame)
            .OrderBy(a => a.SourcePosition).ToList();
        // Marked by the collector: the F and G repeats, bars back; a name between ends the run.
        Assert.Equal(new[] { -1, 0, -1, 0, 1, 1, -1 }, frames.Select(a => a.RepeatsDiagramBack));
        // The page: the change, and the head of the second system — not the repeats on a system.
        var laid = new LayoutEngine().Layout(collected);
        Assert.Equal(new[] { "frame:133211", "frame:320003", "frame:320003", "frame:320003" },
            laid.ArticulationLayouts.Where(a => a.Glyph.StartsWith("frame:")).OrderBy(a => a.SourceIndex)
                .Select(a => a.Glyph));
        // The names likewise: the repeated F goes; the diagram(…) items print none, the @chord(G) is its own.
        Assert.Equal(new[] { -1, 0, -1 }, collected.ChordNames.OrderBy(c => c.SourcePosition).Select(c => c.RepeatsNameBack));
        Assert.Equal(new[] { "F", "G" }, laid.ChordNameLayouts.OrderBy(c => c.SourcePosition).Select(c => c.ChordText));
        // The twin: each repeat asks whether the diagram (or name) it repeats is on its system.
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("#(define (lysRepeatedDiagram from)", ly);
        Assert.Equal(4, Regex.Matches(ly, "-\\\\tweak after-line-breaking #\\(lysRepeatedDiagram \\d+").Count);
        Assert.Equal(4, Regex.Matches(ly, "-\\\\tweak before-line-breaking #\\(lysItemDiagram \\d+").Count);
        Assert.Single(Regex.Matches(ly, "ChordName.after-line-breaking = #\\(lysRepeatedDiagram -\\d+"));
        Assert.Single(Regex.Matches(ly, "ChordName.before-line-breaking = #\\(lysItemDiagram -\\d+"));
        // None repeats: none of it.
        Assert.DoesNotContain("lysItemDiagram", new LilyPondExporter().Export(SyntaxTree.Parse(
            Book("instrument guitar", "chord(F 133211)2 chord(G 320003)2 |"))));
    }

    /// <summary>A diagram over a &lt;&lt; &gt;&gt; group belongs to the group: its box is cleared by the
    /// column after the group, not by the group's second note, so the spread notes keep the
    /// spacing the bare group has — stretched alike where the box needs the room (owner's report
    /// 2026-10-31: `&lt;&lt; diagram(G 320003) &gt;&gt;1` had its first gap twice the others).</summary>
    [Theory]
    [InlineData("<< diagram(G 320003) >>1 |", null)]
    [InlineData("<< diagram(G 320003) >>2 c'4 d'4 |", null)]
    [InlineData("<< g, b d g b g >>2@diagram(320003) c'4 d'4 |", "<< g, b d g b g >>2 c'4 d'4 |")]
    [InlineData("<< g, b d g b g >>2@chord(G 320003) c'4 d'4 |", "<< g, b d g b g >>2@chord(G) c'4 d'4 |")]
    public void ADiagramOverASpreadGroup_LeavesTheGroupEvenlySpaced(string music, string? bare)
    {
        static (List<double> Heads, ScoreLayout Laid) Heads(string music)
        {
            var tree = SyntaxTree.Parse(Book("instrument guitar", music, top: "octave absolute\n"));
            Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            var laid = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
            var measure = laid.AllSystems[0].Measures[0];
            return ([.. measure.Items.Select(i => measure.X + i.X)], laid);
        }
        var (with, laid) = Heads(music);
        var gaps = Enumerable.Range(1, 5).Select(i => with[i] - with[i - 1]).ToList();
        if (bare is null)
        {
            // The spread item's six notes: even, to the eye (the bug read 4.48 then 1.9s).
            Assert.True(gaps.Max() - gaps.Min() < 0.3, $"the group's gaps: {string.Join(" ", gaps.Select(g => g.ToString("F2")))}");
        }
        else
        {
            // Against the same group with no diagram: the gaps grow by ONE factor (1 where the
            // group is already wide enough), never the first alone.
            var (plain, _) = Heads(bare);
            Assert.Equal(plain.Count, with.Count);
            var ratios = Enumerable.Range(1, 5).Select(i => gaps[i - 1] / (plain[i] - plain[i - 1])).ToList();
            Assert.True(ratios[0] >= 0.999, $"the group is not squeezed: {ratios[0]:F3}");
            Assert.All(ratios, r => Assert.InRange(r, ratios[0] - 0.03, ratios[0] + 0.03));
        }
        // The box still clears what follows the group: the frame ends before the c' stands.
        var frame = Assert.Single(laid.ArticulationLayouts, a => a.Glyph.StartsWith("frame:"));
        if (with.Count > 6)
            Assert.True(frame.X + frame.Ink.Right <= with[6], $"the frame ends at {frame.X + frame.Ink.Right:F3}, the c' stands at {with[6]:F3}");
    }

    /// <summary>Right-click → "Convert to chord notes" (owner's request 2026-10-31): the item
    /// becomes the &lt;…&gt; chord it plays, octave marks read off the walk, what it showed kept
    /// as a bare @chord (owner's decision the same day: `&lt;f a c&gt;1@chord`, not `@chord(F)`;
    /// the symbol goes in only when the bare mark would print another name) — and the file
    /// sounds the same.</summary>
    [Fact]
    public void AnItem_ConvertsToTheChordItPlays_AndSoundsTheSame()
    {
        static string Convert(string book, string at)
        {
            var r = ShapeChordConverter.ToNotes(book, book.IndexOf(at, StringComparison.Ordinal));
            Assert.True(r.Replacement != null, r.Error);
            return r.Replacement;
        }
        string guitar = Book("instrument guitar", "c4 diagram(133211)4 chord(F 133211)4 e4 | chord(x32010)2 g2 |");
        // Relative mode: each member read from the chord's first note; no diagram carried over,
        // a chord(…) item's name kept as the bare mark (the notes name it F, as the item did).
        Assert.Equal(@"<f,\6 c\5 f'\4 a'\3 c'\2 f''\1>4", Convert(guitar, "diagram("));
        Assert.Equal(@"<f\6 c\5 f'\4 a'\3 c'\2 f''\1>4@chord", Convert(guitar, "chord(F"));
        Assert.EndsWith(">2@chord", Convert(guitar, "chord(x"));
        // A piano's usual voicing, no string numbers; the name kept.
        string piano = Book("clef treble", "c'4 chord(C)4 chord(G7)2 |");
        Assert.Equal("<c e g>4@chord", Convert(piano, "chord(C"));
        Assert.Equal("<g b d f>2@chord", Convert(piano, "chord(G7"));
        // The page prints the same name after as before — the bare mark's reading of the notes.
        string converted = ShapeChordConverter.ToNotes(piano, piano.IndexOf("chord(G7", StringComparison.Ordinal)) is { Replacement: { } r, Start: var s, End: var e }
            ? piano[..s] + r + piano[e..] : throw new InvalidOperationException();
        Assert.Equal(Collected(piano).ChordNames.OrderBy(c => c.SourcePosition).Select(c => c.ChordText),
            Collected(converted).ChordNames.OrderBy(c => c.SourcePosition).Select(c => c.ChordText));
        // Not on an item, or inside << >>: nothing, and why.
        Assert.NotNull(ShapeChordConverter.ToNotes(guitar, guitar.IndexOf("e4", StringComparison.Ordinal)).Error);
        string spread = Book("instrument guitar", "<< diagram(C x32010) >>2 r2 |");
        Assert.Contains("<< >>", ShapeChordConverter.ToNotes(spread, spread.IndexOf("diagram", StringComparison.Ordinal)).Error);
    }

    /// <summary>Right-click a &lt;…&gt; chord → "Add Guitar Diagram" (owner's decision
    /// 2026-10-31): the chord stays and gains @diagram(SHAPE), the shape a bare @diagram draws;
    /// LYS1057 warns when the notes later leave the shape, and its fix writes the chord's own.</summary>
    [Fact]
    public void AChord_GainsItsDiagram_AndAStaleOneWarns()
    {
        string book = Book("instrument guitar", "<g b d' f'>1 | <c e g c' e'>2@diagram r2 | <e g c'>1@diagram(x32010) |", top: "octave absolute\n");
        var r = ShapeChordConverter.AddDiagram(book, book.IndexOf("<g", StringComparison.Ordinal));
        Assert.Equal("@diagram(xx5431)", r.Replacement);
        Assert.Equal(r.Start, r.End);   // an insertion, after the duration
        Assert.Equal("1", book[(r.Start - 1)..r.Start]);
        // A bare @diagram is written out where it stands.
        var bare = ShapeChordConverter.AddDiagram(book, book.IndexOf("<c e", StringComparison.Ordinal));
        Assert.Equal("@diagram", book[bare.Start..bare.End]);
        // One written already: nothing.
        Assert.NotNull(ShapeChordConverter.AddDiagram(book, book.IndexOf("<e g", StringComparison.Ordinal)).Error);

        // E G C sounds no C major shape: stale, with the chord's own shape as the fix.
        var stale = Assert.Single(Diagnostics(book), d => d.Code == DiagnosticCodes.DiagramNotTheChord);
        Assert.Equal("@diagram(x32010)", book.Substring(stale.Span.Start, stale.Span.Length));
        Assert.Contains("own shape: @diagram(", stale.Message);
        // Ctrl+Shift+Up on the written shape: another hand for the same notes; nothing else moves.
        string written = Book("instrument guitar", "<g b d' f'>1@diagram(xx5431) |", top: "octave absolute\n");
        int caret = written.IndexOf("5431", StringComparison.Ordinal);
        var step = LilySharp.Core.Editing.NoteStepper.Step(written, SyntaxTree.Parse(written), [(caret, caret)], 1, semitone: true);
        var edit = Assert.Single(step.Edits);
        Assert.Equal("xx5431", written[edit.Start..edit.End]);
        Assert.Equal("x-10-976x", edit.NewText);
        Assert.Contains("fingering 2 of", step.Message);
        // Down at the first fingering takes the shape away: back to the bare mark, which follows the notes.
        string first = Book("instrument guitar", "<g b d' f'>1@diagram(xx5431) |", top: "octave absolute\n");
        int at = first.IndexOf("5431", StringComparison.Ordinal);
        var down = Assert.Single(LilySharp.Core.Editing.NoteStepper.Step(first, SyntaxTree.Parse(first), [(at, at)], -1, semitone: true).Edits);
        Assert.Equal("(xx5431)", first[down.Start..down.End]);
        Assert.Equal("", down.NewText);
        // A bare @diagram shows the first fingering: Up writes it out; Down stays.
        string plain = Book("instrument guitar", "<g b d' f'>1@diagram |", top: "octave absolute\n");
        int on = plain.IndexOf("diagram", StringComparison.Ordinal) + 2;
        var up = Assert.Single(LilySharp.Core.Editing.NoteStepper.Step(plain, SyntaxTree.Parse(plain), [(on, on)], 1, semitone: true).Edits);
        Assert.Contains("<g b d' f'>1@diagram(xx5431) |", plain[..up.Start] + up.NewText + plain[up.End..]);
        var stay = LilySharp.Core.Editing.NoteStepper.Step(plain, SyntaxTree.Parse(plain), [(on, on)], -1, semitone: true);
        Assert.Empty(stay.Edits);
        Assert.Contains("drawn from the notes", stay.Message);
        // The shape the notes sound, written out: no warning.
        string fresh = Book("instrument guitar", "<c e g c' e'>1@diagram(x32010) |", top: "octave absolute\n");
        Assert.DoesNotContain(Diagnostics(fresh), d => d.Code == DiagnosticCodes.DiagramNotTheChord);
    }

    /// <summary>An @diagram over a note that also prints a name stands under the name, as
    /// @chord(NAME SHAPE)'s diagram does (owner's report 2026-10-31: <f a c>1@chord@diagram drew
    /// the diagram over its name); alone, or put below with .down, it is an ordinary script.</summary>
    [Fact]
    public void ADiagramBesideAName_StandsUnderIt()
    {
        bool Under(string music) => Assert.Single(Collected(Book("instrument guitar", music)).Articulations,
            a => a.Type == ArticulationType.FretFrame).UnderChordName;
        Assert.True(Under("<f a c>1@chord@diagram |"));
        Assert.True(Under("<f a c>1@diagram@chord |"));
        Assert.True(Under("c1@chord(F)@diagram(133211) |"));
        Assert.False(Under("<f a c>1@diagram |"));
        Assert.False(Under("c1@chord(F)@diagram(133211).down |"));
        // On a <…> chord a bare @chord names its notes and the shape is a way to hold them — what a
        // step from a bare @diagram writes: not LYS1056's two words for one (owner's report 2026-10-31).
        Assert.DoesNotContain(Diagnostics(Book("instrument guitar", "<f a c>1@chord@diagram(xx321x) |", top: "octave absolute\n")),
            d => d.Code == DiagnosticCodes.ChordNameOfAShape);
        Assert.Contains(Diagnostics(Book("instrument guitar", "c1@diagram(x32010)@chord |")),
            d => d.Code == DiagnosticCodes.ChordNameOfAShape);
    }

    /// <summary>A file whose scores each name their own form plays nothing as MIDI; the diagram's
    /// step and "Add Guitar Diagram" read the notes off the page, so they work there too (owner's
    /// report 2026-10-31: "not played in any score").</summary>
    [Fact]
    public void ScoresWithTheirOwnForms_StillStepAndAddDiagrams()
    {
        const string book = """
            part gt { instrument guitar }
            section onNote { gt { f1@diagram(133211) | f1@diagram | } }
            section onChord { gt { <f a c>1@chord | } }
            score a { form { onNote } staff gt }
            score b { form { onChord } staff gt }
            """;
        var tree = SyntaxTree.Parse(book);
        int shape = book.IndexOf("33211", StringComparison.Ordinal);
        Assert.Equal("xx3211", Assert.Single(LilySharp.Core.Editing.NoteStepper.Step(book, tree, [(shape, shape)], 1, semitone: true).Edits).NewText);
        int bare = book.IndexOf("diagram |", StringComparison.Ordinal) + 2;
        Assert.StartsWith("(", Assert.Single(LilySharp.Core.Editing.NoteStepper.Step(book, tree, [(bare, bare)], 1, semitone: true).Edits).NewText);
        Assert.Equal("@diagram(xx321x)", ShapeChordConverter.AddDiagram(book, book.IndexOf("<f a", StringComparison.Ordinal)).Replacement);

        // The checks read the same notes: a chord no hand holds (LYS1055), a diagram its notes left (LYS1057).
        const string checks = """
            octave absolute
            part gt { instrument guitar }
            section s { gt { <e f fis g>1 | <e g c'>1@diagram(x32010) | diagram(133211)1 | } }
            score a { form { s } staff gt }
            """;
        var found = Diagnostics(checks);
        Assert.Contains(found, d => d.Code == DiagnosticCodes.ChordNotFingerable);
        Assert.Contains(found, d => d.Code == DiagnosticCodes.DiagramNotTheChord);
        // …and Convert to Chord Notes checks that the file still sounds the same.
        var notes = ShapeChordConverter.ToNotes(checks, checks.IndexOf("diagram(1", StringComparison.Ordinal));
        Assert.True(notes.Replacement != null, notes.Error);
        Assert.Equal(LilySharp.Core.Semantics.PlayedNotes.Signature(SyntaxTree.Parse(checks)),
            LilySharp.Core.Semantics.PlayedNotes.Signature(SyntaxTree.Parse(checks[..notes.Start] + notes.Replacement + checks[notes.End..])));
        Assert.NotEqual("", LilySharp.Core.Semantics.PlayedNotes.Signature(SyntaxTree.Parse(checks)));
    }

    /// <summary>Owner's review 2026-10-31. A chord(…) item with no shape draws no diagram even in an
    /// `all` score (it sounds close, and a diagram is a score's while its notes are the part's); its
    /// close voicing stands by the foot of the staff as drawn, in the instrument's range, and on a
    /// fretted part where a hand can hold it.</summary>
    [Fact]
    public void AChordItemWithNoShape_StaysCloseInRangeAndUndrawn()
    {
        Assert.Empty(Frames(Book("instrument guitar", "chord(F)1 |", top: "layout { chordDiagrams all }\n")));
        Assert.Equal(new[] { "133211" }, Frames(Book("instrument guitar", "chord(F 133211)1 |", top: "layout { chordDiagrams all }\n")));
        // A ukulele's G is G4 B4 D5: G3 is below its C4.
        string uke = Book("instrument ukulele", "chord(G)1 |");
        Assert.Equal(new[] { 67, 71, 74 }, MidiPitches(uke));
        Assert.DoesNotContain(Diagnostics(uke), d => d.Severity == DiagnosticSeverity.Warning);
        // A bass's F is held: not F1 A1 C2, which no hand frets.
        string bass = Book("instrument bass", "chord(F)1 |");
        Assert.DoesNotContain(Diagnostics(bass), d => d.Code == DiagnosticCodes.ChordNotFingerable);
        Assert.Equal(new[] { 41, 45, 48 }, MidiPitches(bass));
    }

    /// <summary>`chordDiagrams none` draws no diagram at all, an @diagram's included — on the page,
    /// in the twin and in MusicXML (owner's review 2026-10-31: it drew one while the items did not).</summary>
    [Fact]
    public void ChordDiagramsNone_DrawsNoDiagramAtAll()
    {
        string none = Book("instrument guitar", "f1@diagram(133211) | <f a c>1@diagram | diagram(F 133211)1 |",
            top: "layout { chordDiagrams none }\n");
        Assert.Empty(Frames(none));
        Assert.DoesNotContain("fret-diagram", new LilyPondExporter().Export(SyntaxTree.Parse(none)));
        Assert.DoesNotContain("<frame", new MusicXmlExporter().Export(SyntaxTree.Parse(none)).ToXml().ToString());
        Assert.Single(Frames(Book("instrument guitar", "f1@diagram(133211) |")));
    }

    // ================================================================ the outputs

    [Fact]
    public void Midi_PlaysTheShape_TiesAndDynamicsAsOnAChord()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(
                Book("instrument guitar", "diagram(C x32013)2~ diagram(C x32013)2 | diagram(G 320003)1@ff |")))
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
            "grace { diagram(C x32013)8 } c'2 tuplet 3/2 { diagram(C x32013)4 diagram(G 320003) r } |");
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
            Book("instrument guitar", "diagram(Cm7 x3x546)1 |"))).ToXml().ToString();
        Assert.Equal(new[] { "5", "3", "2", "1" },
            Regex.Matches(xml, @"<string>(\d)</string>").Select(m => m.Groups[1].Value));
        // Under the guitar's treble_8 clef: the pitches it sounds (Spelling_FollowsTheSymbolThenTheKey).
        Assert.Equal(new[] { "C3", "C4", "E-14", "B-14" },
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
            Book("instrument guitar", "g''1 | diagram(C x32013)1 | d1 | diagram(Cm7 x3x546)1 |")));
        // The twin is `\relative c` since the owner's decision 2026-10-28, so from the G5 the
        // chord's C3 is c,,,.
        Assert.Contains(@"<c,,,\5 e\4 g\3 c\2 g'\1>1", ly);
        Assert.Contains(@"d1 |", ly);
        Assert.Contains(@"<c\5 c'\3 ees\2 bes'\1>1", ly);   // read from the d, D3
        Assert.Contains(@"\omit StringNumber", ly);
        string fixedLy = new LilyPondExporter().Export(SyntaxTree.Parse(
            Book("instrument guitar", "diagram(C x32013)1 |", top: "octave absolute")));
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
        string doc = Book("instrument guitar", "diagram(C x32013)2 diagram(C)2 |");
        string? hover = HoverAt(doc, "diagram(C x");
        Assert.NotNull(hover);
        Assert.Contains("**Chord from a shape** `C`", hover);
        Assert.Contains("guitar: `x32013` — C3  E3  G3  C4  G4", hover);
        Assert.Contains("guitar: `x32010`", HoverAt(doc, "diagram(C)"));   // the usual shape (2026-10-31)
    }

    /// <summary>The capo is the FILE's (the top-level <c>capo N</c>, owner's grammar 2026-10-31):
    /// every score sounds the item under it, so the hover's line is one — a per-score capo
    /// (2026-09-30 to 2026-10-31, when the capo was a layout word and two scores could differ)
    /// went with the layout word. The line reads as it always did.</summary>
    [Fact]
    public void Hover_ReadsTheFilesCapo_InEveryScore()
    {
        const string book = """
            capo 3
            layout open { chordDiagrams written }
            part gt { instrument guitar }
            section A { gt { diagram(Eb x32010)1 | } }
            form { A }
            score { staff gt }
            score open { layout open  staff gt }
            """;
        string? hover = HoverAt(book, "diagram(Eb x");
        Assert.Contains("guitar: `x32010` — E♭3  G3  B♭3  E♭4  G4", hover);
        Assert.DoesNotContain("(Default)", hover);
        Assert.DoesNotContain("no capo", hover);
        string single = Book("instrument guitar", "diagram(C x32010)1 |");
        Assert.Contains("guitar: `x32010` — C3  E3  G3  C4  E4", HoverAt(single, "diagram(C x"));
    }

    /// <summary>LYS1039 checks the item under the file's capo, as every score sounds it: x32010
    /// is E♭ at capo 3 and C with no capo, so <c>diagram(Eb x32010)</c> warns without the capo
    /// line and is silent with it, however many scores draw the part.</summary>
    [Fact]
    public void TheShapeCheck_ReadsTheFilesCapo()
    {
        const string open = """
            part gt { instrument guitar }
            section A { gt { diagram(Eb x32010)1 | } }
            form { A }
            score { staff gt }
            score two { staff gt }
            """;
        var mismatch = Assert.Single(Diagnostics(open), d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        Assert.Contains("x32010", mismatch.Message);
        // Under the capo that presses it, nothing to say — in every score.
        Assert.DoesNotContain(Diagnostics("capo 3\n" + open), d => d.Code == DiagnosticCodes.ChordShapeMismatch);
        Assert.DoesNotContain(Diagnostics(Book("instrument guitar", "diagram(Eb x32010)1 |",
            top: "capo 3")), d => d.Code == DiagnosticCodes.ChordShapeMismatch);
    }

    /// <summary>The hover spells a tone that is no chord tone as the key in force spells it, as the
    /// page does — x32012's top string is F♯ in C and G♭ under a flat key. Until 2026-09-30 the
    /// hover spelled it in C whatever the key.</summary>
    [Fact]
    public void Hover_SpellsANonChordToneInTheKey()
    {
        Assert.Contains("F♯4", HoverAt(Book("instrument guitar", "diagram(C x32012)1 |"), "diagram(C x"));
        string flat = Book("instrument guitar\n  key f major", "diagram(C x32012)1 |");
        string? hover = HoverAt(flat, "diagram(C x");
        Assert.Contains("G♭4", hover);
        Assert.DoesNotContain("F♯", hover);
        // …as the page prints the same note.
        var chord = Assert.IsType<ChordItem>(Items(Collected(flat))[0]);
        Assert.Equal(("flat", 0), (chord.Notes.Single(n => n.Accidental != null).Accidental,
            chord.Notes.Count(n => n.Accidental == "sharp")));
    }

    private static (string After, StepResponse Response) Step(string text, int caret, int dir, bool semitone = false)
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
            Semitone = semitone,
        });
        string after = text;
        foreach (var e in response.Edits.OrderByDescending(e => e.Start))
            after = after[..e.Start] + e.NewText + after[e.End..];
        return (after, response);
    }

    /// <summary>Ctrl+Shift+Up/Down on the item steps its shape through the @chord order, on the
    /// PART's tuning, and sounds it; Down at the default takes the shape away — the name alone
    /// still draws and plays it (owner's request 2026-10-31; it used to stop there).</summary>
    [Fact]
    public void TheStep_StepsTheItemsShape_AndSoundsIt()
    {
        string Doc(string words) => Book("instrument guitar", $"diagram({words})1 |");
        string text = Doc("C");
        var (after, up) = Step(text, text.IndexOf("diagram(C", StringComparison.Ordinal) + 9, +1, semitone: true);
        Assert.Equal(Doc("C x32010"), after);
        Assert.StartsWith("C: shape 1 of", up.Message);
        Assert.Equal(new[] { 48, 52, 55, 60, 64 }, up.Pitches);
        text = Doc("C x32010");
        var (next, _) = Step(text, text.IndexOf("x32010", StringComparison.Ordinal) + 2, +1, semitone: true);
        Assert.NotEqual(text, next);
        Assert.Matches(@"diagram\(C [x0-9-]+\)1", next);
        var (bare, down) = Step(text, text.IndexOf("x32010", StringComparison.Ordinal) + 2, -1, semitone: true);
        Assert.Equal(Doc("C"), bare);
        Assert.False(down.Fallback);
        Assert.Contains("still draws and plays the usual shape (x32010)", down.Message);
        // On a ukulele part it steps the ukulele's shapes.
        string uke = Book("instrument ukulele", "diagram(C)1 |");
        Assert.Equal(Book("instrument ukulele", "diagram(C 0003)1 |"),
            Step(uke, uke.IndexOf("diagram(C", StringComparison.Ordinal) + 9, +1, semitone: true).After);
    }

    /// <summary>Owner's request 2026-10-31: a shape with no symbol steps too — through the order
    /// of the chord its notes sound (x32010 is C), writing shapes only; Down at the first keeps
    /// it. (A symbol-less @chord steps its note instead: StepRequestTests.)</summary>
    [Fact]
    public void TheStep_OnAShapeWithNoSymbol_WalksTheChordItSounds()
    {
        string text = Book("instrument guitar", "diagram(x32010)1 |");
        int caret = text.IndexOf("x32010", StringComparison.Ordinal) + 2;
        var (after, up) = Step(text, caret, +1, semitone: true);
        Assert.Matches(@"diagram\([x0-9-]+\)1 \|", after);
        Assert.DoesNotContain("x32010", after);
        Assert.StartsWith("C: shape 2 of", up.Message);
        Assert.NotEmpty(up.Pitches);   // the new shape sounds, as a named one does
        var (same, down) = Step(text, caret, -1, semitone: true);
        Assert.Equal(text, same);
        Assert.Contains("keeps its shape", down.Message);
        // Back down from the second shape is the first again.
        Assert.Equal(text, Step(after, after.IndexOf("diagram(", StringComparison.Ordinal) + 9, -1, semitone: true).After);

        string am = Book("instrument guitar", "diagram(x02210)1 |");
        var (amAfter, said) = Step(am, am.IndexOf("x02210", StringComparison.Ordinal) + 2, +1, semitone: true);
        Assert.DoesNotContain("x02210", amAfter);
        Assert.StartsWith("Am: shape 2 of", said.Message);
    }

    /// <summary>Owner's decisions 2026-10-31 (docs/chord-syntax-design.md): every combination is
    /// one word. A bare @chord beside an @diagram(shape) asks in two what @chord(NAME shape) or
    /// chord(shape) says in one: it warns (LYS1056) naming both, and still draws. A shape alone in
    /// an @chord is one word — the name read off it and its diagram — and is not warned.</summary>
    [Fact]
    public void AShapesName_IsAskedOfTheItem_TheOtherSpellingsSayHow()
    {
        var two = Assert.Single(Diagnostics(Book("instrument guitar", "c'1@diagram(x32010)@chord |")),
            d => d.Code == DiagnosticCodes.ChordNameOfAShape);
        Assert.Contains("@chord(C x32010)", two.Message);
        Assert.Contains("chord(x32010) in place of the note", two.Message);
        foreach (var fine in new[] { "c'1@chord(x32010) |", "chord(x32010)1 |", "diagram(x32010)1@chord |",
                                     "c'1@chord(C x32010) |", "<c e g>1@chord |", "c'1@diagram(x32010) |" })
            Assert.DoesNotContain(Diagnostics(Book("instrument guitar", fine)),
                d => d.Code == DiagnosticCodes.ChordNameOfAShape);
        // A bare @chord on a single note names nothing: said, as on a rest.
        Assert.Contains(Diagnostics(Book("instrument guitar", "c'1@chord |")),
            d => d.Code == DiagnosticCodes.ChordNotRecognized && d.Message.Contains("single note"));
    }

    /// <summary>An @diagram's shape on a fretted part must have its strings (LYS1038): seven
    /// characters on a guitar drew seven strings in silence. Elsewhere any diagram is drawn.</summary>
    [Fact]
    public void ADiagramOfTheWrongStringCount_OnAFrettedPart_Warns()
    {
        var warning = Assert.Single(Diagnostics(Book("instrument guitar", "c'1@diagram(0x32010) |")),
            d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn);
        Assert.Contains("'guitar' has 6 strings", warning.Message);
        Assert.DoesNotContain(Diagnostics(Book("instrument guitar", "c'1@diagram(x32010) |")),
            d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn);
        Assert.DoesNotContain(Diagnostics(Book("clef treble", "c'1@diagram(0x32010) |")),
            d => d.Code == DiagnosticCodes.ChordDiagramNotDrawn);
    }

    /// <summary>Owner's request 2026-10-31: hovering a symbol-less item names the chord it
    /// sounds, and Ctrl+. writes that name in.</summary>
    [Fact]
    public void ASymbolLessItem_IsNamedByItsHover_AndTheQuickFixWritesTheName()
    {
        string text = Book("instrument guitar", "diagram(x02210)1 |");
        Assert.Contains("this shape is **Am**", LilySharpLanguageServer.ShapeChordHover(Item(text), text));
        Assert.DoesNotContain("this shape is", LilySharpLanguageServer.ShapeChordHover(
            Item(Book("instrument guitar", "diagram(Am x02210)1 |")), text));

        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///shape-name.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.IndexOf("x02210", StringComparison.Ordinal) + 2);
        var fix = Assert.Single(server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new Position(line, character), End = new Position(line, character) },
        }) ?? [], a => a.Title == "Write the chord name: Am");
        var edit = fix.Edit!.Changes!.Values.Single().Single();
        int start = DocumentManager.GetOffset(text, edit.Range.Start), end = DocumentManager.GetOffset(text, edit.Range.End);
        Assert.Equal(Book("instrument guitar", "diagram(Am x02210)1 |"), text[..start] + edit.NewText + text[end..]);
        // …and the one that shows it, in the word that says a name is seen.
        var show = Assert.Single(server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new Position(line, character), End = new Position(line, character) },
        }) ?? [], a => a.Title.StartsWith("Show the chord name: chord(Am", StringComparison.Ordinal));
        string shown = text;
        foreach (var e in show.Edit!.Changes!.Values.Single().OrderByDescending(e => DocumentManager.GetOffset(text, e.Range.Start)))
            shown = shown[..DocumentManager.GetOffset(shown, e.Range.Start)] + e.NewText + shown[DocumentManager.GetOffset(shown, e.Range.End)..];
        Assert.Equal(Book("instrument guitar", "chord(Am x02210)1 |"), shown);
    }

    /// <summary>Owner's request 2026-10-31: with shapes for two tunings written, the caret on one
    /// steps that one on its own tuning — the message names it — and the caret on a shape no
    /// tuning here has the strings of keeps the old rule (the score's shape steps) and says how.</summary>
    [Fact]
    public void TheStep_OnOneOfSeveralShapes_StepsThatOne()
    {
        const string both = "layout uke { chordTuning ukulele }\npart melody { clef treble }\n"
            + "section A { melody { e''1@chord(C x32010 0003) | } }\nform { A }\n"
            + "score { staff melody }\nscore uke { layout uke  staff melody }\n";
        var (uke, said) = Step(both, both.IndexOf("0003", StringComparison.Ordinal) + 1, +1, semitone: true);
        Assert.Contains("@chord(C x32010 0403)", uke);
        Assert.StartsWith("C (ukulele): shape 2 of", said.Message);
        var (gtr, saidGtr) = Step(both, both.IndexOf("x32010", StringComparison.Ordinal) + 1, +1, semitone: true);
        Assert.Contains("@chord(C x32013 0003)", gtr);
        Assert.StartsWith("C (guitar): shape 2 of", saidGtr.Message);

        string guitarOnly = both.Replace("score uke { layout uke  staff melody }\n", "");
        var (stepped, told) = Step(guitarOnly, guitarOnly.IndexOf("0003", StringComparison.Ordinal) + 1, +1, semitone: true);
        Assert.Contains("@chord(C x32013 0003)", stepped);
        Assert.Contains("'0003' is drawn by no score here", told.Message);
    }

    [Fact]
    public void Completion_InsideTheItem_IsTheChordSymbols()
    {
        Assert.True(LilySharpLanguageServer.IsInsideShapeChordItem("gt { diagram(", 13));
        Assert.True(LilySharpLanguageServer.IsInsideChordAnnotation("gt { diagram(C ", 15));
        Assert.False(LilySharpLanguageServer.IsInsideShapeChordItem("gt { c4@chord(", 14));
        Assert.False(LilySharpLanguageServer.IsInsideShapeChordItem("gt { diagram(C) ", 16));
    }

    /// <summary>The part's instrument first, the score's chordTuning word for the chords no fretted
    /// part owns (owner's decision 2026-10-31, review D1): a guitar part's chord(F 133211) under
    /// a ukulele word drew the ukulele's F while playing the guitar shape.</summary>
    [Fact]
    public void ADiagram_FollowsItsPartsInstrument_TheScoresWordTheRest()
    {
        const string book = """
            octave absolute
            layout { chordTuning ukulele  chordDiagrams all }
            part gt { instrument guitar }
            part pn { clef treble }
            section A { gt { chord(F 133211)1 | c'1@chord(F) | } pn { c'1@chord(F) | c'1 | } }
            form { A }
            score { staff gt staff pn }
            """;
        var frames = Collected(book).Articulations.Where(a => a.Type == ArticulationType.FretFrame)
            .OrderBy(a => a.SourcePosition).Select(a => a.FrameSpec).ToList();
        Assert.Equal(3, frames.Count);
        Assert.Equal("133211", frames[0]);                 // the guitar's shape, as it plays
        Assert.StartsWith("133211|", frames[1]);           // the guitar part's F: the guitar's
        Assert.StartsWith("2010", frames[2]);              // the piano's F: the score's ukulele
    }

    /// <summary>An @diagram strum after strum stands where the diagram changes and at a system's
    /// head, as an item's does (owner's decision 2026-10-31, review C3); its @chord name stands on
    /// every chord.</summary>
    [Fact]
    public void ADiagramMark_StandsWhereTheDiagramChanges()
    {
        string book = Book("instrument guitar",
            "<f a c>4@chord@diagram <f a c>4@chord@diagram <g b d>4@chord@diagram <g b d>4@chord@diagram | "
            + "<g b d>4@chord@diagram r2. |");
        var collected = Collected(book);
        Assert.Equal(new[] { -1, 0, -1, 0, 1 }, collected.Articulations.Where(a => a.Type == ArticulationType.FretFrame)
            .OrderBy(a => a.SourcePosition).Select(a => a.RepeatsDiagramBack));
        Assert.All(collected.ChordNames, c => Assert.Equal(-1, c.RepeatsNameBack));
        // On the page: the two changes only; every name.
        var laid = new LayoutEngine().Layout(collected);
        Assert.Equal(2, laid.ArticulationLayouts.Count(a => a.Glyph.StartsWith("frame:")));
        Assert.Equal(5, laid.ChordNameLayouts.Length);
        // The twin asks LilyPond's breaks the same question.
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        Assert.Equal(3, Regex.Matches(ly, "lysRepeatedDiagram \\d+").Count);
        // An @chord(NAME SHAPE)'s diagram stands with its name on every chord.
        var named = Collected(Book("instrument guitar", "c4@chord(F 133211) c4@chord(F 133211) r2 |"));
        Assert.All(named.Articulations.Where(a => a.Type == ArticulationType.FretFrame), a => Assert.Equal(-1, a.RepeatsDiagramBack));
    }

    /// <summary>An @diagram after a &lt;&lt; &gt;&gt; group stands over its first note, as its @chord does
    /// (review 2026-10-31, D2: the page dropped it with LYS4008 while the twin drew it); a bare one
    /// draws the group's notes as fretted.</summary>
    [Fact]
    public void ADiagramOnASpreadGroup_StandsOverItsFirstNote()
    {
        string book = Book("instrument guitar", "<< c e g c' e' >>2@diagram(x32010) << f a c' f' >>2@chord@diagram |",
            top: "octave absolute\n");
        Assert.DoesNotContain(Diagnostics(book), d => d.Code == DiagnosticCodes.ArpeggioAnnotationUnsupported);
        var frames = Collected(book).Articulations.Where(a => a.Type == ArticulationType.FretFrame)
            .OrderBy(a => a.SourcePosition).ToList();
        Assert.Equal(2, frames.Count);
        Assert.Equal("x32010", frames[0].FrameSpec);
        Assert.Equal(0, frames[0].ItemIndex);
        Assert.Equal(5, frames[1].ItemIndex);            // the second group's first note
        Assert.True(frames[1].UnderChordName);
        Assert.False(frames[0].UnderChordName);
        // The twin draws the same two, the bare one's shape read off the page.
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        Assert.Equal(2, Regex.Matches(ly, "fret-diagram-terse").Count);
        Assert.Contains("fret-diagram-terse \"x;3;2;o;1;o;\"", ly);
    }

    /// <summary>A part written in two voices, in a file whose scores name their own forms (so its
    /// notes are read off the page, whose voices are "voice1" and "voice2"), is checked as its part
    /// (review 2026-10-31, C4: neither LYS1055 nor LYS1057 was raised there).</summary>
    [Fact]
    public void ATwoVoicePart_IsCheckedWhereTheScoresNameTheirOwnForms()
    {
        const string music = "voice { c''1 | c''1 | } { <e f fis g>1 | <e g c'>1@diagram(x32010) | }";
        string own = $"octave absolute\npart gt {{ instrument guitar }}\nsection s {{ gt {{ {music} }} }}\nscore a {{ form {{ s }} staff gt }}";
        string shared = $"octave absolute\npart gt {{ instrument guitar }}\nsection s {{ gt {{ {music} }} }}\nform {{ s }}\nscore {{ staff gt }}";
        static string[] Codes(string book) => [.. Diagnostics(book)
            .Where(d => d.Code is DiagnosticCodes.ChordNotFingerable or DiagnosticCodes.DiagramNotTheChord)
            .Select(d => $"{d.Code}@{d.Span.Start}")];
        Assert.Equal(2, Codes(own).Length);
        Assert.Equal(Codes(shared), Codes(own));
        Assert.All(LilySharp.Core.Semantics.PlayedNotes.Of(SyntaxTree.Parse(own)), n => Assert.Equal("gt", n.Part));
    }

    /// <summary>"Add Guitar Diagram" on a single note (review 2026-10-31, C1): where the note is
    /// held, what a bare @diagram on it draws — offered in a fretted part only.</summary>
    [Fact]
    public void ANote_GainsWhereItIsHeld()
    {
        string book = Book("instrument guitar", "c'1 | c'2:8 r2 | c'1@diagram |", top: "octave absolute\n");
        var r = ShapeChordConverter.AddDiagram(book, book.IndexOf("c'1", StringComparison.Ordinal));
        Assert.Equal("@diagram(xxxx1x)", r.Replacement);
        Assert.Equal("c'1", book[(r.Start - 3)..r.End]);
        // After a tremolo; a bare mark written out where it stands.
        var tremolo = ShapeChordConverter.AddDiagram(book, book.IndexOf("c'2", StringComparison.Ordinal));
        Assert.Equal(":8", book[(tremolo.Start - 2)..tremolo.Start]);
        var bare = ShapeChordConverter.AddDiagram(book, book.IndexOf("c'1@", StringComparison.Ordinal));
        Assert.Equal("@diagram", book[bare.Start..bare.End]);
        // Offered on a note only where a fretted instrument plays it; on a chord anywhere.
        Assert.True(ShapeChordConverter.OffersDiagram(SyntaxTree.Parse(book), book.IndexOf("c'1", StringComparison.Ordinal)));
        string piano = Book("clef treble", "c'2 <c e g>2 |");
        Assert.False(ShapeChordConverter.OffersDiagram(SyntaxTree.Parse(piano), piano.IndexOf("c'2", StringComparison.Ordinal)));
        Assert.True(ShapeChordConverter.OffersDiagram(SyntaxTree.Parse(piano), piano.IndexOf("<c", StringComparison.Ordinal)));
        Assert.False(ShapeChordConverter.OffersDiagram(SyntaxTree.Parse(book), book.IndexOf("r2", StringComparison.Ordinal)));
    }

    /// <summary>"Convert to Chord Symbol chord(…)" on a &lt;…&gt; chord (review 2026-10-31, C2): the way
    /// back from "Convert to Chord Notes" — named from the notes or its @chord, with the shape when
    /// the close voicing is not the notes or a diagram is drawn — and the file sounds the same.</summary>
    [Fact]
    public void AChord_ConvertsBackToTheItem_AndSoundsTheSame()
    {
        static string Apply(string book, ShapeChordConverter.Result r)
        {
            Assert.True(r.Replacement != null, r.Error);
            return book[..r.Start] + r.Replacement + book[r.End..];
        }
        static string Back(string book, string at)
            => Apply(book, ShapeChordConverter.ToChordItem(book, book.IndexOf(at, StringComparison.Ordinal)));

        // Round trips on a guitar: the name kept, the shape the notes need.
        string guitar = Book("instrument guitar", "c4 diagram(133211)4 chord(F 133211)4 e4 | chord(x32010)2 g2 |");
        string asNotes = Apply(guitar, ShapeChordConverter.ToNotes(guitar, guitar.IndexOf("chord(F", StringComparison.Ordinal)));
        Assert.Equal(guitar, Back(asNotes, "<f"));
        string named = Apply(guitar, ShapeChordConverter.ToNotes(guitar, guitar.IndexOf("diagram(", StringComparison.Ordinal)));
        Assert.Contains("c4 chord(F 133211)4 chord(F 133211)4", Back(named, "<f"));

        // A piano: the close voicing is the notes - the name alone; other marks stay.
        string piano = Book("clef treble", "c'4 <c e g>4@accent <e g c'>2 |");
        Assert.Contains("c'4 chord(C)4@accent <e", Back(piano, "<c e"));
        // An inversion is not the close voicing: refused, and why.
        var inverted = ShapeChordConverter.ToChordItem(piano, piano.IndexOf("<e g", StringComparison.Ordinal));
        Assert.Contains("would not sound the same", inverted.Error);

        // The marks the item now says go; a drawn diagram keeps its shape.
        string marked = Book("instrument guitar", "<f, c f a c' f'>1@chord@diagram |", top: "octave absolute\n");
        Assert.Contains("chord(F 133211)1 |", Back(marked, "<f"));
        string written = Book("instrument guitar", "<f, c f a c' f'>1@chord(F)@diagram(133211)@accent |", top: "octave absolute\n");
        Assert.Contains("chord(F 133211)1@accent |", Back(written, "<f"));

        // Notes that name no chord: nothing, and why.
        string cluster = Book("clef treble", "<c cis d>1 |");
        Assert.Contains("name no chord", ShapeChordConverter.ToChordItem(cluster, cluster.IndexOf("<c", StringComparison.Ordinal)).Error);
    }
}
