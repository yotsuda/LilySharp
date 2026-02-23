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
