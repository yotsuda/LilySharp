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
/// A top-level <c>lyrics</c> block only renders when a <c>score</c> references it:
/// note-aligned under a staff via <c>staff X with lyrics NAME</c>, or as an independent
/// <c>lyrics NAME</c> row. There is NO implicit auto-attach — so an unreferenced (or
/// unnamed, hence unreferenceable) block would silently vanish. This flags it with a
/// hint to name and attach it, mirroring how <c>with chords</c> already works.
///
/// Only genuinely top-level blocks are checked; an inline <c>lyrics { … }</c> written
/// inside a part or section is left alone (as the track validator does).
/// </summary>
internal sealed class LyricUnattachedValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // There is NO implicit auto-attach — not even for a scoreless loose-music file. A
        // top-level `lyrics {}` block renders ONLY when a score references it, so any block
        // no score names (including every block in a file that has no `score {}` at all) is
        // flagged here.
        var blocks = root.DescendantNodes().OfType<LyricsBlockSyntax>()
            .Where(b => !HasPartOrSectionAncestor(b))
            .ToList();
        if (blocks.Count == 0)
            return;

        // Names a score references: `lyrics NAME` rows + `staff … with lyrics NAME`
        // (grand-staff / tab inner staves are StaffRenderSyntax descendants too).
        var referenced = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var row in root.DescendantNodes().OfType<LyricsRowRenderSyntax>())
            referenced.Add(row.PartName);
        foreach (var staff in root.DescendantNodes().OfType<StaffRenderSyntax>())
            for (int i = 0; i + 2 < staff.SlotCount; i++)
                if (staff.GetChild(i) is SyntaxTokenNode w && w.Kind == SyntaxKind.WithKeyword
                    && staff.GetChild(i + 1) is SyntaxTokenNode k && k.Kind == SyntaxKind.LyricsKeyword
                    && staff.GetChild(i + 2) is SyntaxTokenNode n && n.Text.Length > 0)
                    referenced.Add(n.Text);

        foreach (var block in blocks)
        {
            if (block.VoiceName is { } name && referenced.Contains(name))
                continue;
            _diagnostics.Error(block.LyricsKeyword.Span, DiagnosticCodes.LyricUnattached,
                "this 'lyrics' block is not shown by any score: attach it under a staff with " +
                "'staff X with lyrics NAME', or place it as a 'lyrics NAME' row (name the block first).");
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
