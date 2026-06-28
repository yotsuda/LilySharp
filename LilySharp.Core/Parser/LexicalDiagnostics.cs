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
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Parser;

/// <summary>
/// Derives lexical diagnostics (unterminated string literal / block comment) from a
/// finished token stream. Run as a post-pass over the FINAL tokens — by both full and
/// incremental parse — instead of inside the <see cref="Lexer"/>, so the lexer stays
/// stateless (incremental re-lexing splices token streams) and the two parse paths can
/// never disagree on which lexical errors a document has.
/// </summary>
internal static class LexicalDiagnostics
{
    /// <summary>Scans the token stream and returns any unterminated-string /
    /// unterminated-comment diagnostics, positioned by walking the absolute offsets.</summary>
    public static List<Diagnostic> Scan(IReadOnlyList<SyntaxToken> tokens)
    {
        var diagnostics = new List<Diagnostic>();
        int pos = 0;
        foreach (var token in tokens)
        {
            ScanTrivia(token.LeadingTrivia, pos, diagnostics);

            int tokenStart = pos + token.LeadingTriviaWidth;
            if (token.Kind == SyntaxKind.StringLiteral && !StringTerminated(token.Text))
                diagnostics.Add(Diagnostic.Error(
                    new TextSpan(tokenStart, token.Width),
                    DiagnosticCodes.UnterminatedString,
                    "Unterminated string literal (missing closing '\"')."));

            ScanTrivia(token.TrailingTrivia, tokenStart + token.Width, diagnostics);

            pos += token.FullWidth;
        }
        return diagnostics;
    }

    /// <summary>Recursively walks a trivia node (single trivia or trivia list),
    /// flagging any block comment that never closed.</summary>
    private static void ScanTrivia(GreenNode? trivia, int start, List<Diagnostic> diagnostics)
    {
        if (trivia == null)
            return;

        if (trivia.SlotCount == 0)
        {
            if (trivia.Kind == SyntaxKind.BlockCommentTrivia && !CommentTerminated(trivia.Text))
                diagnostics.Add(Diagnostic.Error(
                    new TextSpan(start, trivia.FullWidth),
                    DiagnosticCodes.UnterminatedComment,
                    "Unterminated block comment (missing '*/')."));
            return;
        }

        int offset = start;
        for (int i = 0; i < trivia.SlotCount; i++)
        {
            var child = trivia.GetSlot(i);
            if (child == null)
                continue;
            ScanTrivia(child, offset, diagnostics);
            offset += child.FullWidth;
        }
    }

    /// <summary>A string literal is terminated iff a closing quote is reached before
    /// the end, walking the same escape rule the lexer uses (so "\" at EOF is open).</summary>
    private static bool StringTerminated(string text)
    {
        int i = 1; // skip the opening quote
        while (i < text.Length)
        {
            if (text[i] == '\\') { i += 2; continue; } // escaped char
            if (text[i] == '"') return true;
            i++;
        }
        return false;
    }

    /// <summary>A block comment is terminated iff it ends in "*/" (the lexer stops at
    /// the first close, so any "*/" in the text is the closer).</summary>
    private static bool CommentTerminated(string text)
        => text.Length >= 4 && text.EndsWith("*/", StringComparison.Ordinal);
}
