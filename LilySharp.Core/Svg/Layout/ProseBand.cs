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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>A stretch of one line in one style: its words, where it begins from the line's
/// left end, and where it begins in the text read (−1 when unknown).</summary>
internal sealed record ProsePiece(string Text, double X, FontStyle Style, int Offset);

/// <summary>One line as broken: its baseline below the band's reference point, and its pieces.</summary>
/// <param name="Baseline">The baseline below the band's reference point.</param>
/// <param name="Pieces">The pieces, each from the line's left end.</param>
/// <param name="Width">The line's own width.</param>
/// <param name="Left">The line's left end from the left margin: 0 for a text with no shape,
/// else the left margin's white (<c>xMargin</c>), the frame's inset and, centred, half the white the line leaves.</param>
/// <param name="Scale">The line's size over the text's: 1, or a heading's
/// (<see cref="ProseBand.HeadingScale"/>). Its box grows with it.</param>
/// <param name="Marker">The marker of the list item the line begins — its X from the line's
/// left end, so negative: it hangs in the indent — or null.</param>
/// <param name="Indent">The hanging indent of the list the line stands in, from the text's
/// left edge to the item's words; 0 outside a list.</param>
internal sealed record ProseLine(double Baseline, ImmutableArray<ProsePiece> Pieces, double Width, double Left = 0,
    double Scale = 1, ProsePiece? Marker = null, double Indent = 0)
{
    /// <summary>Whether the line is a list item's: it keeps its hanging shape under <c>center</c>.</summary>
    public bool InList => Indent > 0 || Marker is not null;
}

/// <summary>
/// A fenced text's shape in staff spaces (owner-approved 2026-11-13): the opening line's
/// <c>xMargin</c>, <c>yMargin</c>, <c>center</c> and <c>box</c> on THIS paper's staff space.
/// </summary>
/// <param name="MarginLeft">The white at the text's left, inside the line width.</param>
/// <param name="MarginRight">The white at its right.</param>
/// <param name="Center">Whether each line is centred.</param>
/// <param name="Box">Whether a frame is ruled around the text.</param>
/// <param name="MarginTop">The white above the text, or null for
/// <see cref="ProseBand.DefaultMargin"/>: the line wrote no <c>yMargin</c>.</param>
/// <param name="MarginBottom">The white below it, or null likewise.</param>
internal readonly record struct ProseShape(double MarginLeft, double MarginRight, bool Center, bool Box,
    double? MarginTop = null, double? MarginBottom = null)
{
    /// <summary><paramref name="shape"/> on a staff space of <paramref name="staffSpaceMm"/>
    /// millimetres, through the conversion <c>paper { }</c>'s lengths take.</summary>
    public static ProseShape Of(Syntax.TextShape shape, double staffSpaceMm)
    {
        double In(Syntax.TextLength length)
            => Semantics.PaperPlanReader.ToStaffSpaces(length.Value, length.Unit, staffSpaceMm);
        return new(In(shape.MarginLeft), In(shape.MarginRight), shape.Center, shape.Box,
            shape.MarginTop is { } top ? In(top) : null, shape.MarginBottom is { } bottom ? In(bottom) : null);
    }
}

/// <summary>The frame ruled around a boxed text: its OUTER left and right edges from the left
/// margin, and its rule. Its outer top and bottom are the band's own <c>Up</c> and <c>Down</c>.</summary>
internal sealed record ProseFrame(double Left, double Right, double Thickness);

/// <summary>
/// A fenced text set for the page (docs/markdown-text-design.md): its lines, broken to the
/// line width, about a reference point that is the FIRST line's baseline at the left margin.
/// </summary>
/// <remarks>
/// The page spaces it as LilyPond spaces a top-level markup — a line of the page's chain, by
/// <c>markup-markup-spacing</c>, <c>markup-system-spacing</c> and <c>score-markup-spacing</c>
/// — and every line is a box that does not depend on its letters, as a <c>\markuplist</c>'s
/// lines are: <see cref="LineUp"/> above the baseline and <see cref="LineDown"/> below.
/// LILYPOND-REF: scm/markup.scm space-lines — each line of a top-level markup list padded to
/// two thirds of the baseline-skip above its baseline and one third below.
/// MEASURED 2026-11-12, LilyPond 2.26.0 (LilySharp-Lab sessions/p1226/md): <c>e10.ly</c>'s
/// lines step 3.000000, and its last baseline stands 3.276 above the staff's top line — 1.0
/// below the baseline, the padding 0.5, the clef's 1.776 over the top line.
/// LILYSHARP-OWN: the breaking itself (LilyPond's <c>\wordwrap</c> does not break a text
/// with no spaces — <c>e5.ly</c>), and the white between two paragraphs.
/// Headings and lists (owner's request 2026-11-13) are lines of the same kind: a heading's
/// line is larger (<see cref="HeadingScale"/>) and its box with it; a list item's lines
/// begin at the list's indent and its first carries the marker.
/// </remarks>
/// <param name="Lines">The lines, top to bottom.</param>
/// <param name="Up">How far the band reaches above its reference point.</param>
/// <param name="Down">How far it reaches below it.</param>
/// <param name="Width">How far it reaches right of the left margin: its widest line, or — with
/// a shape — the frame's outer right edge or the furthest line's end.</param>
/// <param name="Position">Where its fence begins in the file (data-pos), or 0.</param>
/// <param name="Left">Where the text begins, from the left margin (<c>xMargin</c>'s white, and a frame's inset).</param>
/// <param name="TextWidth">The width the lines were broken to.</param>
/// <param name="Frame">The frame of a boxed text, or null. Its outer top stands
/// <paramref name="MarginTop"/> inside <paramref name="Up"/> and its outer bottom
/// <paramref name="MarginBottom"/> inside <paramref name="Down"/>.</param>
/// <param name="MarginTop">The white above the text (<c>yMargin</c>, or
/// <see cref="DefaultMargin"/>), already in <paramref name="Up"/> — so the page's springs
/// and the page breaker's priced depth see it.</param>
/// <param name="MarginBottom">The white below it, already in <paramref name="Down"/>.</param>
internal sealed record ProseBand(
    ImmutableArray<ProseLine> Lines, double Up, double Down, double Width, int Position,
    double Left = 0, double TextWidth = 0, ProseFrame? Frame = null, double MarginTop = 0, double MarginBottom = 0)
{
    /// <summary>The rule of a boxed text's frame.</summary>
    /// <remarks>LILYPOND-REF: scm/define-markup-commands.scm box — <c>thickness</c> 1, times
    /// the layout's line-thickness 0.1; scm/stencil.scm box-stencil rules it OUTSIDE the
    /// padded extent.</remarks>
    public const double BoxThickness = 0.1;

    /// <summary>The white between a boxed text's line boxes and its frame's rule, on all four sides.</summary>
    /// <remarks>LILYSHARP-OWN (owner-approved 2026-11-13: a ruled frame; the padding is this
    /// choice). LilyPond's <c>box-padding</c> default 0.2 hugs a single word; a paragraph
    /// block set at 2.2 wants about half its em. MEASURED 2026-11-13, LilyPond 2.26.0
    /// (LilySharp-Lab sessions/p1226/md/fencelayout box-pad.ly): see docs/markdown-text-design.md
    /// section 5.1 for the numbers.</remarks>
    public const double BoxPadding = 1.0;

    /// <summary>The white the page keeps above and below a text — a boxed text's frame, a
    /// plain text's line boxes — beyond what its chain keeps around a markup, where the
    /// opening line writes no <c>yMargin</c>.</summary>
    /// <remarks>
    /// LILYSHARP-OWN: one staff space each way. LilyPond's own spacing is <c>yMargin 0</c>.
    /// It began as a boxed text's alone (the owner on seeing the frame, 2026-11-13: "the white
    /// above and below the box looks too tight" — a rule drawn ON the line box's edge fills
    /// the white that letters leave in it; with no margin the composer's line stood 0.95 over
    /// the rule and a rehearsal mark as near under it, as LilyPond's own <c>\box</c> in a
    /// markup list stands: 4.0705 from the composer's baseline to the boxed first baseline).
    /// Then every text's (the owner, the same day: "the white above and below the md text is
    /// tight; widen it a little, or make it settable" — both: this, and <c>yMargin</c>). A
    /// box's frame stands where it stood: the two are one margin, not added.
    /// ⚠️ The margin is part of the band's EXTENT (<c>Up</c>, <c>Down</c>), as the twin's
    /// <c>\with-dimension</c> is of its line's: it moves the text where the chain's PADDING
    /// rules — under the composer's line, over a system whose clef or mark reaches up — and
    /// is taken out of the distance where a basic-distance rules (the 12 from the last
    /// staff's middle line to an <c>end</c> text's first baseline holds until the margin
    /// and the padding outgrow it).
    /// </remarks>
    public const double DefaultMargin = 1.0;

    /// <summary>The most a <c>yMargin</c> length may be, in staff spaces.</summary>
    /// <remarks>LILYSHARP-OWN: 50 — about 88 mm, under a third of an A4 page's height at the
    /// default staff; MarkdownTextValidator refuses more (LYS4057) and <see cref="Build"/>
    /// clamps. Without a limit the page breaks: MEASURED 2026-11-13 (LilySharp-Lab
    /// sessions/p1226/md/margins/limit, the limit lifted for the run), <c>yMargin 100</c>
    /// under the title or on an <c>end</c> text of an eight-bar piece drew page 1 with its
    /// one system pushed off the paper — the text is not broken over a page, and the music
    /// does not leave it — under the warning "page 1 is over-full by 68.5 staff spaces"
    /// (268.5 at <c>yMargin 200</c>). At 50 above and below the same piece is one whole page
    /// with no warning. A mistyped unit (<c>20cm</c> for <c>20mm</c>) is the likely writer of more.</remarks>
    public const double MaximumMargin = 50.0;

    /// <summary>The least width a text keeps whatever its side margins ask.</summary>
    /// <remarks>LILYSHARP-OWN: ten staff spaces — four or five ems of the text, a short word
    /// a line. MarkdownTextValidator refuses margins that leave less on the paper the fence
    /// stands under; on another score's narrower paper the margins give way in proportion.</remarks>
    public const double MinimumWidth = 10.0;

    /// <summary>How far a boxed text stands inside its frame's outer edge: the padding and the rule.</summary>
    public static double InsetOf(ProseShape shape) => shape.Box ? BoxPadding + BoxThickness : 0;

    /// <summary>The width <paramref name="shape"/> leaves the text on a line of
    /// <paramref name="lineWidth"/>, as written (it may be less than <see cref="MinimumWidth"/>).</summary>
    public static double WidthLeft(ProseShape shape, double lineWidth)
        => lineWidth - 2 * InsetOf(shape) - shape.MarginLeft - shape.MarginRight;

    /// <summary>The text's size: the 11pt text font at a 20pt staff, the poet / composer line's.</summary>
    /// <remarks>MEASURED: <c>e1.ly</c>, font-size 2.2000 on a top-level markup.</remarks>
    public const double FontSize = HeaderBand.ComposerFontSize;

    /// <summary>Baseline to baseline inside a paragraph, at <see cref="FontSize"/>.</summary>
    /// <remarks>LILYPOND-REF: scm/define-markup-commands.scm — baseline-skip 3 is the
    /// markup default. MEASURED: <c>e2.ly</c>, <c>e10.ly</c>, 3.000000.</remarks>
    public const double BaselineSkip = 3.0;

    /// <summary>The white added between two paragraphs, in baseline-skips.</summary>
    /// <remarks>LILYSHARP-OWN: half a line. LilyPond has no paragraph; the twin writes it as
    /// the first line's taller box.</remarks>
    public const double ParagraphGap = 0.5;

    /// <summary>A heading's size in LilyPond's font-size steps over the text: <c>#</c> two,
    /// <c>##</c> one (owner's request 2026-11-13: headings are drawn).</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm fontsize — each step a magstep of
    /// 2^(1/6); MEASURED 2026-11-12 (docs/markdown-text-design.md section 6):
    /// <c>\fontsize #2</c> on the text's 2.2000 is 2.7719.
    /// LILYSHARP-OWN: which steps. Bold at +2 is 2.77, the size (and weight) of the piece's
    /// SUBTITLE (HeaderBand.SubtitleFontSize) and clearly under its title's 3.49; bold at +1
    /// is 2.47, between that and the text. A heading in a page text never reaches the title.
    /// </remarks>
    public static int HeadingSteps(int level) => level switch { 1 => 2, 2 => 1, _ => 0 };

    /// <summary>A heading's size over the text's: 2^(steps/6). 1 for a paragraph.</summary>
    public static double HeadingScale(int level) => level == 0 ? 1 : Math.Pow(2, HeadingSteps(level) / 6.0);

    /// <summary>The white over a heading that is not the text's first line, in baseline-skips.</summary>
    /// <remarks>LILYSHARP-OWN: a whole line, twice a paragraph's — the heading belongs to
    /// what follows it. LilyPond has no heading; the twin writes the white as the line's
    /// taller box.</remarks>
    public const double HeadingGapAbove = 1.0;

    /// <summary>The white under a heading, in baseline-skips.</summary>
    /// <remarks>LILYSHARP-OWN: a quarter of a line, half a paragraph's.</remarks>
    public const double HeadingGapBelow = 0.25;

    /// <summary>A list level's indent in ems of the text: from the level's left edge to its
    /// items' words. A numbered list whose widest number needs more takes more.</summary>
    /// <remarks>LILYSHARP-OWN: 1.5 em (3.3 staff spaces at the text's 2.2).</remarks>
    public const double ListIndent = 1.5;

    /// <summary>The white between a marker's right end and its item's words, in ems. Markers
    /// are set flush RIGHT against it, so the stops of <c>9.</c> and <c>10.</c> align.</summary>
    /// <remarks>LILYSHARP-OWN: half an em.</remarks>
    public const double MarkerGap = 0.5;

    /// <summary>
    /// The marker drawn for <paramref name="marker"/> in a list <paramref name="depth"/>
    /// lists deep: a number as written; for a bullet list a bullet (U+2022) at the odd depths
    /// and an en dash (U+2013) at the even ones, or a hyphen where the text's face has neither.
    /// </summary>
    /// <remarks>LILYSHARP-OWN. The second level's mark differs from the first's, as is the
    /// custom; an open circle (U+25E6) is not in the bundled text face, the dash is.</remarks>
    public static string MarkerGlyph(string marker, int depth, ScoreTextMetrics fonts)
    {
        if (marker != Prose.Bullet)
            return marker;
        double em = Em(fonts);
        var style = StyleOf(fonts, FontStyle.Regular);
        string glyph = depth % 2 == 1 ? Prose.Bullet : "–";
        return fonts.Spells(glyph, em, TextRole.Markdown, style) ? glyph : "-";
    }

    /// <summary>The text's em for THIS score.</summary>
    public static double Em(ScoreTextMetrics fonts) => fonts.Size(TextRole.Markdown, FontSize);

    /// <summary>Baseline to baseline for THIS score: the skip, as large as the text is set.</summary>
    public static double Skip(ScoreTextMetrics fonts) => BaselineSkip * Em(fonts) / FontSize;

    /// <summary>A line's box above its baseline: two thirds of the skip.</summary>
    public static double LineUp(ScoreTextMetrics fonts) => Skip(fonts) * 2.0 / 3.0;

    /// <summary>A line's box below its baseline: one third of the skip.</summary>
    public static double LineDown(ScoreTextMetrics fonts) => Skip(fonts) / 3.0;

    /// <summary>The style a run is drawn in: the Markdown's over the score's own for the text.</summary>
    public static FontStyle StyleOf(ScoreTextMetrics fonts, FontStyle run)
        => fonts.Style(TextRole.Markdown, FontStyle.Regular) | run;

    /// <summary>
    /// Sets <paramref name="markdown"/> to <paramref name="lineWidth"/>, or null when it
    /// draws nothing (an empty fence, or one that writes only what is not drawn).
    /// </summary>
    /// <remarks>
    /// <paramref name="shape"/> (owner-approved 2026-11-13): the side margins
    /// (<c>xMargin</c>) are white inside the line width, so the text is broken that much
    /// narrower and begins that much further right; a frame's OUTER edge stands on the
    /// margins' edges and the text inside it is inset by <see cref="BoxPadding"/> and the
    /// rule, broken narrower again; <c>center</c> sets each line in the middle of the text's
    /// width (a line wider than it — one long word — keeps the left edge). The frame's rule
    /// and padding, and the white above and below (<c>yMargin</c>, or
    /// <see cref="DefaultMargin"/> — with no shape too), are in <c>Up</c> and <c>Down</c>.
    /// </remarks>
    public static ProseBand? Build(string? markdown, int position, ScoreTextMetrics fonts, double lineWidth,
        ProseShape shape = default)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;
        var prose = Prose.Read(markdown);
        if (prose.Blocks.IsEmpty)
            return null;

        // The side margins give way, in proportion, before the text is narrower than the minimum
        // (or than the line itself, on a page narrower than that).
        double inset = InsetOf(shape);
        double marginLeft = Math.Max(0, shape.MarginLeft), marginRight = Math.Max(0, shape.MarginRight);
        double room = Math.Max(0, lineWidth - 2 * inset);
        double least = Math.Min(MinimumWidth, room);
        if (marginLeft + marginRight > room - least)
        {
            double keep = marginLeft + marginRight > 0 ? (room - least) / (marginLeft + marginRight) : 0;
            marginLeft *= keep;
            marginRight *= keep;
        }
        bool shaped = shape != default;
        double left = marginLeft + inset;
        double textWidth = shaped ? room - marginLeft - marginRight : lineWidth;

        double em = Em(fonts);
        double skip = Skip(fonts);
        var regular = StyleOf(fonts, FontStyle.Regular);
        var lines = ImmutableArray.CreateBuilder<ProseLine>();
        double baseline = 0;
        bool first = true;
        double widest = 0;
        ProseBlock? previous = null;
        double previousScale = 1;
        foreach (var block in prose.Blocks)
        {
            // A heading (owner's request 2026-11-13): bold, larger, its box grown as it is.
            double scale = HeadingScale(block.Heading);
            var set = block.Heading == 0 ? block : block with
            {
                Runs = ImmutableArray.CreateRange(block.Runs, r => r with { Style = r.Style | FontStyle.Bold }),
            };

            // A list's item (same request): each list it stands in indents it — by
            // ListIndent, or by that list's widest marker and the gap when that is more — and
            // the marker hangs in the indent, flush right. Every line of the item begins at
            // the indent, so a wrapped line stands under the item's words.
            double indent = 0;
            for (int level = 0; level < block.Depth; level++)
            {
                string widestMarker = MarkerGlyph(block.Lists[level], level + 1, fonts);
                indent += Math.Max(ListIndent * em,
                    fonts.Advance(widestMarker, em, TextRole.Markdown, regular) + MarkerGap * em);
            }
            ProsePiece? marker = null;
            if (block.Marker is not null)
            {
                string glyph = MarkerGlyph(block.Marker, block.Depth, fonts);
                marker = new ProsePiece(glyph,
                    -MarkerGap * em - fonts.Advance(glyph, em, TextRole.Markdown, regular), FontStyle.Regular, -1);
            }

            // (A list nested until nothing is left keeps an em: its words run over, as one long word does.)
            var broken = Break(set, fonts, em * scale, Math.Max(em, textWidth - indent));
            if (broken.Count == 0 && marker is not null)
                broken.Add(new ProseLine(0, ImmutableArray<ProsePiece>.Empty, 0));      // a marker with no words
            for (int i = 0; i < broken.Count; i++)
            {
                if (!first)
                {
                    // Box to box: the line above's third of its skip, this line's two thirds
                    // — the skip itself between two lines of the text's size, written so, so
                    // that a text of paragraphs is to the last digit what it was.
                    double step = previousScale == 1 && scale == 1
                        ? skip
                        : LineDown(fonts) * previousScale + LineUp(fonts) * scale;
                    baseline += step + (i == 0 ? GapOver(previous!, block) * skip : 0);
                }
                first = false;
                previousScale = scale;
                // Centred (docs/markdown-text-design.md section 5.1): a paragraph and a heading;
                // a list keeps its hanging shape from the text's left edge. LILYSHARP-OWN.
                bool centred = shape.Center && block.Depth == 0;
                double lineLeft = left + indent + (centred ? Math.Max(0, (textWidth - broken[i].Width) / 2) : 0);
                lines.Add(broken[i] with
                {
                    Baseline = baseline, Left = lineLeft, Scale = scale, Marker = i == 0 ? marker : null, Indent = indent,
                });
                widest = Math.Max(widest, lineLeft + broken[i].Width);
            }
            if (broken.Count > 0)
                previous = block;
        }
        if (lines.Count == 0)
            return null;
        var frame = shape.Box ? new ProseFrame(marginLeft, lineWidth - marginRight, BoxThickness) : null;
        // The white above and below (yMargin, or the default): in the band's extent, outside
        // a frame. Clamped, for a caller that did not come through the validator.
        double marginTop = Math.Clamp(shape.MarginTop ?? DefaultMargin, 0, MaximumMargin);
        double marginBottom = Math.Clamp(shape.MarginBottom ?? DefaultMargin, 0, MaximumMargin);
        return new ProseBand(lines.ToImmutable(), LineUp(fonts) * lines[0].Scale + inset + marginTop,
            baseline + LineDown(fonts) * lines[^1].Scale + inset + marginBottom,
            frame is null ? widest : Math.Max(widest, frame.Right), position, left, textWidth, frame,
            marginTop, marginBottom);
    }

    /// <summary>
    /// The white between <paramref name="above"/>'s last line and <paramref name="below"/>'s
    /// first, in baseline-skips: a quarter under a heading, one over a heading, none between
    /// the items of a tight list, half between two paragraphs — and so between a paragraph
    /// and a list, either way round, and between the items of a loose list.
    /// </summary>
    private static double GapOver(ProseBlock above, ProseBlock below)
        => above.Heading != 0 ? HeadingGapBelow
            : below.Heading != 0 ? HeadingGapAbove
            : below.Tight ? 0
            : ParagraphGap;

    /// <summary>One thing a line is made of: a word, a run of spaces, one wide character (with
    /// the punctuation that may not leave it), or a break written by hand.</summary>
    private readonly record struct Atom(string Text, FontStyle Style, int Offset, bool IsSpace, bool IsBreak, bool BreakBefore);

    /// <summary>Breaks one paragraph into lines no wider than <paramref name="lineWidth"/>.</summary>
    /// <remarks>
    /// LILYSHARP-OWN. A line ends at a space, or between two wide characters (kana,
    /// ideographs, hangul), or between a wide character and a word — never before a closing
    /// mark or a stop, never after an opening bracket. A word wider than the line is not
    /// cut: it stands alone and runs over. The spaces a line ends at are not set.
    /// </remarks>
    private static List<ProseLine> Break(ProseBlock block, ScoreTextMetrics fonts, double em, double lineWidth)
    {
        var atoms = Atoms(block);
        var lines = new List<ProseLine>();
        var line = new List<(Atom Atom, double Width)>();
        double width = 0;

        double WidthOf(Atom a)
        {
            // A wide character the text's face does not have is its em square: the face that
            // draws it is the machine's, which this layout does not measure
            // (ScoreTextMetrics.InkOrFallbackBox's remark). ⚠️ So a line of such characters is
            // as long as a face of full squares makes it; under a face with narrower kana it
            // ends short of the margin, and a piece after a change of style stands off it.
            double w = 0;
            var style = StyleOf(fonts, a.Style);
            var narrow = new StringBuilder();
            foreach (char c in a.Text)
            {
                if (!Prose.IsWide(c))
                    narrow.Append(c);
                else if (fonts.Spells(c.ToString(), em, TextRole.Markdown, style))
                    w += fonts.Advance(c.ToString(), em, TextRole.Markdown, style);
                else
                    w += em;
            }
            if (narrow.Length > 0)
                w += narrow.ToString().Trim().Length == 0
                    ? SpaceWidth(fonts, em, style) * narrow.Length
                    : fonts.Advance(narrow.ToString(), em, TextRole.Markdown, style);
            return w;
        }

        void Flush()
        {
            while (line.Count > 0 && line[^1].Atom.IsSpace)
            {
                width -= line[^1].Width;
                line.RemoveAt(line.Count - 1);
            }
            var pieces = ImmutableArray.CreateBuilder<ProsePiece>();
            double x = 0, pieceX = 0;
            StringBuilder? text = null;
            FontStyle style = FontStyle.Regular;
            int offset = -1;
            foreach (var (atom, w) in line)
            {
                if (text is null || atom.Style != style)
                {
                    if (text is not null)
                        pieces.Add(new ProsePiece(text.ToString(), pieceX, style, offset));
                    text = new StringBuilder();
                    style = atom.Style;
                    pieceX = x;
                    offset = atom.Offset;
                }
                text.Append(atom.Text);
                x += w;
            }
            if (text is not null)
                pieces.Add(new ProsePiece(text.ToString(), pieceX, style, offset));
            lines.Add(new ProseLine(0, pieces.ToImmutable(), Math.Max(0, width)));
            line.Clear();
            width = 0;
        }

        foreach (var atom in atoms)
        {
            if (atom.IsBreak)
            {
                Flush();
                continue;
            }
            double w = WidthOf(atom);
            if (atom.IsSpace)
            {
                if (line.Count > 0)
                {
                    line.Add((atom, w));
                    width += w;
                }
                continue;
            }
            if (line.Count > 0 && atom.BreakBefore && width + w > lineWidth + 1e-9)
                Flush();
            line.Add((atom, w));
            width += w;
        }
        if (line.Count > 0)
            Flush();
        return lines;
    }

    private static double SpaceWidth(ScoreTextMetrics fonts, double em, FontStyle style)
    {
        // The space between two letters, kerning and all; a face that measures none keeps a quarter em.
        double w = fonts.Advance("n n", em, TextRole.Markdown, style) - fonts.Advance("nn", em, TextRole.Markdown, style);
        return w > 0 ? w : em * 0.25;
    }

    private static List<Atom> Atoms(ProseBlock block)
    {
        var atoms = new List<Atom>();
        // The paragraph as one string of characters with their styles, so that a word the
        // Markdown cuts in two (a **bo**ld half) is still one word to the breaker.
        var chars = new List<(char C, FontStyle Style, int Offset, bool Soft)>();
        foreach (var run in block.Runs)
        {
            if (run.Text == ProseRun.Break)
            {
                chars.Add(('\n', run.Style, run.Offset, false));
                continue;
            }
            for (int i = 0; i < run.Text.Length; i++)
                chars.Add((run.Text[i], run.Style, run.Offset < 0 ? -1 : run.Offset + i, run.Offset < 0));
        }

        for (int i = 0; i < chars.Count;)
        {
            var (c, style, offset, soft) = chars[i];
            if (c == '\n')
            {
                atoms.Add(new Atom("", style, offset, false, true, false));
                i++;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                int start = i;
                while (i < chars.Count && chars[i].C != '\n' && char.IsWhiteSpace(chars[i].C))
                    i++;
                // A line end inside the paragraph, between two characters that take no space.
                bool joined = soft && i - start == 1 && start > 0 && i < chars.Count
                    && Prose.JoinsWithoutSpace(chars[start - 1].C, chars[i].C);
                if (!joined && atoms.Count > 0)
                    atoms.Add(new Atom(" ", style, -1, true, false, false));
                continue;
            }

            bool wide = Prose.IsWide(c);
            bool previousWide = i > 0 && Prose.IsWide(chars[i - 1].C);
            bool afterSpace = i > 0 && char.IsWhiteSpace(chars[i - 1].C);
            bool breakBefore = atoms.Count > 0
                && (afterSpace || wide || previousWide)
                && !Prose.StaysWithPrevious(c)
                && !(i > 0 && Prose.StaysWithNext(chars[i - 1].C));
            if (wide)
            {
                AddStyled(atoms, c.ToString(), style, offset, breakBefore);
                i++;
                continue;
            }
            // A word: to the next space or wide character; its styles may change inside it.
            bool firstOfWord = true;
            while (i < chars.Count && !char.IsWhiteSpace(chars[i].C) && !Prose.IsWide(chars[i].C))
            {
                int from = i;
                var s = chars[i].Style;
                var text = new StringBuilder();
                while (i < chars.Count && !char.IsWhiteSpace(chars[i].C) && !Prose.IsWide(chars[i].C) && chars[i].Style == s)
                    text.Append(chars[i++].C);
                AddStyled(atoms, text.ToString(), s, chars[from].Offset, firstOfWord && breakBefore);
                firstOfWord = false;
            }
        }
        while (atoms.Count > 0 && atoms[^1].IsSpace)
            atoms.RemoveAt(atoms.Count - 1);
        return atoms;
    }

    private static void AddStyled(List<Atom> atoms, string text, FontStyle style, int offset, bool breakBefore)
        => atoms.Add(new Atom(text, style, offset, false, false, breakBefore));
}
