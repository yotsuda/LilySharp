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
        ScoreContent content, IReadOnlyList<CombinedStaffAddressing> addressings)
    {
        var byStaff = addressings.ToDictionary(a => a.StaffIndex);
        return content with
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
            MusicMarks = MoveMarks(content.MusicMarks, byStaff),
        };
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
    /// LILYSHARP-OWN: two answers here have no LilyPond measurement behind them. An END on a rest
    ///   engraved for nobody stays at its collected place (its bar and its moment are still
    ///   right; the item number names nothing, and the line then closes on that bar's end or
    ///   head). And a text spanner or an ottava whose part changes streams between its ends is
    ///   left as collected, both ends (the pairing pass in the body says why).
    ///   departs from: ly/engraver-init.ly:375 Text_spanner_engraver consisted into the Voice context — what LilyPond draws
    ///   for a spanner whose stop is heard in another context is not measured.
    ///   goes when: TextSpannerEngraver and OttavaBracketEngraver carry the end's own voice.
    ///   observed by: CombinedStaffHairpinTests (AMarkOnARestNobodyEngraves, ALinePairedInOneVoice_GoesAsAPair).
    /// </remarks>
    private static ImmutableArray<MusicMarkItem> MoveMarks(
        ImmutableArray<MusicMarkItem> marks, Dictionary<int, CombinedStaffAddressing> byStaff)
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
        // in the start's voice) go as a pair: both ends moved when they land in one voice; both
        // dropped when the start's note is engraved by nobody; and BOTH LEFT where they were
        // collected when the part changes streams between them — `@rit` in a bar the parts play
        // apart, `@!rit` in a bar part two plays alone. Moved one by one those two ends name two
        // voices and pair with nothing: no line at all, where the collected numbers still draw
        // one (Lab sessions/p1321/probe x1, x2).
        // ⚠️ That last answer is the picture before session 1321, not a right one: the ends are
        // then read under the part's own numbers. It goes when those engravers carry the end's
        // own voice, as a hairpin does (HairpinItem.EndVoiceIndex).
        var stays = new HashSet<int>();
        var dropped = new HashSet<int>();
        var open = new Dictionary<(int Staff, int Voice, int Line), int>();
        for (int i = 0; i < marks.Length; i++)
        {
            var m = marks[i];
            if (!IsANotesMark(m) || !byStaff.TryGetValue(m.StaffIndex, out var map) || PairedLine(m) is not { } line)
                continue;
            var key = (m.StaffIndex, m.VoiceIndex, line.Line);
            if (!line.IsEnd)
                open[key] = i;
            else if (open.Remove(key, out int s))
            {
                var start = Moved(marks[s], map);
                var end = Moved(m, map);
                if (start is null)
                {
                    dropped.Add(s);
                    dropped.Add(i);
                }
                else if (end is null || end.VoiceIndex != start.VoiceIndex)
                {
                    stays.Add(s);
                    stays.Add(i);
                }
            }
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
            if (dropped.Contains(i))
                continue;
            var placed = Moved(m, map) ?? (IsAnEnd(m.Type) ? m : null);
            if (placed is null
                || !seen.Add((placed.StaffIndex, placed.VoiceIndex, placed.MeasureIndex, placed.AnchorItemIndex,
                              placed.Type, placed.Text, placed.SpanFamily)))
                continue;
            built.Add(placed);
        }
        return built.ToImmutable();
    }

    /// <summary>For an end of a line that is paired within one voice — a text spanner of its
    /// family, an ottava bracket — which line and which end; null for every other mark.</summary>
    private static (int Line, bool IsEnd)? PairedLine(MusicMarkItem m) => m.Type switch
    {
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
