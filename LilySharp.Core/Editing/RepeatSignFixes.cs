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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The ways out of LYS1070 — two voices writing different repeat signs on one bar line — as
/// edits an editor can offer (owner's request 2026-11-14: "the fix is plain, but the same bar
/// stands lines apart in another part and its bar line is hard to find"). Read off the same
/// signs the report was made from (<see cref="RepeatSignAgreementValidator.Signs"/>), so a fix
/// can only name the two signs the error named.
/// </summary>
public static class RepeatSignFixes
{
    /// <summary>One way out: its title, and the text to put in place of <c>[Start, End)</c>.</summary>
    public readonly record struct Fix(string Title, int Start, int End, string NewText);

    /// <summary>The fixes for the sign reported at <paramref name="reported"/> (the span of
    /// the diagnostic): write the other voice's sign here, write this one there, or write a
    /// plain <c>|</c> here and follow the other. Empty when no reported sign stands there.</summary>
    public static IReadOnlyList<Fix> For(SyntaxTree tree, TextSpan reported)
    {
        var signs = RepeatSignAgreementValidator.Signs(tree.GetRoot());
        int at = signs.FindIndex(s => s.Node.BarToken.Span.Start == reported.Start);
        if (at < 0)
            return [];
        var here = signs[at];
        var first = signs.First(s => s.Section == here.Section && s.BarsBefore == here.BarsBefore);
        if (first.Text == here.Text || ReferenceEquals(first.Node, here.Node))
            return [];
        return
        [
            new Fix($"Write '{first.Text}' here, as {first.Voice} does", Start(here), End(here), first.Text),
            new Fix($"Write '{here.Text}' in {first.Voice} too", Start(first), End(first), here.Text),
            new Fix($"Write a plain '|' here - it follows the '{first.Text}' of {first.Voice}", Start(here), End(here), "|"),
        ];

        static int Start(RepeatSignAgreementValidator.Sign sign) => sign.Node.Span.Start;
        static int End(RepeatSignAgreementValidator.Sign sign) => sign.Node.Span.Start + sign.Node.Span.Length;
    }
}