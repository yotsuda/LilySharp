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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A score-wide side table (lyrics, chord symbols, dynamics, …) bucketed by a
/// small integer key — a measure index or a global staff ordinal — the same
/// items in the same document order, reachable per key without walking the
/// whole table. Built once; <see cref="At"/> answers any key (out-of-range
/// reads are empty, so a caller may ask for measure −1 or one past the end
/// exactly as the full-scan filters used to answer them).
/// </summary>
/// <remarks>
/// The per-measure spring builders (gate and layout both) used to filter the
/// FULL table on every measure — <c>score.Lyrics</c> three times per sung
/// measure, <c>score.ChordNames</c> twice — and the per-system skyline pass
/// re-filtered dynamics/tuplets/scripts per (system, staff): an
/// O(units × table) full-render cost with no keystroke-path guard of its own
/// (~10⁷ item visits on a 1000-bar songbook). Bucketing is pure re-indexing:
/// each bucket preserves document order, so every consumer's scan order — and
/// with it list append order, engraver input order and Math.Max fold order —
/// is exactly the full scan's order restricted to the keys it kept.
/// </remarks>
internal sealed class IndexBuckets<T>
{
    private readonly ImmutableArray<T>[] _byKey;
    private readonly int _firstKey;

    public static readonly IndexBuckets<T> Empty = new(System.Array.Empty<ImmutableArray<T>>(), 0);

    private IndexBuckets(ImmutableArray<T>[] byKey, int firstKey)
    {
        _byKey = byKey;
        _firstKey = firstKey;
    }

    /// <summary>All items whose key is <paramref name="key"/>, in document order;
    /// empty for any key the table has no item on.</summary>
    public ImmutableArray<T> At(int key)
    {
        int i = key - _firstKey;
        return i >= 0 && i < _byKey.Length ? _byKey[i] : ImmutableArray<T>.Empty;
    }

    /// <summary>
    /// Buckets <paramref name="items"/> by <paramref name="keyOf"/>. The bucket
    /// range spans the observed min..max key (whatever those are — no
    /// non-negativity is assumed), so no item is dropped.
    /// </summary>
    public static IndexBuckets<T> Build(IReadOnlyList<T> items, System.Func<T, int> keyOf)
    {
        if (items.Count == 0)
            return Empty;

        int min = int.MaxValue, max = int.MinValue;
        foreach (var item in items)
        {
            int k = keyOf(item);
            if (k < min) min = k;
            if (k > max) max = k;
        }

        var builders = new ImmutableArray<T>.Builder?[max - min + 1];
        foreach (var item in items)
            (builders[keyOf(item) - min] ??= ImmutableArray.CreateBuilder<T>()).Add(item);

        var buckets = new ImmutableArray<T>[builders.Length];
        for (int i = 0; i < builders.Length; i++)
            buckets[i] = builders[i]?.ToImmutable() ?? ImmutableArray<T>.Empty;
        return new IndexBuckets<T>(buckets, min);
    }
}

/// <summary>
/// The per-score memo of the side-table buckets — one construction per score,
/// shared by every consumer. The break gate and the system layout MUST read the
/// same instance (RULES §5.4's one-list rule: a bar must be priced for breaking
/// exactly as it will be laid out), which the score-keyed memo makes structural,
/// the same way <see cref="LyricSpacing.OwnVoiceEdgeProvider"/> already shares
/// the alignment-edge table.
/// </summary>
internal static class ScoreSideTables
{
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, IndexBuckets<LyricItem>> _lyricsByScore = new();
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, IndexBuckets<ChordNameItem>> _chordsByScore = new();

    /// <summary>The score's lyrics bucketed by measure (memoized per score).</summary>
    internal static IndexBuckets<LyricItem> Lyrics(MultiStaffScore score)
        => score.Lyrics.IsDefaultOrEmpty
            ? IndexBuckets<LyricItem>.Empty
            : _lyricsByScore.GetValue(score, s => BucketLyrics(s.Lyrics));

    /// <summary>The one bucketing spelling for lyrics — the memo above and any
    /// direct (test) construction share it.</summary>
    internal static IndexBuckets<LyricItem> BucketLyrics(IReadOnlyList<LyricItem> lyrics)
        => IndexBuckets<LyricItem>.Build(lyrics, ly => ly.MeasureIndex);

    /// <summary>The score's chord symbols bucketed by measure (memoized per score).</summary>
    internal static IndexBuckets<ChordNameItem> ChordNames(MultiStaffScore score)
        => score.ChordNames.IsDefaultOrEmpty
            ? IndexBuckets<ChordNameItem>.Empty
            : _chordsByScore.GetValue(score, s => BucketChordNames(s.ChordNames));

    /// <summary>The one bucketing spelling for chord symbols, as above.</summary>
    internal static IndexBuckets<ChordNameItem> BucketChordNames(IReadOnlyList<ChordNameItem> chordNames)
        => IndexBuckets<ChordNameItem>.Build(chordNames, cn => cn.MeasureIndex);

    // ---- STAFF-keyed buckets: the per-(system, staff) skyline pass reads a staff's
    // own slice of these tables on every system, so the slice is cut once per score.
    // Stable (document-order) bucketing on purpose: the engravers' input order —
    // and through it the merge order the 4878-ULP lesson pinned — is the document's.

    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, IndexBuckets<DynamicItem>> _dynamicsByScore = new();
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, IndexBuckets<TupletBracketItem>> _tupletsByScore = new();
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, IndexBuckets<ArticulationItem>> _articulationsByScore = new();
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, IndexBuckets<TextSpannerItem>> _textSpannersByScore = new();

    /// <summary>The score's dynamics bucketed by global staff index (memoized per score).</summary>
    internal static IndexBuckets<DynamicItem> DynamicsByStaff(MultiStaffScore score)
        => score.Dynamics.IsDefaultOrEmpty
            ? IndexBuckets<DynamicItem>.Empty
            : _dynamicsByScore.GetValue(score,
                s => IndexBuckets<DynamicItem>.Build(s.Dynamics, d => d.StaffIndex));

    /// <summary>The score's tuplet brackets bucketed by global staff index (memoized per score).</summary>
    internal static IndexBuckets<TupletBracketItem> TupletBracketsByStaff(MultiStaffScore score)
        => score.TupletBrackets.IsDefaultOrEmpty
            ? IndexBuckets<TupletBracketItem>.Empty
            : _tupletsByScore.GetValue(score,
                s => IndexBuckets<TupletBracketItem>.Build(s.TupletBrackets, t => t.StaffIndex));

    /// <summary>The score's articulations bucketed by global staff index (memoized per score).</summary>
    internal static IndexBuckets<ArticulationItem> ArticulationsByStaff(MultiStaffScore score)
        => score.Articulations.IsDefaultOrEmpty
            ? IndexBuckets<ArticulationItem>.Empty
            : _articulationsByScore.GetValue(score,
                s => IndexBuckets<ArticulationItem>.Build(s.Articulations, a => a.StaffIndex));

    /// <summary>
    /// The score's accel./rit. SPANNERS — derived from the marks, not stored on the score —
    /// bucketed by global staff index (memoized per score).
    /// </summary>
    /// <remarks>
    /// ⚠️ DERIVED, WHICH IS WHY IT IS MEMOISED HERE RATHER THAN CALLED. The staff-skyline
    /// pass runs once per (system, staff) and needs one staff's spanners on every one of
    /// them; <c>TextSpannerEngraver.DetectTextSpanners</c> walks the WHOLE mark table and
    /// allocates, and `MusicMarks` is not a rare property — a score with a hundred `@text`
    /// annotations and no rit. at all would have paid that walk S×systems times on every
    /// keystroke. Cut once per score, an empty answer costs one dictionary hit.
    /// </remarks>
    internal static IndexBuckets<TextSpannerItem> TextSpannersByStaff(MultiStaffScore score)
        => score.MusicMarks.IsDefaultOrEmpty
            ? IndexBuckets<TextSpannerItem>.Empty
            : _textSpannersByScore.GetValue(score,
                s => IndexBuckets<TextSpannerItem>.Build(
                    TextSpannerEngraver.DetectTextSpanners(s.MusicMarks), t => t.StaffIndex));
}
