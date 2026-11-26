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
    private readonly bool _leftOut;

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

    private SyntaxToken(SyntaxKind kind, string spelling, bool leftOut)
        : base(kind, 0)
    {
        _text = spelling;
        _leftOut = leftOut;
    }

    /// <summary>
    /// A token the writer LEFT OUT where the grammar lets them — the bar line between two
    /// percent marks in a row (<c>% % %</c> reads as <c>% | % | %</c>) — put into the tree
    /// where it is read: it has the spelling every reader expects (<see cref="Text"/> is
    /// <paramref name="spelling"/>), no width, no trivia, and writes nothing, so the tree's
    /// full text and every position stay the source's. Not an error-recovery token
    /// (those are empty, see Parser.Expect): nothing is reported for it.
    /// </summary>
    public static SyntaxToken LeftOut(SyntaxKind kind, string spelling) => new(kind, spelling, leftOut: true);

    /// <summary>Whether this token stands for one the writer left out (<see cref="LeftOut"/>).</summary>
    public bool IsLeftOut => _leftOut;

    private readonly string? _source;

    private SyntaxToken(SyntaxKind kind, string spelling, string source, GreenNode? leadingTrivia, GreenNode? trailingTrivia)
        : base(kind, ComputeFullWidth(source, leadingTrivia, trailingTrivia))
    {
        _text = spelling;
        _source = source;
        _leadingTrivia = leadingTrivia;
        _trailingTrivia = trailingTrivia;
    }

    /// <summary>
    /// A token WRITTEN DIFFERENTLY from how it reads: <paramref name="source"/> is what stands in
    /// the file (its width, what <see cref="WriteTo"/> writes), <paramref name="spelling"/> is what
    /// every reader sees as <see cref="Text"/>. A drum steps chart's <c>x</c> reads as the row's
    /// drum <c>hh</c> (Parser.Steps): the tree carries the drum note the step stands for, at the
    /// step's own place, so data-pos, diagnostics and the round trip stay the source's. An empty
    /// <paramref name="source"/> is a <see cref="LeftOut"/> that may carry trivia.
    /// </summary>
    public static SyntaxToken Spelled(SyntaxKind kind, string spelling, string source,
        GreenNode? leadingTrivia = null, GreenNode? trailingTrivia = null)
        => new(kind, spelling, source, leadingTrivia, trailingTrivia);

    /// <summary>Whether this token is written differently from how it reads (<see cref="Spelled"/>).</summary>
    public bool IsSpelled => _source != null;

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
        if (_source != null)
            return new SyntaxToken(Kind, _text, _source, leading, trailing);
        return new SyntaxToken(Kind, _text, leading, trailing);
    }

    public override void WriteTo(System.IO.TextWriter writer)
    {
        if (_leftOut)
            return;
        _leadingTrivia?.WriteTo(writer);
        writer.Write(_source ?? _text);
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