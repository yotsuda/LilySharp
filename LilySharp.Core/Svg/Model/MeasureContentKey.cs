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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// F3 / S5 substrate (LSP_F3_QUERY_GRAPH_DESIGN.md §1 Layer 1, §19.4): a stable,
/// position-INDEPENDENT identity for a measure's resolved content. This is the
/// "<c>measure_green</c>" the design assumed it got for free but does not — green
/// nodes carry no structural hash and measures are discovered by the collector,
/// not interned (§0.5 correction 1) — so the identity is manufactured here.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE RESOLVED MODEL, NOT THE SOURCE TEXT: the design suggested a source-slice
/// hash, but neither <see cref="Measure.SourceStart"/>/<see cref="Measure.SourceEnd"/>
/// nor the per-item <c>SourcePosition</c> are precise enough to slice a measure's
/// text (the measure offsets straddle bar boundaries; item offsets lag by several
/// characters — click-to-source only tolerates this via a ~50px nearest-match
/// threshold). The reliable signal is the already-resolved <see cref="Measure.Items"/>
/// — the very data layout/render consume — so the key hashes that, with every
/// item's position-dependent <c>SourcePosition</c> excluded.
/// </para>
/// <para>
/// POSITION-INDEPENDENCE / EDIT-LOCALITY: because positions are excluded, a measure
/// whose resolved content is unchanged keeps its key even when an edit elsewhere
/// shifts it. An edit that changes a measure's notes changes only that measure's
/// key. This is what lets S5+ recompute only the measures (and systems) that
/// actually changed.
/// </para>
/// <para>
/// SCOPE / SOUNDNESS (read before building a consumer): this key covers
/// <see cref="Measure.Items"/> plus the measure's layout-affecting structural
/// fields (barlines, break permission/penalty, section label, pickup). It does
/// NOT yet cover:
/// <list type="bullet">
/// <item>The ~17 <see cref="Score"/>-level side-tables (dynamics, articulations,
/// lyrics, tuplet/volta brackets, arpeggios, trill spanners, …) that attach to a
/// measure's notes by source position — these must be folded in (per measure)
/// before key-equality may be treated as full render-identity.</item>
/// <item>The running <see cref="MeasureContext"/> (key/clef/time today; octave /
/// ottava / pending ties deferred): relative-octave/ottava cascades change a
/// measure's resolved pitches via the chain, not its own content. The full
/// memoization key is <c>(content key, entry context, side-tables)</c>.</item>
/// </list>
/// As Layer-1 substrate this is correct and complete for what it claims; the
/// gaps above are explicit prerequisites for the S5b consumer.
/// </para>
/// <para>
/// The hash is deterministic WITHIN a process (it uses <see cref="HashCode"/> and
/// <see cref="string.GetHashCode()"/>, both per-process randomized) — sufficient
/// for an in-session incremental cache and for same-process tests; it is not a
/// stable on-disk identifier. Collision risk is negligible and any real divergence
/// is caught by the incremental==full differential harness when a consumer lands.
/// </para>
/// </remarks>
public readonly record struct MeasureContentKey(int Hash)
{
    /// <summary>Computes the content key of a single measure.</summary>
    public static MeasureContentKey Of(Measure measure)
    {
        var hc = new HashCode();

        // Structural fields that affect layout/render but are NOT in Items.
        // Position fields (SourceStart/SourceEnd/SectionLabelPosition) are
        // deliberately excluded so the key is position-independent.
        hc.Add(measure.StartBarline);
        hc.Add(measure.EndBarline);
        hc.Add(measure.SectionLabel);
        hc.Add(measure.HasBreakAfter);
        hc.Add(measure.LineBreakPermission);
        hc.Add(measure.BreakPenalty);
        hc.Add(measure.PageBreakPermission);
        hc.Add(measure.PageTurnPermission);
        hc.Add(measure.IsPickup);

        // Resolved items, in order.
        foreach (var item in measure.Items)
            hc.Add(HashItem(item));

        return new MeasureContentKey(hc.ToHashCode());
    }

    /// <summary>
    /// Computes the content key of every measure in document order. Index-aligned
    /// with <paramref name="measures"/> (and so with the <see cref="MeasureContext"/>
    /// chain), forming the Layer-1 identity vector for the demand-driven DAG.
    /// </summary>
    public static ImmutableArray<MeasureContentKey> Compute(IReadOnlyList<Measure> measures)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureContentKey>(measures.Count);
        for (int i = 0; i < measures.Count; i++)
            builder.Add(Of(measures[i]));
        return builder.MoveToImmutable();
    }

    // Per-type cache of the public, readable, non-position properties to fold,
    // in a stable (ordinal-by-name) order.
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ItemPropsCache = new();

    private static int HashItem(MusicItem item)
    {
        var hc = new HashCode();
        hc.Add(item.GetType());                       // discriminate item kinds
        foreach (var p in ItemProps(item.GetType()))
            AddValue(ref hc, p.GetValue(item));
        return hc.ToHashCode();
    }

    // Reflection over public properties (auto-including any new content field, so
    // the key never silently drifts behind the model — cf. the §9 drift hazard),
    // excluding only the position-dependent SourcePosition.
    private static PropertyInfo[] ItemProps(Type type) => ItemPropsCache.GetOrAdd(type, static t =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead
                        && p.GetIndexParameters().Length == 0
                        && p.Name != nameof(MusicItem.SourcePosition))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray());

    private static void AddValue(ref HashCode hc, object? value)
    {
        switch (value)
        {
            case null:
                hc.Add(0);
                break;
            case string s:                            // before IEnumerable: hash chars
                hc.Add(s);
                break;
            case IEnumerable e:                        // ImmutableArray<T> etc.: element-wise
                foreach (var element in e)
                    AddValue(ref hc, element);
                break;
            default:                                   // structs/enums/records: content GetHashCode
                hc.Add(value);
                break;
        }
    }

    public override string ToString() => $"mck:{Hash:x8}";
}
