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
