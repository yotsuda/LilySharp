// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/spanner.cc
//     Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
//   lily/system.cc
//     Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A piece of a spanner that has been split at one or more system breaks.
/// </summary>
/// <remarks>
/// Mirrors LilyPond's "broken piece" concept where a spanner is cloned per system
/// and the bounds are reattached to that system's edge columns.
/// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing clone + bound set
/// LILYPOND-REF: lily/item.cc:127-135 — Item::break_status_dir LEFT/RIGHT/CENTER
///
/// IsFirst / IsLast map to LP's break_status_dir():
///   - IsFirst &amp;&amp; IsLast  ⇔ original (CENTER) — single system, no split needed
///   - IsFirst &amp;&amp; !IsLast ⇔ left piece (RIGHT-cut, RIGHT bound at system end)
///   - !IsFirst &amp;&amp; IsLast ⇔ right piece (LEFT-cut, LEFT bound at system start)
///   - IsMiddle              ⇔ middle piece (both ends cut — only happens for 3+ systems)
/// </remarks>
public readonly record struct SpannerBreakSegment(
    int SystemIndex,
    int StartMeasureIndex,
    int EndMeasureIndex,
    bool IsFirst,
    bool IsLast,
    bool IsMiddle);

/// <summary>
/// Helpers for splitting a spanner that crosses one or more system breaks
/// into per-system pieces, mirroring LilyPond's break-substitution algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/break-substitution.cc:67-153 — substitute_grob &amp; do_break_substitution
/// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
/// LILYPOND-REF: lily/spanner.cc:356-372 — Spanner::find_broken_piece (rank-indexed lookup)
/// LILYPOND-REF: lily/system.cc:143-192 — do_break_substitution_and_fixup_refpoints
///
/// LilyPond clones each cross-line spanner once per system it touches and
/// reattaches its bounds to the system-edge Paper_columns. LilySharp does not
/// keep clones of spanner Items — instead, each engraver consults the segments
/// returned here and emits one Layout record per segment, preserving the
/// (StartMeasureIndex, IsFirst, IsLast) information so that broken-edge
/// rendering (cut-off / continuation) can decide its visuals from the segment.
/// </remarks>
internal static class SpannerBreakSubstitution
{
    /// <summary>
    /// Builds a measure-index → system-index lookup for the given systems.
    /// Engravers call this once and reuse the map across multiple spanner Splits.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system.cc:143-192 — fixup_refpoints walks all systems once.
    /// </remarks>
    public static IReadOnlyDictionary<int, int> BuildMeasureToSystemMap(
        ImmutableArray<SystemLayout> systems)
    {
        var arr = ImmutableCollectionsMarshal.AsArray(systems);
        if (arr is null || arr.Length == 0)
            return EmptyMeasureToSystem;
        // A consecutive array answers from itself (ConsecutiveMeasureMap's remarks carry the
        // account); the table below is for the arrays that are not.
        return (IReadOnlyDictionary<int, int>?)ConsecutiveMeasureMap.Of(arr)
            ?? MeasureToSystemMaps.GetValue(arr, BuildMeasureToSystemMapFor);
    }

    private static readonly Dictionary<int, int> EmptyMeasureToSystem = new();

    /// <summary>One table per system array, keyed on the ARRAY ITSELF.</summary>
    /// <remarks>
    /// ⚠️ THE TABLE IS SHARED AND READ-ONLY BY TYPE. Every caller of
    /// <see cref="BuildMeasureToSystemMap"/> now receives the SAME instance for the same
    /// systems, which is sound because the map is a pure function of the array (its only
    /// inputs are each <c>MeasureLayout.MeasureIndex</c> and its position in the array, and
    /// an <c>ImmutableArray</c>'s contents cannot change) and because
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> is what they are handed: the compiler,
    /// not a remark, is what keeps the sharing honest.
    /// <para>
    /// ⚠️ A <c>ConditionalWeakTable</c> AND NOT A SLOT, so the entry dies with the system
    /// array it belongs to — a two-slot cache would root two whole layouts past the
    /// keystroke that made them. RULES §5.3's warning about this structure ("a table made
    /// once per tree is paid every keystroke when the tree is new every keystroke") is about
    /// reading its claim, not about using it: the key here is ONE PASS's system array, which
    /// is exactly the scope the duplicate builds live in.
    /// </para>
    /// <para>
    /// MEASURED (session 422, the owner's 231 books × 8 forward keystrokes): this table and
    /// its twin <c>LayoutUtilities.BuildMeasureMap</c> were built 29,108 times over 1,848
    /// keystrokes — 15.75 times a keystroke, 0.755% of one — because sixteen houses each
    /// built their own. Session 420 had given ONE of them (MusicMarkEngraver) a door to a
    /// prebuilt map, which is the shape RULES §7.6 warns about: "N 箇所を 1 軒にした" is not
    /// "counted them all".
    /// </para>
    /// </remarks>
    private static readonly ConditionalWeakTable<SystemLayout[], Dictionary<int, int>>
        MeasureToSystemMaps = new();

    private static Dictionary<int, int> BuildMeasureToSystemMapFor(SystemLayout[] systems)
    {
        // SIZED, for the reason LayoutUtilities.BuildMeasureMapFor gives — this is the twin
        // the MEASURED paragraph above already names.
        int measures = 0;
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
            measures += systems[sysIdx].Measures.Length;

        var map = new Dictionary<int, int>(measures);
        for (int sysIdx = 0; sysIdx < systems.Length; sysIdx++)
        {
            foreach (var ml in systems[sysIdx].Measures)
                map[ml.MeasureIndex] = sysIdx;
        }
        return map;
    }

    /// <summary>
    /// Splits a spanner spanning [startMeasure, endMeasure] into per-system segments.
    /// Returns one segment per system the spanner touches.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
    /// LILYPOND-REF: lily/spanner.cc:356-372 — Spanner::find_broken_piece
    ///
    /// Pre: <paramref name="measureToSystemIdx"/> covers both spannerStartMeasure and
    /// spannerEndMeasure (built via <see cref="BuildMeasureToSystemMap"/>).
    /// Returns an empty array if either measure is missing from the map (defensive
    /// no-op for malformed inputs).
    ///
    /// Each segment carries (StartMeasureIndex, EndMeasureIndex) clamped to the
    /// touching system's measure range:
    /// - First segment: (spannerStartMeasure, last measure of startSys)
    /// - Middle segments: (first measure of sys, last measure of sys)
    /// - Last segment: (first measure of endSys, spannerEndMeasure)
    /// - Single-system spanner: (spannerStartMeasure, spannerEndMeasure), IsFirst=IsLast=true
    /// </remarks>
    public static ImmutableArray<SpannerBreakSegment> Split(
        int spannerStartMeasure,
        int spannerEndMeasure,
        ImmutableArray<SystemLayout> systems,
        IReadOnlyDictionary<int, int> measureToSystemIdx)
    {
        if (systems.IsDefaultOrEmpty)
            return ImmutableArray<SpannerBreakSegment>.Empty;

        if (!measureToSystemIdx.TryGetValue(spannerStartMeasure, out int startSys) ||
            !measureToSystemIdx.TryGetValue(spannerEndMeasure, out int endSys))
            return ImmutableArray<SpannerBreakSegment>.Empty;

        if (startSys > endSys)
            return ImmutableArray<SpannerBreakSegment>.Empty;

        // LILYPOND-REF: lily/spanner.cc:356-372 — single-system spanner returns the original (CENTER).
        if (startSys == endSys)
        {
            return ImmutableArray.Create(new SpannerBreakSegment(
                SystemIndex: startSys,
                StartMeasureIndex: spannerStartMeasure,
                EndMeasureIndex: spannerEndMeasure,
                IsFirst: true,
                IsLast: true,
                IsMiddle: false));
        }

        // LILYPOND-REF: lily/spanner.cc:124-137 — clone() per system; bounds clamped to system edges.
        var builder = ImmutableArray.CreateBuilder<SpannerBreakSegment>();
        for (int sysIdx = startSys; sysIdx <= endSys; sysIdx++)
        {
            bool isFirst = sysIdx == startSys;
            bool isLast = sysIdx == endSys;
            var sys = systems[sysIdx];

            int segStart = isFirst
                ? spannerStartMeasure
                : sys.Measures[0].MeasureIndex;
            int segEnd = isLast
                ? spannerEndMeasure
                : sys.Measures[^1].MeasureIndex;

            builder.Add(new SpannerBreakSegment(
                SystemIndex: sysIdx,
                StartMeasureIndex: segStart,
                EndMeasureIndex: segEnd,
                IsFirst: isFirst,
                IsLast: isLast,
                IsMiddle: !isFirst && !isLast));
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Iterates a spanner's broken pieces: <see cref="Split"/>s [start, end] into
    /// per-system segments and pairs each with its <see cref="SystemLayout"/>.
    /// Centralizes the split + empty-guard + system lookup that every spanner engraver
    /// repeats, so each one keeps only its own per-piece geometry. Walks nothing when
    /// the span can't be split (defensive no-op), so a <c>foreach</c> over it skips the
    /// body just like the old <c>if (segments.IsEmpty) continue;</c>.
    /// </summary>
    /// <remarks>
    /// A struct (<see cref="BrokenPieceList"/>) over Split's array rather than a <c>yield</c>
    /// wrapper: the wrapper was a 112 B iterator per spanner (session 446's census, 270 B a
    /// keystroke for the voltas alone over the reader's corpus) and paired nothing the
    /// array and an index cannot pair. Same segments, same order.
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing (one piece per system).
    /// </remarks>
    public static BrokenPieceList BrokenPieces(
        int spannerStartMeasure,
        int spannerEndMeasure,
        ImmutableArray<SystemLayout> systems,
        IReadOnlyDictionary<int, int> measureToSystemIdx)
        => new(Split(spannerStartMeasure, spannerEndMeasure, systems, measureToSystemIdx), systems);

    /// <summary>A spanner's pieces, each with the system it lies on — the value
    /// <see cref="BrokenPieces"/> hands out. <c>foreach</c> binds to <see cref="GetEnumerator"/>
    /// and allocates nothing.</summary>
    public readonly struct BrokenPieceList : IEnumerable<(SpannerBreakSegment Segment, SystemLayout System)>
    {
        private readonly ImmutableArray<SpannerBreakSegment> _segments;
        private readonly ImmutableArray<SystemLayout> _systems;

        internal BrokenPieceList(ImmutableArray<SpannerBreakSegment> segments, ImmutableArray<SystemLayout> systems)
        {
            _segments = segments;
            _systems = systems;
        }

        public Enumerator GetEnumerator() => new(_segments, _systems);

        IEnumerator<(SpannerBreakSegment Segment, SystemLayout System)>
            IEnumerable<(SpannerBreakSegment Segment, SystemLayout System)>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>First piece to last, each with <c>systems[segment.SystemIndex]</c>.</summary>
        public struct Enumerator : IEnumerator<(SpannerBreakSegment Segment, SystemLayout System)>
        {
            private readonly ImmutableArray<SpannerBreakSegment> _segments;
            private readonly ImmutableArray<SystemLayout> _systems;
            private int _i;

            internal Enumerator(ImmutableArray<SpannerBreakSegment> segments, ImmutableArray<SystemLayout> systems)
            {
                _segments = segments;
                _systems = systems;
                _i = -1;
            }

            public readonly (SpannerBreakSegment Segment, SystemLayout System) Current
                => (_segments[_i], _systems[_segments[_i].SystemIndex]);

            readonly object IEnumerator.Current => Current;

            public bool MoveNext() => ++_i < _segments.Length;

            public void Reset() => _i = -1;

            public readonly void Dispose() { }
        }
    }

    /// <summary>
    /// Reattaches a broken piece's X bounds to the system edges: the first piece
    /// keeps the spanner's own <paramref name="ownStartX"/> and the last piece its
    /// own <paramref name="ownEndX"/>; every cut edge snaps to the system's first
    /// measure's left / last measure's right. Shared by the engravers whose
    /// non-broken edge is exactly the system's outer measure bounds (Hairpin,
    /// Glissando); other spanners (TextSpanner/Trill/Ottava/Volta) reattach to
    /// their own padded item bounds and keep their own inline logic.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spanner.cc:124-137 — clone() per system; bounds clamped to system edges.</remarks>
    internal static (double StartX, double EndX) ReattachSpanX(
        SpannerBreakSegment segment, SystemLayout system, double ownStartX, double ownEndX)
    {
        double startX = segment.IsFirst ? ownStartX : system.Measures[0].X;
        var lastMeasure = system.Measures[^1];
        double endX = segment.IsLast ? ownEndX : lastMeasure.X + lastMeasure.Width;
        return (startX, endX);
    }
}
