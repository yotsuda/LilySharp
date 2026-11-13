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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The opening line of a fenced text (docs/markdown-text-design.md): it says <c>md</c>, and
/// then only the words its place takes — <c>end</c> and the shape (<c>box</c>, <c>center</c>,
/// <c>xMargin LEFT [RIGHT]</c>, <c>yMargin TOP [BOTTOM]</c>; owner-approved 2026-11-13) at
/// the top level and in a score, a printing (<c>2</c>, <c>1,3</c>, <c>2-3</c>, <c>all</c>)
/// directly in a top-level section.
/// </summary>
internal sealed class MarkdownTextValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>The one kind this pass reads.</summary>
    internal static readonly SyntaxKind[] Kinds = [SyntaxKind.MarkdownText];

    /// <summary>The sentence's list of what a top-level or a score's opening line may say after <c>md</c>.</summary>
    internal const string PageWords =
        "end (under the last system), title, subtitle, composer, poet, arranger or copyright "
        + "(that header line, written as Markdown), box (a ruled frame), center (each line centred), "
        + "xMargin LEFT [RIGHT] (white at the sides: ```md xMargin 20mm), "
        + "yMargin TOP [BOTTOM] (white above and below: ```md yMargin 2)";

    /// <summary>
    /// A top-level or a score's opening line (owner-approved 2026-11-13): <c>end</c> once,
    /// and the shape — <c>box</c>, <c>center</c>, <c>xMargin LEFT [RIGHT]</c>,
    /// <c>yMargin TOP [BOTTOM]</c> — each once, in any order; side margins that leave the text
    /// its least width on the paper the fence stands under, a margin above or below no
    /// more than the most one may be. Each fence is read alone: a later one for the same
    /// place is a block with a shape of its own.
    /// </summary>
    private void ValidatePageWords(SyntaxTree tree, MarkdownTextSyntax text, FencedText value, int at)
    {
        bool placed = false;
        foreach (var (word, offset) in value.Words.Skip(1))
        {
            if (!FencedText.IsPlaceWord(word))
                continue;
            // (`end` beside a header place is said below, in words that name what it takes.)
            if (word == FencedText.EndWord && value.HeaderPlace is not null)
                continue;
            if (placed)
            {
                _diagnostics.Error(new TextSpan(at + offset, word.Length), DiagnosticCodes.FencedTextWord,
                    $"'{word}' is a second place for one text. Write one.");
            }
            placed = true;
        }

        // A header line written as Markdown (owner's decision 2026-11-13) is set where and as
        // that line is: its place takes no shape and no `end`.
        if (value.HeaderPlace is { } place)
        {
            ValidateHeaderPlace(value, place, at);
            return;
        }

        var problems = new List<(int Offset, int Length, string? Message)>();
        var shape = value.ReadShape(problems);
        foreach (var (offset, length, message) in problems)
        {
            string word = text.Fence.Text.Substring(offset, length);
            // `indent` was xMargin's name for the hours before it was paired with yMargin
            // (owner's decision 2026-11-13, never released): no word now, and its sentence
            // alone says what to write.
            string unknown = word == FencedText.FormerXMarginWord
                ? "'indent' is not a word of this line: the white at a text's sides is written 'xMargin' "
                  + "(```md xMargin 20mm), as the white above and below it is 'yMargin'."
                : $"'{word}' means nothing here. This text stands under the title; after ```md "
                  + $"the opening line may say: {PageWords}.";
            _diagnostics.Error(new TextSpan(at + offset, length), DiagnosticCodes.FencedTextWord, message ?? unknown);
        }

        // (A later fence for the same place is a block of its own, with its own shape: until
        // grammar evaluation 4, 2026-11-13, a shape word there was an error — "two fences for
        // one place are one text" — and a boxed note with a centred dedication had no spelling.)
        if (problems.Count > 0)
            return;
        string Mm(double ss, Svg.Layout.LayoutOptions on)
            => (ss * on.StaffSpaceMm).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        // The width the side margins leave, on the paper this fence stands under: the
        // score's own when it is written in a score that names one, else the file's.
        if ((shape.MarginLeft != default || shape.MarginRight != default)
            && LeavesTooLittle(tree.GetRoot(), text, shape, out double width, out var paper))
        {
            var (start, end) = SpanOfMargin(value, FencedText.XMarginWord);
            _diagnostics.Error(new TextSpan(at + start, end - start), DiagnosticCodes.FencedTextWord,
                $"These margins leave the text {Mm(Math.Max(0, width), paper)} mm of a line width of {Mm(paper.ContentWidth, paper)} mm"
                + (shape.Box ? " (the frame takes its padding too)" : "")
                + $". A text keeps at least {Mm(Svg.Layout.ProseBand.MinimumWidth, paper)} mm: write smaller margins.");
        }

        // The white above and below: no more than the most a margin may be.
        if (IsTooTall(tree.GetRoot(), text, shape, out var under))
        {
            var (start, end) = SpanOfMargin(value, FencedText.YMarginWord);
            _diagnostics.Error(new TextSpan(at + start, end - start), DiagnosticCodes.FencedTextWord,
                $"A margin above or below a text is at most {Mm(Svg.Layout.ProseBand.MaximumMargin, under)} mm "
                + $"({Svg.Layout.ProseBand.MaximumMargin.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} staff spaces): "
                + "write a smaller one.");
        }
    }

    /// <summary>What a header place's sentence says it is set as.</summary>
    private static string SetAs(string place) => place switch
    {
        "title" => "the title is: centred, at its size, bold",
        "subtitle" => "the subtitle is: centred under the title, at its size, bold",
        "composer" => "the composer is: at the right margin",
        "poet" => "the poet is: at the left margin, on the composer's line",
        "arranger" => "the arranger is: at the right margin, under the composer",
        _ => "the copyright line is: centred at the first page's foot",
    };

    /// <summary>
    /// A header place's opening line (owner's decision 2026-11-13): <c>```md title</c> and
    /// nothing more. A shape word or <c>end</c> beside it is an error naming what the place
    /// takes; the words or lengths of one error are one span.
    /// </summary>
    private void ValidateHeaderPlace(FencedText value, string place, int at)
    {
        for (int i = 1; i < value.Words.Count; i++)
        {
            var (word, offset) = value.Words[i];
            if (word == FencedText.EndWord)
            {
                _diagnostics.Error(new TextSpan(at + offset, word.Length), DiagnosticCodes.FencedTextWord,
                    $"'end' does not go with '{place}': a header line written as Markdown stands where {SetAs(place)}. "
                    + $"Write ```md {place} alone, or ```md end for a text under the last system.");
                continue;
            }
            if (FencedText.IsPlaceWord(word))
                continue;       // a second place is said above
            int end = offset + word.Length;
            // A margin's lengths go with it: one error.
            if (word is FencedText.XMarginWord or FencedText.YMarginWord)
            {
                while (i + 1 < value.Words.Count && value.Words[i + 1].Text.Length > 0
                       && (char.IsAsciiDigit(value.Words[i + 1].Text[0]) || value.Words[i + 1].Text[0] is '.' or '-' or '+'))
                {
                    i++;
                    end = value.Words[i].Offset + value.Words[i].Text.Length;
                }
            }
            string message = FencedText.ShapeWords.Contains(word)
                ? $"'{word}' does not go with '{place}': a header line written as Markdown is set where and as {SetAs(place)}. "
                  + $"After ```md {place} the opening line takes nothing more."
                : $"'{word}' means nothing here. After ```md {place} the opening line takes nothing more: "
                  + $"the {place} is set where and as {SetAs(place)}.";
            _diagnostics.Error(new TextSpan(at + offset, end - offset), DiagnosticCodes.FencedTextWord, message);
        }
    }

    /// <summary>The margin word <paramref name="word"/> and its lengths, as one span of the token.</summary>
    private static (int Start, int End) SpanOfMargin(FencedText value, string word)
    {
        int first = IndexOfWord(value, word);
        int last = first;
        while (last + 1 < value.Words.Count && TextLength.TryRead(value.Words[last + 1].Text, out _, out _))
            last++;
        return (value.Words[first].Offset, value.Words[last].Offset + value.Words[last].Text.Length);
    }

    /// <summary>
    /// Whether <paramref name="shape"/> leaves <paramref name="text"/> less than its least
    /// width (<see cref="Svg.Layout.ProseBand.MinimumWidth"/>) on the paper the fence stands
    /// under: what this pass refuses, and what the step keys on an <c>xMargin</c>'s length stop
    /// before (Editing.NoteStepper, owner's request 2026-11-13) — one reading for both.
    /// </summary>
    /// <param name="root">The file.</param>
    /// <param name="text">The fence.</param>
    /// <param name="shape">The shape to try: the fence's own, or the one a step would write.</param>
    /// <param name="width">The width the shape leaves, in staff spaces (it may be negative).</param>
    /// <param name="paper">The paper it was measured on.</param>
    internal static bool LeavesTooLittle(SyntaxNode root, MarkdownTextSyntax text, TextShape shape,
        out double width, out Svg.Layout.LayoutOptions paper)
    {
        paper = PaperOf(root, text);
        var set = Svg.Layout.ProseShape.Of(shape, paper.StaffSpaceMm);
        width = Svg.Layout.ProseBand.WidthLeft(set, paper.ContentWidth);
        return width < Svg.Layout.ProseBand.MinimumWidth - 1e-9;
    }

    /// <summary>
    /// Whether <paramref name="shape"/>'s <c>yMargin</c> asks more white above or below
    /// <paramref name="text"/> than a margin may be
    /// (<see cref="Svg.Layout.ProseBand.MaximumMargin"/>) on the paper the fence stands under:
    /// what this pass refuses, and what the step keys on a <c>yMargin</c>'s length stop before.
    /// </summary>
    /// <param name="root">The file.</param>
    /// <param name="text">The fence.</param>
    /// <param name="shape">The shape to try: the fence's own, or the one a step would write.</param>
    /// <param name="paper">The paper it was measured on.</param>
    internal static bool IsTooTall(SyntaxNode root, MarkdownTextSyntax text, TextShape shape,
        out Svg.Layout.LayoutOptions paper)
    {
        paper = PaperOf(root, text);
        var set = Svg.Layout.ProseShape.Of(shape, paper.StaffSpaceMm);
        return Math.Max(set.MarginTop ?? 0, set.MarginBottom ?? 0) > Svg.Layout.ProseBand.MaximumMargin + 1e-9;
    }

    private static int IndexOfWord(FencedText value, string word)
    {
        for (int i = 1; i < value.Words.Count; i++)
        {
            if (value.Words[i].Text == word)
                return i;
        }
        return -1;
    }

    /// <summary>The paper a fence's text is set on where it is written: the file's unnamed
    /// <c>paper { }</c>, or — in a score that writes <c>paper NAME</c> or its own block — that
    /// score's, read as the collector reads it.</summary>
    private static Svg.Layout.LayoutOptions PaperOf(SyntaxNode root, MarkdownTextSyntax text)
    {
        var paper = PaperPlanReader.FileDefault(root) is { } file
            ? PaperPlanReader.Read(file, out _)
            : Svg.Layout.LayoutOptions.Default;
        if (text.Parent is RenderDeclarationSyntax render
            && render.ChildNodes().OfType<PaperDeclarationSyntax>().LastOrDefault() is { } reference)
            paper = PaperPlanReader.ReadReference(root, reference, paper);
        return paper;
    }

    public void Validate(SyntaxTree tree)
    {
        // The header places written as Markdown so far, by scope (where the fence's parent
        // begins: the file, or a score) and place.
        var headerFences = new HashSet<(int Scope, SyntaxKind Kind, string Place)>();
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(Kinds))
        {
            if (node is not MarkdownTextSyntax text)
                continue;
            var value = text.Value;
            int at = text.Fence.Span.Start;
            if (!value.IsMarkdown)
            {
                // The fence's own backticks when it says nothing, else its first word.
                var span = value.Words.Count == 0
                    ? new TextSpan(at, 3)
                    : new TextSpan(at + value.Words[0].Offset, value.Words[0].Text.Length);
                _diagnostics.Error(span, DiagnosticCodes.FencedTextWord,
                    "A fenced text says what it is written in, and Lily# reads Markdown: open it with ```md.");
                continue;
            }

            // A section inside a part is music: a fence there is a stray item to the parser.
            bool inSection = text.Parent is SectionDeclarationSyntax;

            if (inSection)
            {
                bool placed = false;
                foreach (var (word, offset) in value.Words.Skip(1))
                {
                    var span = new TextSpan(at + offset, word.Length);
                    bool known = word == FencedText.AllWord || MarkdownTexts.PrintingsOf(word) != null;
                    if (!known)
                    {
                        _diagnostics.Error(span, DiagnosticCodes.FencedTextWord,
                            $"'{word}' is not a printing. A section's text is printed at the section's first printing; "
                            + "say another with a number (```md 2), a list (```md 1,3), a range (```md 2-3) or ```md all.");
                    }
                    else if (placed)
                    {
                        _diagnostics.Error(span, DiagnosticCodes.FencedTextWord,
                            $"'{word}' is a second place for one text. Write one.");
                    }
                    placed |= known;
                }
            }
            else
            {
                ValidatePageWords(tree, text, value, at);
            }

            // What is read and not drawn is said where it is written, never dropped in silence.
            if (inSection)
            {
                _diagnostics.Warning(new TextSpan(at, 3), DiagnosticCodes.FencedTextNotDrawn,
                    "A section's text is read and not printed yet: the page prints the text under the title "
                    + "and the text after the last system (```md end) so far.");
                continue;
            }

            // A header line written as Markdown (owner's decision 2026-11-13) is ONE text: a
            // second fence for the place in the same scope (the file, or one score) is an
            // error — page texts stack, a header line does not. It takes inline text: what is
            // not is said, and drawn as plain lines.
            if (value.HeaderPlace is { } place)
            {
                var key = (text.Parent?.Span.Start ?? -1, text.Parent?.Kind ?? SyntaxKind.None, place);
                if (!headerFences.Add(key))
                {
                    var word = value.Words.First(w => w.Text == place);
                    _diagnostics.Error(new TextSpan(at + word.Offset, word.Text.Length), DiagnosticCodes.FencedTextWord,
                        $"A second ```md {place}: a header line is one text, and the {place} is already written as Markdown "
                        + $"{(text.Parent is RenderDeclarationSyntax ? "in this score" : "above")}. "
                        + "Write it in one fence — a line break inside it (two spaces or a backslash at a line's end) gives it several lines.");
                }
                foreach (var note in Rendering.HeaderProse.Read(value.Body).NotInline)
                {
                    var span = SpanOf(value, note, at);
                    _diagnostics.Warning(span, DiagnosticCodes.FencedTextNotDrawn,
                        $"{note.What} is not inline text: the {place} takes inline text only — words in bold and italic, "
                        + "and line breaks — so this is drawn as plain lines.");
                }
                continue;
            }
            foreach (var note in Rendering.Prose.Read(value.Body).NotDrawn)
            {
                _diagnostics.Warning(SpanOf(value, note, at), DiagnosticCodes.FencedTextNotDrawn,
                    $"{note.What} is read and not drawn: a fenced text prints its paragraphs, headings and lists, bold and italic so far.");
            }
        }
    }

    /// <summary>Where a note on the body stands in the file: from its place to its line's end.</summary>
    private static TextSpan SpanOf(FencedText value, Rendering.ProseNote note, int at)
    {
        int start = value.TokenOffsetOf(note.Offset);
        int lineEnd = value.Body.IndexOf('\n', note.Offset);
        int length = Math.Max(1, Math.Min(note.Length, (lineEnd < 0 ? value.Body.Length : lineEnd) - note.Offset));
        return new TextSpan(at + start, length);
    }
}

/// <summary>What the words of a fenced text's opening line mean (docs/markdown-text-design.md).</summary>
public static class MarkdownTexts
{
    /// <summary>The printings a word names — <c>2</c>, <c>1,3</c>, <c>2-3</c>, counted from 1 —
    /// or null when it is not such a word.</summary>
    public static IReadOnlyList<int>? PrintingsOf(string word)
    {
        var printings = new List<int>();
        foreach (string piece in word.Split(','))
        {
            string[] ends = piece.Split('-');
            if (ends.Length is < 1 or > 2 || !ends.All(e => e.Length is > 0 and <= 4 && e.All(char.IsAsciiDigit)))
                return null;
            int from = int.Parse(ends[0], System.Globalization.CultureInfo.InvariantCulture);
            int to = int.Parse(ends[^1], System.Globalization.CultureInfo.InvariantCulture);
            if (from < 1 || to < from)
                return null;
            for (int n = from; n <= to; n++)
                printings.Add(n);
        }
        return printings.Count == 0 ? null : printings;
    }
}
