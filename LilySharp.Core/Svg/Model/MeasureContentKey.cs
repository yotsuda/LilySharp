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
using System.Linq.Expressions;
using System.Reflection;
using LilySharp.Core.Svg.Collector;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// F3 / S5 substrate (LSP_F3_QUERY_GRAPH_DESIGN.md §1 Layer 1, §19.4): a stable,
/// position-INDEPENDENT identity for a measure's rendered content. This is the
/// "<c>measure_green</c>" the design assumed it got for free but does not — green
/// nodes carry no structural hash and measures are discovered by the collector,
/// not interned (§0.5 correction 1) — so the identity is manufactured here.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE RESOLVED MODEL, NOT THE SOURCE TEXT: the design suggested a source-slice
/// hash, but neither <see cref="Measure.SourceStart"/>/<see cref="Measure.SourceEnd"/>
/// nor the per-item <c>SourcePosition</c> are precise enough to slice a measure's
/// text (measure offsets straddle bar boundaries; item offsets lag several chars —
/// click-to-source only tolerates this via a ~50px nearest-match threshold). So the
/// key hashes the already-resolved model — the very data layout/render consume —
/// with every position-dependent field excluded.
/// </para>
/// <para>
/// TWO LEVELS:
/// <list type="bullet">
/// <item><see cref="Of(Measure)"/> / <see cref="Compute(IReadOnlyList{Measure})"/>
/// — the measure's INTRINSIC content: <see cref="Measure.Items"/> plus its
/// layout-affecting structural fields (barlines, break permission/penalty, section
/// label, pickup).</item>
/// <item><see cref="Compute(Score)"/> — the COMPLETE per-measure render-input key:
/// the intrinsic content PLUS the <see cref="Score"/> side-tables (dynamics,
/// articulations, lyrics, tuplet/volta brackets, arpeggios, trill spanners, …) that
/// attach to the measure by <c>MeasureIndex</c>, PLUS the entry
/// <see cref="MeasureContext"/> (key/clef/time at the measure start, which drives
/// the line-start prefix glyphs). Key-equality here means render-identity (modulo
/// the deferred octave/ottava context, which already shows up as changed resolved
/// items rather than via the context carry).</item>
/// </list>
/// </para>
/// <para>
/// POSITION-INDEPENDENCE / EDIT-LOCALITY: every absolute position is excluded —
/// each item's <c>SourcePosition</c>, and each side-table item's absolute
/// <c>MeasureIndex</c>/<c>StartMeasureIndex</c>/<c>EndMeasureIndex</c> (the latter
/// are the bucketing key, not content; spanners instead fold a position-independent
/// relative role: start / middle / end). So a measure whose rendered content is
/// unchanged keeps its key even when an edit elsewhere shifts it — what lets S5+
/// recompute only the measures (and systems) that actually changed.
/// </para>
/// <para>
/// SOUNDNESS BIAS: fields are folded by reflection so a new content field cannot
/// silently drift out of the key (§9 drift hazard). Only proven-positional fields
/// are excluded; everything else is included, so the bias is toward over- (never
/// under-) sensitivity — a missed reuse costs a little speed, a false reuse would
/// cost correctness. The hash is deterministic within a process (it uses
/// <see cref="HashCode"/> / <see cref="string.GetHashCode()"/>, both per-process
/// randomized) — sufficient for an in-session cache and same-process tests.
/// Collisions are git-like negligible and caught by the incremental==full harness.
/// </para>
/// </remarks>
public readonly record struct MeasureContentKey(int Hash)
{
    /// <summary>Computes the INTRINSIC content key of a single measure (its items
    /// and structural fields only — no side-tables, no entry context).</summary>
    public static MeasureContentKey Of(Measure measure)
    {
        var hc = new HashCode();
        AddIntrinsic(ref hc, measure);
        return new MeasureContentKey(hc.ToHashCode());
    }

    /// <summary>Intrinsic content keys for a measure list, in document order.</summary>
    public static ImmutableArray<MeasureContentKey> Compute(IReadOnlyList<Measure> measures)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureContentKey>(measures.Count);
        for (int i = 0; i < measures.Count; i++)
            builder.Add(Of(measures[i]));
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// The COMPLETE per-measure render-input key vector for a score's primary
    /// voice: intrinsic content + entry <see cref="MeasureContext"/> + the score
    /// side-tables bucketed onto each measure by <c>MeasureIndex</c>. Index-aligned
    /// with <c>score.Voice.Measures</c>.
    /// </summary>
    public static ImmutableArray<MeasureContentKey> Compute(Score score)
    {
        var measures = score.Voice.Measures;
        int n = measures.Length;
        var chain = MeasureContextChain.Compute(score);
        var sideTables = BucketSideTables(score, n);

        var builder = ImmutableArray.CreateBuilder<MeasureContentKey>(n);
        for (int i = 0; i < n; i++)
        {
            var hc = new HashCode();
            AddIntrinsic(ref hc, measures[i]);
            hc.Add(chain.Entry[i]);                 // line-start prefix identity
            foreach (int itemHash in sideTables[i]) // attached annotations (ordered)
                hc.Add(itemHash);
            builder.Add(new MeasureContentKey(hc.ToHashCode()));
        }
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// The COMPLETE per-measure render-input key vector for a multi-staff score:
    /// per measure index, every staff's intrinsic content + that staff's entry
    /// context, combined, plus the score side-tables bucketed by <c>MeasureIndex</c>.
    /// Index-aligned with the primary content staff's measures.
    /// </summary>
    /// <remarks>
    /// Side-tables are folded by measure index across ALL staves (StaffIndex is not
    /// needed: a measure's key already combines every staff at that index, so a
    /// dynamic on any staff at measure i lands in measure i's key regardless of
    /// staff — sound, and it is what couples the per-system spring solve, which spans
    /// all staves' columns).
    /// </remarks>
    public static ImmutableArray<MeasureContentKey> Compute(MultiStaffScore score)
    {
        int n = score.MeasureCount;
        var acc = new HashCode[n];

        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            var measures = staff.PrimaryVoice.Measures;
            var chain = MeasureContextChain.Compute(
                measures, new MeasureContext(score.KeySignature, score.TimeSignature, staff.Clef));
            int m = Math.Min(n, measures.Length);
            for (int i = 0; i < m; i++)
            {
                acc[i].Add(staffIndex);                 // discriminate which staff
                AddIntrinsic(ref acc[i], measures[i]);
                acc[i].Add(chain.Entry[i]);
            }
        }

        var sideTables = BucketSideTables(score, n);
        for (int i = 0; i < n; i++)
            foreach (int itemHash in sideTables[i])
                acc[i].Add(itemHash);

        var builder = ImmutableArray.CreateBuilder<MeasureContentKey>(n);
        for (int i = 0; i < n; i++)
            builder.Add(new MeasureContentKey(acc[i].ToHashCode()));
        return builder.MoveToImmutable();
    }

    public override string ToString() => $"mck:{Hash:x8}";

    // --- intrinsic (items + structural fields) ---

    private static void AddIntrinsic(ref HashCode hc, Measure measure)
    {
        // Structural fields that affect layout/render but are NOT in Items. Position
        // fields (SourceStart/SourceEnd/SectionLabelPosition) are excluded so the
        // key is position-independent.
        hc.Add(measure.StartBarline);
        hc.Add(measure.EndBarline);
        hc.Add(measure.SectionLabel);
        hc.Add(measure.HasBreakAfter);
        hc.Add(measure.LineBreakPermission);
        hc.Add(measure.BreakPenalty);
        hc.Add(measure.PageBreakPermission);
        hc.Add(measure.PageTurnPermission);
        hc.Add(measure.IsPickup);

        foreach (var item in measure.Items)
            hc.Add(HashContent(item, ItemExclusions));
    }

    // --- side-tables, bucketed onto measures by MeasureIndex ---

    // Excluded from item hashes: the position-dependent source offset.
    private static readonly HashSet<string> ItemExclusions = new(StringComparer.Ordinal)
    {
        nameof(MusicItem.SourcePosition),
    };

    // Excluded from side-table item hashes: the source offset AND the absolute
    // measure indices (the bucketing key, not content — a spanner's relative role
    // is folded separately so it stays position-independent).
    private static readonly HashSet<string> SideExclusions = new(StringComparer.Ordinal)
    {
        "SourcePosition", "MeasureIndex", "StartMeasureIndex", "EndMeasureIndex",
    };

    private static List<int>[] BucketSideTables(Score score, int measureCount)
    {
        var buckets = new List<int>[measureCount];
        for (int i = 0; i < measureCount; i++)
            buckets[i] = new List<int>();

        // Single-measure tables: each item belongs to one measure (item.MeasureIndex).
        // Fixed call order keeps the per-bucket fold deterministic.
        BucketSingle(score.Dynamics, buckets);
        BucketSingle(score.Articulations, buckets);
        BucketSingle(score.GraceNotes, buckets);
        BucketSingle(score.Tremolos, buckets);
        BucketSingle(score.Lyrics, buckets);
        BucketSingle(score.MusicMarks, buckets);
        BucketSingle(score.CustomTexts, buckets);
        BucketSingle(score.TupletBrackets, buckets);
        BucketSingle(score.Arpeggios, buckets);
        BucketSingle(score.FiguredBasses, buckets);
        BucketSingle(score.ChordNames, buckets);
        BucketSingle(score.PercentRepeats, buckets);
        BucketSingle(score.CrossStaffItems, buckets);
        BucketSingle(score.GrobOverrides, buckets);
        BucketSingle(score.GrobReverts, buckets);

        // Span tables: an item covers [StartMeasureIndex, EndMeasureIndex]; fold it
        // into every covered measure with its relative role so the key is
        // position-independent (no absolute indices).
        BucketSpan(score.VoltaBrackets, buckets);
        BucketSpan(score.TrillSpanners, buckets);

        return buckets;
    }

    private static List<int>[] BucketSideTables(MultiStaffScore score, int measureCount)
    {
        var buckets = new List<int>[measureCount];
        for (int i = 0; i < measureCount; i++)
            buckets[i] = new List<int>();

        // Same tables as the Score overload, by MeasureIndex across all staves.
        // (MultiStaffScore has no separate Tremolos table — tremolo lives on the
        // note item, already folded via the intrinsic key.)
        BucketSingle(score.Dynamics, buckets);
        BucketSingle(score.Articulations, buckets);
        BucketSingle(score.GraceNotes, buckets);
        BucketSingle(score.Lyrics, buckets);
        BucketSingle(score.MusicMarks, buckets);
        BucketSingle(score.CustomTexts, buckets);
        BucketSingle(score.TupletBrackets, buckets);
        BucketSingle(score.Arpeggios, buckets);
        BucketSingle(score.FiguredBasses, buckets);
        BucketSingle(score.ChordNames, buckets);
        BucketSingle(score.PercentRepeats, buckets);
        BucketSingle(score.CrossStaffItems, buckets);
        BucketSingle(score.GrobOverrides, buckets);
        BucketSingle(score.GrobReverts, buckets);
        BucketSpan(score.VoltaBrackets, buckets);
        BucketSpan(score.TrillSpanners, buckets);

        return buckets;
    }

    private static void BucketSingle(IEnumerable items, List<int>[] buckets)
    {
        foreach (var item in items)
        {
            int mi = GetInt(item, "MeasureIndex");
            if (mi >= 0 && mi < buckets.Length)
                buckets[mi].Add(HashContent(item, SideExclusions));
        }
    }

    private static void BucketSpan(IEnumerable items, List<int>[] buckets)
    {
        foreach (var item in items)
        {
            int start = GetInt(item, "StartMeasureIndex");
            int end = GetInt(item, "EndMeasureIndex");
            for (int mi = start; mi <= end; mi++)
            {
                if (mi < 0 || mi >= buckets.Length)
                    continue;
                // Relative role: 0=only, 1=start, 2=middle, 3=end. Position-independent.
                int role = start == end ? 0 : mi == start ? 1 : mi == end ? 3 : 2;
                var hc = new HashCode();
                hc.Add(role);
                hc.Add(HashContent(item, SideExclusions));
                buckets[mi].Add(hc.ToHashCode());
            }
        }
    }

    // --- content hashing (reflection discovers properties once per type; the
    //     per-item hot path uses compiled getters) ---

    // Reflection is used ONCE per type to discover the public, readable, non-excluded
    // properties (so the key auto-covers any new content field and never silently
    // drifts behind the model — cf. the §9 drift hazard); Compute then folds them via
    // COMPILED getters rather than Reflection.GetValue, ~30x faster, which matters
    // because Compute runs on every edit (a wholesale GetValue version cost ~3.5ms per
    // edit on a 41-measure multi-staff score — enough to cancel the reuse it enables).
    private static readonly ConcurrentDictionary<Type, Func<object, object?>[]> ItemGetters = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object?>[]> SideGetters = new();
    private static readonly ConcurrentDictionary<(Type, string), Func<object, int>?> IntGetters = new();

    private static int HashContent(object item, HashSet<string> excluded)
    {
        var hc = new HashCode();
        hc.Add(item.GetType());                       // discriminate kinds
        foreach (var get in Getters(item.GetType(), excluded))
            AddValue(ref hc, get(item));
        return hc.ToHashCode();
    }

    private static Func<object, object?>[] Getters(Type type, HashSet<string> excluded)
    {
        var cache = ReferenceEquals(excluded, ItemExclusions) ? ItemGetters : SideGetters;
        return cache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead
                            && p.GetIndexParameters().Length == 0
                            && !excluded.Contains(p.Name))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(CompileGetter)
                .ToArray());
    }

    // o => (object)((TDeclaring)o).Prop
    private static Func<object, object?> CompileGetter(PropertyInfo p)
    {
        var o = Expression.Parameter(typeof(object), "o");
        var body = Expression.Convert(
            Expression.Property(Expression.Convert(o, p.DeclaringType!), p), typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, o).Compile();
    }

    private static int GetInt(object item, string property)
    {
        var get = IntGetters.GetOrAdd((item.GetType(), property), static key =>
        {
            var p = key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || p.PropertyType != typeof(int))
                return null;
            var o = Expression.Parameter(typeof(object), "o");
            var body = Expression.Property(Expression.Convert(o, p.DeclaringType!), p);
            return Expression.Lambda<Func<object, int>>(body, o).Compile();
        });
        return get == null ? -1 : get(item);
    }

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
                try
                {
                    foreach (var element in e)
                        AddValue(ref hc, element);
                }
                catch (InvalidOperationException)      // default (uninitialized) ImmutableArray
                {
                    hc.Add(-1);
                }
                break;
            default:                                   // structs/enums/records: content GetHashCode
                hc.Add(value);
                break;
        }
    }
}
