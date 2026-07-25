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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Lyric-driven horizontal spacing: syllable text-width estimation and the spring
/// adjustments that keep adjacent lyric syllables from colliding. Extracted from
/// <see cref="SpacingRules"/>.
/// </summary>
internal static class LyricSpacing
{
    /// <summary>
    /// Widens an EXISTING spring chain so adjacent syllables don't collide: this
    /// post-processes the timing-column springs used by the multi-staff layouter,
    /// which is how every score gets its lyric-driven spacing (a single-staff score
    /// is promoted to a MultiStaffScore). SpacingRules used to carry a second,
    /// from-scratch builder for a single-staff path that no longer exists; it was
    /// removed rather than kept in step with the timing-column springs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:80-85 skyline-based min_distance.
    /// The spring chain is [start→col0, col0→col1, …, colLast→end]; for a
    /// single-voice measure the timing columns coincide with the note items, so
    /// spring i+1 spans item i → item i+1. When the column count does not match
    /// the item count (extra voices), the mapping breaks down and the chain is
    /// returned unchanged — lyrics are only engraved on single-voice staves.
    /// </remarks>
    public static ImmutableArray<Spring> ApplyLyricSpacing(
        ImmutableArray<Spring> springs,
        Measure measure,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics)
    {
        // The springs are built from TIMING COLUMNS. In a plain measure those columns
        // coincide with the note items, so the item-index reservation below lines up. But
        // when the bar opens with a non-note item (a mid-piece time/clef change) the item
        // slots no longer match the columns — the item-index chain would bail and leave the
        // syllables to crowd. Reserve by timing column instead.
        if (measure.Items.Length == 0 || springs.Length != measure.Items.Length + 1)
            return ReserveLyricWidthByColumn(springs, columnTimings, measureIndex, lyrics, _ => true);

        var lyricsByItem = new Dictionary<int, List<LyricItem>>();
        foreach (var lyric in lyrics)
        {
            if (lyric.MeasureIndex != measureIndex)
                continue;
            if (!lyricsByItem.TryGetValue(lyric.ItemIndex, out var list))
                lyricsByItem[lyric.ItemIndex] = list = new List<LyricItem>();
            list.Add(lyric);
        }
        if (lyricsByItem.Count == 0)
            return springs;

        var result = springs.ToBuilder();

        // First spring (start barline → item 0): reserve item 0's left extent.
        if (lyricsByItem.TryGetValue(0, out var firstLyrics))
        {
            var s0 = result[0];
            double adjustedMin = Math.Max(s0.MinDistance, GetLyricLeftExtent(firstLyrics) + GlyphMetrics.MinItemGap);
            result[0] = new Spring(Math.Max(s0.IdealDistance, adjustedMin), adjustedMin, s0.InverseStretchStrength);
        }

        // Between items: spring i+1 spans item i → item i+1.
        for (int i = 0; i < measure.Items.Length - 1; i++)
        {
            double lyricDistance = CalculateLyricDistance(
                lyricsByItem.GetValueOrDefault(i),
                lyricsByItem.GetValueOrDefault(i + 1));
            var spring = result[i + 1];
            if (lyricDistance > spring.MinDistance)
                result[i + 1] = new Spring(
                    Math.Max(spring.IdealDistance, lyricDistance),
                    lyricDistance, spring.InverseStretchStrength);
        }

        // Last spring (item last → end barline): reserve last item's right extent.
        int lastIndex = measure.Items.Length - 1;
        if (lyricsByItem.TryGetValue(lastIndex, out var lastLyrics))
        {
            var sl = result[^1];
            double adjustedMin = Math.Max(sl.MinDistance, GetLyricRightExtent(lastLyrics) + GlyphMetrics.MinItemGap);
            result[^1] = new Spring(Math.Max(sl.IdealDistance, adjustedMin), adjustedMin, sl.InverseStretchStrength);
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Lead-sheet variant: reserves lyric width against the TIMING COLUMNS the springs were
    /// built from, matching each syllable to its column by <see cref="LyricItem.Timing"/>.
    /// </summary>
    /// <remarks>
    /// On a lead sheet the chord and lyric rows subdivide a bar differently (e.g. chords on the
    /// half note, three syllables on thirds), so the column count is the UNION of both and does
    /// NOT equal the syllable count — the item-index <see cref="ApplyLyricSpacing"/> bails out on
    /// that mismatch and the syllables crowd (and overrun the barline). This maps each syllable to
    /// its column and reserves its ink across the springs SPANNING adjacent lyric columns (a
    /// chord-only column may sit between two syllables), plus the leading/trailing extents against
    /// the barlines. LILYPOND-REF: lily/separation-item.cc set_distance — syllable widths join the
    /// column springs the spacing-spanner solves.
    /// </remarks>
    public static ImmutableArray<Spring> ApplyLeadSheetLyricSpacing(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics)
        => ReserveLyricWidthByColumn(springs, columnTimings, measureIndex, lyrics, ly => ly.IsLyricsRow);

    /// <summary>
    /// Reserves lyric ink against the TIMING COLUMNS the springs were built from: each
    /// syllable matched to its column by <see cref="LyricItem.Timing"/>, its width spread
    /// across the springs spanning adjacent lyric columns (plus the leading/trailing extents
    /// against the barlines). <paramref name="include"/> selects which lyrics participate
    /// (row-only on a lead sheet; all on a staff). Robust to columns that carry no syllable
    /// (a chord-only column, or a bar's leading time/clef change).
    /// </summary>
    private static ImmutableArray<Spring> ReserveLyricWidthByColumn(
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics,
        System.Func<LyricItem, bool> include)
    {
        int cols = columnTimings.Count;
        // springs = [start→col0, col0→col1, …, colLast→end] → length == cols + 1.
        if (cols == 0 || springs.Length != cols + 1)
            return springs;

        var lyricsByCol = new Dictionary<int, List<LyricItem>>();
        foreach (var ly in lyrics)
        {
            if (ly.MeasureIndex != measureIndex || !include(ly))
                continue;
            int col = -1;
            for (int c = 0; c < cols; c++)
                if (columnTimings[c].Equals(ly.Timing)) { col = c; break; }
            if (col < 0)
                continue;
            if (!lyricsByCol.TryGetValue(col, out var list))
                lyricsByCol[col] = list = new List<LyricItem>();
            list.Add(ly);
        }
        if (lyricsByCol.Count == 0)
            return springs;

        var result = springs.ToBuilder();
        var lyricCols = lyricsByCol.Keys.OrderBy(c => c).ToList();

        // Leading extent: the first syllable clears the start barline.
        int firstCol = lyricCols[0];
        BumpSpanMin(result, 0, firstCol,
            GetLyricLeftExtent(lyricsByCol[firstCol]) + GlyphMetrics.MinItemGap);

        // Between consecutive syllables (across any note/chord-only columns in between).
        for (int p = 0; p + 1 < lyricCols.Count; p++)
        {
            int a = lyricCols[p], b = lyricCols[p + 1];
            BumpSpanMin(result, a + 1, b,
                CalculateLyricDistance(lyricsByCol[a], lyricsByCol[b]));
        }

        // Trailing extent: the last syllable clears the end barline.
        int lastCol = lyricCols[^1];
        BumpSpanMin(result, lastCol + 1, cols,
            GetLyricRightExtent(lyricsByCol[lastCol]) + GlyphMetrics.MinItemGap);

        return result.ToImmutable();
    }

    /// <summary>
    /// Ensures the total MIN distance of springs[<paramref name="from"/>..<paramref name="to"/>]
    /// (inclusive) is at least <paramref name="need"/>, adding any deficit to the last spring of
    /// the span so the right-hand column is pushed out (the syllable X follows its column).
    /// </summary>
    private static void BumpSpanMin(ImmutableArray<Spring>.Builder springs, int from, int to, double need)
    {
        if (from > to || from < 0 || to >= springs.Count)
            return;
        double have = 0;
        for (int s = from; s <= to; s++)
            have += springs[s].MinDistance;
        if (need <= have)
            return;
        var sp = springs[to];
        double newMin = sp.MinDistance + (need - have);
        springs[to] = new Spring(System.Math.Max(sp.IdealDistance, newMin), newMin, sp.InverseStretchStrength);
    }

    /// <summary>
    /// How far the syllable ink on a measure's FIRST column reaches LEFT of that column —
    /// the lyric half of LilyPond's keep-inside-line rod. Mirrors the selection
    /// <see cref="ApplyLyricSpacing"/> / <see cref="ApplyLeadSheetLyricSpacing"/> make, so
    /// the quantity rodded is the same one those reserve.
    /// </summary>
    /// <remarks>
    /// A syllable is drawn with <c>text-anchor="middle"</c> on its column
    /// (SharedRenderer.Overlays' DrawLyrics), so its ink starts half a width to the LEFT —
    /// that half width IS <c>-extent[LEFT]</c> for the column. NO padding is added: LilyPond's
    /// rod is <c>add_rod (0, i, -keep_inside_line_[LEFT])</c> with none
    /// (lily/simple-spacer.cc:559), unlike the neighbour reservations above, which carry
    /// <see cref="GlyphMetrics.MinItemGap"/>.
    /// </remarks>
    internal static double LeadingLeftExtent(
        ImmutableArray<Spring> springs,
        Measure measure,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics,
        bool isLeadSheet)
    {
        if (lyrics.Count == 0 || columnTimings.Count == 0)
            return 0.0;

        // Staff-backed bars whose items line up with the springs reserve BY ITEM INDEX, so
        // the first column's syllables are item 0's (ApplyLyricSpacing:77-82). Everything
        // else — a lead sheet, an empty bar, a bar opening with a time/clef change — reserves
        // BY TIMING COLUMN (ReserveLyricWidthByColumn:171-173).
        bool byItem = !isLeadSheet
                      && measure.Items.Length > 0
                      && springs.Length == measure.Items.Length + 1;

        var first = new List<LyricItem>();
        foreach (var ly in lyrics)
        {
            if (ly.MeasureIndex != measureIndex)
                continue;
            if (byItem)
            {
                if (ly.ItemIndex == 0)
                    first.Add(ly);
            }
            else if ((!isLeadSheet || ly.IsLyricsRow) && columnTimings[0].Equals(ly.Timing))
            {
                first.Add(ly);
            }
        }
        return GetLyricLeftExtent(first);
    }

    /// <summary>
    /// Calculates the minimum distance between two notes based on their lyrics.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    ///
    /// The distance is: prevLyricRightExtent + nextLyricLeftExtent + padding
    /// where each extent is half the lyric text width (centered under note).
    /// </remarks>
    internal static double CalculateLyricDistance(List<LyricItem>? prevLyrics, List<LyricItem>? nextLyrics)
    {
        if (prevLyrics == null && nextLyrics == null)
            return 0;

        double prevRight = GetLyricRightExtent(prevLyrics);
        double nextLeft = GetLyricLeftExtent(nextLyrics);

        // Minimum INK gap between syllables: a word-space at the lyric font
        // (~0.31 em at 3.2 ss), which is also what LP's lyric spacing yields
        // between words. It doubles as headroom for the renderer's actual
        // serif face, whose advances differ from the Times table by a few
        // percent either way (the face behind generic "serif" is the
        // viewer's choice; we cannot measure it at layout time).
        const double lyricPadding = 1.0;  // staff spaces

        return prevRight + nextLeft + lyricPadding;
    }

    /// <summary>
    /// Gets the right extent of lyrics (from note center to right edge of text).
    /// </summary>
    internal static double GetLyricRightExtent(List<LyricItem>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(lyric.Text);
            // Right extent is half the width (text is centered under note)
            maxExtent = Math.Max(maxExtent, width / 2);
        }
        return maxExtent;
    }

    /// <summary>
    /// Gets the left extent of lyrics (from note center to left edge of text).
    /// </summary>
    internal static double GetLyricLeftExtent(List<LyricItem>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(lyric.Text);
            // Left extent is half the width (text is centered under note)
            maxExtent = Math.Max(maxExtent, width / 2);
        }
        return maxExtent;
    }

    // Real serif-regular advances (TextFontMetrics, from the bundled face's own outlines)
    // at the 3.2 ss lyric font —
    // this used to be a crude 3-bucket table that under-measured capitals
    // ("Up" by ~0.7 ss), so the springs reserved too little and wide syllables
    // overlapped their neighbours in lyric rows.
    private static double EstimateLyricTextWidth(string text)
        => Rendering.TextFontMetrics.Serif(text, 3.2);
}
