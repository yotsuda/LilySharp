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
    private static Score Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        return new MeasureCollector().Collect(tree, null);
    }

    private static List<NoteItem> Notes(Score score) =>
        score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    [Fact]
    public void RegionMarksItsNotesAndOnlyItsNotes()
    {
        var notes = Notes(Collect("{ c'4 d' cue { e'4 f' } | }"));
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
        var notes = Notes(Collect("{ cue { c'4 d' } e'4 f' | }"));
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
        Assert.Contains("\\cueClef bass", ly);
        Assert.Contains("\\cueClefUnset", ly);
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
