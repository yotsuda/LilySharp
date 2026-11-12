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
using System.Text;

namespace LilySharp.Core.Syntax;

/// <summary>
/// What a fenced text's token says (docs/markdown-text-design.md): the words of its opening
/// line, and the text between the fences with the CLOSING fence's indentation taken off
/// each line (the rule of a C# raw string: where the closing fence stands is the text's margin).
/// </summary>
/// <remarks>
/// The token is the whole fence — the lexer reads nothing inside it — so this is the one
/// place that knows its shape. Every offset is into the token's own text.
/// </remarks>
/// <param name="Words">The opening line's words after the backticks, with where each begins.</param>
/// <param name="Body">The text between the fences. Lines end with <c>\n</c>.</param>
/// <param name="Lines">For each line of <see cref="Body"/>: where it begins in the body and
/// where that character stands in the token — what takes a place in the text back to the file.</param>
/// <param name="Closed">Whether a closing fence was written.</param>
public sealed record FencedText(
    IReadOnlyList<(string Text, int Offset)> Words,
    string Body,
    IReadOnlyList<(int InBody, int InToken)> Lines,
    bool Closed)
{
    /// <summary>The word that says the text is Markdown, and its longer spelling.</summary>
    public const string MarkdownWord = "md";

    /// <summary><see cref="MarkdownWord"/> written out.</summary>
    public const string MarkdownLongWord = "markdown";

    /// <summary>The word that puts a top-level or a score's text under the last system.</summary>
    public const string EndWord = "end";

    /// <summary>The word that prints a section's text at every printing of the section.</summary>
    public const string AllWord = "all";

    /// <summary>Whether the opening line begins with <c>md</c> or <c>markdown</c>.</summary>
    public bool IsMarkdown => Words.Count > 0
        && (Words[0].Text == MarkdownWord || Words[0].Text == MarkdownLongWord);

    /// <summary>Reads a <see cref="SyntaxKind.FencedText"/> token's text.</summary>
    /// <param name="token">The token's text, from its first backtick.</param>
    public static FencedText Read(string token)
    {
        int indent = 0;
        int ticks = 0;
        while (ticks < token.Length && token[ticks] == '`')
            ticks++;

        int lineEnd = token.IndexOf('\n', ticks);
        int openingEnd = lineEnd < 0 ? token.Length : lineEnd;
        var words = new List<(string, int)>();
        for (int i = ticks; i < openingEnd;)
        {
            if (char.IsWhiteSpace(token[i])) { i++; continue; }
            int start = i;
            while (i < openingEnd && !char.IsWhiteSpace(token[i]))
                i++;
            words.Add((token[start..i], start));
        }
        if (lineEnd < 0)
            return new FencedText(words, "", [], Closed: false);

        // The closing fence, if the token ends with one: the last line, blanks then backticks.
        int bodyEnd = token.Length;
        bool closed = false;
        int lastLine = token.LastIndexOf('\n') + 1;
        {
            int i = lastLine;
            while (i < token.Length && (token[i] == ' ' || token[i] == '\t'))
                i++;
            int run = i;
            while (i < token.Length && token[i] == '`')
                i++;
            if (i - run >= ticks && i == token.Length && lastLine > lineEnd)
            {
                closed = true;
                bodyEnd = lastLine;
                indent = run - lastLine;
            }
        }

        var body = new StringBuilder();
        var lines = new List<(int, int)>();
        for (int at = lineEnd + 1; at < bodyEnd;)
        {
            int next = token.IndexOf('\n', at);
            int end = next < 0 || next >= bodyEnd ? bodyEnd : next + 1;
            int from = at;
            for (int taken = 0; taken < indent && from < end && (token[from] == ' ' || token[from] == '\t'); taken++)
                from++;
            lines.Add((body.Length, from));
            foreach (char c in token.AsSpan(from, end - from))
            {
                if (c != '\r')
                    body.Append(c);
            }
            at = end;
        }
        return new FencedText(words, body.ToString(), lines, closed);
    }

    /// <summary>Where a place in <see cref="Body"/> stands in the token.</summary>
    /// <remarks>Exact for a file with no carriage returns inside the line before the place; a
    /// line's own beginning is always exact.</remarks>
    public int TokenOffsetOf(int bodyOffset)
    {
        for (int i = Lines.Count - 1; i >= 0; i--)
        {
            if (Lines[i].InBody <= bodyOffset)
                return Lines[i].InToken + (bodyOffset - Lines[i].InBody);
        }
        return 0;
    }
}
