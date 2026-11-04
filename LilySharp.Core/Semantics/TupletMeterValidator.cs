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
/// LYS1065: a <c>time</c> or <c>partial</c> in a tuplet's body (owner's decision 2026-11-05). Both
/// belong to a bar's start, where writing one before the tuplet says the same, and past a member
/// they would stand inside a bar (LYS2017). The page, the bar counts and the checks read a meter
/// from the music's flat run, so it would be dropped without a word.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: a deliberate difference from LilyPond, which takes <c>\time</c> inside <c>\tuplet</c>.
/// </remarks>
internal sealed class TupletMeterValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var time in tree.GetNodes<TimeSignatureSyntax>())
            Judge(time, "time");
        foreach (var partial in tree.GetNodes<PartialDeclarationSyntax>())
            Judge(partial, "partial");
    }

    private void Judge(SyntaxNode node, string word)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is TupletExpressionSyntax)
            {
                _diagnostics.Error(node.Span, DiagnosticCodes.MeterInTuplet,
                    $"'{word}' cannot be written inside a tuplet: write it before the tuplet, at the start of its bar");
                return;
            }
        }
    }
}
