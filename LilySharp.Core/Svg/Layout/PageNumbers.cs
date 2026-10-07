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

using System.Globalization;
using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The page number LilyPond prints in a page's header — the one thing its default
/// <c>oddHeaderMarkup</c> / <c>evenHeaderMarkup</c> put there for a book without an
/// instrument header — and the height that header takes from the page.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/titling-init.ly:108-121 oddHeaderMarkup / evenHeaderMarkup, page:page-number-string —
///   a <c>\fill-line</c> whose page number is the LAST column on an odd page and the FIRST on
///   an even one, printed <c>\if \should-print-page-number</c>;
/// LILYPOND-REF: ly/titling-init.ly:42-48 should-print-page-number — print-page-number, and not on
///   the first page unless print-first-page-number;
/// LILYPOND-REF: ly/paper-defaults-init.ly:142-145 first-page-number 1, print-first-page-number
///   ##f, print-page-number ##t, page-number-type arabic.
/// So with the defaults — the only setting Lily# has — page 1 has no header and every later
/// page has its number, odd pages at the right margin, even ones at the left.
/// <para>
/// THE HEIGHT IS THE HEADER STENCIL'S: <c>Page::calc_printable_height</c> takes it from the
/// page the breaker prices (scm/page.scm:303-321 calc-printable-height), and the page layout
/// floors the first system under it (lily/page-layout-problem.cc:435-444 header_height_,
/// <c>bottom_skyline_.set_minimum_height (-header_height_)</c>). A text stencil's Y extent is
/// its INK, so the height is the digits' ink. MEASURED (2.26.0, Lab sessions/p851/ws, the twin
/// of test/multi-page-vertical): pages 2 and 3 set their numbers' baselines 1.5488 under the
/// 10 mm top margin, LilyPond Serif at 2.2.
/// </para>
/// </remarks>
public static class PageNumbers
{
    /// <summary>The number printed on the page at <paramref name="pageIndex"/> (0-based), or
    /// null where none is.</summary>
    public static string? Text(int pageIndex)
        => pageIndex <= 0 ? null : (pageIndex + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>The em the number is set at: the default text size, as the header markup is.</summary>
    public static double Em(ScoreTextMetrics fonts)
        => fonts.Size(TextRole.PageNumber, MusicMarkEngraver.TextFontEm);

    /// <summary>The weight and slant the number is set in.</summary>
    public static FontStyle Style(ScoreTextMetrics fonts)
        => fonts.Style(TextRole.PageNumber, FontStyle.Regular);

    /// <summary>The number's ink about its baseline, up-positive — (0, 0) where none is printed.</summary>
    public static (double Bottom, double Top) Ink(ScoreTextMetrics fonts, int pageIndex)
        => Text(pageIndex) is { } text
            ? fonts.Ink(text, Em(fonts), TextRole.PageNumber, Style(fonts))
            : (0.0, 0.0);

    /// <summary>The height the page's header takes from it — 0 where nothing is printed.</summary>
    /// <remarks>LILYPOND-REF: lily/page-layout-problem.cc:435 — header_height_ =
    /// head->extent (Y_AXIS).length ().</remarks>
    public static double HeaderHeight(ScoreTextMetrics fonts, int pageIndex)
    {
        var (bottom, top) = Ink(fonts, pageIndex);
        return top - bottom;
    }

    /// <summary>Whether the page's number stands at the RIGHT margin (an odd page) rather than
    /// the left.</summary>
    public static bool AtRight(int pageIndex) => (pageIndex + 1) % 2 == 1;
}
