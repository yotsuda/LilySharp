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

using System;
using System.IO;
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The two <c>template-…</c> completions put a WHOLE FILE into the editor, and that is a
/// different act from every other completion in the list.
/// </summary>
/// <remarks>
/// ⚠️ Until 2026-08-23 they were ordinary snippets: accepting one dropped a complete second
/// piece — its own <c>title</c>, <c>tempo</c>, <c>form main</c>, <c>score main</c> — at the
/// caret of whatever the writer already had. Nothing was red. Every net in this suite reads
/// the completion LIST (is the label offered? is it offered in the right context?) and none
/// read what accepting one DOES to the document, so the only observer was the writer's eye.
/// <para>
/// The fix moves the work to the editor: the item accepts as a no-op edit and carries the
/// command <c>lilysharp.applyScoreTemplate</c>, which asks before it clears the file. That
/// splits the behaviour across two languages — the C# item and the TypeScript handler — so
/// the tests below pin BOTH halves and the seam between them (the command name, which is
/// the only thing the two halves share and nothing else would catch drifting).
/// </para>
/// ⚠️ What is NOT observed here: the dialog itself, and the replacement it performs. Those
/// live in <c>editors/vscode/src/extension.ts</c>, which no test in this repo executes.
/// What can be pinned from this side is that the item hands over everything the handler
/// needs and changes nothing on its own — and that is what these do.
/// </remarks>
[Trait("Category", "Unit")]
public class ScoreTemplateCompletionTests
{
    /// <summary>The command the items name. Written out rather than read from the server so
    /// a rename has to be made in two places on purpose — this constant is one half of the
    /// seam with the extension, and the extension is the other.</summary>
    private const string ApplyCommand = "lilysharp.applyScoreTemplate";

    public static TheoryData<string> TemplateLabels => new()
    {
        "template-twinkle",
        "template-twinkle-piano",
    };

    /// <summary>The template item as the server builds it for a caret at the end of
    /// <paramref name="text"/> (the position is what lets it build a range).</summary>
    private static CompletionItem ItemIn(string label, string text)
    {
        int line = 0, character = 0;
        foreach (char c in text)
        {
            if (c == '\n') { line++; character = 0; }
            else character++;
        }

        return LilySharpLanguageServer
            .GetTopLevelCompletions(text, text.Length, new Position(line, character))
            .Items.Single(i => i.Label == label);
    }

    /// <summary>The document after the client applies <paramref name="edit"/> — the LSP
    /// contract for accepting a completion item that carries a textEdit.</summary>
    private static string ApplyEdit(string text, TextEdit edit)
    {
        static int Offset(string s, Position p)
        {
            int line = 0, i = 0;
            while (line < p.Line)
            {
                i = s.IndexOf('\n', i) + 1;
                line++;
            }
            return i + p.Character;
        }

        int start = Offset(text, edit.Range.Start);
        int end = Offset(text, edit.Range.End);
        return text[..start] + edit.NewText + text[end..];
    }

    [Theory]
    [MemberData(nameof(TemplateLabels))]
    public void AcceptingATemplate_LeavesTheDocumentExactlyAsItWas(string label)
    {
        // The whole design rests on this: the editor asks "may I clear this file?" AFTER the
        // item has been accepted, so declining must be able to leave the writer with what
        // they had — including the word they typed to find the item. An item that inserted
        // "" instead would have eaten `temp` before the question was even asked.
        const string src = "title \"my piece\"\n\npart melody { section A { c4 } }\n\ntemp";
        var item = ItemIn(label, src);

        Assert.NotNull(item.TextEdit);
        Assert.Equal(src, ApplyEdit(src, item.TextEdit!));
    }

    [Theory]
    [MemberData(nameof(TemplateLabels))]
    public void ATemplateItem_NeverTypesItsOwnLabelOrItsText(string label)
    {
        // With no textEdit and no insertText the LSP says the LABEL is inserted, which would
        // type `template-twinkle` into a score. The empty insertText is the floor under that
        // for any client that ignores the range.
        const string src = "temp";
        var item = ItemIn(label, src);

        Assert.Equal("", item.InsertText);
        Assert.Equal(InsertTextFormat.Plaintext, item.InsertTextFormat);
        Assert.Equal("temp", item.TextEdit!.NewText);
    }

    [Theory]
    [MemberData(nameof(TemplateLabels))]
    public void ATemplateItem_HandsTheEditorTheTextAndTheWordItRetyped(string label)
    {
        const string src = "temp";
        var item = ItemIn(label, src);

        Assert.NotNull(item.Command);
        Assert.Equal(ApplyCommand, item.Command!.CommandIdentifier);

        var args = item.Command.Arguments!;
        Assert.Equal(6, args.Length);
        Assert.Equal(label, args[0]);

        // The text travels in the argument: the server is its one home, so a new template
        // needs no change in the extension.
        var template = Assert.IsType<string>(args[1]);
        Assert.Contains("score main", template);

        // The last four are the range the no-op edit rewrote. The editor subtracts it before
        // asking whether there is anything to lose — a fresh file where `temp` was typed to
        // find this item is empty, and must not be asked.
        Assert.Equal(item.TextEdit!.Range.Start.Line, args[2]);
        Assert.Equal(item.TextEdit.Range.Start.Character, args[3]);
        Assert.Equal(item.TextEdit.Range.End.Line, args[4]);
        Assert.Equal(item.TextEdit.Range.End.Character, args[5]);
    }

    [Theory]
    [MemberData(nameof(TemplateLabels))]
    public void ATemplateText_CarriesNoSnippetMarkers(string label)
    {
        // As a snippet the text ended in `$0` (where the caret was to land). The editor now
        // writes it as plain text, so a leftover marker would PRINT — and `$` is not a Lily#
        // token, so the file it produced would not even compile.
        var template = (string)ItemIn(label, "temp").Command!.Arguments![1];

        Assert.DoesNotContain("$0", template);
        Assert.DoesNotContain("${", template);
    }

    [Theory]
    [MemberData(nameof(TemplateLabels))]
    public void ATemplateText_IndentsLikeTheCorpusDoes(string label)
    {
        // ⚠️ These are the only completion items that are NOT snippets, and that is exactly
        // what makes this a test. VS Code re-indents snippet text to the editor's own
        // insertSpaces/tabSize, so the `\t` these carried while they were snippets was the
        // portable spelling — and became a literal tab the moment the editor started writing
        // them verbatim. The fix that stopped them landing at the caret is what put the tab
        // into the file, which is the shape of a fix that moves a defect instead of closing
        // it. Nothing else here would have caught it: a tabbed file parses perfectly.
        //
        // Two spaces because the corpus says so: 545 of the 548 tracked .lys files that
        // indent at all, and no file at all uses a tab.
        var template = (string)ItemIn(label, "temp").Command!.Arguments![1];

        Assert.DoesNotContain("\t", template);
        Assert.DoesNotContain("\r", template);
        foreach (var line in template.Split('\n').Where(l => l.StartsWith(' ')))
        {
            int indent = line.Length - line.TrimStart(' ').Length;
            Assert.True(indent % 2 == 0,
                $"{label} indents a line by {indent} spaces, which is not a level of two: {line}");
        }
    }

    [Theory]
    [MemberData(nameof(TemplateLabels))]
    public void ATemplateText_IsACompleteScoreOnItsOwn(string label)
    {
        // It replaces the file, so it IS the file: no fragment allowances, no sections
        // borrowed from what used to be there.
        var template = (string)ItemIn(label, "temp").Command!.Arguments![1];

        var tree = SyntaxTree.Parse(template);
        Assert.False(tree.HasErrors,
            $"{label} does not parse: {string.Join(" | ", tree.Diagnostics.Select(d => d.Message))}");

        var semantic = LilySharp.Core.Semantics.SemanticValidation.Run(tree)
            .Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(semantic.Count == 0,
            $"{label} does not compile: {string.Join(" | ", semantic.Select(d => d.Message))}");
    }

    [Theory]
    [MemberData(nameof(TemplateLabels))]
    public void WithNoCaretPosition_TheItemStillCannotTypeItsLabel(string label)
    {
        // The server always HAS a position in production (the completion request carries
        // one); this is the shape the other suites' `GetTopLevelCompletions()` calls see.
        // Pinned anyway: an unobserved fallback whose failure mode is "type the label into
        // the score" is exactly the kind that stays green for years.
        var item = LilySharpLanguageServer.GetTopLevelCompletions()
            .Items.Single(i => i.Label == label);

        Assert.Null(item.TextEdit);
        Assert.Equal("", item.InsertText);
        Assert.Equal(ApplyCommand, item.Command!.CommandIdentifier);
        Assert.Equal(2, item.Command.Arguments!.Length);
    }

    [Fact]
    public void TheExtension_RegistersTheCommandTheTemplatesName()
    {
        // The seam. The C# half names a command; the TypeScript half implements it. Nothing
        // links them at build time, and a mismatch is silent in the worst way — the item is
        // accepted, the no-op edit applies, and NOTHING happens.
        var extension = File.ReadAllText(RepoFile("editors/vscode/src/extension.ts"));

        Assert.Contains($"registerCommand('{ApplyCommand}'", extension);
    }

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Cannot find {relative} walking up from {AppContext.BaseDirectory}");
    }
}
