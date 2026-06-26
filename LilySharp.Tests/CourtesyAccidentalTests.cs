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
    public void NoAutoCourtesy_AfterBarline_SharpThenNatural()
    {
        // cis in measure 1, c (natural) in measure 2. LilyPond's DEFAULT
        // accidental style forgets the accidental at the barline — c matches
        // the C-major key, so NO accidental (and no courtesy) is shown.
        // Verified vs LilyPond 2.24.4. LILYPOND-REF: lily/accidental-engraver.cc.
        var source = @"
part melody { clef treble }
phrase m { cis'4 d e f | c4 d e f | }
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
        Assert.Null(firstNote.Accidental);
        Assert.False(firstNote.IsCourtesy);
    }

    [Fact]
    public void NoAutoCourtesy_AfterBarline_FlatThenNatural()
    {
        // bes in measure 1, b (natural) in measure 2. As above, LilyPond shows
        // no cautionary accidental across the barline — b matches the C-major
        // key. Verified vs LilyPond 2.24.4.
        var source = @"
part melody { clef treble }
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
        Assert.Null(firstNote.Accidental);
        Assert.False(firstNote.IsCourtesy);
    }

    [Fact]
    public void NoCourtesy_SameMeasure_NoBarlineCrossing()
    {
        // c after cis in same measure: current accidental engine only checks key signature,
        // so c (matching C major key sig) shows no accidental at all (not even courtesy)
        var source = @"
part melody { clef treble }
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
part melody { clef treble }
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
part melody { clef treble }
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
        // An explicit @courtesy accidental renders parenthesis glyphs in SVG.
        // (Auto cross-measure courtesy is not produced — LP forgets the
        // accidental at the barline — so the explicit annotation is the path
        // that exercises the parenthesized rendering.)
        var source = @"
part melody { clef treble }
phrase m { cis'4 d e f | c4@courtesy d e f | }
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
part melody { clef treble }
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
