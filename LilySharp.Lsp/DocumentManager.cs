using LilySharp.Core.Syntax;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace LilySharp.Lsp;

/// <summary>
/// Manages open documents and their syntax trees.
/// </summary>
public sealed class DocumentManager
{
    private readonly Dictionary<Uri, Document> _documents = [];

    /// <summary>
    /// Opens or updates a document.
    /// </summary>
    public Document OpenOrUpdate(Uri uri, string text, int? version = null)
    {
        var tree = SyntaxTree.Parse(text);
        var doc = new Document(uri, text, tree, version ?? 0);
        _documents[uri] = doc;
        return doc;
    }

    /// <summary>
    /// Gets an open document.
    /// </summary>
    public Document? GetDocument(Uri uri)
    {
        return _documents.TryGetValue(uri, out var doc) ? doc : null;
    }

    /// <summary>
    /// Closes a document.
    /// </summary>
    public void Close(Uri uri)
    {
        _documents.Remove(uri);
    }

    /// <summary>
    /// Gets all open documents.
    /// </summary>
    public IEnumerable<Document> GetAllDocuments() => _documents.Values;
    /// <summary>
    /// Applies incremental changes to a document.
    /// </summary>
    public Document ApplyChanges(Uri uri, IEnumerable<TextDocumentContentChangeEvent> changes, int version)
    {
        var doc = GetDocument(uri);
        if (doc == null)
            throw new InvalidOperationException($"Document not found: {uri}");

        var tree = doc.Tree;
        var currentText = doc.Text;
        
        foreach (var change in changes)
        {
            if (change.Range != null)
            {
                // Incremental change - use current text for offset calculation
                var start = GetOffset(currentText, change.Range.Start);
                var end = GetOffset(currentText, change.Range.End);
                var textChange = new TextChange(new TextSpan(start, end - start), change.Text);
                tree = tree.WithChange(textChange);
                currentText = tree.Text; // Update text for next change
            }
            else
            {
                // Full replacement
                tree = SyntaxTree.Parse(change.Text);
                currentText = tree.Text;
            }
        }

        var newDoc = new Document(uri, tree.Text, tree, version);
        _documents[uri] = newDoc;
        return newDoc;
    }

    /// <summary>
    /// Converts line/character position to text offset.
    /// </summary>
    private static int GetOffset(string text, Position position)
    {
        int offset = 0;
        int line = 0;
        
        while (line < position.Line && offset < text.Length)
        {
            if (text[offset] == '\n')
                line++;
            else if (text[offset] == '\r')
            {
                line++;
                if (offset + 1 < text.Length && text[offset + 1] == '\n')
                    offset++;
            }
            offset++;
        }
        
        return offset + position.Character;
    }
}

/// <summary>
/// Represents an open document.
/// </summary>
public sealed class Document
{
    public Uri Uri { get; }
    public string Text { get; }
    public SyntaxTree Tree { get; }
    public int Version { get; }

    public Document(Uri uri, string text, SyntaxTree tree, int version)
    {
        Uri = uri;
        Text = text;
        Tree = tree;
        Version = version;
    }
}


