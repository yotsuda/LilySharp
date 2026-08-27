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
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== Signature Help ==========

    [JsonRpcMethod(Methods.TextDocumentSignatureHelpName, UseSingleObjectParameterDeserialization = true)]
    public Task<SignatureHelp?> GetSignatureHelpAsync(SignatureHelpParams @params, CancellationToken token)
        => OffDispatch(() => GetSignatureHelp(@params), token);

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

        // Match against a CODE-ONLY view of the line: string contents and comments
        // blanked (StripStringsAndComments is length-preserving up to a // comment,
        // so an index into the code view is an index into the line). The trigger
        // character is ' ', so the old raw-substring match actually fired — typing
        // the space after `title "Ragtime"` summoned `time`'s signature from inside
        // the string, and `stem`/`system`-like words would summon `tempo`'s never
        // (2026-08-26 review, appendix F finding 7). Line-local approximation on
        // purpose: a line starting inside a /* block comment is treated as code,
        // exactly as the formatter's per-line callers do.
        bool inBlockComment = false;
        var codeText = StripStringsAndComments(lineText, ref inBlockComment);

        // First keyword on the line (in priority order) that has a signature wins.
        foreach (var entry in LanguageReference.Signatures)
        {
            int kw = IndexOfKeywordToken(codeText, entry.Keyword);
            if (kw < 0) continue;

            // Counted over the ORIGINAL text (not the code view): the blanking turns
            // a quoted marking into a run of spaces, which would merge the separators
            // around it and lose the argument; the walk below keeps a string literal
            // one token instead.
            int activeParameter = ActiveParameterOf(lineText[(kw + entry.Keyword.Length)..]);
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

    // The signature table lives in LanguageReference (ONE HOME with the hover
    // text, each row carrying a compilable Sample of the grammar it advertises).

    /// <summary>First occurrence of <paramref name="keyword"/> in the code view that
    /// stands as its own word — `title "Ragtime"` blanks to spaces so `time` cannot
    /// match inside it, and this guard keeps it from matching inside an identifier
    /// (`timeline`) either. −1 if the line has no such token.</summary>
    private static int IndexOfKeywordToken(string code, string keyword)
    {
        for (int at = code.IndexOf(keyword, StringComparison.Ordinal); at >= 0;
             at = code.IndexOf(keyword, at + 1, StringComparison.Ordinal))
        {
            bool startsWord = at == 0 || !IsWordChar(code[at - 1]);
            int end = at + keyword.Length;
            bool endsWord = end >= code.Length || !IsWordChar(code[end]);
            if (startsWord && endsWord) return at;
        }
        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The parameter index the caret sits on, for the text between the
    /// keyword and the caret: arguments are separated by whitespace RUNS outside
    /// string literals — a quoted marking with spaces inside is one argument, where
    /// the old per-character space count pushed activeParameter past the end on
    /// `tempo "Allegro con brio" …` (2026-08-26 review, appendix F finding 7). The
    /// first run (between the keyword and its first argument) starts the count at
    /// parameter 0; the caller clamps to the signature's parameter count.</summary>
    private static int ActiveParameterOf(string afterKeyword)
    {
        int separators = 0;
        bool inString = false;
        bool inSeparator = false;
        foreach (var c in afterKeyword)
        {
            if (inString)
            {
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"')
            {
                inString = true;
                inSeparator = false;
                continue;
            }
            if (c == ' ' || c == '\t')
            {
                if (!inSeparator)
                {
                    separators++;
                    inSeparator = true;
                }
                continue;
            }
            inSeparator = false;
        }
        return Math.Max(0, separators - 1);
    }

    // ========== Document Highlight ==========

    [JsonRpcMethod(Methods.TextDocumentDocumentHighlightName, UseSingleObjectParameterDeserialization = true)]
    public Task<DocumentHighlight[]?> GetDocumentHighlightAsync(DocumentHighlightParams @params, CancellationToken token)
        => OffDispatch(() => GetDocumentHighlight(@params), token);

    public DocumentHighlight[]? GetDocumentHighlight(DocumentHighlightParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var offset = GetOffset(doc.Text, position.Line, position.Character);

        // Every occurrence of the symbol at the caret, across all named namespaces
        // (the same model Rename and Find All References use). A declaration is a
        // Write highlight, a reference a Read highlight.
        var occurrences = SymbolOccurrences(doc, offset);
        if (occurrences.Count == 0) return null;

        var highlights = new List<DocumentHighlight>();
        foreach (var token in occurrences)
        {
            var (line, character) = GetLineAndCharacter(doc.Text, token.Span.Start);
            highlights.Add(new DocumentHighlight
            {
                Range = new LspRange
                {
                    Start = new Position { Line = line, Character = character },
                    End = new Position { Line = line, Character = character + token.Width }
                },
                Kind = IsDeclarationToken(token) ? DocumentHighlightKind.Write : DocumentHighlightKind.Read
            });
        }

        return highlights.ToArray();
    }

}
