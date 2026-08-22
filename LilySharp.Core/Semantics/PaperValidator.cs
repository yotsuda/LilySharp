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
/// Reports what is wrong inside a <c>paper { }</c> block: a key that is not in the
/// vocabulary, a key set twice, a key with no number, a unit that is not one.
/// </summary>
/// <remarks>
/// ⚠️ AN UNKNOWN KEY IS AN ERROR, not a warning — <see cref="FontBindingValidator"/>'s
/// reasoning holds here word for word: a setting that reaches nothing looks exactly like
/// one that works, the page just comes out at its default size. The reading itself lives
/// in <see cref="PaperPlanReader"/>, shared with the collector, so the two cannot
/// disagree about what is legal.
/// </remarks>
internal sealed class PaperValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var paper in tree.GetRoot().DescendantNodes().OfType<PaperDeclarationSyntax>())
        {
            PaperPlanReader.Read(paper, out var problems);
            foreach (var p in problems)
            {
                if (p.IsError)
                    _diagnostics.Error(p.Span, p.Code, p.Message);
                else
                    _diagnostics.Warning(p.Span, p.Code, p.Message);
            }
        }
    }
}
