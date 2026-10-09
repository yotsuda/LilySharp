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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// The member each line of a C# file stands in — <c>Type.Member</c>, or <c>Type</c> between
/// members — which the generated inventories (<see cref="ApproximationInventoryTests"/>,
/// <see cref="MagicConstantInventoryTests"/>) print where they used to print a line number.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ WHY NOT THE LINE NUMBER (session 871): a line added anywhere above a site renumbered
/// it, so every unrelated edit made both committed files stale — five regenerations in one
/// session, each a diff of shifted numbers that hid the one row that really changed. A member
/// name moves only when the member is renamed, so the diff now shows what changed.
/// </para>
/// <para>
/// A COMMENT belongs to the member it is the trivia of: a doc comment, or a comment above a
/// member, to the member BELOW it (Roslyn attaches leading trivia to the next token); a
/// comment inside a body, to the body's member. A comment between the last member and the
/// type's closing brace is the type's. Overloads share a name — the locator says where to
/// look, and the row's own text says which.
/// </para>
/// </remarks>
internal static class SourceMemberLocator
{
    /// <summary>
    /// One locator per line of <paramref name="text"/>, split the way
    /// <see cref="System.IO.File.ReadAllLines(string)"/> splits it (CR LF, LF or CR), so the
    /// index is the caller's line index.
    /// </summary>
    public static string[] Of(string text)
    {
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var result = new List<string>();
        int start = 0;
        while (start <= text.Length)
        {
            int end = start;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n')
                end++;
            if (end == text.Length && start == end && start > 0)
                break;   // ReadAllLines does not report the empty line after a final newline
            int pos = start;
            while (pos < end && char.IsWhiteSpace(text[pos]))
                pos++;
            result.Add(Locate(root, Math.Min(pos, Math.Max(0, text.Length - 1))));
            if (end == text.Length)
                break;
            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        }
        return [.. result];
    }

    private static string Locate(SyntaxNode root, int position)
    {
        if (root.FullSpan.Length == 0)
            return "(file)";
        var token = root.FindToken(position);
        string? member = null;
        var types = new List<string>();
        for (var node = token.Parent; node != null; node = node.Parent)
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax type:
                    types.Add(type.Identifier.Text);
                    break;
                case DelegateDeclarationSyntax del:
                    member ??= del.Identifier.Text;
                    break;
                case MemberDeclarationSyntax m when member == null:
                    member = NameOf(m);
                    break;
            }
        }
        if (types.Count == 0)
            return member ?? "(file)";
        types.Reverse();
        string typeName = string.Join(".", types);
        return member == null ? typeName : $"{typeName}.{member}";
    }

    private static string? NameOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor",
        DestructorDeclarationSyntax => "Finalize",
        PropertyDeclarationSyntax p => p.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        EventDeclarationSyntax e => e.Identifier.Text,
        BaseFieldDeclarationSyntax f => string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.Text)),
        OperatorDeclarationSyntax o => "operator " + o.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax c => "operator " + c.Type,
        EnumMemberDeclarationSyntax e => e.Identifier.Text,
        _ => null,   // a namespace, a global statement: no member of a type
    };
}
