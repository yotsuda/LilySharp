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
        var names = NameTokensOf(doc.Tree);

        if (OccurrencesAmong(names.Parts, offset) is { Count: > 0 } parts)
            return parts;
        if (OccurrencesAmong(names.Sections, offset) is { Count: > 0 } sections)
            return sections;
        if (OccurrencesAmong(names.Forms, offset) is { Count: > 0 } forms)
            return forms;
        if (OccurrencesAmong(names.Lyrics, offset) is { Count: > 0 } lyrics)
            return lyrics;
        if (OccurrencesAmong(names.Chords, offset) is { Count: > 0 } chords)
            return chords;

        // Phrase / legacy variable: declaration + every bare reference.
        var root = doc.Tree.GetRoot();
        var node = doc.Tree.FindNode(offset);
        if (node != null && FindVariableNameAt(node) is { } variableName)
        {
            var toks = new List<SyntaxNode>();
            ForEachOccurrence(root, variableName, (nameNode, _) => toks.Add(nameNode));
            return toks;
        }

        return Array.Empty<SyntaxNode>();
    }

    /// <summary>The name tokens of a tree, one list per namespace, in the order
    /// <see cref="SymbolOccurrences"/> tries them.</summary>
    private sealed record NameTokens(
        IReadOnlyList<SyntaxTokenNode> Parts,
        IReadOnlyList<SyntaxTokenNode> Sections,
        IReadOnlyList<SyntaxTokenNode> Forms,
        IReadOnlyList<SyntaxTokenNode> Lyrics,
        IReadOnlyList<SyntaxTokenNode> Chords);

    /// <summary>
    /// The five name-token lists of a tree, built once per TREE instance.
    /// </summary>
    /// <remarks>
    /// Document Highlight is asked on every caret movement, and each ask used to
    /// walk the whole tree five times over — every node materialized as a red node
    /// each time — before it knew whether the caret was on a name at all, which on a
    /// long score is the same work as the outline for every arrow key (owner report
    /// 2026-09-04: the caret itself felt sluggish). The lists depend on nothing but
    /// the tree, and the document manager replaces the tree on every edit, so the
    /// entry is exact for as long as it exists and dies with the version that owned
    /// it — the same shape as <see cref="LineStartsCache"/>. Rename and Find All
    /// References read the same lists.
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxTree, NameTokens> NameTokensCache = new();

    private static NameTokens NameTokensOf(SyntaxTree tree) =>
        NameTokensCache.GetValue(tree, static t =>
        {
            var root = t.GetRoot();
            return new NameTokens(
                PartReferenceFinder.AllPartNameTokens(root),
                SectionReferenceFinder.AllSectionNameTokens(root),
                FormNameTokens(root).ToList(),
                LyricsNameTokens(root).ToList(),
                ChordNameTokens(root).ToList());
        });

    /// <summary>If <paramref name="offset"/> lands on a token in
    /// <paramref name="tokens"/> (end inclusive, so a caret just past the name still
    /// resolves), every token there sharing its text; else empty.</summary>
    private static IReadOnlyList<SyntaxNode> OccurrencesAmong(IReadOnlyList<SyntaxTokenNode> tokens, int offset)
    {
        var hit = tokens.FirstOrDefault(t => offset >= t.Span.Start && offset <= t.Span.End);
        return hit == null
            ? Array.Empty<SyntaxNode>()
            : tokens.Where(t => t.Text == hit.Text).ToArray();
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
                if (CaseSpellingAction(doc, uri, diagnostic) is { } spelling)
                    actions.Add(spelling);
            }
        }

        // Semantic diagnostics (the same set the Problems panel shows — see
        // PublishDiagnosticsCore) carry their own quick fixes. Matched by OVERLAP with the
        // requested range, not by span start: the caret usually sits INSIDE the squiggle
        // (on a letter of the section name), where a start-only test finds nothing.
        // Guarded like the publish path: a validator crash must not take the lightbulb down.
        try
        {
            var semantic = DocumentDiagnostics(doc.Text, doc.Tree,
                uri.IsFile ? uri.LocalPath : string.Empty,
                p => System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : null).ToList();
            bool todoOffered = false;
            foreach (var diagnostic in semantic)
            {
                if (diagnostic.Span.Start > endOffset || diagnostic.Span.End < startOffset)
                    continue;
                if (BarCountPadding(doc.Tree, doc.Text, diagnostic) is { } pads)
                    actions.Add(PadBarsAction(doc, uri, diagnostic, pads));
                if (SplitSectionsAction(doc, uri, diagnostic) is { } split)
                    actions.Add(split);
                if (CaseSpellingAction(doc, uri, diagnostic) is { } spelling)
                    actions.Add(spelling);
                if (diagnostic.Code == DiagnosticCodes.TodoMark && !todoOffered)
                {
                    todoOffered = true;
                    actions.AddRange(OmrCandidateActions(doc, uri, diagnostic));
                    actions.AddRange(ResolveTodoActions(doc, uri, diagnostic, semantic));
                }
            }
        }
        catch
        {
            // Swallow: the syntax quick fixes above still answer.
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

    /// <summary>The command the "split to match" quick fix runs — the extension's, which asks
    /// the server for the plan (lilysharp/splitSections), shows it and applies it.</summary>
    internal const string SplitSectionsCommand = "lilysharp.splitSectionsToMatch";

    /// <summary>The second quick fix for an LYS2007 (owner's decision, 2026-09-28): when one part
    /// has cut the section into several and the others still write it whole, offer to cut the
    /// others the same way. Only OFFERED here, from the cheap part of the splitter (a parse
    /// and the bar counts); the plan is built, checked and confirmed when the author picks it.
    /// </summary>
    private static CodeAction? SplitSectionsAction(Document doc, Uri uri, CoreDiagnostic diagnostic)
    {
        if (diagnostic.Code != DiagnosticCodes.SectionBarCountMismatch)
            return null;
        var name = SectionOfWarning.Match(diagnostic.Message);
        if (!name.Success)
            return null;
        var offer = LilySharp.Core.Editing.SectionSplitter.FindOffers(doc.Text)
            .FirstOrDefault(o => o.Section == name.Groups[1].Value);
        if (offer == null)
            return null;
        string title = offer.NeedsChoice
            ? $"Split section {offer.Section} in the other parts to match a part…"
            : $"Split section {offer.Section} in the other parts to match {offer.Candidates[0].Part} "
                + $"({offer.Candidates[0].Describe()})…";
        return new CodeAction
        {
            Title = title,
            Kind = CodeActionKind.QuickFix,
            Diagnostics = [ConvertDiagnostic(diagnostic, doc.Text, uri)],
            Command = new Command
            {
                Title = title,
                CommandIdentifier = SplitSectionsCommand,
                Arguments = [uri.ToString(), offer.Section],
            },
        };
    }

    private static readonly System.Text.RegularExpressions.Regex SectionOfWarning =
        new(@"^Section '([^']+)' is not the same length", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The quick fix for a spelling that differs from a real one only in case (2026-09-29,
    /// HANDOFF §1.0): every validator that finds one says "… case-sensitive: write 'X'" —
    /// an annotation name (<c>@upbow</c> → <c>@upBow</c>), a value (<c>@ottava(BASSA)</c>),
    /// a chord shape word (<c>X32010</c>), a layout key or value — and this offers X for the
    /// squiggled text. ⚠️ ONLY when the squiggled text IS X in another case: the message is
    /// read, not the validator, and a span that covers more or less than the spelling would
    /// make an edit that says something else. That test is what keeps the offer honest
    /// without a second copy of each validator's span rule.
    /// </summary>
    private static CodeAction? CaseSpellingAction(Document doc, Uri uri, CoreDiagnostic diagnostic)
    {
        var m = CaseSpellingOfWarning.Match(diagnostic.Message);
        if (!m.Success)
            return null;
        string wanted = m.Groups[1].Value;
        int start = diagnostic.Span.Start, end = diagnostic.Span.End;
        if (start < 0 || end > doc.Text.Length || end <= start)
            return null;
        string written = doc.Text[start..end];
        if (written == wanted || !string.Equals(written, wanted, StringComparison.OrdinalIgnoreCase))
            return null;
        var (startLine, startChar) = GetLineAndCharacter(doc.Text, start);
        var (endLine, endChar) = GetLineAndCharacter(doc.Text, end);
        string title = $"Write '{wanted}'";
        return new CodeAction
        {
            Title = title,
            Kind = CodeActionKind.QuickFix,
            Diagnostics = [ConvertDiagnostic(diagnostic, doc.Text, uri)],
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]>
                {
                    [uri.ToString()] =
                    [
                        new TextEdit
                        {
                            Range = new LspRange
                            {
                                Start = new Position { Line = startLine, Character = startChar },
                                End = new Position { Line = endLine, Character = endChar },
                            },
                            NewText = wanted,
                        },
                    ],
                },
            },
        };
    }

    /// <summary>
    /// The quick fixes of a <c>@todo</c> mark (LYS4026, whose span is the mark itself, '@'
    /// included): "resolve" deletes it, and — when the file has more than one — "resolve all"
    /// deletes every one. A mark is a plain annotation, so deleting its text is the whole edit.
    /// </summary>
    private static IEnumerable<CodeAction> ResolveTodoActions(
        Document doc, Uri uri, CoreDiagnostic diagnostic, IReadOnlyList<CoreDiagnostic> all)
    {
        TextEdit Delete(CoreDiagnostic d)
        {
            var (sl, sc) = GetLineAndCharacter(doc.Text, d.Span.Start);
            var (el, ec) = GetLineAndCharacter(doc.Text, d.Span.End);
            return new TextEdit
            {
                Range = new LspRange
                {
                    Start = new Position { Line = sl, Character = sc },
                    End = new Position { Line = el, Character = ec },
                },
                NewText = "",
            };
        }
        bool IsMark(CoreDiagnostic d) => d.Code == DiagnosticCodes.TodoMark
            && d.Span.Start >= 0 && d.Span.End <= doc.Text.Length
            && doc.Text.AsSpan(d.Span.Start).StartsWith("@todo", StringComparison.Ordinal);

        if (!IsMark(diagnostic))
            yield break;
        yield return new CodeAction
        {
            Title = "Resolve this TODO (remove @todo)",
            Kind = CodeActionKind.QuickFix,
            Diagnostics = [ConvertDiagnostic(diagnostic, doc.Text, uri)],
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]> { [uri.ToString()] = [Delete(diagnostic)] },
            },
        };
        var marks = all.Where(IsMark).DistinctBy(d => d.Span.Start).ToList();
        if (marks.Count > 1)
            yield return new CodeAction
            {
                Title = $"Resolve all {marks.Count} TODOs in this file",
                Kind = CodeActionKind.QuickFix,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<string, TextEdit[]> { [uri.ToString()] = marks.Select(Delete).ToArray() },
                },
            };
    }

    /// <summary>
    /// The OMR reader's candidates for a <c>@todo(key …)</c> (LilySharp-Omr proposal B2): the
    /// side file <c>x.omr.json</c> beside <c>x.lys</c> lists, per key, what the mark's item
    /// might be — <c>todos[].candidates[] = { label, text }</c>, <c>text</c> being the item as
    /// it would be written without the mark. Each becomes a quick fix that writes it over the
    /// whole item, which removes the mark with it. The reader writes the file and Lily# only
    /// reads it; a missing or unreadable file, or a key it does not list, offers nothing.
    /// </summary>
    private static IEnumerable<CodeAction> OmrCandidateActions(Document doc, Uri uri, CoreDiagnostic diagnostic)
    {
        if (!uri.IsFile)
            return [];
        var mark = doc.Tree.GetRoot().DescendantNodes()
            .FirstOrDefault(n => n is ArticulationSyntax or MusicMarkSyntax && n.Span.Start == diagnostic.Span.Start);
        if (mark?.Parent is not { } host || TodoAnnotation.Of(mark) is not { Key: { } key })
            return [];
        var candidates = OmrSideFile.Candidates(System.IO.Path.ChangeExtension(uri.LocalPath, ".omr.json"), key);
        if (candidates.Count == 0)
            return [];
        var (sl, sc) = GetLineAndCharacter(doc.Text, host.Span.Start);
        var (el, ec) = GetLineAndCharacter(doc.Text, host.Span.End);
        var range = new LspRange
        {
            Start = new Position { Line = sl, Character = sc },
            End = new Position { Line = el, Character = ec },
        };
        return candidates.Select(c => new CodeAction
        {
            Title = c.Label is { Length: > 0 } label && label != c.Text
                ? $"Write {label}: {c.Text} (resolves the TODO)"
                : $"Write {c.Text} (resolves the TODO)",
            Kind = CodeActionKind.QuickFix,
            Diagnostics = [ConvertDiagnostic(diagnostic, doc.Text, uri)],
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]>
                {
                    [uri.ToString()] = [new TextEdit { Range = range, NewText = c.Text }],
                },
            },
        }).ToList();
    }

    /// <summary>The spelling a case hint names: "Names / Values / Keys are case-sensitive: write 'X'."</summary>
    private static readonly System.Text.RegularExpressions.Regex CaseSpellingOfWarning =
        new(@"case-sensitive: write '([^']+)'", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The quick fix for one LYS2007: insert the bare bar lines
    /// <see cref="BarCountPadding"/> settled on, at the end of each short layer's body — one
    /// action, one edit per layer.</summary>
    private static CodeAction PadBarsAction(
        Document doc, Uri uri, CoreDiagnostic diagnostic, IReadOnlyList<BarPad> pads)
    {
        TextEdit EditOf(BarPad pad)
        {
            var (line, character) = GetLineAndCharacter(doc.Text, pad.Offset);
            var at = new Position { Line = line, Character = character };
            return new TextEdit { Range = new LspRange { Start = at, End = at }, NewText = pad.Text };
        }

        // Counted in BAR LINES, not bars: over an open last bar the first `|` only closes it,
        // and the title must say what the edit writes. One short part or track keeps the old
        // title; several are named while they are few, and counted when they are many (the
        // warning's related list names every one).
        var one = pads[0];
        var voices = pads.OrderBy(p => p.Offset).Select(p => p.Voice).ToList();
        string title = pads.Count > 3
            ? $"Add bar lines to the {pads.Count} shorter parts and tracks"
            : pads.Count > 1
            ? $"Add bar lines to {string.Join(", ", voices.Take(voices.Count - 1))} and {voices[^1]}"
            : one.Bars == 1
                ? $"Add 1 bar line to {one.Voice} (|)"
                : $"Add {one.Bars} bar lines to {one.Voice} ({string.Join(" ", Enumerable.Repeat("|", one.Bars))})";
        return new CodeAction
        {
            Title = title,
            Kind = CodeActionKind.QuickFix,
            Diagnostics = [ConvertDiagnostic(diagnostic, doc.Text, uri)],
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<string, TextEdit[]>
                {
                    [uri.ToString()] = pads.Select(EditOf).ToArray(),
                },
            },
        };
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
                newText = $"form {{ {names} }}\n\n";
            }
            else
            {
                var last = sections[^1];
                offset = last.Position + last.FullWidth;
                newText = $"\nform {{ {names} }}\n";
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
