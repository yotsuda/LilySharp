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
/// Reports what is wrong inside a <c>fonts { }</c> block — a key that is not a role, a
/// key bound twice, a key with no face — and what is wrong with the NAME LAYER: a score
/// referencing a name nothing declares, two declarations sharing a name, a declaration
/// no score references, and one score referencing fonts twice.
/// </summary>
/// <remarks>
/// ⚠️ AN UNKNOWN KEY IS AN ERROR, not a warning. A binding that reaches nothing looks
/// exactly like one that works — the page just comes out in the bundled face — so the
/// score would have to be compared against another machine's to notice. Every other
/// vocabulary in this language is refused the same way (an unknown grob override, an
/// unknown clef), and the message names the whole vocabulary so the fix is one read.
/// An unknown REFERENCE name, an unreferenced declaration and a duplicate name are the
/// same fact one layer up, and get the same treatment (the unreferenced one warns —
/// the writer may be about to reference it from a score not yet written).
/// </remarks>
internal sealed class FontBindingValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var all = root.DescendantNodes().OfType<FontDeclarationSyntax>().ToList();

        // Each block's own entries, wherever the block stands — the unnamed default, a
        // named declaration, or a score's override block. The reading is
        // FontPlanReader's, shared with the collector, so the two cannot disagree.
        foreach (var font in all)
        {
            FontPlanReader.Read(font, out var problems);
            foreach (var p in problems)
            {
                if (p.IsError)
                    _diagnostics.Error(p.Span, p.Code, p.Message);
                else
                    _diagnostics.Warning(p.Span, p.Code, p.Message);
            }
        }

        // The name layer.
        var declarations = FontPlanReader.NamedDeclarations(root);
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in declarations)
        {
            string name = d.NameToken!.Text;
            if (!declaredNames.Add(name))
                _diagnostics.Error(d.NameToken.Span, DiagnosticCodes.DuplicateFontsBlockName,
                    $"A fonts block named '{name}' is already declared; a reference binds "
                    + "to the first, so names must be unique.");
        }

        var references = all
            .Where(f => f.NameToken != null && FontPlanReader.IsInsideRender(f))
            .ToList();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in references)
        {
            referenced.Add(r.NameToken!.Text);
            if (!FontPlanReader.TryResolve(root, r, out _, out var problem)
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
                    DiagnosticCodes.DuplicateFontsReference,
                    "This fonts reference is overwritten by a later one in the same "
                    + "score; only the last one takes effect.");
        }

        foreach (var d in declarations)
        {
            if (!referenced.Contains(d.NameToken!.Text))
                _diagnostics.Warning(d.NameToken.Span, DiagnosticCodes.UnreferencedNamedFonts,
                    $"No score references the fonts block '{d.NameToken.Text}', so it "
                    + "binds nothing. Reference it inside a score: fonts "
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
