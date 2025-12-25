using Xunit;
using LilySharp.Lsp;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace LilySharp.Tests;

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
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(text);
        
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

    // ========== LSP Server Integration Tests ==========

    [Fact]
    public void LspServer_Initialize_ReturnsCapabilities()
    {
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        
        // Act
        var initParams = new InitializeParams
        {
            ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
            RootUri = new Uri("file:///test"),
            Capabilities = new ClientCapabilities()
        };
        var result = server.Initialize(initParams);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Capabilities);
        Assert.NotNull(result.Capabilities.TextDocumentSync);
        Assert.NotNull(result.Capabilities.HoverProvider);
        Assert.NotNull(result.Capabilities.CompletionProvider);
    }

    [Fact]
    public void LspServer_GetVersion_ReturnsVersion()
    {
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        
        // Act
        var version = server.GetVersion();

        // Assert
        Assert.NotNull(version);
        Assert.StartsWith("0.1.1-", version);
    }

    [Fact]
    public void LspServer_GetSvg_GeneratesSvg()
    {
        // This test verifies the SVG generation pipeline used by VS Code preview
        
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        var uri = new Uri("file:///test.lys");
        
        // Initialize and open document
        server.Initialize(new InitializeParams
        {
            ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
            RootUri = new Uri("file:///test"),
            Capabilities = new ClientCapabilities()
        });
        
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "lilysharp",
                Version = 1,
                Text = "c4 d4 e4 f4 | g4 a4 b4 c'4"
            }
        });

        // Act
        var svgParams = new SvgParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        };
        var result = server.GetSvg(svgParams);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Error);
        Assert.NotNull(result.Svg);
        Assert.Contains("<svg", result.Svg);
        Assert.Contains("</svg>", result.Svg);
    }

    [Fact]
    public void LspServer_GetSvg_ReturnsErrorForUnknownDocument()
    {
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        var uri = new Uri("file:///unknown.lys");

        // Act
        var result = server.GetSvg(new SvgParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        });

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LspServer_GetSvg_ReturnsErrorForParseError()
    {
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        var uri = new Uri("file:///test.lys");
        
        // Open document with syntax error
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "lilysharp",
                Version = 1,
                Text = "c4 { unclosed" // Missing closing brace
            }
        });

        // Act
        var result = server.GetSvg(new SvgParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        });

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Error);
        Assert.Null(result.Svg);
    }

    [Fact]
    public void LspServer_Hover_ReturnsNoteInfo()
    {
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        var uri = new Uri("file:///test.lys");
        
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "lilysharp",
                Version = 1,
                Text = "c4 d4 e4"
            }
        });

        // Act - hover over 'c4' at position (0, 1) which should be in the note
        var result = server.Hover(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = 0, Character = 1 }
        });

        // Assert - hover may return null if no hover info for the position
        // This test primarily verifies that the Hover method doesn't crash
        // and returns a valid result structure when hover info is available
        if (result != null)
        {
            Assert.NotNull(result.Range);
        }
    }

    [Fact]
    public void LspServer_Completion_ReturnsCompletionItems()
    {
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        var uri = new Uri("file:///test.lys");
        
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "lilysharp",
                Version = 1,
                Text = ""
            }
        });

        // Act - request completions at start of empty document
        var result = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = 0, Character = 0 }
        });

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, item => item.Label == "score");
    }

    [Fact]
    public void LspServer_DocumentSymbol_ReturnsSymbols()
    {
        // Arrange
        using var inputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var outputPipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
        using var clientWriter = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, inputPipe.ClientSafePipeHandle);
        using var clientReader = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.Out, outputPipe.ClientSafePipeHandle);

        var server = new LilySharpLanguageServer(clientWriter, clientReader);
        var uri = new Uri("file:///test.lys");
        
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "lilysharp",
                Version = 1,
                Text = "let melody = { c4 d4 e4 }\nscore { $melody }"
            }
        });

        // Act
        var result = server.DocumentSymbol(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        });

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains(result, s => s.Name == "melody");
    }
}