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
internal sealed class DuplicateScoreNameValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var render in tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>())
        {
            // The on-disk output name, from the ONE home that the renderer and the
            // preview's picker also read (RenderSpecParser.OutputNameOf). Two scores
            // sharing that key collide on disk — and, because the picker carries this
            // very word, the second one could not be selected in the preview either.
            // ⚠️ Reading the RAW basename here was not the same test: "Take 1.0" and
            // "Take 1.1" are different words but ONE output name (the rule drops what
            // follows the last dot), so the collision went unreported (2026-09-06).
            string outputKey = Svg.Collector.RenderSpecParser.OutputNameOf(render);

            if (seen.Add(outputKey)) continue; // first time → fine

            SyntaxTokenNode tok = render.Basename ?? render.FormName ?? render.RenderKeyword;
            string label = outputKey.Length == 0
                ? "the input-file output (form 'main' with no basename)"
                : $"output name \"{outputKey}\"";
            _diagnostics.Error(tok.Span,
                DiagnosticCodes.DuplicateScoreName,
                $"Duplicate {label}; give one score a distinct \"basename\".");
        }
    }
}
