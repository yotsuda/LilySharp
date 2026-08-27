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
    // ========== Rename ==========

    [JsonRpcMethod(Methods.TextDocumentRenameName, UseSingleObjectParameterDeserialization = true)]
    public Task<WorkspaceEdit?> RenameAsync(RenameParams @params, CancellationToken token)
        => OffDispatch(() => Rename(@params), token);

    public WorkspaceEdit? Rename(RenameParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var position = @params.Position;
        var newName = @params.NewName;
        int offset = GetOffset(doc.Text, position.Line, position.Character);

        // Every occurrence (declaration + references) of the symbol the caret sits
        // on, across all named namespaces. Empty when the caret is not on a rename-
        // able name — return null so the client shows "cannot rename" rather than a
        // no-op edit.
        var occurrences = SymbolOccurrences(doc, offset);
        if (occurrences.Count == 0) return null;

        var edits = occurrences.Select(t => TokenTextEdit(doc.Text, t, newName)).ToArray();
        return new WorkspaceEdit
        {
            Changes = new Dictionary<string, TextEdit[]>
            {
                [uri.ToString()] = edits
            }
        };
    }

    /// <summary>
    /// Every occurrence token (declaration + references) of the symbol at
    /// <paramref name="offset"/>. Shared by Rename (rewrite all), Find All References
    /// (list them), and Document Highlight (mark them). The namespace dispatch and
    /// its reference model mirror Go to Definition's
    /// <see cref="ResolveDefinitionTarget"/>, so navigation, rename, and reference
    /// search never disagree about what a name means at a position:
    /// <list type="bullet">
    /// <item><c>part</c> (declaration + section-body part blocks + score staff/ossia/
    /// tab/midi targets) — via <see cref="PartReferenceFinder"/>;</item>
    /// <item><c>section</c> (declaration + structure/form references, silent + volta)
    /// — via <see cref="SectionReferenceFinder"/>;</item>
    /// <item><c>form</c> (declaration + <c>score NAME</c> references);</item>
    /// <item><c>lyrics</c> / <c>chords</c> block (declaration + <c>with …</c> clauses
    /// and rows);</item>
    /// <item><c>phrase</c> / legacy variable (declaration + bare references).</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<SyntaxNode> SymbolOccurrences(Document doc, int offset)
    {
        var root = doc.Tree.GetRoot();

        if (PartReferenceFinder.PartNameTokenAt(root, offset) is { } partTok)
            return PartReferenceFinder.Occurrences(root, partTok.Text);

        if (SectionReferenceFinder.SectionNameTokenAt(root, offset) is { } sectionTok)
            return SectionReferenceFinder.Occurrences(root, sectionTok.Text);

        if (OccurrencesAmong(FormNameTokens(root), offset) is { Count: > 0 } forms)
            return forms;
        if (OccurrencesAmong(LyricsNameTokens(root), offset) is { Count: > 0 } lyrics)
            return lyrics;
        if (OccurrencesAmong(ChordNameTokens(root), offset) is { Count: > 0 } chords)
            return chords;

        // Phrase / legacy variable: declaration + every bare reference.
        var node = doc.Tree.FindNode(offset);
        if (node != null && FindVariableNameAt(node) is { } variableName)
        {
            var toks = new List<SyntaxNode>();
            ForEachOccurrence(root, variableName, (nameNode, _) => toks.Add(nameNode));
            return toks;
        }

        return Array.Empty<SyntaxNode>();
    }

    /// <summary>If <paramref name="offset"/> lands on a token in
    /// <paramref name="tokens"/>, every token there sharing its text; else empty.
    /// <paramref name="tokens"/> is enumerated once.</summary>
    private static IReadOnlyList<SyntaxNode> OccurrencesAmong(IEnumerable<SyntaxTokenNode> tokens, int offset)
    {
        var all = tokens.ToList();
        var hit = all.FirstOrDefault(t => offset >= t.Span.Start && offset <= t.Span.End);
        return hit == null
            ? Array.Empty<SyntaxNode>()
            : all.Where(t => t.Text == hit.Text).ToArray();
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
    public Task<TextEdit[]?> FormatAsync(DocumentFormattingParams @params, CancellationToken token)
        => OffDispatch(() => Format(@params), token);

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
        bool inBlockComment = false; // /* … */ carries across lines

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r').Trim();

            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine();
                continue;
            }

            // Drive indentation off a code-only view (string literals and comments
            // blanked out) so a brace inside "…" or a // / /* */ comment doesn't
            // corrupt the depth — and a real brace hidden behind a trailing comment
            // still counts.
            var codeLine = StripStringsAndComments(line, ref inBlockComment).Trim();

            // Adjust depth for closing braces at start of line
            if (codeLine.StartsWith('}') || codeLine.StartsWith(">>"))
            {
                depth = Math.Max(0, depth - 1);
            }

            // Write indented line (the ORIGINAL text, strings/comments intact)
            var indent = string.Concat(Enumerable.Repeat(indentStr, depth));
            sb.AppendLine($"{indent}{line}");

            // Adjust depth for opening braces at end of line
            if (codeLine.EndsWith('{') || codeLine.EndsWith("<<"))
            {
                depth++;
            }
            // Handle inline open (e.g., "} else {")
            else if (codeLine.Contains('{') && !codeLine.Contains('}'))
            {
                depth++;
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns <paramref name="line"/> with string literals and comments replaced by
    /// spaces, so brace-based indentation logic sees only code. Strings are
    /// line-bounded; a <c>/* … */</c> block comment carries across lines via
    /// <paramref name="inBlockComment"/>.
    /// </summary>
    internal static string StripStringsAndComments(string line, ref bool inBlockComment)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inBlockComment)
            {
                if (c == '/' && i > 0 && line[i - 1] == '*') inBlockComment = false;
                sb.Append(' ');
                continue;
            }
            if (inString)
            {
                if (c == '"') inString = false;
                sb.Append(' ');
                continue;
            }
            if (c == '"') { inString = true; sb.Append(' '); continue; }
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                break; // rest of the line is a // comment; the code before it is kept
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                inBlockComment = true;
                sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ========== Code Actions ==========

    [JsonRpcMethod(Methods.TextDocumentCodeActionName, UseSingleObjectParameterDeserialization = true)]
    public Task<CodeAction[]?> GetCodeActionsAsync(CodeActionParams @params, CancellationToken token)
        => OffDispatch(() => GetCodeActions(@params), token);

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
        if (sections.Count > 0 && !doc.Tree.GetNodes<FormDeclarationSyntax>().Any())
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
                newText = $"form main {{ {names} }}\n\n";
            }
            else
            {
                var last = sections[^1];
                offset = last.Position + last.FullWidth;
                newText = $"\nform main {{ {names} }}\n";
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
                                NewText = "melody"
                            }
                        }
                    }
                }
            });
        }


        return actions;
    }

}
