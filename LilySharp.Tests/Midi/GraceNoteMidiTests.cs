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

using System;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// MIDI grace-note export (regression guard for Semantics M5). Grace export is
/// not covered by the SVG snapshot suite, so these pin: grace CHORD members are
/// emitted (were silently dropped by OfType&lt;NoteSyntax&gt;), the grace sounding
/// duration is 9/40 of the WRITTEN duration (was hard-coded 1/32 — LILYPOND-REF:
/// ly/articulate.ly ac:defaultGraceFactor = 9/40), and grace time is still stolen
/// from the following note so the downbeat stays on the metric grid.
/// </summary>
public class GraceNoteMidiTests
{
    private static List<MidiNote> ExportNotes(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var file = new MidiExporter().Export(tree);
        return file.Tracks.SelectMany(t => t.Notes).ToList();
    }

    [Fact]
    public void GraceChord_EmitsAllMembers()
    {
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { grace { <c' e'>16 } d'4 | } }
            form main { A }
            score main { staff m }
            """);
        // Before the fix the grace chord was dropped whole (OfType<NoteSyntax>),
        // leaving only the main d'. Now both chord members sound as grace notes.
        Assert.Equal(3, notes.Count);
        var grace = notes.Where(n => n.StartTick == 0).ToList();
        Assert.Equal(2, grace.Count);                                  // both members
        Assert.Equal(2, grace.Select(n => n.Pitch).Distinct().Count()); // distinct pitches
        Assert.All(grace, g => Assert.Equal(grace[0].DurationTicks, g.DurationTicks));
        var main = Assert.Single(notes.Where(n => n.StartTick > 0));
        Assert.Equal(grace[0].DurationTicks, main.StartTick);          // main starts after the grace
    }

    [Fact]
    public void GraceNote_SoundingDuration_Is9_40thOfWritten()
    {
        // LILYPOND-REF: ly/articulate.ly ac:defaultGraceFactor = 9/40 — a grace
        // note sounds for 9/40 of its NOTATED duration in LP's built-in MIDI.
        int GraceDur(string dur) => ExportNotes($$"""
            octave absolute
            part m { clef treble }
            section A { m { grace { c'{{dur}} } d'4 | } }
            form main { A }
            score main { staff m }
            """).OrderBy(n => n.StartTick).First().DurationTicks;
        int NoteDur(string dur) => ExportNotes($$"""
            octave absolute
            part m { clef treble }
            section A { m { c'{{dur}} } }
            form main { A }
            score main { staff m }
            """).First().DurationTicks;

        // Each grace item's sounding time is 9/40 of its written value (checked
        // independently for a 16th and a 32nd; the exact 2x ratio between them is
        // not asserted because integer tick rounding of 9/40 breaks it — 480 PPQ:
        // 16th -> round(9/40*120)=27, 32nd -> round(9/40*60)=14, and 2*14 != 27).
        int g16 = GraceDur("16"), g32 = GraceDur("32");
        Assert.Equal(9, (int)Math.Round(g16 * 40.0 / NoteDur("16")));      // factor == 9/40
        Assert.Equal(9, (int)Math.Round(g32 * 40.0 / NoteDur("32")));

        // An unwritten grace duration is an EIGHTH — the LAYOUT's rule, read from
        // MeasureCollector.CollectGraceNotes (graceDefaultDuration = Fraction.Eighth).
        // ⚠️ It used to be a 1/32 here and a 1/8 on the page: one spelling with two
        // answers, which is what this line now guards against coming back.
        int gDefault = GraceDur(""), g8 = GraceDur("8");
        Assert.Equal(g8, gDefault);
    }

    [Fact]
    public void GraceNote_ThreadsWrittenDurationToLaterGraceItems()
    {
        // grace { d16 e }: e inherits the 16th (duration threads within the group),
        // it is NOT reset to the 1/32 default — so both sound the same length.
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { grace { d'16 e' } f'4 | } }
            form main { A }
            score main { staff m }
            """).OrderBy(n => n.StartTick).ToList();
        Assert.Equal(3, notes.Count);
        Assert.Equal(notes[0].DurationTicks, notes[1].DurationTicks); // e' == d' (both 16th → same 9/40 length)
    }

    [Fact]
    public void Grace_StealsTimeFromFollowingNote_KeepingGrid()
    {
        var notes = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { grace { c'16 } d'4 | } }
            form main { A }
            score main { staff m }
            """).OrderBy(n => n.StartTick).ToList();
        int quarter = ExportNotes("""
            octave absolute
            part m { clef treble }
            section A { m { d'4 } }
            form main { A }
            score main { staff m }
            """).First().DurationTicks;

        Assert.Equal(2, notes.Count);
        var grace = notes[0];
        var main = notes[1];
        Assert.Equal(0, grace.StartTick);
        Assert.Equal(grace.DurationTicks, main.StartTick);          // main begins where the grace ends
        Assert.Equal(quarter, main.StartTick + main.DurationTicks); // pair fills d's original quarter slot
    }

    [Fact]
    public void GraceNote_AdvancesRelativeOctaveForFollowingNote()
    {
        // Default (relative) mode: the note AFTER a grace group resolves its octave
        // relative to the grace's LAST pitch — the same result as if the grace pitch
        // were a plain note. Pins the collector/exporter octave-threading seam through
        // grace, which was previously untested. LILYPOND-REF: grace threads relative octave.
        int DAfter(string body) => ExportNotes($$"""
            part m { clef treble }
            section A { m { {{body}} } }
            form main { A }
            score main { staff m }
            """).OrderByDescending(n => n.StartTick).First().Pitch; // the trailing d

        // `grace { g'16 }` and a plain `g'16` before the d reference the same pitch,
        // so d must land on the same octave in both.
        Assert.Equal(DAfter("c'4 g'16 d16"), DAfter("c'4 grace { g'16 } d16"));
    }

    private static string PhraseBook(string phrases, string music)
        => "octave absolute\npart m { clef treble }\n" + phrases
           + "\nsection A { m {\n" + music + "\n} }\n"
           + "form main { A }\nscore main { staff m }\n";

    /// <summary>Every sounded event as (start, length, pitch), in time order — the whole
    /// performance, so a phrase that sounds at the wrong moment or the wrong length fails
    /// as loudly as one that sounds the wrong note.</summary>
    private static (int Start, int Dur, int Pitch)[] Performance(string phrases, string music)
        => ExportNotes(PhraseBook(phrases, music))
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => (n.StartTick, n.DurationTicks, n.Pitch)).ToArray();

    /// <summary>
    /// <c>grace { G }</c> SOUNDS what <c>G</c> holds — the same performance, event for
    /// event, as writing those notes in the body.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS READER WAS THE ONE LEFT BEHIND. Session 300 taught the page and LYS4020 that
    /// a phrase reference is a container and expanded it in
    /// <c>Semantics.GraceBodySupport.BodyElements</c>, whose remarks say "written once and
    /// read twice" — but <see cref="MidiExporter"/> was walking <c>grace.Body.Items</c>
    /// itself and took bare notes, chords and rests only. MEASURED 2026-08-30 (session 301,
    /// scratch/p301/ab): the SVG of <c>grace { G } c'4 c'2.</c> was byte-identical to the
    /// inline spelling while its MIDI was byte-identical to the book WITH NO GRACE IN IT
    /// (91 bytes against 107) — the page drew two grace notes nobody could hear.
    /// <para>
    /// ⚠️ THE THIRD ASSERT IS THE ONE THAT KEEPS THE FIRST TWO HONEST, the same way it is on
    /// <c>GraceBodyValidatorTests.APhraseReferenceInAGraceBody_EngravesWhatThePhraseHolds</c>,
    /// whose rows these are: "equal to the inline control" is also what TWO silences satisfy,
    /// and two silences is exactly what the defect sounded like.
    /// </para>
    /// </remarks>
    [Theory]
    // The plain case, and the nested one: a phrase body may reference another phrase.
    [InlineData("phrase G { d'16 e' }", "grace { G } c'1 | e'1 |", "grace { d'16 e' } c'1 | e'1 |")]
    [InlineData("phrase I { d'16 e' }\nphrase O { I f'16 }",
                "grace { O } c'1 | e'1 |", "grace { d'16 e' f'16 } c'1 | e'1 |")]
    // Mixed with bare notes on both sides of the reference.
    [InlineData("phrase G { d'16 e' }",
                "grace { c'16 G a'16 } c'1 | e'1 |", "grace { c'16 d'16 e' a'16 } c'1 | e'1 |")]
    public void APhraseReferenceInAGraceBody_SoundsWhatThePhraseHolds(
        string phrases, string written, string control)
    {
        Assert.Equal(Performance("", control), Performance(phrases, written));
        Assert.NotEqual(Performance("", "c'1 | e'1 |"), Performance(phrases, written));
    }

    /// <summary>
    /// The page and the sound agree about a grace body: the grace notes engraved for
    /// <c>grace { G }</c> are the grace notes played for it, pitch for pitch.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE NET UNDER A STATEMENT WITH FOUR READERS. What a grace body carries is
    /// stated once (<c>Semantics.GraceBodySupport</c>) and read by the page
    /// (<c>MeasureCollector.CollectGraceNotes</c>), the report
    /// (<c>Semantics.GraceBodyValidator</c>), this exporter and
    /// <c>MusicXmlExporter.ProcessGraceNotes</c> — four walks that cannot be folded into one
    /// because each carries its OWN frame out of a phrase (this one a sounding transpose and
    /// a tick, the page an octave and a column). Checklist 7.7's answer for a pair that
    /// cannot be folded is a DIFFERENTIAL net, and this is it: it names no expected pitches,
    /// so it survives every change to what they are and fails the moment two readers part
    /// company again.
    /// ⚠️ It compares the GRACE notes only. The narrowings still differ below that line — a
    /// chord and a rest in a grace body sound here and are dropped by the page (docs/HANDOFF.md
    /// §2 U8) — and this row is about the container, not about that.
    /// </remarks>
    [Theory]
    [InlineData("phrase G { d'16 e' }", "grace { G } c'1 |")]
    [InlineData("phrase I { d'16 e' }\nphrase O { I f'16 }", "grace { O } c'1 |")]
    [InlineData("phrase G { d'16 e' }", "grace { c'16 G a'16 } c'1 |")]
    public void AGraceBody_SoundsThePitchesItDraws(string phrases, string music)
    {
        string book = PhraseBook(phrases, music);

        int[] drawn = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(book))
            .GraceNotes.Single().Notes.Select(n => n.Midi).ToArray();

        // The grace events are the ones before the main note's downbeat: grace time is
        // STOLEN from it (Grace_StealsTimeFromFollowingNote_KeepingGrid), so the main note
        // is the last thing to start.
        var sounded = ExportNotes(book).OrderBy(n => n.StartTick).ToList();
        int[] played = sounded.Take(sounded.Count - 1).Select(n => n.Pitch).ToArray();

        Assert.NotEmpty(drawn);
        Assert.Equal(drawn, played);
    }

    /// <summary>
    /// A phrase body is played in a FRESH relative frame inside a grace, exactly as it is
    /// drawn in one: the same reference sounds the same pitches whatever the grace played
    /// before it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SECOND HALF IS NOT DECORATION — it is the same pairing
    /// <c>GraceBodyValidatorTests.APhraseInAGraceBody_ReadsAFreshFrame</c> makes for the
    /// page. "Both books agree" is also what an exporter that had stopped resolving relative
    /// octaves would say, so the INLINE spelling of the same two notes is asserted to
    /// DISAGREE across the same pair: the running frame is live, and the reference is what
    /// stands outside it.
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_IsPlayedInAFreshFrame()
    {
        // The two grace pitches only: the note IN FRONT of the grace is the thing being
        // varied, so comparing the whole performance would compare the variable itself.
        int[] GracePitches(string phrases, string music)
            => ExportNotes(
                "part m { clef treble }\n" + phrases
                + "\nsection A { m {\n" + music + "\n} }\n"
                + "form main { A }\nscore main { staff m }\n")
                .OrderBy(n => n.StartTick).Skip(1).Take(2).Select(n => n.Pitch).ToArray();

        const string G = "phrase G { d16 e }";
        Assert.Equal(
            GracePitches(G, "c'2 grace { G } c'2 | e'1 |"),
            GracePitches(G, "c,,2 grace { G } c'2 | e'1 |"));

        Assert.NotEqual(
            GracePitches("", "c'2 grace { d16 e } c'2 | e'1 |"),
            GracePitches("", "c,,2 grace { d16 e } c'2 | e'1 |"));
    }

    /// <summary>
    /// A reference inside a grace hands the relative chain back at the phrase's ANCHOR when
    /// it is PLAYED too — the chord rule, so a phrase's interior never leaks into the note
    /// sounded after the grace.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE PAIR IS THE POINT (the sounding twin of
    /// <c>GraceBodyValidatorTests.APhraseInAGraceBody_HandsTheChainBackAtItsAnchor</c>):
    /// equality with the main-stream reference alone would also hold for an exporter that had
    /// stopped moving the frame at all, so the INLINE spelling of the same two notes is
    /// asserted to leave it somewhere ELSE — <c>grace { d16 d' }</c> ends an octave up and
    /// hands THAT over, while <c>grace { G }</c> hands over the bare d its body opens with.
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_HandsThePlayedChainBackAtItsAnchor()
    {
        int NoteAfter(string phrases, string music, int index)
            => ExportNotes(
                "part m { clef treble }\n" + phrases
                + "\nsection A { m {\n" + music + "\n} }\n"
                + "form main { A }\nscore main { staff m }\n")
                .OrderBy(n => n.StartTick).ElementAt(index).Pitch;

        const string G = "phrase G { d16 d' }";
        int afterGrace = NoteAfter(G, "grace { G } c2 c2 | e'1 |", 2);
        int afterReference = NoteAfter(G, "G c2 c2 | e'1 |", 2);
        int afterInline = NoteAfter("", "grace { d16 d' } c2 c2 | e'1 |", 2);

        Assert.Equal(afterReference, afterGrace);
        Assert.NotEqual(afterInline, afterGrace);
    }

    /// <summary>
    /// A reference resets the grace group's own DURATION memory to the default eighth, the
    /// same as the page does: <c>grace { c'16 G }</c> gives G's undurated first note an
    /// eighth, not the sixteenth written in front of it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE RULE IS "the boundary restores what THAT reader reads" (docs/HANDOFF.md §1,
    /// session 300), and this reader reads a duration. It is asserted against the same
    /// phrase played after an eighth, not against a hand-written tick count, so it says
    /// "the sixteenth did not reach through the reference" rather than "the answer is 27".
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_OpensAtTheGroupsDefaultDuration()
    {
        const string G = "phrase G { d' }";
        int Nth(string music, int index) => ExportNotes(PhraseBook(G, music))
            .OrderBy(n => n.StartTick).ElementAt(index).DurationTicks;

        // The note out of G: alone in the body it is the first grace event, after a written
        // sixteenth it is the second.
        int alone = Nth("grace { G } c'1 |", 0);
        int afterASixteenth = Nth("grace { c'16 G } c'1 |", 1);
        // The same shape with the phrase written out: THAT one does inherit the sixteenth,
        // which is what makes the equality above a statement about the boundary.
        int inlineAfterASixteenth = Nth("grace { c'16 d' } c'1 |", 1);

        Assert.Equal(alone, afterASixteenth);
        Assert.NotEqual(inlineAfterASixteenth, afterASixteenth);
    }

    /// <summary>
    /// A reference inside a grace gives back everything it borrowed: the movable phrase's
    /// sounding transpose and the octave its own marks shifted. The note after the grace is
    /// the note it would have been with no phrase in front of it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS ROW EXISTS BECAUSE A POISON FOUND NOTHING. Session 301 ran a poison that drops
    /// both restores at the end marker (<c>e_midinorestore</c>, scratch/p301/poison.py) and
    /// the whole suite stayed green: the rest of this file's phrase rows write no key change
    /// and no octave mark on the reference, so neither borrowed quantity was ever non-zero.
    /// A poison that turns nothing red is the report that the net is missing (RULES §5.4),
    /// and this is the net.
    /// ⚠️ Both halves are asserted against the SAME book without the phrase, not against a
    /// hand-written pitch: what they claim is "the grace put it back", which stays true when
    /// the transpose interval or the anchor rule changes.
    /// </remarks>
    [Fact]
    public void APhraseInAGraceBody_GivesBackWhatItBorrowed()
    {
        static int Last(string source)
            => ExportNotes(source).OrderBy(n => n.StartTick).Last().Pitch;

        // ⑴ The movable phrase's transpose. Lick is written in the home key and referenced
        // under an ambient G, so it sounds shifted — and the c'1 after it must not.
        const string Modulating = """
            octave absolute
            key c major
            part m { clef treble }
            phrase Lick { c'16 d' e' }
            section A { m { key g major %BODY% c'1 | } }
            form main { A }
            score main { staff m }
            """;
        Assert.Equal(
            Last(Modulating.Replace("%BODY% ", "")),
            Last(Modulating.Replace("%BODY%", "grace { Lick }")));
        // …and the grace itself DID move, or the equality above is about a phrase that
        // borrowed nothing.
        Assert.NotEqual(
            ExportNotes(Modulating.Replace("%BODY%", "grace { Lick }"))
                .OrderBy(n => n.StartTick).First().Pitch,
            ExportNotes(Modulating.Replace("key g major ", "").Replace("%BODY%", "grace { Lick }"))
                .OrderBy(n => n.StartTick).First().Pitch);

        // ⑵ The octave mark on the reference, which in ABSOLUTE mode lands on the part's
        // absolute base rather than on a running frame. `grace { G' }` must leave the c'1
        // where `grace { G }` leaves it.
        const string Marked = """
            octave absolute
            part m { clef treble }
            phrase G { d'16 e' }
            section A { m { grace { %REF% } c'1 | } }
            form main { A }
            score main { staff m }
            """;
        Assert.Equal(Last(Marked.Replace("%REF%", "G")), Last(Marked.Replace("%REF%", "G'")));
        // …and the mark DID raise the phrase itself.
        Assert.NotEqual(
            ExportNotes(Marked.Replace("%REF%", "G")).OrderBy(n => n.StartTick).First().Pitch,
            ExportNotes(Marked.Replace("%REF%", "G'")).OrderBy(n => n.StartTick).First().Pitch);
    }

    /// <summary>
    /// A tuplet written in a grace body SOUNDS, and it sounds at its ratio: the three
    /// sixteenths of <c>tuplet 3/2</c> take two sixteenths' worth of grace time between them.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE RATIO IS LILYPOND'S ANSWER, MEASURED, not a guess about what a tuplet ought to
    /// mean. On LilyPond's own <c>\midi</c> at division 384 (2026-08-30, session 302,
    /// scratch/p302/lp). ⚠️ THIS LINE SAID "LilyPond 2.26.0" FOR ONE COMMIT AND THAT WAS
    /// WRONG: the run was the WSL binary, v2.27.3, and the canonical 2.26.0 exe stalled for
    /// 13 minutes when asked for the same books. The ticks are QUALITATIVE; the canonical
    /// half is the mechanism cited on <c>Svg.Collector.GraceTupletStartMarker</c>, which was
    /// read in the 2.26.0 source. <c>\grace { d'16 e' f' } c'4</c> sounds its grace notes at ticks
    /// 0 / 21 / 43 and hands the main note over at 64, while
    /// <c>\grace { \tuplet 3/2 { … } } c'4</c> sounds them at 0 / 14 / 29 and hands over at
    /// 43 = round(64 × 2/3).
    /// <para>
    /// ⚠️ THE FIRST ASSERT IS THE ONE THAT WOULD HAVE FAILED BEFORE THIS TRIP, and not by a
    /// tick: MEASURED 2026-08-30 (scratch/p302/ab) the MIDI for this book was BYTE-IDENTICAL
    /// to the book with no grace in it at all — the page, the sound and the MusicXML each
    /// dropped the whole body. So the row asks for the notes first and the ratio second.
    /// </para>
    /// <para>
    /// ⚠️ IT IS ASSERTED AS A RATIO AGAINST THE UNTUPLETED CONTROL rather than as a tick
    /// count, so it survives any later change to the 9/40 grace factor or the division —
    /// what it pins is that the tuplet is read, not that the answer is 18.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATupletInAGraceBody_SoundsAtItsRatio()
    {
        (int Start, int Dur, int Pitch)[] Grace(string music)
            => Performance("", music).Where(n => n.Dur < 100).ToArray();

        var plain = Grace("grace { d'16 e' f' } c'4 c'2 c'4 | e'1 |");
        var triplet = Grace("grace { tuplet 3/2 { d'16 e' f' } } c'4 c'2 c'4 | e'1 |");

        // The notes are there at all, and they are the same three notes.
        Assert.Equal(3, triplet.Length);
        Assert.Equal(plain.Select(n => n.Pitch), triplet.Select(n => n.Pitch));

        // …and each of them is two thirds as long, so the group is two thirds as long.
        Assert.All(triplet.Zip(plain),
            p => Assert.Equal(p.Second.Dur * 2 / 3, p.First.Dur));
        Assert.Equal((plain[^1].Start + plain[^1].Dur) * 2 / 3,
                     triplet[^1].Start + triplet[^1].Dur);
    }

    /// <summary>
    /// The time the following note gives up shrinks with the tuplet too — a grace body's
    /// steal is the length of what it played, so a ratio that reached the notes and not the
    /// steal would push the downbeat off the grid.
    /// </summary>
    [Fact]
    public void ATupletInAGraceBody_StealsOnlyWhatItPlayed()
    {
        (int Start, int Dur, int Pitch)[] Main(string music)
            => Performance("", music).Where(n => n.Dur > 100).ToArray();

        var plain = Main("grace { d'16 e' f' } c'4 c'2 c'4 | e'1 |");
        var triplet = Main("grace { tuplet 3/2 { d'16 e' f' } } c'4 c'2 c'4 | e'1 |");

        Assert.Equal(plain[0].Start * 2 / 3, triplet[0].Start);
        // ⚠️ The control is not decoration: 0 == 0 would satisfy the line above for an
        // exporter that had gone back to dropping the body, which is exactly the state this
        // trip found it in.
        Assert.True(plain[0].Start > 0);

        // ⚠️ AND THE RATIO STOPS AT THE GRACE. _tupletStack is SHARED with the main stream,
        // so a push this walk forgot to pop would shorten every note after the grace to two
        // thirds and nothing above would notice — the whole rest of the piece, silently
        // faster. ⚠️ From the SECOND main note on: the first is the one that pays the steal,
        // so it is SUPPOSED to differ (480 - 81 against 480 - 54), and asserting it equal was
        // this test's own first answer.
        Assert.Equal(plain.Skip(1).Select(n => n.Dur), triplet.Skip(1).Select(n => n.Dur));
        // …and the note that pays gives up exactly what was played, no more.
        Assert.Equal(plain[0].Start + plain[0].Dur, triplet[0].Start + triplet[0].Dur);
    }

    /// <summary>
    /// The two tuplet expanders answer the same question the same way: a tuplet written in a
    /// grace body compresses its notes by the ratio a tuplet written in the MAIN STREAM
    /// compresses its notes by.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS A DIFFERENTIAL NET, and it is here because the pair CANNOT BE FOLDED —
    /// checklist 7.7's own answer. <c>MeasureCollector</c>'s walk expands a main-stream tuplet
    /// as a node it recurses through; <c>GraceBodySupport.BodyElements</c> expands one into a
    /// flat list bracketed by markers, because a grace body's elements are judged one at a
    /// time by a narrowing that has to be able to NAME what it drops. The same argument the
    /// phrase pair carries (see APhraseInAGraceBody_OffersTheSameBodyTheMainStreamDoes) —
    /// and the same answer.
    /// <para>
    /// ⚠️ IT WRITES NO EXPECTED VALUE DOWN. The assert is a cross-multiplied equality of two
    /// RATIOS, so it survives a change to the 9/40 grace factor, to the division, or to the
    /// grace group's default duration — what it pins is that the two expanders agree, which
    /// is the thing a second spelling loses.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATupletInAGraceBody_CompressesTheWayTheMainStreamDoes()
    {
        // The first sounded event of each book, by start time then pitch.
        int First(string music) => Performance("", music)[0].Dur;

        int mainPlain = First("d'16 e' f' c'4 c'2 c'8 | e'1 |");
        int mainTuplet = First("tuplet 3/2 { d'16 e' f' } c'4 c'2 c'8 | e'1 |");
        int gracePlain = First("grace { d'16 e' f' } c'4 c'2 c'4 | e'1 |");
        int graceTuplet = First("grace { tuplet 3/2 { d'16 e' f' } } c'4 c'2 c'4 | e'1 |");

        // mainTuplet / mainPlain == graceTuplet / gracePlain, in integers.
        Assert.Equal(mainTuplet * gracePlain, graceTuplet * mainPlain);

        // ⚠️ …and the ratio is not 1 on either side, or the line above is 0 == 0 for two
        // walkers that had both stopped reading the tuplet. This is the pair, not decoration:
        // MEASURED 2026-08-30 (scratch/p302/ab), before this trip the grace side WAS the
        // book with no grace in it at all.
        Assert.True(mainTuplet < mainPlain);
        Assert.True(graceTuplet < gracePlain);
    }

    /// <summary>
    /// A CHORD and a REST inside a grace tuplet scale with it too — the chord's members
    /// sound two thirds as long, and the rest gives up two thirds as much of the grace's
    /// time, so the note after it starts where the ratio says.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE HALF SESSION 302 WIDENED WITHOUT A NET, found by auditing its own
    /// claims in the same session. A chord and a rest in a grace body have SOUNDED since
    /// 2026-07-10 while the page and the MusicXML still drop them (the open half of
    /// docs/HANDOFF.md §2 U8) — so teaching the expander to walk into a tuplet silently
    /// widened those two arms as well, and nothing asked whether the ratio reached them.
    /// MEASURED 2026-08-30 (scratch/p302/ab/e_chordrest_plain.lys and f_chordrest_tuplet.lys,
    /// division 480): both chord members 27 -> 18 ticks, the bare note after the rest
    /// 54 -> 36, the group 81 -> 54, and the main note still ends on the beat at 480.
    /// <para>
    /// ⚠️ THE REST IS THE ONE THAT COULD HAVE ROTTED QUIETLY: it emits no note, so a ratio
    /// that missed it would show up only as the LATER grace notes starting late — audible,
    /// invisible to any assert about pitches.
    /// </para>
    /// </remarks>
    [Fact]
    public void AChordAndARestInAGraceTuplet_ScaleWithIt()
    {
        const string Plain = "grace { <c' e'>16 r16 g'16 } c'4 c'2 c'4 | e'1 |";
        const string Triplet = "grace { tuplet 3/2 { <c' e'>16 r16 g'16 } } c'4 c'2 c'4 | e'1 |";

        var plain = Performance("", Plain);
        var triplet = Performance("", Triplet);

        // Same events, same pitches: the tuplet loses nothing on the way in.
        Assert.Equal(plain.Select(n => n.Pitch), triplet.Select(n => n.Pitch));

        // Both chord members and the bare note are two thirds as long…
        var plainGrace = plain.Where(n => n.Dur < 100).ToArray();
        var tripletGrace = triplet.Where(n => n.Dur < 100).ToArray();
        Assert.Equal(3, tripletGrace.Length);          // two chord members + the bare note
        Assert.All(tripletGrace.Zip(plainGrace), p => Assert.Equal(p.Second.Dur * 2 / 3, p.First.Dur));

        // …and the note written AFTER the rest starts two thirds of the way in, which is the
        // only place the rest's own scaling is visible at all.
        Assert.Equal(plainGrace[^1].Start * 2 / 3, tripletGrace[^1].Start);

        // The main note still ends on the beat: the steal shrank with what was played.
        var plainMain = plain.First(n => n.Dur > 100);
        var tripletMain = triplet.First(n => n.Dur > 100);
        Assert.Equal(plainMain.Start + plainMain.Dur, tripletMain.Start + tripletMain.Dur);

        // ⚠️ The control: none of the above says anything if the plain book had no grace in
        // it either. MEASURED before this trip, that was literally the state of the tupleted
        // book (scratch/p302/ab).
        Assert.True(plainGrace[^1].Start > 0);
    }
}
