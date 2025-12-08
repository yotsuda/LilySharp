using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using Lilysharp.Core.Syntax;
using Lilysharp.Core.Semantics;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = Lilysharp.Core.Syntax.DiagnosticSeverity;

namespace Lilysharp.Lsp;

/// <summary>
/// Lilysharp Language Server implementation.
/// </summary>
public sealed class LilysharpLanguageServer
{
    private readonly JsonRpc _rpc;
    private readonly DocumentManager _documentManager = new();

    public LilysharpLanguageServer(Stream input, Stream output)
    {
        var handler = new HeaderDelimitedMessageHandler(input, output);
        _rpc = new JsonRpc(handler, this);
    }

    public async Task RunAsync()
    {
        _rpc.StartListening();
        await _rpc.Completion;
    }

    [JsonRpcMethod(Methods.InitializeName)]
    public InitializeResult Initialize(InitializeParams @params)
    {
        return new InitializeResult
        {
            Capabilities = new ServerCapabilities
            {
                TextDocumentSync = new TextDocumentSyncOptions
                {
                    OpenClose = true,
                    Change = TextDocumentSyncKind.Full,
                    Save = new SaveOptions { IncludeText = true }
                },
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = ["\\", "@", " "],
                    ResolveProvider = false
                },
                HoverProvider = true,
                DocumentSymbolProvider = true,
                DefinitionProvider = true
            }
        };
    }

    [JsonRpcMethod(Methods.InitializedName)]
    public void Initialized()
    {
        // Client is ready
    }

    [JsonRpcMethod(Methods.ShutdownName)]
    public object? Shutdown()
    {
        return null;
    }

    [JsonRpcMethod(Methods.ExitName)]
    public void Exit()
    {
        Environment.Exit(0);
    }

    // ========== Text Document Synchronization ==========

    [JsonRpcMethod(Methods.TextDocumentDidOpenName)]
    public void DidOpen(DidOpenTextDocumentParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var text = @params.TextDocument.Text;
        var version = @params.TextDocument.Version;

        var doc = _documentManager.OpenOrUpdate(uri, text, version);
        PublishDiagnostics(doc);
    }

    [JsonRpcMethod(Methods.TextDocumentDidChangeName)]
    public void DidChange(DidChangeTextDocumentParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var version = @params.TextDocument.Version;

        // Full sync - use the last content change
        if (@params.ContentChanges.Length > 0)
        {
            var text = @params.ContentChanges[^1].Text;
            var doc = _documentManager.OpenOrUpdate(uri, text, version);
            PublishDiagnostics(doc);
        }
    }

    [JsonRpcMethod(Methods.TextDocumentDidCloseName)]
    public void DidClose(DidCloseTextDocumentParams @params)
    {
        _documentManager.Close(@params.TextDocument.Uri);
        
        // Clear diagnostics
        _rpc.NotifyAsync(Methods.TextDocumentPublishDiagnosticsName, new PublishDiagnosticParams
        {
            Uri = @params.TextDocument.Uri,
            Diagnostics = []
        });
    }

    [JsonRpcMethod(Methods.TextDocumentDidSaveName)]
    public void DidSave(DidSaveTextDocumentParams @params)
    {
        if (@params.Text != null)
        {
            var doc = _documentManager.OpenOrUpdate(@params.TextDocument.Uri, @params.Text);
            PublishDiagnostics(doc);
        }
    }

    // ========== Diagnostics ==========

    private void PublishDiagnostics(Document doc)
    {
        var diagnostics = new List<Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic>();

        // Parser diagnostics
        foreach (var d in doc.Tree.Diagnostics)
        {
            diagnostics.Add(ConvertDiagnostic(d, doc.Text));
        }

        // Semantic diagnostics (measure validation)
        var validator = new MeasureValidator();
        validator.Validate(doc.Tree);
        foreach (var d in validator.Diagnostics)
        {
            diagnostics.Add(ConvertDiagnostic(d, doc.Text));
        }

        _rpc.NotifyAsync(Methods.TextDocumentPublishDiagnosticsName, new PublishDiagnosticParams
        {
            Uri = doc.Uri,
            Diagnostics = [.. diagnostics]
        });
    }

    private static Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic ConvertDiagnostic(
        Lilysharp.Core.Syntax.Diagnostic d, string text)
    {
        var (startLine, startCol) = GetLineAndColumn(text, d.Span.Start);
        var (endLine, endCol) = GetLineAndColumn(text, d.Span.Start + d.Span.Length);

        return new Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic
        {
            Range = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            },
            Severity = d.Severity switch
            {
                CoreDiagnosticSeverity.Error => LspDiagnosticSeverity.Error,
                CoreDiagnosticSeverity.Warning => LspDiagnosticSeverity.Warning,
                CoreDiagnosticSeverity.Info => LspDiagnosticSeverity.Information,
                _ => LspDiagnosticSeverity.Hint
            },
            Code = d.Code,
            Source = "lilysharp",
            Message = d.Message
        };
    }

    private static (int line, int col) GetLineAndColumn(string text, int position)
    {
        int line = 0;
        int col = 0;
        for (int i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }
        }
        return (line, col);
    }

    // ========== Completion ==========

    [JsonRpcMethod(Methods.TextDocumentCompletionName)]
    public CompletionList? Completion(CompletionParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);

        // Determine context
        var context = GetCompletionContext(doc.Text, offset);

        return context switch
        {
            CompletionContext.TopLevel => GetTopLevelCompletions(),
            CompletionContext.MusicBlock => GetMusicCompletions(),
            CompletionContext.AfterAt => GetArticulationCompletions(),
            CompletionContext.AfterBackslash => GetDynamicCompletions(),
            _ => null
        };
    }

    private enum CompletionContext
    {
        Unknown,
        TopLevel,
        MusicBlock,
        AfterAt,
        AfterBackslash
    }

    private CompletionContext GetCompletionContext(string text, int offset)
    {
        if (offset == 0)
            return CompletionContext.TopLevel;

        // Look back for context clues
        int i = offset - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;

        if (i >= 0)
        {
            if (text[i] == '@')
                return CompletionContext.AfterAt;
            if (text[i] == '\\')
                return CompletionContext.AfterBackslash;
            if (text[i] == '{')
                return CompletionContext.MusicBlock;
        }

        // Check if inside braces
        int braceDepth = 0;
        for (int j = 0; j < offset; j++)
        {
            if (text[j] == '{') braceDepth++;
            else if (text[j] == '}') braceDepth--;
        }

        return braceDepth > 0 ? CompletionContext.MusicBlock : CompletionContext.TopLevel;
    }

    private static CompletionList GetTopLevelCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertText = "score {\n\t$0\n}" },
                new CompletionItem { Label = "part", Kind = CompletionItemKind.Keyword, InsertText = "part {\n\t$0\n}" },
                new CompletionItem { Label = "relative", Kind = CompletionItemKind.Keyword, InsertText = "relative c' {\n\t$0\n}" },
                new CompletionItem { Label = "let", Kind = CompletionItemKind.Keyword, InsertText = "let $1 = $0" },
                new CompletionItem { Label = "title", Kind = CompletionItemKind.Keyword, InsertText = "title \"$0\"" },
                new CompletionItem { Label = "composer", Kind = CompletionItemKind.Keyword, InsertText = "composer \"$0\"" },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertText = "tempo $0" },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertText = "time $0" },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertText = "key $0" }
            ]
        };
    }

    private static CompletionList GetMusicCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "c", Kind = CompletionItemKind.Value, Detail = "C pitch" },
                new CompletionItem { Label = "d", Kind = CompletionItemKind.Value, Detail = "D pitch" },
                new CompletionItem { Label = "e", Kind = CompletionItemKind.Value, Detail = "E pitch" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "F pitch" },
                new CompletionItem { Label = "g", Kind = CompletionItemKind.Value, Detail = "G pitch" },
                new CompletionItem { Label = "a", Kind = CompletionItemKind.Value, Detail = "A pitch" },
                new CompletionItem { Label = "b", Kind = CompletionItemKind.Value, Detail = "B pitch" },
                new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest" },
                new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertText = "repeat volta 2 {\n\t$0\n}" }
            ]
        };
    }

    private static CompletionList GetArticulationCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "staccato", Kind = CompletionItemKind.Value, Detail = "Staccato articulation" },
                new CompletionItem { Label = "accent", Kind = CompletionItemKind.Value, Detail = "Accent" },
                new CompletionItem { Label = "tenuto", Kind = CompletionItemKind.Value, Detail = "Tenuto" },
                new CompletionItem { Label = "marcato", Kind = CompletionItemKind.Value, Detail = "Marcato" },
                new CompletionItem { Label = "fermata", Kind = CompletionItemKind.Value, Detail = "Fermata" },
                new CompletionItem { Label = "portato", Kind = CompletionItemKind.Value, Detail = "Portato" }
            ]
        };
    }

    private static CompletionList GetDynamicCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "ppp", Kind = CompletionItemKind.Value, Detail = "Pianississimo" },
                new CompletionItem { Label = "pp", Kind = CompletionItemKind.Value, Detail = "Pianissimo" },
                new CompletionItem { Label = "p", Kind = CompletionItemKind.Value, Detail = "Piano" },
                new CompletionItem { Label = "mp", Kind = CompletionItemKind.Value, Detail = "Mezzo-piano" },
                new CompletionItem { Label = "mf", Kind = CompletionItemKind.Value, Detail = "Mezzo-forte" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "Forte" },
                new CompletionItem { Label = "ff", Kind = CompletionItemKind.Value, Detail = "Fortissimo" },
                new CompletionItem { Label = "fff", Kind = CompletionItemKind.Value, Detail = "Fortississimo" },
                new CompletionItem { Label = "cresc", Kind = CompletionItemKind.Value, Detail = "Crescendo" },
                new CompletionItem { Label = "decresc", Kind = CompletionItemKind.Value, Detail = "Decrescendo" },
                new CompletionItem { Label = "dim", Kind = CompletionItemKind.Value, Detail = "Diminuendo" }
            ]
        };
    }

    // ========== Hover ==========

    [JsonRpcMethod(Methods.TextDocumentHoverName)]
    public Hover? Hover(TextDocumentPositionParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return null;

        var offset = GetOffset(doc.Text, @params.Position.Line, @params.Position.Character);
        var node = doc.Tree.FindNode(offset);

        if (node == null)
            return null;

        var content = GetHoverContent(node);
        if (content == null)
            return null;

        var (startLine, startCol) = GetLineAndColumn(doc.Text, node.Position);
        var (endLine, endCol) = GetLineAndColumn(doc.Text, node.Position + node.FullWidth);

        return new Hover
        {
            Contents = new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = content
            },
            Range = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            }
        };
    }

    private static string? GetHoverContent(SyntaxNode node)
    {
        return node switch
        {
            NoteSyntax note => $"**Note**: {note.Pitch.PitchName}\n\nOctave offset: {note.Pitch.OctaveOffset}\n\nDuration: {note.Duration?.Value.ToString() ?? "inherited"}",
            RestSyntax rest => $"**Rest**\n\nDuration: {rest.Duration?.Value.ToString() ?? "inherited"}",
            ChordSyntax => "**Chord**",
            BarlineSyntax => "**Barline**",
            TieSyntax => "**Tie**: Connects two notes of the same pitch",
            SlurSyntax slur => slur.IsOpen ? "**Slur start**" : "**Slur end**",
            RepeatExpressionSyntax => "**Repeat**: Repeats the enclosed music",
            ParallelExpressionSyntax => "**Parallel**: Multiple voices played simultaneously",
            _ => null
        };
    }

    private static int GetOffset(string text, int line, int character)
    {
        int offset = 0;
        int currentLine = 0;

        while (offset < text.Length && currentLine < line)
        {
            if (text[offset] == '\n')
                currentLine++;
            offset++;
        }

        return Math.Min(offset + character, text.Length);
    }

    // ========== Document Symbols ==========

    [JsonRpcMethod(Methods.TextDocumentDocumentSymbolName)]
    public DocumentSymbol[]? DocumentSymbol(DocumentSymbolParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var symbols = new List<DocumentSymbol>();
        CollectSymbols(doc.Tree.GetRoot(), doc.Text, symbols);
        return symbols.ToArray();
    }

    private void CollectSymbols(SyntaxNode node, string text, List<DocumentSymbol> symbols)
    {
        var symbol = CreateSymbol(node, text);
        if (symbol != null)
        {
            // Collect children
            var children = new List<DocumentSymbol>();
            for (int i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetChild(i);
                if (child != null && child is not SyntaxTokenNode)
                    CollectSymbols(child, text, children);
            }
            if (children.Count > 0)
            {
                symbol.Children = children.ToArray();
            }
            symbols.Add(symbol);
        }
        else
        {
            // No symbol for this node, but check children
            for (int i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetChild(i);
                if (child != null && child is not SyntaxTokenNode)
                    CollectSymbols(child, text, symbols);
            }
        }
    }

    private DocumentSymbol? CreateSymbol(SyntaxNode node, string text)
    {
        var (name, kind) = node switch
        {
            ScoreDeclarationSyntax => ("score", SymbolKind.Module),
            PartDeclarationSyntax part => (GetPartName(part), SymbolKind.Class),
            StaffDeclarationSyntax staff => (GetStaffName(staff), SymbolKind.Class),

            VariableDeclarationSyntax variable => (variable.Name.Text, SymbolKind.Variable),
            RelativeExpressionSyntax => ("relative", SymbolKind.Namespace),
            RepeatExpressionSyntax repeat => ($"repeat {repeat.Count.Text}x", SymbolKind.Operator),
            ParallelExpressionSyntax => ("parallel", SymbolKind.Struct),
            TupletExpressionSyntax tuplet => ($"tuplet {tuplet.TupletRatio}/{tuplet.BaseDivision}", SymbolKind.Operator),
            KeySignatureSyntax key => ($"key {key.Pitch.PitchName} {(key.IsMajor ? "major" : "minor")}", SymbolKind.Key),
            ClefDeclarationSyntax clef => ($"clef {clef.ClefName.Text}", SymbolKind.Key),
            LyricsBlockSyntax => ("lyrics", SymbolKind.String),
            _ => (null, SymbolKind.Null)
        };

        if (name == null) return null;

        var (startLine, startCol) = GetLineAndColumn(text, node.Position);
        var (endLine, endCol) = GetLineAndColumn(text, node.Position + node.FullWidth);
        
        return new DocumentSymbol
        {
            Name = name,
            Kind = kind,
            Range = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            },
            SelectionRange = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            }
        };
    }

    private static string GetPartName(PartDeclarationSyntax part)
    {
        // Try to get identifier or string name
        for (int i = 0; i < part.SlotCount; i++)
        {
            var child = part.GetChild(i);
            if (child is SyntaxTokenNode token)
            {
                if (token.Kind == SyntaxKind.Identifier)
                    return token.Text;
                if (token.Kind == SyntaxKind.StringLiteral)
                    return token.Text.Trim('"');
            }
        }
        return "part";
    }

    private static string GetStaffName(StaffDeclarationSyntax staff)
    {
        for (int i = 0; i < staff.SlotCount; i++)
        {
            var child = staff.GetChild(i);
            if (child is SyntaxTokenNode token && token.Kind == SyntaxKind.Identifier)
                return token.Text;
        }
        return "staff";
    }

    // ========== Go to Definition ==========

    [JsonRpcMethod(Methods.TextDocumentDefinitionName)]
    public Location? Definition(TextDocumentPositionParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);
        var node = doc.Tree.FindNode(offset);
        
        if (node == null) return null;

        // Find variable reference
        var varRef = FindAncestor<VariableReferenceSyntax>(node);
        if (varRef != null)
        {
            var name = varRef.Name.Text;
            var definition = FindVariableDefinition(doc.Tree.GetRoot(), name);
            if (definition != null)
            {
                return CreateLocation(uri, doc.Text, definition);
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(SyntaxNode node) where T : SyntaxNode
    {
        var current = node;
        while (current != null)
        {
            if (current is T t)
                return t;
            current = current.Parent;
        }
        return null;
    }

    private static VariableDeclarationSyntax? FindVariableDefinition(SyntaxNode root, string name)
    {
        foreach (var decl in root.DescendantNodes<VariableDeclarationSyntax>())
        {
            if (decl.Name.Text == name)
                return decl;
        }
        return null;
    }

    private Location CreateLocation(Uri uri, string text, SyntaxNode node)
    {
        var (startLine, startCol) = GetLineAndColumn(text, node.Position);
        var (endLine, endCol) = GetLineAndColumn(text, node.Position + node.FullWidth);
        
        return new Location
        {
            Uri = uri,
            Range = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            }
        };
    }
}