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
/// then only the word its place takes — <c>end</c> at the top level and in a score, a
/// printing (<c>2</c>, <c>1,3</c>, <c>2-3</c>, <c>all</c>) directly in a top-level section.
/// </summary>
internal sealed class MarkdownTextValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>The one kind this pass reads.</summary>
    internal static readonly SyntaxKind[] Kinds = [SyntaxKind.MarkdownText];

    public void Validate(SyntaxTree tree)
    {
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

            bool placed = false;
            foreach (var (word, offset) in value.Words.Skip(1))
            {
                var span = new TextSpan(at + offset, word.Length);
                bool known = inSection
                    ? word == FencedText.AllWord || MarkdownTexts.PrintingsOf(word) != null
                    : word == FencedText.EndWord;
                if (!known)
                {
                    _diagnostics.Error(span, DiagnosticCodes.FencedTextWord, inSection
                        ? $"'{word}' is not a printing. A section's text is printed at the section's first printing; "
                          + "say another with a number (```md 2), a list (```md 1,3), a range (```md 2-3) or ```md all."
                        : $"'{word}' means nothing here. This text stands under the title; ```md end puts it under "
                          + "the last system.");
                }
                else if (placed)
                {
                    _diagnostics.Error(span, DiagnosticCodes.FencedTextWord,
                        $"'{word}' is a second place for one text. Write one.");
                }
                placed |= known;
            }

            // What is read and not drawn is said where it is written, never dropped in silence.
            if (inSection)
            {
                _diagnostics.Warning(new TextSpan(at, 3), DiagnosticCodes.FencedTextNotDrawn,
                    "A section's text is read and not printed yet: the page prints the text under the title "
                    + "and the text after the last system (```md end) so far.");
                continue;
            }
            foreach (var note in Rendering.Prose.Read(value.Body).NotDrawn)
            {
                int start = value.TokenOffsetOf(note.Offset);
                int lineEnd = value.Body.IndexOf('\n', note.Offset);
                int length = Math.Max(1, Math.Min(note.Length, (lineEnd < 0 ? value.Body.Length : lineEnd) - note.Offset));
                _diagnostics.Warning(new TextSpan(at + start, length), DiagnosticCodes.FencedTextNotDrawn,
                    $"{note.What} is read and not drawn: a fenced text prints its paragraphs, bold and italic so far.");
            }
        }
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
