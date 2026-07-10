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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Validates the <c>form</c> / <c>score</c> binding: every <c>form</c> is named
/// (<c>form Main { ... }</c>), form names are unique (case-sensitive), and every
/// <c>score</c> references an existing form by name (<c>score Main { ... }</c>).
/// A form is the piece's arrangement — the order sections play in, with repeats
/// and navigation. The reserved form name <c>main</c> writes to the input file's
/// stem; any other name becomes the output file name unless a <c>"basename"</c>
/// overrides it.
/// </summary>
internal sealed class StructureDeclarationValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var forms = tree.GetNodes<StructureDeclarationSyntax>().ToList();

        // Every form must be named; names are unique and case-sensitive.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            string name = form.NameText;
            if (string.IsNullOrEmpty(name))
            {
                _diagnostics.Error(form.StructureKeyword.Span, DiagnosticCodes.UnnamedForm,
                    "A 'form' must be named, e.g. 'form main { ... }'.");
                continue;
            }
            if (!declared.Add(name))
                _diagnostics.Error(form.Name!.Span, DiagnosticCodes.DuplicateFormName,
                    $"Duplicate form name '{name}'. Each form name must be unique.");
        }

        // Every score must reference a form that exists.
        foreach (var score in tree.GetNodes<RenderDeclarationSyntax>())
        {
            string reference = score.FormNameText;
            if (string.IsNullOrEmpty(reference))
                _diagnostics.Error(score.RenderKeyword.Span, DiagnosticCodes.UnknownFormReference,
                    "A 'score' must name the form it renders, e.g. 'score main { ... }'.");
            else if (!declared.Contains(reference))
                _diagnostics.Error(score.FormName!.Span, DiagnosticCodes.UnknownFormReference,
                    $"Unknown form '{reference}'. Declare it with 'form {reference} {{ ... }}'.");
        }
    }
}
