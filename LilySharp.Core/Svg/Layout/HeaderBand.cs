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
using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>One chord of the score's chord list (<c>chordList</c>): the printed name and the
/// diagram it draws, or none (a chord with no shape on the tuning, <c>chordDiagrams none</c>).</summary>
internal sealed record ChordListEntry(string Text, int SuperFrom, int BracketSuperFrom, string? Spec);

/// <summary>One placed chord of the list: its cell from the page's left edge, the name's start
/// X and baseline (below the band's top), and the diagram's grid centre X and grid bottom.</summary>
internal sealed record ChordListCell(ChordListEntry Entry, double X, double Width,
    double NameX, double NameBaseline, double GridCentreX, double GridBottom);

/// <summary>The chord list's rows as placed: every cell, and the list's bottom below the band's top.</summary>
internal sealed record ChordListLayout(ImmutableArray<ChordListCell> Cells, double Depth);

/// <summary>
/// The book title as LilyPond pages it: a TOP-ALIGNED column of the header's rows — the
/// title, the subtitle, then the poet / composer line — whose ink depth is what the page
/// chain spaces the first system against, and whose baselines are where the renderer sets
/// the strings.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/titling-init.ly bookTitleMarkup (lines 68-97) — a \column with baseline-skip 3.5
/// of \fill-line rows: the title in \huge \larger \larger \bold, the subtitle in
/// \large \bold, the poet and the composer at the two ends of the poet / instrument /
/// composer row at text size. The rows Lily# has no text for (dedication, subsubtitle,
/// meter, arranger) are empty stencils and the column drops them, so it holds three rows
/// at most.
/// LILYPOND-REF: lily/paper-book.cc:443 Paper_book::book_title — <c>align_to (Y_AXIS, UP)</c>:
/// the column's reference point is the TOP of its ink, so the paper system LilyPond pages
/// it as has Y-extent (−Depth . 0), and the page's top spring runs to that top.
/// LILYPOND-REF: scm/define-markup-commands.scm:2660-2685 column, interpret-markup-list —
/// stack-lines over the rows' stencils with the baseline-skip; scm/stencil.scm stack-lines
/// (lines 153-168) — ly:stencil-stack with the skip as the minimum distance between the rows' reference
/// points (their baselines) and zero padding between their extents, so the next baseline is
/// max (previous baseline + 3.5, previous ink bottom + next ink top). On
/// "Express Yourself" over "Madonna" the skip binds: depth 6.106695 = title top-to-baseline
/// 2.607 + 3.5, the composer having no descender (audit/lp-geometry titled-page.ly, TTL).
/// ⚠️ Text extents are INK, as LilyPond's text stencils are — the column of a lone title is
/// exactly its glyphs' height (TTT: 3.279091), a lone composer its cap height (TTC: 1.654450).
/// </remarks>
/// <param name="Depth">The column's ink height: its top (the reference point) to its lowest
/// ink, in staff spaces.</param>
/// <param name="TitleBaseline">The title row's baseline below the column's top, or null when
/// the header has no title.</param>
/// <param name="ComposerBaseline">The poet / composer row's baseline below the column's top,
/// or null when the header has neither a poet nor a composer.</param>
/// <param name="Width">The widest of the rows' advance widths, in staff spaces — what the
/// snippet page (<see cref="LayoutOptions.CropWidth"/>) must be at least as wide as, the
/// title and subtitle being centred on the page, the poet set against its left margin and
/// the composer against its right.</param>
/// <param name="SubtitleBaseline">The subtitle row's baseline below the column's top, or null
/// when the header has no subtitle.</param>
internal sealed record HeaderBand(
    double Depth,
    double? TitleBaseline,
    double? ComposerBaseline,
    double Width = 0,
    double? SubtitleBaseline = null,
    // The chord list under the title rows (chordList; owner's design 2026-09-29, HANDOFF §2 K5 ⑤),
    // or null. Its depth is in Depth, its widest row in Width.
    ChordListLayout? ChordList = null)
{
    /// <summary>The white between the title rows and the chord list, and between its rows.</summary>
    /// <remarks>LILYSHARP-OWN: LilyPond has no chord list; the gap is Lily#'s, a little more
    /// than the padding a diagram keeps under its name (<c>ChordNameEngraver.DiagramUnderNamePadding</c>).</remarks>
    public const double ChordListGap = 2.0;

    /// <summary>The white between two cells of a chord-list row.</summary>
    public const double ChordListCellGap = 3.0;

    /// <summary>
    /// <paramref name="band"/> (or a fresh one) with the chord list <paramref name="entries"/>
    /// placed under its rows: cells as wide as the name or the diagram, <see cref="ChordListCellGap"/>
    /// apart, in the fewest rows that fit <paramref name="contentWidth"/> with as nearly EQUAL
    /// counts as those rows allow (16 chords on a page that holds 12 make 8 + 8, not 12 + 4;
    /// owner's decision 2026-09-29), each row CENTRED on the page. The name stands centred over
    /// its diagram, the diagram <c>ChordNameEngraver.DiagramUnderNamePadding</c> under the
    /// name's ink, every diagram of a row on one grid-bottom line.
    /// </summary>
    public static HeaderBand? WithChordList(HeaderBand? band, IReadOnlyList<ChordListEntry> entries,
        ScoreTextMetrics fonts, double pageWidth, double marginLeft, double contentWidth)
    {
        if (entries.Count == 0)
            return band;
        var measured = entries.Select(e =>
        {
            double nameWidth = ChordNameGlyphRun.Width(fonts, e.Text, e.SuperFrom, e.BracketSuperFrom);
            var (bottom, top) = ChordNameGlyphRun.Ink(fonts, e.Text, e.SuperFrom, e.BracketSuperFrom);
            // A markup diagram (no finger numbers), as the twin's \fret-diagram-terse is.
            var box = e.Spec != null ? FretFrameGeometry.Box(e.Spec, fonts) : (GlyphMetrics.BBox?)null;
            double width = Math.Max(nameWidth, box?.Width ?? 0);
            return (Entry: e, NameWidth: nameWidth, NameBottom: bottom, NameTop: top, Box: box, Width: width);
        }).ToList();

        // Rows: the fewest with EVEN counts that all fit — r = 1, 2, …: split the list into r
        // runs of ceil(n / r) or floor(n / r) cells (the longer runs first) and take the first
        // r whose every run fits the width; a run of one cell always "fits".
        var rows = new List<List<int>>();
        int n = measured.Count;
        for (int r = 1; r <= n; r++)
        {
            var split = new List<List<int>>();
            int at = 0, extra = n % r, size = n / r;
            for (int k = 0; k < r; k++)
            {
                int count = size + (k < extra ? 1 : 0);
                split.Add([.. Enumerable.Range(at, count)]);
                at += count;
            }
            bool fits = split.All(run => run.Count <= 1
                || run.Sum(i => measured[i].Width) + ChordListCellGap * (run.Count - 1) <= contentWidth);
            if (fits)
            {
                rows = split;
                break;
            }
        }

        double depth = band?.Depth ?? 0;
        double widest = band?.Width ?? 0;
        var cells = ImmutableArray.CreateBuilder<ChordListCell>();
        double y = depth + (depth > 0 ? ChordListGap : 0);
        foreach (var row in rows)
        {
            double rowWidth = row.Sum(i => measured[i].Width) + ChordListCellGap * (row.Count - 1);
            widest = Math.Max(widest, rowWidth);
            double left = marginLeft + (contentWidth - rowWidth) / 2;
            double nameTop = row.Max(i => measured[i].NameTop);
            double nameBottom = row.Min(i => measured[i].NameBottom);
            double boxTop = row.Max(i => measured[i].Box?.Top ?? 0);
            double baseline = y + nameTop;
            // The row's diagrams share one grid bottom: under the deepest name's ink.
            double gridBottom = baseline - nameBottom + ChordNameEngraver.DiagramUnderNamePadding + boxTop;
            double cx = left;
            foreach (int i in row)
            {
                var m = measured[i];
                double nameX = cx + (m.Width - m.NameWidth) / 2;
                double gridCentre = m.Box is { } b ? cx + (m.Width - b.Width) / 2 - b.Left : cx + m.Width / 2;
                cells.Add(new ChordListCell(m.Entry, cx, m.Width, nameX, baseline, gridCentre, gridBottom));
                cx += m.Width + ChordListCellGap;
            }
            // The row's depth: the diagrams' ink under their grid bottom (the strings' overhang,
            // Box.Bottom is negative), else the names'.
            double boxBottom = row.Min(i => measured[i].Box?.Bottom ?? 0);
            y = row.Any(i => measured[i].Box != null) ? gridBottom - boxBottom : baseline - nameBottom;
            y += ChordListGap;
        }
        double listDepth = y - ChordListGap;
        var list = new ChordListLayout(cells.ToImmutable(), listDepth);
        return (band ?? new HeaderBand(0, null, null)) with { Depth = listDepth, Width = widest, ChordList = list };
    }
    /// <summary>The column's minimum baseline-to-baseline step.</summary>
    /// <remarks>LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, line 69 —
    /// <c>\override #'(baseline-skip . 3.5)</c>.</remarks>
    public const double BaselineSkip = 3.5;

    /// <summary>
    /// The least white left between one row's ink and the next row's, in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (user decision 2026-09-26): LilyPond stacks the rows with ZERO padding
    /// between their extents (scm/stencil.scm stack-lines → ly:stencil-stack), so a row made
    /// larger by a <c>fonts { subtitle step +5 }</c> sits with its cap height touching the
    /// title's baseline — measured on LilyPond 2.26.0 itself, the same picture. At the
    /// default sizes the 3.5 baseline-skip binds with room to spare and this changes nothing;
    /// it only moves a row whose own ink outgrows the skip.
    /// </remarks>
    public const double RowPadding = 0.5;

    /// <summary>
    /// The title's font size: four font-size steps over the 11pt text font, 2.2 × 2^(4/6).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, lines 74-77 — the title row,
    /// <c>\huge \larger \larger \bold \fromproperty #'header:title</c>;
    /// scm/define-markup-commands.scm:4009-4021 huge is font-size 2 and :3657 larger adds 1
    /// (prepend-alist-chain 'font-size), each step a magstep of 2^(1/6) — 11pt × 2^(4/6) =
    /// 17.46pt, 3.49 staff spaces at a 20pt staff.
    /// </remarks>
    public const double TitleFontSize = 3.49;

    /// <summary>The composer's font size: the 11pt text font, 2.2 staff spaces at a 20pt staff.</summary>
    /// <remarks>LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, lines 86-90 — the poet / instrument /
    /// composer row; <c>\fromproperty #'header:composer</c> carries no size command.</remarks>
    public const double ComposerFontSize = 2.2;

    /// <summary>The title's em for THIS score: <see cref="TitleFontSize"/> unless the
    /// score's <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>title</c>. Read by
    /// <see cref="Build"/> and by the draw, so the two cannot come apart.</summary>
    public static double TitleEm(ScoreTextMetrics fonts) => fonts.Size(TextRole.Title, TitleFontSize);

    /// <summary>The title's weight and slant: bookTitleMarkup's <c>\bold</c> unless the score
    /// wrote a style for <c>title</c>.</summary>
    public static FontStyle TitleStyle(ScoreTextMetrics fonts) => fonts.Style(TextRole.Title, FontStyle.Bold);

    /// <summary>The composer's em for THIS score — see <see cref="TitleEm"/>.</summary>
    public static double ComposerEm(ScoreTextMetrics fonts) => fonts.Size(TextRole.Composer, ComposerFontSize);

    /// <summary>The composer's weight and slant: upright unless the score wrote a style.</summary>
    public static FontStyle ComposerStyle(ScoreTextMetrics fonts) => fonts.Style(TextRole.Composer, FontStyle.Regular);

    /// <summary>
    /// The subtitle's font size: two font-size steps over the 11pt text font, 2.2 × 2^(2/6).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, lines 78-81 — the subtitle row,
    /// <c>\large \bold \fromproperty #'header:subtitle</c>; large is font-size 2, each step a
    /// magstep of 2^(1/6) — 11pt × 2^(2/6) = 13.86pt, 2.77 staff spaces at a 20pt staff.
    /// </remarks>
    public const double SubtitleFontSize = 2.77;

    /// <summary>The subtitle's em for THIS score — see <see cref="TitleEm"/>.</summary>
    public static double SubtitleEm(ScoreTextMetrics fonts) => fonts.Size(TextRole.Subtitle, SubtitleFontSize);

    /// <summary>The subtitle's weight and slant: bookTitleMarkup's <c>\bold</c> unless the
    /// score wrote a style.</summary>
    public static FontStyle SubtitleStyle(ScoreTextMetrics fonts) => fonts.Style(TextRole.Subtitle, FontStyle.Bold);

    /// <summary>The poet's em for THIS score: the composer's text size, the same row.</summary>
    /// <remarks>LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, lines 86-90 —
    /// <c>\fromproperty #'header:poet</c> carries no size command, like the composer.</remarks>
    public static double PoetEm(ScoreTextMetrics fonts) => fonts.Size(TextRole.Poet, ComposerFontSize);

    /// <summary>The poet's weight and slant: upright unless the score wrote a style.</summary>
    public static FontStyle PoetStyle(ScoreTextMetrics fonts) => fonts.Style(TextRole.Poet, FontStyle.Regular);

    /// <summary>
    /// The least room <c>\fill-line</c> leaves between the poet and the composer when the row
    /// is too full to spread them — the snippet page's width reads it.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-markup-commands.scm:2157-2161 define-markup-command
    /// (fill-line …) — <c>(word-space 0.6)</c>.</remarks>
    public const double FillLineWordSpace = 0.6;

    /// <summary>
    /// The column for a header, or null when the book has no title, subtitle, poet or
    /// composer and LilyPond would page no title line at all.
    /// </summary>
    /// <param name="title">The title, or null.</param>
    /// <param name="composer">The composer, or null.</param>
    /// <param name="fonts">The score's text metrics — the faces the header is set in.</param>
    /// <param name="subtitle">The subtitle, or null.</param>
    /// <param name="poet">The poet, or null.</param>
    /// <param name="instrument">The instrument line, or null — "Capo 3" for a capo score
    /// (2026-09-29): the centre of the poet / composer row, at the composer's size.</param>
    public static HeaderBand? Build(string? title, string? composer, ScoreTextMetrics fonts,
        string? subtitle = null, string? poet = null, string? instrument = null)
    {
        if (title is null && composer is null && subtitle is null && poet is null && instrument is null)
            return null;

        double? previousBaseline = null;
        double depth = 0;
        double width = 0;

        // LILYPOND-REF: scm/stencil.scm stack-lines (lines 153-168) — ly:stencil-stack: the row's
        // reference point is its baseline; it is placed at least BaselineSkip below the
        // previous baseline and at least its own ink top below the previous row's ink bottom —
        // plus RowPadding, which LilyPond does not add (LILYSHARP-OWN, see the constant).
        // A row is one or two strings on one baseline (\fill-line): its ink is their union.
        double Stack(double inkBottom, double inkTop, double rowWidth)
        {
            double baseline = previousBaseline is { } prev
                ? Math.Max(prev + BaselineSkip, depth + RowPadding + inkTop)
                : inkTop;
            previousBaseline = baseline;
            depth = Math.Max(depth, baseline - inkBottom);
            width = Math.Max(width, rowWidth);
            return baseline;
        }

        // The same size and style the draw sets each string in (SharedRenderer.DrawHeader).
        double? StackOne(string? text, double size, TextRole role, FontStyle style)
        {
            if (text is null)
                return null;
            // A string the face cannot spell (CJK) is drawn from a fallback face this layout
            // never measures — reserve the face's own box for it (InkOrFallbackBox).
            var (inkBottom, inkTop) = fonts.InkOrFallbackBox(text, size, role, style);
            return Stack(inkBottom, inkTop, fonts.Advance(text, size, role, style));
        }

        double? titleBaseline = StackOne(title, TitleEm(fonts), TextRole.Title, TitleStyle(fonts));
        double? subtitleBaseline = StackOne(subtitle, SubtitleEm(fonts), TextRole.Subtitle, SubtitleStyle(fonts));

        double? composerBaseline = null;
        if (poet is not null || composer is not null || instrument is not null)
        {
            double inkBottom = double.PositiveInfinity, inkTop = double.NegativeInfinity, rowWidth = 0;
            void Add(string? text, double size, TextRole role, FontStyle style)
            {
                if (text is null)
                    return;
                var (bottom, top) = fonts.InkOrFallbackBox(text, size, role, style);
                inkBottom = Math.Min(inkBottom, bottom);
                inkTop = Math.Max(inkTop, top);
                rowWidth += fonts.Advance(text, size, role, style);
            }
            Add(poet, PoetEm(fonts), TextRole.Poet, PoetStyle(fonts));
            // LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, lines 86-90 — the middle of the
            // poet / instrument / composer \fill-line; set in the composer's face and size.
            Add(instrument, ComposerEm(fonts), TextRole.Composer, ComposerStyle(fonts));
            Add(composer, ComposerEm(fonts), TextRole.Composer, ComposerStyle(fonts));
            int strings = (poet is null ? 0 : 1) + (instrument is null ? 0 : 1) + (composer is null ? 0 : 1);
            if (strings > 1)
                rowWidth += FillLineWordSpace * (strings - 1);
            composerBaseline = Stack(inkBottom, inkTop, rowWidth);
        }

        return new HeaderBand(depth, titleBaseline, composerBaseline, width, subtitleBaseline);
    }
}
