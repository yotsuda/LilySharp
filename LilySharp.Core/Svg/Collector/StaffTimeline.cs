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
/// ONE measure of a staff in moment order: every item of every voice — notes, chords, rests,
/// grace-time items and the zero-length directives (clef, key, meter changes) — in the order
/// a staff-level engraver meets them. The one spelling of "the staff's items in time order"
/// that the staff-level readers consume (docs/moment-walk-design.md, stage ②).
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/global-context.cc:174-203 Global_context::iterate — one moment at a
/// time, every iterator processed up to it, then <c>OneTimeStep</c>;
/// lily/simultaneous-music-iterator.cc:97-115 Simultaneous_music_iterator::process — the
/// children (the voices) whose pending moment IS the current one are each advanced to it, in
/// their own order. A grace stands in the grace part of the moment (lily/moment.cc), before
/// the main part's items at the same main moment.
/// </para>
/// <para>
/// The order is a k-way MERGE of the voices' item sequences — each voice is consumed in the
/// order it was written, and at each step the voice whose next item has the smallest
/// (<see cref="Entry.Onset"/>, <see cref="Entry.GraceRank"/>) comes first, lower voice index
/// first on a tie. A merge rather than a sort, because a voice's sequence is not quite
/// monotone in that key: a clef written just before a grace stands at the grace's onset with
/// the MAIN rank, and sorting would carry the grace in front of the clef it was written
/// after. The merge keeps every voice's written order and still puts a grace of one voice
/// before another voice's main-time item at the same onset, which is the one cross-voice
/// relation the readers ask (<c>StaffAccidentals.Earlier</c>).
/// </para>
/// <para>
/// Until session 969 each staff-level reader built this order for itself:
/// <see cref="VoiceCollector.CollectMeasure"/> grouped the sounding items by onset for the
/// collision table and the staff's accidental columns, and <see cref="StaffAccidentals"/>
/// walked the voices again for its heads. Both read this now; the filters that differ between
/// them (the collision table wants neither rests nor grace time nor directives) are theirs,
/// stated at the reader, not here.
/// </para>
/// </remarks>
internal static class StaffTimeline
{
    /// <summary>One item of one voice at its moment in the measure.</summary>
    /// <param name="VoiceIndex">0-based index into the staff's voices.</param>
    /// <param name="ItemIndex">Index into that voice's measure items.</param>
    /// <param name="Item">The item.</param>
    /// <param name="Onset">Where it starts, as measure time from the bar line. A grace-time
    /// item shares the onset of the main-time item it leads (<see cref="MusicItem.Duration"/>
    /// is zero in grace time), and so does a zero-length directive.</param>
    /// <param name="GraceRank">0 in grace time, 1 in main time: at one onset the grace part
    /// comes first.</param>
    internal readonly record struct Entry(int VoiceIndex, int ItemIndex, MusicItem Item, Fraction Onset, int GraceRank)
    {
        /// <summary>Main-time rank — the value <see cref="GraceRank"/> takes off grace time.</summary>
        public const int MainRank = 1;

        /// <summary>Grace-time rank.</summary>
        public const int GraceTimeRank = 0;
    }

    /// <summary>
    /// The items of measure <paramref name="measureIndex"/> across every voice of
    /// <paramref name="voices"/>, in moment order (see the remarks). A voice shorter than the
    /// measure index contributes nothing; a measure with no items gives an empty array.
    /// </summary>
    public static ImmutableArray<Entry> OfMeasure(ImmutableArray<Voice> voices, int measureIndex)
    {
        if (voices.IsDefaultOrEmpty)
            return ImmutableArray<Entry>.Empty;

        // Each voice's items with their onsets, in written order.
        var perVoice = new List<Entry>?[voices.Length];
        int total = 0;
        for (int vi = 0; vi < voices.Length; vi++)
        {
            var measures = voices[vi].Measures;
            if (measureIndex >= measures.Length)
                continue;
            var items = measures[measureIndex].Items;
            if (items.Length == 0)
                continue;
            var list = new List<Entry>(items.Length);
            var onset = Fraction.Zero;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                list.Add(new Entry(vi, ii, item, onset,
                    item.GraceTime ? Entry.GraceTimeRank : Entry.MainRank));
                onset += item.Duration;
            }
            perVoice[vi] = list;
            total += list.Count;
        }
        if (total == 0)
            return ImmutableArray<Entry>.Empty;

        // The merge: at each step the voice whose next item is earliest, lower voice first on
        // a tie, so a voice is never reordered against itself.
        var merged = ImmutableArray.CreateBuilder<Entry>(total);
        var next = new int[voices.Length];
        while (merged.Count < total)
        {
            int pick = -1;
            for (int vi = 0; vi < voices.Length; vi++)
            {
                var list = perVoice[vi];
                if (list == null || next[vi] >= list.Count)
                    continue;
                if (pick < 0 || Precedes(list[next[vi]], perVoice[pick]![next[pick]]))
                    pick = vi;
            }
            merged.Add(perVoice[pick]![next[pick]++]);
        }
        return merged.MoveToImmutable();
    }

    /// <summary>Strictly earlier in the merge key: onset, then the grace part before the
    /// main part. Equal keys are NOT earlier (the caller's voice order then decides).</summary>
    private static bool Precedes(Entry a, Entry b)
    {
        int byOnset = a.Onset.CompareTo(b.Onset);
        return byOnset < 0 || (byOnset == 0 && a.GraceRank < b.GraceRank);
    }
}
