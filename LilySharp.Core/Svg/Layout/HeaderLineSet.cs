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

/// <summary>Which edge of the page a set header line stands against.</summary>
internal enum HeaderAlign
{
    /// <summary>About the page's middle (the copyright).</summary>
    Center,
    /// <summary>Ending at the right margin (the arranger).</summary>
    Right,
}

/// <summary>
/// One of the two header lines of 2026-11-13 (owner's decision) as it is set: the
/// <c>arranger</c> (a row of the header's column, under the composer's) or the
/// <c>copyright</c> (the first page's footer) — its string, the size, role and style it is
/// drawn in, and its measured ink and advance, so the layout that reserves its room and the
/// draw that prints it read one value.
/// </summary>
/// <param name="Text">The string.</param>
/// <param name="Em">The size it is set at.</param>
/// <param name="Role">The text role whose face it is set in.</param>
/// <param name="Style">Its weight and slant.</param>
/// <param name="Align">Where it stands on its line.</param>
/// <param name="Position">Where the string is written in the file (the preview's click).</param>
/// <param name="InkBottom">Its lowest ink about its baseline (up-positive, so 0 or below).</param>
/// <param name="InkTop">Its ink top over its baseline.</param>
/// <param name="Width">Its advance width.</param>
internal sealed record HeaderLineSet(
    string Text, double Em, TextRole Role, FontStyle Style, HeaderAlign Align, int Position,
    double InkBottom, double InkTop, double Width)
{
    /// <summary>The ink's height, top to bottom: what a footer takes from its page.</summary>
    /// <remarks>LILYPOND-REF: lily/page-layout-problem.cc Page_layout_problem —
    /// <c>footer_height_</c> is the foot stencil's Y extent, and a text stencil's extent is
    /// its INK (MEASURED 2026-11-13, see PageLayouter's last-bottom spring).</remarks>
    public double Height => InkTop - InkBottom;

    /// <summary>
    /// The arranger of <paramref name="score"/>, or null: the composer's size, role and style.
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/titling-init.ly bookTitleMarkup, lines 91-94 —
    /// <c>\fromproperty #'header:arranger</c> carries no size command, as the composer's does
    /// not. MEASURED 2026-11-13, LilyPond 2.26.0 (LilySharp-Lab sessions/p1226/md/header,
    /// p1.ly): font-size 2.2000, the composer's.</remarks>
    public static HeaderLineSet? Arranger(Model.MultiStaffScore score)
    {
        var lines = score.HeaderLines;
        var fonts = score.TextMetrics;
        return Of(lines.Arranger, lines.ArrangerPosition, fonts, HeaderBand.ComposerEm(fonts),
            TextRole.Composer, HeaderBand.ComposerStyle(fonts), HeaderAlign.Right);
    }

    /// <summary>
    /// The copyright line of <paramref name="score"/>, or null: the text size, in the role of
    /// the page's own header and footer (the page number's).
    /// </summary>
    public static HeaderLineSet? Copyright(Model.MultiStaffScore score)
    {
        var lines = score.HeaderLines;
        var fonts = score.TextMetrics;
        return Of(lines.Copyright, lines.CopyrightPosition, fonts, HeaderBand.CopyrightEm(fonts),
            TextRole.PageNumber, HeaderBand.CopyrightStyle(fonts), HeaderAlign.Center);
    }

    private static HeaderLineSet? Of(string? text, int position, ScoreTextMetrics fonts,
        double em, TextRole role, FontStyle style, HeaderAlign align)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        // A string the face cannot spell (CJK) is drawn from a fallback face this layout never
        // measures — a box is reserved for it, as for every header string (HeaderBand.Build).
        var (inkBottom, inkTop) = fonts.InkOrFallbackBox(text, em, role, style);
        return new HeaderLineSet(text, em, role, style, align, position,
            inkBottom, inkTop, fonts.Advance(text, em, role, style));
    }
}
