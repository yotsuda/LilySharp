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

using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

/// <summary>
/// LilySharp Language Server implementation.
/// </summary>
public sealed partial class LilySharpLanguageServer
{
    /// <summary>Reported to the client (lilysharp/version). Read from the
    /// assembly's informational version — the build stamps it with the package
    /// version and git SHA — so it can never go stale the way the previous
    /// hand-written constant did (it still said 0.1.1-20260702 in July 3
    /// builds, making every fresh deploy look like yesterday's server).</summary>
    public static readonly string Version =
        System.Attribute.GetCustomAttribute(
            typeof(LilySharpLanguageServer).Assembly,
            typeof(System.Reflection.AssemblyInformationalVersionAttribute))
            is System.Reflection.AssemblyInformationalVersionAttribute info
            ? info.InformationalVersion : "unknown";

    private readonly JsonRpc _rpc;
    private readonly DocumentManager _documentManager = new();

    // Debounced diagnostics: typing bursts cancel the pending validation run
    // so only the settled document gets validated, not every keystroke.
    // didOpen/didSave still publish immediately.
    private const int DiagnosticsDebounceMs = 200;
    private readonly Dictionary<Uri, CancellationTokenSource> _pendingDiagnostics = [];
    private readonly object _diagnosticsGate = new();

    // Editor setting (lilysharp.completion.flatSpelling): when true the completer
    // suggests the contracted Dutch flats es/as instead of ees/aes. Compilation
    // accepts both regardless; this only changes suggestions. Seeded from the
    // client's initializationOptions at start and kept live via
    // DidChangeConfiguration. Default = full (ees/aes).
    private bool _flatSpellingContracted;

    /// <summary>
    /// True iff the pushed settings ask for the contracted Dutch flats (es/as).
    /// The client sends <c>{ completion: { flatSpelling: "contracted" | "full" } }</c>
    /// both as initializationOptions and in didChangeConfiguration, so one parse
    /// serves both entry points. null / missing / anything-but-"contracted" means
    /// the default, full (ees/aes).
    /// </summary>
    internal static bool ParseFlatSpellingContracted(Newtonsoft.Json.Linq.JObject? settings) =>
        settings?["completion"]?["flatSpelling"]?.ToString() == "contracted";

    public LilySharpLanguageServer(Stream input, Stream output)
    {
        var handler = new HeaderDelimitedMessageHandler(output, input);
        _rpc = new JsonRpc(handler);
        _rpc.AddLocalRpcTarget(this, new JsonRpcTargetOptions
        {
            UseSingleObjectParameterDeserialization = true
        });
    }

    public async Task RunAsync()
    {
        _rpc.StartListening();
        await _rpc.Completion;
    }

    [JsonRpcMethod(Methods.InitializeName, UseSingleObjectParameterDeserialization = true)]
    public InitializeResult Initialize(InitializeParams @params)
    {
        // completion.flatSpelling: "contracted" makes the completer suggest es/as
        // in flat keys instead of ees/aes (both always compile). This is the
        // STARTUP value; DidChangeConfiguration keeps it live afterwards.
        _flatSpellingContracted = ParseFlatSpellingContracted(
            @params.InitializationOptions as Newtonsoft.Json.Linq.JObject);

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
                SignatureHelpProvider = new SignatureHelpOptions
                {
                    TriggerCharacters = new[] { " " },
                    RetriggerCharacters = new[] { " " }
                },
                DocumentSymbolProvider = true,
                DefinitionProvider = true,
                ReferencesProvider = true,
                DocumentHighlightProvider = true,
                FoldingRangeProvider = true,
                RenameProvider = true,
                DocumentFormattingProvider = true,
                CodeActionProvider = new CodeActionOptions
                {
                    CodeActionKinds = new[]
                    {
                        CodeActionKind.QuickFix,
                        CodeActionKind.Refactor
                    }
                },
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

    // Applied live when the user changes a lilysharp.* setting — the client pushes
    // the new values here, so e.g. completion.flatSpelling takes effect on the next
    // completion without a window reload. The next Completion() reads the updated
    // field. Settings shape matches initializationOptions: { completion: { … } }.
    [JsonRpcMethod(Methods.WorkspaceDidChangeConfigurationName, UseSingleObjectParameterDeserialization = true)]
    public void DidChangeConfiguration(DidChangeConfigurationParams @params)
    {
        _flatSpellingContracted = ParseFlatSpellingContracted(
            @params.Settings as Newtonsoft.Json.Linq.JObject);
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

    [JsonRpcMethod(Methods.TextDocumentDidOpenName, UseSingleObjectParameterDeserialization = true)]
    public void DidOpen(DidOpenTextDocumentParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var text = @params.TextDocument.Text;
        var version = @params.TextDocument.Version;

        var doc = _documentManager.OpenOrUpdate(uri, text, version);
        PublishDiagnostics(doc);
    }

    [JsonRpcMethod(Methods.TextDocumentDidChangeName, UseSingleObjectParameterDeserialization = true)]
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
            ScheduleDiagnostics(doc);
        }
    }

    [JsonRpcMethod(Methods.TextDocumentDidCloseName, UseSingleObjectParameterDeserialization = true)]
    public void DidClose(DidCloseTextDocumentParams @params)
    {
        CancelPendingDiagnostics(@params.TextDocument.Uri);
        _documentManager.Close(@params.TextDocument.Uri);
        DropSvgSession(@params.TextDocument.Uri);

        // Clear diagnostics — sent BY NAME (see PublishDiagnostics for why NotifyAsync,
        // which sends params positionally, is wrong here).
        _rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName, new PublishDiagnosticParams
        {
            Uri = @params.TextDocument.Uri,
            Diagnostics = []
        });
    }

    [JsonRpcMethod(Methods.TextDocumentDidSaveName, UseSingleObjectParameterDeserialization = true)]
    public void DidSave(DidSaveTextDocumentParams @params)
    {
        if (@params.Text != null)
        {
            // A save carries no version; preserve the current one instead of letting
            // it reset to 0 (which is below any live didChange version and would make
            // a pending debounced diagnostics run mis-decide it is stale).
            var uri = @params.TextDocument.Uri;
            var version = _documentManager.GetDocument(uri)?.Version ?? 0;
            var doc = _documentManager.OpenOrUpdate(uri, @params.Text, version);
            PublishDiagnostics(doc);
        }
    }

    // ========== Diagnostics ==========

    /// <summary>
    /// Schedules a debounced diagnostics run for the document. A newer change
    /// to the same document cancels the pending run; the run also re-checks
    /// that its document is still the latest version before publishing.
    /// </summary>
    private void ScheduleDiagnostics(Document doc)
    {
        CancellationToken token;
        lock (_diagnosticsGate)
        {
            if (_pendingDiagnostics.TryGetValue(doc.Uri, out var old))
            {
                old.Cancel();
                old.Dispose();
            }
            var cts = new CancellationTokenSource();
            _pendingDiagnostics[doc.Uri] = cts;
            token = cts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DiagnosticsDebounceMs, token);

                // Drop if the document moved on (or was closed) while we slept.
                var current = _documentManager.GetDocument(doc.Uri);
                if (current == null || current.Version != doc.Version)
                    return;

                PublishDiagnostics(doc);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer change — nothing to do.
            }
        }, CancellationToken.None);
    }

    private void CancelPendingDiagnostics(Uri uri)
    {
        lock (_diagnosticsGate)
        {
            if (_pendingDiagnostics.Remove(uri, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }

    private void PublishDiagnostics(Document doc)
    {
        var diagnostics = new List<Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic>();

        // Parser diagnostics
        foreach (var d in doc.Tree.Diagnostics)
        {
            diagnostics.Add(ConvertDiagnostic(d, doc.Text));
        }

        // Semantic diagnostics — every validator via the shared registry (same set
        // the CLI's `check` runs, so the two can never drift). Defensive: a validator
        // that throws on a broken tree must NOT blank the Problems panel — the parser
        // diagnostics (which usually explain the breakage) still publish.
        try
        {
            foreach (var d in LilySharp.Core.Semantics.SemanticValidation.Run(doc.Tree))
            {
                diagnostics.Add(ConvertDiagnostic(d, doc.Text));
            }
        }
        catch
        {
            // Swallow: keep the syntax diagnostics collected above. A validator crash is a
            // Lily# bug, not something the author can act on, and must not take down the LSP.
        }

        // publishDiagnostics params must be sent BY NAME (a single object). NotifyAsync
        // sends a single argument POSITIONALLY (params: [obj]); the client then rejects it
        // ("defines parameters by name but received parameters by position") and drops the
        // diagnostics. NotifyWithParameterObjectAsync sends the object as params directly.
        _rpc.NotifyWithParameterObjectAsync(Methods.TextDocumentPublishDiagnosticsName, new PublishDiagnosticParams
        {
            Uri = doc.Uri,
            Diagnostics = [.. diagnostics]
        });
    }

    private static Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic ConvertDiagnostic(
        LilySharp.Core.Syntax.Diagnostic d, string text)
    {
        var (start, end) = TrimSpanToInk(text, d.Span.Start, d.Span.Start + d.Span.Length);
        var (startLine, startCol) = GetLineAndColumn(text, start);
        var (endLine, endCol) = GetLineAndColumn(text, end);

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
            Source = "Lily#",
            Message = d.Message
        };
    }

    // Canonical offset → (line, character) conversion. Delegates to
    // GetLineAndCharacter so every feature agrees on line breaks — the old
    // body counted only '\n', so on a lone-'\r' (classic-Mac) file diagnostics/
    // hover/go-to-def reported different lines than rename/highlight.
    private static (int line, int col) GetLineAndColumn(string text, int position)
        => GetLineAndCharacter(text, position);

    // Shrinks a diagnostic span to its INK — the range without leading/trailing
    // whitespace. A composite node's Span (GreenSyntaxNode does not compute its
    // own leading/trailing trivia) reaches to the FULL span, so the whitespace
    // before its first token would push the squiggle left of the code (and the
    // whitespace/newline after its last token would drag it right, even onto the
    // next line). A token-derived span has no interior whitespace at its ends, so
    // this is a no-op for those. An all-whitespace span (defensive — a real
    // diagnostic points at code) is left untouched rather than collapsed.
    internal static (int start, int end) TrimSpanToInk(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        int s = start, e = end;
        while (s < e && char.IsWhiteSpace(text[s])) s++;
        while (e > s && char.IsWhiteSpace(text[e - 1])) e--;
        return s < e ? (s, e) : (start, end);
    }

}
