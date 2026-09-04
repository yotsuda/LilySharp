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

using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The book title as LilyPond pages it: a TOP-ALIGNED column of the header's rows — the
/// title, then the composer — whose ink depth is what the page chain spaces the first
/// system against, and whose baselines are where the renderer sets the strings.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/titling-init.ly bookTitleMarkup (lines 68-97) — a \column with baseline-skip 3.5
/// of \fill-line rows: the title in \huge \larger \larger \bold, the composer on the
/// poet / instrument / composer row at text size. The rows Lily# has no text for
/// (dedication, subtitle, subsubtitle, meter, arranger) are empty stencils and the column
/// drops them, so it holds two rows at most.
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
/// <param name="ComposerBaseline">The composer row's baseline below the column's top, or null
/// when the header has no composer.</param>
internal sealed record HeaderBand(
    double Depth,
    double? TitleBaseline,
    double? ComposerBaseline)
{
    /// <summary>The column's minimum baseline-to-baseline step.</summary>
    /// <remarks>LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, line 69 —
    /// <c>\override #'(baseline-skip . 3.5)</c>.</remarks>
    public const double BaselineSkip = 3.5;

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

    /// <summary>
    /// The column for a header, or null when the book has neither a title nor a composer and
    /// LilyPond would page no title line at all.
    /// </summary>
    /// <param name="fonts">The score's text metrics — the faces the title and composer are set in.</param>
    public static HeaderBand? Build(string? title, string? composer, ScoreTextMetrics fonts)
    {
        if (title is null && composer is null)
            return null;

        double? previousBaseline = null;
        double depth = 0;

        // LILYPOND-REF: scm/stencil.scm stack-lines (lines 153-168) — ly:stencil-stack: the row's
        // reference point is its baseline; it is placed at least BaselineSkip below the
        // previous baseline and at least its own ink top below the previous row's ink bottom.
        double Stack(string text, double size, TextRole role, FontStyle style)
        {
            var (inkBottom, inkTop) = fonts.Ink(text, size, role, style);
            double baseline = previousBaseline is { } prev
                ? Math.Max(prev + BaselineSkip, depth + inkTop)
                : inkTop;
            previousBaseline = baseline;
            depth = Math.Max(depth, baseline - inkBottom);
            return baseline;
        }

        double? titleBaseline = title is null
            ? null
            : Stack(title, TitleFontSize, TextRole.Title, FontStyle.Bold);
        double? composerBaseline = composer is null
            ? null
            : Stack(composer, ComposerFontSize, TextRole.Composer, FontStyle.Regular);

        return new HeaderBand(depth, titleBaseline, composerBaseline);
    }
}
