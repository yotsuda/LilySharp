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
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Spring> springs,
        Measure measure,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics,
        IReadOnlyList<(double Left, double Centre)> parentAlignmentEdges)
    {
        // Where a self-aligned grob stands on column c — the syllable's ink is placed
        // THERE (centred on the extent's centre, or left-aligned on its left edge for a
        // melisma), not on the column, so every extent below is measured from it.
        // LILYPOND-REF: lily/self-alignment-interface.cc:121-139.
        // ⚠️ A column past the end of the supplied list falls back to the PLACEHOLDER, which
        // is what LilyPond does for a column with no note heads — NOT to 0, which is a model
        // that exists nowhere in LilyPond (it would mean an alignment extent of zero width).
        (double Left, double Centre) Edge(int column) => AlignmentEdge(parentAlignmentEdges, column);

        // The springs are built from TIMING COLUMNS. In a plain measure those columns
        // coincide with the note items, so the item-index reservation below lines up. But
        // when the bar opens with a non-note item (a mid-piece time/clef change) the item
        // slots no longer match the columns — the item-index chain would bail and leave the
        // syllables to crowd. Reserve by timing column instead.
        if (measure.Items.Length == 0 || springs.Length != measure.Items.Length + 1)
            return ReserveLyricWidthByColumn(
                fonts, springs, columnTimings, measureIndex, lyrics, _ => true, parentAlignmentEdges);

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
        var lyricItems = lyricsByItem.Keys.OrderBy(i => i).ToList();

        // Reserve ink across the SPANS between lyric-carrying items, exactly as the
        // by-column variant does — NOT per adjacent item pair. A wide syllable held
        // over following notes (a melisma) overlaps THEIR columns freely in LilyPond;
        // only the next SYLLABLE's ink (or the barline) binds. The old per-pair form
        // pushed the melisma's first held note out by the whole syllable width.
        // (For two syllables on consecutive items the span is a single spring, so
        // this is byte-identical to the per-pair form there.)

        // Leading extent: the first syllable clears the start barline.
        int firstItem = lyricItems[0];
        BumpSpanMin(result, 0, firstItem,
            GetLyricLeftExtent(fonts, lyricsByItem[firstItem], Edge(firstItem)) + GlyphMetrics.MinItemGap);

        // Between consecutive syllables (across any held/silent items in between).
        for (int p = 0; p + 1 < lyricItems.Count; p++)
        {
            int a = lyricItems[p], b = lyricItems[p + 1];
            BumpSpanMin(result, a + 1, b,
                CalculateLyricDistance(fonts, lyricsByItem[a], lyricsByItem[b], Edge(a), Edge(b)));
        }

        // Trailing extent: the last syllable clears the end barline.
        int lastItem = lyricItems[^1];
        BumpSpanMin(result, lastItem + 1, measure.Items.Length,
            GetLyricRightExtent(fonts, lyricsByItem[lastItem], Edge(lastItem)) + GlyphMetrics.MinItemGap);

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
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics,
        IReadOnlyList<(double Left, double Centre)> parentAlignmentEdges)
        => ReserveLyricWidthByColumn(
            fonts, springs, columnTimings, measureIndex, lyrics, ly => ly.IsLyricsRow,
            parentAlignmentEdges);

    /// <summary>
    /// The alignment edge pair for a column, with LilyPond's own fallback for a column the
    /// list does not cover: the PLACEHOLDER, never zero.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:130-139 — an empty note-column extent
    /// falls back to <c>X-alignment-extent</c>, i.e. <c>(0 . 1.35)</c>
    /// (scm/define-grobs.scm:2749-2750). ⚠️ There is no LilyPond regime in which the extent a
    /// syllable aligns on is EMPTY, so 0 must never be the fallback: a caller that reached
    /// this with a short list would otherwise silently get the pre-port model back.
    /// </remarks>
    private static (double Left, double Centre) AlignmentEdge(
        IReadOnlyList<(double Left, double Centre)> edges, int column)
        => column >= 0 && column < edges.Count
            ? edges[column]
            : (0.0, EngravingDefaults.PaperColumnXAlignmentExtentWidth / 2);

    /// <summary>
    /// Reserves lyric ink against the TIMING COLUMNS the springs were built from: each
    /// syllable matched to its column by <see cref="LyricItem.Timing"/>, its width spread
    /// across the springs spanning adjacent lyric columns (plus the leading/trailing extents
    /// against the barlines). <paramref name="include"/> selects which lyrics participate
    /// (row-only on a lead sheet; all on a staff). Robust to columns that carry no syllable
    /// (a chord-only column, or a bar's leading time/clef change).
    /// </summary>
    private static ImmutableArray<Spring> ReserveLyricWidthByColumn(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Spring> springs,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics,
        System.Func<LyricItem, bool> include,
        IReadOnlyList<(double Left, double Centre)> parentAlignmentEdges)
    {
        (double Left, double Centre) Edge(int column) => AlignmentEdge(parentAlignmentEdges, column);

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
            GetLyricLeftExtent(fonts, lyricsByCol[firstCol], Edge(firstCol)) + GlyphMetrics.MinItemGap);

        // Between consecutive syllables (across any note/chord-only columns in between).
        for (int p = 0; p + 1 < lyricCols.Count; p++)
        {
            int a = lyricCols[p], b = lyricCols[p + 1];
            BumpSpanMin(result, a + 1, b,
                CalculateLyricDistance(fonts, lyricsByCol[a], lyricsByCol[b], Edge(a), Edge(b)));
        }

        // Trailing extent: the last syllable clears the end barline.
        int lastCol = lyricCols[^1];
        BumpSpanMin(result, lastCol + 1, cols,
            GetLyricRightExtent(fonts, lyricsByCol[lastCol], Edge(lastCol)) + GlyphMetrics.MinItemGap);

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
    /// How far the syllable ink on each of a measure's columns reaches beyond that column —
    /// the lyric side of LilyPond's keep-inside-line rod, one entry per column. Mirrors the
    /// selection <see cref="ApplyLyricSpacing"/> / <see cref="ApplyLeadSheetLyricSpacing"/>
    /// make, so the quantity rodded is the same one those reserve.
    /// </summary>
    /// <remarks>
    /// A syllable's ink is centred on the column's ALIGNMENT EXTENT, not on the column: its
    /// centre stands <c>he.centre</c> right of the column
    /// (lily/self-alignment-interface.cc:117-176, see LyricEngraver), so it reaches
    /// <c>w/2 - he.centre</c> LEFT of the column and <c>w/2 + he.centre</c> RIGHT of it —
    /// asymmetric, and the left reach goes NEGATIVE for a syllable narrower than the extent,
    /// which is a rod that simply does not bind.
    /// ⚠️ These are the LYRIC's reaches alone; LilyPond's <c>keep_inside_line_</c> is the
    /// column's WHOLE ink, so see the caller (MultiStaffLayouter) for the rest of it.
    /// NO padding is added — LilyPond's rods carry none (lily/simple-spacer.cc:558-559) —
    /// unlike the neighbour reservations above, which carry
    /// <see cref="GlyphMetrics.MinItemGap"/>.
    /// </remarks>
    internal static (double[] Left, double[] Right) InkReachPerColumn(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<Spring> springs,
        Measure measure,
        IReadOnlyList<Fraction> columnTimings,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics,
        bool isLeadSheet,
        IReadOnlyList<(double Left, double Centre)> parentAlignmentEdges)
    {
        var leftReach = new double[columnTimings.Count];
        var rightReach = new double[columnTimings.Count];
        if (lyrics.Count == 0 || columnTimings.Count == 0)
            return (leftReach, rightReach);

        // Staff-backed bars whose items line up with the springs reserve BY ITEM INDEX, so
        // column c carries item c's syllables (ApplyLyricSpacing:77-95). Everything else — a
        // lead sheet, an empty bar, a bar opening with a time/clef change — reserves BY
        // TIMING COLUMN (ReserveLyricWidthByColumn:150-163).
        bool byItem = !isLeadSheet
                      && measure.Items.Length > 0
                      && springs.Length == measure.Items.Length + 1;

        var perColumn = new List<LyricItem>[columnTimings.Count];
        foreach (var ly in lyrics)
        {
            if (ly.MeasureIndex != measureIndex)
                continue;
            int col = -1;
            if (byItem)
            {
                if (ly.ItemIndex >= 0 && ly.ItemIndex < columnTimings.Count)
                    col = ly.ItemIndex;
            }
            else if (!isLeadSheet || ly.IsLyricsRow)
            {
                for (int c = 0; c < columnTimings.Count; c++)
                    if (columnTimings[c].Equals(ly.Timing)) { col = c; break; }
            }
            if (col < 0)
                continue;
            (perColumn[col] ??= new List<LyricItem>()).Add(ly);
        }

        for (int c = 0; c < leftReach.Length; c++)
        {
            if (perColumn[c] is not { Count: > 0 })
                continue;
            var edge = AlignmentEdge(parentAlignmentEdges, c);
            leftReach[c] = GetLyricLeftExtent(fonts, perColumn[c], edge);
            rightReach[c] = GetLyricRightExtent(fonts, perColumn[c], edge);
        }
        return (leftReach, rightReach);
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
    internal static double CalculateLyricDistance(
        Rendering.ScoreTextMetrics fonts,
        List<LyricItem>? prevLyrics, List<LyricItem>? nextLyrics,
        (double Left, double Centre) prevAlignmentEdge, (double Left, double Centre) nextAlignmentEdge)
    {
        if (prevLyrics == null && nextLyrics == null)
            return 0;

        double prevRight = GetLyricRightExtent(fonts, prevLyrics, prevAlignmentEdge);
        double nextLeft = GetLyricLeftExtent(fonts, nextLyrics, nextAlignmentEdge);

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
    /// How far the widest syllable on a column reaches RIGHT of that column. A centred
    /// syllable's ink runs <c>(he.centre − w/2 . he.centre + w/2)</c>; a MELISMA syllable is
    /// left-aligned on the extent, so its ink runs <c>(he.left . he.left + w)</c> — the same
    /// X model the engraver draws with.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:117-176 aligned_on_parent, and
    /// lily/lyric-engraver.cc:180-183 melisma_busy for the LEFT alignment. ⚠️ The edge pair is
    /// REQUIRED: there is no LilyPond regime in which a syllable is centred on its column, so
    /// a defaulted 0 would be a model of Lily#'s own making leaking back in.
    /// </remarks>
    internal static double GetLyricRightExtent(
        Rendering.ScoreTextMetrics fonts, List<LyricItem>? lyrics, (double Left, double Centre) alignmentEdge)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(fonts, lyric.Text);
            maxExtent = Math.Max(maxExtent, lyric.MelismaAlignLeft
                ? alignmentEdge.Left + width
                : width / 2 + alignmentEdge.Centre);
        }
        return maxExtent;
    }

    /// <summary>
    /// How far the widest syllable on a column reaches LEFT of that column — negative when
    /// the syllable does not reach left of it at all (a narrow centred syllable, or any
    /// left-aligned melisma syllable, whose ink starts AT the extent's left edge).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:117-176, as for
    /// <see cref="GetLyricRightExtent"/>.
    /// </remarks>
    internal static double GetLyricLeftExtent(
        Rendering.ScoreTextMetrics fonts, List<LyricItem>? lyrics, (double Left, double Centre) alignmentEdge)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(fonts, lyric.Text);
            maxExtent = Math.Max(maxExtent, lyric.MelismaAlignLeft
                ? -alignmentEdge.Left
                : width / 2 - alignmentEdge.Centre);
        }
        return maxExtent;
    }

    // Real serif-regular advances (TextFontMetrics, from the bundled face's own outlines)
    // at the 3.2 ss lyric font —
    // this used to be a crude 3-bucket table that under-measured capitals
    // ("Up" by ~0.7 ss), so the springs reserved too little and wide syllables
    // overlapped their neighbours in lyric rows.
    private static double EstimateLyricTextWidth(Rendering.ScoreTextMetrics fonts, string text)
        => fonts.Advance(text, 3.2, Rendering.TextRole.LyricText);
}
