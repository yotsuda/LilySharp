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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>@p(editorial)</c> (owner-approved 2026-11-13, grammar evaluation 3) puts a dynamic LEVEL
/// in parentheses, "(p)" — an editor's dynamic. A hairpin (<c>@cresc</c>, <c>@decresc</c>,
/// <c>@dim</c>) and a niente are no level's letters, and the parentheses on them are refused
/// (<see cref="DiagnosticCodes.EditorialNotALevel"/>); the mark is drawn as the plain one.
/// </summary>
internal sealed class EditorialDynamicValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var dynamic in tree.GetRoot().DescendantNodes<DynamicSyntax>())
        {
            if (!dynamic.IsEditorial)
                continue;
            string name = dynamic.DynamicToken.Text;
            if (name is "cresc" or "decresc" or "dim" or "niente")
                _diagnostics.Error(dynamic.Span, DiagnosticCodes.EditorialNotALevel,
                    $"'(editorial)' is written on a dynamic level and puts it in parentheses - @p(editorial) prints (p) - and "
                    + $"'@{name}' is " + (name == "niente" ? "the niente" : "a hairpin")
                    + $", so it is drawn as the plain '@{name}'. Write the level the editor suggests, @mf(editorial), "
                    + "or a word in parentheses: @text(\"(cresc.)\").");
        }
    }
}