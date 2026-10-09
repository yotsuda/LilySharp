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
/// Flags two <c>score</c> blocks that share the same name, which would collide on
/// disk and be indistinguishable in the preview's score picker. Two UNNAMED scores
/// collide too — both would be the "(Default)" entry.
/// </summary>
internal sealed class DuplicateScoreNameValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var render in TopLevelNodes.OfRoot<RenderDeclarationSyntax>(tree.GetRoot()))
        {
            // The on-disk output name, from the ONE home that the renderer and the
            // preview's picker also read (RenderSpecParser.OutputNameOf). Two scores
            // sharing that key collide on disk — and, because the picker carries this
            // very word, the second one could not be selected in the preview either.
            string outputKey = Svg.Collector.RenderSpecParser.OutputNameOf(render);

            if (seen.Add(outputKey)) continue; // first time → fine

            SyntaxTokenNode tok = render.ScoreName ?? render.QuotedName ?? render.RenderKeyword;
            string message = outputKey.Length == 0
                ? "A second unnamed score: the unnamed score is the file's default, so there is one — name this one ('score tab { … }')."
                : $"Duplicate score name '{outputKey}'; give one score a distinct name.";
            _diagnostics.Error(tok.Span, DiagnosticCodes.DuplicateScoreName, message);
        }
    }
}
