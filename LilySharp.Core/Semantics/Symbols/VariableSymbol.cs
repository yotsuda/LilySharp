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
