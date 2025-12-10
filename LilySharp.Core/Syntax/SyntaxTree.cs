using LilySharp.Core.Parser;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Syntax;

/// <summary>
/// Represents a parsed LilySharp source file.
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

    /// <summary>
    /// Creates a new syntax tree with the specified changes applied.
    /// </summary>
    /// <param name="changes">The changes to apply, in document order.</param>
    /// <returns>A new syntax tree with the changes applied.</returns>
    public SyntaxTree WithChanges(params TextChange[] changes)
    {
        if (changes.Length == 0)
            return this;

        var newText = ApplyChanges(_text, changes);
        return Parse(newText);
    }

    /// <summary>
    /// Creates a new syntax tree with a single change applied.
    /// </summary>
    public SyntaxTree WithChange(TextChange change)
        => WithChanges(change);

    /// <summary>
    /// Applies changes to text, processing from end to start to maintain positions.
    /// </summary>
    private static string ApplyChanges(string text, TextChange[] changes)
    {
        // Sort changes by position descending to apply from end to start
        var sortedChanges = changes.OrderByDescending(c => c.Span.Start).ToArray();
        
        var result = text;
        foreach (var change in sortedChanges)
        {
            var prefix = result[..change.Span.Start];
            var suffix = result[(change.Span.Start + change.Span.Length)..];
            result = prefix + change.NewText + suffix;
        }
        
        return result;
    }
}