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

        var lines = GroupByLine(lyrics, measureIndex, ly => ly.ItemIndex, _ => true);
        if (lines.Count == 0)
            return springs;

        var result = springs.ToBuilder();
        foreach (var (_, byCol) in lines)
            ReserveLyricLine(result, fonts, byCol, measure.Items.Length, Edge);
        return result.ToImmutable();
    }

    /// <summary>
    /// One lyric line's syllables per column, one entry per LINE — a verse of a voice's (or
    /// row's) lyrics. Sorted both ways so the reservation below is deterministic.
    /// </summary>
    /// <remarks>
    /// The line, not the column set, is the rod's unit: LilyPond's Hyphen_engraver lives per
    /// Lyrics context (LILYPOND-REF: lily/hyphen-engraver.cc — one <c>hyphen_</c> chain per
    /// engraver instance), so a LyricSpace/LyricHyphen never spans from one verse's syllable
    /// to another's, and the connector that picks 0.45 or 0.1 is always the same line's.
    /// Grouping ALL verses into one chain — the pre-2026-08-20 shape — let verse 1's ink be
    /// priced against verse 2's connector.
    /// </remarks>
    private static SortedDictionary<(int Voice, int Verse, int Staff, bool Row),
        SortedDictionary<int, List<LyricItem>>> GroupByLine(
        IReadOnlyList<LyricItem> lyrics, int measureIndex,
        System.Func<LyricItem, int> columnOf, System.Func<LyricItem, bool> include)
    {
        var lines = new SortedDictionary<(int, int, int, bool),
            SortedDictionary<int, List<LyricItem>>>();
        foreach (var lyric in lyrics)
        {
            if (lyric.MeasureIndex != measureIndex || !include(lyric))
                continue;
            int col = columnOf(lyric);
            if (col < 0)
                continue;
            var key = (lyric.VoiceId, lyric.VerseNumber, lyric.StaffIndex, lyric.IsLyricsRow);
            if (!lines.TryGetValue(key, out var byCol))
                lines[key] = byCol = new SortedDictionary<int, List<LyricItem>>();
            if (!byCol.TryGetValue(col, out var list))
                byCol[col] = list = new List<LyricItem>();
            list.Add(lyric);
        }
        return lines;
    }

    /// <summary>
    /// One lyric line's reservations over a measure's spring chain: the leading extent to
    /// the opening bar, the word/hyphen distance between consecutive syllables, the
    /// trailing extent to the closing bar. Reserves across the SPANS between
    /// syllable-carrying columns — a wide syllable held over following notes (a melisma)
    /// overlaps THEIR columns freely in LilyPond; only the next SYLLABLE's ink binds.
    /// Verses rod independently (one call per line) and <see cref="BumpSpanMin"/>'s
    /// have-check makes the effective reservation their max, as separate rods would be.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN, the leading/trailing halves: LilyPond reserves NOTHING between a
    /// syllable and a bar line (their spacing boxes never overlap in Y — LyricText even
    /// recedes 0.2 each side, extra-spacing-height (0.2 . -0.2)) and rods the next
    /// SYLLABLE's ink straight across the bar instead. Lily# prices measures one at a time
    /// for breaking and laying out, so the cross-bar pair has no chain to rod over and the
    /// reservation is cut at the bar line with <see cref="GlyphMetrics.MinItemGap"/> on
    /// each side. The ledger point lyrics.column.word-gap.cross-barline carries the cost
    /// (+0.54-class: 0.4 + bar ink + 0.4 against LilyPond's 0.45 spanning it); the port
    /// would be a line-level rod in MultiStaffLayouter's rods list PLUS the same quantity
    /// in the break gate's pricing — HANDOFF 2H holds the item.
    /// </remarks>
    private static void ReserveLyricLine(
        ImmutableArray<Spring>.Builder result,
        Rendering.ScoreTextMetrics fonts,
        SortedDictionary<int, List<LyricItem>> byCol,
        int endColumn,
        System.Func<int, (double Left, double Centre)> edge)
    {
        var cols = new List<int>(byCol.Keys);

        // Leading extent: the line's first syllable clears the start barline.
        int first = cols[0];
        BumpSpanMin(result, 0, first,
            GetLyricLeftExtent(fonts, byCol[first], edge(first)) + GlyphMetrics.MinItemGap);

        // Between consecutive syllables (across any held/silent columns in between).
        for (int p = 0; p + 1 < cols.Count; p++)
        {
            int a = cols[p], b = cols[p + 1];
            BumpSpanMin(result, a + 1, b,
                CalculateLyricDistance(fonts, byCol[a], byCol[b], edge(a), edge(b)));
        }

        // Trailing extent: the line's last syllable clears the end barline.
        int last = cols[^1];
        BumpSpanMin(result, last + 1, endColumn,
            GetLyricRightExtent(fonts, byCol[last], edge(last)) + GlyphMetrics.MinItemGap);
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

        int ColumnOf(LyricItem ly)
        {
            for (int c = 0; c < cols; c++)
                if (columnTimings[c].Equals(ly.Timing))
                    return c;
            return -1;
        }

        var lines = GroupByLine(lyrics, measureIndex, ColumnOf, include);
        if (lines.Count == 0)
            return springs;

        var result = springs.ToBuilder();
        foreach (var (_, byCol) in lines)
            ReserveLyricLine(result, fonts, byCol, cols, Edge);
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
    /// LyricSpace's minimum-distance — the ink-to-ink floor between two WORDS.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm LyricSpace <c>(minimum-distance . 0.45)</c>.
    /// lily/hyphen-engraver.cc:107 makes a LyricSpace spanner wherever a syllable follows
    /// with no hyphen between; lily/lyric-hyphen.cc:163-179 <c>set_spacing_rods</c> turns it
    /// into a rod of minimum-distance + <c>bounds_protrusion</c>, i.e. ink edge to ink edge.
    /// Measured: probe LCW's word gap 6.322649 = advance("mum") 5.872649 + 0.45, to the digit.
    /// </remarks>
    internal const double WordSpaceMinimum = 0.45;

    /// <summary>
    /// LyricHyphen's minimum-distance — the floor between two syllables of ONE word.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm LyricHyphen <c>(minimum-distance . 0.1)</c>, through
    /// the same <c>set_spacing_rods</c>. The dash itself claims NO space mid-line — print
    /// returns empty when it cannot fit (lily/lyric-hyphen.cc:108-121; probe LCH dumps all six
    /// mid-line hyphens with empty stencils) — so the rod is the whole story. Measured: LCH's
    /// hyphen gap 5.972649 = the same advance + 0.1, a 0.35 fork on one changed connector.
    /// </remarks>
    internal const double HyphenSpaceMinimum = 0.1;

    /// <summary>
    /// The minimum distance between two syllable-carrying columns of ONE lyric line: the
    /// syllables' reaches toward each other plus LilyPond's word or hyphen space.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-hyphen.cc:163-179 <c>Lyric_hyphen::set_spacing_rods</c> —
    /// <c>minimum-distance + bounds_protrusion()</c>, where the protrusion is each bound's
    /// ink toward the other, converted to column frame by <c>Rod::add_to_cols</c> (the
    /// syllable's alignment offset — the edge pairs here). Which minimum applies is the
    /// PREVIOUS syllable's connector: a hyphen after it makes the spanner a LyricHyphen
    /// (0.1), anything else — including an extender, which suppresses nothing — leaves the
    /// LyricSpace (0.45). ⚠️ Callers pass ONE lyric line's syllables (hyphen-engraver is
    /// per Lyrics context); a mixed-verse list would let one verse's connector price
    /// another verse's ink.
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

        bool hyphen = prevLyrics is { Count: > 0 }
            && prevLyrics.TrueForAll(l => l.ConnectorType == LyricConnectorType.Hyphen);

        return prevRight + nextLeft + (hyphen ? HyphenSpaceMinimum : WordSpaceMinimum);
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

    // The syllable's width as spacing sees it: the ADVANCE at the DRAWN lyric size.
    // LILYPOND-REF: lily/pango-font.cc:351-362 Pango_font::pango_item_string_stencil — a
    // text stencil's X extent is Pango's LOGICAL rectangle (the advance, quantised to
    // whole 1200-dpi pixels), and that stencil extent is what joins the paper column's
    // spacing boxes and the LyricSpace rod's bounds_protrusion. fonts.Advance carries the
    // same quantisation, so the two engines read one number: probe LCW dumps 5.872649 for
    // "mum" and lyrics.column.word-gap closes to the ninth digit on it. (The 3.2-era
    // readings were 0.0012 em wider than LilyPond's — that offset was the QUANTISATION
    // measured at the wrong size, not a side bearing: the ledger pair's fork ratio 1.2941
    // vs the size ratio 1.2959 is the same fact from the outside.)
    // ⚠️ Sized at EngravingDefaults.LyricTextFontSize — the size the syllable is DRAWN at.
    // This was 3.2, the pre-em-correction lyric size, until 2026-08-20, so every column
    // reservation was ~30% wider than the drawn ink; the lyrics.column.* pair is what
    // pinned the size to the drawn em. (The 3.2 had itself replaced a crude 3-bucket table
    // that under-measured capitals — "Up" by ~0.7 ss — so the springs reserved too little
    // and wide syllables overlapped.)
    private static double EstimateLyricTextWidth(Rendering.ScoreTextMetrics fonts, string text)
        => fonts.Advance(text, EngravingDefaults.LyricTextFontSize, Rendering.TextRole.LyricText);
}
