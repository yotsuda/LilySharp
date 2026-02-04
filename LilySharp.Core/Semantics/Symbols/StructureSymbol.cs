using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Represents a structure definition.
/// </summary>
/// <example>
/// structure {
///     |: A [1. B] [2. C] :|
/// }
/// </example>
public sealed record StructureSymbol : Symbol
{
    /// <summary>
    /// Creates a new structure symbol.
    /// </summary>
    /// <param name="declaringSyntax">The syntax node that declares this structure.</param>
    public StructureSymbol(StructureDeclarationSyntax declaringSyntax)
    {
        _declaringSyntax = declaringSyntax;
    }

    private readonly StructureDeclarationSyntax _declaringSyntax;

    /// <inheritdoc/>
    public override string Name => "structure";

    /// <inheritdoc/>
    public override SymbolKind Kind => SymbolKind.Structure;

    /// <inheritdoc/>
    public override SyntaxNode DeclaringSyntax => _declaringSyntax;

    /// <summary>
    /// The syntax node that declares this structure (typed).
    /// </summary>
    public StructureDeclarationSyntax DeclarationSyntax => _declaringSyntax;
}
