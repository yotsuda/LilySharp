using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Represents a variable definition.
/// </summary>
/// <example>
/// theme = c4 d e f
/// </example>
public sealed record VariableSymbol : Symbol
{
    /// <summary>
    /// Creates a new variable symbol.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="declaringSyntax">The syntax node that declares this variable.</param>
    /// <param name="value">The expression assigned to this variable.</param>
    public VariableSymbol(
        string name,
        VariableDeclarationSyntax declaringSyntax,
        SyntaxNode value)
    {
        _name = name;
        _declaringSyntax = declaringSyntax;
        Value = value;
    }

    private readonly string _name;
    private readonly VariableDeclarationSyntax _declaringSyntax;

    /// <inheritdoc/>
    public override string Name => _name;

    /// <inheritdoc/>
    public override SymbolKind Kind => SymbolKind.Variable;

    /// <inheritdoc/>
    public override SyntaxNode DeclaringSyntax => _declaringSyntax;

    /// <summary>
    /// The syntax node that declares this variable (typed).
    /// </summary>
    public VariableDeclarationSyntax DeclarationSyntax => _declaringSyntax;

    /// <summary>
    /// The expression assigned to this variable.
    /// </summary>
    public SyntaxNode Value { get; }
}
