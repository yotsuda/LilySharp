using System.Collections.Immutable;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Represents a part definition.
/// </summary>
/// <example>
/// part Piano {
///     clef = treble
///     key = C major
/// }
/// </example>
public sealed record PartSymbol : Symbol
{
    /// <summary>
    /// Creates a new part symbol.
    /// </summary>
    /// <param name="name">The part name.</param>
    /// <param name="declaringSyntax">The syntax node that declares this part.</param>
    /// <param name="properties">The properties defined for this part.</param>
    public PartSymbol(
        string name,
        SyntaxNode declaringSyntax,
        ImmutableDictionary<string, string> properties)
    {
        _name = name;
        _declaringSyntax = declaringSyntax;
        Properties = properties;
    }
    
    private readonly string _name;
    private readonly SyntaxNode _declaringSyntax;
    
    /// <inheritdoc/>
    public override string Name => _name;
    
    /// <inheritdoc/>
    public override SymbolKind Kind => SymbolKind.Part;
    
    /// <inheritdoc/>
    public override SyntaxNode DeclaringSyntax => _declaringSyntax;
    
    /// <summary>
    /// The properties defined for this part (e.g., clef, key).
    /// </summary>
    public ImmutableDictionary<string, string> Properties { get; }
    
    /// <summary>
    /// Gets a property value, or null if not defined.
    /// </summary>
    public string? GetProperty(string name) => 
        Properties.TryGetValue(name, out var value) ? value : null;
}
