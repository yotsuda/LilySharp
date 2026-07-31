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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Flags a <c>score</c> block that holds no render item at all (<c>score main { }</c>).
/// It is not a harmless no-op: the engraver happily lays out a page with a title and
/// no music, which reads as a layout failure rather than as a mistake in the source.
/// </summary>
internal sealed class EmptyScoreValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var render in tree.GetRoot().DescendantNodes().OfType<RenderDeclarationSyntax>())
        {
            // "What counts as a render item" is decided in exactly one place — the
            // parser that the engraver itself uses. Re-listing the item node kinds
            // here would be a second spelling of the same question, and the two
            // would drift the day a new row type is added.
            var spec = RenderSpecParser.Parse(render);
            if (spec is null || !spec.Items.IsEmpty)
                continue;

            string name = render.FormNameText;
            string which = name.Length == 0 ? "This score" : $"Score '{name}'";
            // Mark the EMPTY BODY, not the `score` keyword: the body is what has to
            // change, and a squiggle on the keyword reads as "this line is wrong"
            // while the eye is drawn to whatever declaration sits above it.
            _diagnostics.Error(render.BodySpan ?? render.RenderKeyword.Span,
                DiagnosticCodes.EmptyScore,
                $"{which} has nothing to engrave — its body declares no staff. "
                + "Add a render item, e.g. 'staff melody', 'grandStaff { staff rh staff lh }' "
                + "or 'tab guitar'.");
        }
    }
}
