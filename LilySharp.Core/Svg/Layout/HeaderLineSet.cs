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
/// its baseline below the first line's, and its own ink about its own baseline (up-positive;
/// 0 and 0 for a line with nothing to draw) — what a re-stepped set's ink is rebuilt from
/// (<see cref="HeaderLineSet.Restepped"/>).</summary>
internal sealed record HeaderTextLine(System.Collections.Immutable.ImmutableArray<HeaderLinePiece> Pieces,
    double Width, double Baseline, double InkBottom = 0, double InkTop = 0, bool HasInk = false);

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
/// over the line's own, <see cref="InnerStep"/> under the one before. Default for a plain
/// string, drawn as <see cref="Text"/> alone.</param>
/// <param name="NextRowTop">Where the ink top of the header row under this one stands, below
/// this set's first baseline (y down), when this set has several lines and a row follows it
/// in the header's column — null otherwise. Set by <see cref="HeaderBand.Build"/>; the twin
/// writes it as the column's least depth so LilyPond stacks the next row where the page does.</param>
internal sealed record HeaderLineSet(
    string Text, double Em, TextRole Role, FontStyle Style, HeaderAlign Align, int Position,
    double InkBottom, double InkTop, double Width,
    System.Collections.Immutable.ImmutableArray<HeaderTextLine> Lines = default,
    double? NextRowTop = null)
{
    /// <summary>Whether it is written as Markdown and drawn line by line, piece by piece.</summary>
    public bool IsMarkdown => !Lines.IsDefault;

    /// <summary>The last line's baseline below the first's: 0 for one line or a plain string.</summary>
    public double LastBaseline => IsMarkdown && Lines.Length > 0 ? Lines[^1].Baseline : 0;

    /// <summary>The step between its lines (its second line's baseline under its first), or
    /// null when it has one line.</summary>
    public double? Step => IsMarkdown && Lines.Length > 1 ? Lines[1].Baseline - Lines[0].Baseline : null;

    /// <summary>
    /// The share of the row white that the white between a header line's OWN lines takes.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (owner's decision 2026-11-14): "title 内の改行幅 y と、title と subtitle
    /// の間の幅 y は、少しだけ差をつけたい" — the lines of one header line stand a little closer
    /// than that line stands to the next row, so they read as one block and the next row as
    /// the next thing. LilyPond has no step of its own for a header line of several lines
    /// (a <c>\column</c> in a <c>\header</c> field takes the default baseline-skip); the twin
    /// writes this one. 0.75 judged by eye on the showcase (LilySharp-Lab sessions/p1226/md/header2)
    /// and on a two-line composer over an arranger.
    /// </remarks>
    public const double InnerWhiteShare = 0.75;

    /// <summary>
    /// The step between two lines of a header line written as Markdown, at its size: its
    /// face's ascender and descender at <paramref name="em"/> and, between them,
    /// <see cref="InnerWhiteShare"/> of the white LilyPond's 3.5 baseline-skip
    /// (<see cref="HeaderBand.BaselineSkip"/>) leaves between this line's descender and the
    /// next row's ascender (<paramref name="nextAscender"/>; this line's own ascender when no
    /// row follows — a copyright at the foot, a title with nothing under it).
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (owner's decision 2026-11-14, see <see cref="InnerWhiteShare"/>). The
    /// white is measured between the faces' designed ascender and descender lines
    /// (<see cref="TextFontMetrics.TypoExtents"/>: OS/2 typo, an em apart in the bundled
    /// Schola), not the lines' own ink, so a line of lower-case letters steps as one with
    /// capitals does.
    /// Replaces the page text's 3.0 grown to the size (2026-11-13), which stood the title's
    /// own lines 4.76 apart over a subtitle 3.26 under them — the reverse of the owner's picture.
    /// </remarks>
    public static double InnerStep(ScoreTextMetrics fonts, double em, TextRole role, FontStyle style,
        double? nextAscender = null)
    {
        var (ascender, descender) = TextFontMetrics.TypoExtents(fonts.Face(role, style));
        double up = ascender * em, down = -descender * em;
        double rowWhite = HeaderBand.BaselineSkip - down - (nextAscender ?? up);
        return up + down + InnerWhiteShare * rowWhite;
    }

    /// <summary>Its face's ascender at its size, in staff spaces: what a header line of
    /// several lines above it measures its white against (<see cref="InnerStep"/>).</summary>
    public double Ascender(ScoreTextMetrics fonts) => AscenderOf(fonts, Em, Role, Style);

    /// <summary>The ascender of <paramref name="role"/>'s face in <paramref name="style"/> at
    /// <paramref name="em"/>, in staff spaces.</summary>
    public static double AscenderOf(ScoreTextMetrics fonts, double em, TextRole role, FontStyle style)
        => TextFontMetrics.TypoExtents(fonts.Face(role, style)).Ascender * em;

    /// <summary>
    /// This set with its lines <paramref name="step"/> apart, its ink rebuilt from theirs —
    /// itself when it is a plain string or one line.
    /// </summary>
    public HeaderLineSet Restepped(double step)
    {
        if (!IsMarkdown || Lines.Length < 2)
            return this;
        double inkTop = double.NegativeInfinity, inkBottom = double.PositiveInfinity;
        var lines = System.Collections.Immutable.ImmutableArray.CreateBuilder<HeaderTextLine>(Lines.Length);
        for (int k = 0; k < Lines.Length; k++)
        {
            double baseline = k * step;
            var line = Lines[k] with { Baseline = baseline };
            if (line.HasInk)
            {
                inkTop = Math.Max(inkTop, line.InkTop - baseline);
                inkBottom = Math.Min(inkBottom, line.InkBottom - baseline);
            }
            lines.Add(line);
        }
        if (double.IsInfinity(inkTop))
            (inkTop, inkBottom) = (0, 0);
        return this with { Lines = lines.MoveToImmutable(), InkTop = inkTop, InkBottom = inkBottom };
    }

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
        // The step with no row under it (a copyright at the foot, the arranger, the last row);
        // HeaderBand.Build re-steps a set a row follows (Restepped), measured against that row.
        double step = InnerStep(fonts, em, role, style);
        double inkTop = double.NegativeInfinity, inkBottom = double.PositiveInfinity, width = 0;
        var lines = System.Collections.Immutable.ImmutableArray.CreateBuilder<HeaderTextLine>(prose.Lines.Length);
        for (int k = 0; k < prose.Lines.Length; k++)
        {
            double baseline = k * step;
            var pieces = System.Collections.Immutable.ImmutableArray.CreateBuilder<HeaderLinePiece>();
            double x = 0;
            double lineTop = double.NegativeInfinity, lineBottom = double.PositiveInfinity;
            foreach (var run in prose.Lines[k])
            {
                var pieceStyle = style | run.Style;
                pieces.Add(new HeaderLinePiece(run.Text, pieceStyle, x));
                // A string the face cannot spell (CJK) is drawn from a fallback face this
                // layout never measures — a box is reserved for it (InkOrFallbackBox).
                var (bottom, top) = fonts.InkOrFallbackBox(run.Text, em, role, pieceStyle);
                if (run.Text.Trim().Length > 0)
                {
                    lineTop = Math.Max(lineTop, top);
                    lineBottom = Math.Min(lineBottom, bottom);
                }
                x += fonts.Advance(run.Text, em, role, pieceStyle);
            }
            bool hasInk = !double.IsInfinity(lineTop);
            if (hasInk)
            {
                inkTop = Math.Max(inkTop, lineTop - baseline);
                inkBottom = Math.Min(inkBottom, lineBottom - baseline);
            }
            width = Math.Max(width, x);
            lines.Add(new HeaderTextLine(pieces.ToImmutable(), x, baseline,
                hasInk ? lineBottom : 0, hasInk ? lineTop : 0, hasInk));
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
