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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Resolves every occurrence of a <c>section</c> name in a document: its
/// declaration (<c>section NAME { … }</c>, top-level or part-major inner) and
/// each place a <c>structure { … }</c> plays it — a plain reference
/// (<c>NAME</c>), a silent reference (<c>~NAME</c>), and a volta alternative
/// (<c>[1. NAME]</c> / <c>1. NAME</c>). Powers the editor's "rename a section"
/// refactor, so a section can be renamed everywhere from any single occurrence.
/// </summary>
/// <remarks>
/// A per-occurrence display label (<c>NAME "Reprise"</c>) is a separate string
/// token and is never rewritten — only the section identifier is.
/// </remarks>
public static class SectionReferenceFinder
{
    /// <summary>
    /// Every section-name token in the tree — the declaration name plus each
    /// structure reference — in document order. Empty (missing) tokens from an
    /// incomplete reference are skipped.
    /// </summary>
    public static IReadOnlyList<SyntaxTokenNode> AllSectionNameTokens(SyntaxNode root)
    {
        var tokens = new List<SyntaxTokenNode>();
        foreach (var node in root.DescendantNodes())
        {
            SyntaxTokenNode? tok = node switch
            {
                SectionDeclarationSyntax decl => decl.Name,
                SectionReferenceSyntax r => r.Identifier,
                StructureAlternativeSyntax alt => alt.SectionName,
                // `~NAME` has no dedicated red node — it is a generic node whose
                // children are [ ~ , identifier ].
                { Kind: SyntaxKind.SilentSectionReference } s => s.GetChild(1) as SyntaxTokenNode,
                _ => null,
            };
            if (tok != null && tok.Text.Length > 0)
                tokens.Add(tok);
        }
        return tokens;
    }

    /// <summary>
    /// The section-name token whose span contains <paramref name="offset"/> (end
    /// inclusive, so a caret just past the identifier still resolves), or null
    /// when the offset is not on a section declaration or reference.
    /// </summary>
    public static SyntaxTokenNode? SectionNameTokenAt(SyntaxNode root, int offset)
    {
        foreach (var tok in AllSectionNameTokens(root))
            if (offset >= tok.Span.Start && offset <= tok.Span.End)
                return tok;
        return null;
    }

    /// <summary>All section-name tokens whose text equals <paramref name="name"/>.</summary>
    public static IReadOnlyList<SyntaxTokenNode> Occurrences(SyntaxNode root, string name)
        => AllSectionNameTokens(root).Where(t => t.Text == name).ToList();
}
