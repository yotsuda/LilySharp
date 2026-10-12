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
            // By OVERLAP, like the semantic ones below: the caret sits inside the squiggle
            // (on a letter of `score "tab"`'s name) more often than on its first character.
            if (diagnostic.Span.Start <= endOffset && diagnostic.Span.End >= startOffset)
            {
                // Generate quick fixes based on diagnostic
                var fixes = GenerateQuickFixes(doc, diagnostic, uri);
                actions.AddRange(fixes);
                if (CaseSpellingAction(doc, uri, diagnostic) is { } spelling)
                    actions.Add(spelling);
                if (ScoreNameQuotedAction(doc, uri, diagnostic) is { } bareName)
                    actions.Add(bareName);
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
                // A todo's report underlines its memo only; its fixes answer anywhere on the
                // mark, '@' to ')' — Ctrl+. on the word `todo` resolves it too.
                var reach = diagnostic.Code == DiagnosticCodes.TodoMark && TodoMarkAt(doc.Tree, diagnostic) is { } todoMark
                    ? todoMark.Span : diagnostic.Span;
                if (reach.Start > endOffset || reach.End < startOffset)
                    continue;
                if (BarCountPadding(doc.Tree, doc.Text, diagnostic) is { } pads)
                    actions.Add(PadBarsAction(doc, uri, diagnostic, pads));
                if (SplitSectionsAction(doc, uri, diagnostic) is { } split)
                    actions.Add(split);
                if (MissingScoreAction(doc, uri, diagnostic) is { } score)
                    actions.Add(score);
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

    /// <summary>
    /// The quick fix for LYS0038, a score's name written as a quoted string: <c>score "tab"</c>
    /// becomes <c>score tab</c> (the words joined camelCase when the text is not one name —
    /// <see cref="RenderDeclarationSyntax.QuotedNameAsBareWord"/>, the word the error names), and the string beside
    /// a bare name (<c>score tab "both"</c>) is deleted, the bare name being the score's.
    /// </summary>
    private static CodeAction? ScoreNameQuotedAction(Document doc, Uri uri, CoreDiagnostic diagnostic)
    {
        if (diagnostic.Code != DiagnosticCodes.ScoreNameQuoted)
            return null;
        var render = doc.Tree.GetRoot().ChildNodes().OfType<RenderDeclarationSyntax>()
            .FirstOrDefault(r => r.QuotedName is { } q && q.Span.Start == diagnostic.Span.Start);
        if (render?.QuotedName is not { } quoted)
            return null;
        int start = quoted.Span.Start, end = quoted.Span.End;
        string newText, title;
        if (render.ScoreName is { } bare)
        {
            start = bare.Span.End;   // with the space before the string
            newText = "";
            title = $"Delete the quoted name — the score is '{bare.Text}'";
        }
        else
        {
            newText = render.QuotedNameAsBareWord ?? "";
            if (newText.Length == 0)
                return null;
            title = $"Write 'score {newText}'";
        }
        return QuickFix(title, doc, uri, diagnostic, EditOf(doc.Text, start, end, newText));
    }

    /// <summary>
    /// The quick fix for LYS6014, a book with music and no score: append
    /// <see cref="LilySharp.Core.Editing.ScoreSkeleton"/>'s score — every chord track, a staff per
    /// part in declaration order, every lyrics track — at the end of the file, in its line endings.
    /// </summary>
    private static CodeAction? MissingScoreAction(Document doc, Uri uri, CoreDiagnostic diagnostic)
    {
        if (diagnostic.Code != DiagnosticCodes.MissingScore)
            return null;
        string score = LilySharp.Core.Editing.ScoreSkeleton.For(doc.Tree.GetRoot());
        string eol = doc.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string lead = doc.Text.Length == 0 || doc.Text.EndsWith('\n') ? eol : eol + eol;
        return QuickFix($"Add {score}", doc, uri, diagnostic,
            EditOf(doc.Text, doc.Text.Length, doc.Text.Length, lead + score + eol));
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
        return QuickFix($"Write '{wanted}'", doc, uri, diagnostic, EditOf(doc.Text, start, end, wanted));
    }

    /// <summary>
    /// The quick fixes of a <c>@todo</c> mark (LYS4026, whose span is the mark itself, '@'
    /// included): "resolve" deletes it, and — when the file has more than one — "resolve all"
    /// deletes every one. A mark is a plain annotation, so deleting its text is the whole edit.
    /// </summary>
    private static IEnumerable<CodeAction> ResolveTodoActions(
        Document doc, Uri uri, CoreDiagnostic diagnostic, IReadOnlyList<CoreDiagnostic> all)
    {
        // The report underlines what the mark says (TodoValidator.MessageSpan), so the edit
        // deletes the MARK the report sits in, '@' to ')'.
        TextEdit Delete(CoreDiagnostic d) => TodoMarkAt(doc.Tree, d) is { } mark
            ? EditOf(doc.Text, mark.Span.Start, mark.Span.End, "")
            : EditOf(doc.Text, d.Span.Start, d.Span.Start, "");
        bool IsMark(CoreDiagnostic d) => d.Code == DiagnosticCodes.TodoMark && TodoMarkAt(doc.Tree, d) != null;

        if (!IsMark(diagnostic))
            yield break;
        yield return QuickFix("Resolve this TODO (remove @todo)", doc, uri, diagnostic, Delete(diagnostic));
        var marks = all.Where(IsMark).DistinctBy(d => d.Span.Start).ToList();
        if (marks.Count > 1)
            yield return new CodeAction
            {
                Title = $"Resolve all {marks.Count} TODOs in this file",
                Kind = CodeActionKind.QuickFix,
                Edit = WorkspaceEditOf(uri, [.. marks.Select(Delete)]),
            };
    }

    /// <summary>The <c>@todo</c> mark a LYS4026 report sits in — the report underlines the
    /// memo, the key or the word, so it is found by CONTAINMENT, not by where it starts
    /// (TodoValidator.MessageSpan). Null when none holds it.</summary>
    /// <summary>Every <c>@todo</c> mark of the tree with its key (null for one with none), for
    /// matching many LYS4026 reports at once by containment, as <see cref="TodoMarkAt"/> does one.</summary>
    private static List<(int Start, int End, string? Key)> TodoMarksOf(SyntaxTree tree)
        => [.. tree.GetRoot().DescendantNodes()
            .Where(n => n is ArticulationSyntax or MusicMarkSyntax)
            .Select(n => (Node: n, Todo: TodoAnnotation.Of(n)))
            .Where(x => x.Todo != null)
            .Select(x => (x.Node.Span.Start, x.Node.Span.End, x.Todo!.Key))];

    private static SyntaxNode? TodoMarkAt(SyntaxTree tree, CoreDiagnostic diagnostic)
        => tree.GetRoot().DescendantNodes().FirstOrDefault(n =>
            n is ArticulationSyntax or MusicMarkSyntax
            && n.Span.Start <= diagnostic.Span.Start && diagnostic.Span.End <= n.Span.End
            && TodoAnnotation.Of(n) != null);

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
        var mark = TodoMarkAt(doc.Tree, diagnostic);
        if (mark?.Parent is not { } host || TodoAnnotation.Of(mark) is not { Key: { } key })
            return [];
        var candidates = OmrSideFile.Candidates(System.IO.Path.ChangeExtension(uri.LocalPath, ".omr.json"), key);
        if (candidates.Count == 0)
            return [];
        return candidates.Select(c => QuickFix(
            c.Label is { Length: > 0 } label && label != c.Text
                ? $"Write {label}: {c.Text} (resolves the TODO)"
                : $"Write {c.Text} (resolves the TODO)",
            doc, uri, diagnostic, EditOf(doc.Text, host.Span.Start, host.Span.End, c.Text))).ToList();
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
        return QuickFix(title, doc, uri, diagnostic,
            [.. pads.Select(p => EditOf(doc.Text, p.Offset, p.Offset, p.Text))]);
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
            actions.Add(new CodeAction
            {
                Title = "Insert rest (r4)",
                Kind = CodeActionKind.QuickFix,
                Edit = WorkspaceEditOf(uri, EditOf(doc.Text, diagnostic.Span.Start, diagnostic.Span.End, "r4")),
            });
        }

        // Fix: Unclosed brace
        if (message.Contains("Expected '}'") || message.Contains("unclosed"))
        {
            // At the end of the text, before a trailing CR.
            int end = doc.Text.TrimEnd('\r').Length;
            actions.Add(new CodeAction
            {
                Title = "Add closing brace",
                Kind = CodeActionKind.QuickFix,
                Edit = WorkspaceEditOf(uri, EditOf(doc.Text, end, end, "\n}")),
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
            actions.Add(new CodeAction
            {
                Title = "Insert structure declaration",
                Kind = CodeActionKind.Refactor,
                Edit = WorkspaceEditOf(uri, EditOf(doc.Text, offset, offset, newText)),
            });
        }

        // Refactor: Extract variable from music block
        if (node is MusicBlockSyntax block && block.Items.Any())
        {
            var blockText = block.ToFullString().Trim();
            actions.Add(new CodeAction
            {
                Title = "Extract to phrase",
                Kind = CodeActionKind.Refactor,
                Edit = WorkspaceEditOf(uri,
                    // Insert a phrase declaration at the top. blockText already
                    // includes the braces, so `phrase melody { … }` is well-formed.
                    // (`let name = …` was removed from the grammar.)
                    EditOf(doc.Text, 0, 0, $"phrase melody {blockText}\n\n"),
                    // Replace block with a phrase reference
                    EditOf(doc.Text, block.Position, block.Position + block.FullWidth, "melody")),
            });
        }


        return actions;
    }

    /// <summary>The edit that writes <paramref name="newText"/> over the text offsets
    /// [<paramref name="start"/>, <paramref name="end"/>) — an insertion when they are equal.</summary>
    private static TextEdit EditOf(string text, int start, int end, string newText)
    {
        var (startLine, startChar) = GetLineAndCharacter(text, start);
        var (endLine, endChar) = GetLineAndCharacter(text, end);
        return new TextEdit
        {
            Range = new LspRange
            {
                Start = new Position { Line = startLine, Character = startChar },
                End = new Position { Line = endLine, Character = endChar },
            },
            NewText = newText,
        };
    }

    /// <summary>The workspace edit that applies <paramref name="edits"/> to the one document.</summary>
    private static WorkspaceEdit WorkspaceEditOf(Uri uri, params TextEdit[] edits)
        => new() { Changes = new Dictionary<string, TextEdit[]> { [uri.ToString()] = edits } };

    /// <summary>A quick fix for <paramref name="diagnostic"/>: the action every diagnostic-bound
    /// fix here builds, titled, tied to its diagnostic, and applying <paramref name="edits"/>.</summary>
    private static CodeAction QuickFix(
        string title, Document doc, Uri uri, CoreDiagnostic diagnostic, params TextEdit[] edits) => new()
    {
        Title = title,
        Kind = CodeActionKind.QuickFix,
        Diagnostics = [ConvertDiagnostic(diagnostic, doc.Text, uri)],
        Edit = WorkspaceEditOf(uri, edits),
    };

}
