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

using System;
using System.Collections.Generic;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports every <c>@todo</c> mark (LYS4026, with its memo), a todo whose arguments do not
/// read (LYS4027) and a key written twice (LYS4028). See <see cref="TodoAnnotation"/>.
/// </summary>
internal sealed class TodoValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
        {
            if (TodoAnnotation.Of(node) is not { } todo)
                continue;
            _diagnostics.Warning(node.Span, DiagnosticCodes.TodoMark, todo.Message);
            if (todo.Problem is { } problem)
                _diagnostics.Warning(node.Span, DiagnosticCodes.TodoArgument, problem);
            else if (todo.Key is { } key && !keys.Add(key))
                _diagnostics.Warning(node.Span, DiagnosticCodes.TodoKeyRepeated,
                    $"the todo key '{key}' is written twice in this file; a key names one mark.");
        }
    }
}
