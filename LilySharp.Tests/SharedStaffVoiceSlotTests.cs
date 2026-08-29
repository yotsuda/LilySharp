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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A staff shared by several parts (<c>condensedStaff</c>) numbers its voices ACROSS the
/// parts — <c>Staff.Voices</c> is their concatenation — while the collector walks one part
/// at a time. The number it stamps on every item it addresses by voice must be the item's
/// place in THAT array, because that is what every consumer reads it as
/// (<c>score.Voices[tie.VoiceIndex]</c>, <c>AnchorItem(voices, dynamic.VoiceIndex, …)</c>,
/// <c>BeamDetector</c>'s <c>t.VoiceIndex == v</c>). This is the instrument for that.
/// </summary>
/// <remarks>
/// ⚠️ THE CORPUS IS BLIND TO THIS AND THAT WAS MEASURED, NOT ASSUMED (session 284). The fix
/// moved 0 of the 899-book sweep (573 tracked + 326 author books) while the falsifying book
/// below moves — so the sweep sees the mechanism and simply has no book that writes the
/// shape. Only seven corpus books write <c>condensedStaff</c> at all, and none of them
/// gives its later parts a tuplet, a dynamic or an articulation. A 0-book A/B would have
/// said nothing either way (HANDOFF RULES §5.3), which is why the equations are asserted
/// against the part standing ALONE rather than against a snapshot.
/// <para>
/// ⚠️ WHAT A READER SAW BEFORE THE FIX, in the book below: the lower part's triplet number
/// was engraved ABOVE the staff over the UPPER part's notes (x 29.51, y 8.29 — the upper
/// part's beam is the only thing up there), and its <c>@f</c> sat under the upper part's
/// first note. Both now land on the notes that wrote them. The page got 2.70 shorter,
/// because the bracket that had been hoisted above the staff was reserving room there.
/// </para>
/// </remarks>
public class SharedStaffVoiceSlotTests
{
    /// <summary>
    /// BOTH parts are polyphonic, so the staff's voices are <c>[rh, rh.2, lh, lh.2]</c> and
    /// the two numbers this pins are different ones: the lower part's BASE is two (not one
    /// — the arithmetic is "everything already placed on this staff", not "the binding's
    /// ordinal"), and its second voice is base PLUS its own span slot, three. Its triplet
    /// and its <c>@f</c> ride the two islands that carry a collected voice number, and they
    /// are put in DIFFERENT voices of the lower part so one number cannot cover for the
    /// other.
    /// </summary>
    private const string FourVoiceBook = """
        octave absolute
        time 4/4
        key c major
        part rh { clef treble }
        part lh { clef treble }
        section Main {
          rh { voice { c''2 r4 g''8 a''8 | } voice { e'2 r2 | } }
          lh { voice { c'4@f e' g' a' | } voice { g2 tuplet 3/2 { c'8 d' e' } r4 | } }
        }
        form main { Main }
        score main "x" { condensedStaff { rh lh } }
        """;

    /// <summary>
    /// THE EQUATION: the number stamped on a shared staff's later part addresses THAT
    /// part's stream. Read through the staff's own voice array, which is the only place
    /// the number means anything.
    /// </summary>
    [Fact]
    public void ALaterPartOnASharedStaffIsAddressedByItsPlaceInTheStaffsVoices()
    {
        var score = Collect(FourVoiceBook);
        var staff = score.EnumerateStaves().Single().Staff;
        Assert.Equal(new[] { "rh", "rh.2", "lh", "lh.2" }, staff.Voices.Select(v => v.Name));

        // ⑴ The dynamic sits in lh's FIRST voice, so it names the part's BASE — 2, past
        //    BOTH of rh's. The anchor is readable as a note: lh opens on c' where rh's two
        //    voices open on c'' and e', so a wrong slot cannot pass by landing on an
        //    identical head.
        var dynamic = Assert.Single(score.Dynamics);
        Assert.Equal(0, dynamic.StaffIndex);
        Assert.Equal(2, dynamic.VoiceIndex);
        var anchor = LayoutUtilities.VoiceItemAt(
            staff.Voices, dynamic.VoiceIndex, dynamic.MeasureIndex, dynamic.ItemIndex);
        Assert.Equal(
            StaffPositionOf(staff.Voices[2], dynamic.MeasureIndex, dynamic.ItemIndex),
            StaffPositionOf(anchor));
        Assert.NotEqual(
            StaffPositionOf(staff.Voices[0], dynamic.MeasureIndex, dynamic.ItemIndex),
            StaffPositionOf(anchor));

        // ⑵ The bracket sits in lh's SECOND voice, so it names base PLUS the span slot —
        //    3, the last voice on the staff. This is the number the base alone cannot give.
        var bracket = Assert.Single(score.TupletBrackets);
        Assert.Equal(0, bracket.StaffIndex);
        Assert.Equal(3, bracket.VoiceIndex);
    }

    /// <summary>
    /// ...and the consequence the drawn page shows: the first part's beams are detected
    /// from the first part's tuplets only. Asserted against the SAME music standing alone,
    /// after first showing the case discriminates — a book whose beams do not move would
    /// make any slotting look sound, including none at all.
    /// </summary>
    /// <remarks>
    /// The shape is the one <see cref="ForeignTupletBracketTests"/> measured for two STAVES
    /// (its remark carries why this and its mirror were the only two of nine hand-written
    /// candidates that answer differently): the upper part's <c>c16 d16. e32 f16</c> has one
    /// interior stem that takes a flag direction, and a span STARTING on that stem's moment
    /// turns its beamlet round, because <c>flag_directions</c> skips a stem standing at a
    /// span boundary. Read as an index into the upper part, the lower part's triplet opens
    /// exactly there.
    /// </remarks>
    [Fact]
    public void TheFirstPartsBeamletIsUnmovedByTheSecondPartsTuplet()
    {
        const string upper = "c16 d16. e32 f16 g16 r2 r8 r16 |";
        var shared = Collect($$"""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef treble }
            section Main {
              rh { {{upper}} }
              lh { c'16 d' tuplet 3/2 { e' f' g' } r2. | }
            }
            form main { Main }
            score main "x" { condensedStaff { rh lh } }
            """);
        var staff = shared.EnumerateStaves().Single().Staff;
        var bracket = Assert.Single(shared.TupletBrackets);
        Assert.Equal(1, bracket.VoiceIndex);

        var drawn = Surface(new BeamDetector().DetectBeamGroups(
            MultiStaffLayouter.StaffBeamScoreOf(shared, staff, 0)));

        // ⑴ The case discriminates: hand the SAME detection the unslotted list — every
        //    bracket at slot 0, which is what the collector stamped before session 284 —
        //    and the first part's answer changes.
        var unslotted = new Score(
            staff.Voices, shared.TimeSignature, shared.KeySignature, "treble",
            tupletBrackets: shared.TupletBrackets
                .Select(t => t with { VoiceIndex = 0 }).ToImmutableArray());
        Assert.NotEqual(drawn, Surface(new BeamDetector().DetectBeamGroups(unslotted)));

        // ⑵ ...and the slotted answer for the first part is the one it gives with no second
        //    part in the book at all.
        var alone = Collect($$"""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            section Main { rh { {{upper}} } }
            form main { Main }
            score main "x" { staff rh }
            """);
        var aloneStaff = alone.EnumerateStaves().Single().Staff;
        Assert.Empty(alone.TupletBrackets);
        var aloneSurface = Surface(new BeamDetector().DetectBeamGroups(
            MultiStaffLayouter.StaffBeamScoreOf(alone, aloneStaff, 0)));
        Assert.Equal(aloneSurface, FirstVoiceOf(drawn));
    }

    /// <summary>
    /// The three slottings, off the render spec itself: what the collector is told about
    /// each binding before it walks a note.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TWO SHARED CASES TAKE THE SAME SLOT AND MEAN DIFFERENT THINGS BY IT, which is
    /// why they are still two values. A condensed staff's number is final — the staff IS the
    /// concatenation. A combined staff's is PROVISIONAL: the collector writes it in the same
    /// space so the two parts can be told apart at all, and <c>CombinedStaffAddressing</c>
    /// translates it once <c>PartCombiner.Combine</c> has said where everything went (it
    /// moves items between the two streams it emits, merges two parts' notes into one
    /// column, drops what it engraves with nobody, and writes spacers into the gaps — so
    /// neither the voice number nor the item index survives on its own). Session 285.
    /// </remarks>
    [Theory]
    [InlineData("condensedStaff", VoiceSlotting.AppendedToStaff)]
    [InlineData("combinedStaff", VoiceSlotting.CombinedIntoStaff)]
    public void ASharedStaffOpensOnceAndSlotsItsLaterPartsByHowTheStaffIsBuilt(
        string kind, VoiceSlotting later)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef treble }
            section Main { rh { c''1 | } lh { c'1 | } }
            form main { Main }
            score main "x" { {{kind}} { rh lh } }
            """);
        var slots = RenderSpecParser.FindFirst(tree).GetVoiceBindings()
            .Select(b => (b.VoiceName, b.Slotting)).ToArray();
        Assert.Equal(new[] { ("rh", VoiceSlotting.OwnStaff), ("lh", later) }, slots);
    }

    /// <summary>
    /// A staff of its own still starts at slot 0, and its <c>voice { } { }</c> spans still
    /// count from there — the arithmetic the shared case generalises must not cost the
    /// common case its numbering. Two staves, so a wrong base would show as a staff's
    /// second voice claiming a slot the staff does not have.
    /// </summary>
    [Fact]
    public void StavesOfTheirOwnKeepCountingFromZero()
    {
        var score = Collect("""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef bass }
            section Main {
              rh { voice { tuplet 3/2 { c''8 d'' e'' } r2. | } voice { e'1 | } }
              lh { voice { tuplet 3/2 { c8 d e } r2. | } voice { e,1 | } }
            }
            form main { Main }
            score main "x" { grandStaff { staff rh staff lh } }
            """);
        Assert.Equal(
            new[] { (0, 0), (1, 0) },
            score.TupletBrackets.Select(t => (t.StaffIndex, t.VoiceIndex)).OrderBy(x => x));
        foreach (var (_, staff, index) in score.EnumerateStaves())
            foreach (var t in score.TupletBrackets.Where(t => t.StaffIndex == index))
                Assert.InRange(t.VoiceIndex, 0, staff.Voices.Length - 1);
    }

    /// <summary>
    /// A <c>combinedStaff</c> whose two parts never agree: rh in eighths, lh in quarters
    /// with a triplet of its own, so the combiner routes them into two streams and every
    /// address lands IN RANGE when read at the wrong slot. That is what makes the book a
    /// falsifier rather than a crash — nothing is dropped by a guard and nothing warns.
    /// </summary>
    private const string CombinedApartBook = """
        octave absolute
        time 4/4
        key c major
        part rh { clef treble }
        part lh { clef treble }
        section Main {
          rh { c''8 d'' e'' f'' g'' a'' b'' c''' | }
          lh { c'4 e'4@f g'4@staccato tuplet 3/2 { c'8 d' e' } | }
        }
        form main { Main }
        score main "x" { %%KIND%% { rh lh } }
        """;

    /// <summary>
    /// THE EQUATION for the combiner: what the second part wrote is addressed to the stream
    /// the combiner put that part IN, not to the slot it was collected at.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ASSERTIONS GO THROUGH THE STAFF'S VOICE ARRAY, because that is the only place
    /// a voice index means anything, and they name the NOTE — lh's three quarters are c', e'
    /// and g' where rh's eighths at the same indices are c'', d'' and e'', so a wrong slot
    /// cannot pass by landing on a head that happens to look the same.
    /// <para>
    /// MEASURED on the drawn page before this was closed (scratch/p285/comb-ann.lys): the
    /// <c>@f</c> written on lh's second quarter (x 15.00) was engraved at x 12.44, the
    /// centre of rh's SECOND EIGHTH (x 11.79); the <c>@staccato</c> written on lh's third
    /// quarter (x 21.41) sat on rh's third eighth (x 15.00); and the triplet number stood
    /// ABOVE the staff at x 22.65 over rh's items 3..5, because the bracket was read as the
    /// first part's and the first part's stems are up. Its own notes are at x 27.82 and its
    /// stems are down.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACombinedStaffsSecondPartIsAddressedToTheStreamTheCombinerPutItIn()
    {
        var score = Collect(CombinedApartBook.Replace("%%KIND%%", "combinedStaff"));
        var staff = score.EnumerateStaves().Single().Staff;
        Assert.Equal(2, staff.Voices.Length);
        // The same part with nothing to combine with — the reference the anchors are read
        // against, so no expected pitch is written down anywhere in this test.
        var lh = PartAlone("c'4 e'4@f g'4@staccato tuplet 3/2 { c'8 d' e' } |").Voices[0];
        int? LhItem(int index) => StaffPositionOf(lh.Measures[0].Items[index]);

        // ⑴ The dynamic hangs off lh's SECOND quarter (e'), not rh's second eighth (d'').
        var dynamic = Assert.Single(score.Dynamics);
        Assert.Equal(0, dynamic.StaffIndex);
        Assert.Equal(LhItem(1), StaffPositionOf(LayoutUtilities.VoiceItemAt(
            staff.Voices, dynamic.VoiceIndex, dynamic.MeasureIndex, dynamic.ItemIndex)));

        // ⑵ …and the articulation off lh's THIRD quarter (g').
        var script = Assert.Single(score.Articulations);
        Assert.Equal(LhItem(2), StaffPositionOf(LayoutUtilities.VoiceItemAt(
            staff.Voices, script.VoiceIndex, script.MeasureIndex, script.ItemIndex)));

        // ⑶ …and the bracket covers lh's triplet — the part's items 3..5. Read as indices
        //    into rh it would name rh's f'', g'' and a'' instead, which is where it drew.
        var bracket = Assert.Single(score.TupletBrackets);
        Assert.Equal(
            new[] { LhItem(3), LhItem(4), LhItem(5) },
            Enumerable.Range(bracket.StartNoteIndex, bracket.EndNoteIndex - bracket.StartNoteIndex + 1)
                .Select(i => StaffPositionOf(LayoutUtilities.VoiceItemAt(
                    staff.Voices, bracket.VoiceIndex, bracket.MeasureIndex, i))));
    }

    /// <summary>
    /// …and the differential form of the same equation: parts that never agree are two
    /// streams either way, so a <c>combinedStaff</c> and a <c>condensedStaff</c> must anchor
    /// the SAME music to the same notes.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS NET NEEDS NO EXPECTED VALUES, which is its point (HANDOFF RULES §5.4): the
    /// two spellings reach the staff by different roads — one concatenates the parts'
    /// voices, the other rewrites them — and the question they are both asked is "which note
    /// did the writer hang this on". It caught nothing the test above does not, and it will
    /// catch what a future change to either road does to the other.
    /// ⚠️ It does NOT protect a shape where the parts DO agree: there the combiner merges
    /// them and the two spellings are meant to differ. <see cref="Collect"/>ing the apart
    /// book is what keeps that out.
    /// </remarks>
    [Fact]
    public void PartsThatNeverAgreeAnchorAlikeWhetherTheStaffIsCondensedOrCombined()
    {
        static (int? Dynamic, int? Script, int?[] Bracket) AnchorsOf(string kind, string book)
        {
            var score = Collect(book.Replace("%%KIND%%", kind));
            var staff = score.EnumerateStaves().Single().Staff;
            var d = Assert.Single(score.Dynamics);
            var a = Assert.Single(score.Articulations);
            var t = Assert.Single(score.TupletBrackets);
            return (
                StaffPositionOf(LayoutUtilities.VoiceItemAt(staff.Voices, d.VoiceIndex, d.MeasureIndex, d.ItemIndex)),
                StaffPositionOf(LayoutUtilities.VoiceItemAt(staff.Voices, a.VoiceIndex, a.MeasureIndex, a.ItemIndex)),
                Enumerable.Range(t.StartNoteIndex, t.EndNoteIndex - t.StartNoteIndex + 1)
                    .Select(i => StaffPositionOf(LayoutUtilities.VoiceItemAt(
                        staff.Voices, t.VoiceIndex, t.MeasureIndex, i)))
                    .ToArray());
        }

        var condensed = AnchorsOf("condensedStaff", CombinedApartBook);
        var combined = AnchorsOf("combinedStaff", CombinedApartBook);
        Assert.Equal(condensed.Dynamic, combined.Dynamic);
        Assert.Equal(condensed.Script, combined.Script);
        Assert.Equal(condensed.Bracket, combined.Bracket);
        // …and the case discriminates: the anchors are not all the same note to begin with.
        Assert.NotEqual(combined.Dynamic, combined.Script);
    }

    /// <summary>
    /// The item index MOVES, not just the voice: the combiner drops the silence a part
    /// spends under the other's solo and writes ONE spacer for the whole gap, so the note
    /// that was the part's third item is its second.
    /// </summary>
    /// <remarks>
    /// A slotting that carried the voice and left the index alone would put the <c>@f</c> on
    /// the note AFTER the one that was written with it — which is why this is asserted by
    /// pitch rather than by number.
    /// </remarks>
    [Fact]
    public void TheItemIndexMovesWhereTheCombinerWroteASpacer()
    {
        var score = Collect("""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef treble }
            section Main {
              rh { c''8 d'' e'' f'' g'' a'' b'' c''' | }
              lh { r4 r4 c'4@f e'4 | }
            }
            form main { Main }
            score main "x" { combinedStaff { rh lh } }
            """);
        var staff = score.EnumerateStaves().Single().Staff;
        var dynamic = Assert.Single(score.Dynamics);
        // It was written on the part's item 2 and it is not there any more…
        Assert.NotEqual(2, dynamic.ItemIndex);
        // …and where it IS is the same note, read through the part standing alone.
        var lh = PartAlone("r4 r4 c'4@f e'4 |").Voices[0];
        Assert.Equal(
            StaffPositionOf(lh.Measures[0].Items[2]),
            StaffPositionOf(LayoutUtilities.VoiceItemAt(
                staff.Voices, dynamic.VoiceIndex, dynamic.MeasureIndex, dynamic.ItemIndex)));
    }

    /// <summary>
    /// A passage the combiner engraves with NOBODY takes the annotations written on it with
    /// it, because there is no note left for them to hang on.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS ONE HAS A CORPUS OBSERVER AND LILYPOND'S OWN ANSWER BESIDE IT. The music is
    /// bar 1 of <c>audit/lpreg/pcsm-probe.lys</c>, the twin of LilyPond's
    /// <c>input/regression/part-combine-silence-mixed.ly</c>, and that book's header records
    /// what 2.26.0 engraves there: "ONE rest, dir=(), +1.0 ss, label 'r' ONLY ('R' goes with
    /// its event)". Until session 285 Lily# printed BOTH labels — the "R" belonged to the
    /// multi-measure rest the routing had sent to NullVoice, and nothing was asking the
    /// routing where its annotations had gone. Three corpus books moved onto LilyPond's
    /// answer when this closed (pcsm-probe, pcsm-frame, pcsm-frame2) and nothing else in
    /// 899 did.
    /// LILYPOND-REF: ly/music-functions-init.ly:1643-1651 make-directed-part-combine-music —
    /// <c>\context NullVoice = "null"</c>, the context with no engravers.
    /// </remarks>
    [Fact]
    public void ThePassageEngravedByNobodyTakesItsAnnotationsWithIt()
    {
        var score = Collect("""
            octave absolute
            time 4/4
            part vone { clef treble }
            part vtwo { clef treble }
            section A {
              vone { R1@text("R") | }
              vtwo { r1@text("r") | }
            }
            form main { A }
            score main "x" { combinedStaff { vone vtwo } }
            """);
        var text = Assert.Single(score.Dynamics);
        Assert.Equal("r", text.Text);
    }

    /// <summary>
    /// A part that is itself polyphonic keeps its extra voices on the staff untouched, and
    /// what was written in them is addressed at the slot they were APPENDED to — after the
    /// voices the combiner emitted, in concatenation order.
    /// </summary>
    /// <remarks>
    /// The arithmetic this pins is the one branch of the translation that is not a lookup:
    /// concatenation slot 3 here (part two's second voice) is the staff's slot 3 as well,
    /// but only because the numbers happen to meet — the combiner emitted two voices and
    /// part two's first voice was consumed, so slot 3 is the second appended voice, at
    /// <c>2 + (3 - 2)</c>. A book where the combiner emits ONE voice moves it.
    /// </remarks>
    [Fact]
    public void AnExtraVoiceOfACombinedPartIsAddressedWhereItWasAppended()
    {
        var score = Collect("""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef treble }
            section Main {
              rh { c''8 d'' e'' f'' g'' a'' b'' c''' | }
              lh { voice { c'4 e' g' b' | } voice { c2@f e2 | } }
            }
            form main { Main }
            score main "x" { combinedStaff { rh lh } }
            """);
        var staff = score.EnumerateStaves().Single().Staff;
        var dynamic = Assert.Single(score.Dynamics);
        Assert.InRange(dynamic.VoiceIndex, 0, staff.Voices.Length - 1);
        var lh = PartAlone("voice { c'4 e' g' b' | } voice { c2@f e2 | }");
        Assert.Equal(
            StaffPositionOf(lh.Voices[1].Measures[0].Items[0]),
            StaffPositionOf(LayoutUtilities.VoiceItemAt(
                staff.Voices, dynamic.VoiceIndex, dynamic.MeasureIndex, dynamic.ItemIndex)));
    }

    /// <summary>
    /// THE INVARIANT, over the shape that has all three of the combiner's rewrites in it:
    /// whatever a collected item is re-addressed to, it names a real item of the voice it
    /// names. A span may not straddle two voices, because it is engraved in the one it opens
    /// in.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE BOOK IS CHOSEN SO THE PARTS MERGE MID-TUPLET, which is the one arm no other
    /// test here reaches. lh's triplet eighths are WRITTEN as eighths — that is what
    /// LilyPond's comparable-note-events compares, not the sounding duration
    /// (LILYPOND-REF: scm/part-combiner.scm:59-74) — so where rh also writes an eighth
    /// within a ninth of them the two are one note column, and the bracket's first note is
    /// in the shared stream while the rest of it is not. It is CLIPPED there rather than
    /// reaching across, and this asserts the property that makes clipping the right answer
    /// rather than the shape clipping happens to produce: there is no LilyPond measurement
    /// of this book to write a shape down from (HANDOFF RULES §5.3).
    /// </remarks>
    [Fact]
    public void EveryReAddressedItemNamesAnItemOfTheVoiceItNames()
    {
        var score = Collect("""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef treble }
            section Main {
              rh { c''8 d'' e'' f'' g'' a'' b'' c''' | }
              lh { c'4 e'4@f g'4@staccato tuplet 3/2 { c''8 d'' e'' } | }
            }
            form main { Main }
            score main "x" { combinedStaff { rh lh } }
            """);
        var staff = score.EnumerateStaves().Single().Staff;

        void Resolves(int voice, int measure, int item, string what)
        {
            Assert.InRange(voice, 0, staff.Voices.Length - 1);
            Assert.True(
                LayoutUtilities.VoiceItemAt(staff.Voices, voice, measure, item) != null,
                $"{what} names voice {voice}, measure {measure}, item {item} — which is not there");
        }

        foreach (var d in score.Dynamics)
            Resolves(d.VoiceIndex, d.MeasureIndex, d.ItemIndex, "a dynamic");
        foreach (var a in score.Articulations)
            Resolves(a.VoiceIndex, a.MeasureIndex, a.ItemIndex, "an articulation");
        foreach (var t in score.TupletBrackets)
        {
            Resolves(t.VoiceIndex, t.MeasureIndex, t.StartNoteIndex, "a bracket's start");
            Resolves(t.VoiceIndex, t.MeasureIndex, t.EndNoteIndex, "a bracket's end");
            Assert.True(t.EndNoteIndex >= t.StartNoteIndex);
        }
        // …and the case discriminates: this book really does merge the two parts, so the
        // arm being asserted is the one that ran.
        Assert.Contains(staff.Voices[0].Measures[0].Items, i => i is ChordItem);
    }

    // ---------- helpers ----------

    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    /// <summary>
    /// The same music as a staff of its OWN — the reference the shared-staff anchors are
    /// read against, so no expected pitch has to be written down (and a change to how the
    /// part itself is collected moves both sides together instead of reddening the wrong
    /// test).
    /// </summary>
    private static Staff PartAlone(string music) => Collect($$"""
        octave absolute
        time 4/4
        key c major
        part lh { clef treble }
        section Main { lh { {{music}} } }
        form main { Main }
        score main "x" { staff lh }
        """).EnumerateStaves().Single().Staff;

    private static int? StaffPositionOf(MusicItem? item)
        => item is NoteItem n ? n.StaffPosition : null;

    private static int? StaffPositionOf(Voice voice, int measureIndex, int itemIndex)
        => StaffPositionOf(LayoutUtilities.VoiceItemAt(
            ImmutableArray.Create(voice), 0, measureIndex, itemIndex));

    /// <summary>The whole of a detection's answer, as a string, so two answers can be
    /// compared and a difference can be read. The beamlet counts are the point here.</summary>
    private static string Surface(ImmutableArray<BeamGroup> groups) =>
        string.Join(";", groups.Select(g =>
            $"{g.MeasureIndex}:{g.StartIndex}:{(g.StemUp ? 'u' : 'd')}:{g.GrowDirection}:{g.VoiceIndex}"
            + "|" + string.Join(",", g.Members.Select(m =>
                $"{m.ResolveMeasureIndex(g.MeasureIndex)}.{m.ItemIndex}"
                + $".{(m.MemberStemUp ? 'u' : 'd')}.{m.BeamCount}.{m.BeamCountLeft}.{m.BeamCountRight}"))));

    /// <summary>The groups of voice 0 out of a whole staff's surface — the staff carries
    /// both parts' beams, the book standing alone carries only the first part's.</summary>
    private static string FirstVoiceOf(string surface) =>
        string.Join(";", surface.Split(';').Where(g => g.Split('|')[0].EndsWith(":0")));
}
