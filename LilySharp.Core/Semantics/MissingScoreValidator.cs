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

using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// LYS6014: a book that has music — at least one section — and no <c>score</c> block. The
/// score is the one place a book says what is engraved (owner's decision 2026-10-09); without
/// one the renderer used to draw the FIRST part alone and leave the rest out in silence. A
/// file of phrases or part headers only (no section) has nothing to engrave and is not
/// reported, so a library a book includes with <c>using</c> stays clean.
/// </summary>
internal sealed class MissingScoreValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        if (TopLevelNodes.OfRoot<RenderDeclarationSyntax>(root).Any())
            return;
        var firstSection = root.DescendantNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault();
        if (firstSection is null)
            return;
        _diagnostics.Error(firstSection.SectionKeyword.Span, DiagnosticCodes.MissingScore,
            "This book has music but no score, so nothing says what to engrave. Add one, e.g. "
            + ScoreSkeleton.For(root) + " (the quick fix writes it).");
    }
}
