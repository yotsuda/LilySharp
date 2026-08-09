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
/// The rules of <c>condensedStaff { … }</c>: at least two parts, and nothing inside it but
/// part names.
/// </summary>
/// <remarks>
/// Both are reported here rather than swallowed in the render-spec parser, because the
/// neighbouring container shows what swallowing costs: <c>grandStaff</c> with fewer than two
/// staves returns null, the item disappears, and the score then complains that its body
/// "declares no staff" — about a body that declares one. A rule worth having is worth
/// naming.
/// </remarks>
internal sealed class CondensedStaffValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var condensed in tree.GetRoot().DescendantNodes().OfType<CondensedStaffRenderSyntax>())
        {
            // A non-part member (`staff X`, a nested group) is reported by the PARSER, which
            // is where it is visible: those tokens never reach this node, because the parser
            // skips them to keep the rest of the block parsing.
            int count = condensed.PartNames.Count(n => n.Length > 0);
            if (count >= 2)
                continue;

            _diagnostics.Error(condensed.Span, DiagnosticCodes.CondensedStaffNeedsTwoParts,
                count == 0
                    ? "'condensedStaff' names no parts. It puts several parts on one staff, "
                      + "so it needs at least two: condensedStaff { flute1 flute2 }."
                    : "'condensedStaff' names only one part, and one part on one staff is "
                      + "what 'staff " + condensed.PartNames.First(n => n.Length > 0)
                      + "' already does. Name at least two, or use 'staff'.");
        }
    }
}
