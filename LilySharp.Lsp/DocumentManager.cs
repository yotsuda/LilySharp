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

using LilySharp.Core.Syntax;
using LilySharp.Lsp.Protocol;

namespace LilySharp.Lsp;

/// <summary>
/// Manages open documents and their syntax trees.
/// </summary>
/// <remarks>
/// Thread-safe: diagnostics are published from debounced background tasks
/// while didChange notifications mutate the map on the dispatch thread, so
/// every access goes through <see cref="_gate"/>. <see cref="Document"/>
/// instances themselves are immutable.
/// </remarks>
public sealed class DocumentManager
{
    private readonly Dictionary<Uri, Document> _documents = [];
    private readonly object _gate = new();

    /// <summary>
    /// Opens or updates a document.
    /// </summary>
    public Document OpenOrUpdate(Uri uri, string text, int? version = null)
    {
        var tree = SyntaxTree.Parse(text);
        var doc = new Document(uri, text, tree, version ?? 0);
        lock (_gate)
            _documents[uri] = doc;
        return doc;
    }

    /// <summary>
    /// Gets an open document.
    /// </summary>
    public Document? GetDocument(Uri uri)
    {
        lock (_gate)
            return _documents.TryGetValue(uri, out var doc) ? doc : null;
    }

    /// <summary>
    /// Closes a document.
    /// </summary>
    public void Close(Uri uri)
    {
        lock (_gate)
            _documents.Remove(uri);
    }

    /// <summary>
    /// Gets all open documents.
    /// </summary>
    public IEnumerable<Document> GetAllDocuments()
    {
        lock (_gate)
            return _documents.Values.ToArray();
    }

    /// <summary>
    /// Applies incremental changes to a document. Per the LSP spec the changes
    /// are sequential — each Range refers to the document state AFTER the
    /// previous change — so the edits are applied to the TEXT one by one and
    /// the result is parsed ONCE (previously every change in a batch triggered
    /// its own full re-parse).
    /// </summary>
    public Document ApplyChanges(Uri uri, IEnumerable<TextDocumentContentChangeEvent> changes, int version)
    {
        var baseDoc = GetDocument(uri);
        var doc = baseDoc;
        if (doc == null)
        {
            // A didChange before didOpen is a client protocol violation. Rather than
            // throw (which drops the edit and surfaces an error), start from an empty
            // document and apply the changes best-effort; the client re-syncs on its
            // next full update.
            var empty = SyntaxTree.Parse("");
            doc = new Document(uri, empty.Text, empty, version);
        }

        var changeList = changes as IReadOnlyList<TextDocumentContentChangeEvent> ?? changes.ToList();

        SyntaxTree tree;
        if (changeList.Count == 1 && changeList[0].Range != null)
        {
            // The common case (one edit per keystroke) goes through the
            // INCREMENTAL reparse: unchanged top-level items of the previous
            // tree are reused instead of being re-parsed.
            var change = changeList[0];
            var start = GetOffset(doc.Text, change.Range!.Start);
            var end = Math.Max(start, GetOffset(doc.Text, change.Range.End));
            tree = doc.Tree.WithChange(
                new TextChange(new TextSpan(start, end - start), change.Text));
        }
        else
        {
            // Multi-change batches and full replacements: apply the edits to
            // the text sequentially (per the LSP spec each Range refers to the
            // post-previous-change state) and parse once.
            var currentText = doc.Text;
            foreach (var change in changeList)
            {
                if (change.Range != null)
                {
                    var start = GetOffset(currentText, change.Range.Start);
                    var end = Math.Max(start, GetOffset(currentText, change.Range.End));
                    currentText = currentText[..start] + change.Text + currentText[end..];
                }
                else
                {
                    // Full replacement
                    currentText = change.Text;
                }
            }
            tree = SyntaxTree.Parse(currentText);
        }

        var newDoc = new Document(uri, tree.Text, tree, version);
        lock (_gate)
        {
            // Atomic compare-and-store: drop this update if a newer version is already
            // stored, or if the document changed since we read our base (a stale or
            // out-of-order didChange whose text was computed from now-superseded state).
            // Notifications dispatch sequentially today, so this is a safety net for
            // concurrent access rather than a currently-hit race.
            var current = _documents.GetValueOrDefault(uri);
            if (current != null && (version < current.Version || !ReferenceEquals(current, baseDoc)))
                return current;
            _documents[uri] = newDoc;
        }
        return newDoc;
    }

    /// <summary>
    /// Converts an LSP line/character position to a text offset. The LSP
    /// default position encoding is UTF-16 code units, which is exactly C#'s
    /// string indexing, so <c>Position.Character</c> maps 1:1. Out-of-range
    /// positions are clamped per the spec ("if the character value is greater
    /// than the line length it defaults back to the line length").
    /// </summary>
    internal static int GetOffset(string text, Position position)
    {
        // The shared per-text-instance line index (LilySharpLanguageServer.LineStartsOf)
        // instead of a scan from offset 0 per edit — this runs once per change per
        // didChange, and the index is the one semantic tokens / diagnostics already
        // build for the same text instance. A line past the last one lands at the end
        // of the text, exactly where the old scan's `offset < text.Length` bound left it.
        var lineStarts = LilySharpLanguageServer.LineStartsOf(text);
        int offset = position.Line <= 0 ? 0
            : position.Line < lineStarts.Length ? lineStarts[position.Line]
            : text.Length;

        // Clamp the character offset to the end of this line (and the text).
        int lineEnd = offset;
        while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r')
            lineEnd++;

        return Math.Min(offset + Math.Max(0, position.Character), lineEnd);
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


