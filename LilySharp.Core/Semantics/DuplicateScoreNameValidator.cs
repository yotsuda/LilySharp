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
/// Flags two <c>score</c> blocks that share the same name (the output basename),
/// which would collide on disk and be indistinguishable in the preview's score
/// picker. Two UNNAMED scores collide too — both would be the "(Default)" entry.
/// </summary>
public sealed class DuplicateScoreNameValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var seen = new HashSet<string>();
        foreach (var render in tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>())
        {
            // Child 0 is the `score` keyword; child 1 is the name token or the brace.
            var keyword = render.GetChild(0) as SyntaxTokenNode;
            var nameToken = render.GetChild(1) is SyntaxTokenNode t && t.Kind != SyntaxKind.OpenBrace ? t : null;
            string name = nameToken == null ? "" : nameToken.Text.Trim('"');

            if (seen.Add(name)) continue; // first time → fine

            var tok = nameToken ?? keyword;
            if (tok == null) continue;
            string label = name.Length == 0 ? "the default (unnamed) score" : $"score name \"{name}\"";
            _diagnostics.Error(new TextSpan(tok.Position, tok.FullWidth),
                DiagnosticCodes.DuplicateScoreName,
                $"Duplicate {label}; each score must have a unique name");
        }
    }
}
