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
/// A token in the green tree. Tokens are leaf nodes with text.
/// </summary>
internal class SyntaxToken : GreenNode
{
    private readonly string _text;
    private readonly GreenNode? _leadingTrivia;
    private readonly GreenNode? _trailingTrivia;

    public SyntaxToken(SyntaxKind kind, string text)
        : this(kind, text, null, null)
    {
    }

    public SyntaxToken(SyntaxKind kind, string text, GreenNode? leadingTrivia, GreenNode? trailingTrivia)
        : base(kind, ComputeFullWidth(text, leadingTrivia, trailingTrivia))
    {
        _text = text;
        _leadingTrivia = leadingTrivia;
        _trailingTrivia = trailingTrivia;
    }

    private static int ComputeFullWidth(string text, GreenNode? leading, GreenNode? trailing)
    {
        return text.Length
             + (leading?.FullWidth ?? 0)
             + (trailing?.FullWidth ?? 0);
    }

    public override bool IsToken => true;
    public override string Text => _text;
    public override GreenNode? LeadingTrivia => _leadingTrivia;
    public override GreenNode? TrailingTrivia => _trailingTrivia;

    /// <summary>
    /// Creates a copy with different trivia.
    /// </summary>
    public SyntaxToken WithTrivia(GreenNode? leading, GreenNode? trailing)
    {
        if (leading == _leadingTrivia && trailing == _trailingTrivia)
            return this;
        return new SyntaxToken(Kind, _text, leading, trailing);
    }

    public override void WriteTo(System.IO.TextWriter writer)
    {
        _leadingTrivia?.WriteTo(writer);
        writer.Write(_text);
        _trailingTrivia?.WriteTo(writer);
    }
}

/// <summary>
/// Trivia node (whitespace, comments).
/// </summary>
internal class SyntaxTrivia : GreenNode
{
    private readonly string _text;

    public SyntaxTrivia(SyntaxKind kind, string text)
        : base(kind, text.Length)
    {
        _text = text;
    }

    public override bool IsTrivia => true;
    public override string Text => _text;

    public override void WriteTo(System.IO.TextWriter writer)
    {
        writer.Write(_text);
    }
}

/// <summary>
/// A list of trivia (used for leading/trailing trivia on tokens).
/// </summary>
internal class SyntaxTriviaList : GreenNode
{
    private readonly GreenNode[] _triviaNodes;

    public SyntaxTriviaList(GreenNode[] nodes)
        : base(SyntaxKind.None, nodes)
    {
        _triviaNodes = nodes;
    }

    public override bool IsTrivia => true;

    public override void WriteTo(System.IO.TextWriter writer)
    {
        foreach (var node in _triviaNodes)
        {
            node.WriteTo(writer);
        }
    }
}