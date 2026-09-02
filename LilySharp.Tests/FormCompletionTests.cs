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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Editor completion inside a <c>form Name { }</c> block offers section names and
/// navigation marks, never note names.
/// </summary>
[Trait("Category", "Unit")]
public class FormCompletionTests
{
    private const string Doc = """
        part m {
          section Intro { c4 d e f | }
          section Verse { g4 a b c | }
        }
        form main { Intro segno Verse to coda }
        score main { staff m }
        """;

    [Fact]
    public void InsideFormBlock_IsDetected()
    {
        int offset = Doc.IndexOf("Intro segno") + "Intro ".Length; // inside form main { }
        Assert.Equal(LilySharpLanguageServer.CompletionContext.FormBlock,
            LilySharpLanguageServer.GetCompletionContext(Doc, offset));
    }

    [Fact]
    public void InsideSectionMusicBlock_IsNotFormBody()
    {
        // Inside a section's music block the completions route to music (note)
        // names, not the form body's section/navigation list.
        int offset = Doc.IndexOf("c4 d");
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.FormBlock,
            LilySharpLanguageServer.GetCompletionContext(Doc, offset));
    }

    [Fact]
    public void StructureCompletions_OfferSectionNamesAndNavMarks()
    {
        var labels = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        Assert.Contains("Intro", labels);   // declared section names
        Assert.Contains("Verse", labels);
        Assert.Contains("segno", labels);   // navigation marks
        Assert.Contains("to coda", labels);
        Assert.Contains("ds al coda", labels);
    }

    [Fact]
    public void StructureCompletions_OfferRepeatVoltaAndOtherSyntax()
    {
        var labels = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        Assert.Contains("|:", labels);     // repeat barlines
        Assert.Contains(":|", labels);
        Assert.Contains("[1. ]", labels);  // volta brackets
        Assert.Contains("[2. ]", labels);
        Assert.Contains("_\"\"", labels);  // custom text

        // Silent sections are offered with the name attached (~Intro), not bare ~.
        Assert.Contains("~Intro", labels);
        Assert.Contains("~Verse", labels);
        Assert.DoesNotContain("~", labels);
    }

    /// <summary>
    /// The whole of what a form body can hold (Parser.Form.cs ParseFormItem): the repeat
    /// family with its count, the three engraved barlines, the breaks — the rows the list
    /// was short of until 2026-09-02.
    /// </summary>
    [Fact]
    public void StructureCompletions_OfferTheWholeFormVocabulary()
    {
        var labels = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();
        foreach (var expected in new[] { "|:", ":|", ":|:", ":|*3", "[1. ]", "[2. ]", "[3. ]", "[1-2. ]",
                                         "||", "|.", "!", "break", "nobreak", "_\"\"" })
            Assert.Contains(expected, labels);
        // A plain `|` is an inert divider in a form and is not offered.
        Assert.DoesNotContain("|", labels);
    }

    /// <summary>
    /// Every plain item compiles where it is inserted — the net is the compiler, not this
    /// file's idea of the grammar. A `|:` needs its `:|` to be a block, so it is placed as
    /// one; every other item stands between two sections.
    /// </summary>
    [Fact]
    public void EveryOfferedPlainItem_CompilesInAForm()
    {
        var items = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .Where(i => i.InsertTextFormat != LilySharp.Lsp.Protocol.InsertTextFormat.Snippet);
        foreach (var item in items)
        {
            string insert = item.InsertText ?? item.Label!;
            string body = insert == "|:" ? "|: Intro :| Verse" : $"Intro {insert} Verse";
            var tree = LilySharp.Core.Syntax.SyntaxTree.Parse($$"""
                part m {
                  section Intro { c4 d e f | }
                  section Verse { g4 a b c | }
                }
                form main { {{body}} }
                score main { staff m }
                """);
            Assert.False(tree.HasErrors,
                $"'{item.Label}' → {insert} does not parse in a form: "
                + string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        }
    }

    /// <summary>
    /// The list reads in the order a writer reaches for it — sections, silent sections,
    /// the repeat block, endings, navigation, barlines, breaks, text — by sortText, which
    /// the editor honours; without one VS Code sorted by label and the repeat bars sat
    /// under the section names.
    /// </summary>
    [Fact]
    public void StructureCompletions_SortInGroups()
    {
        var sorted = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label!).ToArray();
        Assert.Equal(
            new[] { "Intro", "Verse", "~Intro", "~Verse", "|:", ":|", ":|:", ":|*3",
                    "[1. ]", "[2. ]", "[3. ]", "[1-2. ]", "segno" },
            sorted.Take(13).ToArray());
        Assert.True(System.Array.IndexOf(sorted, "||") > System.Array.IndexOf(sorted, "ds al coda"));
        Assert.True(System.Array.IndexOf(sorted, "break") > System.Array.IndexOf(sorted, "!"));
        Assert.Equal("_\"\"", sorted[^1]);
    }

    // ── end to end: the real Completion path, with a repeat bar half-typed ──

    private static LilySharp.Lsp.Protocol.CompletionItem[] CompletionAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///form.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var list = server.Completion(new LilySharp.Lsp.Protocol.CompletionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Position = new LilySharp.Lsp.Protocol.Position(line, character),
        });
        return list?.Items ?? System.Array.Empty<LilySharp.Lsp.Protocol.CompletionItem>();
    }

    [Theory]
    [InlineData("form main { Intro ")]
    [InlineData("form main {\n  Intro\n  ")]
    [InlineData("form main {\n  |: Intro [1. Verse ] :| ")]
    public void InAFormBody_TheRepeatBarIsOffered(string tail)
    {
        string doc = "part m {\n  section Intro { c4 d e f | }\n  section Verse { g4 a b c | }\n}\n" + tail;
        var labels = CompletionAt(doc, doc.Length).Select(i => i.Label).ToArray();
        Assert.Contains("|:", labels);
        Assert.Contains(":|", labels);
        Assert.Contains("Intro", labels);
        Assert.DoesNotContain("c", labels);
    }

    [Fact]
    public void AHalfTypedRepeatBar_IsReplacedNotAppendedTo()
    {
        // `|` typed, caret after it: accepting `|:` must give `|:`, not `||:`. `|` is no
        // word character, so the editor's own replace range is empty; the item carries the
        // range over the typed `|` itself.
        string doc = "part m {\n  section Intro { c4 d e f | }\n}\nform main { Intro |";
        var item = CompletionAt(doc, doc.Length).Single(i => i.Label == "|:");
        Assert.NotNull(item.TextEdit);
        Assert.Equal("|:", item.TextEdit!.NewText);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(doc, doc.Length - 1);
        Assert.Equal(line, item.TextEdit.Range!.Start.Line);
        Assert.Equal(character, item.TextEdit.Range.Start.Character);
        // A section name, being a word, carries no such edit.
        Assert.Null(CompletionAt(doc, doc.Length).Single(i => i.Label == "Intro").TextEdit);
    }

    [Fact]
    public void StructureCompletions_OfferNoNoteNames()
    {
        var labels = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        foreach (var note in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(note, labels);
    }
}
