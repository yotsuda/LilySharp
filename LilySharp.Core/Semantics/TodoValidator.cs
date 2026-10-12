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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports every <c>@todo</c> mark (LYS4026, with its memo), a todo whose arguments do not
/// read (LYS4027) and a key written twice (LYS4028). See <see cref="TodoAnnotation"/>.
/// </summary>
internal sealed class TodoValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
        {
            if (TodoAnnotation.Of(node) is not { } todo)
                continue;
            // A mark left to check is not a problem in the music: Information, so F8 and the Problems
            // list still reach it, after the real warnings (owner's decision 2026-10-12).
            _diagnostics.Add(Diagnostic.Info(MessageSpan(node), DiagnosticCodes.TodoMark, todo.Message));
            if (todo.Problem is { } problem)
                _diagnostics.Warning(node.Span, DiagnosticCodes.TodoArgument, problem);
            else if (todo.Key is { } key && !keys.Add(key))
                _diagnostics.Warning(node.Span, DiagnosticCodes.TodoKeyRepeated,
                    $"the todo key '{key}' is written twice in this file; a key names one mark.");
        }

        // A chords row takes one mark, @todo (owner's decision 2026-10-11, the parser's
        // ChordRowMarks): any other is kept in the tree by the parser and refused here.
        foreach (var host in tree.GetRoot().DescendantNodesOfKinds(SyntaxKind.ChordEntry, SyntaxKind.Rest))
        {
            if (host.Kind == SyntaxKind.Rest && !InChordsRow(host))
                continue;
            foreach (var child in host.ChildNodes())
                if (child.Kind is SyntaxKind.Articulation or SyntaxKind.MusicMark or SyntaxKind.Dynamic
                    && TodoAnnotation.Of(child) == null)
                    _diagnostics.Error(child.Span, DiagnosticCodes.ChordBlockBadMember,
                        $"a chord in a 'chords' row takes one mark, @todo; "
                        + $"'{tree.Text.Substring(child.Span.Start, child.Span.Length)}' marks a note, not a chord symbol.");
        }
    }

    /// <summary>Whether a rest stands in a <c>chords</c> body (its no-chord slot).</summary>
    private static bool InChordsRow(SyntaxNode rest)
    {
        for (var n = rest.Parent; n != null; n = n.Parent)
            if (n.Kind == SyntaxKind.ChordPartBlock)
                return true;
        return false;
    }

    /// <summary>
    /// Where a mark's report is underlined: the memo's text inside its quotes, else the key,
    /// else the word <c>todo</c> — never the whole mark.
    /// </summary>
    /// <remarks>
    /// A todo is a note to oneself, not a mistake, so the editor underlines what it SAYS
    /// (owner's request 2026-10-11: "@todo(\"ここ\") — underline only ここ"); a squiggle over
    /// <c>@todo("…")</c> as a whole read like an error in the mark. The other two reports
    /// (an argument that does not read, a key written twice) are about the mark itself and
    /// keep its span. The quick fixes that delete the mark find it from this span
    /// (LilySharpLanguageServer.TodoMarkAt) — the span alone no longer starts at the '@'.
    /// </remarks>
    internal static TextSpan MessageSpan(SyntaxNode mark)
    {
        SyntaxTokenNode? key = null, word = null;
        foreach (var n in mark.DescendantNodes())
        {
            if (n is not SyntaxTokenNode token)
                continue;
            if (token.Kind == SyntaxKind.StringLiteral)
            {
                // Inside the quotes: `"memo"` and the verbatim `@"memo"`. An empty or unclosed
                // string keeps the whole token, which is then the only text to underline.
                string text = token.Text;
                int open = text.StartsWith('@') ? 2 : 1;
                int close = text.Length > open && text[^1] == '"' ? 1 : 0;
                return text.Length - open - close > 0
                    ? new TextSpan(token.Span.Start + open, text.Length - open - close)
                    : token.Span;
            }
            if (token.Text == TodoAnnotation.Name && word == null)
                word = token;
            else if (word != null && key == null && TodoAnnotation.IsKey(token.Text))
                key = token;
        }
        return (key ?? word)?.Span ?? mark.Span;
    }
}
