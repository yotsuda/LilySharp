using System.Collections.Immutable;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Represents a section definition.
/// </summary>
/// <example>
/// section A {
///     c4 d e f |
/// }
/// </example>
public sealed record SectionSymbol : Symbol
{
    /// <summary>
    /// Creates a new section symbol.
    /// </summary>
    /// <param name="name">The section name (e.g., "A", "B", "Chorus").</param>
    /// <param name="declaringSyntax">The syntax node that declares this section.</param>
    /// <param name="body">The music content of this section.</param>
    public SectionSymbol(
        string name,
        SectionDeclarationSyntax declaringSyntax,
        ImmutableArray<SyntaxNode> body)
    {
        _name = name;
        _declaringSyntax = declaringSyntax;
        Body = body;
    }
    
    private readonly string _name;
    private readonly SectionDeclarationSyntax _declaringSyntax;
    
    /// <inheritdoc/>
    public override string Name => _name;
    
    /// <inheritdoc/>
    public override SymbolKind Kind => SymbolKind.Section;
    
    /// <inheritdoc/>
    public override SyntaxNode DeclaringSyntax => _declaringSyntax;
    
    /// <summary>
    /// The syntax node that declares this section (typed).
    /// </summary>
    public SectionDeclarationSyntax DeclarationSyntax => _declaringSyntax;
    
    /// <summary>
    /// The music content of this section.
    /// </summary>
    public ImmutableArray<SyntaxNode> Body { get; }
}
