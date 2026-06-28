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
    // The lexer's token stream, retained for incremental LEXING on the next
    // edit. Unlike tokens enumerated from the green tree, this stream is
    // text-faithful even when the parse had errors (the parser synthesizes
    // missing tokens; the lexer never does).
    private readonly IReadOnlyList<SyntaxToken> _tokens;
    private CompilationUnitSyntax? _redRoot;

    private SyntaxTree(string text, CompilationUnitGreen root,
        IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<SyntaxToken> tokens)
    {
        _text = text;
        _greenRoot = root;
        _diagnostics = diagnostics;
        _tokens = tokens;
    }

    internal IReadOnlyList<SyntaxToken> Tokens => _tokens;

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
        var tokens = lexer.ScanAllTokens().ToList();
        var parser = new Parser.Parser(tokens);
        var root = parser.ParseCompilationUnit();
        return new SyntaxTree(text, root, MergeDiagnostics(tokens, parser.Diagnostics), tokens);
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

        // Combined damaged region in OLD-text coordinates (all spans refer to
        // the original text; ApplyChanges applies them back-to-front).
        int damageStart = changes.Min(c => c.Span.Start);
        int damageOldEnd = changes.Max(c => c.Span.End);

        return ParseIncremental(this, newText, damageStart, damageOldEnd);
    }

    /// <summary>
    /// Incremental reparse. Both halves are incremental:
    /// LEXING — tokens outside the damaged region are spliced from the old
    /// tree's retained stream (re-lexing only the damage, resynchronizing on
    /// the first shared token boundary after it); PARSING — top-level items
    /// outside the edit are adopted wholesale because green nodes are
    /// position-free. Both fall back to full work automatically when reuse
    /// is not possible (e.g. an edit that opens an unterminated comment).
    /// </summary>
    public static SyntaxTree ParseIncremental(
        SyntaxTree oldTree, string newText, int damageStart, int damageOldEnd)
    {
        int delta = newText.Length - oldTree._text.Length;
        var reuse = IncrementalReuseMap.Create(oldTree, damageStart, damageOldEnd, delta);

        var tokens = IncrementalLexer.Splice(
            oldTree.Tokens, newText, damageStart, damageOldEnd, delta);

        var parser = new Parser.Parser(tokens, reuse);
        var root = parser.ParseCompilationUnit();
        return new SyntaxTree(newText, root, MergeDiagnostics(tokens, parser.Diagnostics), tokens);
    }

    /// <summary>Combines lexical diagnostics (derived from the final token stream by
    /// <see cref="Parser.LexicalDiagnostics"/>, so full and incremental parses agree)
    /// with the parser's, ordered by source position — an unterminated string/comment
    /// reads in line with the parser errors that follow it.</summary>
    private static List<Diagnostic> MergeDiagnostics(
        IReadOnlyList<SyntaxToken> tokens, DiagnosticBag parser)
    {
        var combined = Parser.LexicalDiagnostics.Scan(tokens);
        combined.AddRange(parser.ToList());
        combined.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return combined;
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