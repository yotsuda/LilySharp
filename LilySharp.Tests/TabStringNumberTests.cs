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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Bass tab support: the <c>\N</c> string-number annotation forces the fret's
/// string, bass tunings sound 8vb, and the note carries a clef-independent MIDI
/// pitch for fret calculation. LILYPOND-REF: lily/tab-note-heads-engraver.cc.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabStringNumberTests
{
    private static NoteItem FirstNote(string body)
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n" + body + "\n  }\n}\n" +
                  "form main { Main }\nscore \"x\" { staff bl }\n";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        return score.Voice.Measures[0].Items.OfType<NoteItem>().First();
    }

    [Fact]
    public void StringNumberAnnotation_IsParsedOntoTheNote()
    {
        // The \4 must reach the note (it used to be silently dropped).
        var note = FirstNote("a4\\4 b c d |");
        Assert.Equal(4, note.StringNumber);
    }

    [Fact]
    public void NoteCarriesClefIndependentMidi()
    {
        // The first 'a' resolves to A3 (MIDI 57); the note carries that absolute
        // pitch (NOT the clef-relative StaffPosition the tab must avoid for pitch).
        var note = FirstNote("a4 b c d |");
        Assert.Equal(57, note.Midi);
        Assert.Null(note.StringNumber);
    }

    [Theory]
    [InlineData(TuningType.Bass, -12)]
    [InlineData(TuningType.Bass5, -12)]
    [InlineData(TuningType.Bass6, -12)]
    [InlineData(TuningType.Guitar, 0)]
    [InlineData(TuningType.Ukulele, 0)]
    public void BassTuningsSound8vb(TuningType tuning, int expectedShift)
    {
        Assert.Equal(expectedShift, Tunings.TuningTransposition(tuning));
    }

    [Fact]
    public void Bass6Tuning_IsSixStringsLowBHighC()
    {
        Assert.Equal(6, Tunings.GetStringCount(TuningType.Bass6));
        // B0 E1 A1 D2 G2 C3
        Assert.Equal(new[] { 23, 28, 33, 38, 43, 48 }, Tunings.GetTuning(TuningType.Bass6));
    }

    [Fact]
    public void CalculateFret_HonoursPreferredString()
    {
        // A1 (33) forced onto string 4 (E1=28) of a 4-string bass = fret 5,
        // even though the open A string (string 3) would be fret 0.
        var (str, fret) = Tunings.CalculateFret(33, Tunings.Bass, preferredString: 4);
        Assert.Equal(4, str);
        Assert.Equal(5, fret);
    }

    /// <summary>
    /// The hand reaches <c>HandSpan</c> frets from where it sits — at 5 that is 5, 6, 7, 8 —
    /// and it stays there unless another string offers a position more than
    /// <c>HandShiftCost</c> frets lower.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (see <see cref="Tunings.CalculateFret"/>): not LilyPond's rule, which
    /// never looks at the hand and would answer fret 2 every time. E2 (40) on a 4-string bass
    /// is fret 12 on string 4 (E1=28), 7 on string 3 (A1=33) and 2 on string 2 (D2=38); no
    /// string plays it open, which is what makes it the case that separates the two rules —
    /// and, with three positions five frets apart, the case that shows where the shift cost
    /// bites. From 7 the drop to 2 is exactly a hand's width and is refused; from 9 or 12 it
    /// is worth more than that and taken.
    /// </remarks>
    [Theory]
    [InlineData(null, 2, 2)]  // hand nowhere: the lowest fret on the instrument
    [InlineData(2, 2, 2)]     // hand at 2 reaches 2..5 — it is already there
    [InlineData(7, 3, 7)]     // hand at 7 reaches 7..10: 7 ties with 2 + shift, so stay
    [InlineData(9, 2, 2)]     // hand at 9 reaches 9..12, but 12 loses to 2 + shift: come down
    [InlineData(12, 2, 2)]    // and the same from 12
    public void CalculateFret_StaysPutUnlessAClearlyLowerPositionIsOffered(
        int? hand, int expectedString, int expectedFret)
    {
        var (str, fret) = Tunings.CalculateFret(40, Tunings.Bass, 0, handPosition: hand);
        Assert.Equal(expectedString, str);
        Assert.Equal(expectedFret, fret);
    }

    /// <summary>
    /// An OPEN string needs no hand, so it is always reachable — and being fret 0 it always
    /// wins the "lowest" tie-break, wherever the hand happens to be.
    /// </summary>
    /// <remarks>
    /// This is also the whole of "a shift right after an open string is cheap": the note
    /// costs the hand nothing to play, and <c>TabResolver.PlaceHand</c> forgets the position
    /// when it sees one, so nothing keeps the NEXT note up the neck either.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    public void CalculateFret_TakesAnOpenStringWhereverTheHandIs(int? hand)
    {
        // G2 (43) is string 1's open pitch, and 15 / 10 / 5 on the strings below it.
        Assert.Equal((1, 0), Tunings.CalculateFret(43, Tunings.Bass, 0, handPosition: hand));
    }

    // ---- Tab tie behaviour ----

    private static (Score Score, MeasureCollector Collector) CollectBody(string body)
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n" + body + "\n  }\n}\n" +
                  "form main { Main }\nscore \"x\" { staff bl }\n";
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(src));
        return (score, collector);
    }

    private static List<NoteItem> Notes(Score score) =>
        score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    [Fact]
    public void TieDestination_IsFlaggedTieTarget()
    {
        // The held note (tie destination) is flagged so the tab hides its fret;
        // the struck note (source) is not.
        var notes = Notes(CollectBody("a4\\4~ a4 b4 |").Score);
        Assert.False(notes[0].IsTieTarget);
        Assert.True(notes[1].IsTieTarget);
    }

    [Fact]
    public void TieSource_AdoptsDestinationString_WhenSourceUnspecified()
    {
        // Source has no \N but the destination names string 3 → source adopts 3
        // (so the struck note sits on the held string). Not a conflict.
        var (score, collector) = CollectBody("a4~ a4\\3 b4 |");
        var notes = Notes(score);
        Assert.Equal(3, notes[0].StringNumber);
        Assert.True(notes[1].IsTieTarget);
        Assert.Empty(collector.TabTieWarnings);
    }

    [Fact]
    public void TieWithConflictingStrings_Warns_AndKeepsSourceString()
    {
        var (score, collector) = CollectBody("a4\\4~ a4\\3 b4 |");
        var w = Assert.Single(collector.TabTieWarnings);
        Assert.Equal(4, w.PreviousString);
        Assert.Equal(3, w.FollowingString);
        Assert.Equal(4, Notes(score)[0].StringNumber); // source string kept
    }

    // ---- Per-staff tab string resolution (inheritance + nearest fret) ----

    private static List<NoteItem> TabNotes(string body)
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n" + body + "\n  }\n}\n" +
                  "form main { Main }\nscore \"x\" { staff bass bl tab bass bl }\n";
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec);
        var tab = multi.EnumerateStaves().First(s => s.Staff.IsTab).Staff;
        return tab.PrimaryVoice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();
    }

    [Fact]
    public void RepeatedPitchInBar_ReusesFirstString()
    {
        // a\4 sets string 4; the bare a's in the same bar inherit it (accidental-like).
        var notes = TabNotes("a8\\4 a a a a a a a |");
        Assert.All(notes, n => Assert.Equal(4, n.StringNumber));
    }

    // ---- Chord fret-number collision offsets (bigger font) ----

    [Fact]
    public void TwoNoteChord_PutsSmallerFretOnLeft()
    {
        // Strings 1 (fret 3) and 2 (fret 2), adjacent → the smaller fret (2) shifts left.
        var off = LilySharp.Core.Rendering.SharedRenderer.AssignTabChordOffsets(
            new[] { (str: 1, fret: 3), (str: 2, fret: 2) });
        Assert.True(off[1] < 0, "smaller fret should be left of centre");
        Assert.True(off[0] > 0, "larger fret should be right of centre");
    }

    [Fact]
    public void ThreeNoteChord_Zigzags()
    {
        // Three adjacent strings → left, right, left (zigzag, not a slant).
        var off = LilySharp.Core.Rendering.SharedRenderer.AssignTabChordOffsets(
            new[] { (str: 1, fret: 0), (str: 2, fret: 0), (str: 3, fret: 0) });
        Assert.True(off[0] < 0 && off[1] > 0 && off[2] < 0);
    }

    [Fact]
    public void NonAdjacentChord_NotShifted()
    {
        // Strings 1 and 3 don't overlap vertically, so neither digit moves.
        var off = LilySharp.Core.Rendering.SharedRenderer.AssignTabChordOffsets(
            new[] { (str: 1, fret: 5), (str: 3, fret: 7) });
        Assert.Equal(new[] { 0.0, 0.0 }, off);
    }

    [Fact]
    public void ChordWithOutOfRangeNotes_AssignsDistinctStrings()
    {
        // Several very low notes fret below 0 on every string (out of range). The
        // fallback used to pick a shared best-effort string (CalculateFret ignores
        // occupancy) and not mark it used, so two could collide on one line. Every
        // chord member must still get its own string.
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n <c,,,, e,,,, g,,,,>4 r r r |\n  }\n}\n" +
                  "form main { Main }\nscore \"x\" { tab bass bl }\n";
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec);
        var tab = multi.EnumerateStaves().First(s => s.Staff.IsTab).Staff;
        var chord = tab.PrimaryVoice.Measures.SelectMany(m => m.Items).OfType<ChordItem>().First();

        var strings = chord.Notes.Select(n => n.StringNumber).ToList();
        Assert.Equal(3, strings.Count);
        Assert.Equal(strings.Count, strings.Distinct().Count()); // no two members share a string
    }

    // ---- Part-defined tuning + braceless render grammar ----

    private static Staff RenderStaff(string body, bool wantTab)
    {
        var src = body;
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec);
        return multi.EnumerateStaves().First(s => s.Staff.IsTab == wantTab).Staff;
    }

    [Fact]
    public void TabUsesPartTuning_WhenRenderGivesNone()
    {
        // `tab bl` with no tuning takes it from the part's `tuning bass5`.
        var tab = RenderStaff(
            "part bl { clef bass tuning bass5 }\nsection Main { bl { a4 b c d | } }\n" +
            "form main { Main }\nscore \"x\" { tab bl }\n", wantTab: true);
        Assert.Equal(TuningType.Bass5, tab.Tuning);
    }

    [Fact]
    public void TabRenderTuning_OverridesPartTuning()
    {
        var tab = RenderStaff(
            "part bl { clef bass tuning bass }\nsection Main { bl { a4 b c d | } }\n" +
            "form main { Main }\nscore \"x\" { tab bass6 bl }\n", wantTab: true);
        Assert.Equal(TuningType.Bass6, tab.Tuning);
    }

    [Fact]
    public void BracelessStaff_TakesClefFromPart()
    {
        var notation = RenderStaff(
            "part bl { clef bass tuning bass }\nsection Main { bl { a4 b c d | } }\n" +
            "form main { Main }\nscore \"x\" { staff bl  tab bl }\n", wantTab: false);
        Assert.Equal(ClefType.Bass, notation.Clef);
    }

    [Fact]
    public void TabOnlyScore_RoutesThroughTabPipeline()
    {
        // A lone `tab` (no paired `staff`) must still render as a tab staff, not
        // fall back to a plain notation staff.
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n a4\\4 b4 c4 d4 |\n  }\n}\n" +
                  "form main { Main }\nscore \"x\" { tab bass bl }\n";
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        Assert.True(spec.IsMultiStaff);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec);
        Assert.Contains(multi.EnumerateStaves(), s => s.Staff.IsTab);
    }

    [Fact]
    public void BarePitch_AutoPicksStringNearestPreviousFret()
    {
        // a\4 = fret 5 on the E string; the following b auto-picks the E string
        // (fret 7, distance 2) over the A string (fret 2, distance 3).
        var notes = TabNotes("a4\\4 b4 |");
        Assert.Equal(4, notes[1].StringNumber);
    }

    [Fact]
    public void StringNumberToken_KeepsFollowingSourcePositionsAligned()
    {
        // Regression: the \N token text must span BOTH chars ("\4", not "4"); a
        // token's width comes from its text, so a 1-char text under a 2-char span
        // drifts every following note's SourcePosition by 1 per \N, which silently
        // broke the editor<->preview note mapping on tab scores.
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n r4 a4\\4 b4\\3 c4 |\n  }\n}\n" +
                  "form main { Main }\nscore \"x\" { staff bl }\n";
        var notes = Notes(new MeasureCollector().Collect(SyntaxTree.Parse(src)));
        Assert.Equal(src.IndexOf("b4") - src.IndexOf("a4"),
            notes[1].SourcePosition - notes[0].SourcePosition);
        Assert.Equal(src.IndexOf("c4") - src.IndexOf("b4"),
            notes[2].SourcePosition - notes[1].SourcePosition);
    }

    [Fact]
    public void TabTieStringValidator_SurfacesConflict()
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n a4\\4~ a4\\3 b4 |\n  }\n}\n" +
                  "form main { Main }\nscore \"x\" { staff bl }\n";
        var validator = new TabTieStringValidator();
        validator.Validate(SyntaxTree.Parse(src));
        var d = Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.TabTieStringConflict, d.Code);
    }
}
