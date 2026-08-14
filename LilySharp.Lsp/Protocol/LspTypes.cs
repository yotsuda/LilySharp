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

using Newtonsoft.Json;

namespace LilySharp.Lsp.Protocol;

// The subset of the Language Server Protocol 3.17 wire format that Lily#'s
// server actually speaks. These used to come from
// Microsoft.VisualStudio.LanguageServer.Protocol, which cannot ship inside a
// GPL program: its license is the "Microsoft Visual Studio Add-ons and
// Extensions" EULA, which forbids publishing the software or combining it with
// an application for others to use. See the note in LilySharp.Lsp.csproj.
//
// The protocol itself is an open specification (microsoft.github.io/language-
// server-protocol), so what is reproduced here are the JSON member names the
// spec defines — every property carries the [JsonProperty] name the wire uses,
// because the transport (StreamJsonRpc's Newtonsoft formatter) would otherwise
// serialize the C# PascalCase names and no client would understand them.
//
// Optional members are omitted rather than sent as null: a capability the
// server does not implement must be ABSENT, and some clients treat an explicit
// null as "present but broken".

#region Basic structures

/// <summary>Zero-based line and UTF-16 character offset within a line.</summary>
public class Position
{
    public Position() { }

    public Position(int line, int character)
    {
        Line = line;
        Character = character;
    }

    [JsonProperty("line")]
    public int Line { get; set; }

    [JsonProperty("character")]
    public int Character { get; set; }
}

/// <summary>A half-open span: <see cref="Start"/> inclusive, <see cref="End"/> exclusive.</summary>
public class Range
{
    public Range() { }

    public Range(Position start, Position end)
    {
        Start = start;
        End = end;
    }

    [JsonProperty("start")]
    public Position Start { get; set; } = new();

    [JsonProperty("end")]
    public Position End { get; set; } = new();
}

/// <summary>A range inside a specific document.</summary>
public class Location
{
    [JsonProperty("uri")]
    public Uri Uri { get; set; } = null!;

    [JsonProperty("range")]
    public Range Range { get; set; } = new();
}

/// <summary>A textual edit: replace <see cref="Range"/> with <see cref="NewText"/>.</summary>
public class TextEdit
{
    [JsonProperty("range")]
    public Range Range { get; set; } = new();

    [JsonProperty("newText")]
    public string NewText { get; set; } = "";
}

/// <summary>Edits across documents, keyed by document URI.</summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class WorkspaceEdit
{
    [JsonProperty("changes")]
    public Dictionary<string, TextEdit[]>? Changes { get; set; }
}

public class TextDocumentIdentifier
{
    [JsonProperty("uri")]
    public Uri Uri { get; set; } = null!;
}

/// <summary>A document identifier that also states which revision it refers to.</summary>
public class VersionedTextDocumentIdentifier : TextDocumentIdentifier
{
    [JsonProperty("version")]
    public int Version { get; set; }
}

public class TextDocumentItem
{
    [JsonProperty("uri")]
    public Uri Uri { get; set; } = null!;

    [JsonProperty("languageId")]
    public string LanguageId { get; set; } = "";

    [JsonProperty("version")]
    public int Version { get; set; }

    [JsonProperty("text")]
    public string Text { get; set; } = "";
}

/// <summary>Base of every request that names a document and a caret in it.</summary>
public class TextDocumentPositionParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;

    [JsonProperty("position")]
    public Position Position { get; set; } = new();
}

/// <summary>Markdown or plain text, tagged so the client knows which.</summary>
public class MarkupContent
{
    [JsonProperty("kind")]
    public string Kind { get; set; } = MarkupKind.PlainText;

    [JsonProperty("value")]
    public string Value { get; set; } = "";
}

/// <summary>Wire values for <see cref="MarkupContent.Kind"/>.</summary>
public static class MarkupKind
{
    public const string PlainText = "plaintext";
    public const string Markdown = "markdown";
}

/// <summary>A command the client can run; Lily# only ever names commands the extension registers.</summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class Command
{
    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("command")]
    public string CommandIdentifier { get; set; } = "";

    [JsonProperty("arguments")]
    public object[]? Arguments { get; set; }
}

#endregion

#region Lifecycle

/// <summary>The method names Lily#'s server handles. The spec fixes these strings.</summary>
public static class Methods
{
    public const string InitializeName = "initialize";
    public const string InitializedName = "initialized";
    public const string ShutdownName = "shutdown";
    public const string ExitName = "exit";

    public const string WorkspaceDidChangeConfigurationName = "workspace/didChangeConfiguration";

    public const string TextDocumentDidOpenName = "textDocument/didOpen";
    public const string TextDocumentDidChangeName = "textDocument/didChange";
    public const string TextDocumentDidCloseName = "textDocument/didClose";
    public const string TextDocumentDidSaveName = "textDocument/didSave";
    public const string TextDocumentPublishDiagnosticsName = "textDocument/publishDiagnostics";

    public const string TextDocumentCompletionName = "textDocument/completion";
    public const string TextDocumentHoverName = "textDocument/hover";
    public const string TextDocumentSignatureHelpName = "textDocument/signatureHelp";
    public const string TextDocumentDefinitionName = "textDocument/definition";
    public const string TextDocumentReferencesName = "textDocument/references";
    public const string TextDocumentDocumentHighlightName = "textDocument/documentHighlight";
    public const string TextDocumentDocumentSymbolName = "textDocument/documentSymbol";
    public const string TextDocumentFoldingRangeName = "textDocument/foldingRange";
    public const string TextDocumentFormattingName = "textDocument/formatting";
    public const string TextDocumentRenameName = "textDocument/rename";
    public const string TextDocumentCodeActionName = "textDocument/codeAction";
    public const string TextDocumentSemanticTokensFullName = "textDocument/semanticTokens/full";
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class InitializeParams
{
    [JsonProperty("processId")]
    public int? ProcessId { get; set; }

    [JsonProperty("rootPath")]
    public string? RootPath { get; set; }

    [JsonProperty("rootUri")]
    public Uri? RootUri { get; set; }

    /// <summary>Client-supplied settings; a Newtonsoft JObject at runtime.</summary>
    [JsonProperty("initializationOptions")]
    public object? InitializationOptions { get; set; }

    [JsonProperty("capabilities")]
    public object? Capabilities { get; set; }

    [JsonProperty("trace")]
    public string? Trace { get; set; }
}

public class InitializeResult
{
    [JsonProperty("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();
}

/// <summary>
/// What this server can do. Every member is optional and omitted when null —
/// an absent capability is how a server declines a feature.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class ServerCapabilities
{
    [JsonProperty("textDocumentSync")]
    public TextDocumentSyncOptions? TextDocumentSync { get; set; }

    [JsonProperty("completionProvider")]
    public CompletionOptions? CompletionProvider { get; set; }

    [JsonProperty("hoverProvider")]
    public bool? HoverProvider { get; set; }

    [JsonProperty("signatureHelpProvider")]
    public SignatureHelpOptions? SignatureHelpProvider { get; set; }

    [JsonProperty("definitionProvider")]
    public bool? DefinitionProvider { get; set; }

    [JsonProperty("referencesProvider")]
    public bool? ReferencesProvider { get; set; }

    [JsonProperty("documentHighlightProvider")]
    public bool? DocumentHighlightProvider { get; set; }

    [JsonProperty("documentSymbolProvider")]
    public bool? DocumentSymbolProvider { get; set; }

    [JsonProperty("codeActionProvider")]
    public CodeActionOptions? CodeActionProvider { get; set; }

    [JsonProperty("documentFormattingProvider")]
    public bool? DocumentFormattingProvider { get; set; }

    [JsonProperty("renameProvider")]
    public bool? RenameProvider { get; set; }

    [JsonProperty("foldingRangeProvider")]
    public bool? FoldingRangeProvider { get; set; }

    /// <summary>
    /// Named for the type, not the wire member, because that is how the
    /// server code has always spelled it — the JSON name is what matters.
    /// </summary>
    [JsonProperty("semanticTokensProvider")]
    public SemanticTokensOptions? SemanticTokensOptions { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class TextDocumentSyncOptions
{
    [JsonProperty("openClose")]
    public bool OpenClose { get; set; }

    [JsonProperty("change")]
    public TextDocumentSyncKind Change { get; set; }

    [JsonProperty("save")]
    public SaveOptions? Save { get; set; }
}

public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2,
}

public class SaveOptions
{
    [JsonProperty("includeText")]
    public bool IncludeText { get; set; }
}

#endregion

#region Document synchronization

public class DidOpenTextDocumentParams
{
    [JsonProperty("textDocument")]
    public TextDocumentItem TextDocument { get; set; } = null!;
}

public class DidChangeTextDocumentParams
{
    [JsonProperty("textDocument")]
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = null!;

    [JsonProperty("contentChanges")]
    public TextDocumentContentChangeEvent[] ContentChanges { get; set; } = [];
}

/// <summary>
/// One incremental change. <see cref="Range"/> is absent for a full-document
/// replacement, which is what makes the two sync kinds share a shape.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class TextDocumentContentChangeEvent
{
    [JsonProperty("range")]
    public Range? Range { get; set; }

    [JsonProperty("rangeLength")]
    public int? RangeLength { get; set; }

    [JsonProperty("text")]
    public string Text { get; set; } = "";
}

public class DidCloseTextDocumentParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class DidSaveTextDocumentParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;

    [JsonProperty("text")]
    public string? Text { get; set; }
}

public class DidChangeConfigurationParams
{
    /// <summary>The pushed settings tree; a Newtonsoft JObject at runtime.</summary>
    [JsonProperty("settings")]
    public object? Settings { get; set; }
}

#endregion

#region Diagnostics

public class PublishDiagnosticParams
{
    [JsonProperty("uri")]
    public Uri Uri { get; set; } = null!;

    [JsonProperty("diagnostics")]
    public Diagnostic[] Diagnostics { get; set; } = [];
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class Diagnostic
{
    [JsonProperty("range")]
    public Range Range { get; set; } = new();

    [JsonProperty("severity")]
    public DiagnosticSeverity? Severity { get; set; }

    /// <summary>The spec allows an integer or a string here.</summary>
    [JsonProperty("code")]
    public object? Code { get; set; }

    [JsonProperty("source")]
    public string? Source { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

#endregion

#region Completion

public class CompletionParams : TextDocumentPositionParams
{
    [JsonProperty("context")]
    public CompletionContext? Context { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class CompletionContext
{
    [JsonProperty("triggerKind")]
    public int TriggerKind { get; set; }

    [JsonProperty("triggerCharacter")]
    public string? TriggerCharacter { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class CompletionOptions
{
    [JsonProperty("triggerCharacters")]
    public string[]? TriggerCharacters { get; set; }

    [JsonProperty("resolveProvider")]
    public bool ResolveProvider { get; set; }
}

public class CompletionList
{
    [JsonProperty("isIncomplete")]
    public bool IsIncomplete { get; set; }

    [JsonProperty("items")]
    public CompletionItem[] Items { get; set; } = [];
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class CompletionItem
{
    [JsonProperty("label")]
    public string Label { get; set; } = "";

    [JsonProperty("kind")]
    public CompletionItemKind? Kind { get; set; }

    [JsonProperty("detail")]
    public string? Detail { get; set; }

    /// <summary>A plain string or a <see cref="MarkupContent"/>, per the spec.</summary>
    [JsonProperty("documentation")]
    public object? Documentation { get; set; }

    [JsonProperty("preselect")]
    public bool? Preselect { get; set; }

    [JsonProperty("sortText")]
    public string? SortText { get; set; }

    [JsonProperty("filterText")]
    public string? FilterText { get; set; }

    [JsonProperty("insertText")]
    public string? InsertText { get; set; }

    [JsonProperty("insertTextFormat")]
    public InsertTextFormat? InsertTextFormat { get; set; }

    [JsonProperty("textEdit")]
    public TextEdit? TextEdit { get; set; }

    [JsonProperty("additionalTextEdits")]
    public TextEdit[]? AdditionalTextEdits { get; set; }

    [JsonProperty("commitCharacters")]
    public string[]? CommitCharacters { get; set; }

    [JsonProperty("command")]
    public Command? Command { get; set; }

    [JsonProperty("data")]
    public object? Data { get; set; }
}

public enum CompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25,
}

public enum InsertTextFormat
{
    Plaintext = 1,
    Snippet = 2,
}

#endregion

#region Hover, signature help, symbols

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class Hover
{
    [JsonProperty("contents")]
    public MarkupContent Contents { get; set; } = new();

    [JsonProperty("range")]
    public Range? Range { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class SignatureHelpOptions
{
    [JsonProperty("triggerCharacters")]
    public string[]? TriggerCharacters { get; set; }

    [JsonProperty("retriggerCharacters")]
    public string[]? RetriggerCharacters { get; set; }
}

public class SignatureHelpParams : TextDocumentPositionParams
{
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class SignatureHelp
{
    [JsonProperty("signatures")]
    public SignatureInformation[] Signatures { get; set; } = [];

    [JsonProperty("activeSignature")]
    public int? ActiveSignature { get; set; }

    [JsonProperty("activeParameter")]
    public int? ActiveParameter { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class SignatureInformation
{
    [JsonProperty("label")]
    public string Label { get; set; } = "";

    [JsonProperty("documentation")]
    public string? Documentation { get; set; }

    [JsonProperty("parameters")]
    public ParameterInformation[]? Parameters { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class ParameterInformation
{
    [JsonProperty("label")]
    public string Label { get; set; } = "";

    [JsonProperty("documentation")]
    public string? Documentation { get; set; }
}

public class DocumentSymbolParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

/// <summary>
/// A symbol in the outline. <see cref="Range"/> covers the whole construct;
/// <see cref="SelectionRange"/> is the part to reveal when it is picked.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class DocumentSymbol
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("detail")]
    public string? Detail { get; set; }

    [JsonProperty("kind")]
    public SymbolKind Kind { get; set; }

    [JsonProperty("range")]
    public Range Range { get; set; } = new();

    [JsonProperty("selectionRange")]
    public Range SelectionRange { get; set; } = new();

    [JsonProperty("children")]
    public DocumentSymbol[]? Children { get; set; }
}

public enum SymbolKind
{
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26,
}

#endregion

#region Navigation and editing

public class ReferenceParams : TextDocumentPositionParams
{
    [JsonProperty("context")]
    public ReferenceContext? Context { get; set; }
}

public class ReferenceContext
{
    [JsonProperty("includeDeclaration")]
    public bool IncludeDeclaration { get; set; }
}

public class RenameParams : TextDocumentPositionParams
{
    [JsonProperty("newName")]
    public string NewName { get; set; } = "";
}

public class DocumentHighlightParams : TextDocumentPositionParams
{
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class DocumentHighlight
{
    [JsonProperty("range")]
    public Range Range { get; set; } = new();

    [JsonProperty("kind")]
    public DocumentHighlightKind? Kind { get; set; }
}

public enum DocumentHighlightKind
{
    Text = 1,
    Read = 2,
    Write = 3,
}

public class DocumentFormattingParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;

    [JsonProperty("options")]
    public FormattingOptions? Options { get; set; }
}

public class FormattingOptions
{
    [JsonProperty("tabSize")]
    public int TabSize { get; set; }

    [JsonProperty("insertSpaces")]
    public bool InsertSpaces { get; set; }
}

public class FoldingRangeParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class FoldingRange
{
    [JsonProperty("startLine")]
    public int StartLine { get; set; }

    [JsonProperty("startCharacter")]
    public int? StartCharacter { get; set; }

    [JsonProperty("endLine")]
    public int EndLine { get; set; }

    [JsonProperty("endCharacter")]
    public int? EndCharacter { get; set; }

    [JsonProperty("kind")]
    public string? Kind { get; set; }
}

/// <summary>Wire values for <see cref="FoldingRange.Kind"/>.</summary>
public static class FoldingRangeKind
{
    public const string Comment = "comment";
    public const string Imports = "imports";
    public const string Region = "region";
}

public class CodeActionParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;

    [JsonProperty("range")]
    public Range Range { get; set; } = new();

    [JsonProperty("context")]
    public CodeActionContext? Context { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class CodeActionContext
{
    [JsonProperty("diagnostics")]
    public Diagnostic[]? Diagnostics { get; set; }

    [JsonProperty("only")]
    public string[]? Only { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class CodeAction
{
    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("kind")]
    public string? Kind { get; set; }

    [JsonProperty("diagnostics")]
    public Diagnostic[]? Diagnostics { get; set; }

    [JsonProperty("edit")]
    public WorkspaceEdit? Edit { get; set; }

    [JsonProperty("command")]
    public Command? Command { get; set; }

    [JsonProperty("data")]
    public object? Data { get; set; }
}

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class CodeActionOptions
{
    [JsonProperty("codeActionKinds")]
    public string[]? CodeActionKinds { get; set; }

    [JsonProperty("resolveProvider")]
    public bool? ResolveProvider { get; set; }
}

/// <summary>
/// Wire values for <see cref="CodeAction.Kind"/>. Hierarchical: a client
/// filtering on "refactor" also matches "refactor.extract".
/// </summary>
public static class CodeActionKind
{
    public const string Empty = "";
    public const string QuickFix = "quickfix";
    public const string Refactor = "refactor";
    public const string RefactorExtract = "refactor.extract";
    public const string RefactorInline = "refactor.inline";
    public const string RefactorRewrite = "refactor.rewrite";
    public const string Source = "source";
    public const string SourceOrganizeImports = "source.organizeImports";
}

#endregion

#region Semantic tokens

[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class SemanticTokensOptions
{
    [JsonProperty("legend")]
    public SemanticTokensLegend Legend { get; set; } = new();

    [JsonProperty("range")]
    public bool? Range { get; set; }

    [JsonProperty("full")]
    public bool Full { get; set; }
}

public class SemanticTokensLegend
{
    [JsonProperty("tokenTypes")]
    public string[] TokenTypes { get; set; } = [];

    [JsonProperty("tokenModifiers")]
    public string[] TokenModifiers { get; set; } = [];
}

public class SemanticTokensParams
{
    [JsonProperty("textDocument")]
    public TextDocumentIdentifier TextDocument { get; set; } = null!;
}

/// <summary>
/// The flat token stream: five integers per token (deltaLine, deltaStart,
/// length, tokenType, tokenModifiers), each row relative to the previous one.
/// </summary>
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class SemanticTokens
{
    [JsonProperty("resultId")]
    public string? ResultId { get; set; }

    [JsonProperty("data")]
    public int[] Data { get; set; } = [];
}

/// <summary>The standard token type names Lily# uses in its legend.</summary>
public static class SemanticTokenTypes
{
    public const string Comment = "comment";
    public const string Keyword = "keyword";
    public const string Number = "number";
    public const string Operator = "operator";
    public const string String = "string";
    public const string Variable = "variable";
}

#endregion
