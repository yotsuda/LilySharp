using Xunit;
using Lilysharp.Core.Syntax;
using Lilysharp.Core.MusicXml;

namespace Lilysharp.Tests;

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
        var source = @"relative c' { c d e f }";
        var tree = SyntaxTree.Parse(source);
        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);
        
        var notes = xml.Parts[0].Measures[0].Notes;
        Assert.Equal(4, notes.Count);
        Assert.Equal(5, notes[0].Octave); // c' = octave 5
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
        Assert.Equal(4, attrs.Divisions);
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
}