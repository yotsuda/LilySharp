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
/// The one rule of <c>combinedStaff { … }</c> that is not the parser's: exactly two parts.
/// </summary>
/// <remarks>
/// The message points at <c>condensedStaff</c> for three or more, because that is the
/// container that takes any number — and because the difference between them is worth
/// stating where someone has just discovered it: a condensed staff KEEPS both parts and
/// draws them as voices, a combined one merges what they share.
/// </remarks>
internal sealed class CombinedStaffValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var combined in tree.GetRoot().DescendantNodes().OfType<CombinedStaffRenderSyntax>())
        {
            var names = combined.PartNames.Where(n => n.Length > 0).ToList();
            if (names.Count == 2)
                continue;

            _diagnostics.Error(combined.Span, DiagnosticCodes.CombinedStaffNeedsTwoParts,
                names.Count < 2
                    ? "'combinedStaff' combines exactly two parts, and this one names "
                      + (names.Count == 0 ? "none" : "only '" + names[0] + "'")
                      + ". Name two: combinedStaff { flute1 flute2 }."
                    : $"'combinedStaff' combines exactly two parts, and this one names {names.Count}. "
                      + "Combining is defined pairwise — 'Solo' and 'Solo II' name one of TWO parts — "
                      + "so for more, use 'condensedStaff', which puts any number on one staff as "
                      + "separate voices.");
        }
    }
}
