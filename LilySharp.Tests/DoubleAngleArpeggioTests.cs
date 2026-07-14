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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The <c>&lt;&lt; … &gt;&gt;</c> arpeggio: sequential notes (each with its own duration)
/// whose octaves anchor to the FIRST note like a chord. A <c>\\</c> inside keeps the
/// removed-polyphony migration error.
/// </summary>
[Trait("Category", "Unit")]
public class DoubleAngleArpeggioTests
{
    private static int[] Pitches(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes.Select(n => n.Pitch).ToArray();

    [Fact]
    public void DoubleAngle_WithoutBackslash_ParsesAsArpeggio()
    {
        var tree = SyntaxTree.Parse("section A { m { << c e g >> } }\nform main { A }\nscore main { staff m }");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        Assert.Single(tree.GetRoot().DescendantNodes<ArpeggioSyntax>());
    }

    [Fact]
    public void DoubleAngle_WithBackslash_KeepsTheRemovedPolyphonyError()
    {
        // `<< a \\ b >>` is the OLD polyphony form — it must still yield the migration
        // error, not an arpeggio.
        var tree = SyntaxTree.Parse("section A { m { << c \\\\ e >> } }\nform main { A }\nscore main { staff m }");
        Assert.True(tree.HasErrors);
        Assert.Empty(tree.GetRoot().DescendantNodes<ArpeggioSyntax>());
    }

    [Fact]
    public void OctavesAnchorToTheFirstNote_AndNotesPlayInSequence()
    {
        // c e g e anchored to the first c → C4 E4 G4 E4. The SECOND group's c returns to
        // the same C4 (the running reference after the group is the FIRST note), instead
        // of drifting up from the previous g.
        var pitches = Pitches("section A { m { << c8 e g e >> << c8 e g e >> } }\nform main { A }\nscore main { staff m }");
        Assert.Equal(new[] { 60, 64, 67, 64, 60, 64, 67, 64 }, pitches);
    }

    [Fact]
    public void LeadingRest_DoesNotBecomeTheRoot_TheFirstPitchAnchors()
    {
        // A rest before the first pitch must NOT anchor the group (it has no pitch): the
        // first PITCHED member `e` is the root, so the following `c` stacks ABOVE it (C5),
        // exactly as `<< e c >>` does. Previously the leading rest made the root fall back
        // to 'c', dropping the c to C4.
        Assert.Equal(
            Pitches("section A { m { << e c >> } }\nform main { A }\nscore main { staff m }"),
            Pitches("section A { m { << r8 e c >> } }\nform main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void EachNoteStacksAboveTheRoot_LikeAChord()
    {
        // `<< c g >>` — g is the fifth ABOVE c (G4), exactly like the chord `<c g>`, NOT
        // the nearer G3 below.
        Assert.Equal(new[] { 60, 67 },
            Pitches("section A { m { << c g >> } }\nform main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void MemberOctavesAreIndependentOfWrittenOrder_AndMatchTheChord()
    {
        // Excluding the root, the members' octaves do NOT depend on the order written —
        // `<< c e g >>` and `<< c g e >>` sound the same pitches (only the SEQUENCE
        // differs) — and they match the chord `<c e g>`.
        int[] Sorted(string m) => Pitches($"section A {{ m {{ {m} }} }}\nform main {{ A }}\nscore main {{ staff m }}").OrderBy(p => p).ToArray();
        var ceg = Sorted("<< c e g >>");
        Assert.Equal(ceg, Sorted("<< c g e >>"));
        Assert.Equal(ceg, Sorted("<c e g>"));
    }

    [Fact]
    public void TrailingDuration_MakesAnAutoTuplet()
    {
        // `<< c e g >>2` fits 3 quarters (3/4) into a half note (1/2) → a 3:2 triplet;
        // in 2/4 it fills the measure exactly and draws a "3" bracket.
        var src = "time 2/4\nsection A { m { << c e g >>2 } }\nform main { A }\nscore main { staff m }";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "m");
        Assert.Single(score.TupletBrackets);
        Assert.Equal(3, score.TupletBrackets[0].Numerator);
        Assert.Equal(2, score.TupletBrackets[0].Denominator);
        Assert.Equal(new Fraction(1, 2), score.Voice.Measures[0].TotalDuration);
    }

    [Fact]
    public void WithoutTrailingDuration_ThereIsNoTuplet()
    {
        var src = "section A { m { << c e g >> } }\nform main { A }\nscore main { staff m }";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "m");
        Assert.Empty(score.TupletBrackets);
    }

    [Fact]
    public void MusicCompletions_OfferTheArpeggioSnippet()
    {
        // In a music block, Ctrl+Space offers the `<< >>` arpeggio snippet, like `tuplet`.
        var arp = LilySharpLanguageServer.GetMusicCompletions("", 0).Items.Single(i => i.Label == "<< >>");
        Assert.Equal("<< $0 >>", arp.InsertText);
    }

    [Fact]
    public void MusicXmlExport_StacksMembersAboveTheRoot()
    {
        // Exported MusicXML places g in octave 4 (a fifth ABOVE c), matching the render —
        // not the relative-nearest g3. (Previously the arpeggio was dropped entirely.)
        var src = "section A { m { << c g >> } }\nform main { A }\nscore main { staff m }";
        var path = System.IO.Path.GetTempFileName();
        try
        {
            new LilySharp.Core.MusicXml.MusicXmlExporter().ExportToFile(SyntaxTree.Parse(src), path);
            var xml = System.IO.File.ReadAllText(path);
            Assert.Matches(@"<step>C</step>\s*<octave>4</octave>", xml);
            Assert.Matches(@"<step>G</step>\s*<octave>4</octave>", xml);
        }
        finally { System.IO.File.Delete(path); }
    }

    private static bool MeasureOverflows(string src)
    {
        var v = new LilySharp.Core.Semantics.MeasureValidator();
        v.Validate(SyntaxTree.Parse(src));
        return v.Diagnostics.Any(d => d.Code == DiagnosticCodes.MeasureOverflow);
    }

    [Fact]
    public void ArpeggioOverflowingItsMeasure_IsFlagged()
    {
        // `<< c e g >>` is 3 quarters; in 2/4 it would cross the barline — the within-
        // measure constraint surfaces as the measure exceeding the meter.
        Assert.True(MeasureOverflows(
            "time 2/4\nsection A { m { << c e g >> } }\nform main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void ArpeggioFittingViaAutoTuplet_StaysWithinItsMeasure()
    {
        // `<< c e g >>2` fits a half note = the whole 2/4 bar, so no overflow.
        Assert.False(MeasureOverflows(
            "time 2/4\nsection A { m { << c e g >>2 } }\nform main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void RestMember_IsAGapAndDoesNotAffectStacking()
    {
        // `<< c8 r e g >>` — the rest is a gap; e and g still stack above c.
        var src = "section A { m { << c8 r e g >> } }\nform main { A }\nscore main { staff m }";
        var notes = new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes;
        Assert.Equal(new[] { 60, 64, 67 }, notes.Select(n => n.Pitch).ToArray());
        // The rest inserts a gap: c→e spans two eighths (note + rest), e→g only one.
        Assert.True(notes[1].StartTick - notes[0].StartTick > notes[2].StartTick - notes[1].StartTick);
    }

    [Fact]
    public void NestedChord_SoundsStacked_ThenTheSequenceContinues()
    {
        // `<< <c e> g >>` — a chord may be a member: its c and e sound together, then g
        // follows in sequence (an arpeggio of a chord + a note).
        var src = "section A { m { << <c e>8 g >> } }\nform main { A }\nscore main { staff m }";
        var notes = new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes;
        Assert.Equal(3, notes.Count);
        Assert.Equal(notes[0].StartTick, notes[1].StartTick); // the chord's two notes coincide
        Assert.True(notes[2].StartTick > notes[1].StartTick); // g follows the chord
        Assert.Contains(60, notes.Select(n => n.Pitch));       // c
        Assert.Contains(64, notes.Select(n => n.Pitch));       // e (anchored to c)
    }

    [Fact]
    public void InnerDurationCarries_AndNotesAreSequentialNotStacked()
    {
        // `<< c8 e g >>` — e and g inherit the eighth; three eighths at distinct onsets.
        var src = "section A { m { << c8 e g >> } }\nform main { A }\nscore main { staff m }";
        var notes = new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes;
        Assert.Equal(3, notes.Count);
        var starts = notes.Select(n => n.StartTick).ToArray();
        Assert.True(starts[0] < starts[1] && starts[1] < starts[2],
            $"onsets should be strictly increasing, got [{string.Join(", ", starts)}]");
    }
}
