using Lilysharp.Core.Syntax;

namespace Lilysharp.Lsp;

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