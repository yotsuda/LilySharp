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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Flags an <c>override</c> / <c>revert</c> naming a grob property the engine does not
/// read. The grammar accepts any <c>Grob.property = value</c> — the value is typed at
/// collection (docs/VALUE_SITE_AUDIT.md §2) but nothing ever checked the NAME, so a
/// property outside <see cref="SupportedGrobOverrides"/> engraved exactly as if the line
/// were absent, and said nothing about it.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED before this validator existed: <c>override Wibble.wobble = 5</c>,
/// <c>override Stem.wibble = 1</c>, <c>override Stem.direction = -1</c>,
/// <c>override Stem.length = 12</c>, <c>override Beam.thickness = 9</c> and the mis-cased
/// <c>override stem.direction = 1</c> each produced "No errors found" and an SVG identical
/// to the no-override control. Only the three pairs in
/// <see cref="SupportedGrobOverrides"/> moved a single byte.
/// </para>
/// <para>
/// ⚠️ It is an ERROR, not a warning, and the wording says "not supported in this version"
/// rather than "unknown". Both follow from the same asymmetry: silence can never be
/// tightened after release (files that were accepted would start failing), while an error
/// can always be relaxed — implementing a property turns this off for that spelling and
/// breaks nobody, because a file that errors is not a file that worked. "Unknown" would
/// also become a lie on the day the property lands; "not supported in this version" stays
/// true on both sides of that commit.
/// </para>
/// <para>
/// ⚠️ Scope: this is the NAME check only. Whether the VALUE suits the property (a string
/// where a number is read) is the resolver's business — <c>GetDouble</c> and friends
/// already answer null for a value they cannot read, and inventing a second opinion here
/// would be a second spelling of the same judgement.
/// </para>
/// </remarks>
internal sealed class OverrideVocabularyValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        // `once override X` / `once revert X` need no case of their own: the wrapped
        // command is a descendant, so the walk reaches it and reports the same span.
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            var (keyword, grob, property) = node switch
            {
                OverrideDeclarationSyntax o => ("override", o.GrobName, o.PropertyName),
                RevertDeclarationSyntax r => ("revert", r.GrobName, r.PropertyName),
                _ => (null, null, null),
            };
            if (keyword == null || grob == null || property == null)
                continue;

            string spelling = $"{grob.Text}.{property.Text}";
            if (SupportedGrobOverrides.Contains(grob.Text, property.Text))
                continue;

            _diagnostics.Error(node.Span, DiagnosticCodes.OverridePropertyUnsupported,
                $"'{spelling}' is not supported in this version of Lily# — "
                + $"this '{keyword}' would change nothing. "
                + $"Supported: {string.Join(", ", SupportedGrobOverrides.Spellings)}. "
                + "Grob names are PascalCase and properties are lisp-case, both case-sensitive.");
        }
    }
}
