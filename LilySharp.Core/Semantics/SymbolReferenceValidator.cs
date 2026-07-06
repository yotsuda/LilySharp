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
/// Validates that all symbol references (variables, phrases, sections) are defined.
/// </summary>
internal sealed class SymbolReferenceValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly HashSet<string> _definedVariables = new();
    private readonly HashSet<string> _definedPhrases = new();
    private readonly HashSet<string> _definedSections = new();
    private readonly HashSet<string> _definedParts = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// Validates all symbol references in a syntax tree.
    /// </summary>
    public void Validate(SyntaxTree tree)
    {
        _definedVariables.Clear();
        _definedPhrases.Clear();
        _definedSections.Clear();
        _definedParts.Clear();

        var root = tree.GetRoot();
        
        // First pass: collect all definitions
        CollectDefinitions(root);
        
        // Second pass: validate references
        ValidateReferences(root);
    }

    private void CollectDefinitions(SyntaxNode node)
    {
        switch (node)
        {
            case VariableDeclarationSyntax varDecl:
                _definedVariables.Add(varDecl.Name.Text);
                break;
                
            case PhraseDeclarationSyntax phraseDecl:
                _definedPhrases.Add(phraseDecl.Name.Text);
                // Phrases can also be referenced as variables
                _definedVariables.Add(phraseDecl.Name.Text);
                break;
                
            case SectionDeclarationSyntax sectionDecl:
                _definedSections.Add(sectionDecl.SectionName);
                break;
                
            case PartDeclarationSyntax partDecl:
                _definedParts.Add(partDecl.Name.Text);
                break;
        }

        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null)
                CollectDefinitions(child);
        }
    }

    private void ValidateReferences(SyntaxNode node)
    {
        switch (node)
        {
            case VariableReferenceSyntax varRef:
                var varName = varRef.Name.Text;
                if (!_definedVariables.Contains(varName) && !_definedPhrases.Contains(varName))
                {
                    _diagnostics.Error(
                        new TextSpan(varRef.Name.Position, varRef.Name.FullWidth),
                        DiagnosticCodes.UndefinedVariable,
                        $"Undefined variable or phrase: '{varName}'");
                }
                break;
                
            case SectionReferenceSyntax sectionRef:
                var sectionName = sectionRef.SectionName;
                if (!_definedSections.Contains(sectionName))
                {
                    _diagnostics.Error(
                        new TextSpan(sectionRef.Position, sectionRef.FullWidth),
                        DiagnosticCodes.UndefinedSection,
                        $"Undefined section: '{sectionName}'");
                }
                break;
        }

        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null)
                ValidateReferences(child);
        }
    }
}
