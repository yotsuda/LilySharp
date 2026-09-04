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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Tablature;

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
    /// <remarks>
    /// ⚠️ A TAB STAFF'S BUCKET IS EMPTY, and that is this table's job rather than each
    /// reader's: a tab staff blanks DynamicText and TextScript, so it reserves no room for
    /// them either (see <see cref="TabStaffStencils"/>). Every reader of this bucket is a
    /// RESERVATION — <c>MultiStaffLayouter.BuildAllStaffSkylines</c> — so the blanking
    /// belongs where the slice is cut. The score's own <c>Dynamics</c> table is untouched:
    /// it still drives MIDI velocity and the exporters, and
    /// <c>SharedRenderer.ResolveDataPos</c> indexes it BY POSITION.
    /// </remarks>
    internal static IndexBuckets<DynamicItem> DynamicsByStaff(MultiStaffScore score)
        => score.Dynamics.IsDefaultOrEmpty
            ? IndexBuckets<DynamicItem>.Empty
            : _dynamicsByScore.GetValue(score,
                s => IndexBuckets<DynamicItem>.Build(
                    TabStaffStencils.Blank(s, s.Dynamics, static d => d.StaffIndex),
                    d => d.StaffIndex));

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
    /// <para>
    /// ⚠️ A TAB STAFF'S BUCKET IS EMPTY, for the reason <see cref="DynamicsByStaff"/>
    /// gives: a tab staff blanks TextSpanner, so it reserves no band above itself for one
    /// (<see cref="TabStaffStencils"/>). This is the reservation half of the defect —
    /// a `staff`+`tab` score's SECOND rit. was reserving a band above the tab line, and
    /// with a chord row between the two staves that band landed inside the notation staff.
    /// </para>
    /// </remarks>
    internal static IndexBuckets<TextSpannerItem> TextSpannersByStaff(MultiStaffScore score)
        => score.MusicMarks.IsDefaultOrEmpty
            ? IndexBuckets<TextSpannerItem>.Empty
            : _textSpannersByScore.GetValue(score,
                s => IndexBuckets<TextSpannerItem>.Build(
                    TabStaffStencils.Blank(
                        s, TextSpannerEngraver.DetectTextSpanners(s.MusicMarks),
                        static t => t.StaffIndex),
                    t => t.StaffIndex));

    // ---- BAR-keyed facts the spring builders read for EVERY bar, from both the break
    // gate and the layout (one instance each, §5.4's one-list rule).

    // Arrays, not ImmutableArray: the weak table wants a reference type. Neither is
    // handed out for writing — every reader indexes.
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, bool[]> _usedBarsByScore = new();
    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, Fraction[]> _metersByScore = new();

    /// <summary>
    /// Per bar index: whether ANY grob stands in a column of that bar — a note, chord or
    /// drawn rest in any voice of any staff, a chord symbol, a lyric syllable, a dynamic,
    /// a script, a text, a tuplet bracket, a bass figure, a grace, or a beat slash. A bar
    /// with none is an EMPTY BAR (<see cref="SpacingRules.EmptyBarSprings"/>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc:115-136 Paper_column::is_used — a column is used
    ///   by the grobs it holds (<c>elements</c>), whatever their kind; the reader of this
    ///   table is the spacing problem, which drops every unused column
    ///   (lily/system.cc:751-777 System::used_columns_in_range).
    /// The side tables are the grobs Lily# hangs on a bar's columns rather than on a note;
    /// a measure-wide percent sign is NOT one of them — LilyPond's PercentRepeat is a
    /// spanner bounded by the BAR-LINE columns and DoublePercentRepeat is an item ON the
    /// bar line (scm/define-grobs.scm:1290-1309 DoublePercentRepeat break-align-symbol
    /// staff-bar) — while a beat slash is a RepeatSlash grob in the musical column
    /// (scm/define-grobs.scm:2909-2918 RepeatSlash rhythmic-grob-interface). Rehearsal
    /// marks, tempo marks and volta brackets live on the bar-line column too and so are
    /// not read here.
    /// </remarks>
    internal static IReadOnlyList<bool> UsedBars(MultiStaffScore score)
        => _usedBarsByScore.GetValue(score, ComputeUsedBars);

    private static bool[] ComputeUsedBars(MultiStaffScore score)
    {
        int n = score.MeasureCount;
        var used = new bool[n];
        foreach (var (_, staff, _) in score.EnumerateStaves())
            foreach (var voice in staff.Voices)
            {
                int m = System.Math.Min(n, voice.Measures.Length);
                for (int i = 0; i < m; i++)
                    if (!used[i] && !SpacingRules.BarHoldsOnlySkips(voice.Measures[i].Items))
                        used[i] = true;
            }

        void Mark(int measureIndex)
        {
            if (measureIndex >= 0 && measureIndex < n)
                used[measureIndex] = true;
        }
        foreach (var it in score.Lyrics) Mark(it.MeasureIndex);
        foreach (var it in score.ChordNames) Mark(it.MeasureIndex);
        foreach (var it in score.Dynamics) Mark(it.MeasureIndex);
        foreach (var it in score.Articulations) Mark(it.MeasureIndex);
        foreach (var it in score.CustomTexts) Mark(it.MeasureIndex);
        foreach (var it in score.TupletBrackets) Mark(it.MeasureIndex);
        foreach (var it in score.FiguredBasses) Mark(it.MeasureIndex);
        foreach (var it in score.GraceNotes) Mark(it.MeasureIndex);
        foreach (var it in score.PercentRepeats)
            if (it.IsBeatSlash)
                Mark(it.MeasureIndex);
        return used;
    }

    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, double[]> _doublePercentHalfWidthsByScore = new();

    /// <summary>
    /// Per bar index: half the ink width of the widest DOUBLE percent sign centred on the
    /// bar line that OPENS that bar (the line between the two bars of a `%%` pair, on which
    /// LilyPond break-aligns the sign), 0 where none stands. A sign on a tab staff is
    /// one-and-a-half-sized, and the column's skyline is the union over the staves, so the
    /// widest wins.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1290-1309 DoublePercentRepeat break-align-symbol staff-bar.
    ///   The item's MeasureIndex is the pair's SECOND measure, so the sign stands on that
    ///   measure's opening bar line (<see cref="PercentRepeatItem"/>).
    /// </remarks>
    internal static IReadOnlyList<double> DoublePercentHalfWidths(MultiStaffScore score)
        => _doublePercentHalfWidthsByScore.GetValue(score, ComputeDoublePercentHalfWidths);

    private static double[] ComputeDoublePercentHalfWidths(MultiStaffScore score)
    {
        int n = score.MeasureCount;
        var half = new double[n];
        if (score.PercentRepeats.IsDefaultOrEmpty)
            return half;
        // The staff space each global staff index draws in: 1 for notation, the string
        // spacing for a tab (the same reading SharedRenderer.DrawPercentRepeats makes).
        var staffSpace = new Dictionary<int, double>();
        foreach (var (_, staff, index) in score.EnumerateStaves())
            staffSpace[index] = staff.Tuning is { } tuning
                ? EngravingDefaults.TabStringSpace(Tunings.GetStringCount(tuning))
                : 1.0;
        foreach (var pr in score.PercentRepeats)
        {
            if (!pr.IsDouble || pr.MeasureIndex < 0 || pr.MeasureIndex >= n)
                continue;
            double ss = staffSpace.TryGetValue(pr.StaffIndex, out var s) ? s : 1.0;
            double h = PercentRepeatEngraver.DoublePercentInkWidth(ss) / 2;
            if (h > half[pr.MeasureIndex])
                half[pr.MeasureIndex] = h;
        }
        return half;
    }

    /// <summary>
    /// The meter's bar length in force at each bar index — LilyPond's <c>measure-length</c>,
    /// stamped on the first command column of every bar (the citation is on
    /// <see cref="SpacingRules.StandardBreakableColumnSpacing"/>, which reads it) — memoized
    /// per score. The same walk <see cref="SpacingRules.CalculateCommonShortestDuration(MultiStaffScore)"/>
    /// takes for its full-measure-rest test, cut once.
    /// </summary>
    internal static IReadOnlyList<Fraction> PrevailingMeters(MultiStaffScore score)
        => _metersByScore.GetValue(score, s =>
            MultiMeasureRestEngraver.PrevailingMeters(
                s.AllVoices.Select(v => v.Measures).ToList(), s.MeasureCount,
                s.TimeSignature.MeasureDuration));
}
