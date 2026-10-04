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
/// A <c>@todo</c> mark: a note, rest or chord left to check — <c>@todo</c>,
/// <c>@todo("memo")</c>, <c>@todo(key)</c> or <c>@todo(key "memo")</c>.
/// </summary>
/// <remarks>
/// <para>
/// The owner's decision of 2026-10-05 (LilySharp-Omr proposal A1): a general "check this
/// later" mark, which an OMR reader writes on the symbols it is unsure of and a writer on
/// anything. It changes neither the page nor the playback; the compiler reports each one
/// (<see cref="DiagnosticCodes.TodoMark"/>), the preview draws the marked head red
/// (<c>data-todo</c> on its glyph), and resolving it is deleting it.
/// </para>
/// <para>
/// The two arguments are told apart by their QUOTES alone: a string is the memo, a bare word
/// the key. The key is what a tool's side file (the OMR's <c>x.omr.json</c>) links the mark
/// by, so a hand-written mark needs none. Both reuse spellings the annotations already have —
/// a bare word as in <c>@bend(full)</c>, a string as in <c>@text("dolce")</c>, two arguments
/// side by side as in <c>@figuredBass(6 4)</c> — so no token is new.
/// </para>
/// </remarks>
/// <param name="Key">The key, or null when none is written.</param>
/// <param name="Memo">The memo's text without its quotes, or null when none is written.</param>
/// <param name="Problem">What is wrong with the arguments, or null; when set, neither
/// <paramref name="Key"/> nor <paramref name="Memo"/> is read.</param>
public sealed record TodoAnnotation(string? Key, string? Memo, string? Problem)
{
    /// <summary>The annotation's name.</summary>
    public const string Name = "todo";

    private static readonly TodoAnnotation Bare = new(null, null, null);

    /// <summary>
    /// The todo an annotation node writes, or null when it is not one. A bare <c>@todo</c>
    /// is an articulation node, <c>@todo(…)</c> a mark node; <c>@!todo</c> is neither (there
    /// is nothing to end).
    /// </summary>
    public static TodoAnnotation? Of(SyntaxNode annotation) => annotation switch
    {
        ArticulationSyntax art when string.Equals(art.NameToken.Text, Name, StringComparison.Ordinal) => Bare,
        MusicMarkSyntax mark when !mark.IsSpanEnd && string.Equals(mark.Name, Name, StringComparison.Ordinal)
            => FromArguments(mark.Arguments),
        _ => null,
    };

    /// <summary>The first todo among a host's annotations (a note's, a rest's, a chord's), or null.</summary>
    public static TodoAnnotation? OfAny(IEnumerable<SyntaxNode> annotations)
    {
        foreach (var annotation in annotations)
            if (Of(annotation) is { } todo)
                return todo;
        return null;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a key: a letter, then letters, digits, <c>_</c> and
    /// <c>-</c> (<c>o1203</c>, <c>b4-lh</c>).
    /// </summary>
    public static bool IsKey(string text)
        => text.Length > 0 && char.IsAsciiLetter(text[0])
           && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>The diagnostic's text: <c>TODO</c>, or <c>TODO: memo</c>.</summary>
    public string Message => string.IsNullOrEmpty(Memo) ? "TODO" : "TODO: " + Memo;

    private static TodoAnnotation FromArguments(IReadOnlyList<MarkArgument> arguments)
    {
        const string Shape = "a todo takes a key, a quoted memo, or both: "
                             + "@todo(o12), @todo(\"memo\"), @todo(o12 \"memo\")";
        static string? MemoOf(MarkArgument a) => a.Value is LysValue.Str s ? s.V : null;

        switch (arguments.Count)
        {
            case 0:
                return Bare;
            case 1 when MemoOf(arguments[0]) is { } memo:
                return new(null, memo, null);
            case 1 when IsKey(arguments[0].Text):
                return new(arguments[0].Text, null, null);
            case 2 when IsKey(arguments[0].Text) && MemoOf(arguments[1]) is { } memo:
                return new(arguments[0].Text, memo, null);
            default:
                return new(null, null,
                    arguments.Count is 1 or 2 && !IsKey(arguments[0].Text) && MemoOf(arguments[0]) is null
                        ? $"'{arguments[0].Text}' is not a key (a letter, then letters, digits, '_' "
                          + "and '-'); " + Shape + "."
                        : Shape + ".");
        }
    }
}
