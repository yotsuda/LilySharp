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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// An address a collector stamped on an item: the voice slot, the measure and the position
/// in that measure. Every island that anchors an item to a note carries these three
/// (<c>DynamicItem</c>, <c>ArticulationItem</c>, <c>TupletBracketItem</c>,
/// <c>TrillSpannerItem</c>), which is why the translation below can be spelt once.
/// </summary>
public readonly record struct VoiceItemAddress(int VoiceIndex, int MeasureIndex, int ItemIndex);

/// <summary>
/// One <c>combinedStaff</c>'s answer to "the collector addressed this item to voice slot V,
/// measure M, item I — where is it now?".
/// </summary>
/// <remarks>
/// ⚠️ WHY A TRANSLATION AND NOT AN ARITHMETIC. A <c>condensedStaff</c> needs none: its staff
/// IS its parts' voices concatenated, so a part's slot is a base and every index survives
/// (<see cref="VoiceSlotting.AppendedToStaff"/>). The combiner does not concatenate — it
/// REWRITES. It moves items between the two streams it emits, merges two parts' notes into
/// one column, drops what it routes to <see cref="PartCombineVoiceId.Null"/>, and writes
/// spacer rests into the gaps, so a part's item index does not survive either. The only
/// thing that can answer is the routing itself, which is what
/// <see cref="PartCombineResult.ItemAddresses"/> reports.
/// <para>
/// THE COLLECTED ADDRESS IS IN CONCATENATION SPACE — the same space a condensed staff's
/// parts are collected in, and for the same reason: it is the one space where two parts on
/// one staff have different addresses at all, so the item can still be told whose it is
/// when it gets here. It is translated into the staff's real slots exactly once, after
/// <c>RenderSpec.ToStaffGroups</c> has built them.
/// </para>
/// </remarks>
/// <param name="StaffIndex">The global staff index this combined staff was built at — the
/// same counter the collector's binding loop keeps, since <c>GetVoiceBindings</c> and
/// <c>ToStaffGroups</c> walk one order (<c>OrderedItems</c>).</param>
/// <param name="FirstPartVoiceCount">How many voices part one contributed, which is where
/// part two's concatenation slots begin. The collector reaches the same number by the same
/// route — both read the voices of the part named first — so the two agree by construction
/// rather than by arrangement.</param>
/// <param name="CombinedVoiceCount">How many voices the combiner emitted (1 or 2). The
/// staff's remaining slots hold the parts' OTHER voices, untouched, in concatenation
/// order, which is what <c>ToStaffGroups</c> appends there.</param>
/// <param name="PartItems">Per part, where each of its first voice's sounding items went;
/// an item absent from it was engraved by nobody.</param>
public sealed record CombinedStaffAddressing(
    int StaffIndex,
    int FirstPartVoiceCount,
    int CombinedVoiceCount,
    ImmutableArray<ImmutableDictionary<(int Measure, int Item), CombinedItemAddress>> PartItems)
{
    /// <summary>
    /// Where the item collected at <paramref name="collected"/> is now, or null if the
    /// combiner engraved it with nobody.
    /// </summary>
    /// <remarks>
    /// Three kinds of slot arrive here and only one of them needs the routing:
    /// <list type="number">
    /// <item>slot 0 and slot <see cref="FirstPartVoiceCount"/> are the two streams the
    /// combiner consumed — the parts' FIRST voices — and only the routing knows where their
    /// items went.</item>
    /// <item>every other slot is one of the parts' remaining voices, which
    /// <c>ToStaffGroups</c> appends to the staff untouched. Its items do not move; only its
    /// slot does, from its place in the concatenation to its place after the combined
    /// voices. Concatenation slot <c>v</c> is the <c>v-1</c>-th appended voice when it
    /// belongs to part one and the <c>v-2</c>-th when it belongs to part two (part two's
    /// first voice was consumed, so one more slot drops out ahead of it).</item>
    /// <item>a degenerate <c>combinedStaff</c> naming ONE part has
    /// <see cref="FirstPartVoiceCount"/> and slot 0 name the same thing; the first arm wins
    /// and the second part's map is empty, which is what a part that is not there means.</item>
    /// </list>
    /// </remarks>
    public VoiceItemAddress? Translate(VoiceItemAddress collected)
    {
        int slot = collected.VoiceIndex;
        int part = slot == 0 ? 0 : slot == FirstPartVoiceCount ? 1 : -1;
        if (part < 0)
            return collected with
            {
                VoiceIndex = CombinedVoiceCount + slot - (slot < FirstPartVoiceCount ? 1 : 2),
            };
        if (part >= PartItems.Length
            || !PartItems[part].TryGetValue((collected.MeasureIndex, collected.ItemIndex), out var to))
            return null;
        return new VoiceItemAddress(to.VoiceIndex, to.MeasureIndex, to.ItemIndex);
    }

    /// <summary>
    /// Where a span that OPENS at <paramref name="start"/> and closes at
    /// <paramref name="end"/> is now — or null if its opening item is not engraved at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ A SPAN IS ENGRAVED IN THE VOICE IT OPENS IN, so the two ends cannot be translated
    /// independently: the combiner is free to route the rest of the passage into the other
    /// stream, and the closing item would then name a position in a voice the span is not
    /// in. LilyPond answers this the same way and for the same reason — its Tuplet_engraver
    /// and Trill_spanner_engraver live in ONE Voice context, so a bracket is bounded by the
    /// note columns THAT context saw, and the part's notes that moved to another context
    /// were seen by another engraver.
    /// LILYPOND-REF: lily/tuplet-engraver.cc:187-197 acknowledge_note_column — the bracket
    ///   takes the columns THIS engraver is acknowledged for, which are its own context's.
    /// LILYPOND-REF: ly/music-functions-init.ly:1643-1651 make-directed-part-combine-music —
    ///   the five contexts a part is moved between while that is happening.
    /// <para>
    /// So the span is CLIPPED: the end walks back through its own measure to the last item
    /// that also landed in the opening voice, and falls back to the opening item itself when
    /// none does. In a book where the passage never changes streams — which is every book
    /// that has ever been engraved with this — the first step already answers and nothing is
    /// clipped.
    /// </para>
    /// <para>
    /// ⚠️ THE WALK STAYS IN THE END'S OWN MEASURE, which is the whole story for a tuplet
    /// bracket (its two indices are two positions in ONE measure) and not the whole story
    /// for a trill spanner, whose stop may be measures away: a trill that changes streams
    /// somewhere in between clips to its opening note rather than to the last note before
    /// the change. Carrying it further needs the routing to be asked for the measures in
    /// between, which needs a measure count this does not have — and there is no book to
    /// read it on, so it is written down rather than guessed at.
    /// </para>
    /// </remarks>
    public (VoiceItemAddress Start, VoiceItemAddress End)? TranslateSpan(
        VoiceItemAddress start, VoiceItemAddress end)
    {
        if (Translate(start) is not { } movedStart)
            return null;
        int lowest = end.MeasureIndex == start.MeasureIndex ? start.ItemIndex + 1 : 0;
        for (int i = end.ItemIndex; i >= lowest; i--)
            if (Translate(end with { ItemIndex = i }) is { } movedEnd
                && movedEnd.VoiceIndex == movedStart.VoiceIndex)
                return (movedStart, movedEnd);
        return (movedStart, movedStart);
    }
}

/// <summary>
/// Applies the combined staves' translations to the collected annotation lists.
/// </summary>
/// <remarks>
/// ⚠️ FOUR ITEM TYPES, SEVEN SPELLINGS. The islands a collector addresses by voice are
/// tuplet brackets, dynamics, articulations, trill spanners, fingerings, frames and bends —
/// but the last three ride <see cref="ArticulationItem"/> (as <c>Pluck</c>, <c>FretFrame</c>
/// and <c>Bend</c>), and free text (<c>@text</c>) rides <see cref="DynamicItem"/>. Counting
/// the SINKS rather than the spellings is what makes this list closeable: it is complete
/// exactly when every model type carrying a collected <c>VoiceIndex</c> appears below, which
/// is a question the compiler can be asked (a grep for <c>VoiceIndex</c> over
/// <c>Svg/Model</c> answers it — <c>TieItem</c>, <c>SlurItem</c>, <c>BeamGroup</c> and
/// <c>GlissandoItem</c> also hold one, and none of them is collected: layout stamps those
/// while walking the staff's OWN voices, so their numbers were never in a part's terms).
/// <para>
/// ⚠️ A FIFTH, WHICH THAT GREP DID NOT COUNT UNTIL SESSION 1321: <see cref="MusicMarkItem"/>.
/// Most marks are a bar's, but the two ends of a hairpin, a text spanner, an ottava bracket
/// and a pedal are stamped with the voice and the item of the note they are written on
/// (<c>AnchorItemIndex</c>, not <c>ItemIndex</c> — the name the grep missed). They move in
/// <see cref="MoveMarks"/>.
/// </para>
/// </remarks>
internal static class CombinedStaffReaddress
{
    /// <summary>Re-addresses every island of every combined staff in one pass.</summary>
    internal static ScoreContent Apply(
        ScoreContent content, IReadOnlyList<CombinedStaffAddressing> addressings,
        Func<int, int, ImmutableArray<Measure>>? measuresOf = null,
        List<MusicMarkItem>? unengravedHairpinMarks = null)
    {
        var byStaff = addressings.ToDictionary(a => a.StaffIndex);
        var moved = content with
        {
            Dynamics = MoveEach(content.Dynamics, byStaff,
                d => new VoiceItemAddress(d.VoiceIndex, d.MeasureIndex, d.ItemIndex),
                d => d.StaffIndex,
                (d, to) => d with
                {
                    VoiceIndex = to.VoiceIndex,
                    MeasureIndex = to.MeasureIndex,
                    ItemIndex = to.ItemIndex,
                },
                d => (d.StaffIndex, d.VoiceIndex, d.MeasureIndex, d.ItemIndex,
                      d.Level, d.Text, d.IsAbove, d.IsExpressiveText)),
            Articulations = MoveEach(content.Articulations, byStaff,
                a => new VoiceItemAddress(a.VoiceIndex, a.MeasureIndex, a.ItemIndex),
                a => a.StaffIndex,
                (a, to) => a with
                {
                    VoiceIndex = to.VoiceIndex,
                    MeasureIndex = to.MeasureIndex,
                    ItemIndex = to.ItemIndex,
                    // A group diagram's last item moves with its group — kept only where it
                    // lands in the same bar and voice as the diagram's note.
                    GroupLastItemIndex = a.GroupLastItemIndex < 0 ? -1
                        : byStaff[a.StaffIndex].Translate(new VoiceItemAddress(a.VoiceIndex, a.MeasureIndex, a.GroupLastItemIndex))
                            is { } end && end.VoiceIndex == to.VoiceIndex && end.MeasureIndex == to.MeasureIndex
                            ? end.ItemIndex : -1,
                },
                // A chord member's own mark (a fingering, a string) belongs to ITS note —
                // two of them at one column are two notes' marks, never a duplicate.
                a => a.IsChordMember
                    ? null
                    : (a.StaffIndex, a.VoiceIndex, a.MeasureIndex, a.ItemIndex, a.Type,
                       a.IsAbove, a.DirectionForced, a.BendSemitones, a.BendRelease, a.FrameSpec, a.PluckLetter)),
            TupletBrackets = MoveEachSpan(content.TupletBrackets, byStaff,
                t => (new VoiceItemAddress(t.VoiceIndex, t.MeasureIndex, t.StartNoteIndex),
                      new VoiceItemAddress(t.VoiceIndex, t.MeasureIndex, t.EndNoteIndex)),
                t => t.StaffIndex,
                (t, from, to) => t with
                {
                    VoiceIndex = from.VoiceIndex,
                    MeasureIndex = from.MeasureIndex,
                    StartNoteIndex = from.ItemIndex,
                    EndNoteIndex = to.ItemIndex,
                }),
            TrillSpanners = MoveEachSpan(content.TrillSpanners, byStaff,
                s => (new VoiceItemAddress(s.VoiceIndex, s.StartMeasureIndex, s.StartItemIndex),
                      new VoiceItemAddress(s.VoiceIndex, s.EndMeasureIndex, s.EndItemIndex)),
                s => s.StaffIndex,
                (s, from, to) => s with
                {
                    VoiceIndex = from.VoiceIndex,
                    StartMeasureIndex = from.MeasureIndex,
                    StartItemIndex = from.ItemIndex,
                    EndMeasureIndex = to.MeasureIndex,
                    EndItemIndex = to.ItemIndex,
                }),
            MusicMarks = MoveMarks(content.MusicMarks, byStaff, measuresOf, unengravedHairpinMarks, content.Dynamics),
        };
        return measuresOf == null ? moved : SideByContext(moved, byStaff, measuresOf);
    }

    /// <summary>
    /// The side a combined staff's dynamics and hairpins stand on where nothing is written: part
    /// one's own voice above the staff, part two's below, the shared and solo voices the staff's.
    /// Until session 1334 part one's levels and hairpins stood below with part two's, one under
    /// the other, where its notes were apart.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly:1668-1670 partCombine → make-directed-part-combine-music — the context settings
    ///   of "one" are <c>\voiceOne \override DynamicLineSpanner.direction = #UP</c>, of "two"
    ///   <c>\voiceTwo \override DynamicLineSpanner.direction = #DOWN</c>, and "shared" has none
    ///   (DynamicLineSpanner is not in scm/music-functions.scm:617-634 direction-polyphonic-grobs,
    ///   so <c>\voiceOne</c> alone leaves it below — a <c>voice { } { }</c> span's levels stay below).
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1333/h13/apart.lys, lp-apart.cropped.png): part one's
    ///   p, crescendo and f above the staff, part two's below.
    /// The voice's side is the DEFAULT a line takes with nothing written, as the staff's is
    /// elsewhere: the lines are settled again here, in the staff's own voices
    /// (<see cref="DynamicLineSides.Resolve"/>), so a side written on a mark still turns the line
    /// it opens — <c>e''4@p.down f''@cresc … a''@f</c> in part one stands below, all three.
    /// MEASURED (LilyPond 2.26.0): an explicit <c>_\p</c> in voice "one" turns its line down
    ///   (lily/dynamic-align-engraver.cc:119-160 Dynamic_align_engraver::acknowledge_dynamic, set_grob_direction (line_, d)).
    /// </remarks>
    private static ScoreContent SideByContext(
        ScoreContent content, Dictionary<int, CombinedStaffAddressing> byStaff,
        Func<int, int, ImmutableArray<Measure>> measuresOf)
    {
        bool? SideAt(int staff, int voice, int measure, int item)
        {
            if (!byStaff.ContainsKey(staff))
                return null;
            var measures = measuresOf(staff, voice);
            if (measures.IsDefault || measure < 0 || measure >= measures.Length)
                return null;
            var items = measures[measure].Items;
            if (item < 0 || item >= items.Length)
                return null;
            return items[item].VoiceContext switch
            {
                VoiceContextId.CombineOne => true,
                VoiceContextId.CombineTwo => false,
                _ => null,
            };
        }
        bool any = false;
        var dynamics = content.Dynamics.Select(d =>
        {
            if (d.WrittenAbove == null && !d.IsExpressiveText
                && SideAt(d.StaffIndex, d.VoiceIndex, d.MeasureIndex, d.ItemIndex) is { } above)
            {
                any = true;
                return above != d.IsAbove ? d with { IsAbove = above } : d;
            }
            return d;
        }).ToImmutableArray();
        var marks = content.MusicMarks.Select(m =>
        {
            if (m.Type is MusicMarkType.Cresc or MusicMarkType.Decresc or MusicMarkType.Dim
                && m.HairpinWritten == null
                && SideAt(m.StaffIndex, m.VoiceIndex, m.MeasureIndex, m.AnchorItemIndex) is { } above)
            {
                any = true;
                return above != m.HairpinAbove ? m with { HairpinAbove = above } : m;
            }
            return m;
        }).ToImmutableArray();
        if (!any)
            return content;
        // The lines again, now in the staff's voices: a written side turns its line, the rest take
        // the default each holds.
        var (resolvedDynamics, resolvedMarks) = DynamicLineSides.Resolve(dynamics, marks);
        return content with { Dynamics = resolvedDynamics, MusicMarks = resolvedMarks };
    }

    /// <summary>The marks a note of a voice carries: the two ends of a hairpin, of a text
    /// spanner, of an ottava bracket and of a pedal. Every other mark is its bar's or its
    /// staff's and holds no voice's item number.</summary>
    private static bool IsANotesMark(MusicMarkItem m) => m.AnchorItemIndex >= 0 && m.Type is
        MusicMarkType.Cresc or MusicMarkType.Decresc or MusicMarkType.Dim or MusicMarkType.HairpinStop
        or MusicMarkType.TextSpanStart or MusicMarkType.TextSpanStop
        or MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
        or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown or MusicMarkType.OttavaStop
        or MusicMarkType.SustainOn or MusicMarkType.SustainOff
        or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
        or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    /// <summary>
    /// The marks, with a combined staff's note-carried ones (<see cref="IsANotesMark"/>) moved
    /// to where their note went.
    /// </summary>
    /// <remarks>
    /// Until session 1321 no mark was moved: a hairpin kept the part's own slot and item number,
    /// which on the staff name another voice's item or none. Part two's `@p … @cresc … @f` in a
    /// bar it plays alone began under the p at the bar's head — the bar is in the staff's FIRST
    /// voice, where the levels had been moved to, and the hairpin read the second, a spacer; a
    /// part entering mid-bar (`r2 g'4@cresc g'@f` after the other's `c''2 r2`) began its hairpin
    /// on the other part's note; and a hairpin both parts write in a unison was drawn twice, one
    /// under the other (Lab sessions/p1321/probe h2, h5, h4).
    /// <para>
    /// THREE ANSWERS, by what the routing says of the mark's item:
    /// its note is engraved — the mark goes with it; the mark stands PAST an item (an ottava's
    /// end includes its note, a line's end on a trailing empty chord) — it goes past where that
    /// item went; neither — a START is dropped with its note, as a level is
    /// (<see cref="MoveEach{T}"/>), and an END stays where it was collected, since dropping it
    /// would run the line on to the next end. The moment a mark carries
    /// (<see cref="MusicMarkItem.AnchorTiming"/>) is the bar's and does not move.
    /// </para>
    /// <para>
    /// The same mark landing twice on one item — both parts' `@cresc` on a column the combiner
    /// merged — is one mark, as two identical events in one LilyPond Voice are one grob; the
    /// first is kept. Two DIFFERENT marks there both stay.
    /// </para>
    /// <para>
    /// WHERE THIS COMES FROM. That a mark travels with its note is LilyPond's combiner, which
    /// moves a part's music — its span events with it — between the contexts it routes it to:
    /// the address is the one <see cref="CombinedStaffAddressing.TranslateSpan"/> cites, and it
    /// is not written a second time here. It is not a literal port because LilyPond has no
    /// address to translate: its events ARE in the context, where Lily# collects first and
    /// routes afterwards.
    /// </para>
    /// LILYSHARP-OWN: three answers here are not LilyPond's. An END on a
    ///   rest engraved for nobody — the part's own rest under the other's solo, `r2 r2@!cresc` —
    ///   goes to the item the staff engraves AT ITS MOMENT (<see cref="AtItsMoment"/>): the line
    ///   stops on the beat it was written on. MEASURED (LilyPond 2.26.0, Lab sessions/p1324/lp
    ///   n2, n7): LilyPond loses that `\!` with the rest — "unterminated crescendo",
    ///   "unterminated text spanner" — and draws no hairpin and no line at all; what the writer
    ///   wrote is drawn here instead. Until session 1323 it stayed under the part's own
    ///   numbers, which name nothing on the staff, and the line ran to the bar's end (a pedal
    ///   bracket closed on the bar's head). Where the staff engraves nothing at that moment (the
    ///   other part holds a long note through it) it stays as collected still. A text spanner or
    ///   an ottava whose part changes streams between its ends is one line, its end paired in
    ///   its start's voice (<see cref="MusicMarkItem.LineVoice"/>, which carries the departure's
    ///   address) — and so is a hairpin, whose stop is its part's and not whichever voice's
    ///   comes first. And a hairpin's stop whose START was dropped with its note (the second
    ///   part's in a unison) is dropped with it: left behind it stood in a voice of its own and
    ///   ended that voice's earlier hairpin at its bar's end, across the unison's hairpin (Lab
    ///   sessions/p1323/probe s2, s3).
    ///   observed by: CombinedStaffHairpinTests (AMarkOnARestNobodyEngraves, ALinePairedInOneVoice_GoesAsAPair, AnEndOnARestNobodyEngraves_StopsOnItsBeat, AUnisonsSecondStop_GoesWithItsStart).
    /// </remarks>
    private static ImmutableArray<MusicMarkItem> MoveMarks(
        ImmutableArray<MusicMarkItem> marks, Dictionary<int, CombinedStaffAddressing> byStaff,
        Func<int, int, ImmutableArray<Measure>>? measuresOf, List<MusicMarkItem>? unengravedHairpinMarks = null,
        ImmutableArray<DynamicItem> dynamics = default)
    {
        if (marks.IsDefaultOrEmpty || !marks.Any(m => IsANotesMark(m) && byStaff.ContainsKey(m.StaffIndex)))
            return marks;
        // Where each mark goes by itself (null: its note is engraved by nobody).
        MusicMarkItem? Moved(MusicMarkItem m, CombinedStaffAddressing map)
        {
            var at = new VoiceItemAddress(m.VoiceIndex, m.MeasureIndex, m.AnchorItemIndex);
            if (map.Translate(at) is { } to)
                return m with { VoiceIndex = to.VoiceIndex, MeasureIndex = to.MeasureIndex, AnchorItemIndex = to.ItemIndex };
            if (m.AnchorItemIndex > 0 && map.Translate(at with { ItemIndex = m.AnchorItemIndex - 1 }) is { } before)
                return m with { VoiceIndex = before.VoiceIndex, MeasureIndex = before.MeasureIndex, AnchorItemIndex = before.ItemIndex + 1 };
            return null;
        }

        // The lines whose two ends are paired IN ONE VOICE (TextSpannerEngraver and
        // OttavaBracketEngraver key their open span by staff and voice, and read the end's item
        // in the start's voice unless told otherwise) go as a pair: both ends moved; both dropped
        // when the start's note is engraved by nobody; and where the part CHANGES STREAMS
        // between them — `@rit` in a bar the parts play apart, `@!rit` in a bar part two plays
        // alone — the end is told its start's voice (MusicMarkItem.LineVoice), so the two pair
        // though they stand in two voices. Moved one by one with no such word they paired with
        // nothing and no line was drawn (session 1321's first commit; Lab sessions/p1321/probe
        // x1, x2), and from that session's second commit to session 1322 both were left under
        // the part's own numbers instead.
        // An end on a rest engraved for nobody goes to what the staff engraves at its moment
        // (AtItsMoment). ⚠️ Still left as collected, both ends: a
        // line whose end the staff engraves nothing under (the line closes on that bar's edge).
        // A HAIRPIN is paired the same way for its stop's sake — a part's `@!cresc` ends that
        // part's hairpin — but a hairpin's start goes by itself (a level or the next hairpin
        // ends one too), so only the stop is touched: told its start's voice, sent to its
        // moment, or dropped with a start nobody engraves.
        var stays = new HashSet<int>();
        var dropped = new HashSet<int>();
        var lineVoice = new Dictionary<int, int>();
        var byMoment = new Dictionary<int, MusicMarkItem>();
        var meets = new Dictionary<int, int>();
        var open = new Dictionary<(int Staff, int Voice, int Line), int>();
        // The other part's hairpin start at the moment of `m`, whose own note nobody engraves
        // (-1: none) — the mark a unison keeps.
        int TwinOf(MusicMarkItem m, CombinedStaffAddressing map)
        {
            if (AtItsMoment(m, map, measuresOf) is not { } there)
                return -1;
            for (int j = 0; j < marks.Length; j++)
                if (PairedLine(marks[j]) is (HairpinLine, false) && marks[j].StaffIndex == m.StaffIndex && !ReferenceEquals(marks[j], m)
                    && IsANotesMark(marks[j]) && Moved(marks[j], map) is { } kept
                    && kept.VoiceIndex == there.VoiceIndex && kept.MeasureIndex == there.MeasureIndex
                    && kept.AnchorItemIndex == there.AnchorItemIndex)
                    return j;
            return -1;
        }
        // Whether the part that wrote the hairpin start marks[j] writes anything after it that
        // ends a hairpin — a level, a hairpin's start or a stop — in its own terms (the marks and
        // the levels are both still in their part's voice slot and item numbers here).
        bool EndsInItsPart(int j)
        {
            var start = marks[j];
            bool After(int measure, int item)
                => measure > start.MeasureIndex || (measure == start.MeasureIndex && item > start.AnchorItemIndex);
            for (int k = 0; k < marks.Length; k++)
                if (k != j && marks[k].StaffIndex == start.StaffIndex && marks[k].VoiceIndex == start.VoiceIndex
                    && PairedLine(marks[k]) is (HairpinLine, _) && After(marks[k].MeasureIndex, marks[k].AnchorItemIndex))
                    return true;
            if (!dynamics.IsDefaultOrEmpty)
                foreach (var d in dynamics)
                    if (d.StaffIndex == start.StaffIndex && d.VoiceIndex == start.VoiceIndex && !d.IsExpressiveText
                        && After(d.MeasureIndex, d.ItemIndex))
                        return true;
            return false;
        }
        for (int i = 0; i < marks.Length; i++)
        {
            var m = marks[i];
            if (!IsANotesMark(m) || !byStaff.TryGetValue(m.StaffIndex, out var map) || PairedLine(m) is not { } line)
                continue;
            var key = (m.StaffIndex, m.VoiceIndex, line.Line);
            if (!line.IsEnd)
            {
                // A part's next hairpin while its last is open ends that one, tip to tip —
                // also where the part is engraved in another voice by now, or where this start
                // is dropped for the other part's same mark on the merged column, which then
                // stands for it (MusicMarkItem.MeetsVoice).
                // …and where that last one was a unison's, dropped for the other part's mark on
                // the merged column, the mark kept for it is the one this follows — unless the
                // other part ends that hairpin itself (EndsInItsPart): then it runs on to that
                // end, two hairpins over each other as before (owner's decision 2026-11-19).
                // Since session 1334 the parts are apart wherever their open hairpins differ
                // (PartDynamicEvents, owner's decision 2026-11-20 "match LilyPond"), so a unison
                // reaches here only with both parts' hairpin the same, up to where they part.
                if (line.Line == HairpinLine && open.TryGetValue(key, out int before)
                    && (Moved(marks[before], map)
                        ?? (TwinOf(marks[before], map) is var kept and >= 0 && !EndsInItsPart(kept) ? Moved(marks[kept], map) : null))
                        is { } earlier)
                {
                    var here = Moved(m, map);
                    int at = here != null ? i : TwinOf(m, map);
                    if (at >= 0)
                        meets[at] = earlier.VoiceIndex;
                }
                open[key] = i;
            }
            else if (open.Remove(key, out int s))
            {
                var start = Moved(marks[s], map);
                var end = Moved(m, map);
                if (start != null && end is null && AtItsMoment(m, map, measuresOf) is { } there)
                    byMoment[i] = end = there;
                // …and where nothing is engraved at that moment, a hairpin's stop goes inside the
                // note sounding through it, keeping its moment (owner's decision 2026-11-19).
                else if (start != null && end is null && line.Line == HairpinLine && Inside(m, map, measuresOf) is { } inside)
                    byMoment[i] = end = inside;
                if (start is null)
                {
                    dropped.Add(s);
                    dropped.Add(i);
                }
                else if (end is null)
                {
                    if (line.Line != HairpinLine)
                    {
                        stays.Add(s);
                        stays.Add(i);
                    }
                    // (A hairpin's stop left where it was collected is its start's still.)
                    else if (m.VoiceIndex != start.VoiceIndex)
                        lineVoice[i] = start.VoiceIndex;
                }
                else if (end.VoiceIndex != start.VoiceIndex)
                    lineVoice[i] = start.VoiceIndex;
            }
        }

        void NotEngraved(MusicMarkItem m)
        {
            if (PairedLine(m) is (HairpinLine, _))
                unengravedHairpinMarks?.Add(m);
        }
        var built = ImmutableArray.CreateBuilder<MusicMarkItem>(marks.Length);
        var seen = new HashSet<(int, int, int, int, MusicMarkType, string, TextSpanFamily)>();
        for (int i = 0; i < marks.Length; i++)
        {
            var m = marks[i];
            if (!IsANotesMark(m) || !byStaff.TryGetValue(m.StaffIndex, out var map) || stays.Contains(i))
            {
                built.Add(m);
                continue;
            }
            // (A hairpin's end the page drops is still its part's: a document that writes each
            // part alone reads it from here — MeasureCollector.CombinedStaffUnengravedHairpinMarks.)
            if (dropped.Contains(i))
            {
                NotEngraved(m);
                continue;
            }
            // (A pedal's release is paired with nothing here: it goes to its moment by itself.)
            var placed = byMoment.GetValueOrDefault(i) ?? Moved(m, map)
                ?? (!IsAnEnd(m.Type) ? null
                    : PairedLine(m) is null && AtItsMoment(m, map, measuresOf) is { } there ? there : m);
            if (placed != null && lineVoice.TryGetValue(i, out int paired))
                placed = placed with { LineVoice = paired };
            if (placed != null && meets.TryGetValue(i, out int met))
                placed = placed with { MeetsVoice = met };
            if (placed is null
                || !seen.Add((placed.StaffIndex, placed.VoiceIndex, placed.MeasureIndex, placed.AnchorItemIndex,
                              placed.Type, placed.Text, placed.SpanFamily)))
            {
                NotEngraved(m);
                continue;
            }
            built.Add(placed);
        }
        return built.ToImmutable();
    }

    /// <summary>
    /// The END <paramref name="m"/>, whose own item nobody engraves, on the item the combined
    /// voices engrave at its moment — the first combined voice that has one (a rest is engraved
    /// for nobody under the other part's solo, which is one stream). Null when no voice
    /// engraves anything there, or the mark carries no moment.
    /// </summary>
    /// <remarks>
    /// Only an item that IS something at that moment answers: a spacer the combiner wrote into
    /// a gap is not a column of its own, and a grace note or a signature standing at the same
    /// moment takes no time — the item of the moment that does is the note.
    /// </remarks>
    private static MusicMarkItem? AtItsMoment(MusicMarkItem m, CombinedStaffAddressing map,
        Func<int, int, ImmutableArray<Measure>>? measuresOf)
    {
        if (measuresOf is null || m.AnchorTiming.Denominator == 0)
            return null;
        int? ItemAt(int voice)
        {
            var measures = measuresOf(m.StaffIndex, voice);
            if (measures.IsDefaultOrEmpty || m.MeasureIndex < 0 || m.MeasureIndex >= measures.Length)
                return null;
            var items = measures[m.MeasureIndex].Items;
            var at = Fraction.Zero;
            for (int i = 0; i < items.Length && at <= m.AnchorTiming; i++)
            {
                if (at == m.AnchorTiming && items[i].Duration > Fraction.Zero && items[i] is not RestItem { IsSpacer: true })
                    return i;
                at += items[i].Duration;
            }
            return null;
        }

        for (int v = 0; v < map.CombinedVoiceCount; v++)
            if (ItemAt(v) is { } item)
                return m with { VoiceIndex = v, AnchorItemIndex = item };
        return null;
    }

    /// <summary>
    /// The END <paramref name="m"/>, whose own item nobody engraves and at whose moment no item
    /// is engraved either, on the item the combined voices engrave SOUNDING THROUGH that moment
    /// — a long note the other part holds — with its moment kept
    /// (<see cref="MusicMarkItem.WithinItem"/>). Null when nothing sounds there.
    /// </summary>
    private static MusicMarkItem? Inside(MusicMarkItem m, CombinedStaffAddressing map,
        Func<int, int, ImmutableArray<Measure>>? measuresOf)
    {
        if (measuresOf is null || m.AnchorTiming.Denominator == 0)
            return null;
        for (int v = 0; v < map.CombinedVoiceCount; v++)
        {
            var measures = measuresOf(m.StaffIndex, v);
            if (measures.IsDefaultOrEmpty || m.MeasureIndex < 0 || m.MeasureIndex >= measures.Length)
                continue;
            var items = measures[m.MeasureIndex].Items;
            var at = Fraction.Zero;
            for (int i = 0; i < items.Length && at < m.AnchorTiming; i++)
            {
                var next = at + items[i].Duration;
                // (No observer for the spacer: none sounds through a moment in a book written so
                // far — Lab sessions/p1327/poisons2.ps1 V6.)
                if (next > m.AnchorTiming && items[i] is not RestItem { IsSpacer: true })
                    return m with { VoiceIndex = v, AnchorItemIndex = i, WithinItem = true };
                at = next;
            }
        }
        return null;
    }

    /// <summary><see cref="PairedLine"/>'s name for a hairpin (a text spanner's is its family,
    /// an ottava's -1).</summary>
    private const int HairpinLine = -2;

    /// <summary>For an end of a line that is paired within one voice — a text spanner of its
    /// family, an ottava bracket, a hairpin and its stop — which line and which end; null for
    /// every other mark.</summary>
    private static (int Line, bool IsEnd)? PairedLine(MusicMarkItem m) => m.Type switch
    {
        MusicMarkType.Cresc or MusicMarkType.Decresc or MusicMarkType.Dim => (HairpinLine, false),
        MusicMarkType.HairpinStop => (HairpinLine, true),
        MusicMarkType.TextSpanStart => ((int)m.SpanFamily, false),
        MusicMarkType.TextSpanStop => ((int)m.SpanFamily, true),
        MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
            or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown => (-1, false),
        MusicMarkType.OttavaStop => (-1, true),
        _ => null,
    };

    private static bool IsAnEnd(MusicMarkType type) => type is MusicMarkType.HairpinStop
        or MusicMarkType.TextSpanStop or MusicMarkType.OttavaStop or MusicMarkType.SustainOff
        or MusicMarkType.SostenutoOff or MusicMarkType.UnaCordaOff;

    /// <summary>
    /// One list of single-anchor items, with the ones on a combined staff moved to where
    /// their note went and the ones whose note is engraved by nobody dropped.
    /// </summary>
    /// <param name="sameMark">What makes two moved items the same mark (null: never a
    /// duplicate). Where the combiner merges both parts' notes into one chord column
    /// (<c>PartCombineConfig.Chords</c>), both parts' marks land on that column — and an
    /// `@f` or a fermata written in both parts is ONE event there, as two identical events
    /// in one LilyPond Voice are one grob. Only the first of a set is kept; a mark the two
    /// parts write DIFFERENTLY is not a duplicate and both stay.</param>
    private static ImmutableArray<T> MoveEach<T>(
        ImmutableArray<T> items,
        Dictionary<int, CombinedStaffAddressing> byStaff,
        Func<T, VoiceItemAddress> addressOf,
        Func<T, int> staffOf,
        Func<T, VoiceItemAddress, T> moved,
        Func<T, object?> sameMark)
    {
        if (items.IsDefaultOrEmpty || !items.Any(i => byStaff.ContainsKey(staffOf(i))))
            return items;
        var built = ImmutableArray.CreateBuilder<T>(items.Length);
        var seen = new HashSet<object>();
        foreach (var item in items)
        {
            if (!byStaff.TryGetValue(staffOf(item), out var map))
                built.Add(item);
            else if (map.Translate(addressOf(item)) is { } to)
            {
                var placed = moved(item, to);
                if (sameMark(placed) is { } key && !seen.Add(key))
                    continue;
                built.Add(placed);
            }
        }
        return built.ToImmutable();
    }

    /// <summary>The same for the two items whose address is a span (see
    /// <see cref="CombinedStaffAddressing.TranslateSpan"/>).</summary>
    private static ImmutableArray<T> MoveEachSpan<T>(
        ImmutableArray<T> items,
        Dictionary<int, CombinedStaffAddressing> byStaff,
        Func<T, (VoiceItemAddress Start, VoiceItemAddress End)> addressOf,
        Func<T, int> staffOf,
        Func<T, VoiceItemAddress, VoiceItemAddress, T> moved)
    {
        if (items.IsDefaultOrEmpty || !items.Any(i => byStaff.ContainsKey(staffOf(i))))
            return items;
        var built = ImmutableArray.CreateBuilder<T>(items.Length);
        foreach (var item in items)
        {
            if (!byStaff.TryGetValue(staffOf(item), out var map))
                built.Add(item);
            else
            {
                var (start, end) = addressOf(item);
                if (map.TranslateSpan(start, end) is { } to)
                    built.Add(moved(item, to.Start, to.End));
            }
        }
        return built.ToImmutable();
    }
}
