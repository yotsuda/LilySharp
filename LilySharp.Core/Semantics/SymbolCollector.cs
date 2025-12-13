using System.Collections.Immutable;
using LilySharp.Core.Semantics.Symbols;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Collects symbol definitions from a syntax tree.
/// This is Pass 1 of semantic analysis.
/// </summary>
/// <remarks>
/// Design inspired by Roslyn's declaration phase.
/// The SymbolCollector visits all nodes in the syntax tree
/// and builds a SymbolTable containing all definitions.
/// </remarks>
public sealed class SymbolCollector
{
    private readonly List<SemanticDiagnostic> _diagnostics = new();
    
    /// <summary>
    /// Collects all symbol definitions from the syntax tree.
    /// </summary>
    /// <param name="tree">The syntax tree to analyze.</param>
    /// <returns>A symbol table containing all definitions.</returns>
    public SymbolCollectionResult Collect(SyntaxTree tree)
    {
        var table = new SymbolTable();
        _diagnostics.Clear();
        
        var root = tree.GetRoot();
        CollectSymbols(root, table);
        
        return new SymbolCollectionResult(
            table.ToImmutable(),
            _diagnostics.ToImmutableArray());
    }
    
    private void CollectSymbols(SyntaxNode root, SymbolTable table)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case SectionDeclarationSyntax section:
                    CollectSection(section, table);
                    break;
                    
                case PhraseDeclarationSyntax phrase:
                    CollectPhrase(phrase, table);
                    break;
                    
                case PartBlockSyntax partBlock:
                    CollectPart(partBlock, table);
                    break;
                    
                case VariableDeclarationSyntax variable:
                    CollectVariable(variable, table);
                    break;
                    
                case StructureDeclarationSyntax structure:
                    CollectStructure(structure, table);
                    break;
            }
        }
    }
    
    private void CollectSection(SectionDeclarationSyntax section, SymbolTable table)
    {
        var name = section.SectionName;
        var body = CollectChildNodes(section);
        var symbol = new SectionSymbol(name, section, body);
        
        if (!table.AddSection(symbol))
        {
            _diagnostics.Add(new SemanticDiagnostic(
                DiagnosticSeverity.Error,
                $"Duplicate section definition: '{name}'",
                section.Position));
        }
    }
    
    private void CollectPhrase(PhraseDeclarationSyntax phrase, SymbolTable table)
    {
        var name = phrase.Name.Text;
        var body = CollectChildNodes(phrase.Body);
        var symbol = new PhraseSymbol(name, phrase, body);
        
        if (!table.AddPhrase(symbol))
        {
            _diagnostics.Add(new SemanticDiagnostic(
                DiagnosticSeverity.Error,
                $"Duplicate phrase definition: '{name}'",
                phrase.Position));
        }
    }
    
    private void CollectPart(PartBlockSyntax partBlock, SymbolTable table)
    {
        var name = partBlock.Name;
        var properties = CollectPartProperties(partBlock);
        var symbol = new PartSymbol(name, partBlock, properties);
        
        if (!table.AddPart(symbol))
        {
            _diagnostics.Add(new SemanticDiagnostic(
                DiagnosticSeverity.Error,
                $"Duplicate part definition: '{name}'",
                partBlock.Position));
        }
    }
    
    private void CollectVariable(VariableDeclarationSyntax variable, SymbolTable table)
    {
        var name = variable.Name.Text;
        var symbol = new VariableSymbol(name, variable, variable.Expression);
        
        if (!table.AddVariable(symbol))
        {
            _diagnostics.Add(new SemanticDiagnostic(
                DiagnosticSeverity.Error,
                $"Duplicate variable definition: '{name}'",
                variable.Position));
        }
    }
    
    private void CollectStructure(StructureDeclarationSyntax structure, SymbolTable table)
    {
        var symbol = new StructureSymbol(structure);
        
        if (!table.SetStructure(symbol))
        {
            _diagnostics.Add(new SemanticDiagnostic(
                DiagnosticSeverity.Error,
                "Multiple structure definitions are not allowed",
                structure.Position));
        }
    }
    
    /// <summary>
    /// Collects child nodes from a container, excluding structural tokens.
    /// </summary>
    private static ImmutableArray<SyntaxNode> CollectChildNodes(SyntaxNode container)
    {
        var builder = ImmutableArray.CreateBuilder<SyntaxNode>();
        
        for (int i = 0; i < container.SlotCount; i++)
        {
            var child = container.GetChild(i);
            if (child == null) continue;
            
            // Skip structural tokens like braces and keywords
            if (child is SyntaxTokenNode token)
            {
                var kind = token.Kind;
                if (kind == SyntaxKind.OpenBrace || 
                    kind == SyntaxKind.CloseBrace ||
                    kind == SyntaxKind.SectionKeyword ||
                    kind == SyntaxKind.PhraseKeyword ||
                    kind == SyntaxKind.Equals)
                    continue;
            }
            
            builder.Add(child);
        }
        
        return builder.ToImmutable();
    }
    
    /// <summary>
    /// Collects properties from a part block.
    /// Currently returns empty dictionary as part properties parsing is TBD.
    /// </summary>
    private static ImmutableDictionary<string, string> CollectPartProperties(PartBlockSyntax partBlock)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        
        // Part properties are collected from child nodes
        // For now, look for property-like patterns in descendants
        foreach (var node in partBlock.DescendantNodes())
        {
            // Look for clef, key, etc. declarations within the part
            switch (node)
            {
                case ClefDeclarationSyntax clef:
                    builder["clef"] = clef.ClefName.Text.ToLowerInvariant();
                    break;
                    
                case KeySignatureSyntax key:
                    builder["key"] = key.ToString();
                    break;
                    
                case TimeSignatureSyntax time:
                    builder["time"] = $"{time.Beats}/{time.BeatType}";
                    break;
            }
        }
        
        return builder.ToImmutable();
    }
}

/// <summary>
/// Result of symbol collection.
/// </summary>
public sealed record SymbolCollectionResult(
    ImmutableSymbolTable Symbols,
    ImmutableArray<SemanticDiagnostic> Diagnostics)
{
    /// <summary>
    /// Whether the collection succeeded without errors.
    /// </summary>
    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>
/// A diagnostic message from semantic analysis.
/// </summary>
public sealed record SemanticDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    int Position);

/// <summary>
/// Severity levels for diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational message.</summary>
    Info,
    
    /// <summary>Warning that doesn't prevent compilation.</summary>
    Warning,
    
    /// <summary>Error that prevents successful compilation.</summary>
    Error
}
