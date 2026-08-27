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

        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            var measures = staff.PrimaryVoice.Measures;
            var chain = MeasureContextChain.Compute(
                measures, new MeasureContext(score.KeySignature, score.TimeSignature, staff.Clef));
            int m = Math.Min(n, measures.Length);
            for (int i = 0; i < m; i++)
            {
                acc[i].Add(staffIndex);                 // discriminate which staff
                AddStaffIdentity(ref acc[i], staff);    // per-staff (indent/name/tuning/…)
                AddGroupIdentity(ref acc[i], group);    // ...and which brace/bracket it is in
                AddIntrinsic(ref acc[i], measures[i]);
                acc[i].Add(chain.Entry[i]);

                // A clef change opening measure i+1 is engraved BEFORE the bar line the
                // two measures share, so its width is charged to measure i's CLOSING
                // spring (SpacingRules.BoundaryClefAllowance — applied by both spring
                // systems and by the break gate). Exactly like the run membership folded
                // below, that width is decided by the NEIGHBOURING measure and so cannot
                // be recovered from measure i's own intrinsic hash: without folding it,
                // a system ENDING at measure i keeps its whole key slice when i+1 gains
                // or loses a clef, and the per-system cache hands back a layout with no
                // room reserved for that clef.
                // LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders — the
                // unbroken order puts `clef` before `staff-bar`.
                acc[i].Add(Layout.SpacingRules.BoundaryClefAllowance(
                    measures[i].EndBarline,
                    i + 1 < measures.Length ? measures[i + 1] : null));
            }

            // SECONDARY voices (polyphony within the staff). They occupy the same
            // measure indices and feed the same union columns the spring solve is
            // built from (SystemBreaker.ComputeMultiStaffSpringData collects every
            // voice), so their content belongs in the per-measure key just as the
            // primary's does — otherwise an edit confined to voice 2 leaves the key
            // unchanged and reuse hands back stale voice-2 geometry.
            //
            // The ENTRY CONTEXT is deliberately not recomputed per voice: clef / key /
            // time are staff-level, established by the primary stream, and the chain
            // above already folds them. Only the voice's own measure content is added,
            // discriminated by voice index so two voices holding identical measures
            // cannot cancel out.
            for (int v = 1; v < staff.Voices.Length; v++)
            {
                var voiceMeasures = staff.Voices[v].Measures;
                int mv = Math.Min(n, voiceMeasures.Length);
                for (int i = 0; i < mv; i++)
                {
                    acc[i].Add(v);                      // discriminate which voice
                    AddIntrinsic(ref acc[i], voiceMeasures[i]);
                }
            }
        }

        // A measure's WIDTH depends on its multi-measure-rest run membership: a run
        // collapses to ONE bar carrying a count-dependent rod, so the measures it
        // swallows go to zero width. That membership is decided by the NEIGHBOURING
        // measures, not by this measure's own content, so it cannot be recovered from
        // the intrinsic hash and has to be folded in explicitly — otherwise incremental
        // reuse could hand a rested bar a width computed for a different run.
        // LILYPOND-REF: lily/multi-measure-rest.cc:341-391 calculate_spacing_rods.
        var runMap = Layout.MmrRunMap.ForScore(score);
        for (int i = 0; i < n; i++)
        {
            acc[i].Add(runMap.IsInterior(i));
            acc[i].Add(runMap.TryGetRunStartingAt(i, out var run) ? run.Count : 0);
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

    /// <summary>Returns the hash rendered as a fixed-width hex string.</summary>
    public override string ToString() => $"mck:{Hash:x16}";

    /// <summary>Content fold of one side-table item under the side-table exclusion set
    /// (source offset and absolute measure indices stripped) — exposed for the
    /// beam-detection memo, which keys a measure's tuplet brackets with the SAME
    /// spelling the per-measure side-table buckets use.</summary>
    internal static long HashSideContent(object item) => HashContent(item, SideExclusions);

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

    /// <summary>
    /// Which GROUP the staff is engraved in — the part of the score's shape that is not
    /// visible in any staff's own fields or in any measure's content.
    /// </summary>
    /// <remarks>
    /// ⚠️ WITHOUT THIS THE KEY CANNOT SEE A STAFF LEAVING OR JOINING A BRACE, and the
    /// whole-layout reuse in <c>IncrementalCompiler</c> hands back the previous picture.
    /// Reported 2026-08-04 from the preview: commenting a staff out of a
    /// <c>grandStaff { }</c> and back in left the brace spanning three staves with four
    /// drawn. The intermediate state is what does it — a half-typed <c>/</c> closes the
    /// group early, so the staff moves OUT of the brace while every staff's identity and
    /// every measure's content stay exactly as they were, and the key does not move.
    /// Measured on the reported file: the brace stayed at y=24.69 where a full compile
    /// puts it at y=30.98.
    /// <para>
    /// Type AND size, because the two failures are different: the staves REMAINING in a
    /// shrinking group see the count change, and the staff that LEFT it sees the type
    /// change (a lone staff is its own single group). Folding only one of them leaves the
    /// other direction blind.
    /// </para>
    /// </remarks>
    private static void AddGroupIdentity(ref Hash64 hc, StaffGroup group)
    {
        hc.Add((int)group.Type);
        hc.Add(group.StaffCount);
    }

    private static void AddStaffIdentity(ref Hash64 hc, Staff staff)
    {
        // Staff-level fields that affect the staff's layout/render but are constant
        // across its measures: the instrument name (drives the system indent and the
        // drawn label), tab tuning, ossia scaling, hara-kiri visibility, staff
        // affinity, per-staff key signature, and the text-row band. Clef and the
        // measures of EVERY voice are already captured by the caller (entry context +
        // AddIntrinsic per voice), so nothing voice-related belongs here.
        hc.Add(staff.InstrumentName);
        hc.Add(staff.Tuning);
        hc.Add(staff.IsOssia);
        hc.Add(staff.RemoveEmpty);
        hc.Add(staff.RemoveFirst);
        hc.Add(staff.StaffAffinity);
        hc.Add(staff.PerStaffKeySignature);
        // ⚠️ Staff.ClefPosition is deliberately NOT folded in. It is a SOURCE OFFSET, not
        // content: it changes whenever text is inserted earlier in the file, so hashing it
        // would make a content-unchanged edit miss the cache and defeat whole-layout reuse
        // (measured — it broke ContentUnchangedEdit_ReusesWholeLayout_AndMatchesFull and
        // three of its siblings). It cannot go stale on reuse either, because the renderer
        // reads it off the LIVE score's staff at draw time; only the geometry is cached.
        // The canary in IncrementalReuseSoundnessTests records the same triage.
        hc.Add(staff.IsTextRow);
        hc.Add(staff.TextRowVerses);
        hc.Add(staff.IsLyricsTextRow);
        hc.Add(staff.TabSourceClef);
        hc.Add(staff.Transposition);
        hc.Add(staff.TabNumbersOnly);
        hc.Add(staff.Lines);
        hc.Add((int)staff.PedalStyle);

        // The part combiner's labels. They are a function of the two parts that went INTO
        // the combination, not of the voices that came out, so the voices' own hashes do
        // not stand in for them: an edit can change which part carries a shared passage —
        // and so whether "a2" prints — while leaving the engraved notes identical.
        // Guarded rather than folded unconditionally because this runs per staff per
        // measure on every keystroke, and every staff but a combinedStaff has none.
        if (staff.PartCombineMarks.IsDefaultOrEmpty)
            return;
        foreach (var mark in staff.PartCombineMarks)
        {
            hc.Add(mark.MeasureIndex);
            hc.Add(mark.ItemIndex);
            hc.Add(mark.Text);
        }
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
        // A pedal bracket SPANS measures its own marks are not in: the skyline seed puts
        // its ink into every system the span crosses (PedalEngraver.SolveAndSeed), so
        // deleting the RELEASE — whose mark sits in the LAST measure — must invalidate
        // the middle and start too, or a cached system keeps the bracket's ink under its
        // lyrics after the bracket is gone (MEASURED: the page height went stale;
        // IncrementalCompilerTests.DeletingAPedalRelease_RedrawsTheSystemsTheBracketSpanned).
        // Re-derived rather than read from a side table because the spans are a pure,
        // deterministic function of the marks — the same argument
        // PedalBracketLayout.SourceIndex makes for whole-layout reuse.
        BucketSpan(Svg.Layout.PedalEngraver.DetectPedalBrackets(score.MusicMarks), buckets);

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
        // The pedal SPANS, for the reason the Score overload gives above.
        BucketSpan(Svg.Layout.PedalEngraver.DetectPedalBrackets(score.MusicMarks), buckets);

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
            // One fold per ITEM, not per covered measure: the item's content hash is
            // the same at every mi (only the role varies), and a whole-book spanner
            // (trill/pedal/volta over M measures) was re-folding it M times per
            // keystroke (2026-08-26 review, finding 1-3). The composed value per
            // measure is unchanged — role and content enter the Hash64 as before.
            long content = HashContent(item, SideExclusions);
            for (int mi = start; mi <= end; mi++)
            {
                if (mi < 0 || mi >= buckets.Length)
                    continue;
                // Relative role: 0=only, 1=start, 2=middle, 3=end. Position-independent.
                int role = start == end ? 0 : mi == start ? 1 : mi == end ? 3 : 2;
                var hc = new Hash64();
                hc.Add(role);
                hc.Add(content);
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
    // ⚠️ AND THE COMPILED GETTER RETURNED object?, WHICH BOXED EVERY VALUE-TYPED PROPERTY —
    // one allocation per property per item per measure per keystroke. MEASURED (session 192,
    // perf-plain1k): AddIntrinsic cost 7.32 MB of a 54.1 MB keystroke, 7.3 KB per measure to
    // fold eight notes. So a property whose type can be hashed WITHOUT the box gets a
    // Func<object,int> that calls the value's own GetHashCode directly; everything AddValue
    // has to look at as an object (strings, sequences, ChordNoteInfo, reference types, and
    // any struct that does not override GetHashCode) keeps the old path.
    // ⚠️ THE FOLDED BITS DO NOT MOVE. Add<T> folds (uint)value.GetHashCode(), and the boxed
    // path folded (uint)box.GetHashCode() — the same call on the same value — so the direct
    // path hands hc.Add(int) exactly the number the box would have produced (Int32.GetHashCode
    // is the identity). Enums go through their UNDERLYING type, which is what Enum.GetHashCode
    // returns anyway; a struct is taken only when GetHashCode is declared on the struct
    // ITSELF, so no case falls through to ValueType.GetHashCode (which would box regardless).
    private readonly record struct PropertyFold(
        Func<object, int>? Direct, Func<object, object?>? Boxed);

    private static readonly ConcurrentDictionary<Type, PropertyFold[]> ItemGetters = new();
    private static readonly ConcurrentDictionary<Type, PropertyFold[]> SideGetters = new();
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
        foreach (var fold in Getters(item.GetType(), excluded))
        {
            try
            {
                if (fold.Direct is { } direct)
                    hc.Add(direct(item));
                else
                    AddValue(ref hc, fold.Boxed!(item));
            }
            catch (Exception ex) when (IsPoisonable(ex))
            {
                hc.Add(Interlocked.Increment(ref _getterPoison));
            }
        }
        return hc.ToHashCode();
    }

    private static PropertyFold[] Getters(Type type, HashSet<string> excluded)
    {
        var cache = ReferenceEquals(excluded, ItemExclusions) ? ItemGetters : SideGetters;
        return cache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead
                            && p.GetIndexParameters().Length == 0
                            && !excluded.Contains(p.Name))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(CompileFold)
                .ToArray());
    }

    /// <summary>
    /// Every property this type folds WITHOUT boxing, and whether the number it folds is the
    /// number the boxed path would have folded. The equation the no-box path rests on, at the
    /// level the equation is stated — a corpus A/B cannot see a hash that changed but stayed
    /// equally discriminating, and that is exactly the failure that would go unnoticed until
    /// a book collided (HANDOFF RULES §5.3).
    /// </summary>
    /// <returns>One entry per property taken directly: its name and whether the two folds
    /// agree on this item.</returns>
    internal static IReadOnlyList<(string Property, bool Agrees)> DirectFoldReport(object item)
    {
        var report = new List<(string, bool)>();
        foreach (var p in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead
                                 && p.GetIndexParameters().Length == 0
                                 && !ItemExclusions.Contains(p.Name))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (CompileDirectHash(p) is not { } direct) continue;
            object? boxed = CompileGetter(p)(item);
            // What AddValue folds for these types: hc.Add(0) for a null (a Nullable with no
            // value), otherwise hc.Add(object) — which is (uint)value.GetHashCode().
            int viaBox = boxed is null ? 0 : boxed.GetHashCode();
            report.Add((p.Name, direct(item) == viaBox));
        }
        return report;
    }

    /// <summary>Whether this type's property would be folded through the boxing path —
    /// asserted by the net for the three types <see cref="AddValue"/> reads specially.</summary>
    internal static bool FoldsThroughTheObjectPath(Type declaring, string property)
    {
        var p = declaring.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
        return p != null && CompileDirectHash(p) == null;
    }

    private static PropertyFold CompileFold(PropertyInfo p) =>
        CompileDirectHash(p) is { } direct
            ? new PropertyFold(direct, null)
            : new PropertyFold(null, CompileGetter(p));

    /// <summary>
    /// <c>o =&gt; ((TDeclaring)o).Prop.GetHashCode()</c> with no box, or null when the
    /// property's type is one <see cref="AddValue"/> must see as an object.
    /// </summary>
    /// <remarks>
    /// The three exclusions are the three cases <see cref="AddValue"/> treats specially, and
    /// they are exclusions of MEANING, not of speed: a string is folded char by char, an
    /// <see cref="IEnumerable"/> element by element, and a <c>ChordNoteInfo</c> with its
    /// source position normalised away — taking any of them by GetHashCode would change what
    /// the key sees, and for ChordNoteInfo it would make the key POSITION-DEPENDENT.
    /// </remarks>
    private static Func<object, int>? CompileDirectHash(PropertyInfo p)
    {
        var t = p.PropertyType;
        if (!t.IsValueType
            || typeof(IEnumerable).IsAssignableFrom(t)
            || t == typeof(ChordNoteInfo)
            || Nullable.GetUnderlyingType(t) == typeof(ChordNoteInfo))
            return null;

        var o = Expression.Parameter(typeof(object), "o");
        Expression value = Expression.Property(Expression.Convert(o, p.DeclaringType!), p);
        // An enum's own GetHashCode is Enum's, which would box; it returns the underlying
        // value's hash, so read that instead and the folded number is the same.
        if (t.IsEnum)
            value = Expression.Convert(value, Enum.GetUnderlyingType(t));

        var hash = value.Type.GetMethod(nameof(GetHashCode), Type.EmptyTypes);
        // Declared on the type itself = a non-virtual call on the value. Anything inherited
        // (ValueType.GetHashCode) would box on the way in, so leave it to the object path.
        if (hash == null || hash.DeclaringType != value.Type)
            return null;

        return Expression.Lambda<Func<object, int>>(Expression.Call(value, hash), o).Compile();
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
            case ChordNoteInfo cn:                     // nested chord member: its content
                // GetHashCode would fold the position-dependent source offset, which the
                // top-level ItemExclusions strips but cannot reach inside ChordItem.Notes.
                // Normalize it out so the key stays position-independent (matches noteheads).
                hc.Add(cn with { SourcePosition = -1 });
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
    // Internal (not private): SvgSystemFragmentCache folds its geometry key with the
    // SAME accumulator, so "64-bit FNV equality decides reuse" stays one spelling
    // with one collision-bound argument.
    internal struct Hash64
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
