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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The part combiner's ANALYSIS: what two parts are doing relative to each other at each
/// moment, and which voice that puts each of them in.
/// </summary>
/// <remarks>
/// <para>
/// Every claim here is LilyPond's, either read off scm/part-combiner.scm or measured from
/// its output. The measured ones come from <c>scratch/lpreg/pcombine-lp.ly</c>, four bars
/// chosen to hit unisono, solo1, solo2 and chords, dumped grob by grob.
/// </para>
/// <para>
/// ⚠️ THE TESTS THIS FILE REPLACES ASSERTED A DIFFERENT ANSWER, and it was wrong in a way
/// worth remembering: <c>Analyze_DifferentNotes_ReturnsApart</c> gave two notes six diatonic
/// steps apart and required "apart". LilyPond combines them into a CHORD — its default
/// chord-range is a ninth — so most two-part writing comes out as one voice, not two. The
/// old analyser also walked the two parts by ITEM INDEX rather than by moment, so two voices
/// with different rhythms were compared note-for-note regardless of when they sounded.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PartCombineTests
{
    private static NoteItem Note(int staffPosition, Fraction duration, string? accidental = null,
        bool slurStart = false, bool slurEnd = false, bool tieStart = false) =>
        new(staffPosition, duration, 0, accidental, false, 0,
            hasTieStart: tieStart, hasSlurStart: slurStart, hasSlurEnd: slurEnd);

    private static RestItem Rest(Fraction duration) => new(duration, 0, 0);

    private static RestItem MultiMeasureRest(Fraction duration) =>
        new(duration, 0, 0) { IsMultiMeasure = true };

    private static Measure MakeMeasure(params MusicItem[] items) =>
        new(ImmutableArray.Create(items), BarlineType.None, BarlineType.None, null, 0, 0);

    private static Voice MakeVoice(string name, params Measure[] measures) =>
        new(name, ImmutableArray.Create(measures));

    /// <summary>The configuration at each moment, which is what LilyPond's
    /// <c>determine-split-list</c> returns.</summary>
    private static PartCombineConfig[] Split(Voice one, Voice two) =>
        PartCombiner.DetermineSplitList(one, two).Select(s => s.Config).ToArray();

    // ------------------------------------------------------------------ the analysis

    [Fact]
    public void IdenticalNotes_AreUnisono()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));

        Assert.Equal(
            [PartCombineConfig.Unisono, PartCombineConfig.Unisono],
            Split(v1, v2).Take(2));
    }

    [Fact]
    public void DifferentNotesWithinANinth_BecomeAChord_NotTwoVoices()
    {
        // ★ The rule the previous tests had backwards. chord-range defaults to (0 . 8) —
        // LILYPOND-REF: ly/music-functions-init.ly:1653-1671 partCombine → make-directed-part-combine-music
        // — so two parts a sixth apart with the same rhythm are ONE voice of chords.
        // MEASURED: pcombine-lp.ly's fourth bar (c'/g, e'/g, g'/g, e'/g — a fourth to a
        // seventh) is engraved as four two-note chords with four stems.
        var v1 = MakeVoice("1", MakeMeasure(Note(4, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(-2, Fraction.Quarter)));

        Assert.Equal(PartCombineConfig.Chords, Split(v1, v2)[0]);
    }

    [Fact]
    public void NotesFurtherApartThanANinth_StayApart()
    {
        // Nine diatonic steps: one past the range's upper end.
        var v1 = MakeVoice("1", MakeMeasure(Note(4, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(-5, Fraction.Quarter)));

        Assert.Equal(PartCombineConfig.Apart, Split(v1, v2)[0]);
    }

    [Fact]
    public void TheLowerPartMustBeTheSECONDOne()
    {
        // The interval is SIGNED — part one minus part two (scm/part-combiner.scm:459-465),
        // and the range starts at 0 — so a part one BELOW part two is out of range however
        // close it is. Crossed parts are engraved apart, which is the only way to see that
        // they crossed.
        var v1 = MakeVoice("1", MakeMeasure(Note(-2, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(4, Fraction.Quarter)));

        Assert.Equal(PartCombineConfig.Apart, Split(v1, v2)[0]);
    }

    [Fact]
    public void SamePitchDifferentDuration_IsApart()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter), Rest(Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Half)));

        Assert.Equal(PartCombineConfig.Apart, Split(v1, v2)[0]);
    }

    [Fact]
    public void AnAccidentalThatOnlyONEPartWrites_BreaksTheUnison()
    {
        // ⑤ of the shelf: the old check compared STAFF POSITION alone, so c and c-sharp on
        // the same line counted as a unison and would have been engraved as one notehead —
        // silently dropping an accidental. Pitch, not position.
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter, accidental: "sharp")));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Quarter)));

        Assert.NotEqual(PartCombineConfig.Unisono, Split(v1, v2)[0]);
    }

    [Fact]
    public void WhenOnlyPartOneSounds_ItIsASolo()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Half), Note(0, Fraction.Half)));
        var v2 = MakeVoice("2", MakeMeasure(MultiMeasureRest(Fraction.Whole)));

        Assert.Equal(PartCombineConfig.Solo1, Split(v1, v2)[0]);
    }

    [Fact]
    public void WhenOnlyPartTwoSounds_ItIsSoloII()
    {
        var v1 = MakeVoice("1", MakeMeasure(MultiMeasureRest(Fraction.Whole)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Half), Note(0, Fraction.Half)));

        Assert.Equal(PartCombineConfig.Solo2, Split(v1, v2)[0]);
    }

    [Fact]
    public void RestsThatBeginAndEndTogether_AreOneRest()
    {
        // LILYPOND-REF: scm/part-combiner.scm:512-533 analyze-synced-silence, and the claim
        // of input/regression/part-combine-silence.ly: "Rests must begin and end
        // simultaneously to be merged into the shared voice."
        var v1 = MakeVoice("1", MakeMeasure(Rest(Fraction.Quarter), Rest(Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Rest(Fraction.Quarter), Rest(Fraction.Quarter)));

        Assert.Equal(
            [PartCombineConfig.Unisilence, PartCombineConfig.Unisilence],
            Split(v1, v2).Take(2));
    }

    [Fact]
    public void RestsOfDifferentLengths_AreNotMerged()
    {
        var v1 = MakeVoice("1", MakeMeasure(Rest(Fraction.Half)));
        var v2 = MakeVoice("2", MakeMeasure(Rest(Fraction.Quarter), Rest(Fraction.Quarter)));

        Assert.DoesNotContain(PartCombineConfig.Unisilence, Split(v1, v2));
    }

    [Fact]
    public void AgainstAMultiMeasureRest_ThePlainRestIsTheOneShown()
    {
        // LILYPOND-REF: scm/part-combiner.scm:653-661 analyze-synced-apart-silence — "rest
        // with multi-measure rest: choose the rest".
        var v1 = MakeVoice("1", MakeMeasure(Rest(Fraction.Whole)));
        var v2 = MakeVoice("2", MakeMeasure(MultiMeasureRest(Fraction.Whole)));

        Assert.Equal(PartCombineConfig.Silence1, Split(v1, v2)[0]);
    }

    [Fact]
    public void ASlurHeldOverAMoment_KeepsThePartsApart()
    {
        // LILYPOND-REF: scm/part-combiner.scm:486-501 previous-span-state — the parts may
        // only be compared when
        // the same spanners are open in both. A slur in one part alone is a difference the
        // notes do not show.
        var v1 = MakeVoice("1", MakeMeasure(
            Note(0, Fraction.Quarter, slurStart: true), Note(2, Fraction.Quarter, slurEnd: true)));
        var v2 = MakeVoice("2", MakeMeasure(
            Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));

        Assert.Equal(PartCombineConfig.Apart, Split(v1, v2)[1]);
    }

    [Fact]
    public void TheSameSlurInBothParts_DoesNotKeepThemApart()
    {
        // The control for the test above — otherwise it would pass for a version that simply
        // refused to combine anything slurred. input/regression/part-combine-slur.ly is
        // exactly this shape.
        var v1 = MakeVoice("1", MakeMeasure(
            Note(0, Fraction.Quarter, slurStart: true), Note(2, Fraction.Quarter, slurEnd: true)));
        var v2 = MakeVoice("2", MakeMeasure(
            Note(0, Fraction.Quarter, slurStart: true), Note(2, Fraction.Quarter, slurEnd: true)));

        Assert.Equal(PartCombineConfig.Unisono, Split(v1, v2)[1]);
    }

    [Fact]
    public void TheDecisionTakenOnTheSecondNote_ReachesTheFirst()
    {
        // ★ The analysis is NOT local.
        // LILYPOND-REF: scm/part-combiner.scm:410-420 put, inside analyze-time-step: it fills
        // BACKWARDS over every moment still undecided, which is the claim of
        // input/regression/part-combine-global.ly: "the decision for using separate voices in
        // the 1st measure is made on the 2nd note, but influences the 1st note."
        // Here the first moment is a unison on its own terms; the second is two octaves
        // apart, which is out of range and puts the whole tied span apart.
        var v1 = MakeVoice("1", MakeMeasure(
            Note(0, Fraction.Quarter, tieStart: true), Note(0, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(
            Note(0, Fraction.Quarter, tieStart: true), Note(-9, Fraction.Quarter)));

        Assert.Equal(PartCombineConfig.Apart, Split(v1, v2)[0]);
    }

    [Fact]
    public void PartsWithDifferentRhYthms_AreNeverMistakenForASolo()
    {
        // ⑴ of the shelf. The old analyser advanced the two parts by ITEM INDEX, so
        // `c4 d e f` against `g2 g2` paired the third and fourth quarters with nothing and
        // called them a solo. Compared by MOMENT there is no moment where one part is
        // silent, so nothing here is a solo — the durations differ, so it is all apart.
        var v1 = MakeVoice("1", MakeMeasure(
            Note(0, Fraction.Quarter), Note(1, Fraction.Quarter),
            Note(2, Fraction.Quarter), Note(3, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(-4, Fraction.Half), Note(-4, Fraction.Half)));

        var split = Split(v1, v2);
        Assert.DoesNotContain(PartCombineConfig.Solo1, split);
        Assert.DoesNotContain(PartCombineConfig.Solo2, split);
    }

    // ------------------------------------------------------------------ the routing

    [Fact]
    public void AUnisonIsEngravedOnce()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Quarter), Note(2, Fraction.Quarter)));

        var result = PartCombiner.Combine(v1, v2);

        // One voice, two items: the second part is routed to the null voice and engraved by
        // nobody —
        // LILYPOND-REF: ly/music-functions-init.ly:1643-1651 make-directed-part-combine-music
        // ends with \context NullVoice = "null".
        Assert.Single(result.Voices);
        Assert.Equal(2, result.Voices[0].Measures[0].Items.Length);
    }

    [Fact]
    public void ASoloDropsTheOtherPartsRests()
    {
        // MEASURED: pcombine-lp.ly's second bar has part two resting for the whole bar and
        // LilyPond engraves NO rest there — the dump shows two note heads and nothing else.
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Half), Note(0, Fraction.Half)));
        var v2 = MakeVoice("2", MakeMeasure(MultiMeasureRest(Fraction.Whole)));

        var result = PartCombiner.Combine(v1, v2);

        Assert.Single(result.Voices);
        Assert.All(result.Voices[0].Measures[0].Items, i => Assert.IsType<NoteItem>(i));
    }

    [Fact]
    public void TwoPartsWithinRange_ComeOutAsOneChord()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(4, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(-2, Fraction.Quarter)));

        var result = PartCombiner.Combine(v1, v2);

        Assert.Single(result.Voices);
        var chord = Assert.IsType<ChordItem>(result.Voices[0].Measures[0].Items[0]);
        Assert.Equal([-2, 4], chord.Notes.Select(n => n.StaffPosition));
    }

    [Fact]
    public void WhereThePartsAreApart_TheirStemsArePinnedUpAndDown()
    {
        // The "one" and "two" contexts are created \with { \voiceOne } and \with
        // { \voiceTwo } —
        // LILYPOND-REF: ly/music-functions-init.ly:1668-1671 partCombine → make-directed-part-combine-music.
        var v1 = MakeVoice("1", MakeMeasure(Note(4, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(-5, Fraction.Quarter)));

        var result = PartCombiner.Combine(v1, v2);

        Assert.Equal(2, result.Voices.Length);
        Assert.True(((NoteItem)result.Voices[0].Measures[0].Items[0]).StemUpOverride);
        Assert.False(((NoteItem)result.Voices[1].Measures[0].Items[0]).StemUpOverride);
    }

    [Fact]
    public void WhereThePartsAreCombined_NothingPinsTheStem()
    {
        // ★ The other half of the same claim, and the one a "put them both in voice 1"
        // implementation would fail: "shared" and "solo" are created with NO voice settings,
        // so the stem follows the pitch. MEASURED: every one of pcombine-lp.ly's twelve
        // stems points UP, including the two in the bar where only the LOWER part plays —
        // a forced voice-two would have pointed them down.
        var v1 = MakeVoice("1", MakeMeasure(Note(-6, Fraction.Quarter)));
        var v2 = MakeVoice("2", MakeMeasure(Note(-6, Fraction.Quarter)));

        var result = PartCombiner.Combine(v1, v2);

        Assert.Null(((NoteItem)result.Voices[0].Measures[0].Items[0]).StemUpOverride);
    }

    [Fact]
    public void WhenOnePartRunsOut_TheOtherStaysInTheVoiceItWasIn()
    {
        // analyze-time-step STOPS at the first moment where a part has no state left
        // (LILYPOND-REF: scm/part-combiner.scm:478-504 analyze-time-step — the recursion is
        // inside the else arm), so the moments after that keep no configuration at all.
        // LilyPond's state machines have no row for that, assq-ref returns #f, and a part
        // with no context change stays where it is. Reading the missing row as "back to
        // your own voice" instead would pin the surviving part's stems for the rest of the
        // piece.
        //
        // ⚠️ The shape matters: the undecided moments are the ones BEYOND the index where
        // analyze-time-step stopped, and try-solo stops at the first of them, so part two
        // needs at least three bars more than part one for one to carry a note. Written
        // with only one spare bar the test passes either way — verified by putting the old
        // reading back.
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Whole)));
        var v2 = MakeVoice("2",
            MakeMeasure(Note(0, Fraction.Whole)),
            MakeMeasure(Note(-6, Fraction.Whole)),
            MakeMeasure(Note(-6, Fraction.Whole)),
            MakeMeasure(Note(-6, Fraction.Whole)));

        var result = PartCombiner.Combine(v1, v2);

        Assert.Null(((NoteItem)result.Voices[0].Measures[3].Items[0]).StemUpOverride);
    }

    [Fact]
    public void TheSecondVoiceIsNotCreatedWhenThePartsNeverSeparate()
    {
        var v1 = MakeVoice("1", MakeMeasure(Note(0, Fraction.Whole)));
        var v2 = MakeVoice("2", MakeMeasure(Note(0, Fraction.Whole)));

        Assert.Single(PartCombiner.Combine(v1, v2).Voices);
    }

    // ------------------------------------------------------------------ the labels

    [Fact]
    public void TheThreeLabelsAppearInOrder()
    {
        // The music of pcombine-lp.ly, whose labels LilyPond prints as "a2", "Solo",
        // "Solo II" — and nothing over the fourth bar, because 'chords carries the same
        // Divisi label as 'apart and Divisi prints no text
        // (LILYPOND-REF: scm/part-combiner.scm:830-834 default-part-combine-mark-alist,
        // lily/part-combine-engraver.cc:69-85 create_item).
        var v1 = MakeVoice("1",
            MakeMeasure(Note(-6, Fraction.Quarter), Note(-5, Fraction.Quarter),
                        Note(-4, Fraction.Quarter), Note(-3, Fraction.Quarter)),
            MakeMeasure(Note(-2, Fraction.Half), Note(-2, Fraction.Half)),
            MakeMeasure(MultiMeasureRest(Fraction.Whole)),
            MakeMeasure(Note(-6, Fraction.Quarter), Note(-4, Fraction.Quarter),
                        Note(-2, Fraction.Quarter), Note(-4, Fraction.Quarter)));
        var v2 = MakeVoice("2",
            MakeMeasure(Note(-6, Fraction.Quarter), Note(-5, Fraction.Quarter),
                        Note(-4, Fraction.Quarter), Note(-3, Fraction.Quarter)),
            MakeMeasure(MultiMeasureRest(Fraction.Whole)),
            MakeMeasure(Note(-9, Fraction.Half), Note(-9, Fraction.Half)),
            MakeMeasure(Note(-9, Fraction.Quarter), Note(-9, Fraction.Quarter),
                        Note(-9, Fraction.Quarter), Note(-9, Fraction.Quarter)));

        var marks = PartCombiner.Combine(v1, v2).Marks;

        Assert.Equal(["a2", "Solo", "Solo II"], marks.Select(m => m.Text));
        Assert.Equal([0, 1, 2], marks.Select(m => m.MeasureIndex));
        Assert.All(marks, m => Assert.Equal(0, m.ItemIndex));
    }

    [Fact]
    public void ALabelIsNotRepeatedWhileTheStateHolds()
    {
        // merge-same-label: consecutive unison bars print ONE "a2".
        // LILYPOND-REF: scm/part-combiner.scm:846-891 splits-to-states-using — states with
        // the same label as the previous state are skipped.
        var bar = new[]
        {
            MakeMeasure(Note(0, Fraction.Whole)),
            MakeMeasure(Note(0, Fraction.Whole)),
            MakeMeasure(Note(0, Fraction.Whole)),
        };
        var v1 = MakeVoice("1", bar);
        var v2 = MakeVoice("2", bar);

        Assert.Single(PartCombiner.Combine(v1, v2).Marks);
    }
}
