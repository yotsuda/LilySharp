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
                }),
            Articulations = MoveEach(content.Articulations, byStaff,
                a => new VoiceItemAddress(a.VoiceIndex, a.MeasureIndex, a.ItemIndex),
                a => a.StaffIndex,
                (a, to) => a with
                {
                    VoiceIndex = to.VoiceIndex,
                    MeasureIndex = to.MeasureIndex,
                    ItemIndex = to.ItemIndex,
                }),
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
        };
    }

    /// <summary>
    /// One list of single-anchor items, with the ones on a combined staff moved to where
    /// their note went and the ones whose note is engraved by nobody dropped.
    /// </summary>
    private static ImmutableArray<T> MoveEach<T>(
        ImmutableArray<T> items,
        Dictionary<int, CombinedStaffAddressing> byStaff,
        Func<T, VoiceItemAddress> addressOf,
        Func<T, int> staffOf,
        Func<T, VoiceItemAddress, T> moved)
    {
        if (items.IsDefaultOrEmpty || !items.Any(i => byStaff.ContainsKey(staffOf(i))))
            return items;
        var built = ImmutableArray.CreateBuilder<T>(items.Length);
        foreach (var item in items)
        {
            if (!byStaff.TryGetValue(staffOf(item), out var map))
                built.Add(item);
            else if (map.Translate(addressOf(item)) is { } to)
                built.Add(moved(item, to));
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
