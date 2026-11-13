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
    /// <summary>Beginning at the left margin (the poet).</summary>
    Left,
}

/// <summary>A stretch of a header line written as Markdown in one weight and slant, from the
/// line's left edge.</summary>
internal sealed record HeaderLinePiece(string Text, FontStyle Style, double X);

/// <summary>One line of a header line written as Markdown: its pieces, its advance width,
/// and its baseline below the first line's.</summary>
internal sealed record HeaderTextLine(System.Collections.Immutable.ImmutableArray<HeaderLinePiece> Pieces,
    double Width, double Baseline);

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
/// <param name="InkBottom">Its lowest ink about its baseline (up-positive, so 0 or below) —
/// the first line's baseline where it has several, the lowest line's ink included.</param>
/// <param name="InkTop">Its ink top over its baseline.</param>
/// <param name="Width">Its advance width (the widest line's).</param>
/// <param name="Lines">Its lines, where it is written as Markdown (<c>```md title</c> …;
/// owner's decision 2026-11-13): each in pieces of the weight and slant the Markdown asks for
/// over the line's own, a baseline-skip at its size under the one before. Default for a plain
/// string, drawn as <see cref="Text"/> alone.</param>
internal sealed record HeaderLineSet(
    string Text, double Em, TextRole Role, FontStyle Style, HeaderAlign Align, int Position,
    double InkBottom, double InkTop, double Width,
    System.Collections.Immutable.ImmutableArray<HeaderTextLine> Lines = default)
{
    /// <summary>Whether it is written as Markdown and drawn line by line, piece by piece.</summary>
    public bool IsMarkdown => !Lines.IsDefault;

    /// <summary>
    /// The step from one line of a header line written as Markdown to the next, at its size:
    /// the page text's baseline-skip grown as the line is (3.0 at the text's 2.2; 4.76 for a
    /// title at 3.49) — owner's decision 2026-11-13, "several lines step at that size's
    /// baseline-skip".
    /// </summary>
    /// <remarks>LILYSHARP-OWN, as a heading's lines step (ProseBand, section 5.2). The twin
    /// writes it as the <c>\column</c>'s <c>baseline-skip</c>, so LilyPond steps by it too.</remarks>
    public static double LineStep(double em) => ProseBand.BaselineSkip * em / ProseBand.FontSize;

    /// <summary>
    /// <paramref name="fence"/>'s Markdown set as a header line at <paramref name="em"/> in
    /// <paramref name="role"/>, its own <paramref name="style"/> under the emphasis the
    /// Markdown adds (<c>*…*</c> italic; <c>**…**</c> bold, which a bold line already is) — or
    /// null when it writes nothing to draw.
    /// </summary>
    public static HeaderLineSet? FromFence(Model.HeaderFence fence, ScoreTextMetrics fonts,
        double em, TextRole role, FontStyle style, HeaderAlign align)
    {
        var prose = HeaderProse.Read(fence.Markdown);
        if (prose.Lines.IsEmpty)
            return null;
        double step = LineStep(em);
        double inkTop = double.NegativeInfinity, inkBottom = double.PositiveInfinity, width = 0;
        var lines = System.Collections.Immutable.ImmutableArray.CreateBuilder<HeaderTextLine>(prose.Lines.Length);
        for (int k = 0; k < prose.Lines.Length; k++)
        {
            double baseline = k * step;
            var pieces = System.Collections.Immutable.ImmutableArray.CreateBuilder<HeaderLinePiece>();
            double x = 0;
            foreach (var run in prose.Lines[k])
            {
                var pieceStyle = style | run.Style;
                pieces.Add(new HeaderLinePiece(run.Text, pieceStyle, x));
                // A string the face cannot spell (CJK) is drawn from a fallback face this
                // layout never measures — a box is reserved for it (InkOrFallbackBox).
                var (bottom, top) = fonts.InkOrFallbackBox(run.Text, em, role, pieceStyle);
                if (run.Text.Trim().Length > 0)
                {
                    inkTop = Math.Max(inkTop, top - baseline);
                    inkBottom = Math.Min(inkBottom, bottom - baseline);
                }
                x += fonts.Advance(run.Text, em, role, pieceStyle);
            }
            width = Math.Max(width, x);
            lines.Add(new HeaderTextLine(pieces.ToImmutable(), x, baseline));
        }
        if (double.IsInfinity(inkTop))
            (inkTop, inkBottom) = (0, 0);
        return new HeaderLineSet(prose.Plain, em, role, style, align, fence.Position,
            inkBottom, inkTop, width, lines.MoveToImmutable());
    }

    /// <summary>The title of <paramref name="score"/> written as Markdown, or null: the title's
    /// size and bold, centred.</summary>
    public static HeaderLineSet? TitleFence(Model.MultiStaffScore score)
        => score.HeaderLines.FenceOf("title") is { } f
            ? FromFence(f, score.TextMetrics, HeaderBand.TitleEm(score.TextMetrics), TextRole.Title,
                HeaderBand.TitleStyle(score.TextMetrics), HeaderAlign.Center)
            : null;

    /// <summary>The subtitle written as Markdown, or null: its size and bold, centred.</summary>
    public static HeaderLineSet? SubtitleFence(Model.MultiStaffScore score)
        => score.HeaderLines.FenceOf("subtitle") is { } f
            ? FromFence(f, score.TextMetrics, HeaderBand.SubtitleEm(score.TextMetrics), TextRole.Subtitle,
                HeaderBand.SubtitleStyle(score.TextMetrics), HeaderAlign.Center)
            : null;

    /// <summary>The poet written as Markdown, or null: the text size, at the left margin.</summary>
    public static HeaderLineSet? PoetFence(Model.MultiStaffScore score)
        => score.HeaderLines.FenceOf("poet") is { } f
            ? FromFence(f, score.TextMetrics, HeaderBand.PoetEm(score.TextMetrics), TextRole.Poet,
                HeaderBand.PoetStyle(score.TextMetrics), HeaderAlign.Left)
            : null;

    /// <summary>The composer written as Markdown, or null: the text size, at the right margin.</summary>
    public static HeaderLineSet? ComposerFence(Model.MultiStaffScore score)
        => score.HeaderLines.FenceOf("composer") is { } f
            ? FromFence(f, score.TextMetrics, HeaderBand.ComposerEm(score.TextMetrics), TextRole.Composer,
                HeaderBand.ComposerStyle(score.TextMetrics), HeaderAlign.Right)
            : null;
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
        // Written as Markdown (```md arranger), the page draws that.
        if (lines.FenceOf("arranger") is { } fence)
            return FromFence(fence, fonts, HeaderBand.ComposerEm(fonts), TextRole.Composer,
                HeaderBand.ComposerStyle(fonts), HeaderAlign.Right);
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
        // Written as Markdown (```md copyright), the page draws that: several lines stand
        // with the lowest one's ink on the bottom margin, and the footer is that much taller.
        if (lines.FenceOf("copyright") is { } fence)
            return FromFence(fence, fonts, HeaderBand.CopyrightEm(fonts), TextRole.PageNumber,
                HeaderBand.CopyrightStyle(fonts), HeaderAlign.Center);
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
