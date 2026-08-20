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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A keystroke-crossing memo of one (family, system)'s lyric-chain PREFIX — everything
/// the loose-line chain computes BEFORE its closing spring: the element list, the
/// pre-closing gap minima, and the alignment walk's accumulated profile. Session 224
/// measured the walk as the sung keystroke's largest remaining term (106.6 MB of
/// 161 MB on perf-lyrplain1k — <c>AlignmentWalk</c>'s seed and merges, per system,
/// twice per keystroke), for inputs that are per-system local; only the CLOSING reads
/// across systems, and it reads scalars and one neighbour skyline, never allocation.
/// </summary>
/// <remarks>
/// SOUNDNESS — the key is <see cref="VerseSkylineMemo"/>'s clause verbatim: the system's
/// <see cref="MeasureLayout"/> instances BY REFERENCE, which
/// <see cref="SystemLayoutCache.GetOrComputeMeasures"/> hands back only when the
/// system's content-key slice matches element-wise. Every input the PREFIX reads is
/// covered by that slice: the elements (syllables and rows via the side-table buckets
/// and staff identity), the verse skylines the walk merges (themselves memoized under
/// the same clause), and the anchor profile it is seeded with (the per-staff or system
/// skyline — memo values under the same content keys). The font plan sheds the whole
/// cache at the session door.
/// <para>
/// ⚠️ WHAT IS DELIBERATELY NOT IN THE VALUE: every scalar derived from the ANCHOR
/// TABLES (<c>anchorBase</c> / <c>lastSpaceableStaffY</c> / <c>DeviceDown</c>, and the
/// <c>skylineToAnchor</c> frame step built from them). Those are system-0-anchored or
/// placement-anchored quantities — an edit elsewhere can move them while this system's
/// measures stay reference-identical — so the caller recomputes them LIVE (they are
/// dictionary reads and property walks, allocation-free) and the cached first gap is
/// stored RAW, before the frame step is subtracted. See
/// <c>LyricEngraver.DistributeLooseLines</c>'s AnchorFrame, the one spelling both the
/// prefix build and the live side read.
/// </para>
/// <para>
/// ⚠️ THE CACHED WALK IS A SHARED SNAPSHOT. <see cref="AlignmentWalk.Distance"/> only
/// reads it; the one closing branch that must ADVANCE past it (a next system opening
/// with leading loose rows) forks first (<see cref="AlignmentWalk.Fork"/>). The gap
/// list is likewise copied by the caller before the closing gaps are appended.
/// </para>
/// <para>
/// ⚠️ ONE STORE PER ANNOTATION PASS, unlike <see cref="VerseSkylineMemo"/> and for
/// <see cref="AboveStackMemo"/>'s reason: the walk's SEED reads the pass's anchor
/// profile — the scripted system silhouette on the fallback path, the staff profile
/// otherwise — and the two passes' profiles are not always the same value. MEASURED
/// (session 224): a first draft shared one store, the final pass served the
/// preliminary pass's walk, and two incremental==full nets went red with syllables
/// 0.6-0.9 ss deep on script- and pedal-bearing books. One entry per (family, system),
/// overwritten on miss. A null value is a value: a (family, system) with no elements
/// caches its null and skips on every later pass.
/// </para>
/// </remarks>
internal sealed class LyricChainMemo
{
    /// <summary>The cacheable head of one (family, system) chain: the elements in walk
    /// order, the trailing-row bookkeeping, the pre-closing gap minima (the FIRST gap
    /// raw — the live side subtracts the frame step), and the walk at its post-prefix
    /// position.</summary>
    internal sealed record ChainPrefix(
        ImmutableArray<(int Line, int Verse)> Elements,
        ImmutableArray<(int RowStaff, int Index)> RowFirstElement,
        ImmutableArray<LooseLineSpacer.Gap> RawGaps,
        AlignmentWalk Walk);

    private sealed record Entry(
        ImmutableArray<MeasureLayout> Measures, ChainPrefix? Value);

    private readonly Dictionary<(int Family, int System), Entry> _byUnit = new();

    /// <summary>Hits and misses over this memo's lifetime (diagnostics / tests).</summary>
    public (int Hits, int Misses) Stats { get; private set; }

    /// <summary>Reuses or computes one (family, system)'s chain prefix.</summary>
    public ChainPrefix? GetOrCompute(
        int family, int system, ImmutableArray<MeasureLayout> measures,
        Func<ChainPrefix?> compute)
    {
        if (_byUnit.TryGetValue((family, system), out var e)
            && SameInstances(e.Measures, measures))
        {
            Stats = (Stats.Hits + 1, Stats.Misses);
            return e.Value;
        }
        var value = compute();
        _byUnit[(family, system)] = new Entry(measures, value);
        Stats = (Stats.Hits, Stats.Misses + 1);
        return value;
    }

    private static bool SameInstances(
        ImmutableArray<MeasureLayout> a, ImmutableArray<MeasureLayout> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i]))
                return false;
        return true;
    }
}
