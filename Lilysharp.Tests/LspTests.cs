using Xunit;
using Lilysharp.Lsp;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Lilysharp.Tests;

public class LspTests
{
    [Fact]
    public void DocumentManager_OpenOrUpdate_CreatesDocument()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        
        var doc = manager.OpenOrUpdate(uri, "c4 d4 e4");
        
        Assert.NotNull(doc);
        Assert.Equal(uri, doc.Uri);
        Assert.Equal("c4 d4 e4", doc.Text);
        Assert.False(doc.Tree.HasErrors);
    }

    [Fact]
    public void DocumentManager_GetDocument_ReturnsOpenDocument()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4 e4");
        
        var doc = manager.GetDocument(uri);
        
        Assert.NotNull(doc);
        Assert.Equal("c4 d4 e4", doc.Text);
    }

    [Fact]
    public void DocumentManager_GetDocument_ReturnsNullForUnknown()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///unknown.lys");
        
        var doc = manager.GetDocument(uri);
        
        Assert.Null(doc);
    }

    [Fact]
    public void DocumentManager_Close_RemovesDocument()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4 e4");
        
        manager.Close(uri);
        
        Assert.Null(manager.GetDocument(uri));
    }

    [Fact]
    public void DocumentManager_OpenOrUpdate_UpdatesExisting()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4 e4", 1);
        
        var doc = manager.OpenOrUpdate(uri, "f4 g4 a4", 2);
        
        Assert.Equal("f4 g4 a4", doc.Text);
        Assert.Equal(2, doc.Version);
    }

    [Fact]
    public void DocumentManager_ApplyChanges_FullReplacement()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4 e4", 1);
        
        var changes = new[]
        {
            new TextDocumentContentChangeEvent { Text = "f4 g4 a4" }
        };
        
        var doc = manager.ApplyChanges(uri, changes, 2);
        
        Assert.Equal("f4 g4 a4", doc.Text);
        Assert.Equal(2, doc.Version);
    }

    [Fact]
    public void DocumentManager_ApplyChanges_IncrementalChange()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4 e4", 1);
        
        // Replace "d4" with "f8"
        var changes = new[]
        {
            new TextDocumentContentChangeEvent
            {
                Range = new LspRange
                {
                    Start = new Position { Line = 0, Character = 3 },
                    End = new Position { Line = 0, Character = 5 }
                },
                Text = "f8"
            }
        };
        
        var doc = manager.ApplyChanges(uri, changes, 2);
        
        Assert.Equal("c4 f8 e4", doc.Text);
    }

    [Fact]
    public void DocumentManager_GetAllDocuments_ReturnsAll()
    {
        var manager = new DocumentManager();
        manager.OpenOrUpdate(new Uri("file:///a.lys"), "c4");
        manager.OpenOrUpdate(new Uri("file:///b.lys"), "d4");
        manager.OpenOrUpdate(new Uri("file:///c.lys"), "e4");
        
        var docs = manager.GetAllDocuments().ToList();
        
        Assert.Equal(3, docs.Count);
    }

    [Fact]
    public void Document_HasCorrectProperties()
    {
        var uri = new Uri("file:///test.lys");
        var text = "relative c' { c d e f }";
        var tree = Lilysharp.Core.Syntax.SyntaxTree.Parse(text);
        
        var doc = new Document(uri, text, tree, 42);
        
        Assert.Equal(uri, doc.Uri);
        Assert.Equal(text, doc.Text);
        Assert.Same(tree, doc.Tree);
        Assert.Equal(42, doc.Version);
    }

    [Fact]
    public void DocumentManager_ApplyChanges_MultipleChanges()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4 e4 f4", 1);
        
        // Apply two changes: replace first note and last note
        var changes = new[]
        {
            new TextDocumentContentChangeEvent
            {
                Range = new LspRange
                {
                    Start = new Position { Line = 0, Character = 0 },
                    End = new Position { Line = 0, Character = 2 }
                },
                Text = "g2"
            }
        };
        
        var doc = manager.ApplyChanges(uri, changes, 2);
        
        Assert.Equal("g2 d4 e4 f4", doc.Text);
    }

    [Fact]
    public void DocumentManager_ApplyChanges_Insertion()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 e4", 1);
        
        // Insert "d4 " between c4 and e4
        var changes = new[]
        {
            new TextDocumentContentChangeEvent
            {
                Range = new LspRange
                {
                    Start = new Position { Line = 0, Character = 3 },
                    End = new Position { Line = 0, Character = 3 }
                },
                Text = "d4 "
            }
        };
        
        var doc = manager.ApplyChanges(uri, changes, 2);
        
        Assert.Equal("c4 d4 e4", doc.Text);
    }

    [Fact]
    public void DocumentManager_ApplyChanges_Deletion()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4 e4", 1);
        
        // Delete "d4 "
        var changes = new[]
        {
            new TextDocumentContentChangeEvent
            {
                Range = new LspRange
                {
                    Start = new Position { Line = 0, Character = 3 },
                    End = new Position { Line = 0, Character = 6 }
                },
                Text = ""
            }
        };
        
        var doc = manager.ApplyChanges(uri, changes, 2);
        
        Assert.Equal("c4 e4", doc.Text);
    }

    [Fact]
    public void DocumentManager_ApplyChanges_MultilineDocument()
    {
        var manager = new DocumentManager();
        var uri = new Uri("file:///test.lys");
        manager.OpenOrUpdate(uri, "c4 d4\ne4 f4", 1);
        
        // Replace "e4" on second line
        var changes = new[]
        {
            new TextDocumentContentChangeEvent
            {
                Range = new LspRange
                {
                    Start = new Position { Line = 1, Character = 0 },
                    End = new Position { Line = 1, Character = 2 }
                },
                Text = "g8"
            }
        };
        
        var doc = manager.ApplyChanges(uri, changes, 2);
        
        Assert.Equal("c4 d4\ng8 f4", doc.Text);
    }
}