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
internal sealed record ProseLine(double Baseline, ImmutableArray<ProsePiece> Pieces, double Width);

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
/// </remarks>
/// <param name="Lines">The lines, top to bottom.</param>
/// <param name="Up">How far the band reaches above its reference point.</param>
/// <param name="Down">How far it reaches below it.</param>
/// <param name="Width">Its widest line.</param>
/// <param name="Position">Where its fence begins in the file (data-pos), or 0.</param>
internal sealed record ProseBand(
    ImmutableArray<ProseLine> Lines, double Up, double Down, double Width, int Position)
{
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
    public static ProseBand? Build(string? markdown, int position, ScoreTextMetrics fonts, double lineWidth)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return null;
        var prose = Prose.Read(markdown);
        if (prose.Blocks.IsEmpty)
            return null;

        double em = Em(fonts);
        double skip = Skip(fonts);
        var lines = ImmutableArray.CreateBuilder<ProseLine>();
        double baseline = 0;
        bool first = true;
        double widest = 0;
        foreach (var block in prose.Blocks)
        {
            var broken = Break(block, fonts, em, lineWidth);
            for (int i = 0; i < broken.Count; i++)
            {
                if (!first)
                    baseline += skip + (i == 0 ? ParagraphGap * skip : 0);
                first = false;
                lines.Add(broken[i] with { Baseline = baseline });
                widest = Math.Max(widest, broken[i].Width);
            }
        }
        if (lines.Count == 0)
            return null;
        return new ProseBand(lines.ToImmutable(), LineUp(fonts), baseline + LineDown(fonts), widest, position);
    }

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
