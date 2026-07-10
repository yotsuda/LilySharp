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

using Xunit;
using LilySharp.Core.Syntax;
using System.Linq;
using LilySharp.Core.MusicXml;

namespace LilySharp.Tests;

[Trait("Category", "Integration")]
public class MusicXmlTests
{
    [Fact]
    public void ExportSimpleNotes()
    {
        var source = "c4 d4 e4 f4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        Assert.Single(xml.Parts);
        Assert.True(xml.Parts[0].Measures.Count >= 1);
        Assert.Equal(4, xml.Parts[0].Measures[0].Notes.Count);
    }

    [Fact]
    public void ExportTripleDottedNote_UsesBaseTypeWithThreeDots()
    {
        // A triple-dotted eighth reduces to 15/64. It must export as an "eighth" with
        // three dots, not an undotted "64th" — only single/double dots were special-
        // cased before, so any triple-plus dotted note mis-reported its <type>.
        var tree = SyntaxTree.Parse("{ c8... }");
        var xml = new MusicXmlExporter().Export(tree);

        var note = xml.Parts[0].Measures[0].Notes[0];
        Assert.Equal("eighth", note.Type);
        Assert.Equal(3, note.Dots);
    }

    [Fact]
    public void ExportWithTitle()
    {
        var source = @"
title ""Test Song""
c4 d4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        Assert.Equal("Test Song", xml.Title);
    }

    [Fact]
    public void ExportWithComposer()
    {
        var source = @"
composer ""J.S. Bach""
c4 d4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        Assert.Equal("J.S. Bach", xml.Composer);
    }

    [Fact]
    public void Partial_PickupIsImplicitMeasureZero()
    {
        // 'partial 4' makes the opening bar a pickup: MusicXML marks it
        // implicit and numbers it 0, so the first full bar is 1.
        var tree = SyntaxTree.Parse("time 4/4 partial 4 g4 | c4 d e f | g1 |");
        var xml = new MusicXmlExporter().Export(tree);
        var measures = xml.Parts[0].Measures;

        Assert.Equal(3, measures.Count);
        Assert.True(measures[0].Implicit);
        Assert.Equal(0, measures[0].Number);
        Assert.Single(measures[0].Notes);          // pickup g4
        Assert.False(measures[1].Implicit);
        Assert.Equal(1, measures[1].Number);
        Assert.Equal(4, measures[1].Notes.Count);  // c d e f
        Assert.Equal(2, measures[2].Number);
    }

    [Fact]
    public void Partial_AutoClosesWithoutWrittenBarline()
    {
        // No '|' after the pickup: it must still close at 1/4, with no spurious
        // empty measure (matches the SVG collector's auto-close).
        var tree = SyntaxTree.Parse("time 4/4 partial 4 g4 c4 d e f | g1 |");
        var xml = new MusicXmlExporter().Export(tree);
        var measures = xml.Parts[0].Measures;

        Assert.Equal(3, measures.Count);
        Assert.True(measures[0].Implicit);
        Assert.Single(measures[0].Notes);          // pickup g4
        Assert.Equal(4, measures[1].Notes.Count);  // c d e f
        Assert.Single(measures[2].Notes);          // g1
    }

    [Fact]
    public void Partial_ImplicitAttributeSerialized()
    {
        var tree = SyntaxTree.Parse("time 4/4 partial 4 g4 | c4 d e f |");
        var xml = new MusicXmlExporter().Export(tree);
        var firstMeasureXml = xml.Parts[0].Measures[0].ToXml().ToString();
        Assert.Contains("implicit=\"yes\"", firstMeasureXml);
    }

    [Fact]
    public void ExportWithRest()
    {
        var source = "c4 r4 e4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal(3, notes.Count);
        Assert.False(notes[0].IsRest);
        Assert.True(notes[1].IsRest);
        Assert.False(notes[2].IsRest);
    }

    [Fact]
    public void ExportNotePitch()
    {
        var source = "c4 cis4 des4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal("C", notes[0].Step);
        Assert.Equal(0, notes[0].Alter);

        Assert.Equal("C", notes[1].Step);
        Assert.Equal(1, notes[1].Alter);

        Assert.Equal("D", notes[2].Step);
        Assert.Equal(-1, notes[2].Alter);
    }

    [Fact]
    public void ExportDurations()
    {
        var source = "c1 d2 e4 f8";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal("whole", notes[0].Type);
        Assert.Equal("half", notes[1].Type);
        Assert.Equal("quarter", notes[2].Type);
        Assert.Equal("eighth", notes[3].Type);
    }

    [Fact]
    public void ExportTuplet_ScalesDurationAndEmitsTimeModification()
    {
        // A triplet: three eighths in the time of two = one quarter total.
        var source = "tuplet 3/2 { c8 d8 e8 }";
        var tree = SyntaxTree.Parse(source);
        var xml = new MusicXmlExporter().Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal(3, notes.Count);
        foreach (var n in notes)
        {
            Assert.Equal("eighth", n.Type);
            Assert.Equal(3, n.ActualNotes);   // <time-modification> 3 in the time of 2
            Assert.Equal(2, n.NormalNotes);
            Assert.Equal(8, n.Duration);       // eighth(12) * 2/3, exact at divisions=24
        }
        // The three notes sum to exactly one quarter (24 divisions).
        Assert.Equal(24, notes.Sum(n => n.Duration));
    }

    [Fact]
    public void ExportWithBarlines()
    {
        var source = "c4 d4 | e4 f4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        Assert.Equal(2, xml.Parts[0].Measures.Count);
        Assert.Equal(2, xml.Parts[0].Measures[0].Notes.Count);
        Assert.Equal(2, xml.Parts[0].Measures[1].Notes.Count);
    }

    [Fact]
    public void ExportWithArticulations()
    {
        var source = "c4@staccato d4@accent";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Contains("staccato", notes[0].Articulations);
        Assert.Contains("accent", notes[1].Articulations);
    }

    [Fact]
    public void ExportRelativePitch()
    {
        var source = @"{ c d e f }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal(4, notes.Count);
        // Default starts at C4
        Assert.Equal(4, notes[0].Octave);
    }

    [Fact]
    public void ToXml_ProducesValidDocument()
    {
        var source = @"
title ""Test""
composer ""Me""
c4 d4 e4 f4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var doc = xml.ToXml();
        Assert.NotNull(doc);
        Assert.Equal("score-partwise", doc.Root?.Name.LocalName);
    }

    [Fact]
    public void FirstMeasure_HasAttributes()
    {
        var source = "c4 d4 e4 f4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var attrs = xml.Parts[0].Measures[0].Attributes;
        Assert.NotNull(attrs);
        Assert.Equal(24, attrs.Divisions);
        Assert.Equal(4, attrs.TimeBeats);
        Assert.Equal(4, attrs.TimeBeatType);
    }

    [Fact]
    public void ExportChord_CreatesMultipleNotesWithChordFlag()
    {
        var source = "<c e g>4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal(3, notes.Count);

        // First note should not have chord flag
        Assert.False(notes[0].IsChord);
        Assert.Equal("C", notes[0].Step);

        // Second and third notes should have chord flag
        Assert.True(notes[1].IsChord);
        Assert.Equal("E", notes[1].Step);

        Assert.True(notes[2].IsChord);
        Assert.Equal("G", notes[2].Step);
    }

    [Fact]
    public void ExportWithDynamics_CreatesDirection()
    {
        var source = "c4@f d4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var doc = xml.ToXml();
        var directions = doc.Descendants("dynamics").ToList();
        Assert.NotEmpty(directions);
        Assert.NotNull(directions[0].Element("f"));
    }

    [Fact]
    public void ExportWithOrnament_Trill()
    {
        var source = "c4@trill d4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Contains("trill-mark", notes[0].Ornaments);
    }

    [Fact]
    public void ExportWithPortato()
    {
        var source = "c4@portato";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Contains("detached-legato", notes[0].Articulations);
    }

    [Fact]
    public void ExportWithSlur()
    {
        var source = "c4( d4 e4)";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.True(notes[0].SlurStart);
        Assert.True(notes[2].SlurStop);
    }

    [Fact]
    public void ExportGraceNotes()
    {
        var source = @"acciaccatura { d8 } c4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.True(notes[0].IsGrace);
        Assert.True(notes[0].IsSlash); // acciaccatura = slashed
        Assert.False(notes[1].IsGrace);
    }

    [Fact]
    public void ExportMultiSection_CreatesMultipleParts()
    {
        var source = @"
section A {
    melody { c4 d e f | }
    bass { c2 c | }
}";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        Assert.Equal(2, xml.Parts.Count);
        Assert.Equal("melody", xml.Parts[0].Name);
        Assert.Equal("bass", xml.Parts[1].Name);
    }

    [Fact]
    public void ExportMultiSection_PartMeasures()
    {
        var source = @"
section A {
    melody { c4 d e f | g a b c' | }
}";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        Assert.Single(xml.Parts);
        Assert.Equal("melody", xml.Parts[0].Name);
        Assert.Equal(2, xml.Parts[0].Measures.Count);
    }

    [Fact]
    public void ExportDottedNote()
    {
        var source = "c4. d8";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal("quarter", notes[0].Type);
        Assert.Equal(1, notes[0].Dots);
    }

    [Fact]
    public void ExportKeySignature_InAttributes()
    {
        var source = @"
key g major
c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);

        var attrs = xml.Parts[0].Measures[0].Attributes;
        Assert.NotNull(attrs);
        Assert.Equal(1, attrs.KeyFifths); // G major = 1 sharp
        Assert.Equal("major", attrs.KeyMode);
    }

    [Fact]
    public void ToXml_SlurHasNumberAttribute()
    {
        var source = "c4( d4)";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);
        var doc = xml.ToXml();

        var slurs = doc.Descendants("slur").ToList();
        Assert.Equal(2, slurs.Count);
        Assert.Equal("start", slurs[0].Attribute("type")?.Value);
        Assert.Equal("stop", slurs[1].Attribute("type")?.Value);
    }

    [Fact]
    public void ToXml_GraceWithSlash()
    {
        var source = @"acciaccatura { c8 } d4";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);
        var doc = xml.ToXml();

        var graces = doc.Descendants("grace").ToList();
        Assert.NotEmpty(graces);
        Assert.Equal("yes", graces[0].Attribute("slash")?.Value);
    }

    [Fact]
    public void ExportTransposedPart_RespellsPitchAndShiftsKey()
    {
        var source = @"
key c major
part melody { clef treble transpose d }
section Main { melody { c4 d e } }
form main { Main }
score main ""x"" { staff melody }";
        var tree = SyntaxTree.Parse(source);
        var xml = new MusicXmlExporter().Export(tree);
        var measure = xml.Parts[0].Measures[0];
        var notes = measure.Notes;

        // transpose: d respells the written pitch up a major 2nd: c d e -> d e fis.
        Assert.Equal("D", notes[0].Step);
        Assert.Equal(0, notes[0].Alter);
        Assert.Equal("E", notes[1].Step);
        Assert.Equal(0, notes[1].Alter);
        Assert.Equal("F", notes[2].Step);
        Assert.Equal(1, notes[2].Alter); // e -> fis (F#)

        // The key moves with the music: C major (0) -> D major (2 sharps).
        Assert.NotNull(measure.Attributes);
        Assert.Equal(2, measure.Attributes.KeyFifths);
    }

    [Fact]
    public void ExportMidPieceTempo_EmitsDirection()
    {
        var source = @"
tempo 120
time 4/4
part m { clef treble }
section Main { m { c4 d e f | tempo 160 g a b c } }
form main { Main }
score main ""x"" { staff m }";
        var tree = SyntaxTree.Parse(source);
        var xml = new MusicXmlExporter().Export(tree);
        var measures = xml.Parts[0].Measures;

        // The second measure carries a tempo direction of 160.
        Assert.Contains(measures[1].Directions, d => d.Tempo == 160);
    }
}
