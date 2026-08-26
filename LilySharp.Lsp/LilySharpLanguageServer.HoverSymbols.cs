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

    // The hover text lives in LanguageReference (ONE HOME with the signature
    // table, so the two features cannot drift apart on the same construct).
    private static string? GetHoverContent(SyntaxNode node)
        => LanguageReference.Hover(node);

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

            VariableDeclarationSyntax variable => (variable.Name.Text, SymbolKind.Variable),
            PhraseDeclarationSyntax phrase => ($"phrase {phrase.Name.Text}", SymbolKind.Function),
            SectionDeclarationSyntax section => ($"section {section.SectionName}", SymbolKind.Namespace),
            FormDeclarationSyntax => ("form", SymbolKind.Struct),
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
        return WhitespaceRunRegex().Replace(raw.Trim(), " ");
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

}
