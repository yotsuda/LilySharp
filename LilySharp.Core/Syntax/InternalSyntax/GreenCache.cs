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

namespace LilySharp.Core.Syntax.InternalSyntax;

/// <summary>
/// Interning caches for green tokens and trivia. Green nodes are immutable
/// and position-free, so identical tokens (<c>c4</c>, <c>|</c>, a single
/// space of trivia, …) can be shared between all trees and across reparses.
/// </summary>
/// <remarks>
/// Modeled on Roslyn's LexerCache / TextKeyedCache / CachingFactory: a
/// fixed-size direct-mapped table, overwrite-on-collision, tolerating benign
/// races (a lost write merely costs one duplicate allocation — entries are
/// published as whole immutable objects, so readers never see a torn entry).
/// Trivia lookups are span-keyed so the substring itself is only allocated
/// on a cache miss.
/// </remarks>
internal static class GreenCache
{
    private const int Size = 1 << 11; // 2048 entries each
    private const int Mask = Size - 1;

    // ---------- Trivia ----------

    private sealed class TriviaEntry(SyntaxKind kind, string text, SyntaxTrivia trivia)
    {
        public readonly SyntaxKind Kind = kind;
        public readonly string Text = text;
        public readonly SyntaxTrivia Trivia = trivia;
    }

    private static readonly TriviaEntry?[] TriviaTable = new TriviaEntry?[Size];

    /// <summary>Gets a (possibly shared) trivia node for the given span.</summary>
    public static SyntaxTrivia GetTrivia(SyntaxKind kind, ReadOnlySpan<char> span)
    {
        int hash = HashSpan(kind, span);
        int index = hash & Mask;

        var entry = TriviaTable[index];
        if (entry != null && entry.Kind == kind && span.SequenceEqual(entry.Text))
            return entry.Trivia;

        var text = span.ToString();
        var trivia = new SyntaxTrivia(kind, text);
        TriviaTable[index] = new TriviaEntry(kind, text, trivia);
        return trivia;
    }

    // ---------- Tokens ----------

    private sealed class TokenEntry(
        SyntaxKind kind, string text, GreenNode? leading, GreenNode? trailing, SyntaxToken token)
    {
        public readonly SyntaxKind Kind = kind;
        public readonly string Text = text;
        public readonly GreenNode? Leading = leading;
        public readonly GreenNode? Trailing = trailing;
        public readonly SyntaxToken Token = token;
    }

    private static readonly TokenEntry?[] TokenTable = new TokenEntry?[Size];

    /// <summary>
    /// Gets a (possibly shared) token. Trivia are compared by REFERENCE — the
    /// trivia cache above makes identical trivia share instances, so common
    /// shapes like "c4 + one trailing space" hit reliably.
    /// </summary>
    public static SyntaxToken GetToken(
        SyntaxKind kind, string text, GreenNode? leading, GreenNode? trailing)
    {
        int hash = HashSpan(kind, text.AsSpan());
        if (leading != null) hash = unchecked(hash * 31 + leading.GetHashCode());
        if (trailing != null) hash = unchecked(hash * 31 + trailing.GetHashCode());
        int index = hash & Mask;

        var entry = TokenTable[index];
        if (entry != null
            && entry.Kind == kind
            && ReferenceEquals(entry.Leading, leading)
            && ReferenceEquals(entry.Trailing, trailing)
            && entry.Text == text)
        {
            return entry.Token;
        }

        var token = new SyntaxToken(kind, text, leading, trailing);
        TokenTable[index] = new TokenEntry(kind, text, leading, trailing, token);
        return token;
    }

    private static int HashSpan(SyntaxKind kind, ReadOnlySpan<char> span)
    {
        int hash = (int)kind;
        foreach (char c in span)
            hash = unchecked(hash * 31 + c);
        return hash;
    }
}
