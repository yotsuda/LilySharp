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
/// Reports what is wrong inside a <c>paper { }</c> block — a key that is not in the
/// vocabulary, a key set twice, a key with no number, a unit that is not one — and
/// what is wrong with the NAME LAYER, mirroring <see cref="FontBindingValidator"/>:
/// an unknown reference, a duplicate name, an unreferenced declaration, a score
/// referencing paper twice.
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
        var root = tree.GetRoot();
        var all = root.DescendantNodes().OfType<PaperDeclarationSyntax>().ToList();

        // Each block's own entries, wherever the block stands — the unnamed default, a
        // named declaration, or a score's override block.
        foreach (var paper in all)
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

        // The name layer.
        var declarations = PaperPlanReader.NamedDeclarations(root);
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in declarations)
        {
            string name = d.NameToken!.Text;
            if (!declaredNames.Add(name))
                _diagnostics.Error(d.NameToken.Span, DiagnosticCodes.DuplicatePaperBlockName,
                    $"A paper block named '{name}' is already declared; a reference binds "
                    + "to the first, so names must be unique.");
        }

        var references = all
            .Where(p => p.NameToken != null && FontPlanReader.IsInsideRender(p))
            .ToList();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in references)
        {
            referenced.Add(r.NameToken!.Text);
            if (!PaperPlanReader.TryResolve(root, r, out _, out var problem)
                && problem is { } p)
                _diagnostics.Error(p.Span, p.Code, p.Message);
        }

        // Two references in one score: the LAST wins, like every repeated
        // single-value setting; the earlier ones are named.
        foreach (var group in references.GroupBy(EnclosingRender))
        {
            if (group.Key == null)
                continue;
            var inOrder = group.OrderBy(r => r.Position).ToList();
            for (int i = 0; i < inOrder.Count - 1; i++)
                _diagnostics.Warning(inOrder[i].NameToken!.Span,
                    DiagnosticCodes.DuplicatePaperReference,
                    "This paper reference is overwritten by a later one in the same "
                    + "score; only the last one takes effect.");
        }

        foreach (var d in declarations)
        {
            if (!referenced.Contains(d.NameToken!.Text))
                _diagnostics.Warning(d.NameToken.Span, DiagnosticCodes.UnreferencedNamedPaper,
                    $"No score references the paper block '{d.NameToken.Text}', so it "
                    + "binds nothing. Reference it inside a score: paper "
                    + d.NameToken.Text + ".");
        }
    }

    /// <summary>The score block <paramref name="node"/> stands in, or null.</summary>
    private static RenderDeclarationSyntax? EnclosingRender(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is RenderDeclarationSyntax render)
                return render;
        return null;
    }
}
