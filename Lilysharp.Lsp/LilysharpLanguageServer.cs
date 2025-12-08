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
                    Change = TextDocumentSyncKind.Incremental,
                    Save = new SaveOptions { IncludeText = true }
                },
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = ["\\", "@", " "],
                    ResolveProvider = false
                },
                HoverProvider = true,
                DocumentSymbolProvider = true,
                DefinitionProvider = true,
                ReferencesProvider = true,
                SemanticTokensOptions = new SemanticTokensOptions
                {
                    Full = true,
                    Legend = new SemanticTokensLegend
                    {
                        TokenTypes = new[]
                        {
                            SemanticTokenTypes.Keyword,
                            SemanticTokenTypes.Variable,
                            SemanticTokenTypes.Number,
                            SemanticTokenTypes.String,
                            SemanticTokenTypes.Comment,
                            SemanticTokenTypes.Operator,
                            "pitch",           // Custom: note pitches
                            "articulation",    // Custom: @staccato, @accent
                            "dynamic"          // Custom: \p, \f
                        },
                        TokenModifiers = Array.Empty<string>()
                    }
                }
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

        if (@params.ContentChanges.Length > 0)
        {
            // Check if any change has a Range (incremental) or not (full)
            var hasRange = @params.ContentChanges.Any(c => c.Range != null);
            
            Document doc;
            if (hasRange)
            {
                // Incremental sync
                doc = _documentManager.ApplyChanges(uri, @params.ContentChanges, version);
            }
            else
            {
                // Full sync fallback
                var text = @params.ContentChanges[^1].Text;
                doc = _documentManager.OpenOrUpdate(uri, text, version);
            }
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

    // ========== Find References ==========

    [JsonRpcMethod(Methods.TextDocumentReferencesName)]
    public Location[]? References(ReferenceParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);
        var node = doc.Tree.FindNode(offset);
        
        if (node == null) return null;

        string? name = null;

        // Find variable name from reference or declaration
        var varRef = FindAncestor<VariableReferenceSyntax>(node);
        if (varRef != null)
        {
            name = varRef.Name.Text;
        }
        else
        {
            var varDecl = FindAncestor<VariableDeclarationSyntax>(node);
            if (varDecl != null)
            {
                name = varDecl.Name.Text;
            }
        }

        if (name == null) return null;

        var locations = new List<Location>();
        
        // Include declaration if requested
        if (@params.Context.IncludeDeclaration)
        {
            var decl = FindVariableDefinition(doc.Tree.GetRoot(), name);
            if (decl != null)
            {
                locations.Add(CreateLocation(uri, doc.Text, decl.Name));
            }
        }

        // Find all references
        foreach (var reference in doc.Tree.GetRoot().DescendantNodes<VariableReferenceSyntax>())
        {
            if (reference.Name.Text == name)
            {
                locations.Add(CreateLocation(uri, doc.Text, reference.Name));
            }
        }

        return locations.ToArray();
    }

    // ========== Semantic Tokens ==========

    [JsonRpcMethod(Methods.TextDocumentSemanticTokensFullName)]
    public SemanticTokens? GetSemanticTokensFull(SemanticTokensParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var tokens = new List<int>(); // [deltaLine, deltaStart, length, tokenType, tokenModifiers]
        int prevLine = 0;
        int prevChar = 0;

        foreach (var token in CollectSemanticTokens(doc.Tree.GetRoot(), doc.Text))
        {
            int deltaLine = token.Line - prevLine;
            int deltaChar = deltaLine == 0 ? token.Character - prevChar : token.Character;
            
            tokens.Add(deltaLine);
            tokens.Add(deltaChar);
            tokens.Add(token.Length);
            tokens.Add(token.TokenType);
            tokens.Add(0); // No modifiers
            
            prevLine = token.Line;
            prevChar = token.Character;
        }

        return new SemanticTokens { Data = tokens.ToArray() };
    }

    private record SemanticToken(int Line, int Character, int Length, int TokenType);

    private IEnumerable<SemanticToken> CollectSemanticTokens(SyntaxNode root, string text)
    {
        var tokens = new List<SemanticToken>();
        CollectTokensRecursive(root, text, tokens);
        return tokens.OrderBy(t => t.Line).ThenBy(t => t.Character);
    }

    private void CollectTokensRecursive(SyntaxNode node, string text, List<SemanticToken> tokens)
    {
        // Token types: 0=keyword, 1=variable, 2=number, 3=string, 4=comment, 5=operator, 6=pitch, 7=articulation, 8=dynamic
        
        if (node is SyntaxTokenNode tokenNode)
        {
            var kind = tokenNode.Kind;
            int? tokenType = kind switch
            {
                // Keywords
                SyntaxKind.RelativeKeyword or SyntaxKind.AbsoluteKeyword or SyntaxKind.RepeatKeyword or
                SyntaxKind.AlternativeKeyword or SyntaxKind.LetKeyword or SyntaxKind.UseKeyword or
                SyntaxKind.ScoreKeyword or SyntaxKind.PartKeyword or SyntaxKind.StaffKeyword or
                SyntaxKind.VoiceKeyword or SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword or
                SyntaxKind.TempoKeyword or SyntaxKind.TimeKeyword or SyntaxKind.KeyKeyword or
                SyntaxKind.ClefKeyword or SyntaxKind.TupletKeyword or SyntaxKind.GraceKeyword or
                SyntaxKind.MajorKeyword or SyntaxKind.MinorKeyword or SyntaxKind.LyricsKeyword => 0,
                
                // Numbers
                SyntaxKind.IntegerLiteral => 2,
                
                // Strings
                SyntaxKind.StringLiteral => 3,
                
                // Pitches
                SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or SyntaxKind.PitchF or
                SyntaxKind.PitchG or SyntaxKind.PitchA or SyntaxKind.PitchB => 6,
                
                // Rest
                SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => 6,
                
                // Articulation names
                SyntaxKind.StaccatoKeyword or SyntaxKind.AccentKeyword or SyntaxKind.TenutoKeyword or
                SyntaxKind.MarcatoKeyword or SyntaxKind.FermataKeyword or SyntaxKind.PortatoKeyword => 7,
                
                // Dynamic names
                SyntaxKind.DynamicPPP or SyntaxKind.DynamicPP or SyntaxKind.DynamicP or
                SyntaxKind.DynamicMP or SyntaxKind.DynamicMF or SyntaxKind.DynamicF or
                SyntaxKind.DynamicFF or SyntaxKind.DynamicFFF => 8,
                
                _ => null
            };
            
            if (tokenType.HasValue)
            {
                var (line, character) = GetLineAndCharacter(text, node.Position);
                tokens.Add(new SemanticToken(line, character, node.FullWidth, tokenType.Value));
            }
        }
        else if (node is VariableReferenceSyntax varRef)
        {
            // Variable reference (after $ or use)
            var nameNode = varRef.Name;
            var (line, character) = GetLineAndCharacter(text, nameNode.Position);
            tokens.Add(new SemanticToken(line, character, nameNode.FullWidth, 1));
        }
        else if (node is VariableDeclarationSyntax varDecl)
        {
            // Variable declaration name
            var nameNode = varDecl.Name;
            var (line, character) = GetLineAndCharacter(text, nameNode.Position);
            tokens.Add(new SemanticToken(line, character, nameNode.FullWidth, 1));
        }
        
        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null)
                CollectTokensRecursive(child, text, tokens);
        }
    }

    private static (int line, int character) GetLineAndCharacter(string text, int position)
    {
        int line = 0;
        int lastLineStart = 0;
        
        for (int i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastLineStart = i + 1;
            }
            else if (text[i] == '\r')
            {
                line++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                lastLineStart = i + 1;
            }
        }
        
        return (line, position - lastLineStart);
    }
}