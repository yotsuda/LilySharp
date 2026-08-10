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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The split configuration at one moment: what the two parts are doing relative to each
/// other, and therefore which voice each of them is engraved in.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/part-combiner.scm:339-788 determine-split-list — the symbols this
/// enum spells are that function's own <c>configuration</c> values.
/// </remarks>
public enum PartCombineConfig
{
    /// <summary>No configuration decided yet. LilyPond leaves the slot an empty list and
    /// tests it with <c>symbol?</c>; this is that empty list.</summary>
    Unset,

    /// <summary>The parts are engraved as two voices.</summary>
    Apart,

    /// <summary>Both parts rest, but not identically: two voices, each with its own rest.</summary>
    ApartSilence,

    /// <summary>Reachable only through a caller that supplies its own state machine —
    /// <c>determine-split-list</c> never produces it. Carried because the two context-change
    /// tables have a row for it.
    /// LILYPOND-REF: scm/part-combiner.scm:962-1014 default-part-combine-context-change-state-machine-one</summary>
    ApartSpanner,

    /// <summary>Different notes, same rhythm, within <c>chord-range</c>: ONE voice, the two
    /// pitches merged into a chord. This is the default outcome for most two-part writing —
    /// the range is a ninth.</summary>
    Chords,

    /// <summary>The same notes: one voice, one notehead, "a2".</summary>
    Unisono,

    /// <summary>The same silence: one voice, one rest.</summary>
    Unisilence,

    /// <summary>Only part one sounds: one voice, "Solo".</summary>
    Solo1,

    /// <summary>Only part two sounds: one voice, "Solo II".</summary>
    Solo2,

    /// <summary>Both silent, and part one's silence is the one to print.</summary>
    Silence1,

    /// <summary>Both silent, and part two's silence is the one to print.</summary>
    Silence2,
}

/// <summary>One entry of the split list: a moment and the configuration that starts there.</summary>
public sealed record PartCombineSplit(Fraction Moment, PartCombineConfig Config);

/// <summary>
/// The voice a part's music is routed into at a moment. LilyPond makes these five Voice
/// contexts on the staff and moves each part between them with ContextChange events.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/music-functions-init.ly:1643-1651 make-directed-part-combine-music —
/// <c>\context Voice = "one" … "two" … "shared" … "solo" … \context NullVoice = "null"</c>.
/// </remarks>
public enum PartCombineVoiceId
{
    /// <summary>Part one's own voice, <c>\voiceOne</c> (stems up).</summary>
    One,

    /// <summary>Part two's own voice, <c>\voiceTwo</c> (stems down).</summary>
    Two,

    /// <summary>The voice both parts share. NO voice settings, so stems follow the pitch.</summary>
    Shared,

    /// <summary>The voice a solo passage is engraved in. Also unset, so stems follow the pitch.</summary>
    Solo,

    /// <summary>LilyPond's NullVoice: the music is heard by the analysis and engraved by
    /// nobody. This is how the partner's rest disappears under a solo.</summary>
    Null,
}

/// <summary>
/// An "a2" / "Solo" / "Solo II" label produced by the combiner, anchored at the item it
/// belongs to.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/part-combine-engraver.cc:69-100 create_item / process_music — only
/// <c>solo1</c>, <c>solo2</c> and <c>unisono</c> carry text; the label waits for a moment
/// that has a note (<c>partCombineTextsOnNote</c>, default true).
/// </remarks>
public sealed record PartCombineMark(int MeasureIndex, int ItemIndex, string Text);

/// <summary>What <see cref="PartCombiner.Combine"/> produced.</summary>
/// <param name="Voices">One or two voices for the staff: the shared/solo/one stream, and
/// part two's own stream where the parts are apart. The second is absent when they never are.</param>
/// <param name="Marks">The labels, in moment order.</param>
/// <param name="SplitList">The split list itself, so a test can assert the analysis
/// without going through the engraving.</param>
public sealed record PartCombineResult(
    ImmutableArray<Voice> Voices,
    ImmutableArray<PartCombineMark> Marks,
    ImmutableArray<PartCombineSplit> SplitList);

/// <summary>
/// The part combiner: two parts in, one staff's worth of voices out, with the passages
/// where they agree merged into a single voice and the passages where one of them is
/// silent engraved without the other's rests.
/// </summary>
/// <remarks>
/// <para>
/// This is a MUSIC TRANSFORMATION, run in the collect phase, because that is what it is in
/// LilyPond too: <c>\partCombine</c> analyses the two event lists, then rewrites the music
/// as five Voice contexts plus a sequence of context changes that move each part between
/// them (LILYPOND-REF: ly/music-functions-init.ly:1588-1651 make-directed-part-combine-music).
/// Nothing about it is layout.
/// </para>
/// <para>
/// ⚠️ WHAT IS NOT PORTED, and why it cannot be reached rather than was skipped:
/// <list type="bullet">
/// <item>LILYPOND-REF: scm/part-combiner.scm:354-405 analyze-forced-combine — it reads
/// <c>partCombineForced</c>, which comes from <c>\partCombineApart</c> and friends. Lily#
/// has no spelling for them, so no event can carry it. Adding the spelling is what putting
/// this pass back would take — the pass is a fold over the same result vector.</item>
/// <item>The <c>cresc</c>/<c>decr</c> span states (:215-220, :227-242). Lily# dynamics are
/// not items in the voice stream at all — they are annotations resolved later — so there is
/// nothing in this input to read them off. Slurs, phrasing slurs, ties and manual beams ARE
/// on the items and are analysed.</item>
/// <item>Pitch is compared as (staff position, printed accidental), because that is what a
/// <see cref="NoteItem"/> carries. Two notes that sound alike but print differently — one
/// part restating an accidental the other still has in force — compare unequal. Carrying the
/// alteration on the item is what closing that would take.</item>
/// </list>
/// </para>
/// </remarks>
internal static class PartCombiner
{
    /// <summary>
    /// The default <c>chord-range</c>: notes up to and including a ninth apart may be
    /// combined into one chord. ⚠️ This is why most two-part writing comes out as ONE voice
    /// of chords rather than two voices — measured on <c>scratch/lpreg/pcombine-lp.ly</c>,
    /// whose fourth bar (a fourth to a seventh apart, same rhythm) LilyPond engraves as four
    /// two-note chords with four stems, not as eight notes with eight.
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/music-functions-init.ly:1653-1671 partCombine → make-directed-part-combine-music,
    /// whose chord-range argument defaults to <c>'(0 . 8)</c>.</remarks>
    public const int ChordMinDiff = 0;

    /// <summary>Upper end of <see cref="ChordMinDiff"/>'s range, in diatonic steps.</summary>
    public const int ChordMaxDiff = 8;

    // ⚠️ The three texts below cite their file WITHOUT a line number on purpose: the only
    // thing at those lines is `soloText = "Solo"`, a name with no '-' or '_' in it, and the
    // citation ratchet counts a line range whose text names no compound symbol as an
    // unnamed citation. They are Score-context defaults, easy to find by name.

    /// <summary>Text for a unisono passage. LILYPOND-REF: ly/engraver-init.ly — aDueText.</summary>
    public const string ADueText = "a2";

    /// <summary>Text for a part-one solo. LILYPOND-REF: ly/engraver-init.ly — soloText.</summary>
    public const string SoloText = "Solo";

    /// <summary>Text for a part-two solo. LILYPOND-REF: ly/engraver-init.ly — soloIIText.</summary>
    public const string SoloIIText = "Solo II";

    // ---------------------------------------------------------------- voice states

    /// <summary>
    /// One moment of one part: what starts there, and which spanners are open.
    /// LILYPOND-REF: scm/part-combiner.scm:21-105 Voice-state, previous-voice-state, done?.
    /// </summary>
    /// <remarks>
    /// LilyPond's <c>events</c> is a list because a Voice can have several events at one
    /// moment; a Lily# voice holds a linear item list, so the list is one item — or none,
    /// for the terminal state that marks the end of the part (LilyPond's recording group
    /// records that moment too, and <c>done?</c> asks for exactly it).
    /// </remarks>
    private sealed class VoiceState
    {
        public Fraction Moment;
        public MusicItem? Item;
        public int MeasureIndex = -1;
        public int ItemIndex = -1;

        /// <summary>Index into the split-state vector. LILYPOND-REF: :25 split-index.</summary>
        public int SplitIndex;

        public int VectorIndex;
        public VoiceState[] Vector = [];

        /// <summary>Open spanners as (key, split index where it opened), sorted — LilyPond's
        /// alist, compared by <c>equal?</c>, so the order has to be stable on both sides.
        /// LILYPOND-REF: scm/part-combiner.scm:29-32 spanner-state (analyze-spanner-states
        /// fills it).</summary>
        public ImmutableArray<(string Key, int Index)> SpanState = [];

        public VoiceState? Previous => VectorIndex > 0 ? Vector[VectorIndex - 1] : null;

        /// <summary>LILYPOND-REF: scm/part-combiner.scm:101-105 done?, on make-voice-states'
        /// vector — the last entry represents the end of the part.</summary>
        public bool Done => VectorIndex + 1 == Vector.Length;

        /// <summary>LILYPOND-REF: scm/part-combiner.scm:145-147 previous-span-state.</summary>
        public ImmutableArray<(string Key, int Index)> PreviousSpanState
            => Previous?.SpanState ?? [];
    }

    /// <summary>A note as the combiner compares notes: pitch and written duration, with
    /// everything else stripped.
    /// LILYPOND-REF: scm/part-combiner.scm:59-74 comparable-note-events.</summary>
    private readonly record struct ComparableNote(
        int StaffPosition, string? Accidental, Fraction BaseDuration, int Dots)
        : IComparable<ComparableNote>
    {
        public int CompareTo(ComparableNote other)
        {
            int c = StaffPosition.CompareTo(other.StaffPosition);
            if (c != 0) return c;
            c = string.CompareOrdinal(Accidental, other.Accidental);
            if (c != 0) return c;
            c = BaseDuration.CompareTo(other.BaseDuration);
            return c != 0 ? c : Dots.CompareTo(other.Dots);
        }
    }

    /// <summary>The three kinds of silence LilyPond tells apart.
    /// LILYPOND-REF: scm/part-combiner.scm:76-86 silence-events — multi-measure-rest-event
    /// and the rest.</summary>
    private enum SilenceKind { None, Rest, MultiMeasureRest, Skip }

    /// <summary>
    /// One moment of the merged timeline, holding both parts' states there.
    /// LILYPOND-REF: scm/part-combiner.scm:109-140 Split-state and current-or-previous-voice-states.
    /// </summary>
    private sealed class SplitState
    {
        public Fraction Moment;
        public PartCombineConfig Config = PartCombineConfig.Unset;
        public VoiceState? Vs1;
        public VoiceState? Vs2;
        public bool Synced;
    }

    // ---------------------------------------------------------------- entry point

    /// <summary>
    /// Combines two parts onto one staff.
    /// </summary>
    /// <param name="one">Part one — stems up where the parts are apart.</param>
    /// <param name="two">Part two — stems down where the parts are apart.</param>
    public static PartCombineResult Combine(Voice one, Voice two)
    {
        (one, two) = UnionWrittenRestBoundaries(one, two);

        var states1 = MakeVoiceStates(one);
        var states2 = MakeVoiceStates(two);
        var result = MakeSplitState(states1, states2);
        AnalyzeSpannerStates(states1);
        AnalyzeSpannerStates(states2);

        AnalyzeTimeStep(result, states1, states2);
        AnalyzeA2(result);
        AnalyzeSolo12(result);

        var splitList = result
            .Select(s => new PartCombineSplit(s.Moment, s.Config))
            .ToImmutableArray();

        return Route(one, two, states1, states2, result, splitList);
    }

    /// <summary>
    /// Gives BOTH parts a written-rest boundary at every bar where EITHER part begins one,
    /// so the merged voice keeps the boundaries of the part that gets dropped.
    /// </summary>
    /// <remarks>
    /// LilyPond changes which rest is printed at every moment a part BEGINS a multi-measure
    /// rest against the other's ongoing one, so the printed rests break at the UNION of both
    /// parts' written starts. Measured on 2.26.0 (scratch/lpreg/pcmsh-a.log): part one
    /// writing 8|16|4 against part two writing 16|8|4 engraves FOUR rests — 8, 8, 8, 4 —
    /// breaking at 0/8/16/24, so part one's 16-bar rest is CUT at 16 where part two begins.
    /// LILYPOND-REF: scm/part-combiner.scm:535-552 analyze-unsynced-silence — print the rest
    /// that begins now.
    /// <para>
    /// ⚠️ That analysis cannot reach this case here, and the reason is a model difference,
    /// not a porting gap: <c>R&lt;dur&gt;*N</c> is expanded into N one-bar copies by
    /// <c>MeasureCollector.MusicWalk</c> BEFORE the combiner runs, so the two parts are
    /// never UNSYNCED over rests — every moment holds a one-bar rest in each part and
    /// <c>AnalyzeSyncedSilence</c> always answers Unisilence. The written start survives
    /// only as <c>RestItem.OpensWrittenRun</c>, and the part routed to NullVoice takes its
    /// copy's boundary with it. Folding the boundaries together before routing is what puts
    /// LilyPond's break back, whichever part ends up being the one engraved.
    /// </para>
    /// </remarks>
    private static (Voice One, Voice Two) UnionWrittenRestBoundaries(Voice one, Voice two)
    {
        int n = Math.Min(one.Measures.Length, two.Measures.Length);
        ImmutableArray<Measure>.Builder? edited1 = null;
        ImmutableArray<Measure>.Builder? edited2 = null;

        for (int m = 0; m < n; m++)
        {
            int i1 = BarRestIndex(one.Measures[m]);
            int i2 = BarRestIndex(two.Measures[m]);
            if (i1 < 0 || i2 < 0)
                continue;

            bool opens1 = ((RestItem)one.Measures[m].Items[i1]).OpensWrittenRun;
            bool opens2 = ((RestItem)two.Measures[m].Items[i2]).OpensWrittenRun;
            if (opens1 == opens2)
                continue;

            if (opens2)
            {
                edited1 ??= one.Measures.ToBuilder();
                edited1[m] = OpenWrittenRestAt(one.Measures[m], i1);
            }
            else
            {
                edited2 ??= two.Measures.ToBuilder();
                edited2[m] = OpenWrittenRestAt(two.Measures[m], i2);
            }
        }

        return (edited1 == null ? one : one with { Measures = edited1.ToImmutable() },
                edited2 == null ? two : two with { Measures = edited2.ToImmutable() });
    }

    // "This bar rests with a written R" is spelled ONCE, in the engraver that groups the
    // runs — see MultiMeasureRestEngraver.BarRestIndex. A second spelling here skipped every
    // zero-duration item rather than only the break-aligned ones, so the two could disagree
    // about a bar; the fold and the grouping it feeds must ask the same question.
    private static int BarRestIndex(Measure measure)
        => Layout.MultiMeasureRestEngraver.BarRestIndex(measure);

    private static Measure OpenWrittenRestAt(Measure measure, int index)
    {
        var items = measure.Items.ToBuilder();
        items[index] = (RestItem)items[index] with { OpensWrittenRun = true };
        return measure with { Items = items.ToImmutable() };
    }

    /// <summary>
    /// The split list on its own — the analysis without the engraving, for tests that want
    /// to assert what LilyPond's <c>determine-split-list</c> would return.
    /// </summary>
    public static ImmutableArray<PartCombineSplit> DetermineSplitList(Voice one, Voice two)
        => Combine(one, two).SplitList;

    // ------------------------------------------------- the part's own simultaneous silence

    /// <summary>
    /// Brings the silence a part writes SIMULTANEOUSLY WITH ITSELF into the voice the
    /// combiner reads, choosing between the candidates the way LilyPond does: a rest or a
    /// multi-measure rest is the part's silence, and a skip is one only when there is no rest.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/part-combiner.scm:76-86 silence-events, which keeps multi-measure-rest-event
    /// and rest-event and takes skip-event only when neither is there — "There may be skips in
    /// the same part with rests for various reasons.  Regard the skips only if there are no
    /// rests." That sentence IS the second half of input/regression/part-combine-silence-mixed:
    /// <c>&lt;&lt; R1 s1 s4 &gt;&gt;</c> against <c>&lt;&lt; s4 s1 R1 &gt;&gt;</c> is one
    /// multi-measure rest against one multi-measure rest, so the parts are in unisilence and
    /// one rest is printed — and the answer cannot depend on the order the three are written
    /// in, which is why the book writes them mirrored.
    /// <para>
    /// ⚠️ ⒝ DERIVED, NOT LITERAL, and the reason is the model. LilyPond's Voice-state holds a
    /// LIST of events per moment (<c>events</c>, :21-105), so it can look past the skips
    /// without moving anything; a Lily# <c>VoiceState</c> holds ONE item, because a Lily#
    /// voice is a sequence. So the choice has to happen before the states are built, and it is
    /// made by SWAPPING the chosen silence into the part's first voice — the one
    /// <c>RenderSpec</c> hands to <see cref="Combine"/> — and leaving what was there in the
    /// branch it came from. Nothing is deleted and no voice changes length: both sides of a
    /// swap are silences, and the loser is a skip, which draws nothing wherever it sits.
    /// Making this literal means giving <c>VoiceState</c> an item LIST, and that reaches the
    /// routing too (LilyPond moves a whole Voice context, so all of a moment's events travel
    /// together) — that is the shape of the change, not a note that one is missing.
    /// </para>
    /// <para>
    /// ⚠️ WHAT THIS DOES NOT REACH, stated as narrowings rather than as answers — none of them
    /// is written by any book in the corpus, and each is a place the swap declines to act, NOT
    /// a place the combiner then gets right. Whatever the first voice happens to hold still
    /// goes to <see cref="Combine"/>, so a declined moment is answered by that item.
    /// <list type="bullet">
    /// <item>TWO CANDIDATES OF ONE KIND at a moment — two rests, or (no rests and) two skips.
    /// LilyPond's <c>silence-events</c> returns both and <c>analyze-synced-silence</c>
    /// (:512-533) requires exactly one on each side, which is how it reaches apart-silence;
    /// one item per moment cannot hold that. The arm in
    /// <see cref="AnalyzeSyncedApartSilence"/> that says the same thing is its other half.</item>
    /// <item>A MOMENT THE FIRST VOICE DOES NOT REACH. The swap trades against what that voice
    /// writes, so there has to be something there to trade with.</item>
    /// <item>A MEASURE THAT HOLDS A NOTE in any of the part's voices is skipped whole. The
    /// granularity is the measure because the swap is expressed on measures, not because
    /// LilyPond stops at one — it asks per moment, and a part with a note in bar 4 would still
    /// have its bar-4 silences chosen between.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ THE SWAP MOVES THE ITEM AND NOT WHAT HANGS OFF IT. A label is a
    /// <c>DynamicItem</c> keyed by (measure, item index, voice) in <c>Score.Dynamics</c>,
    /// outside the item stream — the same reason a label outlives the event the combiner sends
    /// to the null voice — so <c>&lt;&lt; R1^"R" s1 &gt;&gt;</c> would print its label over the
    /// skip's branch. The corpus book puts no labels on the branches it stacks, so this is
    /// unobserved rather than known-good; it closes when the labels travel with their items.
    /// </para>
    /// </remarks>
    public static ImmutableArray<Voice> ChooseSilenceWithinPart(ImmutableArray<Voice> partVoices)
    {
        if (partVoices.Length < 2)
            return partVoices;

        int measureCount = partVoices.Max(v => v.Measures.Length);
        Dictionary<int, ImmutableArray<Measure>.Builder>? edited = null;

        for (int m = 0; m < measureCount; m++)
        {
            var candidates = SimultaneousSilenceAt(partVoices, m);
            if (candidates == null)
                continue;

            foreach (var (_, group) in candidates)
            {
                int chosen = ChooseSilence(group);
                if (chosen < 0)                       // two rests at one moment — see above
                    continue;
                // The swap can only be made against what the FIRST voice writes at this
                // moment, because that is the stream RenderSpec hands to Combine. A moment
                // the first voice does not reach is left alone rather than moved into a
                // place there is nothing to trade with.
                int mine = group.FindIndex(c => c.Voice == 0);
                if (mine < 0 || mine == chosen)
                    continue;

                var (voiceA, indexA) = (0, group[mine].ItemIndex);
                var (voiceB, indexB) = (group[chosen].Voice, group[chosen].ItemIndex);

                edited ??= new Dictionary<int, ImmutableArray<Measure>.Builder>();
                var measuresA = MeasuresOf(edited, partVoices, voiceA);
                var measuresB = MeasuresOf(edited, partVoices, voiceB);

                var itemA = measuresA[m].Items[indexA];
                var itemB = measuresB[m].Items[indexB];
                measuresA[m] = ReplaceItem(measuresA[m], indexA, itemB);
                measuresB[m] = ReplaceItem(measuresB[m], indexB, itemA);
            }
        }

        if (edited == null)
            return partVoices;

        var result = partVoices.ToBuilder();
        foreach (var (voiceIndex, measures) in edited)
            result[voiceIndex] = partVoices[voiceIndex] with { Measures = measures.ToImmutable() };
        return result.ToImmutable();
    }

    /// <summary>One of the silences a part writes at one moment, and where it is written.</summary>
    private readonly record struct SilenceCandidate(int Voice, int ItemIndex, RestItem Rest);

    /// <summary>
    /// The moments of measure <paramref name="measureIndex"/> at which the part writes MORE
    /// THAN ONE silence, or null when the measure holds anything that is not a silence.
    /// </summary>
    /// <remarks>
    /// A measure that holds a note in ANY of the part's voices is left entirely alone. That is
    /// this port's granularity, not LilyPond's: LilyPond asks per MOMENT, and would still
    /// choose between the silences of a bar that also has a note somewhere. The swap is
    /// expressed on measures, so the gate is on measures — see the narrowings on
    /// <see cref="ChooseSilenceWithinPart"/>.
    /// </remarks>
    private static List<(Fraction Onset, List<SilenceCandidate> Group)>? SimultaneousSilenceAt(
        ImmutableArray<Voice> partVoices, int measureIndex)
    {
        var byOnset = new Dictionary<Fraction, List<SilenceCandidate>>();
        var order = new List<Fraction>();
        bool anySimultaneous = false;

        for (int v = 0; v < partVoices.Length; v++)
        {
            if (measureIndex >= partVoices[v].Measures.Length)
                continue;
            var items = partVoices[v].Measures[measureIndex].Items;
            var onset = Fraction.Zero;
            for (int i = 0; i < items.Length; i++)
            {
                var duration = SoundingDuration(items[i]);
                if (duration == Fraction.Zero)
                    continue;
                if (items[i] is not RestItem rest)
                    return null;
                if (!byOnset.TryGetValue(onset, out var group))
                {
                    group = [];
                    byOnset[onset] = group;
                    order.Add(onset);
                }
                else
                {
                    anySimultaneous = true;
                }
                group.Add(new SilenceCandidate(v, i, rest));
                onset += duration;
            }
        }

        if (!anySimultaneous)
            return null;

        order.Sort();
        return order
            .Where(o => byOnset[o].Count > 1)
            .Select(o => (o, byOnset[o]))
            .ToList();
    }

    /// <summary>
    /// Which of a moment's silences the part is understood to be resting with, or -1 when
    /// LilyPond's answer is not a single one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/part-combiner.scm:76-86 silence-events over multi-measure-rest-event,
    /// which filters on that and on rest-event and re-filters on skip-event only when the
    /// first came out empty. It returns a LIST, and :512-533 analyze-synced-silence asks for
    /// exactly one on each side — so "two of them" is an answer of its own and NOT a tie to be
    /// broken. ⚠️ That applies to the SKIPS TOO, which the first version of this got wrong: it
    /// declined two rests and then picked the first of several skips, so a part writing two
    /// skips at one moment would have been folded to one and could reach unisilence where
    /// LilyPond has apart-silence.
    /// </remarks>
    private static int ChooseSilence(List<SilenceCandidate> group)
    {
        var (rests, onlyRest) = OnlyOne(group, spacers: false);
        // ⚠️ The fallback is reached by "the rest filter came out EMPTY", not by "it did not
        // come out with one" — several rests are an answer, and skips are not consulted there.
        if (rests > 0)
            return rests == 1 ? onlyRest : -1;
        var (skips, onlySkip) = OnlyOne(group, spacers: true);
        return skips == 1 ? onlySkip : -1;
    }

    /// <summary>How many candidates of the requested kind the moment holds, and where the
    /// last one is.</summary>
    private static (int Count, int Index) OnlyOne(List<SilenceCandidate> group, bool spacers)
    {
        int count = 0, found = -1;
        for (int i = 0; i < group.Count; i++)
        {
            if (group[i].Rest.IsSpacer != spacers)
                continue;
            count++;
            found = i;
        }
        return (count, found);
    }

    private static ImmutableArray<Measure>.Builder MeasuresOf(
        Dictionary<int, ImmutableArray<Measure>.Builder> edited,
        ImmutableArray<Voice> partVoices, int voiceIndex)
    {
        if (!edited.TryGetValue(voiceIndex, out var measures))
        {
            measures = partVoices[voiceIndex].Measures.ToBuilder();
            edited[voiceIndex] = measures;
        }
        return measures;
    }

    private static Measure ReplaceItem(Measure measure, int index, MusicItem item)
    {
        var items = measure.Items.ToBuilder();
        items[index] = item;
        return measure with { Items = items.ToImmutable() };
    }

    // ---------------------------------------------------------------- building states

    private static Fraction SoundingDuration(MusicItem item) => item switch
    {
        NoteItem n => n.Duration,
        RestItem r => r.Duration,
        ChordItem c => c.Duration,
        _ => Fraction.Zero,
    };

    /// <summary>
    /// Flattens a voice into one state per moment, plus the terminal state at the end of
    /// the part. LILYPOND-REF: scm/part-combiner.scm:149-165 make-voice-states.
    /// </summary>
    /// <remarks>
    /// Zero-duration items (a clef, key or time change) share the moment of whatever sounds
    /// there. They are not events the combiner compares, so they are held on the state that
    /// carries the moment and routed with it.
    /// </remarks>
    private static VoiceState[] MakeVoiceStates(Voice voice)
    {
        var states = new List<VoiceState>();
        var moment = Fraction.Zero;
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            var items = voice.Measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var duration = SoundingDuration(items[ii]);
                if (duration == Fraction.Zero)
                    continue;
                states.Add(new VoiceState
                {
                    Moment = moment,
                    Item = items[ii],
                    MeasureIndex = mi,
                    ItemIndex = ii,
                });
                moment += duration;
            }
        }

        // The terminal state: LilyPond's recorded event list ends with the moment the part
        // finishes, and `done?` is "you are looking at that one".
        states.Add(new VoiceState { Moment = moment });

        var vector = states.ToArray();
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i].VectorIndex = i;
            vector[i].Vector = vector;
        }
        return vector;
    }

    /// <summary>
    /// Merges the two moment lists into one, cross-linking each voice state to the split
    /// index it lands on. LILYPOND-REF: scm/part-combiner.scm:167-197 make-split-state.
    /// </summary>
    private static SplitState[] MakeSplitState(VoiceState[] vs1, VoiceState[] vs2)
    {
        var result = new List<SplitState>();
        int idx1 = 0, idx2 = 0;
        while (idx1 < vs1.Length || idx2 < vs2.Length)
        {
            var state1 = idx1 < vs1.Length ? vs1[idx1] : null;
            var state2 = idx2 < vs2.Length ? vs2[idx2] : null;
            var min = state1 != null && state2 != null
                ? (state1.Moment < state2.Moment ? state1.Moment : state2.Moment)
                : (state1?.Moment ?? state2!.Moment);
            int inc1 = state1 != null && state1.Moment == min ? 1 : 0;
            int inc2 = state2 != null && state2.Moment == min ? 1 : 0;

            if (state1 != null) state1.SplitIndex = result.Count;
            if (state2 != null) state2.SplitIndex = result.Count;

            result.Add(new SplitState
            {
                Moment = min,
                Vs1 = state1,
                Vs2 = state2,
                Synced = inc1 == inc2,
            });
            idx1 += inc1;
            idx2 += inc2;
        }
        return result.ToArray();
    }

    /// <summary>
    /// Records which spanners are open at each moment, so a passage crossed by a slur, a
    /// tie or a manual beam is kept apart rather than merged mid-span.
    /// LILYPOND-REF: scm/part-combiner.scm:199-271 analyze-spanner-states.
    /// </summary>
    /// <remarks>
    /// The order of the analysers matters and is LilyPond's: the tie END is cleared before
    /// a tie START on the same note is recorded (:258), so <c>c~ c~ c</c> stays open across
    /// the middle note. A phrasing slur is filed under the same key as a tie, as there
    /// (:230).
    /// </remarks>
    private static void AnalyzeSpannerStates(VoiceState[] states)
    {
        var active = new List<(string Key, int Index)>();
        foreach (var vs in states)
        {
            if (vs.Item is { } item)
            {
                int here = vs.SplitIndex;

                // analyze-span-event (:227-242) — a stop clears, a start records.
                if (EndsSlur(item)) Remove(active, "slur");
                if (EndsBeam(item)) Remove(active, "beam");
                if (StartsSlur(item)) active.Add(("slur", here));
                if (StartsBeam(item)) active.Add(("beam", here));

                // analyze-tie-end then analyze-tie-start (:210-213, :204-208): any note
                // closes the open tie, and a tie mark on this note opens a new one.
                if (item is NoteItem or ChordItem) Remove(active, "tie");
                if (StartsTie(item)) active.Add(("tie", here));
            }

            vs.SpanState = active
                .OrderBy(a => a.Key, StringComparer.Ordinal)
                .ThenBy(a => a.Index)
                .ToImmutableArray();
        }

        static void Remove(List<(string Key, int Index)> active, string key)
            => active.RemoveAll(a => a.Key == key);
    }

    private static bool StartsSlur(MusicItem item) => item switch
    {
        NoteItem n => n.HasSlurStart,
        ChordItem c => c.HasSlurStart,
        RestItem r => r.HasSlurStart,
        _ => false,
    };

    private static bool EndsSlur(MusicItem item) => item switch
    {
        NoteItem n => n.HasSlurEnd,
        ChordItem c => c.HasSlurEnd,
        RestItem r => r.HasSlurEnd,
        _ => false,
    };

    private static bool StartsTie(MusicItem item) => item switch
    {
        NoteItem n => n.HasTieStart,
        ChordItem c => c.HasTieStart,
        _ => false,
    };

    private static bool StartsBeam(MusicItem item) => item switch
    {
        NoteItem n => n.HasBeamStart,
        ChordItem c => c.HasBeamStart,
        _ => false,
    };

    private static bool EndsBeam(MusicItem item) => item switch
    {
        NoteItem n => n.HasBeamEnd,
        ChordItem c => c.HasBeamEnd,
        _ => false,
    };

    // ---------------------------------------------------------------- the four passes

    private static ImmutableArray<ComparableNote> ComparableNoteEvents(VoiceState? vs)
    {
        switch (vs?.Item)
        {
            case NoteItem n:
                return [new ComparableNote(n.StaffPosition, n.Accidental, n.BaseDuration, n.Dots)];
            case ChordItem c:
                return c.Notes
                    .Select(x => new ComparableNote(x.StaffPosition, x.Accidental, c.BaseDuration, c.Dots))
                    .OrderBy(x => x)
                    .ToImmutableArray();
            default:
                return [];
        }
    }

    private static bool HasNotes(VoiceState? vs) => vs?.Item is NoteItem or ChordItem;

    private static SilenceKind Silence(VoiceState? vs) => vs?.Item switch
    {
        RestItem { IsSpacer: true } => SilenceKind.Skip,
        RestItem { IsMultiMeasure: true } => SilenceKind.MultiMeasureRest,
        RestItem => SilenceKind.Rest,
        _ => SilenceKind.None,
    };

    private static Fraction SilenceDuration(VoiceState? vs)
        => vs?.Item is RestItem r ? r.Duration : Fraction.Zero;

    /// <summary>
    /// Sets <paramref name="config"/> at <paramref name="index"/> and backwards over every
    /// entry still undecided. This backward fill is what makes the analysis non-local: the
    /// decision taken on the second note of a bar reaches the first.
    /// LILYPOND-REF: scm/part-combiner.scm:410-420 put, inside analyze-time-step.
    /// </summary>
    private static void Put(SplitState[] result, int index, PartCombineConfig config)
    {
        for (int i = index; i >= 0 && result[i].Config == PartCombineConfig.Unset; i--)
            result[i].Config = config;
    }

    /// <summary>
    /// LILYPOND-REF: scm/part-combiner.scm:409-504 analyze-time-step — decides each moment
    /// from that moment's events alone.
    /// </summary>
    private static void AnalyzeTimeStep(SplitState[] result, VoiceState[] states1, VoiceState[] states2)
    {
        for (int i = 0; i < result.Length; i++)
        {
            var now = result[i];
            var vs1 = now.Vs1;
            var vs2 = now.Vs2;

            // ⚠️ LilyPond recurses only through the else branch (:504), so the walk STOPS at
            // the first moment where one part has run out of states. Everything after it is
            // left for analyze-a2 and analyze-solo12 to decide.
            if (vs1 == null || vs2 == null)
            {
                Put(result, i, PartCombineConfig.Apart);
                return;
            }

            bool sameSpans =
                vs1.PreviousSpanState.SequenceEqual(vs2.PreviousSpanState)
                && vs1.SpanState.SequenceEqual(vs2.SpanState);

            if (now.Synced && sameSpans)
                AnalyzeNotes(result, i, now);
            else
                Put(result, i, PartCombineConfig.Apart);
        }
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:431-476 analyze-notes, over comparable-note-events.</summary>
    private static void AnalyzeNotes(SplitState[] result, int index, SplitState now)
    {
        var vs1 = now.Vs1!;
        var vs2 = now.Vs2!;
        var notes1 = ComparableNoteEvents(vs1);
        var notes2 = ComparableNoteEvents(vs2);

        // Neither part has notes: leave it undecided for analyze-a2.
        if (notes1.IsEmpty && notes2.IsEmpty)
            return;

        // One part has notes and the other does not.
        if (notes1.IsEmpty || notes2.IsEmpty)
        {
            Put(result, index, PartCombineConfig.Apart);
            return;
        }

        // Either part has a chord: only two IDENTICAL chords may combine.
        if (notes1.Length > 1 || notes2.Length > 1)
        {
            Put(result, index,
                ChordMinDiff <= 0 && notes1.SequenceEqual(notes2)
                    ? PartCombineConfig.Chords
                    : PartCombineConfig.Apart);
            return;
        }

        // Different durations.
        if (notes1[0].BaseDuration != notes2[0].BaseDuration || notes1[0].Dots != notes2[0].Dots)
        {
            Put(result, index, PartCombineConfig.Apart);
            return;
        }

        // Outside the chord range?
        int diff = notes1[0].StaffPosition - notes2[0].StaffPosition;
        if (diff < ChordMinDiff || diff > ChordMaxDiff)
        {
            Put(result, index, PartCombineConfig.Apart);
            return;
        }

        // Inside it: inherit whatever the open spanners were decided to be, and combine
        // only if nothing is open.
        if (vs1.Previous is { } p1) CopyStateFrom(result, index, p1);
        if (vs2.Previous is { } p2) CopyStateFrom(result, index, p2);
        if (vs1.SpanState.IsEmpty && vs2.SpanState.IsEmpty)
            Put(result, index, PartCombineConfig.Chords);
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:422-429 copy-state-from — take the
    /// configuration recorded where each open spanner started.</summary>
    private static void CopyStateFrom(SplitState[] result, int index, VoiceState vs)
    {
        foreach (var (_, startIndex) in vs.SpanState)
        {
            var previous = result[startIndex].Config;
            if (previous != PartCombineConfig.Unset)
                Put(result, index, previous);
        }
    }

    /// <summary>
    /// LILYPOND-REF: scm/part-combiner.scm:506-577 analyze-a2, analyze-synced-silence —
    /// promotes identical notes to unisono and identical silence to unisilence.
    /// </summary>
    private static void AnalyzeA2(SplitState[] result)
    {
        for (int i = 0; i < result.Length; i++)
        {
            var now = result[i];
            var vs1 = now.Vs1;
            var vs2 = now.Vs2;
            if (vs1 == null && vs2 == null)
                continue;

            var notes1 = ComparableNoteEvents(vs1);
            var notes2 = ComparableNoteEvents(vs2);

            if (now.Config == PartCombineConfig.Chords
                && !notes1.IsEmpty && notes1.SequenceEqual(notes2))
            {
                now.Config = PartCombineConfig.Unisono;
            }
            else if (now.Synced)
            {
                if (notes1.IsEmpty && notes2.IsEmpty)
                    AnalyzeSyncedSilence(now, vs1, vs2);
            }
            else
            {
                // Not synchronised: look at the state each part is actually in.
                var (cur1, cur2) = CurrentOrPreviousVoiceStates(now);
                if (!HasNotes(cur1) && !HasNotes(cur2))
                    AnalyzeUnsyncedSilence(now, cur1, cur2);
            }
        }
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:512-533 analyze-synced-silence.</summary>
    private static void AnalyzeSyncedSilence(SplitState now, VoiceState? vs1, VoiceState? vs2)
    {
        var kind1 = Silence(vs1);
        var kind2 = Silence(vs2);

        // Equal rests or equal skips, but not one of each.
        now.Config = kind1 != SilenceKind.None && kind1 == kind2
                     && SilenceDuration(vs1) == SilenceDuration(vs2)
            ? PartCombineConfig.Unisilence
            : PartCombineConfig.ApartSilence;
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:535-552 analyze-unsynced-silence — when
    /// a multi-measure rest begins against another part's ongoing one, print the one that
    /// begins now.</summary>
    private static void AnalyzeUnsyncedSilence(SplitState now, VoiceState? vs1, VoiceState? vs2)
    {
        bool mm1 = Silence(vs1) == SilenceKind.MultiMeasureRest;
        bool mm2 = Silence(vs2) == SilenceKind.MultiMeasureRest;

        if (mm1 && vs1!.Moment == now.Moment && (vs2 == null || mm2))
            now.Config = PartCombineConfig.Silence1;
        else if (mm2 && vs2!.Moment == now.Moment && (vs1 == null || mm1))
            now.Config = PartCombineConfig.Silence2;
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:129-140 current-or-previous-voice-states.</summary>
    private static (VoiceState? One, VoiceState? Two) CurrentOrPreviousVoiceStates(SplitState ss)
    {
        var vs1 = ss.Vs1;
        var vs2 = ss.Vs2;
        if (vs1 != null && vs1.Moment != ss.Moment) vs1 = vs1.Previous;
        if (vs2 != null && vs2.Moment != ss.Moment) vs2 = vs2.Previous;
        return (vs1, vs2);
    }

    /// <summary>
    /// LILYPOND-REF: scm/part-combiner.scm:579-754 analyze-solo12, analyze-apart-silence —
    /// turns stretches of
    /// 'apart where only one part sounds into solos, and sorts out which silence to print.
    /// </summary>
    private static void AnalyzeSolo12(SplitState[] result)
    {
        int i = 0;
        while (i < result.Length)
        {
            i = result[i].Config switch
            {
                PartCombineConfig.Apart => AnalyzeApart(result, i),
                PartCombineConfig.ApartSilence => AnalyzeApartSilence(result, i),
                _ => i + 1,
            };
        }
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:599-604 current-voice-state.</summary>
    private static VoiceState? CurrentVoiceState(SplitState now, int voiceNumber)
    {
        var vs = voiceNumber == 1 ? now.Vs1 : now.Vs2;
        if (vs == null || vs.Moment == now.Moment)
            return vs;
        return vs.Previous;
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:714-744 analyze-apart, gated on previous-span-state.</summary>
    private static int AnalyzeApart(SplitState[] result, int index)
    {
        var now = result[index];
        var vs1 = CurrentVoiceState(now, 1);
        var vs2 = CurrentVoiceState(now, 2);
        bool notes1 = HasNotes(vs1);
        bool notes2 = HasNotes(vs2);

        int next;
        if (!notes1 && !notes2)
        {
            // The earlier passes called 'apart what is really 'apart-silence.
            next = AnalyzeApartSilence(result, index);
        }
        else if (!notes2 && vs1 != null && vs1.Moment == now.Moment && vs1.PreviousSpanState.IsEmpty)
        {
            next = TrySolo(result, PartCombineConfig.Solo1, index, index);
        }
        else if (!notes1 && vs2 != null && vs2.Moment == now.Moment && vs2.PreviousSpanState.IsEmpty)
        {
            next = TrySolo(result, PartCombineConfig.Solo2, index, index);
        }
        else
        {
            next = index + 1;
        }

        return Math.Max(next, index + 1);
    }

    /// <summary>
    /// Finds the longest stretch that can be marked solo.
    /// LILYPOND-REF: scm/part-combiner.scm:606-639 try-solo, over current-voice-state.
    /// </summary>
    private static int TrySolo(
        SplitState[] result, PartCombineConfig type, int startIndex, int currentIndex)
    {
        while (currentIndex < result.Length)
        {
            var now = result[currentIndex];
            var soloState = CurrentVoiceState(now, type == PartCombineConfig.Solo1 ? 1 : 2);
            var silentState = CurrentVoiceState(now, type == PartCombineConfig.Solo1 ? 2 : 1);

            if (now.Config != PartCombineConfig.Apart)
                return currentIndex;
            if (HasNotes(silentState))
                return startIndex;
            if (soloState == null)
            {
                PutRange(result, type, startIndex, currentIndex);
                return currentIndex;
            }
            if (soloState.SpanState.IsEmpty)
            {
                // Rests are included: a long rest is shared with the silent voice and
                // classified as unisilence earlier, so it cannot be swept into a solo.
                PutRange(result, type, startIndex, currentIndex);
                startIndex = currentIndex + 1;
                currentIndex++;
                continue;
            }
            currentIndex++;
        }
        return startIndex;
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:589-593 put-range, which analyze-time-step's
    /// own put is not: this one overwrites a decided configuration.</summary>
    private static void PutRange(SplitState[] result, PartCombineConfig config, int from, int to)
    {
        for (int i = from; i <= to && i < result.Length; i++)
            result[i].Config = config;
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:641-712 analyze-apart-silence.</summary>
    private static int AnalyzeApartSilence(SplitState[] result, int index)
    {
        var now = result[index];
        var vs1 = CurrentVoiceState(now, 1);
        var vs2 = CurrentVoiceState(now, 2);

        if (vs1 == null || vs1.Done)
            now.Config = PartCombineConfig.Silence2;
        else if (vs2 == null || vs2.Done)
            now.Config = PartCombineConfig.Silence1;
        else if (now.Synced)
            AnalyzeSyncedApartSilence(result, index, vs1, vs2);
        else
            AnalyzeUnsyncedApartSilence(result, index);

        return index + 1;
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:644-674 analyze-synced-apart-silence.</summary>
    private static void AnalyzeSyncedApartSilence(
        SplitState[] result, int index, VoiceState vs1, VoiceState vs2)
    {
        var kind1 = Silence(vs1);
        var kind2 = Silence(vs2);

        // ⚠️ LilyPond's first arm — "multiple rests in the same part" — cannot fire here: a
        // Lily# voice has at most one item per moment, so each part has exactly one silence
        // or none. The `None` case stands in for it.
        if (kind1 == SilenceKind.None || kind2 == SilenceKind.None)
            result[index].Config = PartCombineConfig.ApartSilence;
        else if (kind1 == SilenceKind.Rest && kind2 == SilenceKind.MultiMeasureRest)
            result[index].Config = PartCombineConfig.Silence1;
        else if (kind1 == SilenceKind.MultiMeasureRest && kind2 == SilenceKind.Rest)
            result[index].Config = PartCombineConfig.Silence2;
        else if (kind1 == SilenceKind.MultiMeasureRest && kind2 == SilenceKind.MultiMeasureRest)
            // Equal multi-measure rests were classified unisilence earlier, so these differ:
            // choose the shorter one.
            result[index].Config = SilenceDuration(vs1) < SilenceDuration(vs2)
                ? PartCombineConfig.Silence1
                : PartCombineConfig.Silence2;
        else
            result[index].Config = PartCombineConfig.ApartSilence;
    }

    /// <summary>LILYPOND-REF: scm/part-combiner.scm:676-692 analyze-unsynced-apart-silence —
    /// stay in silence1/silence2 until the parts resynchronise.</summary>
    private static void AnalyzeUnsyncedApartSilence(SplitState[] result, int index)
    {
        var previous = index > 0 ? result[index - 1].Config : PartCombineConfig.ApartSilence;
        result[index].Config = previous is PartCombineConfig.Silence1 or PartCombineConfig.Silence2
            ? previous
            : PartCombineConfig.ApartSilence;
    }

    // ---------------------------------------------------------------- routing

    /// <summary>The state each part's context-change machine is in.</summary>
    private enum MachineState { Initial, Demoted, Promoted }

    /// <summary>
    /// Part one's context-change machine.
    /// LILYPOND-REF: scm/part-combiner.scm:962-989 default-part-combine-context-change-state-machine-one
    /// </summary>
    private static (MachineState Next, PartCombineVoiceId? Voice) MachineOne(
        MachineState state, PartCombineConfig config) => (state, config) switch
    {
        (MachineState.Initial, PartCombineConfig.Apart) => (MachineState.Initial, PartCombineVoiceId.One),
        (MachineState.Initial, PartCombineConfig.ApartSilence) => (MachineState.Initial, PartCombineVoiceId.One),
        (MachineState.Initial, PartCombineConfig.ApartSpanner) => (MachineState.Initial, PartCombineVoiceId.One),
        (MachineState.Initial, PartCombineConfig.Chords) => (MachineState.Initial, PartCombineVoiceId.Shared),
        (MachineState.Initial, PartCombineConfig.Silence1) => (MachineState.Initial, PartCombineVoiceId.Shared),
        (MachineState.Initial, PartCombineConfig.Silence2) => (MachineState.Demoted, PartCombineVoiceId.Null),
        (MachineState.Initial, PartCombineConfig.Solo1) => (MachineState.Initial, PartCombineVoiceId.Solo),
        (MachineState.Initial, PartCombineConfig.Solo2) => (MachineState.Demoted, PartCombineVoiceId.Null),
        (MachineState.Initial, PartCombineConfig.Unisono) => (MachineState.Initial, PartCombineVoiceId.Shared),
        (MachineState.Initial, PartCombineConfig.Unisilence) => (MachineState.Initial, PartCombineVoiceId.Shared),

        // After a part has been the exclusive input for a passage, the OTHER part is the
        // one that carries a following unisono — the demoted part's multi-measure rests
        // may have been killed.
        (MachineState.Demoted, PartCombineConfig.Apart) => (MachineState.Demoted, PartCombineVoiceId.One),
        (MachineState.Demoted, PartCombineConfig.ApartSilence) => (MachineState.Demoted, PartCombineVoiceId.One),
        (MachineState.Demoted, PartCombineConfig.ApartSpanner) => (MachineState.Demoted, PartCombineVoiceId.One),
        (MachineState.Demoted, PartCombineConfig.Chords) => (MachineState.Demoted, PartCombineVoiceId.Shared),
        (MachineState.Demoted, PartCombineConfig.Silence1) => (MachineState.Initial, PartCombineVoiceId.Shared),
        (MachineState.Demoted, PartCombineConfig.Silence2) => (MachineState.Demoted, PartCombineVoiceId.Null),
        (MachineState.Demoted, PartCombineConfig.Solo1) => (MachineState.Initial, PartCombineVoiceId.Solo),
        (MachineState.Demoted, PartCombineConfig.Solo2) => (MachineState.Demoted, PartCombineVoiceId.Null),
        (MachineState.Demoted, PartCombineConfig.Unisono) => (MachineState.Demoted, PartCombineVoiceId.Null),
        (MachineState.Demoted, PartCombineConfig.Unisilence) => (MachineState.Demoted, PartCombineVoiceId.Null),

        // A configuration with no row — in practice Unset, which survives on the moments
        // AFTER the shorter part has ended, where analyze-time-step stops. LilyPond's
        // assq-ref returns #f there and the state list simply gains no entry, so the part
        // stays in the context it was already in. Null means "no change" to the caller.
        _ => (state, null),
    };

    /// <summary>
    /// Part two's context-change machine.
    /// LILYPOND-REF: scm/part-combiner.scm:991-1014 default-part-combine-context-change-state-machine-two
    /// </summary>
    private static (MachineState Next, PartCombineVoiceId? Voice) MachineTwo(
        MachineState state, PartCombineConfig config) => (state, config) switch
    {
        (MachineState.Initial, PartCombineConfig.Apart) => (MachineState.Initial, PartCombineVoiceId.Two),
        (MachineState.Initial, PartCombineConfig.ApartSilence) => (MachineState.Initial, PartCombineVoiceId.Two),
        (MachineState.Initial, PartCombineConfig.ApartSpanner) => (MachineState.Initial, PartCombineVoiceId.Two),
        (MachineState.Initial, PartCombineConfig.Chords) => (MachineState.Initial, PartCombineVoiceId.Shared),
        (MachineState.Initial, PartCombineConfig.Silence1) => (MachineState.Initial, PartCombineVoiceId.Null),
        (MachineState.Initial, PartCombineConfig.Silence2) => (MachineState.Promoted, PartCombineVoiceId.Shared),
        (MachineState.Initial, PartCombineConfig.Solo1) => (MachineState.Initial, PartCombineVoiceId.Null),
        (MachineState.Initial, PartCombineConfig.Solo2) => (MachineState.Promoted, PartCombineVoiceId.Solo),
        (MachineState.Initial, PartCombineConfig.Unisono) => (MachineState.Initial, PartCombineVoiceId.Null),
        (MachineState.Initial, PartCombineConfig.Unisilence) => (MachineState.Initial, PartCombineVoiceId.Null),

        (MachineState.Promoted, PartCombineConfig.Apart) => (MachineState.Promoted, PartCombineVoiceId.Two),
        (MachineState.Promoted, PartCombineConfig.ApartSilence) => (MachineState.Promoted, PartCombineVoiceId.Two),
        (MachineState.Promoted, PartCombineConfig.ApartSpanner) => (MachineState.Promoted, PartCombineVoiceId.Two),
        (MachineState.Promoted, PartCombineConfig.Chords) => (MachineState.Promoted, PartCombineVoiceId.Shared),
        (MachineState.Promoted, PartCombineConfig.Silence1) => (MachineState.Initial, PartCombineVoiceId.Null),
        (MachineState.Promoted, PartCombineConfig.Silence2) => (MachineState.Promoted, PartCombineVoiceId.Shared),
        (MachineState.Promoted, PartCombineConfig.Solo1) => (MachineState.Initial, PartCombineVoiceId.Null),
        (MachineState.Promoted, PartCombineConfig.Solo2) => (MachineState.Promoted, PartCombineVoiceId.Solo),
        (MachineState.Promoted, PartCombineConfig.Unisono) => (MachineState.Promoted, PartCombineVoiceId.Shared),
        (MachineState.Promoted, PartCombineConfig.Unisilence) => (MachineState.Promoted, PartCombineVoiceId.Shared),

        // See MachineOne's last arm: no row means no context change.
        _ => (state, null),
    };

    /// <summary>The mark label a configuration carries, or null where it carries none — a
    /// configuration with no label leaves the previous one standing.
    /// LILYPOND-REF: scm/part-combiner.scm:823-834 default-part-combine-mark-alist.</summary>
    private static string? MarkLabel(PartCombineConfig config) => config switch
    {
        PartCombineConfig.Apart => "Divisi",
        PartCombineConfig.Chords => "Divisi",
        PartCombineConfig.Solo1 => "Solo1",
        PartCombineConfig.Solo2 => "Solo2",
        PartCombineConfig.Unisono => "Unisono",
        _ => null,
    };

    /// <summary>The text a mark label prints, or null for the labels that print nothing.
    /// LILYPOND-REF: lily/part-combine-engraver.cc:69-85 create_item.</summary>
    private static string? MarkText(string label) => label switch
    {
        "Solo1" => SoloText,
        "Solo2" => SoloIIText,
        "Unisono" => ADueText,
        _ => null,
    };

    /// <summary>
    /// Turns the split list into the staff's voices and labels: each part's items go to the
    /// voice its machine names, the dropped ones are not engraved at all, and two parts in
    /// the shared voice at one moment become a chord.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYPOND HAS FIVE VOICE CONTEXTS HERE AND THIS HAS TWO SLOTS — not a simplification
    /// but a consequence, and the argument is worth keeping because it is what would break if
    /// the tables changed. Read the two machines column by column: for every configuration the
    /// pair (part one's voice, part two's voice) is one of
    /// <c>(one, two)</c>, <c>(shared, shared)</c>, or one engraved against one <c>null</c>.
    /// There is no configuration that puts one part in <c>one</c> and the other in
    /// <c>shared</c>, so at most two voices ever sound, and whichever part is engraved alone
    /// is engraved in a voice with no settings. Slot 0 therefore carries whatever is singular
    /// (one / shared / solo, whichever part it comes from) and slot 1 only ever carries
    /// <c>two</c> — which is also why slot 1 stays absent in a piece that never separates.
    /// <c>(shared, shared)</c> is the case that needs a merge rather than a slot.
    /// </remarks>
    private static PartCombineResult Route(
        Voice one, Voice two,
        VoiceState[] states1, VoiceState[] states2,
        SplitState[] result,
        ImmutableArray<PartCombineSplit> splitList)
    {
        // Walk the split list once, in order, so both machines see every transition.
        var voice1Of = new PartCombineVoiceId[result.Length];
        var voice2Of = new PartCombineVoiceId[result.Length];
        var machine1 = MachineState.Initial;
        var machine2 = MachineState.Initial;
        // Each part starts in its own voice: LilyPond names the initial context "one" and
        // "two" when it builds the part music (make-part-music's initial-voice-name).
        var current1 = PartCombineVoiceId.One;
        var current2 = PartCombineVoiceId.Two;
        for (int i = 0; i < result.Length; i++)
        {
            var config = result[i].Config;
            (machine1, var next1) = MachineOne(machine1, config);
            (machine2, var next2) = MachineTwo(machine2, config);
            current1 = next1 ?? current1;
            current2 = next2 ?? current2;
            voice1Of[i] = current1;
            voice2Of[i] = current2;
        }

        int measureCount = Math.Max(one.Measures.Length, two.Measures.Length);
        var slot0 = new List<SlotEntry>[measureCount];
        var slot1 = new List<SlotEntry>[measureCount];
        for (int m = 0; m < measureCount; m++)
        {
            slot0[m] = [];
            slot1[m] = [];
        }

        // Where each sounding item ended up, so a label can name a position in the voice
        // that comes OUT rather than in the part that went in.
        var landed = new Dictionary<VoiceState, (int Slot, int Measure, int Entry)>();
        // Where part one put the item of a given moment, so part two's item of the same
        // moment can join it in one note column.
        var sharedAt = new Dictionary<int, (int Measure, int Entry)>();

        PlaceItems(one, states1, voice1Of, result, slot0, slot1, landed, sharedAt, isPartOne: true);
        PlaceItems(two, states2, voice2Of, result, slot0, slot1, landed, sharedAt, isPartOne: false);

        var items0 = new ImmutableArray<MusicItem>[measureCount];
        var items1 = new ImmutableArray<MusicItem>[measureCount];
        var index0 = new int[measureCount][];
        var index1 = new int[measureCount][];
        for (int m = 0; m < measureCount; m++)
        {
            (items0[m], index0[m]) = Materialise(slot0[m]);
            (items1[m], index1[m]) = Materialise(slot1[m]);
        }

        // Materialise inserts spacers, so the entry numbers the routing handed out are not
        // the item indices the built voice has. Translate before the labels are placed.
        var placed = new Dictionary<VoiceState, (int Slot, int Measure, int Index)>(landed.Count);
        foreach (var (vs, where) in landed)
            placed[vs] = (where.Slot, where.Measure,
                          (where.Slot == 0 ? index0 : index1)[where.Measure][where.Entry]);

        var voices = BuildVoices(one, two, items0, items1, measureCount);
        var marks = BuildMarks(result, placed);
        return new PartCombineResult(voices, marks, splitList);
    }

    /// <summary>One routed item and the moment WITHIN ITS MEASURE that it sounds at.</summary>
    /// <remarks>
    /// The onset has to be carried because the two parts write into the same slot and a part
    /// can fall silent in the middle of a measure. Reconstructing the timing by adding up the
    /// durations of whatever landed in the slot — which is what this did first — puts every
    /// item after a gap at the wrong moment.
    /// </remarks>
    private readonly record struct SlotEntry(Fraction Onset, MusicItem Item);

    /// <summary>
    /// Turns one slot's routed entries into the item sequence a <see cref="Measure"/> holds,
    /// filling the gaps with spacer rests, and reports where each entry ended up.
    /// </summary>
    /// <remarks>
    /// LilyPond does not need this step: its five contexts run side by side on a common
    /// clock, so a context that is handed nothing for a while is simply silent there. A
    /// Lily# voice is a SEQUENCE whose x follows from the durations in it, so the same
    /// silence has to be written down — as a spacer, which is never drawn.
    /// <para>
    /// ⚠️ An entry whose onset is EARLIER than the end of the entry before it cannot be
    /// expressed: one Lily# voice cannot hold two overlapping items. It is appended where it
    /// falls instead, which puts it late. This is not hypothetical — LilyPond reaches it
    /// whenever the split changes in the middle of a rest, e.g. part one holding r4 in "one"
    /// while part two starts a solo an eighth later (input/regression/part-combine-silence.ly
    /// score 2, bar 1: LP's columns are 2.400 apart, this tree's are 4.600 then 2.750).
    /// Closing it needs the solo/shared stream to be able to move to the other slot, which is
    /// the same architecture the direction-granularity note in HANDOFF §1 is about.
    /// </para>
    /// </remarks>
    private static (ImmutableArray<MusicItem> Items, int[] IndexOfEntry) Materialise(
        List<SlotEntry> entries)
    {
        var indexOfEntry = new int[entries.Count];
        if (entries.Count == 0)
            return ([], indexOfEntry);

        var order = new int[entries.Count];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        // Stable: entries written at one onset keep the order the routing gave them, which is
        // what puts a clef or key change before the note it belongs to.
        var onsets = entries;
        Array.Sort(order, (a, b) =>
        {
            int c = onsets[a].Onset.CompareTo(onsets[b].Onset);
            return c != 0 ? c : a.CompareTo(b);
        });

        var items = ImmutableArray.CreateBuilder<MusicItem>(entries.Count);
        var position = Fraction.Zero;
        foreach (int e in order)
        {
            var (onset, item) = entries[e];
            if (onset > position)
            {
                // ONE spacer, whatever the gap is. Its written value may not be a note value
                // (a gap left by a tuplet is not a sum of them), and that is deliberate: only
                // the total matters, because a spacer is never drawn. Splitting the gap into
                // note values instead would be inventing a rhythm LilyPond does not have —
                // and it is not free, since each spacer is another entry on the voice's clock.
                // ⚠️ NOT the same as LilyPond, which needs nothing here at all: its silent
                // context simply has no event. The one binding case in the corpus is a gap of
                // exactly 1/2 (part-combine-silence score 1), where every shape of this agrees.
                items.Add(new RestItem(onset - position, 0, item.SourcePosition) { IsSpacer = true });
                position = onset;
            }
            indexOfEntry[e] = items.Count;
            items.Add(item);
            var end = onset + SoundingDuration(item);
            if (end > position)
                position = end;
        }
        return (items.ToImmutable(), indexOfEntry);
    }


    /// <summary>
    /// Emits the labels: one where the mark label CHANGES, held back until a moment that
    /// actually has a note, and dropped if the passage never gets one.
    /// LILYPOND-REF: scm/part-combiner.scm:846-891 splits-to-states-using (merge-same-label)
    /// and lily/part-combine-engraver.cc:87-100 process_music (partCombineTextsOnNote).
    /// </summary>
    private static ImmutableArray<PartCombineMark> BuildMarks(
        SplitState[] result, Dictionary<VoiceState, (int Slot, int Measure, int Index)> landed)
    {
        var marks = ImmutableArray.CreateBuilder<PartCombineMark>();
        string? previousLabel = null;
        string? pendingText = null;

        foreach (var ss in result)
        {
            if (MarkLabel(ss.Config) is { } label && label != previousLabel)
            {
                previousLabel = label;
                pendingText = MarkText(label);
            }
            if (pendingText is not { } text)
                continue;

            var anchor = AnchorAt(ss);
            if (anchor == null || !landed.TryGetValue(anchor, out var where))
                continue;
            marks.Add(new PartCombineMark(where.Measure, where.Index, text));
            pendingText = null;
        }
        return marks.ToImmutable();
    }

    /// <summary>The voice state a label hangs on: the moment's part that actually has a
    /// note there.</summary>
    private static VoiceState? AnchorAt(SplitState ss)
    {
        if (HasNotes(ss.Vs1) && ss.Vs1!.Moment == ss.Moment) return ss.Vs1;
        if (HasNotes(ss.Vs2) && ss.Vs2!.Moment == ss.Moment) return ss.Vs2;
        return null;
    }

    /// <summary>
    /// Walks one part measure by measure, in source order, routing each sounding item to
    /// the voice its machine named.
    /// </summary>
    /// <remarks>
    /// Zero-duration items — a clef, key or time change — are not events the combiner
    /// classifies, and a condensed staff has one of each however many parts feed it. Part
    /// one's are carried; part two's are carried only into a measure where part one wrote
    /// none, so a change written in either part still reaches the staff and a change written
    /// in both is not engraved twice.
    /// </remarks>
    private static void PlaceItems(
        Voice part,
        VoiceState[] states,
        PartCombineVoiceId[] voiceOf,
        SplitState[] result,
        List<SlotEntry>[] slot0,
        List<SlotEntry>[] slot1,
        Dictionary<VoiceState, (int Slot, int Measure, int Entry)> landed,
        Dictionary<int, (int Measure, int Entry)> sharedAt,
        bool isPartOne)
    {
        var byPosition = new Dictionary<(int Measure, int Item), VoiceState>();
        foreach (var vs in states)
            if (vs.Item != null)
                byPosition[(vs.MeasureIndex, vs.ItemIndex)] = vs;

        for (int m = 0; m < part.Measures.Length; m++)
        {
            var items = part.Measures[m].Items;
            bool partOneWroteCommands = isPartOne || slot0[m].Any(e => SoundingDuration(e.Item) == Fraction.Zero);
            var onset = Fraction.Zero;

            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                if (SoundingDuration(item) == Fraction.Zero)
                {
                    if (isPartOne || !partOneWroteCommands)
                        slot0[m].Add(new SlotEntry(onset, item));
                    continue;
                }

                var vs = byPosition[(m, ii)];
                var target = voiceOf[vs.SplitIndex];
                var sounding = SoundingDuration(item);
                if (target == PartCombineVoiceId.Null)
                {
                    // LilyPond routes the part into NullVoice, a context that runs alongside
                    // the others on the same clock: the music is still there and still takes
                    // its time, it is just engraved by nobody. Only the ENGRAVING is dropped
                    // here; the onset walks on, so what the part writes next keeps its moment.
                    // LILYPOND-REF: ly/music-functions-init.ly:1618-1625 make-part-music — the
                    // part is ONE notation stream played simultaneously with its list of
                    // context changes, so which context it is in never moves its moments.
                    onset += sounding;
                    continue;
                }

                if (target == PartCombineVoiceId.Two)
                {
                    slot1[m].Add(new SlotEntry(onset,
                        InContext(WithVoiceDirection(item, up: false), target)));
                    landed[vs] = (1, m, slot1[m].Count - 1);
                    onset += sounding;
                    continue;
                }

                var routed = InContext(
                    target == PartCombineVoiceId.One
                        ? WithVoiceDirection(item, up: true)
                        : WithoutVoiceDirection(item),
                    target);

                // Two parts in the shared voice at one moment are ONE note column:
                // LilyPond puts both note events into the same Voice context, which is a
                // chord.
                if (!isPartOne
                    && result[vs.SplitIndex].Config == PartCombineConfig.Chords
                    && sharedAt.TryGetValue(vs.SplitIndex, out var at)
                    && at.Entry < slot0[at.Measure].Count)
                {
                    var host = slot0[at.Measure][at.Entry];
                    slot0[at.Measure][at.Entry] = host with { Item = MergeIntoChord(host.Item, routed) };
                    landed[vs] = (0, at.Measure, at.Entry);
                    onset += sounding;
                    continue;
                }

                slot0[m].Add(new SlotEntry(onset, routed));
                landed[vs] = (0, m, slot0[m].Count - 1);
                if (isPartOne)
                    sharedAt[vs.SplitIndex] = (m, slot0[m].Count - 1);
                onset += sounding;
            }
        }
    }

    /// <summary>
    /// Puts both parts' notes into one note column, which is what two note events in one
    /// Voice context are.
    /// </summary>
    /// <remarks>
    /// The pitches of both survive (they are the point); everything else is taken from part
    /// ONE, and it is worth saying which "everything else" can differ and which cannot.
    /// <list type="bullet">
    /// <item>CANNOT: a slur, tie or manual beam that starts or is held here. A 'chords
    /// moment requires the span state of BOTH parts to be empty — LILYPOND-REF:
    /// scm/part-combiner.scm:431-476 analyze-notes, over comparable-note-events, whose last
    /// condition is exactly that — and
    /// a note that opens one of those has it in its span state. So no spanner of part two is
    /// dropped here — there is none.</item>
    /// <item>CAN, and IS dropped: part two's per-note decoration — cue size, notehead style,
    /// tremolo, fingering, an editorial accidental. LilyPond keeps both events, so both
    /// decorations reach the column. Closing it means carrying them on
    /// <see cref="ChordNoteInfo"/>, which today holds pitch, accidental, ledgers and
    /// fingering only. ⚠️ No point observes it: the corpus has no combined pair whose two
    /// parts decorate the same moment differently.</item>
    /// </list>
    /// </remarks>
    private static MusicItem MergeIntoChord(MusicItem first, MusicItem second)
    {
        var notes = ChordNotesOf(first).AddRange(ChordNotesOf(second))
            .OrderBy(n => n.StaffPosition)
            .ToImmutableArray();

        return first switch
        {
            NoteItem n => new ChordItem(notes, n.BaseDuration, n.Dots, n.SourcePosition,
                n.TremoloBeams, n.HasBeamStart, n.HasBeamEnd, false, n.IsCue,
                n.HasTieStart, n.HasSlurStart, n.HasSlurEnd)
            { BeamId = n.BeamId, StemUpOverride = null },
            ChordItem c => c with { Notes = notes },
            _ => first,
        };
    }

    private static ImmutableArray<ChordNoteInfo> ChordNotesOf(MusicItem item) => item switch
    {
        NoteItem n => [new ChordNoteInfo(n.StaffPosition, n.Accidental, n.NeedsLedgerLines, n.IsCourtesy)],
        ChordItem c => c.Notes,
        _ => [],
    };

    /// <summary>
    /// Records on the item WHICH of LilyPond's five contexts the routing put it in.
    /// </summary>
    /// <remarks>
    /// The stem direction <see cref="WithVoiceDirection"/> bakes is the only other thing the
    /// routing leaves on an item, and it is not enough to answer this: <c>shared</c> and
    /// <c>solo</c> both carry no voice settings, so they are indistinguishable by direction,
    /// and they are exactly the pair whose boundary horizontal spacing has to see (measured:
    /// scratch/lpreg/pctend.log, where the shared voice's wish at the half rest reaches
    /// straight past the whole solo run to the note it engraves next).
    /// </remarks>
    private static MusicItem InContext(MusicItem item, PartCombineVoiceId target) =>
        item with
        {
            VoiceContext = target switch
            {
                PartCombineVoiceId.One => VoiceContextId.CombineOne,
                PartCombineVoiceId.Two => VoiceContextId.CombineTwo,
                PartCombineVoiceId.Shared => VoiceContextId.CombineShared,
                PartCombineVoiceId.Solo => VoiceContextId.CombineSolo,
                // Null never reaches a slot — PlaceItems drops it before this is called.
                _ => VoiceContextId.Default,
            }
        };

    /// <summary>
    /// Bakes <c>\voiceOne</c>/<c>\voiceTwo</c> onto an item routed to part one's or part
    /// two's own voice — the same quantity
    /// <c>MeasureCollector.ResolveVoiceStemDirections</c> bakes for a <c>voice { } { }</c>
    /// span, and in the same slot, so every downstream reader sees one answer.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly:1668-1671 partCombine's make-directed-part-combine-music
    /// call — the "one" context is
    /// created <c>\with { \voiceOne … }</c> and "two" <c>\with { \voiceTwo … }</c>, while
    /// "shared" and "solo" get no settings at all and keep the pitch rule.
    /// </remarks>
    private static MusicItem WithVoiceDirection(MusicItem item, bool up) => item switch
    {
        NoteItem n when n.ForcedStemUp is null => n with { StemUpOverride = up },
        ChordItem c when c.ForcedStemUp is null => c with { StemUpOverride = up },
        RestItem { IsSpacer: false, IsMultiMeasure: false } r => r with { VoiceDirection = up ? 1 : -1 },
        _ => item,
    };

    /// <summary>Strips the direction off an item routed to the shared or solo voice: those
    /// contexts carry no voice settings, so the stem follows the pitch.</summary>
    /// <remarks>
    /// ⚠️ UNCONDITIONALLY, INCLUDING A BEAMED ITEM'S BAKED DIRECTION. <c>StemUpOverride</c>
    /// is the one slot this tree keeps two of LilyPond's quantities in — the voice-props
    /// direction AND the beam-resolved one — so a beamed note arrives here carrying the
    /// direction its beam had IN ITS OWN PART. That group is not this staff's group: the
    /// routing drops notes, merges others into chords, and MultiStaffLayouter re-runs
    /// BeamDetector over the voices that come out. Keeping the old value would hand the new
    /// beam a direction decided for a group that no longer exists.
    /// <para>
    /// This was written with a <c>BeamId is null</c> guard on the note arm and no guard on
    /// the chord arm — the same question answered two ways, with no reason given for either.
    /// The guard is the wrong half: LilyPond's shared and solo contexts set no direction at
    /// all, so the beam decides, and here the beam decides after this runs.
    /// </para>
    /// </remarks>
    private static MusicItem WithoutVoiceDirection(MusicItem item) => item switch
    {
        NoteItem { StemUpOverride: not null } n => n with { StemUpOverride = null },
        ChordItem { StemUpOverride: not null } c => c with { StemUpOverride = null },
        RestItem { VoiceDirection: not 0 } r => r with { VoiceDirection = 0 },
        _ => item,
    };

    private static ImmutableArray<Voice> BuildVoices(
        Voice one, Voice two,
        ImmutableArray<MusicItem>[] slot0, ImmutableArray<MusicItem>[] slot1,
        int measureCount)
    {
        var measures0 = ImmutableArray.CreateBuilder<Measure>(measureCount);
        var measures1 = ImmutableArray.CreateBuilder<Measure>(measureCount);
        bool anySecond = false;

        for (int m = 0; m < measureCount; m++)
        {
            // The measure's non-item properties (barlines, break permissions, source span)
            // come from part one where it has the measure and part two otherwise: they are
            // properties of the BAR, which both parts share.
            var template = m < one.Measures.Length ? one.Measures[m]
                : m < two.Measures.Length ? two.Measures[m]
                : null;
            if (template == null)
                continue;

            measures0.Add(template with { Items = slot0[m] });
            measures1.Add(template with { Items = slot1[m] });
            anySecond |= slot1[m].Length > 0;
        }

        var first = new Voice(one.Name, measures0.ToImmutable());
        if (!anySecond)
            return [first];
        return [first, new Voice(two.Name, measures1.ToImmutable())];
    }
}
