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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Removes every written string number (<c>\1</c> … <c>\9</c>) from a <c>.lys</c> file, so the
/// tab's fingering planner chooses every string (the owner's request, 2026-10-25: a book
/// brought over from LilyPond carries the strings LilyPond needed written, which the planner
/// would mostly choose by itself).
/// </summary>
/// <remarks>
/// The marks are found by the parser (<see cref="SyntaxKind.StringNumberAnnotation"/>), so a
/// <c>\2</c> inside a string or a comment is left alone. Only the mark's own text goes, and the
/// spaces before it with it when it stood apart between spaces (<c>a' \2 b</c> → <c>a' b</c>).
/// </remarks>
public static class StringNumberRemover
{
    /// <summary>The text without its string numbers, and how many were removed.</summary>
    public sealed record Result(string NewText, int Removed);

    public static Result Remove(string source)
    {
        var root = SyntaxTree.Parse(source).GetRoot();
        var marks = root.DescendantNodesOfKinds(SyntaxKind.StringNumberAnnotation)
            .Cast<StringNumberAnnotationSyntax>()
            .Select(n => n.StringNumberToken.Span)
            .OrderByDescending(s => s.Start)
            .ToList();
        var text = new System.Text.StringBuilder(source);
        foreach (var span in marks)
        {
            int start = span.Start, end = span.End;
            bool standsApart = start > 0 && source[start - 1] is ' ' or '\t'
                && (end >= source.Length || char.IsWhiteSpace(source[end]));
            if (standsApart)
                while (start > 0 && source[start - 1] is ' ' or '\t') start--;
            text.Remove(start, end - start);
        }
        return new Result(text.ToString(), marks.Count);
    }
}
