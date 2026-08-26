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
        foreach (var entry in LanguageReference.Signatures)
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

    // The signature table lives in LanguageReference (ONE HOME with the hover
    // text, each row carrying a compilable Sample of the grammar it advertises).

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
