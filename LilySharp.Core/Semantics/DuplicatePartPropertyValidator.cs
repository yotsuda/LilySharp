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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Flags a part header that sets the same property twice
/// (<c>part m { clef bass clef treble }</c>). Each property holds one value, so one of
/// the two is discarded — and the language did not agree with itself about which.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED, with same-length payloads so source offsets could not move the bytes:
/// <c>clef bass clef treble</c> engraved as TREBLE (the last), while
/// <c>lines 5 lines 3</c> engraved as five lines — byte-identical to <c>lines 5</c>
/// alone, i.e. the FIRST. Two properties, two opposite tie-break rules, and neither
/// said a word.
/// </para>
/// <para>
/// ⚠️ The fix is to refuse the duplicate rather than to pick a winner. Picking one would
/// have made the other property's behaviour change — output moving in a commit that was
/// supposed to be about diagnostics — and would have frozen an accident as a promise.
/// No book on disk writes a duplicate (measured across all 309), so nothing had to
/// choose. A rule can still be declared later: going from "refused" to "last wins" only
/// ever accepts more than before.
/// </para>
/// <para>
/// Scope is ONE header. Two parts each setting <c>clef</c> is not a duplicate, and a
/// part-header property does not group with the top-level directive of the same name
/// (that is <c>DuplicateGlobalSettingValidator</c>, which excludes part headers for the
/// same reason: a per-part default is not a global one).
/// </para>
/// </remarks>
internal sealed class DuplicatePartPropertyValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var part in tree.GetRoot().DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            var seen = new Dictionary<string, SyntaxNode>(System.StringComparer.Ordinal);
            foreach (var prop in part.ChildNodes().OfType<PropertyAssignmentSyntax>())
            {
                string name = prop.NameToken.Text;
                if (seen.TryGetValue(name, out var first))
                {
                    _diagnostics.Error(prop.Span, DiagnosticCodes.DuplicatePartProperty,
                        $"'{name}' is set twice in this part header, and only one value can "
                        + "take effect. Remove one of them.");
                    continue;
                }
                seen[name] = prop;
            }
        }
    }
}
