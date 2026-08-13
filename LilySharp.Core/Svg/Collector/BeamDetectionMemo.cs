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

using System.Collections.Generic;
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// ⒫/⑶ beamdirs (HANDOFF §1): the per-measure beam-DETECTION memo for the collect-phase
/// stem-direction probe (<c>MeasureCollector.ResolveBeamStemDirections</c>). Keyed on the
/// measure's position-independent CONTENT (plus the meter in effect and the measure's
/// tuplet brackets — the complete input set of a single-measure detection), it hands back
/// the groups a previous detection of the same content produced, so a keystroke re-detects
/// only the measures the edit actually changed. The BAKE (stem stamps, pure tips, rest
/// shifts, BeamId numbering) always runs live over the returned groups, which is what keeps
/// the resolved model byte-identical to a from-scratch collect — see
/// <see cref="BeamDetector"/>'s memo remarks for the full soundness argument.
/// </summary>
/// <remarks>
/// <para>
/// TWO LIFETIMES, ONE MECHANISM: <c>IncrementalCompiler</c> owns one instance across edits
/// and calls <see cref="BeginCollect"/> per compile, so the PREVIOUS keystroke's entries
/// serve the next one (the cross-keystroke reuse HANDOFF ⑶ names). Within one collect the
/// current generation also serves — a book of content-identical measures detects one and
/// replays the rest, even on the session's first full compile.
/// </para>
/// <para>
/// GENERATIONS bound the memory: <see cref="BeginCollect"/> drops everything not stored or
/// hit in the previous collect. Entries hold <see cref="BeamGroup"/>s whose
/// <c>Member.Item</c> references are the STORING collect's items — the bake never reads
/// them (it addresses the live measure by <c>ItemIndex</c>), but they do pin those items
/// until the entry ages out, the same retention shape as the collect recording itself
/// (<c>IncrementalCompiler._collectSource</c> pins the whole previous collector).
/// </para>
/// <para>
/// The key is a 64-bit FNV fold (<see cref="MeasureContentKey.Hash64"/>); equality of the
/// fold decides reuse, the same collision-bound argument as whole-layout reuse and the
/// per-system fragment cache. A collision is ~2⁻⁶⁴; a missed reuse only costs speed.
/// </para>
/// </remarks>
internal sealed class BeamDetectionMemo
{
    private Dictionary<long, ImmutableArray<BeamGroup>> _previous = new();
    private Dictionary<long, ImmutableArray<BeamGroup>> _current = new();

    /// <summary>Groups replayed from the memo this collect (per-measure hits, including
    /// within-collect duplicates). Diagnostics/tests.</summary>
    internal int Hits { get; private set; }

    /// <summary>Measures detected live and stored this collect. Diagnostics/tests.</summary>
    internal int Misses { get; private set; }

    /// <summary>Starts a new generation: the entries stored (or re-hit) by the previous
    /// collect become the lookup set, everything older is dropped. Called once per compile
    /// by the owner; a resume-abort refill within the same compile keeps the generation
    /// (its entries are content-keyed and text-identical, so they stay sound).</summary>
    public void BeginCollect()
    {
        (_previous, _current) = (_current, new Dictionary<long, ImmutableArray<BeamGroup>>());
        _previous.TrimExcess();
        Hits = 0;
        Misses = 0;
    }

    /// <summary>Looks the key up in the current generation first (within-collect reuse),
    /// then in the previous one (cross-keystroke reuse; a hit is promoted so it survives
    /// the next <see cref="BeginCollect"/>).</summary>
    internal bool TryGet(long key, out ImmutableArray<BeamGroup> groups)
    {
        if (_current.TryGetValue(key, out groups))
        {
            Hits++;
            return true;
        }
        if (_previous.TryGetValue(key, out groups))
        {
            _current[key] = groups;
            Hits++;
            return true;
        }
        Misses++;
        return false;
    }

    /// <summary>Stores one measure's detected groups (possibly empty — a beam-free measure
    /// is a result too, and storing it is what spares the next collect the scan).</summary>
    internal void Store(long key, ImmutableArray<BeamGroup> groups) => _current[key] = groups;
}
