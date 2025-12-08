using Lilysharp.Core.Parser;
using Lilysharp.Core.Syntax.InternalSyntax;

namespace Lilysharp.Core.Syntax;

/// <summary>
/// Represents a parsed Lilysharp source file.
/// </summary>
public sealed class SyntaxTree
{
    private readonly CompilationUnitGreen _root;
    private readonly string _text;

    private SyntaxTree(string text, CompilationUnitGreen root)
    {
        _text = text;
        _root = root;
    }

    /// <summary>
    /// The source text.
    /// </summary>
    public string Text => _text;

    /// <summary>
    /// The root node (internal).
    /// </summary>
    internal CompilationUnitGreen Root => _root;

    /// <summary>
    /// Parse source text into a syntax tree.
    /// </summary>
    public static SyntaxTree Parse(string text)
    {
        var lexer = new Lexer(text);
        var tokens = lexer.ScanAllTokens();
        var parser = new Parser.Parser(tokens);
        var root = parser.ParseCompilationUnit();
        return new SyntaxTree(text, root);
    }

    /// <summary>
    /// Returns the full text reconstructed from the tree.
    /// </summary>
    public string ToFullString()
    {
        return _root.ToFullString();
    }
}