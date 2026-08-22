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
            new[] { ("Grob.property", "Grob name and property (e.g., NoteHead.color, Stem.transparent)"),
                    ("value", "New value (number, string, or identifier)") }),
        new("phrase", "phrase name { music }",
            "Declares a reusable musical phrase. Reference with name.",
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
