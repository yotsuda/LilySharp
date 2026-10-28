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
/// The chord-track <c>for</c> rules (owner's decision 2026-10-28, <see cref="ChordTrackPitch"/>):
/// <list type="bullet">
/// <item>LYS7006 — <c>for P</c> (a definition or a score row) where P is no declared part.</item>
/// <item>LYS7007 — two definition blocks of one track name different parts. Rows never
/// conflict: each says only whose reading it prints.</item>
/// </list>
/// </summary>
internal sealed class ChordForValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // The parts as the lyrics' `sings` reads them (declared, and the section part blocks that
        // are parts without a header), through the finder the editor's colouring asks too.
        var parts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in root.DescendantNodesOfKinds(Editing.PartReferenceFinder.DeclaringKinds))
            if (Editing.PartReferenceFinder.DeclaredName(n) is { } part)
                parts.Add(part.Text);

        var first = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            var (name, forToken) = node switch
            {
                ChordPartBlockSyntax b => (b.PartName, b.ForTargetToken),
                ChordRowRenderSyntax r => (r.PartName, r.ForTargetToken),
                _ => (null, null),
            };
            if (name == null || forToken == null)
                continue;
            string target = forToken.Text;
            if (!parts.Contains(target))
            {
                _diagnostics.Error(forToken.Span, DiagnosticCodes.ChordForUnknown,
                    $"'{name}' is for '{target}', but no part of that name exists in this file.");
                continue;
            }
            if (node is not ChordPartBlockSyntax)
                continue;
            if (!first.TryAdd(name, target) && first[name] != target)
                _diagnostics.Error(forToken.Span, DiagnosticCodes.ChordForConflict,
                    $"'{name}' is already for '{first[name]}' - a track's letters are typed one way; "
                    + "state it once (later blocks may repeat it or omit it). To print the same chords "
                    + $"as another part's player reads them, write 'for' on the score row ('chords {name} for {target}').");
        }
    }
}
