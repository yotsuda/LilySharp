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
/// Warns at a second dynamic written on one note (<c>c4@f@sfz</c>): only the first is
/// engraved (<c>MeasureCollector.CollectDynamics</c>), as in LilyPond, and this says so at the
/// one that is not (<see cref="DiagnosticCodes.DoubleDynamic"/>).
/// </summary>
/// <remarks>
/// A hairpin start (<c>@cresc</c>, <c>@decresc</c>, <c>@dim</c>) is not a dynamic LEVEL and
/// shares a note with one freely — LilyPond's <c>c\f\&lt;</c>.
/// </remarks>
internal sealed class DoubleDynamicValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds([SyntaxKind.Dynamic]))
        {
            if (node is not DynamicSyntax { Level: not DynamicLevel.None } dynamic
                || dynamic.Parent is not { } host)
                continue;
            // The first LEVEL on the host is the one engraved; any later one is reported.
            DynamicSyntax? first = null;
            for (int i = 0; i < host.SlotCount; i++)
                if (host.GetChild(i) is DynamicSyntax { Level: not DynamicLevel.None } d)
                {
                    first = d;
                    break;
                }
            if (first is null || ReferenceEquals(first, dynamic)
                || first.Span.Start == dynamic.Span.Start)
                continue;
            _diagnostics.Warning(
                dynamic.Span,
                DiagnosticCodes.DoubleDynamic,
                $"'@{dynamic.DynamicToken.Text}' is not printed: this note already has "
                + $"'@{first.DynamicToken.Text}', and a note takes one dynamic (LilyPond keeps "
                + "the first as well). Put the second on the next note, or write the one you mean.");
        }
    }
}
