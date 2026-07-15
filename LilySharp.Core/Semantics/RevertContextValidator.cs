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
/// Flags a <c>revert</c> / <c>once</c> written outside a music stream. Both are positional
/// (they act from a point in the music forward), so a structural context has nowhere to
/// anchor them:
/// <list type="bullet">
/// <item>A <c>part {}</c> header holds properties and sections, never a note stream — a
/// <c>revert</c> / <c>once</c> there is always an error.</item>
/// <item>The top level is a note stream ONLY in a bare-music file; once the file has a
/// <c>part</c> / <c>section</c> / <c>form</c>, the top level is structural too.</item>
/// </list>
/// A plain <c>override</c> in those places sets a default; to revert, write it inside a
/// section or voice's music. (The design: docs/grob-override-scope-design.md.)
/// </summary>
internal sealed class RevertContextValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        bool structured = root.DescendantNodes().Any(n =>
            n is PartDeclarationSyntax or SectionDeclarationSyntax or FormDeclarationSyntax);

        foreach (var node in root.DescendantNodes())
        {
            // `once` (wrapping override OR revert) is music-only whatever it wraps. A bare
            // `revert` inside a `once` is reported via the OnceModifier, so skip it here.
            string? kind = node switch
            {
                OnceModifierSyntax => "once",
                RevertDeclarationSyntax r when r.Parent is not OnceModifierSyntax => "revert",
                _ => null,
            };
            if (kind == null)
                continue;

            string? where = node.Parent switch
            {
                PartDeclarationSyntax => "a part header",
                // A section-MAJOR section holds part blocks, not a note stream — a directive
                // directly in it is structural. A single-voice section (holds notes) is a
                // music stream, so a revert there is fine.
                SectionDeclarationSyntax s when IsSectionMajor(s) => "a section header",
                _ when node.Parent == root && structured => "the top level",
                _ => null, // in a note stream (a single-voice section, part block, voice, or bare music)
            };
            if (where == null)
                continue;

            _diagnostics.Error(node.Span, DiagnosticCodes.RevertOutsideMusic,
                $"'{kind}' cannot go in {where} — it acts from a point in the music. "
                + "Set a default with a plain 'override' here, and 'revert' inside a section or voice.");
        }
    }

    /// <summary>A section-major section holds part blocks (its body is per-part music),
    /// so a directive directly in it is structural — unlike a single-voice section whose
    /// body IS a note stream.</summary>
    private static bool IsSectionMajor(SectionDeclarationSyntax section)
        => section.DescendantNodes().Any(n => n.Parent == section
            && n is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax);
}
