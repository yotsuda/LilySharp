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
/// Validates that a score has at most one <c>structure</c> declaration. The
/// structure is the piece's single form (the order sections play in, with
/// repeats and navigation), shared by every <c>score</c> render and by MIDI;
/// a second one was previously silently ignored (last-wins), which is a footgun.
/// Omitting <c>structure</c> entirely is still valid — sections then play in
/// declaration order.
/// </summary>
public sealed class StructureDeclarationValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var structures = tree.GetNodes<StructureDeclarationSyntax>().ToList();

        // The first declaration is the effective one; flag every extra.
        for (int i = 1; i < structures.Count; i++)
        {
            var keyword = structures[i].StructureKeyword;
            _diagnostics.Error(
                new TextSpan(keyword.Position, keyword.FullWidth),
                DiagnosticCodes.MultipleStructureDeclarations,
                "Only one 'structure' declaration is allowed per file; "
                + "it defines the single form shared by all scores and MIDI. "
                + "Remove the extra declaration.");
        }
    }
}
