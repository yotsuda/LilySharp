using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Validates that all symbol references (variables, phrases, sections) are defined.
/// </summary>
public sealed class SymbolReferenceValidator
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
