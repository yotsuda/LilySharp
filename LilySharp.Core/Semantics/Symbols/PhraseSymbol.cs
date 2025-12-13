using System.Collections.Immutable;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Represents a phrase definition.
/// </summary>
/// <example>
/// phrase melody = {
///     c4 d e f |
/// }
/// </example>
public sealed record PhraseSymbol : Symbol
{
    /// <summary>
    /// Creates a new phrase symbol.
    /// </summary>
    /// <param name="name">The phrase name.</param>
    /// <param name="declaringSyntax">The syntax node that declares this phrase.</param>
    /// <param name="body">The music content of this phrase.</param>
    public PhraseSymbol(
        string name,
        PhraseDeclarationSyntax declaringSyntax,
        ImmutableArray<SyntaxNode> body)
    {
        _name = name;
        _declaringSyntax = declaringSyntax;
        Body = body;
    }
    
    private readonly string _name;
    private readonly PhraseDeclarationSyntax _declaringSyntax;
    
    /// <inheritdoc/>
    public override string Name => _name;
    
    /// <inheritdoc/>
    public override SymbolKind Kind => SymbolKind.Phrase;
    
    /// <inheritdoc/>
    public override SyntaxNode DeclaringSyntax => _declaringSyntax;
    
    /// <summary>
    /// The syntax node that declares this phrase (typed).
    /// </summary>
    public PhraseDeclarationSyntax DeclarationSyntax => _declaringSyntax;
    
    /// <summary>
    /// The music content of this phrase.
    /// </summary>
    public ImmutableArray<SyntaxNode> Body { get; }
}
