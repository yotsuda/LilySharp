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

using System.Collections.Immutable;

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
public static class SpannerBreakSubstitution
{
    /// <summary>
    /// Builds a measure-index → system-index lookup for the given systems.
    /// Engravers call this once and reuse the map across multiple spanner Splits.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system.cc:143-192 — fixup_refpoints walks all systems once.
    /// </remarks>
    public static Dictionary<int, int> BuildMeasureToSystemMap(
        ImmutableArray<SystemLayout> systems)
    {
        var map = new Dictionary<int, int>();
        if (systems.IsDefaultOrEmpty)
            return map;

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
        Dictionary<int, int> measureToSystemIdx)
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
    /// per-system segments and yields each paired with its <see cref="SystemLayout"/>.
    /// Centralizes the split + empty-guard + system lookup that every spanner engraver
    /// repeats, so each one keeps only its own per-piece geometry. Yields nothing when
    /// the span can't be split (defensive no-op), so a <c>foreach</c> over it skips the
    /// body just like the old <c>if (segments.IsEmpty) continue;</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing (one piece per system).
    /// </remarks>
    public static IEnumerable<(SpannerBreakSegment Segment, SystemLayout System)> BrokenPieces(
        int spannerStartMeasure,
        int spannerEndMeasure,
        ImmutableArray<SystemLayout> systems,
        Dictionary<int, int> measureToSystemIdx)
    {
        foreach (var segment in Split(spannerStartMeasure, spannerEndMeasure, systems, measureToSystemIdx))
            yield return (segment, systems[segment.SystemIndex]);
    }
}
