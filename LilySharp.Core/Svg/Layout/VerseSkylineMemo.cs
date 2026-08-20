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
/// A keystroke-crossing memo of one SYSTEM's per-(line, verse) lyric verse skylines —
/// the profiles <c>LyricEngraver.DistributeLooseLines</c> measures verse-to-verse and
/// verse-to-staff steps with. Session 224 measured building them fresh as 51 MB of a
/// 212 MB perf-lyrplain1k keystroke (both annotation passes, both directions), for
/// inputs that are byte-identical on every system a one-note edit did not touch.
/// </summary>
/// <remarks>
/// SOUNDNESS — the key is <see cref="FingScriptMemo"/>'s clause, not a new one: the
/// system's <see cref="MeasureLayout"/> instances BY REFERENCE. The lyric layouts these
/// skylines are built from are remade on every keystroke (the model is, so they cannot
/// be reference-keyed), but every input that reaches a verse skyline — the syllable
/// texts and verse numbers (<c>score.Lyrics</c>, folded into the per-measure content key
/// by <c>MeasureContentKey.BucketSideTables</c>), the syllable X (read off the measure
/// layouts, overlap resolution included), the line key (staff structure, folded by
/// <c>AddStaffIdentity</c>) — is covered by that content key, and
/// <see cref="SystemLayoutCache.GetOrComputeMeasures"/> hands back the SAME
/// <c>MeasureLayout</c> array only when the system's content-key slice matches
/// element-wise. So reference-equality of the layouts IS content-equality of the
/// syllables: a syllable edit misses the measures memo, produces new instances, and
/// declines this one. The remaining off-key input, the font plan, sheds the whole
/// <see cref="SystemLayoutCache"/> at the session door (IncrementalCompiler's guard) —
/// and this store rides on that cache, so it goes with it.
/// <para>
/// ⚠️ ONE SHARED INSTANCE FOR BOTH ANNOTATION PASSES, unlike <see cref="AboveStackMemo"/>
/// (one per pass): that type's inputs differ between the passes, so sharing made the two
/// overwrite each other twice a keystroke. A verse skyline is X-only — it reads nothing
/// the passes disagree about (no Y, no pages, no chain ends) — and both passes hold the
/// same measure-layout instances, so the second pass HITS what the first computed. That
/// sharing is half the win.
/// </para>
/// <para>
/// ⚠️ THE CACHED SKYLINES ARE SHARED AND MUTABLE, so consumers must not mutate them.
/// Verified at every reader: <c>AlignmentWalk.Seed</c>/<c>Advance</c>/<c>Distance</c>
/// merge INTO the walk's own accumulation and only read their arguments, and the chain's
/// spring construction reads distances. One entry per system index, overwritten on miss —
/// bounded by the widest system count the session sees, like the paging augments.
/// </para>
/// </remarks>
internal sealed class VerseSkylineMemo
{
    /// <summary>One system's verse skylines: the alignment line (staff index or -1),
    /// the verse number, and the verse's own up/down profiles, in first-seen order.</summary>
    internal readonly record struct VerseSkylines(
        int Line, int Verse, VerticalSkyline Up, VerticalSkyline Down);

    private sealed record Entry(
        ImmutableArray<MeasureLayout> Measures, ImmutableArray<VerseSkylines> Value);

    private readonly Dictionary<int, Entry> _bySystem = new();

    /// <summary>Hits and misses over this memo's lifetime (diagnostics / tests) — what
    /// lets a net assert the memo serves rather than silently recomputing forever.</summary>
    public (int Hits, int Misses) Stats { get; private set; }

    /// <summary>Reuses or computes one system's verse skylines.</summary>
    /// <param name="system">The system index (the store's slot; the value carries no
    /// absolute stamps, so a shifted book simply misses and overwrites).</param>
    /// <param name="measures">The system's measure layouts — the reference key.</param>
    /// <param name="compute">The from-scratch build for a miss.</param>
    public ImmutableArray<VerseSkylines> GetOrCompute(
        int system, ImmutableArray<MeasureLayout> measures,
        Func<ImmutableArray<VerseSkylines>> compute)
    {
        if (_bySystem.TryGetValue(system, out var e) && SameInstances(e.Measures, measures))
        {
            Stats = (Stats.Hits + 1, Stats.Misses);
            return e.Value;
        }
        var value = compute();
        _bySystem[system] = new Entry(measures, value);
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
