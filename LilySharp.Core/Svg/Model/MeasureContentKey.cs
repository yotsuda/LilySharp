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
using System.Threading;
using LilySharp.Core.Svg.Collector;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// F3 / S5 substrate (the F3 incremental design notes §1 Layer 1, §19.4): a stable,
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
/// cost correctness. The hash is deterministic within a process (strings fold their
/// chars; other elements fold their own <see cref="object.GetHashCode()"/>,
/// per-process randomized) — sufficient for an in-session cache and same-process
/// tests. The key is a 64-bit FNV-1a fold (<see cref="Hash64"/>): whole-layout reuse
/// and the per-system cache both decide equality by comparing this value, so its
/// collision probability — not just the incremental==full harness — is what bounds
/// a false reuse. Strings and composed per-item sub-hashes fold at full 64-bit
/// width and int-sized leaves fold losslessly, so the bound is ~2⁻⁶⁴ — EXCEPT where
/// the only differing leaf is itself a 32-bit <c>GetHashCode</c> (records/structs
/// such as <see cref="MeasureContext"/>, doubles): a single-leaf edit there
/// collides at that hash's ~2⁻³². A missed reuse (over-sensitivity) only costs
/// speed.
/// </para>
/// </remarks>
public readonly record struct MeasureContentKey(long Hash)
{
    /// <summary>Computes the INTRINSIC content key of a single measure (its items
    /// and structural fields only — no side-tables, no entry context).</summary>
    public static MeasureContentKey Of(Measure measure)
    {
        var hc = new Hash64();
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
            var hc = new Hash64();
            AddIntrinsic(ref hc, measures[i]);
            hc.Add(chain.Entry[i]);                  // line-start prefix identity
            foreach (long itemHash in sideTables[i]) // attached annotations (ordered)
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
        var acc = new Hash64[n];
        for (int i = 0; i < n; i++)
            acc[i] = new Hash64();                    // seed the FNV basis (array init is zero)

        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            var measures = staff.PrimaryVoice.Measures;
            var chain = MeasureContextChain.Compute(
                measures, new MeasureContext(score.KeySignature, score.TimeSignature, staff.Clef));
            int m = Math.Min(n, measures.Length);
            for (int i = 0; i < m; i++)
            {
                acc[i].Add(staffIndex);                 // discriminate which staff
                AddStaffIdentity(ref acc[i], staff);    // per-staff (indent/name/tuning/…)
                AddIntrinsic(ref acc[i], measures[i]);
                acc[i].Add(chain.Entry[i]);
            }
        }

        var sideTables = BucketSideTables(score, n);
        for (int i = 0; i < n; i++)
            foreach (long itemHash in sideTables[i])
                acc[i].Add(itemHash);

        var builder = ImmutableArray.CreateBuilder<MeasureContentKey>(n);
        for (int i = 0; i < n; i++)
            builder.Add(new MeasureContentKey(acc[i].ToHashCode()));
        return builder.MoveToImmutable();
    }

    public override string ToString() => $"mck:{Hash:x16}";

    // --- intrinsic (items + structural fields) ---

    private static void AddIntrinsic(ref Hash64 hc, Measure measure)
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

    // --- staff-level identity (per-staff fields, not per-measure) ---

    private static void AddStaffIdentity(ref Hash64 hc, Staff staff)
    {
        // Staff-level fields that affect the staff's layout/render but are constant
        // across its measures: the instrument name (drives the system indent and the
        // drawn label), tab tuning, ossia scaling, hara-kiri visibility, staff
        // affinity, per-staff key signature, and the text-row band. Clef and the
        // primary voice's measures are already captured (entry context + AddIntrinsic);
        // secondary voices defeat reuse upstream via MultiStaffScore.HasSecondaryVoices.
        hc.Add(staff.InstrumentName);
        hc.Add(staff.Tuning);
        hc.Add(staff.IsOssia);
        hc.Add(staff.RemoveEmpty);
        hc.Add(staff.RemoveFirst);
        hc.Add(staff.StaffAffinity);
        hc.Add(staff.PerStaffKeySignature);
        hc.Add(staff.IsTextRow);
        hc.Add(staff.TextRowVerses);
        hc.Add(staff.IsLyricsTextRow);
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

    private static List<long>[] BucketSideTables(Score score, int measureCount)
    {
        var buckets = new List<long>[measureCount];
        for (int i = 0; i < measureCount; i++)
            buckets[i] = new List<long>();

        // Single-measure tables: each item belongs to one measure (item.MeasureIndex).
        // Fixed call order keeps the per-bucket fold deterministic.
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

        // Span tables: an item covers [StartMeasureIndex, EndMeasureIndex]; fold it
        // into every covered measure with its relative role so the key is
        // position-independent (no absolute indices).
        BucketSpan(score.VoltaBrackets, buckets);
        BucketSpan(score.TrillSpanners, buckets);

        return buckets;
    }

    private static List<long>[] BucketSideTables(MultiStaffScore score, int measureCount)
    {
        var buckets = new List<long>[measureCount];
        for (int i = 0; i < measureCount; i++)
            buckets[i] = new List<long>();

        // Same tables as the Score overload, by MeasureIndex across all staves.
        // (Tremolo has no side table anywhere — it lives on the note item as
        // TremoloBeams, already folded via the intrinsic key.)
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

    private static void BucketSingle(IEnumerable items, List<long>[] buckets)
    {
        foreach (var item in items)
        {
            int mi = GetInt(item, "MeasureIndex");
            if (mi >= 0 && mi < buckets.Length)
                buckets[mi].Add(HashContent(item, SideExclusions));
        }
    }

    private static void BucketSpan(IEnumerable items, List<long>[] buckets)
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
                var hc = new Hash64();
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

    // A property getter (or element enumeration) that throws must not crash key
    // computation — but folding a CONSTANT in its place would make the key BLIND to
    // whatever state sits behind the throwing getter: two DIFFERENT states folding
    // the same value is under-sensitivity, the false-reuse direction the soundness
    // bias forbids. So the key is POISONED instead: a process-unique value is folded,
    // making this compute's key equal to no other, and the throwing item simply
    // defeats reuse (a missed reuse — the safe direction) instead of enabling it.
    // (The one deliberate exception to the class-remark determinism: a poisoned key
    // is unstable by design.)
    private static int _getterPoison;

    // Only ordinary getter failures are poisoned. Process-fatal or flow-control
    // exceptions must PROPAGATE: turning an OOM, a thread interrupt, or a
    // cooperative cancellation into a hash value would silently convert a hard
    // failure into a permanent reuse-miss slow path. (StackOverflowException is
    // uncatchable on .NET Core; listed for intent.)
    private static bool IsPoisonable(Exception ex) => ex is not (
        OutOfMemoryException or StackOverflowException
        or ThreadInterruptedException or OperationCanceledException);

    private static long HashContent(object item, HashSet<string> excluded)
    {
        var hc = new Hash64();
        hc.Add(item.GetType());                       // discriminate kinds
        foreach (var get in Getters(item.GetType(), excluded))
        {
            try { AddValue(ref hc, get(item)); }
            catch (Exception ex) when (IsPoisonable(ex))
            {
                hc.Add(Interlocked.Increment(ref _getterPoison));
            }
        }
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

    private static void AddValue(ref Hash64 hc, object? value)
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

    // A 64-bit FNV-1a fold. Composed sub-hashes (long) and strings fold at full
    // 64-bit width, and int-sized leaves (int/enum/bool/char) fold losslessly, so
    // those never collide below the accumulator's ~2⁻⁶⁴. Every OTHER element
    // contributes its own 32-bit GetHashCode (per-process randomized, like
    // System.HashCode) — records/structs (MeasureContext) and doubles keep that
    // hash's ~2⁻³² collision odds, which is therefore the effective per-leaf bound
    // (see the class remarks). The `constrained.` call to GetHashCode() means
    // value-type elements (enums, records, MeasureContext) fold without boxing.
    private struct Hash64
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _acc;

        public Hash64() => _acc = Offset;

        public void Add<T>(T value)
        {
            Fold(value is null ? 0u : unchecked((uint)value.GetHashCode()));
        }

        // Folds both halves, so a composed 64-bit sub-hash (HashContent, BucketSpan)
        // is not collapsed to lo^hi by Int64.GetHashCode on its way into the key.
        public void Add(long value)
        {
            unchecked
            {
                Fold((uint)value);
                Fold((uint)((ulong)value >> 32));
            }
        }

        // Folds the chars directly (full width, not the 32-bit string hash), closed
        // by a length terminator whose high bit no UTF-16 unit can carry — so
        // adjacent strings cannot re-bracket ("ab"+"c" vs "a"+"bc") and "" stays
        // distinct from null (which folds a bare 0 via Add<T>).
        public void Add(string? value)
        {
            if (value is null)
            {
                Fold(0u);
                return;
            }
            foreach (char c in value)
                Fold(c);
            Fold(unchecked((uint)value.Length | 0x8000_0000u));
        }

        private void Fold(uint h) => _acc = unchecked((_acc ^ h) * Prime);

        public readonly long ToHashCode() => unchecked((long)_acc);
    }
}
