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
using System.Text;

namespace LilySharp.Core.Syntax;

/// <summary>
/// A length as a fenced text's opening line writes it after <c>indent</c>: a number and the
/// unit glued to it, as <c>paper { }</c> writes its own (<c>20mm</c>, <c>2cm</c>,
/// <c>0.5in</c>), or a bare number of staff spaces.
/// </summary>
/// <param name="Value">The number, never negative.</param>
/// <param name="Unit"><c>mm</c>, <c>cm</c>, <c>in</c>, or null for staff spaces.</param>
public readonly record struct TextLength(double Value, string? Unit)
{
    /// <summary>Reads <paramref name="word"/>, or says in <paramref name="why"/> what it is not.</summary>
    public static bool TryRead(string word, out TextLength length, out string why)
    {
        length = default;
        why = "";
        int digits = 0;
        while (digits < word.Length && (char.IsAsciiDigit(word[digits]) || word[digits] is '.' or '-' or '+'))
            digits++;
        string number = word[..digits], unit = word[digits..];
        if (number.StartsWith('-'))
        {
            why = $"'{word}' is negative. An indent is white inside the line width: write 0 or more.";
            return false;
        }
        if (number.Length == 0 || !char.IsAsciiDigit(number[0]) || !char.IsAsciiDigit(number[^1])
            || !double.TryParse(number, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            why = $"'{word}' is not a length. Write a number and its unit with no space between: 20mm, 2cm, 0.5in, "
                + "or a bare number of staff spaces.";
            return false;
        }
        if (unit.Length > 0 && !FencedText.LengthUnits.Contains(unit))
        {
            // Units are case-sensitive, as paper { }'s are (owner's decision 2026-09-27).
            string? right = FencedText.LengthUnits.FirstOrDefault(
                u => string.Equals(u, unit, StringComparison.OrdinalIgnoreCase));
            why = right is not null
                ? $"'{unit}' is not a unit of this language. Units are case-sensitive: write '{right}'."
                : $"'{unit}' is not a unit of this language. A bare number is staff spaces; the physical units "
                  + "are mm, cm and in.";
            return false;
        }
        length = new TextLength(value, unit.Length == 0 ? null : unit);
        return true;
    }
}

/// <summary>
/// How a fenced text is set on the page, from its opening line's words (owner-approved
/// 2026-11-13; docs/markdown-text-design.md section 5): white at its left and right inside
/// the line width, each line centred, a ruled frame around it. The default is none of them.
/// </summary>
/// <param name="IndentLeft">The white at the text's left (<c>indent</c>'s first length).</param>
/// <param name="IndentRight">The white at its right (<c>indent</c>'s second length, or its first).</param>
/// <param name="Center">Whether each line is centred in the text's width (<c>center</c>).</param>
/// <param name="Box">Whether a frame is ruled around the text (<c>box</c>).</param>
public readonly record struct TextShape(TextLength IndentLeft, TextLength IndentRight, bool Center, bool Box)
{
    /// <summary>Whether the opening line wrote none of the words: the text as it has always been set.</summary>
    public bool IsPlain => this == default;
}

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

    /// <summary>The word that sets white at the text's left and right: <c>indent LEFT [RIGHT]</c>.</summary>
    public const string IndentWord = "indent";

    /// <summary>The word that centres each line in the text's width.</summary>
    public const string CenterWord = "center";

    /// <summary>The word that rules a frame around the text.</summary>
    public const string BoxWord = "box";

    /// <summary>The words that shape a top-level or a score's text on the page, in the order
    /// the sentences that list them say them (owner-approved 2026-11-13).</summary>
    public static readonly IReadOnlyList<string> ShapeWords = [BoxWord, CenterWord, IndentWord];

    /// <summary>The units a length after <c>indent</c> is written in — <c>paper { }</c>'s own;
    /// a bare number is staff spaces.</summary>
    public static readonly IReadOnlyList<string> LengthUnits = ["mm", "cm", "in"];

    /// <summary>Whether the opening line begins with <c>md</c> or <c>markdown</c>.</summary>
    public bool IsMarkdown => Words.Count > 0
        && (Words[0].Text == MarkdownWord || Words[0].Text == MarkdownLongWord);

    /// <summary>The shape the opening line's words give the text; what is wrong with them is
    /// left out of it (<see cref="ReadShape"/> says what).</summary>
    public TextShape Shape => ReadShape(null);

    /// <summary>
    /// Reads the opening line's words after <c>md</c> as a top-level or a score's text takes
    /// them (owner-approved 2026-11-13): <c>end</c>, <c>box</c>, <c>center</c> and
    /// <c>indent LEFT [RIGHT]</c>, in any order, each once.
    /// </summary>
    /// <param name="problems">Receives what is wrong, where: the word's offset in the token,
    /// its length, and the sentence — or a null sentence for a word that is none of these,
    /// which the caller says in its own words (it knows the text's place).</param>
    /// <returns>The shape, with every word that reads; a word that does not sets nothing.</returns>
    public TextShape ReadShape(List<(int Offset, int Length, string? Message)>? problems)
    {
        bool box = false, center = false, indented = false;
        TextLength left = default, right = default;
        for (int i = 1; i < Words.Count; i++)
        {
            var (word, offset) = Words[i];
            switch (word)
            {
                case EndWord:
                    break;      // the text's place, not its shape
                case BoxWord:
                    if (box)
                        problems?.Add((offset, word.Length, "'box' is written twice. Write it once."));
                    box = true;
                    break;
                case CenterWord:
                    if (center)
                        problems?.Add((offset, word.Length, "'center' is written twice. Write it once."));
                    center = true;
                    break;
                case IndentWord:
                {
                    // One length, or two: the words that follow and begin as a number does.
                    var lengths = new List<TextLength>();
                    bool bad = false;
                    while (i + 1 < Words.Count && LooksLikeLength(Words[i + 1].Text))
                    {
                        var (text, at) = Words[++i];
                        if (lengths.Count == 2)
                        {
                            problems?.Add((at, text.Length,
                                $"'{text}' is a third length. 'indent' takes one (both sides) or two (left, then right)."));
                            bad = true;
                        }
                        else if (TextLength.TryRead(text, out var length, out string? why))
                            lengths.Add(length);
                        else
                        {
                            problems?.Add((at, text.Length, why));
                            bad = true;
                        }
                    }
                    if (indented)
                    {
                        problems?.Add((offset, word.Length,
                            "'indent' is written twice. Write it once: one length for both sides, or two for left and right."));
                    }
                    else if (lengths.Count == 0 && !bad)
                    {
                        problems?.Add((offset, word.Length,
                            "'indent' needs a length: ```md indent 20mm for both sides, or ```md indent 20mm 10mm "
                            + "for left and right (mm, cm, in, or a bare number of staff spaces)."));
                    }
                    else if (!bad)
                    {
                        left = lengths[0];
                        right = lengths[^1];
                    }
                    indented = true;
                    break;
                }
                default:
                    problems?.Add((offset, word.Length, null));
                    break;
            }
        }
        return new TextShape(left, right, center, box);

        static bool LooksLikeLength(string word)
            => word.Length > 0 && (char.IsAsciiDigit(word[0]) || word[0] is '-' or '+' or '.');
    }

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
