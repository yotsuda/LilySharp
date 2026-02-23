using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Tests for courtesy (cautionary) accidental rendering.
/// LILYPOND-REF: lily/accidental-engraver.cc, lily/accidental.cc:35-46
/// </summary>
[Trait("Category", "Unit")]
public class CourtesyAccidentalTests
{
    private readonly ITestOutputHelper _output;

    public CourtesyAccidentalTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AutoCourtesy_AfterBarline_SharpThenNatural()
    {
        // cis in measure 1, c (natural) in measure 2 → courtesy natural
        var source = @"
part melody { clef: treble }
phrase m { cis'4 d e f | c4 d e f | }
section A { melody { $m } }
structure { A }
render score """"test.svg"""" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree, "melody");

        // Measure 2, first note (c) should have courtesy accidental
        Assert.True(score.Voice.Measures.Length >= 2);
        var measure2 = score.Voice.Measures[1];
        var firstNote = measure2.Items[0] as NoteItem;
        Assert.NotNull(firstNote);
        Assert.Equal("natural", firstNote.Accidental);
        Assert.True(firstNote.IsCourtesy);
    }

    [Fact]
    public void AutoCourtesy_AfterBarline_FlatThenNatural()
    {
        // bes in measure 1, b (natural) in measure 2 → courtesy natural
        var source = @"
part melody { clef: treble }
phrase m { bes'4 c d e | b4 c d e | }
section A { melody { $m } }
structure { A }
render score """"test.svg"""" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree, "melody");

        Assert.True(score.Voice.Measures.Length >= 2);
        var measure2 = score.Voice.Measures[1];
        var firstNote = measure2.Items[0] as NoteItem;
        Assert.NotNull(firstNote);
        Assert.Equal("natural", firstNote.Accidental);
        Assert.True(firstNote.IsCourtesy);
    }

    [Fact]
    public void NoCourtesy_SameMeasure_NoBarlineCrossing()
    {
        // c after cis in same measure: current accidental engine only checks key signature,
        // so c (matching C major key sig) shows no accidental at all (not even courtesy)
        var source = @"
part melody { clef: treble }
phrase m { cis'4 c d e | }
section A { melody { $m } }
structure { A }
render score """"test.svg"""" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree, "melody");

        var measure = score.Voice.Measures[0];
        // Second note (c) matches key signature → no accidental displayed
        var secondNote = measure.Items[1] as NoteItem;
        Assert.NotNull(secondNote);
        Assert.Null(secondNote.Accidental);
        Assert.False(secondNote.IsCourtesy);
    }

    [Fact]
    public void NoCourtesy_NoPreviousAlteration()
    {
        // No alteration in previous measure → no courtesy
        var source = @"
part melody { clef: treble }
phrase m { c'4 d e f | c4 d e f | }
section A { melody { $m } }
structure { A }
render score """"test.svg"""" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree, "melody");

        var measure2 = score.Voice.Measures[1];
        var firstNote = measure2.Items[0] as NoteItem;
        Assert.NotNull(firstNote);
        Assert.Null(firstNote.Accidental);  // No accidental at all
        Assert.False(firstNote.IsCourtesy);
    }

    [Fact]
    public void ExplicitCourtesy_Annotation()
    {
        // @courtesy annotation forces parenthesized accidental
        var source = @"
part melody { clef: treble }
phrase m { c'4@courtesy d e f | }
section A { melody { $m } }
structure { A }
render score """"test.svg"""" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree, "melody");

        var measure = score.Voice.Measures[0];
        var firstNote = measure.Items[0] as NoteItem;
        Assert.NotNull(firstNote);
        Assert.NotNull(firstNote.Accidental);  // Forced accidental
        Assert.True(firstNote.IsCourtesy);
    }

    [Fact]
    public void CourtesyAccidental_RenderedWithParentheses()
    {
        // Courtesy accidental should render parenthesis glyphs in SVG
        var source = @"
part melody { clef: treble }
phrase m { cis'4 d e f | c4 d e f | }
section A { melody { $m } }
structure { A }
render score """"test.svg"""" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        var svg = SvgGenerator.Generate(tree, options);

        _output.WriteLine(svg);

        // Should contain the parenthesis glyphs (U+E02F = leftparen, U+E02E = rightparen)
        Assert.Contains("\uE02F", svg);  // AccidentalLeftParen
        Assert.Contains("\uE02E", svg);  // AccidentalRightParen
    }

    [Fact]
    public void AccidentalPlacement_IsCourtesy_Propagated()
    {
        // AccidentalPlacement should preserve IsCourtesy from ChordNoteInfo
        var notes = new[]
        {
            new ChordNoteInfo(0, "sharp", false, true),    // courtesy sharp
            new ChordNoteInfo(2, "natural", false, false),  // normal natural
        };

        var placement = new AccidentalPlacement();
        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);

        var courtesyLayout = layouts.First(l => l.StaffPosition == 0);
        var normalLayout = layouts.First(l => l.StaffPosition == 2);

        Assert.True(courtesyLayout.IsCourtesy);
        Assert.False(normalLayout.IsCourtesy);
    }

    [Fact]
    public void AccidentalPlacement_CourtesyWider()
    {
        // Courtesy accidental should have wider spacing (includes paren width)
        var notesCourtesy = new[] { new ChordNoteInfo(0, "sharp", false, true) };
        var notesNormal = new[] { new ChordNoteInfo(0, "sharp", false, false) };

        var placement = new AccidentalPlacement();
        var courtesyLayout = placement.CalculatePositions(notesCourtesy);
        var normalLayout = placement.CalculatePositions(notesNormal);

        // Courtesy XOffset should be more negative (further left) due to paren width
        Assert.True(courtesyLayout[0].XOffset < normalLayout[0].XOffset);
    }

    [Fact]
    public void KeySignature_SharpKey_CourtesyNatural()
    {
        // In key of G major (1 sharp = F#), if F# appears then F natural → courtesy
        var source = @"
part melody { clef: treble }
key g major
phrase m { fis'4 g a b | f4 g a b | }
section A { melody { $m } }
structure { A }
render score """"test.svg"""" { staff { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree, "melody");

        // In G major, F# matches key sig (no accidental shown in measure 1)
        // In measure 2, F natural differs from key sig → shown as normal accidental (not courtesy)
        var measure2 = score.Voice.Measures[1];
        var firstNote = measure2.Items[0] as NoteItem;
        Assert.NotNull(firstNote);
        Assert.Equal("natural", firstNote.Accidental);
        // F natural in G major is a regular accidental (differs from key sig F#), not courtesy
        Assert.False(firstNote.IsCourtesy);
    }
}
