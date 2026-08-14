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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// F3 / S5-3: a session-scoped memo of per-system layout. On an edit, a system
/// whose content is unchanged reuses its cached results instead of recomputing
/// them; only the systems containing edited measures recompute
/// (the F3 incremental design notes §6, §19.5). Two phases are memoized — the
/// per-system spring solve (<see cref="GetOrComputeMeasures"/>) and the per-system
/// skyline build (<see cref="GetOrComputeSkyline"/>) — which the phase breakdown
/// showed are the two dominant per-system costs (the skyline is the larger of the
/// two on multi-staff scores).
/// </summary>
/// <remarks>
/// <para>
/// SOUNDNESS: a cached entry is reused only when the FULL set of inputs matches
/// exactly — the system's ordered per-measure <see cref="MeasureContentKey"/> slice
/// plus every scalar the computation depends on (firstMeasureIndex, count,
/// isFirst/isLast, indent, common shortest duration; and for the skyline also the
/// system height). firstMeasureIndex is part of the key because cached measure
/// layouts stamp an absolute <see cref="MeasureLayout.MeasureIndex"/>. Lookups
/// VERIFY the key exactly (a hash bucket holds a short list, compared element-wise),
/// so a hash collision degrades to a recompute, never a wrong reuse. Because the
/// stored value is exactly what a fresh computation would produce, output stays
/// byte-identical — proven by the IncrementalCompiler incremental==full harness.
/// </para>
/// <para>
/// The dictionaries persist across edits (that is what enables reuse); the
/// content-key vector is refreshed each edit via <see cref="SetContentKeys"/>.
/// </para>
/// </remarks>
internal sealed class SystemLayoutCache
{
    private ImmutableArray<MeasureContentKey> _keys;
    private readonly TypedCache<ImmutableArray<MeasureLayout>> _measures = new();
    private readonly TypedCache<(VerticalSkyline up, VerticalSkyline down)> _skylines = new();
    private readonly TypedCache<MultiStaffLayouter.StaffSkylineSet> _staffSkylines = new();
    private readonly TypedCache<ImmutableArray<BeamLayout>> _staffSystemBeams = new();

    /// <summary>Refreshes the per-measure content keys for the current edit. Must be
    /// called before the layout consults the cache. Also marks the edit boundary for
    /// eviction: entries inserted or hit from here on belong to the new pass and are
    /// exempt from eviction until the next boundary.</summary>
    public void SetContentKeys(ImmutableArray<MeasureContentKey> keys)
    {
        _keys = keys;
        _measures.NextGeneration();
        _skylines.NextGeneration();
        _staffSkylines.NextGeneration();
        _staffSystemBeams.NextGeneration();
    }

    /// <summary>Number of currently cached system measure-layout entries (diagnostics / tests).</summary>
    public int Count => _measures.Count;

    /// <summary>The above-staff stacking memo of the PRELIMINARY annotation pass. One
    /// instance per pass — the two passes stack different systems every keystroke, so a
    /// shared store would overwrite itself twice per keystroke and never hit. Entries
    /// persist across edits by design (a match means the inputs are value-identical, so
    /// staleness cannot serve a wrong answer — see <see cref="AboveStackMemo"/>); the
    /// store is bounded by the session's widest system count, like the paging augments.</summary>
    public AboveStackMemo PreliminaryAboveStack { get; } = new();

    /// <summary>The FINAL annotation pass's above-staff stacking memo
    /// (see <see cref="PreliminaryAboveStack"/>).</summary>
    public AboveStackMemo FinalAboveStack { get; } = new();

    /// <summary>Whether the most recent <see cref="GetOrComputeMeasures"/> call was a
    /// hit (reused) rather than a miss (computed). For diagnostics / tests.</summary>
    public bool LastWasHit { get; private set; }

    /// <summary>Reuses or computes the system's spring-solved measure layouts.</summary>
    public ImmutableArray<MeasureLayout> GetOrComputeMeasures(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<ImmutableArray<MeasureLayout>> compute)
    {
        var result = _measures.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: 0, compute, out bool hit);
        LastWasHit = hit;
        return result;
    }

    /// <summary>Reuses or computes the system's PER-STAFF skylines — the list its staves
    /// are both placed and sprung against.</summary>
    /// <remarks>
    /// Keyed exactly like <see cref="GetOrComputeMeasures"/> and for the same reason: the
    /// staff skylines are a function of that system's measure layouts plus the score's
    /// side-tables, and every one of those inputs is already in this key.
    /// <list type="bullet">
    /// <item>the measure layouts themselves are the value under this same key;</item>
    /// <item><c>Dynamics</c>, <c>Articulations</c>, <c>ChordNames</c>, <c>TupletBrackets</c>
    /// and <c>GraceNotes</c> — the side tables <c>BuildAllStaffSkylines</c> reads — are
    /// folded per measure by <c>MeasureContentKey.BucketSideTables</c>;</item>
    /// <item>slurs, ties and beams are derived from the voices' own measures, which the
    /// intrinsic hash covers (secondary voices included);</item>
    /// <item>which staves exist and what they are is folded by <c>AddStaffIdentity</c>.</item>
    /// </list>
    /// ⚠️ NO <c>systemHeight</c> HERE, unlike <see cref="GetOrComputeSkyline"/>: a staff's
    /// skyline is built in that staff's own frame and does not know where the system's
    /// other staves ended up. Adding it would only make the key stricter, but stating why
    /// it is absent keeps the next reader from "fixing" the asymmetry.
    /// ⚠️ THE VALUE ALSO CARRIES THE INSIDE-STAFF SPANNERS the skylines were built from
    /// (<c>MultiStaffLayouter.StaffInsideSpanners</c>), and the key needs nothing added for
    /// them: they are the slurs, ties and tuplet brackets already named in the list above,
    /// which is why they can ride here instead of being laid out a second time.
    /// ⚠️ THE CACHED LIST IS SHARED, so nothing downstream may mutate it or the skylines in
    /// it. Verified 2026-07-27: every consumer goes through
    /// <c>CalculateStaffGapWithSkylines</c> / <c>AlignmentMinimumWithSkylines</c>, which
    /// only read (<c>Distance</c>, <c>IsEmpty</c>, <c>Count</c>). The one mutation,
    /// <c>ReserveChordRowBand</c>, happens during construction, before the value is stored.
    /// </remarks>
    public MultiStaffLayouter.StaffSkylineSet GetOrComputeStaffSkylines(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<MultiStaffLayouter.StaffSkylineSet> compute)
        => _staffSkylines.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: 0, compute, out _);

    /// <summary>Reuses or computes the system's up/down skyline. Keyed additionally
    /// on <paramref name="systemHeight"/>, which the skyline depends on.</summary>
    public (VerticalSkyline up, VerticalSkyline down) GetOrComputeSkyline(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration, double systemHeight,
        Func<(VerticalSkyline up, VerticalSkyline down)> compute)
        => _skylines.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: systemHeight, compute, out _);

    /// <summary>Reuses or computes ONE staff's laid-out beams for ONE system — the
    /// preliminary annotation pass's per-(staff, system) unit of work.</summary>
    /// <remarks>
    /// Keyed like <see cref="GetOrComputeMeasures"/> — the beams are a function of that
    /// system's measure layouts (member Xs come from <c>MeasureLayout.X</c> /
    /// <c>GetXForTiming</c>) plus the voices' own measures, tuplet spans and the entry
    /// time signature, all of which the content-key slice already folds. That is the same
    /// coverage claim <see cref="GetOrComputeStaffSkylines"/> makes for the beams IT
    /// computes (via <c>MultiStaffLayouter.StaffBeamLayouts</c>), and the same one the
    /// edge-beam lambda in <c>LayoutEngine</c> relies on.
    /// <list type="bullet">
    /// <item><paramref name="systemIndex"/> is in the key because <see cref="BeamLayout"/>
    /// stamps <c>SystemIndex</c> — the same reason firstMeasureIndex is (an absolute stamp
    /// must not survive a reuse under a different absolute).</item>
    /// <item><paramref name="staffIndex"/> is in the key because the value is one staff's
    /// beams and the stamp rides in <c>BeamLayout.StaffIndex</c>.</item>
    /// <item>⚠️ A group whose members CROSS a system boundary must never be memoized here:
    /// its piece in this system exists only because the group reaches into a NEIGHBOUR
    /// system's measures, which this key does not cover. The caller
    /// (<c>LayoutEngine.LayoutPreliminaryStaffBeams</c>) falls back to the unmemoized path
    /// for the whole staff when any such group exists.</item>
    /// </list>
    /// </remarks>
    public ImmutableArray<BeamLayout> GetOrComputeStaffSystemBeams(
        int staffIndex, int systemIndex,
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<ImmutableArray<BeamLayout>> compute)
        => _staffSystemBeams.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: systemIndex,
            compute, out _, extra2: staffIndex);

    /// <summary>Reuses or computes ONE system's augmented PAGING skyline — its base
    /// skyline pair with the annotation ink merged in (scripts, tuplet brackets, bows,
    /// figured bass, voltas, marks, texts, chord names, bar numbers).</summary>
    /// <remarks>
    /// ⚠️ KEYED DIFFERENTLY from every other memo here, and soundly SIMPLER: the key is the
    /// function's own inputs, not the content-key slice they were derived from. One
    /// system's augment is <c>program.Execute(baseline)</c> where the
    /// <see cref="PagingAugmentProgram"/> carries every merge argument RESOLVED (see its
    /// remarks); Execute is deterministic. So "same baseline INSTANCES + equal program ⇒
    /// bit-identical output" holds with no coverage claim about what the annotation
    /// layouts depend on — staff offsets, neighbours, fonts are all inside the resolved
    /// arguments. The baseline is compared by REFERENCE: an unchanged system's base pair
    /// comes back from <see cref="GetOrComputeSkyline"/> as the same instances, and a
    /// recomputed (even if byte-equal) pair just misses into a recompute — conservative,
    /// never wrong.
    /// <para>
    /// One entry per system index, overwritten on miss — the store is bounded by the
    /// widest system count the session ever saw, so it needs no generation eviction. The
    /// cached pair is SHARED across keystrokes; the paging consumer only reads
    /// (<c>PageLayouter</c>'s <c>Distance</c>), verified 2026-08-12.
    /// </para>
    /// </remarks>
    public (VerticalSkyline up, VerticalSkyline down) GetOrComputePagingAugment(
        int systemIndex, (VerticalSkyline up, VerticalSkyline down) baseline,
        PagingAugmentProgram program)
    {
        if (_pagingAugments.TryGetValue(systemIndex, out var e)
            && ReferenceEquals(e.BaseUp, baseline.up)
            && ReferenceEquals(e.BaseDown, baseline.down)
            && program.Matches(e.Program))
            return e.Value;
        var value = program.Execute(baseline);
        _pagingAugments[systemIndex] = new PagingAugmentEntry(
            baseline.up, baseline.down, program, value);
        return value;
    }

    private sealed record PagingAugmentEntry(
        VerticalSkyline BaseUp, VerticalSkyline BaseDown,
        PagingAugmentProgram Program, (VerticalSkyline up, VerticalSkyline down) Value);

    private readonly Dictionary<int, PagingAugmentEntry> _pagingAugments = new();

    // A keyed memo: bucket by a hash of (system identity + extra scalar + content
    // slice), verify the full key exactly on hit so collisions only cost a recompute.
    private sealed class TypedCache<T>
    {
        private sealed class Entry
        {
            public readonly int First, Count;
            public readonly bool IsFirst, IsLast;
            public readonly double Indent, Shortest, Extra, Extra2;
            public readonly ImmutableArray<MeasureContentKey> Content;
            public readonly T Value;

            /// <summary>The pass (see <see cref="NextGeneration"/>) that last inserted
            /// or hit this entry — current-pass entries are exempt from eviction.</summary>
            public int Generation;

            public Entry(int first, int count, bool isFirst, bool isLast, double indent,
                double shortest, double extra, double extra2,
                ImmutableArray<MeasureContentKey> content, T value, int generation)
            {
                First = first; Count = count; IsFirst = isFirst; IsLast = isLast;
                Indent = indent; Shortest = shortest; Extra = extra; Extra2 = extra2;
                Content = content; Value = value;
                Generation = generation;
            }

            public bool Matches(int first, int count, bool isFirst, bool isLast, double indent,
                double shortest, double extra, double extra2,
                ReadOnlySpan<MeasureContentKey> content)
            {
                if (First != first || Count != count || IsFirst != isFirst || IsLast != isLast
                    || Indent != indent || Shortest != shortest || Extra != extra
                    || Extra2 != extra2 || Content.Length != content.Length)
                    return false;
                for (int i = 0; i < content.Length; i++)
                    if (Content[i] != content[i])
                        return false;
                return true;
            }
        }

        // Cap on the STALE backlog: each edit that changes a system leaves its
        // now-stale entry behind, so entries would otherwise accumulate monotonically
        // over a long session. Eviction is always SOUND — a dropped entry just
        // degrades to a recompute (a miss), never a wrong reuse. But entries the
        // CURRENT pass inserted or hit are exempt (second-chance rotation in
        // EvictOldestIfOverCap): evicting those would let a score with more than
        // MaxEntries systems flush its own working set mid-pass and degrade to a
        // permanent 0% hit rate. So the live working set may exceed the cap when the
        // score genuinely needs more; only prior-pass leftovers are bounded by it.
        private const int MaxEntries = 1024;

        private readonly Dictionary<int, List<Entry>> _buckets = new();
        private readonly Queue<(int BucketKey, Entry Entry)> _insertionOrder = new(); // one token per live entry, oldest first
        private int _count;
        private int _generation;

        public int Count => _count;

        /// <summary>Marks an edit boundary (a new layout pass) for the eviction
        /// exemption. Called once per edit via <see cref="SetContentKeys"/>.</summary>
        public void NextGeneration() => _generation++;

        public T GetOrCompute(ImmutableArray<MeasureContentKey> keys,
            int first, int count, bool isFirst, bool isLast, double indent, double shortest,
            double extra, Func<T> compute, out bool hit, double extra2 = 0)
        {
            if (keys.IsDefault || first < 0 || first + count > keys.Length)
            {
                hit = false;
                return compute();
            }

            var slice = new MeasureContentKey[count];
            for (int i = 0; i < count; i++)
                slice[i] = keys[first + i];

            var hc = new HashCode();
            hc.Add(first);
            hc.Add(count);
            hc.Add(isFirst);
            hc.Add(isLast);
            hc.Add(indent);
            hc.Add(shortest);
            hc.Add(extra);
            hc.Add(extra2);
            foreach (var k in slice)
                hc.Add(k);
            int bucketKey = hc.ToHashCode();

            if (_buckets.TryGetValue(bucketKey, out var list))
            {
                foreach (var e in list)
                {
                    if (e.Matches(first, count, isFirst, isLast, indent, shortest, extra, extra2, slice))
                    {
                        e.Generation = _generation; // live this pass -> eviction-exempt
                        hit = true;
                        return e.Value;
                    }
                }
            }
            else
            {
                list = new List<Entry>(1);
                _buckets[bucketKey] = list;
            }

            var fresh = compute();
            var entry = new Entry(first, count, isFirst, isLast, indent, shortest, extra, extra2,
                slice.ToImmutableArray(), fresh, _generation);
            list.Add(entry);
            _insertionOrder.Enqueue((bucketKey, entry));
            _count++;
            EvictOldestIfOverCap();
            hit = false;
            return fresh;
        }

        // Second-chance FIFO, oldest first: an entry the current pass inserted or hit
        // rotates to the back instead of being dropped (evicting the live working set
        // would make a >MaxEntries-system score thrash itself to 0% hits). One full
        // rotation without an eviction means everything live is current-pass — then
        // the cap yields (the cache grows) rather than thrashes. Each queue token
        // holds its exact entry, so removal never touches the wrong entry and _count
        // stays exact.
        private void EvictOldestIfOverCap()
        {
            int scan = _insertionOrder.Count;
            while (_count > MaxEntries && scan-- > 0)
            {
                var (oldKey, entry) = _insertionOrder.Dequeue();
                if (entry.Generation == _generation)
                {
                    _insertionOrder.Enqueue((oldKey, entry));
                    continue;
                }
                if (_buckets.TryGetValue(oldKey, out var oldList) && oldList.Remove(entry))
                {
                    _count--;
                    if (oldList.Count == 0)
                        _buckets.Remove(oldKey);
                }
            }
        }
    }
}
