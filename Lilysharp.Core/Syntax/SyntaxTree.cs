using Lilysharp.Core.Parser;
using Lilysharp.Core.Syntax.InternalSyntax;

namespace Lilysharp.Core.Syntax;

/// <summary>
/// Represents a parsed Lilysharp source file.
/// </summary>
public sealed class SyntaxTree
{
    private readonly CompilationUnitGreen _greenRoot;
    private readonly string _text;
    private readonly IReadOnlyList<Diagnostic> _diagnostics;
    private CompilationUnitSyntax? _redRoot;

    private SyntaxTree(string text, CompilationUnitGreen root, IReadOnlyList<Diagnostic> diagnostics)
    {
        _text = text;
        _greenRoot = root;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// The source text.
    /// </summary>
    public string Text => _text;

    /// <summary>
    /// Parse diagnostics (errors, warnings).
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Whether the tree has any errors.
    /// </summary>
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// The root node (internal green).
    /// </summary>
    internal CompilationUnitGreen Root => _greenRoot;

    /// <summary>
    /// Gets the root syntax node (red node with position info).
    /// </summary>
    public CompilationUnitSyntax GetRoot()
    {
        return _redRoot ??= new CompilationUnitSyntax(_greenRoot, null, 0);
    }

    /// <summary>
    /// Parse source text into a syntax tree.
    /// </summary>
    public static SyntaxTree Parse(string text)
    {
        var lexer = new Lexer(text);
        var tokens = lexer.ScanAllTokens();
        var parser = new Parser.Parser(tokens);
        var root = parser.ParseCompilationUnit();
        return new SyntaxTree(text, root, parser.Diagnostics.ToList());
    }

    /// <summary>
    /// Returns the full text reconstructed from the tree.
    /// </summary>
    public string ToFullString()
    {
        return _greenRoot.ToFullString();
    }

    /// <summary>
    /// Find the node at the given position.
    /// </summary>
    public SyntaxNode? FindNode(int position)
    {
        return GetRoot().FindNode(position);
    }

    /// <summary>
    /// Get all nodes of a specific type.
    /// </summary>
    public IEnumerable<T> GetNodes<T>() where T : SyntaxNode
    {
        return GetRoot().DescendantNodes<T>();
    }
}