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