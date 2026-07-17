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
/// The <c>&lt;&lt; … &gt;&gt;</c> arpeggio: a written-out broken chord whose durationless
/// members play in sequence, EQUALLY SUBDIVIDING the group's total (an auto-tuplet when the
/// share is not a plain note value) and anchoring their octaves to the FIRST pitched member
/// like a chord. A bare number is a scale degree (<c>&lt;&lt; c 3 5 &gt;&gt;</c>), never a
/// duration. A <c>\\</c> inside keeps the removed-polyphony migration error.
/// </summary>
[Trait("Category", "Unit")]
public class DoubleAngleArpeggioTests
{
    private const string Tail = "\nform main { A }\nscore main { staff m }";

    private static int[] Pitches(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes.Select(n => n.Pitch).ToArray();

    private static SyntaxTree Parse(string music) =>
        SyntaxTree.Parse($"section A {{ m {{ {music} }} }}{Tail}");

    private static int[] MidiPitches(string music) =>
        new MidiExporter().Export(Parse(music)).Tracks[1].Notes.Select(n => n.Pitch).ToArray();

    private static LilySharp.Core.Svg.Model.Score Collect(string music) =>
        new MeasureCollector().Collect(Parse(music), "m");

    [Fact]
    public void DoubleAngle_WithoutBackslash_ParsesAsArpeggio()
    {
        var tree = Parse("<< c e g >>");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        Assert.Single(tree.GetRoot().DescendantNodes<ArpeggioSyntax>());
    }

    [Fact]
    public void DoubleAngle_WithBackslash_KeepsTheRemovedPolyphonyError()
    {
        // `<< a \\ b >>` is the OLD polyphony form — it must still yield the migration
        // error, not an arpeggio.
        var tree = Parse("<< c \\\\ e >>");
        Assert.True(tree.HasErrors);
        Assert.Empty(tree.GetRoot().DescendantNodes<ArpeggioSyntax>());
    }

    // ----- degrees: a bare number is a scale degree, not a duration -----

    [Fact]
    public void BareNumberMembers_AreScaleDegrees_ResolvedAgainstTheRootAndKey()
    {
        // `<< c 3 5 >>` = root c + the 3rd + the 5th of C major = c e g. This is the whole
        // point of the rework: `3`/`5` used to be parsed as (invalid) durations.
        Assert.Equal(new[] { 60, 64, 67 }, MidiPitches("<< c 3 5 >>"));
        // `<< d 3 5 >>` = d f a (the 3rd/5th above d in C major).
        Assert.Equal(new[] { 62, 65, 69 }, MidiPitches("<< d 3 5 >>"));
    }

    [Fact]
    public void DegreeArpeggio_ParsesWithoutError()
    {
        var tree = Parse("<< c 3 5 >>");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
    }

    [Fact]
    public void DegreeMembers_MatchTheEquivalentDegreeChord()
    {
        // `<< c 3 5 7 >>` sounds the same pitches as the chord `<c 3 5 7>` (only the
        // sequence differs), so degree resolution is shared.
        int[] Sorted(string m) => MidiPitches(m).OrderBy(p => p).ToArray();
        Assert.Equal(Sorted("<c 3 5 7>"), Sorted("<< c 3 5 7 >>"));
    }

    // ----- octave anchoring (preserved from the pre-rework behavior) -----

    [Fact]
    public void OctavesAnchorToTheFirstNote_AndNotesPlayInSequence()
    {
        // c e g e anchored to the first c → C4 E4 G4 E4. The SECOND group's c returns to
        // the same C4 (the running reference after the group is the FIRST note), instead
        // of drifting up from the previous g.
        Assert.Equal(new[] { 60, 64, 67, 64, 60, 64, 67, 64 },
            MidiPitches("<< c e g e >> << c e g e >>"));
    }

    [Fact]
    public void LeadingRest_DoesNotBecomeTheRoot_TheFirstPitchAnchors()
    {
        // A rest before the first pitch must NOT anchor the group: the first PITCHED member
        // `e` is the root, so the following `c` stacks ABOVE it (C5), exactly as `<< e c >>`.
        Assert.Equal(MidiPitches("<< e c >>"), MidiPitches("<< r e c >>"));
    }

    [Fact]
    public void EachNoteStacksAboveTheRoot_LikeAChord()
    {
        // `<< c g >>` — g is the fifth ABOVE c (G4), exactly like the chord `<c g>`, NOT
        // the nearer G3 below.
        Assert.Equal(new[] { 60, 67 }, MidiPitches("<< c g >>"));
    }

    [Fact]
    public void MemberOctavesAreIndependentOfWrittenOrder_AndMatchTheChord()
    {
        // Excluding the root, the members' octaves do NOT depend on the order written, and
        // they match the chord `<c e g>`.
        int[] Sorted(string m) => MidiPitches(m).OrderBy(p => p).ToArray();
        var ceg = Sorted("<< c e g >>");
        Assert.Equal(ceg, Sorted("<< c g e >>"));
        Assert.Equal(ceg, Sorted("<c e g>"));
    }

    [Fact]
    public void OctaveMarksShiftFromTheStackedPosition()
    {
        // A member's ' shifts that ONE note from its stacked position: g' = G5 above
        // the untouched c e.
        Assert.Equal(new[] { 60, 64, 79 }, MidiPitches("<< c e g' >>"));
        // The marks are LOCAL — the root's included: `<< c' e' g' >>` is the close
        // position C5 E5 G5 (each member +1 from its own stacked spot), NOT a spread
        // E6/G6 — the root's ' does not move the anchor the others stack on.
        Assert.Equal(new[] { 72, 76, 79 }, MidiPitches("<< c' e' g' >>"));
    }

    [Fact]
    public void RootOctaveMark_IsLocal_AndDoesNotMoveTheAnchor()
    {
        // `<< c' e g >>` — the root SOUNDS at C5 but the group's anchor is its bare
        // LETTER (C4): e and g stack above C4, and the next bare c returns to C4.
        Assert.Equal(new[] { 72, 64, 67, 60 }, MidiPitches("<< c' e g >> c"));
    }

    [Fact]
    public void TrailingOctaveMark_MovesTheAnchor_AndPropagates()
    {
        // `<< c e g >>'` — the whole group up an octave AND the next bare note
        // continues in the new register (the anchor itself moved to C5).
        Assert.Equal(new[] { 72, 76, 79, 72 }, MidiPitches("<< c e g >>' c"));
    }

    [Fact]
    public void DegreeArpeggio_WritesDescendingFiguresWithoutMarks()
    {
        // Degrees self-describe their position above the tonic anchor, so a
        // descending figure needs no ',' marks: << 8 5 3 1 >> = C5 G4 E4 C4.
        Assert.Equal(new[] { 72, 67, 64, 60 }, MidiPitches("<< 8 5 3 1 >>"));
    }

    [Fact]
    public void TrailingOctaveMark_ShiftsTheWholeGroup()
    {
        // A ' / , AFTER the closing '>>' shifts the WHOLE group, exactly like a chord's
        // `<c e g>,`. `<< c e g >>` is C4 E4 G4 (60 64 67).
        Assert.Equal(new[] { 48, 52, 55 }, MidiPitches("<< c e g >>,"));   // down an octave
        Assert.Equal(new[] { 72, 76, 79 }, MidiPitches("<< c e g >>'"));   // up an octave
        Assert.Equal(new[] { 36, 40, 43 }, MidiPitches("<< c e g >>,,"));  // down two
    }

    [Fact]
    public void TrailingOctaveMark_AppliesToDegreesToo()
    {
        // `<< c 3 5 >>,` = c e g, one octave down.
        Assert.Equal(new[] { 48, 52, 55 }, MidiPitches("<< c 3 5 >>,"));
    }

    [Fact]
    public void TrailingOctaveMark_MatchesTheChordForm()
    {
        // The group octave mark is the same rule as a chord's `<c e g>,` — same pitches.
        int[] Sorted(string m) => MidiPitches(m).OrderBy(p => p).ToArray();
        Assert.Equal(Sorted("<c e g>,"), Sorted("<< c e g >>,"));
        Assert.Equal(Sorted("<c e g>'"), Sorted("<< c e g >>'"));
    }

    [Fact]
    public void TrailingOctaveMark_DoesNotChangeNoteValuesOrBeaming()
    {
        // A trailing ',' must only move the register — it must NOT alter the members'
        // note value (a stray, unconsumed comma used to leak into an extra beam). Both
        // groups are triplet EIGHTHS occupying one quarter.
        (Fraction, int)[] Shape(string m) => Collect(m).Voice.Measures[0].Items
            .OfType<LilySharp.Core.Svg.Model.NoteItem>()
            .Select(n => (n.BaseDuration, n.Dots)).ToArray();
        var sv = Shape("<< c e g >>,");
        Assert.Equal(Shape("<< c e g >>"), sv);
        Assert.All(sv, x => Assert.Equal(Fraction.Eighth, x.Item1));
    }

    // ----- equal subdivision -----

    [Fact]
    public void TrailingDuration_MakesAnAutoTuplet()
    {
        // `<< c e g >>2` fits 3 members into a half note → a 3:2 triplet; in 2/4 it fills
        // the measure exactly and draws a "3" bracket.
        var score = Collect("time 2/4 << c e g >>2");
        Assert.Single(score.TupletBrackets);
        Assert.Equal(3, score.TupletBrackets[0].Numerator);
        Assert.Equal(2, score.TupletBrackets[0].Denominator);
        Assert.Equal(new Fraction(1, 2), score.Voice.Measures[0].TotalDuration);
    }

    [Fact]
    public void WithoutTrailingDuration_TheGroupInheritsTheRunningDurationAndSubdividesIt()
    {
        // No trailing `N`: the group acts like a single (quarter) note. Three members split
        // that quarter into a triplet; the whole group still occupies just one quarter.
        var score = Collect("<< c e g >>");
        Assert.Single(score.TupletBrackets);
        Assert.Equal(3, score.TupletBrackets[0].Numerator);
        Assert.Equal(2, score.TupletBrackets[0].Denominator);
        Assert.Equal(Fraction.Quarter, score.Voice.Measures[0].TotalDuration);
    }

    [Fact]
    public void PowerOfTwoMemberCount_NeedsNoTuplet()
    {
        // Four members in a quarter are four sixteenths — a power of two, so no bracket.
        var score = Collect("<< c e g a >>");
        Assert.Empty(score.TupletBrackets);
        Assert.Equal(Fraction.Quarter, score.Voice.Measures[0].TotalDuration);
    }

    [Fact]
    public void FiveMembers_MakeAQuintuplet()
    {
        // `<< c d e f g >>4` — five members in a quarter → a 5:4 quintuplet of sixteenths.
        var score = Collect("<< c d e f g >>4");
        Assert.Single(score.TupletBrackets);
        Assert.Equal(5, score.TupletBrackets[0].Numerator);
        Assert.Equal(4, score.TupletBrackets[0].Denominator);
    }

    [Fact]
    public void NineMembers_MakeANonuplet()
    {
        // The guitar fast-picking case: nine members in a quarter → a 9:8 nonuplet.
        var score = Collect("<< c d e f g a b c d >>4");
        Assert.Single(score.TupletBrackets);
        Assert.Equal(9, score.TupletBrackets[0].Numerator);
        Assert.Equal(8, score.TupletBrackets[0].Denominator);
    }

    // ----- rests and nested chords as members -----

    [Fact]
    public void RestMember_IsAGapAndDoesNotAffectStacking()
    {
        // `<< c r e g >>` — the rest is a gap; e and g still stack above c.
        var notes = new MidiExporter().Export(Parse("<< c r e g >>")).Tracks[1].Notes;
        Assert.Equal(new[] { 60, 64, 67 }, notes.Select(n => n.Pitch).ToArray());
        // Equal shares: c→e spans two members (note + rest), e→g only one.
        Assert.True(notes[1].StartTick - notes[0].StartTick > notes[2].StartTick - notes[1].StartTick);
    }

    [Fact]
    public void NestedChord_SoundsStacked_ThenTheSequenceContinues()
    {
        // `<< <c e> g >>` — a chord may be a member: its c and e sound together, then g
        // follows in sequence (an arpeggio of a chord + a note).
        var notes = new MidiExporter().Export(Parse("<< <c e> g >>")).Tracks[1].Notes;
        Assert.Equal(3, notes.Count);
        Assert.Equal(notes[0].StartTick, notes[1].StartTick); // the chord's two notes coincide
        Assert.True(notes[2].StartTick > notes[1].StartTick); // g follows the chord
        Assert.Contains(60, notes.Select(n => n.Pitch));       // c
        Assert.Contains(64, notes.Select(n => n.Pitch));       // e (anchored to c)
    }

    [Fact]
    public void MembersAreSequential_NotStacked()
    {
        // The members sound at distinct, strictly increasing onsets (a broken chord), not
        // all at once.
        var notes = new MidiExporter().Export(Parse("<< c e g >>")).Tracks[1].Notes;
        Assert.Equal(3, notes.Count);
        var starts = notes.Select(n => n.StartTick).ToArray();
        Assert.True(starts[0] < starts[1] && starts[1] < starts[2],
            $"onsets should be strictly increasing, got [{string.Join(", ", starts)}]");
    }

    // ----- measure fit -----

    private static bool MeasureOverflows(string music)
    {
        var v = new MeasureValidator();
        v.Validate(Parse(music));
        return v.Diagnostics.Any(d => d.Code == DiagnosticCodes.MeasureOverflow);
    }

    [Fact]
    public void ArpeggioOverflowingItsMeasure_IsFlagged()
    {
        // `<< c e g >>1` occupies a whole note; in 2/4 it crosses the barline.
        Assert.True(MeasureOverflows("time 2/4 << c e g >>1"));
    }

    [Fact]
    public void ArpeggioFittingItsMeasure_StaysWithin()
    {
        // `<< c e g >>2` occupies a half note = the whole 2/4 bar, so no overflow. A bare
        // `<< c e g >>` occupies just the inherited quarter, which also fits.
        Assert.False(MeasureOverflows("time 2/4 << c e g >>2"));
        Assert.False(MeasureOverflows("time 2/4 << c e g >>"));
    }

    // ----- MusicXML -----

    [Fact]
    public void MusicXmlExport_StacksMembersAboveTheRoot()
    {
        // Exported MusicXML places g in octave 4 (a fifth ABOVE c), matching the render.
        var xml = ExportMusicXml("<< c g >>");
        Assert.Matches(@"<step>C</step>\s*<octave>4</octave>", xml);
        Assert.Matches(@"<step>G</step>\s*<octave>4</octave>", xml);
    }

    [Fact]
    public void MusicXmlExport_ResolvesDegreeMembers()
    {
        // `<< c 3 5 >>` exports c, e, g (the resolved degrees).
        var xml = ExportMusicXml("<< c 3 5 >>");
        Assert.Matches(@"<step>C</step>\s*<octave>4</octave>", xml);
        Assert.Matches(@"<step>E</step>\s*<octave>4</octave>", xml);
        Assert.Matches(@"<step>G</step>\s*<octave>4</octave>", xml);
    }

    [Fact]
    public void MusicXmlExport_EqualSubdivisionStampsATimeModification()
    {
        // `<< c e g >>4` is a triplet — three-in-the-time-of-two time-modification.
        var xml = ExportMusicXml("<< c e g >>4");
        Assert.Matches(@"<time-modification>\s*<actual-notes>3</actual-notes>\s*<normal-notes>2</normal-notes>", xml);
    }

    private static string ExportMusicXml(string music)
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            new LilySharp.Core.MusicXml.MusicXmlExporter().ExportToFile(Parse(music), path);
            return System.IO.File.ReadAllText(path);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void GroupDynamic_TakesEffectAtTheGroupStart()
    {
        // `<< c e g >>@p` sounds the members at the same velocity a plain
        // `c@p` sets — the dynamic acts as if written on the first member.
        int reference = new MidiExporter().Export(Parse("c@p")).Tracks[1].Notes.Single().Velocity;
        var notes = new MidiExporter().Export(Parse("<< c e g >>@p")).Tracks[1].Notes;
        Assert.All(notes, n => Assert.Equal(reference, n.Velocity));
    }

    [Fact]
    public void GroupDynamic_RendersAndExports()
    {
        // The collector anchors the dynamic on the group's first item; the
        // MusicXML export carries the <dynamics> direction.
        var score = Collect("<< c e g >>@f");
        Assert.Contains(score.Dynamics, d => d.MeasureIndex == 0 && d.ItemIndex == 0);
        Assert.Contains("<f />", ExportMusicXml("<< c e g >>@f"));
    }

    [Theory]
    [InlineData("<< c e g >>@staccato")]   // articulations are unwired on the group
    [InlineData("<< c e g >>@fermata")]
    [InlineData("<< c@staccato e g >>")]   // and on a bare member
    [InlineData("<< c@f e g >>")]          // a member dynamic: write it on the group
    public void UnsupportedGroupAnnotation_Warns(string music)
    {
        var diags = LilySharp.Core.Semantics.SemanticValidation.Run(Parse(music));
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.ArpeggioAnnotationUnsupported);
    }

    [Theory]
    [InlineData("<< c e g >>@f")]          // a group dynamic works
    [InlineData("<< c e g >>@chord")]      // a chord name works
    [InlineData("<< <c e>@arpeggio g >>")] // a nested chord keeps its own handling
    public void SupportedGroupAnnotation_StaysQuiet(string music)
    {
        var diags = LilySharp.Core.Semantics.SemanticValidation.Run(Parse(music));
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.ArpeggioAnnotationUnsupported);
    }

    [Fact]
    public void PostEvents_AttachAfterTheClosingAngles()
    {
        // '@chord' (and post-events generally) attach after '>>' like a chord's,
        // and the green tokens round-trip the source exactly.
        var src = "{ << c e g >>@chord }";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        Assert.Equal(src, tree.Root.ToFullString());
        var arp = tree.GetRoot().DescendantNodes<ArpeggioSyntax>().Single();
        Assert.Contains(arp.Articulations, a =>
            a is MusicMarkSyntax { MarkName: "chord" });
    }

    // ----- editor affordance -----

    [Fact]
    public void MusicCompletions_OfferTheArpeggioSnippet()
    {
        // In a music block, Ctrl+Space offers the `<< >>` arpeggio snippet, like `tuplet`.
        var arp = LilySharpLanguageServer.GetMusicCompletions("", 0).Items.Single(i => i.Label == "<< >>");
        Assert.Equal("<< $0 >>", arp.InsertText);
    }
}
