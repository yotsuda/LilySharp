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
/// A piece is a grid of (section x part) cells. Each cell — the music for one part
/// in one section — may be filled exactly once, whether written section-major
/// (<c>section A { bass { ... } }</c>) or part-major (<c>part bass { section A { ... } }</c>),
/// and across included files. Two definitions of the same cell would silently
/// collide, so this flags them. (Sections and parts themselves are open: a section
/// may gather music from many parts, and a part may span many sections.)
/// </summary>
internal sealed class DuplicateCellValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var seen = new HashSet<(string section, string part)>();

        foreach (var section in tree.GetRoot().DescendantNodes<SectionDeclarationSyntax>())
        {
            var owningPart = EnclosingPartName(section);
            if (owningPart != null)
            {
                // Part-major: the inner section is itself one cell for its part.
                Record(seen, section.SectionName, owningPart, section.Name);
            }
            else
            {
                // Section-major: each part-block in the section is a cell. Direct children
                // only: a PartBlockSyntax is produced exclusively by ParseSectionItem
                // (Parser.Sections.cs — the Identifier and clef-keyword arms), so every part
                // block is a DIRECT child of its section declaration (the collector's
                // ProcessSectionBody reads them the same way). The descendant walk this
                // replaced visited every note of the section to find its part blocks, on
                // every settled keystroke (MEASURED, session 401: 5.4 ms on perf-fingbeam1k).
                foreach (var partBlock in section.ChildNodes().OfType<PartBlockSyntax>())
                    Record(seen, section.SectionName, partBlock.Name, partBlock.PartName);
            }
        }
    }

    private void Record(HashSet<(string, string)> seen, string section, string part, SyntaxTokenNode token)
    {
        if (seen.Add((section, part))) return;
        _diagnostics.Error(token.Span,
            DiagnosticCodes.DuplicateCell,
            $"Section \"{section}\" already has music for part \"{part}\"; " +
            "each section/part cell may be defined only once");
    }

    private static string? EnclosingPartName(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartDeclarationSyntax part)
                return part.Name.Text;
        return null;
    }
}
