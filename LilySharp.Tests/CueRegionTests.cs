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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The <c>cue { … }</c> REGION — the shape LilyPond actually has.
/// </summary>
/// <remarks>
/// LilyPond has no per-note cue: its cue is the <c>CueVoice</c> context and the size comes
/// from <c>fontSize = #-4</c>, a CONTEXT property (ly/engraver-init.ly). A per-note mark
/// cannot say where the region starts and ends, and the boundary is observable — MEASURED in
/// audit/lp-geometry/probes/cue-span.ly, a beam, a tie and a slur all fail to cross it.
/// See docs/cue-context-design.md.
/// </remarks>
[Trait("Category", "Unit")]
public class CueRegionTests
{
    /// <summary>Collects a music FRAGMENT — music no longer stands alone as a file.</summary>
    private static Score Collect(string music)
    {
        var tree = MusicSource.Parse(music);
        Assert.False(tree.HasErrors);
        return new MeasureCollector().Collect(tree, null);
    }

    private static List<NoteItem> Notes(Score score) =>
        score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    [Fact]
    public void RegionMarksItsNotesAndOnlyItsNotes()
    {
        var notes = Notes(Collect("c'4 d' cue { e'4 f' } |"));
        Assert.Equal(4, notes.Count);
        Assert.False(notes[0].IsCue);
        Assert.False(notes[1].IsCue);
        Assert.True(notes[2].IsCue);
        Assert.True(notes[3].IsCue);
    }

    /// <summary>
    /// The region ENDS. A cue that leaked past its closing brace would be invisible in the
    /// model and show up only as a font size in the SVG, which is exactly how the first cut
    /// of the collector failed (the outer walk flattened the region and every note came out
    /// full size).
    /// </summary>
    [Fact]
    public void RegionEndsAtItsClosingBrace()
    {
        var notes = Notes(Collect("cue { c'4 d' } e'4 f' |"));
        Assert.Equal(4, notes.Count);
        Assert.True(notes[0].IsCue);
        Assert.True(notes[1].IsCue);
        Assert.False(notes[2].IsCue);
        Assert.False(notes[3].IsCue);
    }

    /// <summary>
    /// A cue is ORDINARY METRIC TIME, unlike a grace. When its body was not counted, every
    /// bar holding one validated as short — `c4 d cue { e4 f }` drew LYS2006 on a bar that
    /// is exactly full.
    /// </summary>
    [Fact]
    public void RegionCountsTowardsTheBar()
    {
        var tree = SyntaxTree.Parse("time 4/4\npart m\nsection A { m { c'4 d' cue { e'4 f' } | } }\nform main { A }\nscore main \"x\" { staff m }");
        var diags = SemanticValidation.Run(tree);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.PickupWithoutPartial);
    }

    /// <summary>A full document: the exporter walks parts and sections, not a bare block.</summary>
    private static string Doc(string music) =>
        "time 4/4\npart m\nsection A { m { " + music + " } }\nform main { A }\n"
        + "score main \"x\" { staff m }";

    [Fact]
    public void ExporterEmitsACueVoice()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Doc("c'4 d' cue { e'4 f' } |")));
        Assert.Contains("\\new CueVoice {", ly);
    }

    /// <summary>
    /// ⚠️ BOTH clefs, not just the opening one: MEASURED (probe cue-span.ly, book D-NOUNSET)
    /// LilyPond leaks the cue clef into the rest of the staff without the unset — the note
    /// after the region read staff position 13 instead of 1.
    /// </summary>
    [Fact]
    public void ExporterEmitsBothCueClefs()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Doc("c'2 cue bass { e2 } |")));
        // QUOTED — \cueClef is declared (type) (string?) and the octave modifier of a name
        // like treble_8 is parsed OUT OF THE STRING, so a bare name arrives already split.
        Assert.Contains("\\cueClef \"bass\"", ly);
        Assert.Contains("\\cueClefUnset", ly);
    }

    /// <summary>
    /// A cue REGION that fills a whole measure of a <c>voice { } { }</c> span is walked
    /// ONCE. The per-voice flatten walks hand-rolled their container skip list and both
    /// copies were missing the cue test, so the region's body was collected twice — once
    /// through <c>ProcessCueRegion</c> (cue-sized) and once flattened (full size). The
    /// duplicate 4/4 rolled the measure: the piece gained a bar the exporter does not
    /// have (`lysc ly` and `lysc layout` disagreed on the bar count, with no warning).
    /// </summary>
    [Fact]
    public void WholeMeasureCueInTheFirstVoiceBlockIsWalkedOnce()
    {
        var score = Collect("c'4 d' e' f' | voice { cue { aes'4 r r r } } { g'4 r r r } |");

        // Was 3 with the drifted skip list: the flattened duplicate rolled a third bar.
        Assert.Equal(2, score.Voices[0].Measures.Length);

        var bar2 = score.Voices[0].Measures[1];
        var notes = bar2.Items.OfType<NoteItem>().ToList();
        var note = Assert.Single(notes);   // was 2: cue-sized aes + its full-size twin
        Assert.True(note.IsCue);
        Assert.Equal(3, bar2.Items.OfType<RestItem>().Count());
    }

    /// <summary>
    /// Same defect, other orientation: the cue in a NON-first voice block goes through
    /// <c>CollectMeasuresFromNode</c> (the second drifted copy of the skip list). There the
    /// duplicate bar did not change the piece's bar count — it silently OVERLAID the
    /// sub-voice's empty placeholder in the NEXT measure, drawing a full-size copy of the
    /// cue on top of whatever the primary voice has there.
    /// </summary>
    [Fact]
    public void WholeMeasureCueInASecondVoiceBlockDoesNotLeakIntoTheNextMeasure()
    {
        var score = Collect("voice { g'4 r r r } { cue { aes'4 r r r } } | c'4 d' e' f' |");

        Assert.Equal(2, score.Voices.Length);
        Assert.Equal(2, score.Voices[0].Measures.Length);

        var cueBar = score.Voices[1].Measures[0];
        var note = Assert.Single(cueBar.Items.OfType<NoteItem>().ToList());
        Assert.True(note.IsCue);

        // Was a full-size aes + 3 rests: the flattened duplicate spilled past the span.
        Assert.Empty(score.Voices[1].Measures[1].Items);
    }

    [Fact]
    public void NestedCueIsRejected()
    {
        var tree = SyntaxTree.Parse("{ cue { c'4 cue { d'4 } } | }");
        var diags = SemanticValidation.Run(tree);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.NestedCueBlock);
    }

    [Fact]
    public void VoiceSpanInsideCueIsRejected()
    {
        var tree = SyntaxTree.Parse("{ cue { voice { c'4 } voice { e'4 } } | }");
        var diags = SemanticValidation.Run(tree);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.VoiceInsideCue);
    }

    // ── LYS4012: a slur or tie with one end inside the region and the other outside ──────
    //
    // MEASURED on LilyPond 2.26.0, 2026-08-15 (probes in scratch/cue-span-probe, twins from
    // `lysc ly` rather than hand-written). LilyPond refuses ALL FOUR crossings, and Lily#
    // accepted all four with "No errors found" while drawing the curve:
    //
    //   slur into a cue    "cannot end slur" + "unterminated slur", no Slur grob
    //   slur out of a cue  the same pair
    //   tie into a cue     "unterminated tie", no Tie grob
    //   tie out of a cue   NOT ONE WORD — and no tie: that book engraves byte-for-byte
    //                      (SHA A036D1151272) as the same bar with no `~` written at all
    //
    // The last row is why this is worth a code of its own. Neither tool was saying anything,
    // so the only signal a writer got was the curve Lily# drew, which LilyPond will never make.

    private static IReadOnlyList<Diagnostic> Check(string music) =>
        SemanticValidation.Run(SyntaxTree.Parse(Doc(music)));

    private static void AssertCrossesCueBoundary(string music)
    {
        var d = Assert.Single(Check(music)
            .Where(x => x.Code == DiagnosticCodes.SpanCrossesCueBoundary).ToList());
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    // ⚠️ Octaves are RELATIVE after the first note, and a tie needs the SAME pitch at both
    // ends: written `c'4 e'~ cue { e'4 … }` the second e' is an octave above the first, the
    // tie does not bind, and these tests went green for the wrong reason (they reported a
    // LYS4007 pitch mismatch instead). Keep the `'` on the anchor note only.

    [Fact]
    public void SlurIntoACueIsRejected() =>
        AssertCrossesCueBoundary("c'4( d cue { e4) f } |");

    [Fact]
    public void SlurOutOfACueIsRejected() =>
        AssertCrossesCueBoundary("c'4 cue { e4( f } g4) |");

    [Fact]
    public void TieIntoACueIsRejected() =>
        AssertCrossesCueBoundary("c'4 e~ cue { e4 f } |");

    /// <summary>The one LilyPond drops in silence — see the table above.</summary>
    [Fact]
    public void TieOutOfACueIsRejected() =>
        AssertCrossesCueBoundary("c'4 cue { e4 f~ } f4 |");

    /// <summary>
    /// The spellings LilyPond engraves without a word, so Lily# must not complain either:
    /// every span closing in the region it opened in.
    /// </summary>
    [Fact]
    public void SpansThatCloseInTheirOwnRegionAreAccepted()
    {
        Assert.Empty(Check("c'4( d) cue { e4( f) } |\nc'4 cue { e4 g~ g4 } |")
            .Where(d => d.Code == DiagnosticCodes.SpanCrossesCueBoundary));
    }

    /// <summary>
    /// ⚠️ A slur PASSING OVER a whole cue region is not a crossing: both of its ends are in
    /// the enclosing voice. MEASURED — LilyPond pairs
    /// <c>c4( \new CueVoice { e4 f } g4)</c> without a warning, so a rule that looked at
    /// "is there a cue anywhere between the ends" instead of at the ends themselves would
    /// reject music LilyPond accepts.
    /// </summary>
    [Fact]
    public void ASlurPassingOverAWholeCueIsAccepted()
    {
        Assert.Empty(Check("c'4( cue { e4 f } g4) |")
            .Where(d => d.Code == DiagnosticCodes.SpanCrossesCueBoundary));
    }

    /// <summary>
    /// A tie that fails to bind at all is reported ONCE, as the larger problem (LYS4007), and
    /// does not also collect a boundary complaint about the mark it never made.
    /// </summary>
    [Fact]
    public void ATieThatMissesItsTargetIsNotAlsoCalledACrossing()
    {
        var diags = Check("c'4 e~ cue { g4 f } |");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.TieTargetMismatch);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.SpanCrossesCueBoundary);
    }

    /// <summary>
    /// <c>@cue</c> is gone, and it fails at the PARSER rather than in a semantic pass: once
    /// <c>cue</c> is a keyword, <c>@cue</c> is an '@' with no annotation name after it.
    /// </summary>
    /// <remarks>
    /// That is the whole farewell, on purpose — Lily# is pre-release and a removal does not
    /// earn a dedicated code (a retired LYS number is never reused either). Recorded as a
    /// test because it is the message anyone with an old book will actually see.
    /// </remarks>
    [Fact]
    public void TheOldPerNoteAnnotationNoLongerParses()
    {
        var tree = SyntaxTree.Parse(Doc("c'4 d'@cue r2 |"));
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics,
            d => d.Message.Contains("Expected articulation or dynamic name after '@'"));
    }
}
