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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Validates that there is at most one top-level <c>structure</c> declaration,
/// and at most one inside each <c>score { }</c> block. The top-level structure is
/// the piece's default form (the order sections play in, with repeats and
/// navigation), shared by every <c>score</c> render and by MIDI; a score may carry
/// its own <c>structure</c> to override that default for that score only (e.g. a
/// practice excerpt). A second structure in the same scope was previously silently
/// ignored (last-wins), which is a footgun. Omitting <c>structure</c> entirely is
/// still valid — sections then play in declaration order.
/// </summary>
internal sealed class StructureDeclarationValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var structures = tree.GetNodes<StructureDeclarationSyntax>().ToList();

        // Group by owning scope: the enclosing score block, or null for top level.
        // The first declaration in each scope is the effective one; flag the rest.
        var byScope = structures.GroupBy(EnclosingScore);
        foreach (var scope in byScope)
        {
            bool inScore = scope.Key != null;
            int n = 0;
            foreach (var structure in scope)
            {
                if (n++ == 0) continue;
                var keyword = structure.StructureKeyword;
                _diagnostics.Error(
                    keyword.Span,
                    DiagnosticCodes.MultipleStructureDeclarations,
                    inScore
                        ? "Only one 'structure' declaration is allowed per score. "
                          + "Remove the extra declaration."
                        : "Only one top-level 'structure' declaration is allowed; "
                          + "it defines the default form shared by all scores and MIDI. "
                          + "A score may carry its own 'structure' to override it. "
                          + "Remove the extra declaration.");
            }
        }
    }

    private static RenderDeclarationSyntax? EnclosingScore(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is RenderDeclarationSyntax render)
                return render;
        return null;
    }
}
