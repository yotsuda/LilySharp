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
/// F3 / S5-3a: a session-scoped memo of per-system measure layout (the spring
/// solve + within-system X positioning that the phase breakdown showed is ~60% of
/// layout). On an edit, a system whose content is unchanged reuses its cached
/// <see cref="MeasureLayout"/> array instead of re-solving its springs; only the
/// systems containing edited measures recompute (LSP_F3_QUERY_GRAPH_DESIGN.md §6,
/// §19.5).
/// </summary>
/// <remarks>
/// <para>
/// SOUNDNESS: a cached entry is reused only when the FULL set of inputs to
/// <c>LayoutMeasures</c> matches exactly — the system's ordered per-measure
/// <see cref="MeasureContentKey"/> slice plus every scalar the solve depends on
/// (firstMeasureIndex, count, isFirst/isLast, indent, common shortest duration).
/// firstMeasureIndex is part of the key because the cached layouts stamp an
/// absolute <see cref="MeasureLayout.MeasureIndex"/>; reusing a layout from a
/// different position would carry a stale index. Lookups VERIFY the key exactly
/// (a hash bucket holds a short list, compared element-wise), so a hash collision
/// degrades to a recompute, never a wrong reuse. Because the stored value is
/// exactly what a fresh solve would produce, output stays byte-identical — proven
/// by the IncrementalCompiler incremental==full harness.
/// </para>
/// <para>
/// SCOPE: single-staff scores only. The key is built from the primary voice's
/// content keys; a multi-staff system's solve couples all staves' columns, so the
/// driver does NOT install a cache for multi-staff scores (it falls back to full
/// layout). The dictionary persists across edits (that is what enables reuse); the
/// content-key vector is refreshed each edit via <see cref="SetContentKeys"/>.
/// </para>
/// </remarks>
public sealed class SystemLayoutCache
{
    private readonly struct Entry
    {
        public readonly int First, Count;
        public readonly bool IsFirst, IsLast;
        public readonly double Indent, Shortest;
        public readonly ImmutableArray<MeasureContentKey> Content; // the system's measure keys, in order
        public readonly ImmutableArray<MeasureLayout> Layout;

        public Entry(int first, int count, bool isFirst, bool isLast, double indent,
            double shortest, ImmutableArray<MeasureContentKey> content, ImmutableArray<MeasureLayout> layout)
        {
            First = first; Count = count; IsFirst = isFirst; IsLast = isLast;
            Indent = indent; Shortest = shortest; Content = content; Layout = layout;
        }

        public bool Matches(int first, int count, bool isFirst, bool isLast, double indent,
            double shortest, ReadOnlySpan<MeasureContentKey> content)
        {
            if (First != first || Count != count || IsFirst != isFirst || IsLast != isLast
                || Indent != indent || Shortest != shortest || Content.Length != content.Length)
                return false;
            for (int i = 0; i < content.Length; i++)
                if (Content[i] != content[i])
                    return false;
            return true;
        }
    }

    private readonly Dictionary<int, List<Entry>> _buckets = new();
    private ImmutableArray<MeasureContentKey> _keys;

    /// <summary>Refreshes the per-measure content keys for the current edit. Must be
    /// called before <see cref="Layout"/> consults the cache.</summary>
    public void SetContentKeys(ImmutableArray<MeasureContentKey> keys) => _keys = keys;

    /// <summary>Number of currently cached system entries (diagnostics / tests).</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (var b in _buckets.Values) n += b.Count;
            return n;
        }
    }

    /// <summary>Whether the most recent <see cref="GetOrCompute"/> call was a hit
    /// (reused) rather than a miss (computed). For diagnostics / tests.</summary>
    public bool LastWasHit { get; private set; }

    /// <summary>
    /// Returns the cached layout for the system spanning
    /// <paramref name="firstMeasureIndex"/>..+<paramref name="measureCount"/> when
    /// every input matches a prior call, otherwise invokes <paramref name="compute"/>
    /// and stores the result. Falls back to <paramref name="compute"/> (no caching)
    /// when content keys are unavailable for the range.
    /// </summary>
    public ImmutableArray<MeasureLayout> GetOrCompute(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<ImmutableArray<MeasureLayout>> compute)
    {
        if (_keys.IsDefault || firstMeasureIndex < 0
            || firstMeasureIndex + measureCount > _keys.Length)
        {
            LastWasHit = false;
            return compute();
        }

        var slice = new MeasureContentKey[measureCount];
        for (int i = 0; i < measureCount; i++)
            slice[i] = _keys[firstMeasureIndex + i];

        var hc = new HashCode();
        hc.Add(firstMeasureIndex);
        hc.Add(measureCount);
        hc.Add(isFirstSystem);
        hc.Add(isLastSystem);
        hc.Add(indent);
        hc.Add(commonShortestDuration);
        foreach (var k in slice)
            hc.Add(k);
        int bucketKey = hc.ToHashCode();

        if (_buckets.TryGetValue(bucketKey, out var list))
        {
            foreach (var e in list)
            {
                if (e.Matches(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                        indent, commonShortestDuration, slice))
                {
                    LastWasHit = true;
                    return e.Layout;
                }
            }
        }
        else
        {
            list = new List<Entry>(1);
            _buckets[bucketKey] = list;
        }

        var fresh = compute();
        list.Add(new Entry(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
            indent, commonShortestDuration, slice.ToImmutableArray(), fresh));
        LastWasHit = false;
        return fresh;
    }
}
