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
/// Base class for all symbols in LilySharp.
/// Symbols represent named entities that can be referenced in the music.
/// </summary>
/// <remarks>
/// Design inspired by Roslyn's Symbol class.
/// All symbols are immutable records.
/// </remarks>
public abstract record Symbol
{
    /// <summary>
    /// The name of this symbol.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// The kind of this symbol.
    /// </summary>
    public abstract SymbolKind Kind { get; }

    /// <summary>
    /// The syntax node that declares this symbol.
    /// </summary>
    public abstract SyntaxNode DeclaringSyntax { get; }

    /// <summary>
    /// The source location where this symbol is declared.
    /// </summary>
    public TextSpan Location => new(DeclaringSyntax.Position, DeclaringSyntax.FullWidth);
}

/// <summary>
/// Represents a span of text in the source.
/// </summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool Contains(int position) => position >= Start && position < End;

    public bool Overlaps(TextSpan other) => Start < other.End && other.Start < End;
}
