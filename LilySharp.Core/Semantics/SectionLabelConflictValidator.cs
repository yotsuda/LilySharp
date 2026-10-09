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

namespace LilySharp.Core.Semantics;

/// <summary>
/// LYS1045: two top-level declarations of one section give it different labels
/// (<c>section A "Verse" { key g major }</c> beside <c>section A "Chorus" { }</c>). The first
/// one's label is the section's (<see cref="SyntaxFacts.DeclaredSectionLabel"/>); the warning
/// sits on each later label that disagrees. The same label written twice says one thing and
/// is left alone.
/// </summary>
internal sealed class SectionLabelConflictValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var first = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var section in TopLevelNodes.OfRoot<SectionDeclarationSyntax>(tree.GetRoot()))
        {
            if (section.Label is not { } label || section.LabelToken is not { } token)
                continue;
            if (!first.TryGetValue(section.SectionName, out var kept))
            {
                first[section.SectionName] = label;
                continue;
            }
            if (kept != label)
                _diagnostics.Warning(token.Span, DiagnosticCodes.SectionLabelConflict,
                    $"Section '{section.SectionName}' is already labelled \"{kept}\" by an earlier "
                    + $"declaration, which wins; delete this label or make the two agree.");
        }
    }
}
