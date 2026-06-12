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

using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;
using LspDiagnosticSeverity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

/// <summary>
/// LilySharp Language Server implementation.
/// </summary>
public sealed class LilySharpLanguageServer
{
    // Version: increment this when making changes to verify deployment
    public const string Version = "0.1.1-20260612-2339";

    private readonly JsonRpc _rpc;
    private readonly DocumentManager _documentManager = new();

    // Debounced diagnostics: typing bursts cancel the pending validation run
    // so only the settled document gets validated, not every keystroke.
    // didOpen/didSave still publish immediately.
    private const int DiagnosticsDebounceMs = 200;
    private readonly Dictionary<Uri, CancellationTokenSource> _pendingDiagnostics = [];
    private readonly object _diagnosticsGate = new();

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

        // Clear diagnostics
        _rpc.NotifyAsync(Methods.TextDocumentPublishDiagnosticsName, new PublishDiagnosticParams
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
            var doc = _documentManager.OpenOrUpdate(@params.TextDocument.Uri, @params.Text);
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

        // Semantic diagnostics (measure validation)
        var validator = new MeasureValidator();
        validator.Validate(doc.Tree);
        foreach (var d in validator.Diagnostics)
        {
            diagnostics.Add(ConvertDiagnostic(d, doc.Text));
        }

        // Symbol reference validation (undefined variables, phrases, sections)
        var symbolValidator = new SymbolReferenceValidator();
        symbolValidator.Validate(doc.Tree);
        foreach (var d in symbolValidator.Diagnostics)
        {
            diagnostics.Add(ConvertDiagnostic(d, doc.Text));
        }

        // Duration validation (invalid note values like 5, 3, 6)
        var durationValidator = new DurationValidator();
        durationValidator.Validate(doc.Tree);
        foreach (var d in durationValidator.Diagnostics)
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
        LilySharp.Core.Syntax.Diagnostic d, string text)
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
            Source = "Lily#",
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

    [JsonRpcMethod(Methods.TextDocumentCompletionName, UseSingleObjectParameterDeserialization = true)]
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
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertText = "score {\n\t$0\n}", Detail = "Score block" },
                new CompletionItem { Label = "part", Kind = CompletionItemKind.Keyword, InsertText = "part $1 {\n\t$0\n}", Detail = "Part declaration" },
                new CompletionItem { Label = "section", Kind = CompletionItemKind.Keyword, InsertText = "section $1 {\n\t$0\n}", Detail = "Section declaration" },
                new CompletionItem { Label = "phrase", Kind = CompletionItemKind.Keyword, InsertText = "phrase $1 {\n\t$0\n}", Detail = "Reusable phrase" },
                new CompletionItem { Label = "structure", Kind = CompletionItemKind.Keyword, InsertText = "structure { $0 }", Detail = "Playback order" },
                new CompletionItem { Label = "render", Kind = CompletionItemKind.Keyword, InsertText = "render score {\n\t$0\n}", Detail = "Output layout" },
                new CompletionItem { Label = "title", Kind = CompletionItemKind.Keyword, InsertText = "title \"$0\"", Detail = "Title metadata" },
                new CompletionItem { Label = "composer", Kind = CompletionItemKind.Keyword, InsertText = "composer \"$0\"", Detail = "Composer metadata" },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertText = "tempo $0", Detail = "Tempo (BPM)" },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertText = "time $0", Detail = "Time signature" },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertText = "key $0", Detail = "Key signature" },
                new CompletionItem { Label = "clef", Kind = CompletionItemKind.Keyword, InsertText = "clef $0", Detail = "Clef (treble/bass/alto/tenor)" },
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertText = "override $1.$2 = $0", Detail = "Override grob property" },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertText = "revert $1.$0", Detail = "Revert grob property" },
                new CompletionItem { Label = "once", Kind = CompletionItemKind.Keyword, InsertText = "once override $1.$2 = $0", Detail = "One-time override" }
            ]
        };
    }

    private static CompletionList GetMusicCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                // Pitches
                new CompletionItem { Label = "c", Kind = CompletionItemKind.Value, Detail = "C pitch", SortText = "0c" },
                new CompletionItem { Label = "d", Kind = CompletionItemKind.Value, Detail = "D pitch", SortText = "0d" },
                new CompletionItem { Label = "e", Kind = CompletionItemKind.Value, Detail = "E pitch", SortText = "0e" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "F pitch", SortText = "0f" },
                new CompletionItem { Label = "g", Kind = CompletionItemKind.Value, Detail = "G pitch", SortText = "0g" },
                new CompletionItem { Label = "a", Kind = CompletionItemKind.Value, Detail = "A pitch", SortText = "0a" },
                new CompletionItem { Label = "b", Kind = CompletionItemKind.Value, Detail = "B pitch", SortText = "0b" },

                // Rests
                new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest", SortText = "1r" },
                new CompletionItem { Label = "s", Kind = CompletionItemKind.Value, Detail = "Spacer rest (invisible)", SortText = "1s" },
                new CompletionItem { Label = "R", Kind = CompletionItemKind.Value, Detail = "Full-measure rest", SortText = "1R" },

                // Structures
                new CompletionItem { Label = "|: :|", Kind = CompletionItemKind.Snippet, InsertText = "|: $0 :|", Detail = "Volta repeat (symbolic; add endings [1. …] [2. …])", SortText = "2repeat" },
                new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertText = "repeat unfold 2 {\n\t$0\n}", Detail = "Repeat block (unfold/percent/tremolo)", SortText = "2repeatkw" },
                new CompletionItem { Label = "tuplet", Kind = CompletionItemKind.Keyword, InsertText = "tuplet 3/2 { $0 }", Detail = "Tuplet (e.g., triplet)", SortText = "2tuplet" },
                new CompletionItem { Label = "grace", Kind = CompletionItemKind.Keyword, InsertText = "grace { $0 }", Detail = "Grace notes", SortText = "2grace" },
                new CompletionItem { Label = "acciaccatura", Kind = CompletionItemKind.Keyword, InsertText = "acciaccatura { $0 }", Detail = "Slashed grace note", SortText = "2acciaccatura" },
                new CompletionItem { Label = "appoggiatura", Kind = CompletionItemKind.Keyword, InsertText = "appoggiatura { $0 }", Detail = "Unslashed grace note", SortText = "2appoggiatura" },

                // Mid-measure declarations
                new CompletionItem { Label = "clef", Kind = CompletionItemKind.Keyword, InsertText = "clef $0", Detail = "Change clef", SortText = "3clef" },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertText = "key $0", Detail = "Change key signature", SortText = "3key" },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertText = "time $0", Detail = "Change time signature", SortText = "3time" },

                // Grob overrides
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertText = "override $1.$2 = $0", Detail = "Override grob property", SortText = "4override" },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertText = "revert $1.$0", Detail = "Revert grob property", SortText = "4revert" },
                new CompletionItem { Label = "once", Kind = CompletionItemKind.Keyword, InsertText = "once override $1.$2 = $0", Detail = "One-time override", SortText = "4once" }
            ]
        };
    }

    private static CompletionList GetArticulationCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                // Articulations
                new CompletionItem { Label = "staccato", Kind = CompletionItemKind.Value, Detail = "Staccato articulation", SortText = "0staccato" },
                new CompletionItem { Label = "accent", Kind = CompletionItemKind.Value, Detail = "Accent", SortText = "0accent" },
                new CompletionItem { Label = "tenuto", Kind = CompletionItemKind.Value, Detail = "Tenuto", SortText = "0tenuto" },
                new CompletionItem { Label = "marcato", Kind = CompletionItemKind.Value, Detail = "Marcato", SortText = "0marcato" },
                new CompletionItem { Label = "fermata", Kind = CompletionItemKind.Value, Detail = "Fermata", SortText = "0fermata" },
                new CompletionItem { Label = "portato", Kind = CompletionItemKind.Value, Detail = "Portato (tenuto + staccato)", SortText = "0portato" },

                // Ornaments
                new CompletionItem { Label = "trill", Kind = CompletionItemKind.Value, Detail = "Trill ornament", SortText = "1trill" },
                new CompletionItem { Label = "mordent", Kind = CompletionItemKind.Value, Detail = "Mordent ornament", SortText = "1mordent" },
                new CompletionItem { Label = "prall", Kind = CompletionItemKind.Value, Detail = "Inverted mordent (pralltriller)", SortText = "1prall" },
                new CompletionItem { Label = "turn", Kind = CompletionItemKind.Value, Detail = "Turn ornament", SortText = "1turn" },
                new CompletionItem { Label = "invertedturn", Kind = CompletionItemKind.Value, Detail = "Inverted turn", SortText = "1invertedturn" },

                // Dynamics (@ prefix style)
                new CompletionItem { Label = "p", Kind = CompletionItemKind.Value, Detail = "Piano (soft)", SortText = "2p" },
                new CompletionItem { Label = "f", Kind = CompletionItemKind.Value, Detail = "Forte (loud)", SortText = "2f" },
                new CompletionItem { Label = "pp", Kind = CompletionItemKind.Value, Detail = "Pianissimo", SortText = "2pp" },
                new CompletionItem { Label = "ff", Kind = CompletionItemKind.Value, Detail = "Fortissimo", SortText = "2ff" },
                new CompletionItem { Label = "mp", Kind = CompletionItemKind.Value, Detail = "Mezzo-piano", SortText = "2mp" },
                new CompletionItem { Label = "mf", Kind = CompletionItemKind.Value, Detail = "Mezzo-forte", SortText = "2mf" },
                new CompletionItem { Label = "cresc", Kind = CompletionItemKind.Value, Detail = "Crescendo hairpin", SortText = "2cresc" },
                new CompletionItem { Label = "decresc", Kind = CompletionItemKind.Value, Detail = "Decrescendo hairpin", SortText = "2decresc" },

                // Music navigation marks
                new CompletionItem { Label = "segno", Kind = CompletionItemKind.Value, Detail = "Segno sign", SortText = "3segno" },
                new CompletionItem { Label = "coda", Kind = CompletionItemKind.Value, Detail = "Coda sign", SortText = "3coda" },
                new CompletionItem { Label = "fine", Kind = CompletionItemKind.Value, Detail = "Fine (end)", SortText = "3fine" },
                new CompletionItem { Label = "dc", Kind = CompletionItemKind.Value, Detail = "Da Capo", SortText = "3dc" },
                new CompletionItem { Label = "ds.al.fine", Kind = CompletionItemKind.Value, Detail = "Dal Segno al Fine", SortText = "3ds.al.fine" },
                new CompletionItem { Label = "ds.al.coda", Kind = CompletionItemKind.Value, Detail = "Dal Segno al Coda", SortText = "3ds.al.coda" },
                new CompletionItem { Label = "mark.A", Kind = CompletionItemKind.Value, Detail = "Rehearsal mark A", SortText = "3mark" },

                // Spanners and brackets
                new CompletionItem { Label = "rit", Kind = CompletionItemKind.Value, Detail = "Ritardando text spanner", SortText = "4rit" },
                new CompletionItem { Label = "accel", Kind = CompletionItemKind.Value, Detail = "Accelerando text spanner", SortText = "4accel" },
                new CompletionItem { Label = "ottava", Kind = CompletionItemKind.Value, Detail = "Ottava (8va) bracket", SortText = "4ottava" },
                new CompletionItem { Label = "loco", Kind = CompletionItemKind.Value, Detail = "End ottava bracket", SortText = "4loco" },
                new CompletionItem { Label = "startTrillSpan", Kind = CompletionItemKind.Value, Detail = "Start trill spanner", SortText = "4startTrillSpan" },
                new CompletionItem { Label = "stopTrillSpan", Kind = CompletionItemKind.Value, Detail = "Stop trill spanner", SortText = "4stopTrillSpan" },

                // Pedal markings
                new CompletionItem { Label = "ped", Kind = CompletionItemKind.Value, Detail = "Sustain pedal on", SortText = "5ped" },
                new CompletionItem { Label = "ped.off", Kind = CompletionItemKind.Value, Detail = "Sustain pedal off", SortText = "5ped.off" },
                new CompletionItem { Label = "sost.ped", Kind = CompletionItemKind.Value, Detail = "Sostenuto pedal on", SortText = "5sost.ped" },
                new CompletionItem { Label = "sostenuto", Kind = CompletionItemKind.Value, Detail = "Sostenuto pedal off", SortText = "5sostenuto" },
                new CompletionItem { Label = "una.corda", Kind = CompletionItemKind.Value, Detail = "Una corda pedal on", SortText = "5una.corda" },
                new CompletionItem { Label = "tre.corde", Kind = CompletionItemKind.Value, Detail = "Una corda pedal off", SortText = "5tre.corde" },

                // Notation marks
                new CompletionItem { Label = "gliss", Kind = CompletionItemKind.Value, Detail = "Glissando to next note", SortText = "6gliss" },
                new CompletionItem { Label = "arpeggio", Kind = CompletionItemKind.Value, Detail = "Arpeggiate chord", SortText = "6arpeggio" },
                new CompletionItem { Label = "courtesy", Kind = CompletionItemKind.Value, Detail = "Force courtesy accidental", SortText = "6courtesy" },

                // Figured bass
                new CompletionItem { Label = "fig.6", Kind = CompletionItemKind.Value, Detail = "Figured bass: 6", SortText = "7fig" },
                new CompletionItem { Label = "fig.6.4", Kind = CompletionItemKind.Value, Detail = "Figured bass: 6/4", SortText = "7fig" },
                new CompletionItem { Label = "fig.5.3", Kind = CompletionItemKind.Value, Detail = "Figured bass: 5/3", SortText = "7fig" },

                // Chord names
                new CompletionItem { Label = "chord.C", Kind = CompletionItemKind.Value, Detail = "Chord name: C major", SortText = "8chord" },
                new CompletionItem { Label = "chord.Am", Kind = CompletionItemKind.Value, Detail = "Chord name: A minor", SortText = "8chord" }
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

    [JsonRpcMethod(Methods.TextDocumentHoverName, UseSingleObjectParameterDeserialization = true)]
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
            SlurSyntax slur => slur.IsOpen ? "**Slur start**: `(`" : "**Slur end**: `)`",
            RepeatExpressionSyntax => "**Repeat**: Repeats the enclosed music",
            ParallelExpressionSyntax => "**Parallel**: Multiple voices played simultaneously",
            TimeSignatureSyntax ts => $"**Time Signature**: {ts.Beats}/{ts.BeatType}",
            TempoDeclarationSyntax tempo => $"**Tempo**: {tempo.Marking ?? ""} {(tempo.BeatUnit != null ? $"{tempo.BeatUnit} = " : "")}{tempo.Bpm ?? 120} BPM".Trim(),
            KeySignatureSyntax key => $"**Key Signature**: {key.Pitch?.PitchName} {(key.IsMajor ? "major" : "minor")}",
            ClefDeclarationSyntax clef => $"**Clef**: {clef.ClefName.Text}",
            GraceExpressionSyntax grace => $"**Grace notes**: {(grace.IsAcciaccatura ? "Acciaccatura (slashed)" : grace.IsAppoggiatura ? "Appoggiatura" : "Grace")}",
            TupletExpressionSyntax tuplet => $"**Tuplet**: {tuplet.TupletRatio} in the time of {tuplet.BaseDivision}",
            OverrideDeclarationSyntax ovr => $"**Override**: `{ovr.GrobName.Text}.{ovr.PropertyName.Text}` = `{ovr.ValueToken.Text}`",
            RevertDeclarationSyntax rev => $"**Revert**: `{rev.GrobName.Text}.{rev.PropertyName.Text}`",
            OnceModifierSyntax => "**Once**: Applies override/revert for one note only",
            PhraseDeclarationSyntax phrase => $"**Phrase**: `{phrase.Name.Text}` — Reusable music block",
            SectionDeclarationSyntax section => $"**Section**: `{section.SectionName}` — Groups parts for a musical section",
            StructureDeclarationSyntax => "**Structure**: Defines playback order of sections",
            RenderDeclarationSyntax => "**Render**: Controls output layout (staff assignment)",
            VariableDeclarationSyntax varDecl => $"**Variable**: `{varDecl.Name.Text}`",
            VariableReferenceSyntax varRef => $"**Variable reference**: `${varRef.Name.Text}`",
            LyricsBlockSyntax => "**Lyrics**: Text aligned to notes",
            ArticulationSyntax art => $"**Articulation**: @{art.NameToken.Text}",
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

    [JsonRpcMethod(Methods.TextDocumentDocumentSymbolName, UseSingleObjectParameterDeserialization = true)]
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
            PhraseDeclarationSyntax phrase => ($"phrase {phrase.Name.Text}", SymbolKind.Function),
            SectionDeclarationSyntax section => ($"section {section.SectionName}", SymbolKind.Namespace),
            StructureDeclarationSyntax => ("structure", SymbolKind.Struct),
            RenderDeclarationSyntax => ("render", SymbolKind.Module),
            RepeatExpressionSyntax repeat => ($"repeat {repeat.Count.Text}x", SymbolKind.Operator),
            ParallelExpressionSyntax => ("parallel", SymbolKind.Struct),
            TupletExpressionSyntax tuplet => ($"tuplet {tuplet.TupletRatio}/{tuplet.BaseDivision}", SymbolKind.Operator),
            KeySignatureSyntax key => ($"key {key.Pitch.PitchName} {(key.IsMajor ? "major" : "minor")}", SymbolKind.Key),
            ClefDeclarationSyntax clef => ($"clef {clef.ClefName.Text}", SymbolKind.Key),
            LyricsBlockSyntax => ("lyrics", SymbolKind.String),
            OverrideDeclarationSyntax ovr => ($"override {ovr.GrobName.Text}.{ovr.PropertyName.Text}", SymbolKind.Property),
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

    [JsonRpcMethod(Methods.TextDocumentDefinitionName, UseSingleObjectParameterDeserialization = true)]
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

    [JsonRpcMethod(Methods.TextDocumentReferencesName, UseSingleObjectParameterDeserialization = true)]
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

    [JsonRpcMethod(Methods.TextDocumentSemanticTokensFullName, UseSingleObjectParameterDeserialization = true)]
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
                SyntaxKind.RepeatKeyword or
                SyntaxKind.AlternativeKeyword or SyntaxKind.LetKeyword or SyntaxKind.UseKeyword or
                SyntaxKind.ScoreKeyword or SyntaxKind.PartKeyword or SyntaxKind.StaffKeyword or
                SyntaxKind.VoiceKeyword or SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword or
                SyntaxKind.TempoKeyword or SyntaxKind.TimeKeyword or SyntaxKind.KeyKeyword or
                SyntaxKind.ClefKeyword or SyntaxKind.TupletKeyword or SyntaxKind.GraceKeyword or
                SyntaxKind.MajorKeyword or SyntaxKind.MinorKeyword or SyntaxKind.LyricsKeyword or
                SyntaxKind.OverrideKeyword or SyntaxKind.RevertKeyword or SyntaxKind.OnceKeyword or
                SyntaxKind.PhraseKeyword or SyntaxKind.SectionKeyword or SyntaxKind.StructureKeyword or
                SyntaxKind.RenderKeyword => 0,

                // Numbers
                SyntaxKind.IntegerLiteral => 2,

                // Strings
                SyntaxKind.StringLiteral => 3,

                // Pitches
                SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or SyntaxKind.PitchF or
                SyntaxKind.PitchG or SyntaxKind.PitchA or SyntaxKind.PitchB => 6,

                // Rest
                SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => 6,

                // Articulation/ornament names are now '@name' identifiers resolved by
                // ArticulationRegistry, not distinct keyword tokens — no special case here.

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

    // ========== Folding Ranges ==========

    [JsonRpcMethod(Methods.TextDocumentFoldingRangeName, UseSingleObjectParameterDeserialization = true)]
    public FoldingRange[]? GetFoldingRanges(FoldingRangeParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var ranges = new List<FoldingRange>();
        CollectFoldingRanges(doc.Tree.GetRoot(), doc.Text, ranges);
        return ranges.ToArray();
    }

    private void CollectFoldingRanges(SyntaxNode node, string text, List<FoldingRange> ranges)
    {
        // Foldable node types: MusicBlock, ScoreDeclaration, PartDeclaration, etc.
        bool isFoldable = node is MusicBlockSyntax or ScoreDeclarationSyntax or
                          PartDeclarationSyntax or StaffDeclarationSyntax or
                          RepeatExpressionSyntax or ParallelExpressionSyntax or
                          TupletExpressionSyntax or GraceExpressionSyntax or
                          LyricsBlockSyntax or AlternativeClauseSyntax or
                          SectionDeclarationSyntax or PhraseDeclarationSyntax or
                          StructureDeclarationSyntax or RenderDeclarationSyntax;

        if (isFoldable && node.FullWidth > 0)
        {
            var startPos = node.Position;
            var endPos = node.Position + node.FullWidth - 1;

            var (startLine, _) = GetLineAndCharacter(text, startPos);
            var (endLine, endChar) = GetLineAndCharacter(text, endPos);

            // Only create fold if it spans multiple lines
            if (endLine > startLine)
            {
                ranges.Add(new FoldingRange
                {
                    StartLine = startLine,
                    EndLine = endLine,
                    Kind = FoldingRangeKind.Region
                });
            }
        }

        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null)
                CollectFoldingRanges(child, text, ranges);
        }
    }

    // ========== Rename ==========

    [JsonRpcMethod(Methods.TextDocumentRenameName, UseSingleObjectParameterDeserialization = true)]
    public WorkspaceEdit? Rename(RenameParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var newName = @params.NewName;

        // Find the node at position
        int offset = GetOffset(doc.Text, position.Line, position.Character);
        var node = doc.Tree.FindNode(offset);
        if (node == null) return null;

        // Find variable name at position
        string? variableName = null;

        if (node is VariableReferenceSyntax varRef)
        {
            variableName = varRef.Name.Text;
        }
        else if (node is VariableDeclarationSyntax varDecl)
        {
            variableName = varDecl.Name.Text;
        }
        else if (node.Parent is VariableReferenceSyntax parentRef)
        {
            variableName = parentRef.Name.Text;
        }
        else if (node.Parent is VariableDeclarationSyntax parentDecl)
        {
            variableName = parentDecl.Name.Text;
        }

        if (variableName == null) return null;

        // Find all references and the declaration
        var edits = new List<TextEdit>();

        // Find declaration
        foreach (var decl in doc.Tree.GetNodes<VariableDeclarationSyntax>())
        {
            if (decl.Name.Text == variableName)
            {
                var (line, character) = GetLineAndCharacter(doc.Text, decl.Name.Position);
                edits.Add(new TextEdit
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + decl.Name.FullWidth }
                    },
                    NewText = newName
                });
            }
        }

        // Find all references
        foreach (var reference in doc.Tree.GetNodes<VariableReferenceSyntax>())
        {
            if (reference.Name.Text == variableName)
            {
                var (line, character) = GetLineAndCharacter(doc.Text, reference.Name.Position);
                edits.Add(new TextEdit
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + reference.Name.FullWidth }
                    },
                    NewText = newName
                });
            }
        }

        if (edits.Count == 0) return null;

        return new WorkspaceEdit
        {
            Changes = new Dictionary<string, TextEdit[]>
            {
                [uri.ToString()] = edits.ToArray()
            }
        };
    }

    // ========== Document Formatting ==========

    [JsonRpcMethod(Methods.TextDocumentFormattingName, UseSingleObjectParameterDeserialization = true)]
    public TextEdit[]? Format(DocumentFormattingParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var options = @params.Options;
        var tabSize = options.TabSize;
        var insertSpaces = options.InsertSpaces;
        var indentStr = insertSpaces ? new string(' ', tabSize) : "\t";

        var formatted = FormatSource(doc.Text, indentStr);

        // Return a single edit replacing the entire document
        var lines = doc.Text.Split('\n');
        var lastLine = lines.Length - 1;
        var lastChar = lines[lastLine].TrimEnd('\r').Length;

        return new[]
        {
            new TextEdit
            {
                Range = new LspRange
                {
                    Start = new Position { Line = 0, Character = 0 },
                    End = new Position { Line = lastLine, Character = lastChar }
                },
                NewText = formatted
            }
        };
    }

    private static string FormatSource(string source, string indentStr)
    {
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        var lines = source.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r').Trim();

            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine();
                continue;
            }

            // Adjust depth for closing braces at start of line
            if (line.StartsWith('}') || line.StartsWith(">>"))
            {
                depth = Math.Max(0, depth - 1);
            }

            // Write indented line
            var indent = string.Concat(Enumerable.Repeat(indentStr, depth));
            sb.AppendLine($"{indent}{line}");

            // Adjust depth for opening braces at end of line
            if (line.EndsWith('{') || line.EndsWith("<<"))
            {
                depth++;
            }
            // Handle inline close (e.g., "} else {")
            else if (line.Contains('{') && !line.Contains('}'))
            {
                depth++;
            }
            else if (line.Contains('}') && !line.Contains('{'))
            {
                // Already handled above
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ========== Code Actions ==========

    [JsonRpcMethod(Methods.TextDocumentCodeActionName, UseSingleObjectParameterDeserialization = true)]
    public CodeAction[]? GetCodeActions(CodeActionParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var actions = new List<CodeAction>();
        var range = @params.Range;

        // Get diagnostics in range
        var startOffset = GetOffset(doc.Text, range.Start.Line, range.Start.Character);
        var endOffset = GetOffset(doc.Text, range.End.Line, range.End.Character);

        foreach (var diagnostic in doc.Tree.Diagnostics)
        {
            if (diagnostic.Span.Start >= startOffset && diagnostic.Span.Start <= endOffset)
            {
                // Generate quick fixes based on diagnostic
                var fixes = GenerateQuickFixes(doc, diagnostic, uri);
                actions.AddRange(fixes);
            }
        }

        // Add refactoring actions for valid selections
        var node = doc.Tree.FindNode(startOffset);
        if (node != null)
        {
            var refactorings = GenerateRefactorings(doc, node, uri);
            actions.AddRange(refactorings);
        }

        return actions.ToArray();
    }

    private IEnumerable<CodeAction> GenerateQuickFixes(Document doc, CoreDiagnostic diagnostic, Uri uri)
    {
        var actions = new List<CodeAction>();
        var message = diagnostic.Message;

        // Fix: Unknown pitch - suggest valid pitches
        if (message.Contains("Unknown") || message.Contains("Expected"))
        {
            // Suggest inserting a rest if there's a parsing error
            var (line, character) = GetLineAndCharacter(doc.Text, diagnostic.Span.Start);
            actions.Add(new CodeAction
            {
                Title = "Insert rest (r4)",
                Kind = CodeActionKind.QuickFix,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<string, TextEdit[]>
                    {
                        [uri.ToString()] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange
                                {
                                    Start = new Position { Line = line, Character = character },
                                    End = new Position { Line = line, Character = character + diagnostic.Span.Length }
                                },
                                NewText = "r4"
                            }
                        }
                    }
                }
            });
        }

        // Fix: Unclosed brace
        if (message.Contains("Expected '}'") || message.Contains("unclosed"))
        {
            var lines = doc.Text.Split('\n');
            var lastLine = lines.Length - 1;
            var lastChar = lines[lastLine].TrimEnd('\r').Length;

            actions.Add(new CodeAction
            {
                Title = "Add closing brace",
                Kind = CodeActionKind.QuickFix,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<string, TextEdit[]>
                    {
                        [uri.ToString()] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange
                                {
                                    Start = new Position { Line = lastLine, Character = lastChar },
                                    End = new Position { Line = lastLine, Character = lastChar }
                                },
                                NewText = "\n}"
                            }
                        }
                    }
                }
            });
        }

        return actions;
    }

    private IEnumerable<CodeAction> GenerateRefactorings(Document doc, SyntaxNode node, Uri uri)
    {
        var actions = new List<CodeAction>();

        // Refactor: Extract variable from music block
        if (node is MusicBlockSyntax block && block.Items.Any())
        {
            var blockText = block.ToFullString().Trim();
            var (line, character) = GetLineAndCharacter(doc.Text, block.Position);
            var (endLine, endChar) = GetLineAndCharacter(doc.Text, block.Position + block.FullWidth);

            actions.Add(new CodeAction
            {
                Title = "Extract to variable",
                Kind = CodeActionKind.Refactor,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<string, TextEdit[]>
                    {
                        [uri.ToString()] = new[]
                        {
                            // Insert variable declaration at start
                            new TextEdit
                            {
                                Range = new LspRange
                                {
                                    Start = new Position { Line = 0, Character = 0 },
                                    End = new Position { Line = 0, Character = 0 }
                                },
                                NewText = $"let melody = {blockText}\n\n"
                            },
                            // Replace block with variable reference
                            new TextEdit
                            {
                                Range = new LspRange
                                {
                                    Start = new Position { Line = line, Character = character },
                                    End = new Position { Line = endLine, Character = endChar }
                                },
                                NewText = "$melody"
                            }
                        }
                    }
                }
            });
        }

        // Refactor: Wrap in relative
        if (node is NoteSyntax note)
        {
            var noteText = note.ToFullString().Trim();
            var (line, character) = GetLineAndCharacter(doc.Text, note.Position);
            var (endLine, endChar) = GetLineAndCharacter(doc.Text, note.Position + note.FullWidth);

            actions.Add(new CodeAction
            {
                Title = "Wrap in relative block",
                Kind = CodeActionKind.Refactor,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<string, TextEdit[]>
                    {
                        [uri.ToString()] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange
                                {
                                    Start = new Position { Line = line, Character = character },
                                    End = new Position { Line = endLine, Character = endChar }
                                },
                                NewText = $"relative c' {{ {noteText} }}"
                            }
                        }
                    }
                }
            });
        }

        return actions;
    }

    // ========== Signature Help ==========

    [JsonRpcMethod(Methods.TextDocumentSignatureHelpName, UseSingleObjectParameterDeserialization = true)]
    public SignatureHelp? GetSignatureHelp(SignatureHelpParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);

        // Look backwards for a keyword
        var lineStart = doc.Text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        var lineText = doc.Text[lineStart..offset];

        // Check for keywords that have signatures
        var signatures = new List<SignatureInformation>();
        int activeParameter = 0;

        if (lineText.Contains("relative"))
        {
            var paramIndex = lineText.IndexOf("relative") + "relative".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "relative pitch { music }",
                Documentation = "Sets relative pitch mode. Notes are interpreted relative to the previous note.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "pitch", Documentation = "Base pitch with optional octave marks (e.g., c', c'')" },
                    new ParameterInformation { Label = "{ music }", Documentation = "Music block containing notes" }
                }
            });
        }
        else if (lineText.Contains("repeat"))
        {
            var paramIndex = lineText.IndexOf("repeat") + "repeat".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "repeat (unfold|percent|tremolo) count { music }",
                Documentation = "Repeats the music block. For volta repeats use the symbolic form "
                    + "'|: … :|' (count '|: … :|*N') with inline endings '[1. …] [2. …]'.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "unfold|percent|tremolo", Documentation = "Repeat kind (volta is the symbolic |: :| form, not this keyword)" },
                    new ParameterInformation { Label = "count", Documentation = "Number of repetitions (integer)" },
                    new ParameterInformation { Label = "{ music }", Documentation = "Music block to repeat" }
                }
            });
        }
        else if (lineText.Contains("tempo"))
        {
            var paramIndex = lineText.IndexOf("tempo") + "tempo".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "tempo \"marking\" duration = bpm",
                Documentation = "Sets the tempo for playback.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "\"marking\"", Documentation = "Optional tempo marking (e.g., \"Allegro\")" },
                    new ParameterInformation { Label = "duration", Documentation = "Note duration (e.g., 4 for quarter note)" },
                    new ParameterInformation { Label = "bpm", Documentation = "Beats per minute" }
                }
            });
        }
        else if (lineText.Contains("time"))
        {
            var paramIndex = lineText.IndexOf("time") + "time".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "time numerator/denominator",
                Documentation = "Sets the time signature.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "numerator/denominator", Documentation = "Time signature (e.g., 4/4, 3/4, 6/8)" }
                }
            });
        }
        else if (lineText.Contains("key"))
        {
            var paramIndex = lineText.IndexOf("key") + "key".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "key pitch major|minor",
                Documentation = "Sets the key signature.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "pitch", Documentation = "Key pitch (e.g., c, g, fis, bes)" },
                    new ParameterInformation { Label = "major|minor", Documentation = "Mode: major or minor" }
                }
            });
        }
        else if (lineText.Contains("tuplet"))
        {
            var paramIndex = lineText.IndexOf("tuplet") + "tuplet".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "tuplet ratio { music }",
                Documentation = "Creates a tuplet (e.g., triplet).",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "ratio", Documentation = "Ratio (e.g., 3/2 for triplet)" },
                    new ParameterInformation { Label = "{ music }", Documentation = "Notes in the tuplet" }
                }
            });
        }
        else if (lineText.Contains("let"))
        {
            var paramIndex = lineText.IndexOf("let") + "let".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "let name = expression",
                Documentation = "Declares a variable.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "name", Documentation = "Variable name (identifier)" },
                    new ParameterInformation { Label = "expression", Documentation = "Value to assign" }
                }
            });
        }
        else if (lineText.Contains("override"))
        {
            var paramIndex = lineText.IndexOf("override") + "override".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "override Grob.property = value",
                Documentation = "Overrides a grob (graphical object) property.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "Grob.property", Documentation = "Grob name and property (e.g., Stem.length, NoteHead.color)" },
                    new ParameterInformation { Label = "value", Documentation = "New value (number, string, or identifier)" }
                }
            });
        }
        else if (lineText.Contains("phrase"))
        {
            var paramIndex = lineText.IndexOf("phrase") + "phrase".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "phrase name { music }",
                Documentation = "Declares a reusable musical phrase. Reference with $name.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "name", Documentation = "Phrase name (identifier)" },
                    new ParameterInformation { Label = "{ music }", Documentation = "Music content" }
                }
            });
        }
        else if (lineText.Contains("section"))
        {
            var paramIndex = lineText.IndexOf("section") + "section".Length;
            activeParameter = CountSpaces(lineText[paramIndex..]);

            signatures.Add(new SignatureInformation
            {
                Label = "section Name { parts... }",
                Documentation = "Declares a section grouping multiple parts.",
                Parameters = new[]
                {
                    new ParameterInformation { Label = "Name", Documentation = "Section name (identifier)" },
                    new ParameterInformation { Label = "{ parts... }", Documentation = "Part blocks with music" }
                }
            });
        }

        if (signatures.Count == 0)
            return null;

        return new SignatureHelp
        {
            Signatures = signatures.ToArray(),
            ActiveSignature = 0,
            ActiveParameter = Math.Min(activeParameter, signatures[0].Parameters?.Length - 1 ?? 0)
        };
    }

    private static int CountSpaces(string text)
    {
        int count = 0;
        foreach (var c in text)
        {
            if (c == ' ' && count < 10) count++;
        }
        return Math.Max(0, count - 1);
    }

    // ========== Document Highlight ==========

    [JsonRpcMethod(Methods.TextDocumentDocumentHighlightName, UseSingleObjectParameterDeserialization = true)]
    public DocumentHighlight[]? GetDocumentHighlight(DocumentHighlightParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);
        var node = doc.Tree.FindNode(offset);
        if (node == null) return null;

        // Find variable name at position
        string? variableName = null;

        if (node is VariableReferenceSyntax varRef)
        {
            variableName = varRef.Name.Text;
        }
        else if (node is VariableDeclarationSyntax varDecl)
        {
            variableName = varDecl.Name.Text;
        }
        else if (node.Parent is VariableReferenceSyntax parentRef)
        {
            variableName = parentRef.Name.Text;
        }
        else if (node.Parent is VariableDeclarationSyntax parentDecl)
        {
            variableName = parentDecl.Name.Text;
        }

        if (variableName == null) return null;

        var highlights = new List<DocumentHighlight>();

        // Highlight declaration (Write)
        foreach (var decl in doc.Tree.GetNodes<VariableDeclarationSyntax>())
        {
            if (decl.Name.Text == variableName)
            {
                var (line, character) = GetLineAndCharacter(doc.Text, decl.Name.Position);
                highlights.Add(new DocumentHighlight
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + decl.Name.FullWidth }
                    },
                    Kind = DocumentHighlightKind.Write
                });
            }
        }

        // Highlight references (Read)
        foreach (var reference in doc.Tree.GetNodes<VariableReferenceSyntax>())
        {
            if (reference.Name.Text == variableName)
            {
                var (line, character) = GetLineAndCharacter(doc.Text, reference.Name.Position);
                highlights.Add(new DocumentHighlight
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + reference.Name.FullWidth }
                    },
                    Kind = DocumentHighlightKind.Read
                });
            }
        }

        return highlights.ToArray();
    }

    // ============================================================
    // Custom: SVG Preview
    // ============================================================

    /// <summary>
    /// Returns the server version for debugging deployment issues.
    /// </summary>
    [JsonRpcMethod("lilysharp/version")]
    public string GetVersion()
    {
        return Version;
    }

    /// <summary>
    /// Custom request to generate SVG from a document.
    /// Used for real-time preview in VS Code.
    /// </summary>
    [JsonRpcMethod("lilysharp/svg", UseSingleObjectParameterDeserialization = true)]
    public SvgResponse GetSvg(SvgParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
        {
            return new SvgResponse
            {
                Svg = null,
                Error = "Document not found"
            };
        }

        // Extract render definitions
        var renders = ExtractRenderInfo(doc.Tree);

        if (doc.Tree.HasErrors)
        {
            var errors = string.Join("\n", doc.Tree.Diagnostics
                .Where(d => d.Severity == CoreDiagnosticSeverity.Error)
                .Select(d => {
                    var (line, col) = GetLineAndColumn(doc.Tree.Text, d.Span.Start);
                    return $"Line {line}, Col {col}: {d.Message}";
                }));
            return new SvgResponse
            {
                Svg = null,
                Error = errors,
                Renders = renders
            };
        }

        try
        {
            // Preview mode: @font-face is defined in HTML, not in SVG
            var renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Preview();

            // Generate SVG using shared generator (same code path as CLI)
            var svg = LilySharp.Core.Svg.SvgGenerator.Generate(doc.Tree, renderOptions, @params.RenderName);

            return new SvgResponse
            {
                Svg = svg,
                Error = null,
                Renders = renders
            };
        }
        catch (Exception ex)
        {
            return new SvgResponse
            {
                Svg = null,
                Error = ex.Message,
                Renders = renders
            };
        }
    }

    /// <summary>
    /// Extract render definitions from the syntax tree.
    /// </summary>
    private RenderInfo[] ExtractRenderInfo(SyntaxTree tree)
    {
        var renders = new List<RenderInfo>();
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is RenderDeclarationSyntax render)
            {
                // Get children: render [type] "filename" { ... }
                string type = "score";
                string filename = "";

                // Iterate through children using GetChild
                for (int i = 0; ; i++)
                {
                    var child = render.GetChild(i);
                    if (child == null) break;

                    if (child is SyntaxTokenNode token)
                    {
                        var text = token.Text;
                        if (text == "score" || text == "audio" || text == "midi")
                        {
                            type = text;
                        }
                        else if (text.StartsWith("\"") && text.EndsWith("\""))
                        {
                            filename = text.Trim('"');
                        }
                    }
                }

                // Use full filename as the name (to distinguish fur-elise.svg from fur-elise.mid)
                var name = filename;
                if (string.IsNullOrEmpty(name))
                {
                    name = $"render_{renders.Count + 1}";
                }

                renders.Add(new RenderInfo
                {
                    Name = name,
                    Type = type,
                    Filename = filename
                });
            }
        }
        return renders.ToArray();
    }

    /// <summary>
    /// Get the voice name from a render declaration.
    /// </summary>
    private string? GetVoiceNameFromRender(SyntaxTree tree, string renderName)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is RenderDeclarationSyntax render)
            {
                string filename = "";

                // Find filename
                for (int i = 0; ; i++)
                {
                    var child = render.GetChild(i);
                    if (child == null) break;

                    if (child is SyntaxTokenNode token)
                    {
                        var text = token.Text;
                        if (text.StartsWith("\"") && text.EndsWith("\""))
                        {
                            filename = text.Trim('"');
                            break;
                        }
                    }
                }

                var name = Path.GetFileNameWithoutExtension(filename);
                if (name == renderName)
                {
                    // Find the first staff voice name
                    foreach (var item in render.DescendantNodes())
                    {
                        if (item is StaffRenderSyntax staff)
                        {
                            // Get voice name from staff { voiceName }
                            for (int i = 0; ; i++)
                            {
                                var staffChild = staff.GetChild(i);
                                if (staffChild == null) break;

                                if (staffChild is SyntaxTokenNode t &&
                                    t.Kind != SyntaxKind.StaffKeyword &&
                                    t.Kind != SyntaxKind.OpenBrace &&
                                    t.Kind != SyntaxKind.CloseBrace &&
                                    !IsClefKeyword(t.Kind))
                                {
                                    return t.Text;
                                }
                            }
                        }
                    }
                }
            }
        }
        return null;
    }

    private bool IsClefKeyword(SyntaxKind kind)
    {
        return kind is SyntaxKind.TrebleKeyword or SyntaxKind.BassKeyword
            or SyntaxKind.AltoKeyword or SyntaxKind.TenorKeyword;
    }
}

/// <summary>
/// Parameters for lilysharp/svg request.
/// </summary>
public class SvgParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
    /// <summary>
    /// Optional render name to select which render block to use.
    /// If null, returns the first score render or default preview.
    /// </summary>
    public string? RenderName { get; set; }
}

/// <summary>
/// Response for lilysharp/svg request.
/// </summary>
public class SvgResponse
{
    public string? Svg { get; set; }
    public string? Error { get; set; }
    /// <summary>
    /// List of available render definitions in the document.
    /// </summary>
    public RenderInfo[]? Renders { get; set; }
}

/// <summary>
/// Information about a render definition.
/// </summary>
public class RenderInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";  // "score" or "audio"
    public string Filename { get; set; } = "";
}




























































