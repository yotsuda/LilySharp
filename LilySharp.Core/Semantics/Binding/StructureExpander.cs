using System.Collections.Immutable;
using LilySharp.Core.Semantics.Symbols;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Binding;

/// <summary>
/// Expands structure blocks into flat music sequences.
/// </summary>
/// <remarks>
/// Handles:
/// - Section references (A, B, Chorus, etc.)
/// - Repeat blocks (|: ... :|)
/// - Alternatives ([1. A] [2. B])
/// </remarks>
public sealed class StructureExpander
{
    private readonly ImmutableSymbolTable _symbols;
    private readonly List<SemanticDiagnostic> _diagnostics;
    
    public StructureExpander(ImmutableSymbolTable symbols, List<SemanticDiagnostic> diagnostics)
    {
        _symbols = symbols;
        _diagnostics = diagnostics;
    }
    
    /// <summary>
    /// Expands a structure declaration into a flat sequence of music nodes.
    /// </summary>
    /// <param name="structure">The structure to expand.</param>
    /// <returns>Expanded music nodes with their part names.</returns>
    public ExpandedStructure Expand(StructureDeclarationSyntax structure)
    {
        var parts = new Dictionary<string, List<SyntaxNode>>();
        
        foreach (var child in structure.DescendantNodes())
        {
            switch (child)
            {
                case StructureRepeatBlockSyntax repeatBlock:
                    ExpandRepeatBlock(repeatBlock, parts);
                    break;
                    
                case SectionReferenceSyntax sectionRef when child.Parent is StructureDeclarationSyntax:
                    // Direct section reference (not inside repeat block)
                    ExpandSectionReference(sectionRef, parts);
                    break;
            }
        }
        
        return new ExpandedStructure(
            parts.ToImmutableDictionary(
                kv => kv.Key, 
                kv => kv.Value.ToImmutableArray()));
    }
    
    private void ExpandRepeatBlock(StructureRepeatBlockSyntax repeatBlock, Dictionary<string, List<SyntaxNode>> parts)
    {
        // Collect sections and alternatives from the repeat block
        var sections = new List<SectionReferenceSyntax>();
        var alternatives = new List<StructureAlternativeSyntax>();
        
        foreach (var child in repeatBlock.DescendantNodes())
        {
            switch (child)
            {
                case SectionReferenceSyntax sectionRef:
                    sections.Add(sectionRef);
                    break;
                case StructureAlternativeSyntax alt:
                    alternatives.Add(alt);
                    break;
            }
        }
        
        // Remove sections that are part of alternatives
        var sectionsInAlternatives = alternatives
            .Select(a => a.SectionName.Text)
            .ToHashSet();
        var mainSections = sections
            .Where(s => !sectionsInAlternatives.Contains(s.SectionName))
            .ToList();
        
        // Determine repeat count based on alternatives
        int repeatCount = alternatives.Count > 0 
            ? alternatives.Max(a => a.AlternativeNumber) 
            : 2;
        
        // Expand: main sections + appropriate alternative for each iteration
        for (int iteration = 1; iteration <= repeatCount; iteration++)
        {
            // Add main sections
            foreach (var section in mainSections)
            {
                ExpandSectionReference(section, parts);
            }
            
            // Add appropriate alternative
            var alt = alternatives.FirstOrDefault(a => a.AlternativeNumber == iteration);
            if (alt != null)
            {
                var altSectionName = alt.SectionName.Text;
                if (_symbols.Sections.TryGetValue(altSectionName, out var altSection))
                {
                    AddSectionContent(altSection, parts);
                }
                else
                {
                    _diagnostics.Add(new SemanticDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Undefined section in alternative: '{altSectionName}'",
                        alt.Position));
                }
            }
        }
    }
    
    private void ExpandSectionReference(SectionReferenceSyntax sectionRef, Dictionary<string, List<SyntaxNode>> parts)
    {
        var sectionName = sectionRef.SectionName;
        
        if (_symbols.Sections.TryGetValue(sectionName, out var section))
        {
            AddSectionContent(section, parts);
        }
        else
        {
            _diagnostics.Add(new SemanticDiagnostic(
                DiagnosticSeverity.Error,
                $"Undefined section: '{sectionName}'",
                sectionRef.Position));
        }
    }
    
    private void AddSectionContent(SectionSymbol section, Dictionary<string, List<SyntaxNode>> parts)
    {
        // Section can contain multiple part blocks (e.g., rightHand, leftHand)
        foreach (var node in section.Body)
        {
            if (node is PartBlockSyntax partBlock)
            {
                var partName = partBlock.Name;
                if (!parts.ContainsKey(partName))
                    parts[partName] = new List<SyntaxNode>();
                
                // Add the music content from the part block
                foreach (var child in partBlock.DescendantNodes())
                {
                    if (child is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax)
                    {
                        parts[partName].Add(child);
                    }
                }
            }
            else
            {
                // Not in a part block - add to default part
                const string defaultPart = "";
                if (!parts.ContainsKey(defaultPart))
                    parts[defaultPart] = new List<SyntaxNode>();
                    
                if (node is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax)
                {
                    parts[defaultPart].Add(node);
                }
            }
        }
    }
}

/// <summary>
/// Result of structure expansion.
/// </summary>
public sealed record ExpandedStructure(
    ImmutableDictionary<string, ImmutableArray<SyntaxNode>> Parts)
{
    /// <summary>
    /// Gets expanded nodes for a specific part.
    /// </summary>
    public ImmutableArray<SyntaxNode> GetPart(string partName)
        => Parts.TryGetValue(partName, out var nodes) ? nodes : ImmutableArray<SyntaxNode>.Empty;
    
    /// <summary>
    /// Gets all part names.
    /// </summary>
    public IEnumerable<string> PartNames => Parts.Keys;
    
    /// <summary>
    /// True if this is a single-part (non-multi-staff) score.
    /// </summary>
    public bool IsSinglePart => Parts.Count <= 1;
    
    /// <summary>
    /// Gets the default part (empty name) or the first part.
    /// </summary>
    public ImmutableArray<SyntaxNode> DefaultPart 
        => Parts.TryGetValue("", out var nodes) ? nodes 
         : Parts.Count > 0 ? Parts.Values.First() 
         : ImmutableArray<SyntaxNode>.Empty;
}
