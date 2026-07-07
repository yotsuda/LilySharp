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
using System.Text.RegularExpressions;
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
public sealed class LilySharpLanguageServer
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
        // the CLI's `check` runs, so the two can never drift).
        foreach (var d in LilySharp.Core.Semantics.SemanticValidation.Run(doc.Tree))
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

        // The word being typed at the cursor (chord letters/digits, e.g. "cmaj7"
        // or, after a ':', the quality being completed).
        int wordStart = offset;
        while (wordStart > 0 && IsChordWordChar(doc.Text[wordStart - 1]))
            wordStart--;
        string word = doc.Text.Substring(wordStart, offset - wordStart);

        // Inside a nameless chords { } block, right after a chord's ':', complete the
        // quality tokens (m, m7, maj7, sus4, …).
        if (wordStart > 0 && doc.Text[wordStart - 1] == ':' && IsInsideChordNamesBlock(doc.Text, offset))
            return GetChordQualityCompletions();

        return context switch
        {
            CompletionContext.TopLevel => GetTopLevelCompletions(),
            // A percussion part's music block offers the drum-kit vocabulary,
            // not pitch letters (LILYPOND-REF: \drummode note names).
            CompletionContext.MusicBlock => IsInsidePercussionPartMusic(doc.Text, offset, out bool inVoice)
                ? GetDrumCompletions(inVoice)
                : GetMusicCompletions(word, CurrentKeySharps(doc.Text, offset)),
            CompletionContext.StructureBlock => GetStructureCompletions(doc.Text),
            CompletionContext.PartBlock => GetPartPropertyCompletions(),
            CompletionContext.AfterClef => GetClefCompletions(),
            CompletionContext.AfterKey => GetKeyTonicCompletions(),
            CompletionContext.AfterKeyTonic => GetKeyModeCompletions(),
            CompletionContext.AfterOctave => GetOctaveCompletions(),
            CompletionContext.AfterTempo => GetTempoCompletions(),
            CompletionContext.AfterTime => GetTimeCompletions(),
            CompletionContext.AfterPartial => GetPartialCompletions(),
            CompletionContext.AfterTitleText => GetTitleTextCompletions(WordBeforeCursor(doc.Text, offset)),
            CompletionContext.ScoreBlock => GetScoreBlockCompletions(),
            CompletionContext.AfterStaffRef => GetDeclaredNameCompletions(doc.Text, "part", "Part"),
            CompletionContext.AfterChordsRef => GetDeclaredNameCompletions(doc.Text, "chords", "Chord part"),
            CompletionContext.AfterLyricsRef => GetDeclaredNameCompletions(doc.Text, "lyrics", "Lyrics part"),
            CompletionContext.AfterWith => GetWithCompletions(),
            CompletionContext.AfterInstrument => GetInstrumentCompletions(doc.Text, offset, position),
            CompletionContext.AfterRemoveEmpty => GetRemoveEmptyCompletions(),
            CompletionContext.AfterAt => GetArticulationCompletions(),
            CompletionContext.AfterBackslash => GetDynamicCompletions(),
            _ => null
        };
    }

    private static bool IsChordWordChar(char c) => char.IsLetterOrDigit(c);

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a nameless <c>chords { … }</c>
    /// block — found by tracking the keyword before each open brace and seeing
    /// whether the innermost still-open block is <c>chords</c>.
    /// </summary>
    private static bool IsInsideChordNamesBlock(string text, int offset)
        => InnermostOpenBlock(text, offset) == "chords";

    /// <summary>
    /// The keyword introducing the innermost still-open <c>keyword { … }</c> block
    /// at <paramref name="offset"/> (e.g. "structure", "chords", "section"),
    /// or null at the top level. Used to scope completions to the block kind.
    /// </summary>
    internal static string? InnermostOpenBlock(string text, int offset)
    {
        var stack = new System.Collections.Generic.Stack<string>();
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                int j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
                int end = j + 1;
                while (j >= 0 && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j--;
                stack.Push(text.Substring(j + 1, end - (j + 1)));
            }
            else if (text[i] == '}' && stack.Count > 0)
            {
                stack.Pop();
            }
        }
        return stack.Count > 0 ? stack.Peek() : null;
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>part &lt;name&gt; { … }</c>
    /// body. <see cref="InnermostOpenBlock"/> cannot answer this: the word before a
    /// part's <c>{</c> is the part NAME, so the introducing <c>part</c> keyword is one
    /// word further back — which is also what tells a declaration (<c>part rh {</c>)
    /// apart from a section-body part reference (<c>rh {</c>).
    /// </summary>
    internal static bool IsInsidePartBlock(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        var stack = new System.Collections.Generic.Stack<bool>();
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                int j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
                while (j >= 0 && IsWordChar(text[j])) j--;      // the block's name
                while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
                int end = j + 1;
                while (j >= 0 && IsWordChar(text[j])) j--;      // the introducing keyword
                stack.Push(text.Substring(j + 1, end - (j + 1)) == "part");
            }
            else if (text[i] == '}' && stack.Count > 0)
            {
                stack.Pop();
            }
        }
        return stack.Count > 0 && stack.Peek();
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>"…"</c> string literal.
    /// Strings never span lines, so an odd number of quotes between the line start
    /// and the cursor means the cursor is inside one.
    /// </summary>
    internal static bool IsInsideStringLiteral(string text, int offset)
    {
        int quotes = 0;
        for (int i = offset - 1; i >= 0 && text[i] != '\n'; i--)
            if (text[i] == '"')
                quotes++;
        return (quotes & 1) == 1;
    }

    /// <summary>Quality-token completions offered after a chord's ':' inside a chords block.</summary>
    private static CompletionList GetChordQualityCompletions()
    {
        var items = ChordQualityRegistry.Tokens
            .OrderBy(t => t)
            .Select(t =>
            {
                ChordQualityRegistry.TryResolve(t, out var q);
                return new CompletionItem
                {
                    Label = t,
                    Kind = CompletionItemKind.EnumMember,
                    Detail = new ChordStructure(0, 0, q).DisplayName.Length == 0
                        ? "major triad"
                        : "C" + ChordQualityRegistry.GetSuffix(q),
                };
            })
            .ToArray();
        return new CompletionList { Items = items };
    }

    /// <summary>
    /// Completions for a <c>structure { … }</c> block: everything a structure body
    /// can hold — the document's section names, the navigation marks (segno / coda /
    /// to coda / D.C. / D.S. …), repeat barlines (<c>|:</c> <c>:|</c>), volta
    /// brackets (<c>[1. …]</c>), the silent-section prefix (<c>~</c>) and custom
    /// text (<c>_"…"</c>). Deliberately offers NO note names — the structure is a
    /// playback order of sections, not music.
    /// </summary>
    internal static CompletionList GetStructureCompletions(string text)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();

        // Section names declared anywhere in the document (in declaration order,
        // deduplicated) — these are what a structure plays.
        var sections = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in Regex.Matches(text, @"\bsection\s+(\w+)"))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name))
                sections.Add(name);
        }
        // Plain reference, then the silent form (~Name = render, no rehearsal
        // label) — one per section, so the ~ prefix is never offered on its own.
        foreach (var name in sections)
            items.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Reference,
                Detail = "Section",
            });
        foreach (var name in sections)
            items.Add(new CompletionItem
            {
                Label = "~" + name,
                InsertText = "~" + name,
                Kind = CompletionItemKind.Reference,
                Detail = "Silent section (renders, no rehearsal label)",
            });

        // Navigation marks placed between sections.
        var navs = new (string Label, string Detail)[]
        {
            ("segno", "Segno (jump target)"),
            ("coda", "Coda (jump target)"),
            ("to coda", "Jump to the coda"),
            ("fine", "End here"),
            ("dc", "Da Capo — repeat from the top"),
            ("ds", "Dal Segno — repeat from the segno"),
            ("dc al fine", "Da Capo al Fine"),
            ("dc al coda", "Da Capo al Coda"),
            ("ds al fine", "Dal Segno al Fine"),
            ("ds al coda", "Dal Segno al Coda"),
        };
        foreach (var (label, detail) in navs)
            items.Add(new CompletionItem
            {
                Label = label,
                Kind = CompletionItemKind.Keyword,
                Detail = detail,
            });

        // Repeat barlines, volta brackets, the silent-section prefix and custom
        // text — the remaining things a structure body can hold.
        items.Add(new CompletionItem
        {
            Label = "|:", InsertText = "|:", Kind = CompletionItemKind.Operator,
            Detail = "Repeat start",
        });
        items.Add(new CompletionItem
        {
            Label = ":|", InsertText = ":|", Kind = CompletionItemKind.Operator,
            Detail = "Repeat end (suffix x3 for a count)",
        });

        CompletionItem Snippet(string label, string insert, string detail) => new()
        {
            Label = label,
            InsertText = insert,
            InsertTextFormat = InsertTextFormat.Snippet,
            Kind = CompletionItemKind.Snippet,
            Detail = detail,
        };
        items.Add(Snippet("[1. ]", "[1. $0]", "1st ending (volta bracket)"));
        items.Add(Snippet("[2. ]", "[2. $0]", "2nd ending (volta bracket)"));
        items.Add(Snippet("[1-2. ]", "[${1:1-2}. $0]", "Multi-pass ending, e.g. [1-2. …] or [1,3. …]"));
        items.Add(Snippet("_\"\"", "_\"$0\"", "Custom text annotation"));

        return new CompletionList { Items = items.ToArray() };
    }

    internal enum CompletionContext
    {
        Unknown,
        TopLevel,
        MusicBlock,
        StructureBlock,
        PartBlock,
        AfterClef,
        AfterKey,
        AfterKeyTonic,
        AfterOctave,
        AfterTempo,
        AfterTime,
        AfterPartial,
        AfterTitleText,
        ScoreBlock,
        AfterStaffRef,
        AfterChordsRef,
        AfterLyricsRef,
        AfterWith,
        AfterInstrument,
        AfterRemoveEmpty,
        AfterAt,
        AfterBackslash
    }

    /// <summary>
    /// The whole word immediately before the partial word being typed at
    /// <paramref name="offset"/> (skipping the current word and any whitespace),
    /// e.g. "clef" in <c>clef tr|</c> or <c>clef |</c>. Empty if none. Hyphen counts
    /// as a word character: instrument presets are hyphenated (piano-right,
    /// 5-string-bass), and the scan must not truncate at the hyphen — in
    /// <c>instrument piano-|</c> the partial word is "piano-", and the preceding
    /// word must still come out as "instrument".
    /// </summary>
    internal static string WordBeforeCursor(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = offset;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // skip the partial word
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--; // skip whitespace
        int end = i;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the preceding word
        return text.Substring(i, end - i);
    }

    internal static CompletionContext GetCompletionContext(string text, int offset)
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
        }

        // Right after the `clef` keyword (in a header or mid-music), only the clef
        // names are valid — offer those alone, not notes/keywords.
        var prevWord = WordBeforeCursor(text, offset);
        if (prevWord == "clef")
            return CompletionContext.AfterClef;

        // `key |` → tonic pitches; `key a |` → only the modes are valid
        // (major/minor/dorian/…), not lyrics/tempo/every keyword.
        if (prevWord == "key")
            return CompletionContext.AfterKey;

        // `octave |` → only its two modes (a bare number re-anchor is typed,
        // not completed).
        if (prevWord == "octave")
            return CompletionContext.AfterOctave;

        // Value positions after the metadata/meter keywords: only their own
        // value forms fit there, not the keyword list. Guarded against string
        // interiors so a title like "tempo di valse" is not hijacked.
        if (!IsInsideStringLiteral(text, offset))
        {
            switch (prevWord)
            {
                case "tempo": return CompletionContext.AfterTempo;
                case "time": return CompletionContext.AfterTime;
                case "partial": return CompletionContext.AfterPartial;
                case "title" or "composer": return CompletionContext.AfterTitleText;
            }
        }
        if (IsPitchName(prevWord) && SecondWordBeforeCursor(text, offset) == "key")
            return CompletionContext.AfterKeyTonic;

        // Right after the `instrument` part property only the known instrument presets
        // are valid — offer those alone (they set clef/octave/tuning defaults). Unlike
        // the music-jargon "clef", "instrument" is an ordinary English word, so the
        // keyword alone is not enough context: it must sit where the part property is
        // actually valid — inside a part { } body and not inside a string — or lyrics
        // like `play my instrument` and titles would have their completion hijacked.
        if (prevWord == "instrument"
            && IsInsidePartBlock(text, offset)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterInstrument;

        // Right after the `removeEmpty` part property only its values are valid
        // (true / all / false — LP RemoveEmptyStaves / RemoveAllEmptyStaves).
        if (string.Equals(prevWord, "removeEmpty", StringComparison.OrdinalIgnoreCase)
            && IsInsidePartBlock(text, offset)
            && !IsInsideStringLiteral(text, offset))
            return CompletionContext.AfterRemoveEmpty;

        // Directly inside a part { } header (and not after one of the
        // value-taking keywords above): offer the part property names — a part
        // body holds properties (and inner sections), never notes.
        if (IsInsidePartBlock(text, offset) && !IsInsideStringLiteral(text, offset))
            return CompletionContext.PartBlock;

        // Inside structure { … } the body is a playback order (section names and
        // navigation marks), not music — so it gets its own completions, never
        // note names. Checked before the brace fallback so 'structure {|' counts.
        if (InnermostOpenBlock(text, offset) == "structure")
            return CompletionContext.StructureBlock;

        // Inside score "name" { } / grandStaff { }: the body is a render spec.
        // After its reference keywords only the declared part names fit; a
        // `with` continues into `with chords PART`.
        if (IsInsideScoreBlock(text, offset))
        {
            return prevWord switch
            {
                "staff" => CompletionContext.AfterStaffRef,
                "chords" => CompletionContext.AfterChordsRef,
                "lyrics" => CompletionContext.AfterLyricsRef,
                "with" => CompletionContext.AfterWith,
                _ => CompletionContext.ScoreBlock,
            };
        }

        if (i >= 0 && text[i] == '{')
            return CompletionContext.MusicBlock;

        // Check if inside braces
        int braceDepth = 0;
        for (int j = 0; j < offset; j++)
        {
            if (text[j] == '{') braceDepth++;
            else if (text[j] == '}') braceDepth--;
        }

        return braceDepth > 0 ? CompletionContext.MusicBlock : CompletionContext.TopLevel;
    }

    /// <summary>The property names a part { } header accepts (bare `name value`
    /// pairs plus inner sections), matching docs/GRAMMAR.md PartProperty.</summary>
    internal static CompletionList GetPartPropertyCompletions()
    {
        var props = new (string Label, string Detail)[]
        {
            ("clef", "Clef (treble/bass/alto/tenor/treble_8)"),
            ("instrument", "Instrument preset (clef/octave/tuning defaults)"),
            ("name", "Display name (system indent label)"),
            ("tuning", "Tab tuning (guitar/bass/ukulele/…)"),
            ("channel", "MIDI channel"),
            ("octave", "Octave mode or absolute base (absolute | relative | N)"),
            ("transpose", "Transpose target pitch"),
            ("removeEmpty", "Hara-kiri: hide this staff in rest-only systems (true | all)"),
            ("time", "Part-local time signature"),
            ("tempo", "Part-local tempo"),
            ("section", "Inner section (part-major form)"),
        };
        return new CompletionList
        {
            Items = props.Select((p, i) => new CompletionItem
            {
                Label = p.Label,
                Kind = CompletionItemKind.Property,
                Detail = p.Detail,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The values valid right after the <c>removeEmpty</c> part
    /// property. LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves
    /// (keeps the first system) / RemoveAllEmptyStaves.</summary>
    internal static CompletionList GetRemoveEmptyCompletions()
    {
        var values = new (string Label, string Detail)[]
        {
            ("true", "Hide in rest-only systems; the FIRST system keeps the staff (LP RemoveEmptyStaves)"),
            ("all", "Hide in rest-only systems including the first (LP RemoveAllEmptyStaves)"),
            ("false", "Never hide (default)"),
        };
        return new CompletionList
        {
            Items = values.Select((v, i) => new CompletionItem
            {
                Label = v.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = v.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The clef names valid right after the <c>clef</c> keyword, ordered
    /// from the highest-sounding clef to the lowest (not alphabetically).</summary>
    /// <summary>True for a bare pitch-name word (c…b with optional is/es
    /// accidental suffixes) — the tonic between `key` and its mode.</summary>
    internal static bool IsPitchName(string word)
        => System.Text.RegularExpressions.Regex.IsMatch(word, "^[a-g](is|es|isis|eses)?$");

    /// <summary>The word before <see cref="WordBeforeCursor"/> — "key" in
    /// <c>key a m|</c>, where the partial word is "m" and the previous is "a".</summary>
    internal static string SecondWordBeforeCursor(string text, int offset)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';
        int i = offset;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // skip the partial word
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the previous word
        while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
        int end = i;
        while (i > 0 && IsWordChar(text[i - 1])) i--;        // the word before it
        return text.Substring(i, end - i);
    }

    /// <summary>
    /// True when <paramref name="offset"/> sits inside a <c>score … { }</c> or
    /// <c>grandStaff { }</c> body. A score block usually carries a name
    /// (<c>score "sheet" {</c> / <c>score practice {</c>) between the keyword
    /// and the brace, so the innermost-block scan must skip one quoted or bare
    /// name before reading the keyword.
    /// </summary>
    internal static bool IsInsideScoreBlock(string text, int offset)
    {
        var stack = new System.Collections.Generic.Stack<bool>();
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                stack.Push(IsScoreBlockOpener(text, i));
            }
            else if (text[i] == '}' && stack.Count > 0)
            {
                stack.Pop();
            }
        }
        return stack.Count > 0 && stack.Peek();
    }

    private static bool IsScoreBlockOpener(string text, int braceIndex)
    {
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        int j = braceIndex - 1;
        while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
        // Read the word (or quoted name) directly before the brace.
        string w1;
        if (j >= 0 && text[j] == '"')
        {
            j--;
            while (j >= 0 && text[j] != '"') j--;
            j--;
            w1 = ""; // a quoted score name — the keyword sits before it
        }
        else
        {
            int end = j + 1;
            while (j >= 0 && IsWordChar(text[j])) j--;
            w1 = text.Substring(j + 1, end - (j + 1));
        }
        if (w1 == "score" || w1 == "grandStaff")
            return true;
        // Known block keywords that take NO name between keyword and brace —
        // anything else is a name (score x { / section A { …), so look one
        // word further back.
        if (w1 is "section" or "part" or "phrase" or "structure" or "chords"
            or "lyrics" or "voice" or "tuplet" or "grace" or "acciaccatura"
            or "appoggiatura" or "repeat" or "ossia" or "tab" or "")
        {
            if (w1 != "")
                return false;
        }
        while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
        int e2 = j + 1;
        while (j >= 0 && IsWordChar(text[j])) j--;
        string w0 = text.Substring(j + 1, e2 - (j + 1));
        return w0 == "score";
    }

    /// <summary>After <c>title</c> / <c>composer</c>: one snippet that drops a
    /// quote pair and parks the caret inside — the text itself is typed.</summary>
    internal static CompletionList GetTitleTextCompletions(string keyword)
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "\"\"",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "\"$0\"",
                    Detail = keyword == "composer" ? "Quoted composer name" : "Quoted title text",
                },
            ]
        };
    }

    /// <summary>The written tempo forms, as fill-in snippets — after <c>tempo</c>
    /// nothing else fits (a bare BPM, a marking text, a beat-unit equation, or
    /// a swing feel).</summary>
    internal static CompletionList GetTempoCompletions()
    {
        var forms = new (string Label, string Insert, string Detail)[]
        {
            ("120", "${1:120}", "Metronome mark: ♩ = 120"),
            ("\"Allegro\" 132", "\"${1:Allegro}\" ${2:132}", "Marking text + BPM: Allegro (♩ = 132)"),
            ("\"Grave\" 4 = 54", "\"${1:Grave}\" ${2:4} = ${3:54}", "Marking + beat unit = BPM (4. = dotted unit)"),
            ("120 swing", "${1:120} swing", "Swing feel (eighths; 'swing 16' for sixteenths)"),
        };
        return new CompletionList
        {
            Items = forms.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Snippet,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = t.Insert,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>Common meters offered after <c>time</c>.</summary>
    internal static CompletionList GetTimeCompletions()
    {
        var meters = new (string Label, string Detail)[]
        {
            ("4/4", "Common time (engraved as C)"),
            ("3/4", "Waltz / minuet"),
            ("2/4", "March / polka"),
            ("2/2", "Cut time (engraved as ¢)"),
            ("6/8", "Compound duple (jig)"),
            ("9/8", "Compound triple (slip jig)"),
            ("12/8", "Compound quadruple (shuffle)"),
            ("3/8", "Fast triple"),
            ("5/4", "Quintuple"),
            ("7/8", "Septuple"),
        };
        return new CompletionList
        {
            Items = meters.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = t.Detail,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>Common pickup lengths offered after <c>partial</c> (the note-
    /// duration grammar: number + optional dots).</summary>
    internal static CompletionList GetPartialCompletions()
    {
        var durations = new (string Label, string Detail)[]
        {
            ("4", "Quarter-note pickup"),
            ("8", "Eighth-note pickup"),
            ("2", "Half-note pickup"),
            ("4.", "Dotted-quarter pickup"),
            ("2.", "Dotted-half pickup (three quarters)"),
            ("8.", "Dotted-eighth pickup"),
        };
        return new CompletionList
        {
            Items = durations.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The render-spec keywords valid inside a score / grandStaff body.</summary>
    internal static CompletionList GetScoreBlockCompletions()
    {
        var specs = new (string Label, string Insert, string Detail)[]
        {
            ("staff", "staff $0", "A staff rendering the named part"),
            ("grandStaff", "grandStaff {\n\t$0\n}", "Braced staff group (piano)"),
            ("chords", "chords $0", "Chord row (no staff) for the named chord part"),
            ("lyrics", "lyrics $0", "Lyrics row (no staff) for the named lyrics part"),
            ("structure", "structure { $0 }", "Per-score playback order override"),
        };
        return new CompletionList
        {
            Items = specs.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.Keyword,
                InsertTextFormat = InsertTextFormat.Snippet,
                InsertText = t.Insert,
                Detail = t.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>After <c>staff NAME with</c> the only continuation is <c>chords</c>.</summary>
    internal static CompletionList GetWithCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem
                {
                    Label = "chords",
                    Kind = CompletionItemKind.Keyword,
                    Detail = "Attach a chord part's symbols above this staff",
                },
            ]
        };
    }

    /// <summary>Names declared as <c>KEYWORD name {</c> anywhere in the document
    /// (parts, chord parts, lyrics parts), offered where a score references them.</summary>
    internal static CompletionList GetDeclaredNameCompletions(string text, string keyword, string detail)
    {
        var names = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Match m in Regex.Matches(text, $@"\b{keyword}\s+(\w+)\s*\{{"))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name))
                names.Add(name);
        }
        return new CompletionList
        {
            Items = names.Select((n, i) => new CompletionItem
            {
                Label = n,
                Kind = CompletionItemKind.Reference,
                Detail = detail,
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The octave-mode words valid right after <c>octave</c>. A bare
    /// integer (<c>octave 3</c>, the part-header base re-anchor) is also legal
    /// there but is typed, not completed.</summary>
    internal static CompletionList GetOctaveCompletions()
    {
        var modes = new (string Label, string Detail)[]
        {
            ("absolute", "Absolute octaves: bare c = C4; ' / , are absolute offsets per note"),
            ("relative", "Relative octaves (default): each note nearest the previous"),
        };
        return new CompletionList
        {
            Items = modes.Select((m, i) => new CompletionItem
            {
                Label = m.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = m.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>Tonic pitches offered right after <c>key</c>, in circle-of-fifths
    /// order (sharps up, then flats down) so related keys sit together.</summary>
    internal static CompletionList GetKeyTonicCompletions()
    {
        var tonics = new (string Label, string Detail)[]
        {
            ("c", "0 ♯/♭ (major)"), ("g", "1 ♯"), ("d", "2 ♯"), ("a", "3 ♯"),
            ("e", "4 ♯"), ("b", "5 ♯"), ("fis", "6 ♯"), ("cis", "7 ♯"),
            ("f", "1 ♭"), ("bes", "2 ♭"), ("ees", "3 ♭"), ("aes", "4 ♭"),
            ("des", "5 ♭"), ("ges", "6 ♭"), ("ces", "7 ♭"),
        };
        return new CompletionList
        {
            Items = tonics.Select((t, i) => new CompletionItem
            {
                Label = t.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = $"Tonic — {t.Detail} signature",
                SortText = i.ToString("D2"),
            }).ToArray()
        };
    }

    /// <summary>The modes valid after <c>key TONIC</c> — nothing else fits there.</summary>
    internal static CompletionList GetKeyModeCompletions()
    {
        var modes = new (string Label, string Detail)[]
        {
            ("major", "Major (ionian)"),
            ("minor", "Natural minor (aeolian): major − 3 sharps"),
            ("ionian", "Ionian (= major)"),
            ("dorian", "Dorian: major − 2 sharps"),
            ("phrygian", "Phrygian: major − 4 sharps"),
            ("lydian", "Lydian: major + 1 sharp"),
            ("mixolydian", "Mixolydian: major − 1 sharp"),
            ("aeolian", "Aeolian (= minor)"),
            ("locrian", "Locrian: major − 5 sharps"),
        };
        return new CompletionList
        {
            Items = modes.Select((m, i) => new CompletionItem
            {
                Label = m.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = m.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    internal static CompletionList GetClefCompletions()
    {
        // High → low pitch range.
        var clefs = new (string Label, string Detail)[]
        {
            ("treble", "Treble (G) clef"),
            ("treble_8", "Treble clef sounding an octave lower (guitar/tenor)"),
            ("alto", "Alto (C) clef"),
            ("tenor", "Tenor (C) clef"),
            ("bass", "Bass (F) clef"),
        };
        return new CompletionList
        {
            // SortText keeps the high→low order (VS Code otherwise sorts by label).
            Items = clefs.Select((c, i) => new CompletionItem
            {
                Label = c.Label,
                Kind = CompletionItemKind.EnumMember,
                Detail = c.Detail,
                SortText = i.ToString(),
            }).ToArray()
        };
    }

    /// <summary>The instrument-preset names valid right after the <c>instrument</c>
    /// part property (they set clef/octave/tuning defaults). Sourced from
    /// <see cref="InstrumentDefaults.KnownInstruments"/> so the list never drifts from
    /// what the compiler recognizes. When the request context is supplied, each item
    /// carries a TextEdit replacing the whole hyphenated token being typed: the
    /// client's default word range stops at '-', so accepting "piano-right" after
    /// typing "piano-" would otherwise leave the prefix in place
    /// ("piano-piano-right"); the explicit range also makes the client filter
    /// against the full hyphenated prefix.</summary>
    internal static CompletionList GetInstrumentCompletions(
        string? text = null, int offset = 0, Position? position = null)
    {
        LspRange? replaceRange = null;
        if (text != null && position != null)
        {
            int start = offset;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1])
                                 || text[start - 1] == '_' || text[start - 1] == '-'))
                start--;
            replaceRange = new LspRange
            {
                Start = new Position(position.Line, position.Character - (offset - start)),
                End = position,
            };
        }

        return new CompletionList
        {
            // SortText (zero-padded) preserves the family grouping — VS Code otherwise
            // sorts by label, which would scatter e.g. "double-bass" among the woodwinds.
            Items = InstrumentDefaults.KnownInstruments.Select((name, i) => new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.EnumMember,
                Detail = "Instrument preset (clef/octave defaults)",
                SortText = i.ToString("D2"),
                TextEdit = replaceRange == null
                    ? null
                    : new TextEdit { Range = replaceRange, NewText = name },
            }).ToArray()
        };
    }

    internal static CompletionList GetTopLevelCompletions()
    {
        return new CompletionList
        {
            Items =
            [
                new CompletionItem { Label = "version", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "version \"$0\"", Detail = "Language version this file targets (optional, first line)" },
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "score {\n\t$0\n}", Detail = "Score block" },
                new CompletionItem { Label = "part", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "part $1 {\n\t$0\n}", Detail = "Part declaration" },
                new CompletionItem { Label = "section", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "section $1 {\n\t$0\n}", Detail = "Section declaration" },
                new CompletionItem { Label = "phrase", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "phrase $1 {\n\t$0\n}", Detail = "Reusable phrase" },
                new CompletionItem { Label = "structure", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "structure { $0 }", Detail = "Playback order" },
                new CompletionItem { Label = "score", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "score {\n\t$0\n}", Detail = "Printable score (visual layout)" },
                new CompletionItem { Label = "title", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "title \"$0\"", Detail = "Title metadata" },
                new CompletionItem { Label = "composer", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "composer \"$0\"", Detail = "Composer metadata" },
                new CompletionItem { Label = "tempo", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tempo $0", Detail = "Tempo (BPM)" },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Time signature" },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "Key signature" },
                new CompletionItem { Label = "clef", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "clef $0", Detail = "Clef (treble/bass/alto/tenor)" },
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $1.$2 = $0", Detail = "Override grob property" },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "revert $1.$0", Detail = "Revert grob property" },
                new CompletionItem { Label = "once", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "once override $1.$2 = $0", Detail = "One-time override" },
                new CompletionItem
                {
                    Label = "newscore",
                    FilterText = "newscore score template",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "// Twinkle, Twinkle, Little Star (public domain).\ntitle \"Twinkle, Twinkle, Little Star\"\ncomposer \"Jane Taylor\"\n\ntempo 100\ntime 4/4\nkey c major\n\npart melody {\n\tclef treble\n\tsection A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n\tsection B { g'4 g f f | e e d2 | }\n}\n\nstructure { A |: B :| A \"A2\" }\n\nscore \"score\" {\n\tstaff melody\n}\n$0",
                    Detail = "Complete single-staff score (Twinkle template)",
                },
                new CompletionItem { Label = "lyrics", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "lyrics {\n\t$0\n}", Detail = "Lyrics block" },
                new CompletionItem
                {
                    Label = "grandstaff",
                    FilterText = "grandstaff piano",
                    Kind = CompletionItemKind.Snippet,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = "// Twinkle, Twinkle, Little Star (public domain) — piano.\ntitle \"Twinkle, Twinkle, Little Star\"\ncomposer \"Jane Taylor\"\n\ntempo 100\ntime 4/4\nkey c major\n\npart rh { clef treble }\npart lh { clef bass }\n\nsection A {\n\trh { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n\tlh { c,2 g | c2 c | f2 c | g2 c | }\n}\n\nstructure { A }\n\nscore \"score\" {\n\tgrandStaff {\n\t\tstaff rh\n\t\tstaff lh\n\t}\n}\n$0",
                    Detail = "Two-staff (piano) score (grand staff template)",
                }
            ]
        };
    }

    /// <summary>
    /// Finds the key signature in force at <paramref name="offset"/> by scanning back
    /// for the nearest preceding <c>key &lt;tonic&gt; &lt;mode&gt;</c> declaration, and
    /// returns its sharp(+)/flat(-) count (0 = C major / no key found).
    /// </summary>
    private static int CurrentKeySharps(string text, int offset)
    {
        if (offset > text.Length) offset = text.Length;
        var prefix = text.Substring(0, offset);
        // tonic carries its own accidental suffix (fis, bes, …); mode is a word.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            prefix, @"\bkey\s+([a-gA-G](?:is|es|isis|eses)?)\s+([A-Za-z]+)");
        if (matches.Count == 0) return 0;
        var last = matches[matches.Count - 1];
        return LilySharp.Core.Music.KeySpelling.SharpsFor(
            last.Groups[1].Value, last.Groups[2].Value) ?? 0;
    }

    /// <summary>
    /// True when the cursor sits in the MUSIC of a percussion part: ascend the
    /// unmatched-brace chain from the cursor, skip voice { } wrappers, take the
    /// first named block as the part reference, and check that part's
    /// declaration for `clef percussion`.
    /// </summary>
    internal static bool IsInsidePercussionPartMusic(string text, int offset)
        => IsInsidePercussionPartMusic(text, offset, out _);

    internal static bool IsInsidePercussionPartMusic(string text, int offset, out bool insideVoice)
    {
        insideVoice = false;
        int depth = 0;
        for (int i = Math.Min(offset, text.Length) - 1; i >= 0; i--)
        {
            char ch = text[i];
            if (ch == '}') { depth++; continue; }
            if (ch != '{') continue;
            if (depth > 0) { depth--; continue; }

            // Word before this unmatched open brace
            int e = i - 1;
            while (e >= 0 && char.IsWhiteSpace(text[e])) e--;
            int s = e;
            while (s >= 0 && (char.IsLetterOrDigit(text[s]) || text[s] == '_')) s--;
            if (e < 0 || s == e) return false;
            string name = text[(s + 1)..(e + 1)];

            if (name.Equals("voice", StringComparison.OrdinalIgnoreCase))
            {
                insideVoice = true;
                continue; // ascend to the enclosing part block
            }

            // Structural keywords: this is not a part-music block.
            if (name is "section" or "score" or "structure" or "part" or "phrase"
                or "grandstaff" or "lyrics" or "chords" or "repeat" or "tuplet"
                or "grace" or "acciaccatura" or "appoggiatura")
                return false;

            return PartIsPercussion(text, name);
        }
        return false;
    }

    private static bool PartIsPercussion(string text, string partName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text,
            @"\bpart\s+" + System.Text.RegularExpressions.Regex.Escape(partName)
            + @"\s*\{(?<body>[^}]*)\}");
        return m.Success && System.Text.RegularExpressions.Regex.IsMatch(
            m.Groups["body"].Value, @"\bclef\s*:?\s*percussion\b");
    }

    /// <summary>
    /// Drum-kit completions for a percussion part's music: the DrumNameRegistry
    /// vocabulary (aliases first — the idiomatic form), plus rests and the
    /// structural snippets that remain valid in drum music. No pitch letters.
    /// </summary>
    private static CompletionList GetDrumCompletions(bool insideVoice)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();
        foreach (var kv in LilySharp.Core.Syntax.DrumNameRegistry.AliasEntries)
        {
            LilySharp.Core.Syntax.DrumNameRegistry.TryGet(kv.Key, out var info);
            items.Add(new CompletionItem
            {
                Label = kv.Key,
                Kind = CompletionItemKind.Value,
                Detail = $"{kv.Value} (GM {info.GmKey})",
                SortText = "0" + kv.Key,
            });
        }
        foreach (var kv in LilySharp.Core.Syntax.DrumNameRegistry.CanonicalEntries)
        {
            items.Add(new CompletionItem
            {
                Label = kv.Key,
                Kind = CompletionItemKind.Value,
                Detail = $"GM {kv.Value.GmKey}",
                SortText = "1" + kv.Key,
            });
        }
        items.AddRange(new[]
        {
            new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest", SortText = "2r" },
            new CompletionItem { Label = "s", Kind = CompletionItemKind.Value, Detail = "Spacer rest (invisible)", SortText = "2s" },
            new CompletionItem { Label = "R", Kind = CompletionItemKind.Value, Detail = "Full-measure rest", SortText = "2R" },
            new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "repeat percent 2 {\n\t$0\n}", Detail = "Repeat block (percent/unfold/tremolo)", SortText = "3repeat" },
            new CompletionItem { Label = "tuplet", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tuplet 3/2 { $0 }", Detail = "Tuplet (e.g., triplet)", SortText = "3tuplet" },
            new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Change time signature", SortText = "4time" },
        });
        // voice { } is only meaningful directly in the part's music —
        // NESTED voice blocks silently become parallel siblings (verified),
        // so the snippet is withheld inside a voice wrapper.
        if (!insideVoice)
            items.Add(new CompletionItem { Label = "voice", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "voice { $0 }", Detail = "Voice (hats up / kick+snare down)", SortText = "3voice" });
        return new CompletionList { IsIncomplete = false, Items = [.. items] };
    }

    private static CompletionList GetMusicCompletions(string word, int keySharps)
    {
        var items = new System.Collections.Generic.List<CompletionItem>();

        // Pitches, spelled for the key in force at the cursor: in G major (one
        // sharp) the F row is offered as "fis", so accepting it writes the
        // sounding note. Filtering on the spelled form keeps the row visible
        // whether the user typed just "f" or the full "fis".
        foreach (char letter in "cdefgab")
        {
            int alt = LilySharp.Core.Music.KeySpelling.Alteration(
                LilySharp.Core.Music.KeySpelling.StepOf(letter), keySharps);
            string spelled = LilySharp.Core.Music.KeySpelling.SpellLetter(letter, keySharps);
            string upper = char.ToUpperInvariant(letter).ToString();
            items.Add(new CompletionItem
            {
                Label = spelled,
                Kind = CompletionItemKind.Value,
                Detail = alt == 0
                    ? $"{upper} pitch"
                    : $"{upper}{(alt > 0 ? "♯" : "♭")} pitch (from key signature)",
                FilterText = spelled,
                InsertText = spelled,
                SortText = "0" + letter
            });
        }

        items.AddRange(new[]
        {
                // Rests
                new CompletionItem { Label = "r", Kind = CompletionItemKind.Value, Detail = "Rest", SortText = "1r" },
                new CompletionItem { Label = "s", Kind = CompletionItemKind.Value, Detail = "Spacer rest (invisible)", SortText = "1s" },
                new CompletionItem { Label = "R", Kind = CompletionItemKind.Value, Detail = "Full-measure rest", SortText = "1R" },

                // Structures
                new CompletionItem { Label = "|: :|", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "|: $0 :|", Detail = "Volta repeat (symbolic; add endings [1. …] [2. …])", SortText = "2repeat" },
                new CompletionItem { Label = "|: :| [1.][2.]", Kind = CompletionItemKind.Snippet, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "|: $1 [1. $2 ] :| [2. $0 ]", Detail = "Volta repeat with endings", SortText = "2repeatalt" },
                new CompletionItem { Label = "repeat", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "repeat unfold 2 {\n\t$0\n}", Detail = "Repeat block (unfold/percent/tremolo)", SortText = "2repeatkw" },
                new CompletionItem { Label = "tuplet", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "tuplet 3/2 { $0 }", Detail = "Tuplet (e.g., triplet)", SortText = "2tuplet" },
                new CompletionItem { Label = "grace", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "grace { $0 }", Detail = "Grace notes", SortText = "2grace" },
                new CompletionItem { Label = "acciaccatura", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "acciaccatura { $0 }", Detail = "Slashed grace note", SortText = "2acciaccatura" },
                new CompletionItem { Label = "appoggiatura", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "appoggiatura { $0 }", Detail = "Unslashed grace note", SortText = "2appoggiatura" },

                // Mid-measure declarations
                new CompletionItem { Label = "clef", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "clef $0", Detail = "Change clef", SortText = "3clef" },
                new CompletionItem { Label = "key", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "key $0", Detail = "Change key signature", SortText = "3key" },
                new CompletionItem { Label = "time", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "time $0", Detail = "Change time signature", SortText = "3time" },

                // Grob overrides
                new CompletionItem { Label = "override", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "override $1.$2 = $0", Detail = "Override grob property", SortText = "4override" },
                new CompletionItem { Label = "revert", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "revert $1.$0", Detail = "Revert grob property", SortText = "4revert" },
                new CompletionItem { Label = "once", Kind = CompletionItemKind.Keyword, InsertTextFormat = InsertTextFormat.Snippet, InsertText = "once override $1.$2 = $0", Detail = "One-time override", SortText = "4once" }
        });

        // Chord note-expansion: a chord symbol the user is typing (cmaj7, am, g7)
        // offers to replace itself with the spelled note chord <c e g b> — the same
        // tone set that names and (later) voices the chord. The notes are bare so
        // relative mode voices them ascending. LILYPOND-REF: scm/chord-entry.scm.
        if (word.Length >= 2 && ChordStructure.TryParseSymbol(word, out var chord))
        {
            var notes = chord.ToNoteChord();
            items.Insert(0, new CompletionItem
            {
                Label = $"{word}  →  {notes}",
                Kind = CompletionItemKind.Snippet,
                FilterText = word,
                InsertText = notes,
                Detail = $"{chord.DisplayName} chord notes",
                SortText = "00chord",
            });
        }
        return new CompletionList { Items = items.ToArray() };
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
                new CompletionItem { Label = "staccatissimo", Kind = CompletionItemKind.Value, Detail = "Staccatissimo (wedge)", SortText = "0staccatissimo" },
                new CompletionItem { Label = "upbow", Kind = CompletionItemKind.Value, Detail = "Up-bow (V, above)", SortText = "0upbow" },
                new CompletionItem { Label = "downbow", Kind = CompletionItemKind.Value, Detail = "Down-bow (frog, above)", SortText = "0downbow" },
                new CompletionItem { Label = "harmonic", Kind = CompletionItemKind.Value, Detail = "Harmonic circle (flageolet)", SortText = "0harmonic" },

                // Free expressive text
                new CompletionItem
                {
                    Label = "text", Kind = CompletionItemKind.Value,
                    Detail = "Free expressive text below the note (\"dolce\", \"pizz.\", …); .up for above",
                    InsertText = "text(\"$0\")", InsertTextFormat = InsertTextFormat.Snippet,
                    SortText = "0text",
                },

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
                new CompletionItem { Label = "sfz", Kind = CompletionItemKind.Value, Detail = "Sforzato accent dynamic", SortText = "2sfz" },
                new CompletionItem { Label = "fp", Kind = CompletionItemKind.Value, Detail = "Forte-piano accent dynamic", SortText = "2fp" },
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
                new CompletionItem { Label = "glissando", Kind = CompletionItemKind.Value, Detail = "Glissando to next note", SortText = "6glissando" },
                new CompletionItem { Label = "gliss", Kind = CompletionItemKind.Value, Detail = "Glissando to next note (short for glissando)", SortText = "6glissando2" },
                new CompletionItem { Label = "arpeggio", Kind = CompletionItemKind.Value, Detail = "Arpeggiate chord", SortText = "6arpeggio" },
                new CompletionItem { Label = "courtesy", Kind = CompletionItemKind.Value, Detail = "Force courtesy accidental", SortText = "6courtesy" },
                new CompletionItem { Label = "editorial", Kind = CompletionItemKind.Value, Detail = "Editorial (suggestion) accidental above the note", SortText = "6editorial" },

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

        var (startLine, startCol) = GetLineAndColumn(doc.Text, node.Span.Start);
        var (endLine, endCol) = GetLineAndColumn(doc.Text, node.Span.End);

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
            RenderDeclarationSyntax => "**Score**: A printable score — visual layout (staff assignment). Output format is a CLI choice.",
            VariableDeclarationSyntax varDecl => $"**Variable**: `{varDecl.Name.Text}`",
            VariableReferenceSyntax varRef => $"**Variable reference**: `${varRef.Name.Text}`",
            LyricsBlockSyntax => "**Lyrics**: Text aligned to notes",
            ArticulationSyntax art => $"**Articulation**: @{art.NameToken.Text}",
            _ => null
        };
    }

    // Delegate to the single, correct line/character -> offset conversion in
    // DocumentManager: it handles \n, \r\n AND lone \r line breaks and clamps the
    // character to the END OF ITS LINE (not just the text length), so an over-large
    // character no longer walks into following lines and resolves the wrong node.
    private static int GetOffset(string text, int line, int character)
        => DocumentManager.GetOffset(text, new Position { Line = line, Character = character });

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

    // The score's name (the quoted string right after `score`), or plain "score"
    // when unnamed. Child 1 is either the name token or the opening brace —
    // mirrors RenderSpecParser's name extraction.
    private static string RenderSymbolName(RenderDeclarationSyntax render) =>
        render.GetChild(1) is SyntaxTokenNode nameTok && nameTok.Kind != SyntaxKind.OpenBrace
            ? $"score {nameTok.Text}"
            : "score";

    private DocumentSymbol? CreateSymbol(SyntaxNode node, string text)
    {
        var (name, kind) = node switch
        {
            PartDeclarationSyntax part => (GetPartName(part), SymbolKind.Class),
            StaffDeclarationSyntax staff => (GetStaffName(staff), SymbolKind.Class),

            VariableDeclarationSyntax variable => (variable.Name.Text, SymbolKind.Variable),
            PhraseDeclarationSyntax phrase => ($"phrase {phrase.Name.Text}", SymbolKind.Function),
            SectionDeclarationSyntax section => ($"section {section.SectionName}", SymbolKind.Namespace),
            StructureDeclarationSyntax => ("structure", SymbolKind.Struct),
            RenderDeclarationSyntax render => (RenderSymbolName(render), SymbolKind.Module),
            RepeatExpressionSyntax repeat => ($"repeat {repeat.Count.Text}x", SymbolKind.Operator),
            // Tuplets and voice-parallel blocks are inline music constructs, not
            // navigation landmarks — emitting one per triplet floods the outline.
            KeySignatureSyntax key => ($"key {key.Pitch?.PitchName} {(key.IsMajor ? "major" : "minor")}", SymbolKind.Key),
            ClefDeclarationSyntax clef => ($"clef {clef.ClefName.Text}", SymbolKind.Key),
            LyricsBlockSyntax => ("lyrics", SymbolKind.String),
            OverrideDeclarationSyntax ovr => ($"override {ovr.GrobName.Text}.{ovr.PropertyName.Text}", SymbolKind.Property),
            // Header landmarks: title/composer (and any other metadata), time, tempo.
            MetadataDeclarationSyntax meta => (NodeText(meta, text), SymbolKind.String),
            TimeSignatureSyntax time => (NodeText(time, text), SymbolKind.Key),
            TempoDeclarationSyntax tempo => (NodeText(tempo, text), SymbolKind.Key),
            _ => (null, SymbolKind.Null)
        };

        if (name == null) return null;

        // A part's human-readable instrument name (the display label if present,
        // else the preset) shows dimmed beside the identifier via the detail field.
        string? detail = node is PartDeclarationSyntax partNode ? GetPartInstrument(partNode) : null;

        var (startLine, startCol) = GetLineAndColumn(text, node.Span.Start);
        var (endLine, endCol) = GetLineAndColumn(text, node.Span.End);

        return new DocumentSymbol
        {
            Name = name,
            Detail = detail,
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

    // Single-line source text of a node (whitespace-collapsed), used for header
    // landmarks (title / composer / time / tempo) shown verbatim in the outline.
    private static string NodeText(SyntaxNode node, string text)
    {
        // Start at the FIRST TOKEN's span, not the node's: a composite node reports
        // no leading trivia (LeadingTrivia => null), so its Span begins at any
        // leading comment above it — the first token's span correctly excludes it.
        int start = node.GetChild(0)?.Span.Start ?? node.Span.Start;
        var raw = text.Substring(start, node.Span.End - start);
        return System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"\s+", " ");
    }

    // A part's instrument label for the outline detail: the quoted display name
    // (`instrument violin "1st Violin"` → "1st Violin") if present, else the
    // preset (`instrument piano-right` → piano-right), else null.
    private static string? GetPartInstrument(PartDeclarationSyntax part)
    {
        for (int i = 0; i < part.SlotCount; i++)
        {
            if (part.GetChild(i) is PropertyAssignmentSyntax prop
                && prop.NameToken.Kind == SyntaxKind.InstrumentKeyword)
            {
                var tokens = new List<SyntaxTokenNode>();
                for (int j = 2; j < prop.SlotCount; j++)
                    if (prop.GetChild(j) is SyntaxTokenNode t) tokens.Add(t);
                var label = tokens.FirstOrDefault(t => t.Kind == SyntaxKind.StringLiteral);
                if (label != null) return label.Text;          // quoted display name
                var preset = string.Concat(tokens.Select(t => t.Text));
                return string.IsNullOrWhiteSpace(preset) ? null : preset;
            }
        }
        return null;
    }

    private static string GetPartName(PartDeclarationSyntax part)
    {
        // The name is child 1 (keyword, name, ...). It may be a clef-word keyword
        // (`part bass`/`treble`), so take child 1 directly rather than scanning for
        // the first Identifier — that scan would skip a keyword name and wrongly
        // return the instrument identifier deeper in the body.
        if (part.GetChild(1) is SyntaxTokenNode name
            && name.Kind != SyntaxKind.OpenBrace
            && !string.IsNullOrWhiteSpace(name.Text))
            return $"part {name.Text.Trim('"')}";
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
        // Use the node's Span (the bare token, excluding leading/trailing trivia)
        // rather than Position/FullWidth (which include trivia): an LSP range must
        // cover the identifier itself, not the surrounding whitespace/newlines.
        var (startLine, startCol) = GetLineAndColumn(text, node.Span.Start);
        var (endLine, endCol) = GetLineAndColumn(text, node.Span.End);

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
                SyntaxKind.AlternativeKeyword or
                SyntaxKind.ScoreKeyword or SyntaxKind.PartKeyword or SyntaxKind.StaffKeyword or
                SyntaxKind.VoiceKeyword or SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword or
                SyntaxKind.TempoKeyword or SyntaxKind.TimeKeyword or SyntaxKind.KeyKeyword or
                SyntaxKind.ClefKeyword or SyntaxKind.TupletKeyword or SyntaxKind.GraceKeyword or
                SyntaxKind.MajorKeyword or SyntaxKind.MinorKeyword or SyntaxKind.LyricsKeyword or
                SyntaxKind.OverrideKeyword or SyntaxKind.RevertKeyword or SyntaxKind.OnceKeyword or
                SyntaxKind.PhraseKeyword or SyntaxKind.SectionKeyword or SyntaxKind.StructureKeyword => 0,

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
                var (line, character) = GetLineAndCharacter(text, node.Span.Start);
                tokens.Add(new SemanticToken(line, character, node.Width, tokenType.Value));
            }
        }
        else if (node is VariableReferenceSyntax varRef)
        {
            // Variable reference (after $ or use)
            var nameNode = varRef.Name;
            var (line, character) = GetLineAndCharacter(text, nameNode.Span.Start);
            tokens.Add(new SemanticToken(line, character, nameNode.Width, 1));
        }
        else if (node is VariableDeclarationSyntax varDecl)
        {
            // Variable declaration name
            var nameNode = varDecl.Name;
            var (line, character) = GetLineAndCharacter(text, nameNode.Span.Start);
            tokens.Add(new SemanticToken(line, character, nameNode.Width, 1));
        }
        else if (node is PropertyAssignmentSyntax propAssign)
        {
            // Property VALUE tokens (instrument bass-guitar, name Foo, …):
            // color the whole value uniformly. Without this only value words
            // that happened to be keywords ("bass" = the clef name) lit up,
            // leaving "-guitar" plain. One span over first→last value token.
            // Restricted to word-valued properties — pitch/number values
            // (transpose d, channel 1) keep their own token colors and must
            // not sit inside an overlapping span.
            string propName = propAssign.NameToken.Text.ToLowerInvariant();
            if (propName is not ("instrument" or "name" or "tuning"))
                goto recurse;
            SyntaxTokenNode? firstVal = null, lastVal = null;
            for (int vi = 2; vi < propAssign.SlotCount; vi++)
            {
                if (propAssign.GetChild(vi) is SyntaxTokenNode vt)
                {
                    firstVal ??= vt;
                    lastVal = vt;
                }
            }
            if (firstVal != null && lastVal != null
                && firstVal.Kind != SyntaxKind.StringLiteral)
            {
                var (line, character) = GetLineAndCharacter(text, firstVal.Span.Start);
                int width = lastVal.Span.End - firstVal.Span.Start;
                tokens.Add(new SemanticToken(line, character, width, 3));
            }
            // fall through to recursion: the property NAME keyword still
            // gets its keyword color from the token pass.
            recurse: ;
        }
        else if (node is ArticulationSyntax artNode)
        {
            // '@' + name as ONE articulation-colored span. @cue/@feather/…
            // only lit up when a TextMate regex happened to list them.
            var (line, character) = GetLineAndCharacter(text, artNode.Span.Start);
            tokens.Add(new SemanticToken(line, character,
                artNode.NameToken.Span.End - artNode.Span.Start, 7));
        }
        else if (node is MusicMarkSyntax markNode)
        {
            // '@name' prefix only — parenthesised args keep their own colors
            // (numbers in @fig(6 4), the string in @text("…")).
            if (markNode.GetChild(1) is SyntaxTokenNode markName)
            {
                var (line, character) = GetLineAndCharacter(text, markNode.Span.Start);
                tokens.Add(new SemanticToken(line, character,
                    markName.Span.End - markNode.Span.Start, 7));
            }
        }
        else if (node is RepeatExpressionSyntax repNode)
        {
            // 'tremolo' / 'percent' / 'unfold' lex as identifiers, not
            // keyword kinds — `repeat` colored, its type word did not.
            var rt = repNode.RepeatType;
            var (rline, rchar) = GetLineAndCharacter(text, rt.Span.Start);
            tokens.Add(new SemanticToken(rline, rchar, rt.Width, 0));
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
        // Foldable node types: MusicBlock, PartDeclaration, etc.
        bool isFoldable = node is MusicBlockSyntax or
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

        // Part-name rename: from the caret on any `part NAME` declaration or one
        // of its references (section-body part blocks, score staff/ossia/tab
        // targets, midi part renders), rewrite every occurrence at once.
        var partToken = LilySharp.Core.Editing.PartReferenceFinder.PartNameTokenAt(doc.Tree.GetRoot(), offset);
        if (partToken != null)
        {
            var partEdits = LilySharp.Core.Editing.PartReferenceFinder
                .Occurrences(doc.Tree.GetRoot(), partToken.Text)
                .Select(t => TokenTextEdit(doc.Text, t, newName))
                .ToArray();
            if (partEdits.Length == 0) return null;
            return new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]>
                {
                    [uri.ToString()] = partEdits
                }
            };
        }

        // Section-name rename: from the caret on any `section NAME` declaration or
        // one of its structure references (plain `NAME`, silent `~NAME`, volta
        // `[1. NAME]`), rewrite every occurrence at once.
        var sectionToken = LilySharp.Core.Editing.SectionReferenceFinder.SectionNameTokenAt(doc.Tree.GetRoot(), offset);
        if (sectionToken != null)
        {
            var sectionEdits = LilySharp.Core.Editing.SectionReferenceFinder
                .Occurrences(doc.Tree.GetRoot(), sectionToken.Text)
                .Select(t => TokenTextEdit(doc.Text, t, newName))
                .ToArray();
            if (sectionEdits.Length == 0) return null;
            return new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]>
                {
                    [uri.ToString()] = sectionEdits
                }
            };
        }

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
                var (line, character) = GetLineAndCharacter(doc.Text, decl.Name.Span.Start);
                edits.Add(new TextEdit
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + decl.Name.Width }
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
                var (line, character) = GetLineAndCharacter(doc.Text, reference.Name.Span.Start);
                edits.Add(new TextEdit
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + reference.Name.Width }
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

    /// <summary>A single-token replacement edit: covers the bare token span
    /// (leading trivia excluded) so only the identifier is rewritten.</summary>
    private static TextEdit TokenTextEdit(string text, SyntaxNode token, string newName)
    {
        var (line, character) = GetLineAndCharacter(text, token.Span.Start);
        return new TextEdit
        {
            Range = new LspRange
            {
                Start = new Position { Line = line, Character = character },
                End = new Position { Line = line, Character = character + token.Width }
            },
            NewText = newName
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
            // Compute the end from the span's END offset (not start-char + length):
            // a diagnostic span may cross a line, and assuming one line puts the end
            // past the line, yielding a malformed edit.
            var (startLine, startChar) = GetLineAndCharacter(doc.Text, diagnostic.Span.Start);
            var (endLine, endChar) = GetLineAndCharacter(doc.Text, diagnostic.Span.End);
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
                                    Start = new Position { Line = startLine, Character = startChar },
                                    End = new Position { Line = endLine, Character = endChar }
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

        // Suggest an explicit `structure` when the file has sections but none is
        // declared. Omitting it is valid (sections play in declaration order),
        // so this is an on-demand convenience, not a warning.
        var sections = doc.Tree.GetNodes<SectionDeclarationSyntax>()
            .OrderBy(s => s.Position).ToList();
        if (sections.Count > 0 && !doc.Tree.GetNodes<StructureDeclarationSyntax>().Any())
        {
            var names = string.Join(" ", sections.Select(s => s.SectionName));
            // Slot it between the sections and the first render/score if present,
            // otherwise right after the last section.
            var firstRender = doc.Tree.GetNodes<RenderDeclarationSyntax>()
                .OrderBy(r => r.Position).FirstOrDefault();
            int offset;
            string newText;
            if (firstRender != null)
            {
                offset = firstRender.Position;
                newText = $"structure {{ {names} }}\n\n";
            }
            else
            {
                var last = sections[^1];
                offset = last.Position + last.FullWidth;
                newText = $"\nstructure {{ {names} }}\n";
            }
            var (insLine, insChar) = GetLineAndCharacter(doc.Text, offset);
            actions.Add(new CodeAction
            {
                Title = "Insert structure declaration",
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
                                    Start = new Position { Line = insLine, Character = insChar },
                                    End = new Position { Line = insLine, Character = insChar }
                                },
                                NewText = newText
                            }
                        }
                    }
                }
            });
        }

        // Refactor: Extract variable from music block
        if (node is MusicBlockSyntax block && block.Items.Any())
        {
            var blockText = block.ToFullString().Trim();
            var (line, character) = GetLineAndCharacter(doc.Text, block.Position);
            var (endLine, endChar) = GetLineAndCharacter(doc.Text, block.Position + block.FullWidth);

            actions.Add(new CodeAction
            {
                Title = "Extract to phrase",
                Kind = CodeActionKind.Refactor,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<string, TextEdit[]>
                    {
                        [uri.ToString()] = new[]
                        {
                            // Insert a phrase declaration at the top. blockText already
                            // includes the braces, so `phrase melody { … }` is well-formed.
                            // (`let name = …` was removed from the grammar.)
                            new TextEdit
                            {
                                Range = new LspRange
                                {
                                    Start = new Position { Line = 0, Character = 0 },
                                    End = new Position { Line = 0, Character = 0 }
                                },
                                NewText = $"phrase melody {blockText}\n\n"
                            },
                            // Replace block with a phrase reference
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

        // First keyword on the line (in priority order) that has a signature wins.
        foreach (var entry in SignatureTable)
        {
            int kw = lineText.IndexOf(entry.Keyword, StringComparison.Ordinal);
            if (kw < 0) continue;

            int activeParameter = CountSpaces(lineText[(kw + entry.Keyword.Length)..]);
            var sig = new SignatureInformation
            {
                Label = entry.Label,
                Documentation = entry.Documentation,
                Parameters = entry.Parameters
                    .Select(p => new ParameterInformation { Label = p.Label, Documentation = p.Documentation })
                    .ToArray(),
            };
            return new SignatureHelp
            {
                Signatures = new[] { sig },
                ActiveSignature = 0,
                ActiveParameter = Math.Min(activeParameter, sig.Parameters.Length - 1),
            };
        }

        return null;
    }

    private readonly record struct SignatureEntry(
        string Keyword, string Label, string Documentation, (string Label, string Documentation)[] Parameters);

    // Keyword → signature, in match-priority order (the first keyword found on the
    // line wins). Adding a keyword's help is a one-line table entry.
    private static readonly SignatureEntry[] SignatureTable =
    {
        new("relative", "relative pitch { music }",
            "Sets relative pitch mode. Notes are interpreted relative to the previous note.",
            new[] { ("pitch", "Base pitch with optional octave marks (e.g., c', c'')"),
                    ("{ music }", "Music block containing notes") }),
        new("repeat", "repeat (unfold|percent|tremolo) count { music }",
            "Repeats the music block. For volta repeats use the symbolic form "
                + "'|: … :|' (count '|: … :|*N') with inline endings '[1. …] [2. …]'.",
            new[] { ("unfold|percent|tremolo", "Repeat kind (volta is the symbolic |: :| form, not this keyword)"),
                    ("count", "Number of repetitions (integer)"),
                    ("{ music }", "Music block to repeat") }),
        new("tempo", "tempo \"marking\" duration = bpm",
            "Sets the tempo for playback.",
            new[] { ("\"marking\"", "Optional tempo marking (e.g., \"Allegro\")"),
                    ("duration", "Note duration (e.g., 4 for quarter note)"),
                    ("bpm", "Beats per minute") }),
        new("time", "time numerator/denominator",
            "Sets the time signature.",
            new[] { ("numerator/denominator", "Time signature (e.g., 4/4, 3/4, 6/8)") }),
        new("key", "key pitch major|minor",
            "Sets the key signature.",
            new[] { ("pitch", "Key pitch (e.g., c, g, fis, bes)"),
                    ("major|minor", "Mode: major or minor") }),
        new("tuplet", "tuplet ratio { music }",
            "Creates a tuplet (e.g., triplet).",
            new[] { ("ratio", "Ratio (e.g., 3/2 for triplet)"),
                    ("{ music }", "Notes in the tuplet") }),
        new("override", "override Grob.property = value",
            "Overrides a grob (graphical object) property.",
            new[] { ("Grob.property", "Grob name and property (e.g., Stem.length, NoteHead.color)"),
                    ("value", "New value (number, string, or identifier)") }),
        new("phrase", "phrase name { music }",
            "Declares a reusable musical phrase. Reference with $name.",
            new[] { ("name", "Phrase name (identifier)"),
                    ("{ music }", "Music content") }),
        new("section", "section Name { parts... }",
            "Declares a section grouping multiple parts.",
            new[] { ("Name", "Section name (identifier)"),
                    ("{ parts... }", "Part blocks with music") }),
    };

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
                var (line, character) = GetLineAndCharacter(doc.Text, decl.Name.Span.Start);
                highlights.Add(new DocumentHighlight
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + decl.Name.Width }
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
                var (line, character) = GetLineAndCharacter(doc.Text, reference.Name.Span.Start);
                highlights.Add(new DocumentHighlight
                {
                    Range = new LspRange
                    {
                        Start = new Position { Line = line, Character = character },
                        End = new Position { Line = line, Character = character + reference.Name.Width }
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
    /// <summary>
    /// The document's tree with <c>include "..."</c> directives resolved (files read
    /// relative to the document), or the plain tree when there are no includes. The
    /// document's own text stays the prefix, so its positions are preserved.
    /// </summary>
    private static SyntaxTree ExpandIncludes(Document doc, Uri uri)
    {
        if (!LilySharp.Core.Parser.IncludeExpander.HasIncludes(doc.Text))
            return doc.Tree;

        var basePath = uri.IsFile ? uri.LocalPath : string.Empty;
        var expanded = LilySharp.Core.Parser.IncludeExpander.Expand(doc.Text, basePath,
            p => System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null);
        return SyntaxTree.Parse(expanded);
    }

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

        // Resolve `include "..."` directives so the preview shows the whole piece.
        // The main file is the prefix of the combined source, so its positions
        // (data-pos editor<->preview sync) are unchanged.
        var tree = ExpandIncludes(doc, @params.TextDocument.Uri);

        // Extract render definitions
        var renders = ExtractRenderInfo(tree);

        if (tree.HasErrors)
        {
            var errors = string.Join("\n", tree.Diagnostics
                .Where(d => d.Severity == CoreDiagnosticSeverity.Error)
                .Select(d => {
                    var (line, col) = GetLineAndColumn(tree.Text, d.Span.Start);
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
            var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree, renderOptions, @params.RenderName);

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
    /// Exports the current document to a file in the requested format. The
    /// extension drives this from the preview's Export button: it shows the
    /// format/save dialog and passes the chosen path here. SVG/PNG/PDF honour the
    /// selected score (RenderName); MIDI and MusicXML export the whole piece.
    /// </summary>
    [JsonRpcMethod("lilysharp/export", UseSingleObjectParameterDeserialization = true)]
    public ExportResponse Export(ExportParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ExportResponse { Success = false, Error = "Document not found" };

        var tree = ExpandIncludes(doc, @params.TextDocument.Uri);
        if (tree.HasErrors)
        {
            var errors = string.Join("\n", tree.Diagnostics
                .Where(d => d.Severity == CoreDiagnosticSeverity.Error)
                .Select(d =>
                {
                    var (line, col) = GetLineAndColumn(tree.Text, d.Span.Start);
                    return $"Line {line}, Col {col}: {d.Message}";
                }));
            return new ExportResponse { Success = false, Error = errors };
        }

        try
        {
            var format = (@params.Format ?? "svg").ToLowerInvariant();
            var outputPath = @params.OutputPath;
            var renderName = @params.RenderName;
            switch (format)
            {
                case "svg":
                    var fontDir = LilySharp.Core.Rendering.FontLocator.Find();
                    // Embed the font so the exported SVG is self-contained; fall back
                    // to a reference if the bundled font can't be located.
                    var svgOpts = fontDir != null
                        ? LilySharp.Core.Svg.Renderer.SvgRenderOptions.Export(fontDir)
                        : LilySharp.Core.Svg.Renderer.SvgRenderOptions.Default;
                    File.WriteAllText(outputPath,
                        LilySharp.Core.Svg.SvgGenerator.Generate(tree, svgOpts, renderName));
                    break;
                case "png":
                {
                    // One file per page, LilyPond naming: single page keeps the
                    // chosen name; multiple pages save as BASE-page1.png,
                    // BASE-page2.png, … (scm/ps-to-png.scm).
                    var pages = LilySharp.Core.Png.PngGenerator.GeneratePages(tree, null, renderName);
                    if (pages.Count == 1)
                    {
                        File.WriteAllBytes(outputPath, pages[0]);
                    }
                    else
                    {
                        var dir = Path.GetDirectoryName(outputPath) ?? "";
                        var baseName = Path.GetFileNameWithoutExtension(outputPath);
                        var pngExt = Path.GetExtension(outputPath);
                        var names = new List<string>(pages.Count);
                        for (int p = 0; p < pages.Count; p++)
                        {
                            var pagePath = Path.Combine(dir, $"{baseName}-page{p + 1}{pngExt}");
                            File.WriteAllBytes(pagePath, pages[p]);
                            names.Add(Path.GetFileName(pagePath));
                        }
                        // The dialog's path names ONE file; report what was
                        // actually written so the toast isn't a lie.
                        outputPath = Path.Combine(dir, string.Join(", ", names));
                    }
                    break;
                }
                case "pdf":
                    File.WriteAllBytes(outputPath,
                        LilySharp.Core.Pdf.PdfGenerator.Generate(tree, null, renderName));
                    break;
                case "midi":
                    new LilySharp.Core.Midi.MidiExporter().Export(tree).Save(outputPath);
                    break;
                case "musicxml":
                    new LilySharp.Core.MusicXml.MusicXmlExporter().ExportToFile(tree, outputPath);
                    break;
                case "vsqx":
                    new LilySharp.Core.Vocaloid.VsqxExporter().Export(tree).Save(outputPath);
                    break;
                case "ly":
                    File.WriteAllText(outputPath,
                        new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree));
                    break;
                default:
                    return new ExportResponse { Success = false, Error = $"Unknown format: {format}" };
            }
            return new ExportResponse { Success = true, OutputPath = outputPath };
        }
        catch (Exception ex)
        {
            return new ExportResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Flattens the piece to note events in seconds for the preview's WebAudio
    /// player: the same MidiExporter model as .mid export, with the tempo map
    /// applied server-side so the webview only schedules oscillators.
    /// </summary>
    [JsonRpcMethod("lilysharp/playback", UseSingleObjectParameterDeserialization = true)]
    public PlaybackResponse GetPlayback(PlaybackParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new PlaybackResponse { Error = "Document not found" };
        var tree = ExpandIncludes(doc, @params.TextDocument.Uri);
        if (tree.HasErrors)
            return new PlaybackResponse { Error = "Score has errors" };
        try
        {
            var midi = new LilySharp.Core.Midi.MidiExporter().Export(tree);
            int tpq = midi.TicksPerQuarterNote;

            // Tempo map: merged from all tracks, sorted; default 120 bpm.
            var tempos = midi.Tracks.SelectMany(t => t.TempoChanges)
                .OrderBy(t => t.Tick).ToList();
            double SecondsAt(int tick)
            {
                double sec = 0;
                int prevTick = 0;
                double usPerBeat = 500000; // 120 bpm
                foreach (var tc in tempos)
                {
                    if (tc.Tick >= tick)
                        break;
                    sec += (tc.Tick - prevTick) * usPerBeat / tpq / 1e6;
                    prevTick = tc.Tick;
                    usPerBeat = tc.MicrosecondsPerBeat;
                }
                return sec + (tick - prevTick) * usPerBeat / tpq / 1e6;
            }

            var notes = midi.Tracks
                .SelectMany(t => t.Notes)
                .OrderBy(n => n.StartTick)
                .Select(n =>
                {
                    double t0 = SecondsAt(n.StartTick);
                    return new PlaybackNote
                    {
                        T = t0,
                        D = Math.Max(0.03, SecondsAt(n.StartTick + n.DurationTicks) - t0),
                        P = n.Pitch + 0.5 * n.QuarterBend,
                        V = n.Velocity,
                        S = n.SourcePos,
                        O = n.SourceOrdinal,
                        I = n.Timbre,
                    };
                })
                .ToArray();
            return new PlaybackResponse { Notes = notes };
        }
        catch (Exception ex)
        {
            return new PlaybackResponse { Error = ex.Message };
        }
    }

    /// <summary>
    /// Converts the document between the section-major and part-major authoring
    /// layouts (the editor command toggles whichever the file currently uses) and
    /// returns the rewritten source; the extension applies it as a full-document edit.
    /// </summary>
    [JsonRpcMethod("lilysharp/convertLayout", UseSingleObjectParameterDeserialization = true)]
    public ConvertLayoutResponse ConvertLayout(ConvertLayoutParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new ConvertLayoutResponse { Success = false, Error = "Document not found" };

        // Refuse to convert a file with syntax errors: cell extraction needs a
        // clean, balanced tree, and the client overwrites the whole document with
        // the result — so a malformed file would be mangled. Leave it untouched.
        if (LilySharp.Core.Syntax.SyntaxTree.Parse(doc.Text).HasErrors)
            return new ConvertLayoutResponse
            {
                Success = false,
                Error = "Fix the syntax errors before converting the layout — no changes made."
            };

        var from = LilySharp.Core.Editing.PartSectionLayoutConverter.Detect(doc.Text);
        if (from == LilySharp.Core.Editing.LayoutForm.Unknown)
            return new ConvertLayoutResponse
            {
                Success = false,
                Error = "No part/section layout to convert — the file needs parts with sections."
            };

        // Convert self-guards: it returns null unless the result round-trips to a
        // clean parse, so this can never produce a corrupt document.
        var newText = LilySharp.Core.Editing.PartSectionLayoutConverter.Convert(doc.Text);
        if (newText == null)
            return new ConvertLayoutResponse
            {
                Success = false,
                Error = "Conversion would not produce a clean result — no changes made."
            };

        var to = from == LilySharp.Core.Editing.LayoutForm.PartMajor
            ? LilySharp.Core.Editing.LayoutForm.SectionMajor
            : LilySharp.Core.Editing.LayoutForm.PartMajor;
        return new ConvertLayoutResponse
        {
            Success = true,
            NewText = newText,
            FromLayout = from.ToString(),
            ToLayout = to.ToString(),
        };
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
                // Structure: score [name] { ... }; the name is the single token after
                // the keyword (child 1 is the name token or the opening brace). Empty
                // when unnamed — the preview shows such a score as "(Default)".
                string filename = render.GetChild(1) is SyntaxTokenNode nameTok
                    && nameTok.Kind != SyntaxKind.OpenBrace
                    ? nameTok.Text.Trim('"') : "";

                renders.Add(new RenderInfo
                {
                    Name = filename, // empty for an unnamed score
                    Type = "score",
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

/// <summary>
/// Parameters for the lilysharp/export request.
/// </summary>
public class ExportParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
    /// <summary>Output format: svg, png, pdf, midi, or musicxml.</summary>
    public string? Format { get; set; }
    /// <summary>Absolute path to write the exported file to.</summary>
    public string OutputPath { get; set; } = "";
    /// <summary>Score to export (visual formats); null = first/default score.</summary>
    public string? RenderName { get; set; }
}

/// <summary>
/// Response for the lilysharp/export request.
/// </summary>
public class ExportResponse
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameters for the lilysharp/playback request.</summary>
public class PlaybackParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

/// <summary>One playable note, in SECONDS (tempo map already applied).</summary>
public class PlaybackNote
{
    public double T { get; set; }   // onset (s)
    public double D { get; set; }   // duration (s)
    public double P { get; set; }   // MIDI pitch (fractional for quarter tones)
    public int V { get; set; }      // velocity 0-127
    public int S { get; set; }      // source offset (-1 = none) for follow-along highlight
    public int O { get; set; }      // printed-copy ordinal of this onset among same-S copies
    public int I { get; set; }      // timbre family for the preview synth
}

/// <summary>Response for the lilysharp/playback request.</summary>
public class PlaybackResponse
{
    public PlaybackNote[]? Notes { get; set; }
    public string? Error { get; set; }
}

/// <summary>Parameters for the lilysharp/convertLayout request.</summary>
public class ConvertLayoutParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

/// <summary>Response for the lilysharp/convertLayout request: the rewritten source
/// plus which layout it went from / to (for a status message).</summary>
public class ConvertLayoutResponse
{
    public bool Success { get; set; }
    public string? NewText { get; set; }
    public string? FromLayout { get; set; }
    public string? ToLayout { get; set; }
    public string? Error { get; set; }
}






















































































































































































