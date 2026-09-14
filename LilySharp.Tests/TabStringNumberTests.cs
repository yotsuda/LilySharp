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
    /// A candidate costs how far it would widen the position past <c>HandSpan</c> (3) frets —
    /// 0 inside, 1 for a stretch, more for a move — the cheapest wins, and a tie goes to the
    /// lower fret. Height alone never moves the hand.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (see <see cref="Tunings.CalculateFret"/>): not LilyPond's rule, which
    /// never looks at the hand and would answer fret 2 every time. E2 (40) on a 4-string bass
    /// is fret 12 on string 4 (E1=28), 7 on string 3 (A1=33) and 2 on string 2 (D2=38); no
    /// string plays it open, which is what makes it the case that separates the two rules.
    /// </remarks>
    [Theory]
    [InlineData(null, null, 2, 2)] // no position yet: the lowest fret on the instrument
    [InlineData(1, 3, 2, 2)]       // 2 is inside 1..3 already
    [InlineData(7, 7, 3, 7)]       // 7 costs 0; 2 would move the hand
    [InlineData(9, 9, 3, 7)]       // 7..9 costs 0; 12 would be a stretch
    [InlineData(10, 10, 4, 12)]    // 10..12 costs 0; 7 is a stretch — the hand stays up
    [InlineData(12, 12, 4, 12)]    // and from 12 nothing is cheaper than staying
    public void CalculateFret_StaysInPositionUnlessAClearlyLowerFretIsOffered(
        int? low, int? high, int expectedString, int expectedFret)
    {
        (int, int)? position = low is { } l && high is { } h ? (l, h) : null;
        var (str, fret) = Tunings.CalculateFret(40, Tunings.Bass, 0, position);
        Assert.Equal(expectedString, str);
        Assert.Equal(expectedFret, fret);
    }

    /// <summary>
    /// The user's report (2026-09-14, <c>scratch\ベースタブLy\tab-fret.lys</c>, bar 1): after
    /// <c>aes g g f f</c>, the <c>ees</c> is the second string's FIRST fret — index finger on
    /// 1, little finger on the f's 3 — and not the third string's sixth.
    /// </summary>
    /// <remarks>
    /// Two rules had to go for this: an open string (the g's) forgot the position, and a shift
    /// put the index finger on the note it moved to, so the f fixed the hand at 3..6. As a
    /// RANGE the position after the f is just 3..3, and the ees widens it to 1..3.
    /// </remarks>
    [Fact]
    public void AFlatThenOpenGThenFThenEFlat_KeepsTheEFlatInTheFirstPosition()
    {
        var bars = BassTabFrets("aes'4 g8 g f f ees4 | bes c8 c d f ees4 |");

        // aes g g f f ees
        Assert.Equal(new (int?, int)[] { (1, 1), (1, 0), (1, 0), (2, 3), (2, 3), (2, 1) }, bars[0]);
        // bes c c d f ees — the same first position, the d open
        Assert.Equal(new (int?, int)[] { (3, 1), (3, 3), (3, 3), (2, 0), (2, 3), (2, 1) }, bars[1]);
    }

    /// <summary>
    /// The user's fingering (2026-09-14, <c>tab-fret.lys</c> section B, bars 2–3): bes on the
    /// fourth string's 6th fret with the little finger, g on its 3rd with the index finger,
    /// bes again at 6, and the c on the THIRD string's 3rd fret with the index finger — not
    /// the fourth string's 8th.
    /// </summary>
    /// <remarks>
    /// The g is a shift, and the hand slides only as far as it must: 4..6 becomes 3..5, not
    /// 3..3. Restarting the position from the g alone let the repeated bes restart it again
    /// at 6..6, and then the fourth string's 8th fret "fitted" and tied with the c's 3rd.
    /// </remarks>
    [Fact]
    public void AShiftSlidesTheHandOnlyAsFarAsItMust()
    {
        var bars = BassTabFrets(
            "aes4. aes8~ aes aes r4 | bes4. bes8~ bes4 g8 bes | c4. c8~ c c r4 | ees4. ees8~ ees ees r4 |");

        Assert.Equal(new (int?, int)[] { (4, 4), (4, 4), (4, 4), (4, 4) }, bars[0]);
        Assert.Equal(new (int?, int)[] { (4, 6), (4, 6), (4, 6), (4, 3), (4, 6) }, bars[1]);
        Assert.Equal(new (int?, int)[] { (3, 3), (3, 3), (3, 3), (3, 3) }, bars[2]);
        // ees: the third string's 6th — a stretch from the c's 3rd — not the second string's
        // 1st, which is lower but moves the hand two frets (user, 2026-09-14)
        Assert.Equal(new (int?, int)[] { (3, 6), (3, 6), (3, 6), (3, 6) }, bars[3]);
    }

    /// <summary>
    /// A stretch across a skipped string is as hard as a move (USER SPECIFIED, 2026-09-14,
    /// <c>Arthur's Theme</c> section A bar 5): with the hand at 3..5 — c on the third string's
    /// 3rd, g on the second string's 5th — and f on the second string's 3rd to follow, bes is
    /// the third string's 1st, not the fourth string's 6th.
    /// </summary>
    /// <remarks>
    /// The fourth string's 6th is a one-fret stretch from the c (neighbouring strings), but the
    /// f after it on the second string's 3rd skips the third string (2): 3 in all. The third
    /// string's 1st moves the hand two frets and leaves the f inside 1..3 on the next string: 2.
    /// </remarks>
    [Fact]
    public void AStretchAcrossASkippedStringCostsAsMuchAsAMove()
    {
        // B♭1 (34) is the fourth string's 6th or the third string's 1st; F2 (41) follows.
        Assert.Equal((3, 1), Tunings.CalculateFret(34, Tunings.Bass, 0, (3, 5), next: (41, 0),
            previousString: 3, previousFret: 3));
        Assert.Equal(Tunings.StringSkipCost, Tunings.SkipCost(4, 6, 2, 3)); // a skip, however far
        Assert.Equal(0, Tunings.SkipCost(3, 6, 2, 3)); // neighbouring strings: no skip at all
    }

    /// <summary>
    /// A stretch is judged between two notes in a row, not against the far end of the hand's
    /// range, which may be a note it stopped holding (USER SPECIFIED, 2026-09-14,
    /// さよならエレジー section C bar 3: <c>bes,,4 r8 f,, f,, f,, aes,, a,,</c> — the third f,,
    /// is the fourth string's 1st, not the fifth string's 6th).
    /// </summary>
    [Fact]
    public void AStretchIsJudgedBetweenNotesInARowNotAgainstTheRange()
    {
        // F1 (29) on a 5-string bass after an F1 on the fourth string's 1st, hand at 1..4 (its 4th
        // left over from a second-string ges, a bar earlier), Ab1 (32) next
        Assert.Equal((4, 1), Tunings.CalculateFret(29, Tunings.GetTuning(TuningType.Bass5), 0,
            (1, 4), next: (32, 0), previousString: 4, previousFret: 1));
    }

    /// <summary>
    /// A leap of an octave or more forgets the position (USER APPROVED, 2026-09-14): the note
    /// after it starts as low as it can, as a first note does.
    /// </summary>
    /// <remarks>
    /// A2 pinned to the fourth string is its 17th fret. A1 an octave below is then the open
    /// third string — without the reset the hand at 17 would refuse an open string and take
    /// the fourth string's 5th — and B1 after it the third string's 2nd.
    /// </remarks>
    [Fact]
    public void ALeapOfAnOctaveForgetsThePosition()
    {
        var bars = BassTabFrets("a'4\\4 a, b r |");
        Assert.Equal(new (int?, int)[] { (4, 17), (3, 0), (3, 2) }, bars[0]);
    }

    /// <summary>
    /// A whole bar with nothing stopped frees the hand (USER SPECIFIED, 2026-09-14, Real Gone
    /// section F bar 1).
    /// </summary>
    /// <remarks>
    /// A2 pinned to the fourth string is its 17th fret. E2 in the very next bar stays up the
    /// neck at the fourth string's 12th; after a bar of rest it is placed as a first note — the
    /// second string's 2nd, the lowest of three tied fingerings.
    /// </remarks>
    [Fact]
    public void AWholeBarWithNothingStoppedFreesTheHand()
    {
        Assert.Equal(new (int?, int)[] { (4, 12) }, BassTabFrets("a'4\\4 r r r | e r r r |")[1]);
        Assert.Equal(new (int?, int)[] { (2, 2) }, BassTabFrets("a'4\\4 r r r | r1 | e r r r |")[2]);
    }

    /// <summary>
    /// The note a slur ends on stays on the slur's string — a slide or legato (USER SPECIFIED,
    /// 2026-09-14, Real Gone Intro bars 12–13: <c>e,4\3( | b,,8)</c>).
    /// </summary>
    /// <remarks>
    /// A2 pinned to the third string is its 12th fret. C3 after it is the second string's 10th
    /// inside the hand (0 + 2 above low position) rather than the third string's 15th (a stretch,
    /// 1 + 2) — unless a slur joins them: then leaving the third string costs 2 more (4 against
    /// 3), and the c slides up the third string to its 15th.
    /// </remarks>
    [Fact]
    public void TheNoteASlurEndsOnStaysOnTheSlursString()
    {
        Assert.Equal(new (int?, int)[] { (3, 12), (2, 10) }, BassTabFrets("a'4\\3 c r r |")[0]);
        Assert.Equal(new (int?, int)[] { (3, 12), (3, 15) }, BassTabFrets("a'4\\3( c) r r |")[0]);
    }

    /// <summary>
    /// A fall slides the finger off the string, so the hand is free after it (USER SPECIFIED,
    /// 2026-09-14, Real Gone Intro bar 13).
    /// </summary>
    /// <remarks>
    /// A2 pinned to the third string is its 12th fret. Without the fall the E2 after it stays
    /// up at the fourth string's 12th (inside the hand); after the fall it is placed as a first
    /// note next to the third string — the second string's 2nd.
    /// </remarks>
    [Fact]
    public void AFallFreesTheHand()
    {
        Assert.Equal(new (int?, int)[] { (3, 12), (4, 12) }, BassTabFrets("a'4\\3 e r r |")[0]);
        Assert.Equal(new (int?, int)[] { (3, 12), (2, 2) }, BassTabFrets("a'4\\3@fall e r r |")[0]);
    }

    /// <summary>
    /// The position is forgotten on an octave, the previous string is not: Amanda section A3
    /// bar 1, <c>g,, g,</c> — g on the fourth string's 3rd, then g' on the second string's 5th
    /// in the octave shape rather than the open first string, which skips two strings.
    /// </summary>
    [Fact]
    public void AnOctaveAfterALeapIsStillPlayedInTheOctaveShape()
    {
        // G1 (31) then G2 (43), bar reuse aside: the open first string would skip from string 4
        Assert.Equal((2, 5), Tunings.CalculateFret(43, Tunings.Bass, 0, null,
            next: null, previousString: 4, previousFret: 3));
    }

    /// <summary>
    /// A fingering that leaves the hand above low position costs a point, so the same shape
    /// one string lower and five frets higher no longer ties with it (USER SPECIFIED,
    /// 2026-09-14, <c>Arthur's Theme</c> Outro bar 2).
    /// </summary>
    /// <remarks>
    /// With the hand at 5..8, F#2 (42) is the second string's 4th (4..8, one fret wider than a
    /// stretch: 1) or the third string's 9th (6..9, a stretch: 1) — a tie that the higher A2
    /// after it used to break upward. The third string's 9th leaves the hand at 6, above low
    /// position, and now costs 2.
    /// </remarks>
    [Fact]
    public void AFingeringAboveLowPositionCostsAPoint()
    {
        var position = new HandPosition(5, 8);
        Assert.Equal((2, 4), Tunings.CalculateFret(42, Tunings.Bass, 0, position,
            next: (45, 0), previousString: 2));
    }

    /// <summary>
    /// Two notes in a row that skip a string cost a point, open strings included (USER
    /// SPECIFIED, 2026-09-14, <c>Arthur's Theme</c> section E bar 2): after g on the second
    /// string's 5th, with g,, on the fourth string's 3rd to follow, the d is the third string's
    /// 5th and not the open second string.
    /// </summary>
    /// <remarks>
    /// Both hand costs are 0 — the open string because the hand is in low position, the 5th
    /// because it is inside 2..5 — so the skip from the open second string to the fourth
    /// decides. Without a skip on either side the melody still decides a tie: with a d, to
    /// follow instead, nothing is skipped and the open string (the lower fret) stays.
    /// </remarks>
    [Fact]
    public void TwoNotesInARowThatSkipAStringCostAPoint()
    {
        var position = new HandPosition(2, 5);
        // D2 (38) between g on string 2 and G1 (31), which only the fourth string plays at 3
        Assert.Equal((3, 5), Tunings.CalculateFret(38, Tunings.Bass, 0, position,
            next: (31, 0), previousString: 2));
        // D2 before B1 (35, the third string's 2nd): no skip either way, the open string stays
        Assert.Equal((2, 0), Tunings.CalculateFret(38, Tunings.Bass, 0, position,
            next: (35, 0), previousString: 2, previousFret: 5));
        Assert.Equal(Tunings.StringSkipCost, Tunings.SkipCost(2, 5, 4, 5)); // second string to fourth at the same fret
        Assert.Equal(0, Tunings.SkipCost(2, 5, 3, 5));
        Assert.Equal(0, Tunings.SkipCost(0, -1, 4, 3));
    }

    /// <summary>
    /// The octave shape — index finger on the root, little finger on the octave two strings up
    /// and two frets along — is not a skip (USER SPECIFIED, 2026-09-14, Amanda section A3).
    /// </summary>
    [Fact]
    public void TheOctaveShapeIsNotASkip()
    {
        Assert.Equal(0, Tunings.SkipCost(4, 3, 2, 5)); // g on string 4 fret 3 → g' on string 2 fret 5
        Assert.Equal(0, Tunings.SkipCost(2, 5, 4, 3)); // and back down
        Assert.Equal(Tunings.StringSkipCost, Tunings.SkipCost(4, 3, 2, 3)); // two strings apart at the same fret: a skip
        Assert.Equal(Tunings.StringSkipCost, Tunings.SkipCost(4, 3, 1, 0)); // g' as the open first string: a skip
    }

    /// <summary>
    /// A guitar is fingered one finger per fret: with the index finger at the 5th the little
    /// finger plays the 8th with no stretch and no penalty; a bass (1-2-4) pays a stretch for
    /// the same reach (USER SPECIFIED, 2026-09-14).
    /// </summary>
    [Fact]
    public void AGuitarHandCoversFourFretsAndABassHandThree()
    {
        Assert.Equal(4, Tunings.HandSpanFor(TuningType.Guitar));
        Assert.Equal(3, Tunings.HandSpanFor(TuningType.Bass));
        Assert.Equal(0, Tunings.MoveCost((5, 5), 8, Tunings.HandSpanFor(TuningType.Guitar)));
        Assert.Equal(1, Tunings.MoveCost((5, 5), 8, Tunings.HandSpanFor(TuningType.Bass)));
        // and a note inside a hand that is already stretched costs nothing more
        Assert.Equal(0, Tunings.MoveCost((3, 6), 5, Tunings.HandSpanFor(TuningType.Bass)));
    }

    /// <summary>
    /// One open note in the middle of a passage played above low position is unnatural (USER
    /// SPECIFIED, 2026-09-14): with the hand at the 7th fret the G is stopped, not open.
    /// </summary>
    /// <remarks>
    /// A2 pinned to the D string is its 7th fret, position 7..7 — above
    /// <c>Tunings.LowPositionTop</c>. The G is the D string's 5th (7..5 is inside the hand),
    /// and the E2 after it the A string's 7th. Written in <c>tab-fret.lys</c>'s octave, where
    /// <c>aes'</c> sounds A♭2.
    /// </remarks>
    [Fact]
    public void AnOpenString_IsNotUsedAboveLowPosition()
    {
        var bars = BassTabFrets("a'4\\2 g e r |");
        Assert.Equal(new (int?, int)[] { (2, 7), (2, 5), (3, 7) }, bars[0]);
    }

    /// <summary>Each bar's tab notes as (string, fret) on a bass <c>tab</c> staff, from a
    /// body written the way <c>scratch\ベースタブLy\tab-fret.lys</c> writes it.</summary>
    private static (int? String, int Fret)[][] BassTabFrets(string body)
    {
        var src = "part melody {\n  instrument bass\n  section A {\n    " + body + "\n  }\n}\n" +
                  "form main { A }\nscore main {\n  tab melody\n}\n";
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        var tab = new MeasureCollector().CollectMultiStaff(tree, spec)
            .EnumerateStaves().First(s => s.Staff.IsTab).Staff;
        int shift = Tunings.SoundingShift(tab.TabSourceClef, tab.Transposition);
        return tab.PrimaryVoice.Measures
            .Select(m => m.Items.OfType<NoteItem>()
                .Select(n => (n.StringNumber,
                    Tunings.CalculateFret(n.Midi + shift, Tunings.Bass, n.StringNumber ?? 0).fret))
                .ToArray())
            .ToArray();
    }

    /// <summary>
    /// An OPEN string is used while the hand is in low position (or not placed yet), and
    /// avoided above it (USER SPECIFIED, 2026-09-14).
    /// </summary>
    /// <remarks>
    /// G2 (43) is string 1's open pitch, and 15 / 10 / 5 on the strings below it.
    /// </remarks>
    [Theory]
    [InlineData(null, null, 1, 0)] // nowhere yet: open
    [InlineData(1, 3, 1, 0)]       // low position: open
    [InlineData(10, 12, 3, 10)]    // up at 10..12: stopped at 10, not one open note
    public void CalculateFret_TakesAnOpenStringOnlyInLowPosition(
        int? low, int? high, int expectedString, int expectedFret)
    {
        (int, int)? position = low is { } l && high is { } h ? (l, h) : null;
        Assert.Equal((expectedString, expectedFret), Tunings.CalculateFret(43, Tunings.Bass, 0, position));
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
        var off = LilySharp.Core.Svg.Layout.TabChordColumns.Offsets(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
            new[] { (str: 1, fret: 3), (str: 2, fret: 2) });
        Assert.True(off[1] < 0, "smaller fret should be left of centre");
        Assert.True(off[0] > 0, "larger fret should be right of centre");
    }

    [Fact]
    public void ThreeNoteChord_Zigzags()
    {
        // Three adjacent strings → left, right, left (zigzag, not a slant).
        var off = LilySharp.Core.Svg.Layout.TabChordColumns.Offsets(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
            new[] { (str: 1, fret: 0), (str: 2, fret: 0), (str: 3, fret: 0) });
        Assert.True(off[0] < 0 && off[1] > 0 && off[2] < 0);
    }

    [Fact]
    public void ThreeNoteChord_LargerColumnGoesRight_MiddleAlone()
    {
        // 0/4/5 top-down: the {0,5} column carries the larger fret, so it goes RIGHT
        // and 4 sits alone on the left (user-specified example, 2026-08-06).
        var off = LilySharp.Core.Svg.Layout.TabChordColumns.Offsets(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
            new[] { (str: 1, fret: 0), (str: 2, fret: 4), (str: 3, fret: 5) });
        Assert.True(off[0] > 0 && off[1] < 0 && off[2] > 0);
    }

    [Fact]
    public void ThreeNoteChord_LargerColumnGoesRight_MiddleWins()
    {
        // 0/5/4 top-down: the lone {5} column outranks {0,4}, so 5 goes RIGHT and
        // 0 and 4 sit left (user-specified example, 2026-08-06 — the open string's
        // 0 has no rule of its own; it just rides the smaller column).
        var off = LilySharp.Core.Svg.Layout.TabChordColumns.Offsets(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
            new[] { (str: 1, fret: 0), (str: 2, fret: 5), (str: 3, fret: 4) });
        Assert.True(off[0] < 0 && off[1] > 0 && off[2] < 0);
    }

    [Fact]
    public void NonAdjacentChord_NotShifted()
    {
        // Strings 1 and 3 don't overlap vertically, so neither digit moves.
        var off = LilySharp.Core.Svg.Layout.TabChordColumns.Offsets(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
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
