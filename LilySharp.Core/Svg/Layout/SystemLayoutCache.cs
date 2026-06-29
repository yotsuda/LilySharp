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
/// (LSP_F3_QUERY_GRAPH_DESIGN.md §6, §19.5). Two phases are memoized — the
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
public sealed class SystemLayoutCache
{
    private ImmutableArray<MeasureContentKey> _keys;
    private readonly TypedCache<ImmutableArray<MeasureLayout>> _measures = new();
    private readonly TypedCache<(VerticalSkyline up, VerticalSkyline down)> _skylines = new();

    /// <summary>Refreshes the per-measure content keys for the current edit. Must be
    /// called before the layout consults the cache.</summary>
    public void SetContentKeys(ImmutableArray<MeasureContentKey> keys) => _keys = keys;

    /// <summary>Number of currently cached system measure-layout entries (diagnostics / tests).</summary>
    public int Count => _measures.Count;

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

    /// <summary>Reuses or computes the system's up/down skyline. Keyed additionally
    /// on <paramref name="systemHeight"/>, which the skyline depends on.</summary>
    public (VerticalSkyline up, VerticalSkyline down) GetOrComputeSkyline(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration, double systemHeight,
        Func<(VerticalSkyline up, VerticalSkyline down)> compute)
        => _skylines.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: systemHeight, compute, out _);

    // A keyed memo: bucket by a hash of (system identity + extra scalar + content
    // slice), verify the full key exactly on hit so collisions only cost a recompute.
    private sealed class TypedCache<T>
    {
        private readonly struct Entry
        {
            public readonly int First, Count;
            public readonly bool IsFirst, IsLast;
            public readonly double Indent, Shortest, Extra;
            public readonly ImmutableArray<MeasureContentKey> Content;
            public readonly T Value;

            public Entry(int first, int count, bool isFirst, bool isLast, double indent,
                double shortest, double extra, ImmutableArray<MeasureContentKey> content, T value)
            {
                First = first; Count = count; IsFirst = isFirst; IsLast = isLast;
                Indent = indent; Shortest = shortest; Extra = extra; Content = content; Value = value;
            }

            public bool Matches(int first, int count, bool isFirst, bool isLast, double indent,
                double shortest, double extra, ReadOnlySpan<MeasureContentKey> content)
            {
                if (First != first || Count != count || IsFirst != isFirst || IsLast != isLast
                    || Indent != indent || Shortest != shortest || Extra != extra
                    || Content.Length != content.Length)
                    return false;
                for (int i = 0; i < content.Length; i++)
                    if (Content[i] != content[i])
                        return false;
                return true;
            }
        }

        private readonly Dictionary<int, List<Entry>> _buckets = new();

        public int Count
        {
            get
            {
                int n = 0;
                foreach (var b in _buckets.Values) n += b.Count;
                return n;
            }
        }

        public T GetOrCompute(ImmutableArray<MeasureContentKey> keys,
            int first, int count, bool isFirst, bool isLast, double indent, double shortest,
            double extra, Func<T> compute, out bool hit)
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
            foreach (var k in slice)
                hc.Add(k);
            int bucketKey = hc.ToHashCode();

            if (_buckets.TryGetValue(bucketKey, out var list))
            {
                foreach (var e in list)
                {
                    if (e.Matches(first, count, isFirst, isLast, indent, shortest, extra, slice))
                    {
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
            list.Add(new Entry(first, count, isFirst, isLast, indent, shortest, extra,
                slice.ToImmutableArray(), fresh));
            hit = false;
            return fresh;
        }
    }
}
