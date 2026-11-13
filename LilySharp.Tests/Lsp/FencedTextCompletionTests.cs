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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using CompletionContext = LilySharp.Lsp.Protocol.CompletionContext;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The fenced text in completion (owner's request 2026-11-13, docs/markdown-text-design.md):
/// <c>```md</c> and <c>```md end</c> on a line of its own at the file's top level and directly
/// in a <c>score { }</c> — the two places the page prints a text from — after the backticks
/// there, <c>end</c> after <c>```md</c>, and nothing at all between the fences.
/// </summary>
[Trait("Category", "Unit")]
public class FencedTextCompletionTests
{
    private const string Md = "```md";
    private const string MdEnd = "```md end";

    /// <summary>A file with a place of every kind; each test puts its caret (‸) into one.</summary>
    private const string Piece =
        "title \"T\"\ntime 4/4\npart p { clef treble }\n"
        + "section A {\n  p { c'4 d' e' f' | }\n}\nform { A }\nscore {\n  staff p\n}\n";

    private static int _documents;

    /// <summary>The list at <c>‸</c>: null when the server answers nothing.</summary>
    private static CompletionItem[]? Ask(string marked, string? trigger = null)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri($"file:///fence{System.Threading.Interlocked.Increment(ref _documents)}.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
            Context = trigger == null
                ? new CompletionContext { TriggerKind = 1 }
                : new CompletionContext { TriggerKind = 2, TriggerCharacter = trigger },
        })?.Items;
    }

    private static string[] Labels(string marked, string? trigger = null)
        => [.. (Ask(marked, trigger) ?? []).Select(i => i.Label!)];

    /// <summary>The text after the item <paramref name="label"/> is taken at <c>‸</c>, a
    /// snippet's stops as nothing — through the request a plain text field completes by.</summary>
    private static string Taken(string marked, string label)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var item = new LilySharpLanguageServer(Stream.Null, Stream.Null)
            .CompleteText(new CompleteTextParams { Text = text, Offset = offset })
            .Items.Single(i => i.Label == label);
        return text[..item.Start] + item.Insert + text[item.End..];
    }

    private static string[] Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree)).Select(d => d.Code + " " + d.Message)];
    }

    // ----- 1. an empty line: the two blocks -----

    [Theory]
    [InlineData("‸\n" + Piece)]                                     // the file's first line
    [InlineData("title \"T\"\n‸\npart p { clef treble }\n")]        // between two declarations
    [InlineData(Piece + "‸")]                                       // the file's last line
    [InlineData(Piece + "md‸")]                                     // the word on its way
    public void OnALineOfItsOwnAtTheTopLevel_TheTwoFencesAreOffered(string marked)
    {
        var items = Ask(marked)!;
        Assert.Contains(items, i => i.Label == "section");
        var md = items.Single(i => i.Label == Md);
        var end = items.Single(i => i.Label == MdEnd);
        Assert.Equal("```md\n$0\n```", md.InsertText);
        Assert.Equal("```md end\n$0\n```", end.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, md.InsertTextFormat);
        Assert.Equal(CompletionItemKind.Snippet, md.Kind);
        Assert.Contains("under the title", md.Detail);
        Assert.Contains("under the last system", end.Detail);
        Assert.Contains("score { }", ((MarkupContent)md.Documentation!).Value);
    }

    [Fact]
    public void TheyAreFoundByMdMarkdownAndText_AndStandLastInTheList()
    {
        var items = Ask("‸\n" + Piece)!;
        var md = items.Single(i => i.Label == Md);
        // The editor filters an empty line's word against the filter text: the backtick is no
        // word character, so the label alone would be found by nothing a writer types.
        foreach (string word in new[] { "md", "markdown", "text" })
            Assert.Contains(word, md.FilterText!.Split(' '));
        Assert.Contains("end", items.Single(i => i.Label == MdEnd).FilterText!.Split(' '));
        // A label that begins with a backtick sorts before every letter: the sort text keeps a
        // page's text after the keywords.
        var sorted = items.OrderBy(i => (i.SortText ?? i.Label!).ToLowerInvariant(), StringComparer.Ordinal).Select(i => i.Label).ToArray();
        Assert.Equal([Md, MdEnd], sorted[^2..]);
    }

    [Theory]
    [InlineData("score {\n  ‸\n}\n")]                               // an empty score
    [InlineData("part p { clef treble }\nscore {\n  staff p\n  ‸\n}\n")]          // after a staff
    [InlineData("part p { clef treble }\nscore parts {\n  ‸\n  staff p\n}\n")]    // before one, named score
    [InlineData("part p { }\nlyrics w { section A { la } }\nscore {\n  staff p\n  lyrics w\n  ‸\n}\n")]
    public void OnALineOfItsOwnInAScore_TheTwoFencesAreOffered_AsThatScoresText(string marked)
    {
        var items = Ask(marked)!;
        Assert.Contains(items, i => i.Label == "staff");
        var md = items.Single(i => i.Label == Md);
        Assert.Equal("```md\n$0\n```", md.InsertText);
        Assert.StartsWith("This score's text", md.Detail);
        Assert.StartsWith("This score's text", items.Single(i => i.Label == MdEnd).Detail);
        var sorted = items.OrderBy(i => (i.SortText ?? i.Label!).ToLowerInvariant(), StringComparer.Ordinal).Select(i => i.Label).ToArray();
        Assert.Equal([Md, MdEnd], sorted[^2..]);
    }

    // ----- where no fence is printed from -----

    public static TheoryData<string> PlacesWithNoFence => new()
    {
        "section A {\n  ‸\n}\n",                                                     // a section (LYS4058)
        "part p { clef treble }\nsection A {\n  ‸\n  p { c'4 }\n}\n",                 // a section of parts
        "part p { clef treble }\nsection A {\n  p {\n    ‸\n  }\n}\n",               // a part's music
        "part p {\n  ‸\n}\n",                                                         // a part's header
        "part p {\n  section A {\n    ‸\n  }\n}\n",                                   // a section in a part
        "lyrics w {\n  section A {\n    ‸\n  }\n}\n",                                 // lyrics
        "lyrics w {\n  ‸\n}\n",
        "form {\n  ‸\n}\n",
        "layout {\n  ‸\n}\n",
        "part p { }\nscore {\n  grandStaff {\n    ‸\n  }\n}\n",                       // a group in a score
        "title \"‸\"\n",                                                              // a string
        "// ‸\n",                                                                     // a comment
        "/*\n‸\n*/\n",
        "title \"T\" ‸\n",                                                            // no line of its own
        "‸ title \"T\"\n",
        "part p { }\nscore { ‸ }\n",
        "part p { }\nscore { staff p ‸\n}\n",
    };

    [Theory]
    [MemberData(nameof(PlacesWithNoFence))]
    public void WhereNoTextIsPrintedFrom_NoFenceIsOffered(string marked)
    {
        Assert.DoesNotContain(Labels(marked), l => l.StartsWith('`'));
        // …nor after the backticks, typed (the backtick is a trigger character) or asked for.
        foreach (string ticks in new[] { "`", "``", "```" })
        {
            string typed = marked.Replace("‸", ticks + "‸");
            Assert.Null(Ask(typed, "`")?.Where(i => i.Label is not null).Select(i => i.Label).FirstOrDefault());
            Assert.DoesNotContain(Labels(typed), l => l.StartsWith('`') || l == "end");
        }
        Assert.DoesNotContain("end", Labels(marked.Replace("‸", "```md ‸")));
    }

    // ----- 2. after the backticks -----

    [Theory]
    [InlineData("`")]
    [InlineData("``")]
    [InlineData("```")]
    [InlineData("```m")]
    [InlineData("```md")]
    public void AfterTheBackticks_TheTwoFencesReplaceWhatIsTyped_WithTheWholeBlock(string typed)
    {
        foreach (string marked in new[] { "title \"T\"\n" + typed + "‸\npart p { clef treble }\n", "part p { }\nscore {\n  staff p\n  " + typed + "‸\n}\n" })
        {
            var items = Ask(marked, typed.EndsWith('`') ? "`" : null)!;
            Assert.Equal([Md, MdEnd], items.Select(i => i.Label));
            int line = marked[..marked.IndexOf('‸')].Count(c => c == '\n');
            int column = marked.IndexOf('‸') - marked.LastIndexOf('\n', marked.IndexOf('‸')) - 1;
            foreach (var item in items)
            {
                // The range runs from the first backtick to the caret, and the editor filters by
                // the text in it: the label begins with whatever was typed.
                Assert.Equal(line, item.TextEdit!.Range.Start.Line);
                Assert.Equal(column - typed.Length, item.TextEdit.Range.Start.Character);
                Assert.Equal(column, item.TextEdit.Range.End.Character);
                Assert.StartsWith(typed, item.FilterText);
                Assert.Equal(item.Label + "\n$0\n```", item.TextEdit.NewText);
                Assert.Equal(InsertTextFormat.Snippet, item.InsertTextFormat);
            }
        }
    }

    [Fact]
    public void AFenceOfFourBackticks_ClosesWithFour()
    {
        var md = Ask("````‸\n")!.First();
        Assert.Equal("````md", md.Label);
        Assert.Equal("````md\n$0\n````", md.TextEdit!.NewText);
    }

    [Theory]
    [InlineData("```‸\nA note.\n```\ntitle \"T\"\n")]
    [InlineData("```m‸\nA note.\n```\ntitle \"T\"\n")]
    [InlineData("part p { }\nscore {\n  ```‸\n  A note.\n  ```\n  staff p\n}\n")]
    public void AFenceThatAlreadyClosesBelow_TakesItsOpeningLineAlone(string marked)
    {
        var items = Ask(marked)!;
        Assert.Equal([Md, MdEnd], items.Select(i => i.Label));
        Assert.Equal([Md, MdEnd], items.Select(i => i.TextEdit!.NewText));
        Assert.All(items, i => Assert.Equal(InsertTextFormat.Plaintext, i.InsertTextFormat));
    }

    [Fact]
    public void AFenceTypedAboveAnotherOne_IsWrittenWhole()
    {
        // The new fence closes on the old one's closing line and holds its opening line in
        // its body: that closing fence is not its own.
        var items = Ask("```‸\ntitle \"T\"\n```md\nA note.\n```\n")!;
        Assert.Equal("```md\n$0\n```", items[0].TextEdit!.NewText);
    }

    [Fact]
    public void ABacktickAnywhereElse_OpensNothing()
    {
        Assert.Null(Ask("part p { }\nsection A { p { c'4 `‸ } }\n", "`"));
        Assert.Null(Ask("title \"a `‸\"\n", "`"));
        Assert.Null(Ask("title \"T\" `‸\n", "`"));
    }

    // ----- 3. after ```md -----

    [Theory]
    [InlineData("```md ‸\n", 6, 6)]
    [InlineData("```md e‸\n", 6, 7)]
    [InlineData("```markdown ‸\n", 12, 12)]
    [InlineData("```md ‸\nA note.\n```\ntitle \"T\"\n", 6, 6)]
    [InlineData("part p { }\nscore {\n```md ‸\n```\n  staff p\n}\n", 6, 6)]
    public void AfterMdAndASpace_EndAndTheShapeWordsAreOffered(string marked, int from, int to)
    {
        var items = Ask(marked, marked.Contains(" ‸") ? " " : null)!;
        // The place, then the shape (owner-approved 2026-11-13), in that order.
        Assert.Equal(["end", "box", "center", "indent"], items.OrderBy(i => i.SortText, StringComparer.Ordinal).Select(i => i.Label));
        var end = items.Single(i => i.Label == "end");
        Assert.Equal("end", end.TextEdit!.NewText);
        Assert.All(items, i => Assert.Equal((from, to), (i.TextEdit!.Range.Start.Character, i.TextEdit.Range.End.Character)));
        Assert.All(items, i => Assert.Equal(i.Label, i.TextEdit!.NewText));
        Assert.Contains("under", end.Detail);
        Assert.Contains("frame", items.Single(i => i.Label == "box").Detail);
        Assert.Contains("20mm", items.Single(i => i.Label == "indent").Detail);
    }

    [Theory]
    [InlineData("```md end ‸\n", "box center indent")]
    [InlineData("```md box ‸\n", "end center indent")]
    [InlineData("```md end center b‸\n", "box indent")]                  // the editor filters by the word
    [InlineData("```md indent 20mm ‸\n", "end box center")]
    [InlineData("```md indent 20mm 10mm box ‸\n", "end center")]
    [InlineData("```md box center indent 2cm end ‸\n", "")]
    [InlineData("part p { }\nscore {\n  ```md box ‸\n  ```\n  staff p\n}\n", "end center indent")]
    public void AWordAlreadyWritten_IsNotOfferedAgain(string marked, string offered)
        => Assert.Equal(offered.Split(' ', StringSplitOptions.RemoveEmptyEntries), Labels(marked, " "));

    [Theory]
    [InlineData("```md indent ‸\n")]                    // a length is typed, not chosen
    [InlineData("```md indent 2‸\n")]
    [InlineData("```md box indent ‸\n")]
    public void AfterIndent_NothingIsOffered(string marked)
        => Assert.Empty(Ask(marked, marked.Contains(" ‸") ? " " : null)!);

    [Theory]
    [InlineData("```md ‸ end\n")]                       // the line goes on after the caret
    [InlineData("```lys ‸\n")]                          // not a text Lily# reads (LYS4057)
    [InlineData("section A {\n  ```md ‸\n  ```\n}\n")]  // a section's text is not printed yet
    public void WhereTheWordsDoNotBelong_NothingIsOffered(string marked)
        => Assert.Empty(Ask(marked, " ")!);

    [Fact]
    public void EveryWordOffered_IsOneTheCompilerTakes()
    {
        foreach (string word in Labels("```md ‸\n", " "))
        {
            string line = word == "indent" ? "```md indent 20mm" : "```md " + word;
            Assert.Empty(Said($"title \"T\"\n{line}\nA note.\n```\npart p {{ clef treble }}\nsection A {{ p {{ c'1 | }} }}\nscore {{ staff p }}\n"));
        }
    }

    // ----- 4. between the fences -----

    [Theory]
    [InlineData("```md\n‸\n```\ntitle \"T\"\n")]
    [InlineData("```md\nA note for the ‸\n```\ntitle \"T\"\n")]
    [InlineData("```md end\nsection ‸\n```\n")]
    [InlineData("```md\nA note.\n```‸\ntitle \"T\"\n")]                               // the closing fence
    [InlineData("```md\nA note.\n``‸`\ntitle \"T\"\n")]
    [InlineData("```md\nnever closed ‸\ntitle \"T\"\n")]                             // to the file's end
    [InlineData("part p { }\nscore {\n  ```md\n  For the parts. ‸\n  ```\n  staff p\n}\n")]
    [InlineData("section A {\n  ```md\n  The key changes here. ‸\n  ```\n  c'1 |\n}\n")]
    [InlineData("part p { }\nsection A { p {\n```md\nstray ‸\n```\n c'1 | } }\n")]    // a fence where none belongs
    public void BetweenTheFences_NothingIsOffered(string marked)
    {
        Assert.Empty(Ask(marked)!);
        Assert.Empty(Ask(marked, " ")!);
        Assert.Empty(Ask(marked, "@")!);
    }

    [Theory]
    [InlineData("```md\nbraces { and \"quotes and /* stars\n```\n‸\n")]
    [InlineData("```md\nsection A {\n```\n‸\n")]
    public void ATextsBracesAndQuotes_AreNotTheFilesOwn(string marked)
    {
        // The line after the fence is the top level still, whatever the text holds.
        var labels = Labels(marked);
        Assert.Contains("section", labels);
        Assert.Contains(Md, labels);
    }

    [Fact]
    public void BeforeAFenceOnItsLine_NoSecondFenceIsOffered()
        => Assert.DoesNotContain(Labels("‸```md\nA note.\n```\n"), l => l.StartsWith('`'));

    // ----- the block that is written -----

    [Theory]
    [InlineData("title \"T\"\n‸\npart p { clef treble }\nsection A { p { c'1 | } }\nscore { staff p }\n", Md)]
    [InlineData("title \"T\"\n‸\npart p { clef treble }\nsection A { p { c'1 | } }\nscore { staff p }\n", MdEnd)]
    [InlineData("title \"T\"\n```‸\npart p { clef treble }\nsection A { p { c'1 | } }\nscore { staff p }\n", Md)]
    [InlineData("title \"T\"\n`‸\npart p { clef treble }\nsection A { p { c'1 | } }\nscore { staff p }\n", MdEnd)]
    [InlineData("part p { clef treble }\nsection A { p { c'1 | } }\nscore {\n‸\nstaff p\n}\n", Md)]
    [InlineData("part p { clef treble }\nsection A { p { c'1 | } }\nscore {\n‸\nstaff p\n}\n", MdEnd)]
    [InlineData("part p { clef treble }\nsection A { p { c'1 | } }\nscore {\nstaff p\n``‸\n}\n", Md)]
    public void TheBlockWritten_IsAFencedTextTheCompilerTakes(string marked, string label)
    {
        string before = marked.Replace("‸", "").Replace("\n```\n", "\n\n").Replace("\n``\n", "\n\n").Replace("\n`\n", "\n\n");
        string after = Taken(marked, label);
        string block = label + "\n\n```\n";
        Assert.Contains("\n" + block, after);
        Assert.Equal(before.Length - 1 + block.Length, after.Length);
        Assert.Empty(Said(after));
        var text = Assert.Single(SyntaxTree.Parse(after).GetRoot().DescendantNodes<MarkdownTextSyntax>());
        Assert.True(text.Value.Closed);
        Assert.Equal(label == MdEnd, text.IsEnd);
        Assert.Equal(marked.Contains("score {\n"), text.Parent is not CompilationUnitSyntax);
    }

    [Fact]
    public void TheBacktickIsATriggerCharacter()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var triggers = server.Initialize(new InitializeParams()).Capabilities.CompletionProvider!.TriggerCharacters!;
        Assert.Contains("`", triggers);
    }
}
