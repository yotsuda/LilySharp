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
using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// In a PART-MAJOR file (parts carry their own <c>section</c> blocks), a top-level
/// lyrics TRACK must mirror that shape: <c>lyrics { section A { … } section B { … } }</c>,
/// one verse per named section. A flat top-level <c>lyrics { … }</c> has no section to
/// anchor to — its verse would silently run from bar 0 across every section — so this
/// requires the sectioned form, matching how the parts are written.
///
/// Only genuinely top-level track blocks are checked: an inline <c>lyrics { … }</c>
/// written inside a part or section is note-bound to that music and left alone, and a
/// section-major or structureless file (where flat lyrics are the norm) is not touched.
/// </summary>
internal sealed class LyricTrackSectionValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        if (PartSectionLayoutConverter.Detect(root) != LayoutForm.PartMajor)
            return;

        foreach (var block in root.DescendantNodes().OfType<LyricsBlockSyntax>())
        {
            // Already sectioned, or an inline (note-bound) block inside a part/section
            // rather than a standalone top-level track — nothing to require.
            if (block.HasSections || HasPartOrSectionAncestor(block))
                continue;

            _diagnostics.Error(block.LyricsKeyword.Span, DiagnosticCodes.LyricTrackNeedsSections,
                "In part-major layout a lyrics track must group its verses by section: " +
                "write 'lyrics { section A { … } }' (mirroring the part's sections).");
        }
    }

    private static bool HasPartOrSectionAncestor(SyntaxNode node)
    {
        for (var n = node.Parent; n != null; n = n.Parent)
            if (n is PartDeclarationSyntax or PartBlockSyntax or SectionDeclarationSyntax)
                return true;
        return false;
    }
}
