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
/// <c>```md</c> — one item since the owner's decision of 2026-11-14 — on a line of its own at
/// the file's top level and directly in a <c>score { }</c> — the two places the page prints a
/// text from — and after the backticks there; the place and shape words after <c>```md</c>
/// (the item opens them at once), and nothing at all between the fences.
/// </summary>
[Trait("Category", "Unit")]
public class FencedTextCompletionTests
{
    private const string Md = "```md";

    /// <summary>What the fence item writes: the opening line with the caret after <c>md </c>,
    /// an empty body line a Tab away, the closing fence.</summary>
    private const string Block = "```md $1\n$0\n```";

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

    // ----- 1. an empty line: the one block (owner's decision 2026-11-14) -----

    [Theory]
    [InlineData("‸\n" + Piece)]                                     // the file's first line
    [InlineData("title \"T\"\n‸\npart p { clef treble }\n")]        // between two declarations
    [InlineData(Piece + "‸")]                                       // the file's last line
    [InlineData(Piece + "md‸")]                                     // the word on its way
    public void OnALineOfItsOwnAtTheTopLevel_TheOneFenceIsOffered(string marked)
    {
        var items = Ask(marked)!;
        Assert.Contains(items, i => i.Label == "section");
        var md = Assert.Single(items, i => i.Label!.StartsWith('`'));
        Assert.Equal(Md, md.Label);
        Assert.Equal(Block, md.InsertText);
        Assert.Equal(InsertTextFormat.Snippet, md.InsertTextFormat);
        Assert.Equal(CompletionItemKind.Snippet, md.Kind);
        // Taking it opens the list again at the caret, after `md `: where the text goes.
        Assert.Equal("editor.action.triggerSuggest", md.Command!.CommandIdentifier);
        Assert.Contains("start, end", md.Detail);
        Assert.Contains("score { }", ((MarkupContent)md.Documentation!).Value);
        Assert.Contains("`start`", ((MarkupContent)md.Documentation!).Value);
    }

    [Fact]
    public void ItIsFoundByMdMarkdownAndText_AndStandsLastInTheList()
    {
        var items = Ask("‸\n" + Piece)!;
        var md = items.Single(i => i.Label == Md);
        // The editor filters an empty line's word against the filter text: the backtick is no
        // word character, so the label alone would be found by nothing a writer types.
        foreach (string word in new[] { "md", "markdown", "text" })
            Assert.Contains(word, md.FilterText!.Split(' '));
        // A label that begins with a backtick sorts before every letter: the sort text keeps a
        // page's text after the keywords.
        var sorted = items.OrderBy(i => (i.SortText ?? i.Label!).ToLowerInvariant(), StringComparer.Ordinal).Select(i => i.Label).ToArray();
        Assert.Equal(Md, sorted[^1]);
    }

    [Theory]
    [InlineData("score {\n  ‸\n}\n")]                               // an empty score
    [InlineData("part p { clef treble }\nscore {\n  staff p\n  ‸\n}\n")]          // after a staff
    [InlineData("part p { clef treble }\nscore parts {\n  ‸\n  staff p\n}\n")]    // before one, named score
    [InlineData("part p { }\nlyrics w { section A { la } }\nscore {\n  staff p\n  lyrics w\n  ‸\n}\n")]
    public void OnALineOfItsOwnInAScore_TheOneFenceIsOffered_AsThatScoresText(string marked)
    {
        var items = Ask(marked)!;
        Assert.Contains(items, i => i.Label == "staff");
        var md = Assert.Single(items, i => i.Label!.StartsWith('`'));
        Assert.Equal(Block, md.InsertText);
        Assert.Equal("editor.action.triggerSuggest", md.Command!.CommandIdentifier);
        Assert.StartsWith("This score's text", md.Detail);
        var sorted = items.OrderBy(i => (i.SortText ?? i.Label!).ToLowerInvariant(), StringComparer.Ordinal).Select(i => i.Label).ToArray();
        Assert.Equal(Md, sorted[^1]);
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
    public void AfterTheBackticks_TheOneFenceReplacesWhatIsTyped_WithTheWholeBlock(string typed)
    {
        foreach (string marked in new[] { "title \"T\"\n" + typed + "‸\npart p { clef treble }\n", "part p { }\nscore {\n  staff p\n  " + typed + "‸\n}\n" })
        {
            var items = Ask(marked, typed.EndsWith('`') ? "`" : null)!;
            Assert.Equal([Md], items.Select(i => i.Label));
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
                Assert.Equal(Block, item.TextEdit.NewText);
                Assert.Equal(InsertTextFormat.Snippet, item.InsertTextFormat);
                Assert.Equal("editor.action.triggerSuggest", item.Command!.CommandIdentifier);
            }
        }
    }

    [Fact]
    public void AFenceOfFourBackticks_ClosesWithFour()
    {
        var md = Ask("````‸\n")!.First();
        Assert.Equal("````md", md.Label);
        Assert.Equal("````md $1\n$0\n````", md.TextEdit!.NewText);
    }

    [Theory]
    [InlineData("```‸\nA note.\n```\ntitle \"T\"\n")]
    [InlineData("```m‸\nA note.\n```\ntitle \"T\"\n")]
    [InlineData("part p { }\nscore {\n  ```‸\n  A note.\n  ```\n  staff p\n}\n")]
    public void AFenceThatAlreadyClosesBelow_TakesItsOpeningLineAlone(string marked)
    {
        var items = Ask(marked)!;
        Assert.Equal([Md], items.Select(i => i.Label));
        Assert.Equal(["```md "], items.Select(i => i.TextEdit!.NewText));
        Assert.All(items, i => Assert.Equal(InsertTextFormat.Plaintext, i.InsertTextFormat));
        Assert.All(items, i => Assert.Equal("editor.action.triggerSuggest", i.Command!.CommandIdentifier));
    }

    [Fact]
    public void AFenceTypedAboveAnotherOne_IsWrittenWhole()
    {
        // The new fence closes on the old one's closing line and holds its opening line in
        // its body: that closing fence is not its own.
        var items = Ask("```‸\ntitle \"T\"\n```md\nA note.\n```\n")!;
        Assert.Equal(Block, items[0].TextEdit!.NewText);
    }

    [Fact]
    public void ABacktickAnywhereElse_OpensNothing()
    {
        Assert.Null(Ask("part p { }\nsection A { p { c'4 `‸ } }\n", "`"));
        Assert.Null(Ask("title \"a `‸\"\n", "`"));
        Assert.Null(Ask("title \"T\" `‸\n", "`"));
    }

    // ----- 3. after ```md (the fence item opens this list at once) -----

    [Theory]
    [InlineData("```md ‸\n", 6, 6)]
    [InlineData("```md e‸\n", 6, 7)]
    [InlineData("```markdown ‸\n", 12, 12)]
    [InlineData("```md ‸\nA note.\n```\ntitle \"T\"\n", 6, 6)]
    [InlineData("part p { }\nscore {\n```md ‸\n```\n  staff p\n}\n", 6, 6)]
    public void AfterMdAndASpace_ThePlacesAndTheShapeWordsAreOffered(string marked, int from, int to)
    {
        var items = Ask(marked, marked.Contains(" ‸") ? " " : null)!;
        // The places — `start` and `end` (owner's decision 2026-11-14), then the six header
        // lines (owner's decision 2026-11-13: ```md title) — then the shape (owner-approved
        // 2026-11-13), in that order.
        Assert.Equal(["start", "end", "title", "subtitle", "composer", "poet", "arranger", "copyright", "box", "center", "xMargin", "yMargin"],
            items.OrderBy(i => i.SortText, StringComparer.Ordinal).Select(i => i.Label));
        var end = items.Single(i => i.Label == "end");
        Assert.Equal("end", end.TextEdit!.NewText);
        Assert.Contains("title, above the first system", items.Single(i => i.Label == "start").Detail);
        Assert.All(items, i => Assert.Equal((from, to), (i.TextEdit!.Range.Start.Character, i.TextEdit.Range.End.Character)));
        // Each word as it is, and a margin with the space its length follows (below).
        Assert.All(items, i => Assert.Equal(i.Label is "xMargin" or "yMargin" ? i.Label + " " : i.Label, i.TextEdit!.NewText));
        Assert.Contains("under", end.Detail);
        Assert.Contains("frame", items.Single(i => i.Label == "box").Detail);
        Assert.Contains("20mm", items.Single(i => i.Label == "xMargin").Detail);
    }

    [Theory]
    [InlineData("```md end ‸\n", "box center xMargin yMargin")]
    [InlineData("```md start ‸\n", "box center xMargin yMargin")]
    [InlineData("```md box ‸\n", "start end center xMargin yMargin")]
    [InlineData("```md end center b‸\n", "box xMargin yMargin")]                  // the editor filters by the word
    [InlineData("```md xMargin 20mm box ‸\n", "start end center yMargin")]
    [InlineData("```md xMargin 20mm 10mm ‸\n", "start end box center yMargin")]   // after two lengths, no third
    [InlineData("```md xMargin 20mm 10mm box ‸\n", "start end center yMargin")]
    [InlineData("```md yMargin 2 1 xMargin 20mm 10mm box ‸\n", "start end center")]
    [InlineData("```md box center xMargin 2cm end ‸\n", "yMargin")]
    [InlineData("```md box center xMargin 2cm start ‸\n", "yMargin")]
    [InlineData("```md title ‸\n", "")]                                           // a header place takes nothing more
    [InlineData("part p { }\nscore {\n  ```md box ‸\n  ```\n  staff p\n}\n", "start end center xMargin yMargin")]
    public void AWordAlreadyWritten_IsNotOfferedAgain(string marked, string offered)
        => Assert.Equal(offered.Split(' ', StringSplitOptions.RemoveEmptyEntries), Labels(marked, " "));

    // ----- the lengths after `xMargin` (owner's request 2026-11-13: "md indent を補完で入れたら、
    //       数字を補完ウィンドウにリストして") -----

    private const string Lengths = "5mm 10mm 15mm 20mm 25mm 30mm 40mm 50mm";

    /// <summary>The labels at <c>‸</c> as the editor lists them: by sort text.</summary>
    private static string Listed(string marked, string? trigger = null)
        => string.Join(" ", (Ask(marked, trigger) ?? []).OrderBy(i => i.SortText, StringComparer.Ordinal).Select(i => i.Label));

    [Theory]
    [InlineData("```md xMargin ‸\n")]
    [InlineData("```md box xMargin ‸\n")]
    [InlineData("```md xMargin ‸\nA note.\n```\ntitle \"T\"\n")]
    [InlineData("part p { }\nscore {\n  ```md end xMargin ‸\n  ```\n  staff p\n}\n")]
    public void AfterXMargin_ItsLengthsAreOffered_SmallestFirst_AndNothingElse(string marked)
    {
        // By the sort text, not the label: "10mm" sorts before "5mm" as a word.
        Assert.Equal(Lengths, Listed(marked, " "));
        Assert.Equal(Lengths, Listed(marked));
        var items = Ask(marked, " ")!;
        Assert.All(items, i => Assert.Equal(CompletionItemKind.Value, i.Kind));
        Assert.All(items, i => Assert.Equal(i.Label, i.TextEdit!.NewText));
        Assert.All(items, i => Assert.Null(i.Command));
        Assert.All(items, i => Assert.Contains("left and right", i.Detail));
    }

    [Fact]
    public void TakingXMarginFromTheList_WritesItsSpace_AndOpensTheLengthsAtOnce()
    {
        var items = Ask("```md ‸\n", " ")!;
        var xMargin = items.Single(i => i.Label == "xMargin");
        Assert.Equal("xMargin ", xMargin.TextEdit!.NewText);
        Assert.Equal("editor.action.triggerSuggest", xMargin.Command!.CommandIdentifier);
        // No other word asks for the list again.
        Assert.All(items.Where(i => i.Label is not ("xMargin" or "yMargin")),
            i => Assert.NotEqual("editor.action.triggerSuggest", i.Command?.CommandIdentifier));

        // Through the request a plain text field completes by: the text taken, and the list
        // it opens next at the caret it leaves.
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var taken = server.CompleteText(new CompleteTextParams { Text = "```md xM\n", Offset = 8 })
            .Items.Single(i => i.Label == "xMargin");
        Assert.True(taken.Again);
        string after = "```md xM\n"[..taken.Start] + taken.Insert + "```md xM\n"[taken.End..];
        Assert.Equal("```md xMargin \n", after);
        Assert.Equal(Lengths, Listed(after.Insert(taken.Start + taken.Caret, "‸")));
    }

    [Theory]
    [InlineData("```md xMargin 20mm ‸\n", "start end box center yMargin " + Lengths)]      // the words, then the second length
    [InlineData("```md box xMargin 2cm ‸\n", "start end center yMargin " + Lengths)]
    [InlineData("```md xMargin 5 ‸\n", "start end box center yMargin " + Lengths)]         // after a bare number of staff spaces
    [InlineData("```md xMargin 20mm 1‸\n", "1mm 1cm 1in 10mm 15mm")]         // a number on its way: the lengths alone
    [InlineData("```md xMargin 20mm b‸\n", "start end box center yMargin")]                // a word on its way: the editor filters
    public void AfterTheFirstLength_TheSecondIsOfferedBesideTheWords(string marked, string listed)
    {
        Assert.Equal(listed, Listed(marked, marked.Contains(" ‸") ? " " : null));
        var second = Ask(marked)!.FirstOrDefault(i => i.Kind == CompletionItemKind.Value);
        if (second != null)
            Assert.Contains("right", second.Detail);
    }

    [Theory]
    [InlineData("```md xMargin 2‸\n", "2mm 2cm 2in 20mm 25mm")]      // the number with each unit, then the list's
    [InlineData("```md xMargin 20‸\n", "20mm 20cm 20in")]
    [InlineData("```md xMargin 5‸\n", "5mm 5cm 5in 50mm")]
    [InlineData("```md xMargin 0.5‸\n", "0.5mm 0.5cm 0.5in")]
    [InlineData("```md xMargin 20m‸\n", "20mm")]                     // the unit on its way
    [InlineData("```md xMargin 2c‸\n", "2cm")]
    [InlineData("```md xMargin 1.‸\n", "")]                          // no number yet
    [InlineData("```md xMargin x‸\n", "")]
    public void WhileANumberIsTyped_ItsUnitsAndTheLengthsThatBeginSoAreOffered(string marked, string listed)
    {
        Assert.Equal(listed, Listed(marked));
        // Each replaces the word typed, so the editor filters by it.
        int word = marked.IndexOf("xMargin ", StringComparison.Ordinal) + 8, caret = marked.IndexOf('‸');
        Assert.All(Ask(marked)!, i => Assert.Equal((word, caret), (i.TextEdit!.Range.Start.Character, i.TextEdit.Range.End.Character)));
    }
    // ----- the lengths after `yMargin` (owner's request 2026-11-13: the white above and below
    //       a text settable) -----

    private const string YLengths = "0 1 2 3 4 5mm 10mm 15mm";

    [Theory]
    [InlineData("```md yMargin ‸\n", YLengths)]                              // staff spaces first, then a few mm
    [InlineData("```md box yMargin ‸\n", YLengths)]
    [InlineData("```md yMargin 3 ‸\n", "start end box center xMargin " + YLengths)]   // the words, then the white below
    [InlineData("```md xMargin 20mm yMargin ‸\n", YLengths)]                 // the margin the caret follows
    [InlineData("```md yMargin 2 xMargin ‸\n", Lengths)]
    [InlineData("```md yMargin 3 1 ‸\n", "start end box center xMargin")]           // after two lengths, no third
    [InlineData("```md yMargin 1‸\n", "1mm 1cm 1in 1 10mm 15mm")]
    public void AfterYMargin_ItsLengthsAreOffered_AsXMarginsAre(string marked, string listed)
    {
        Assert.Equal(listed, Listed(marked, marked.Contains(" ‸") ? " " : null));
        var length = Ask(marked)!.FirstOrDefault(i => i.Kind == CompletionItemKind.Value);
        if (length != null && !marked.Contains("xMargin ‸"))
            Assert.Contains(marked.Contains("yMargin 3 ‸") ? "White below" : "above and below", length.Detail);
    }

    [Fact]
    public void TakingYMarginFromTheList_WritesItsSpace_AndOpensTheLengthsAtOnce()
    {
        var yMargin = Ask("```md ‸\n", " ")!.Single(i => i.Label == "yMargin");
        Assert.Equal("yMargin ", yMargin.TextEdit!.NewText);
        Assert.Equal("editor.action.triggerSuggest", yMargin.Command!.CommandIdentifier);
        Assert.Contains("above and below", yMargin.Detail);
        foreach (string length in YLengths.Split(' '))
            Assert.Empty(Said($"title \"T\"\n```md yMargin {length}\nA note.\n```\npart p {{ clef treble }}\nsection A {{ p {{ c'1 | }} }}\nscore {{ staff p }}\n"));
    }


    [Fact]
    public void EveryLengthOffered_IsOneTheCompilerTakes()
    {
        foreach (string marked in new[] { "```md xMargin ‸\n", "```md xMargin 12‸\n", "```md xMargin 20mm ‸\n" })
            foreach (var item in Ask(marked)!.Where(i => i.Kind == CompletionItemKind.Value && !i.Label!.EndsWith("in") && !i.Label.EndsWith("cm")))
            {
                string line = marked[..marked.IndexOf("‸", StringComparison.Ordinal)];
                line = line[..item.TextEdit!.Range.Start.Character] + item.TextEdit.NewText;
                Assert.Empty(Said($"title \"T\"\n{line}\nA note.\n```\npart p {{ clef treble }}\nsection A {{ p {{ c'1 | }} }}\nscore {{ staff p }}\n"));
            }
    }

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
            string line = word == "xMargin" ? "```md xMargin 20mm" : word == "yMargin" ? "```md yMargin 2" : "```md " + word;
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
    [InlineData("title \"T\"\n‸\npart p { clef treble }\nsection A { p { c'1 | } }\nscore { staff p }\n")]
    [InlineData("title \"T\"\n```‸\npart p { clef treble }\nsection A { p { c'1 | } }\nscore { staff p }\n")]
    [InlineData("title \"T\"\n`‸\npart p { clef treble }\nsection A { p { c'1 | } }\nscore { staff p }\n")]
    [InlineData("part p { clef treble }\nsection A { p { c'1 | } }\nscore {\n‸\nstaff p\n}\n")]
    [InlineData("part p { clef treble }\nsection A { p { c'1 | } }\nscore {\nstaff p\n``‸\n}\n")]
    public void TheBlockWritten_IsAFencedTextTheCompilerTakes(string marked)
    {
        string before = marked.Replace("‸", "").Replace("\n```\n", "\n\n").Replace("\n``\n", "\n\n").Replace("\n`\n", "\n\n");
        string after = Taken(marked, Md);
        // A bare ```md and the space the place word would follow: the writer may move on.
        string block = "```md \n\n```\n";
        Assert.Contains("\n" + block, after);
        Assert.Equal(before.Length - 1 + block.Length, after.Length);
        Assert.Empty(Said(after));
        var text = Assert.Single(SyntaxTree.Parse(after).GetRoot().DescendantNodes<MarkdownTextSyntax>());
        Assert.True(text.Value.Closed);
        Assert.False(text.IsEnd);
        Assert.Equal(marked.Contains("score {\n"), text.Parent is not CompilationUnitSyntax);
    }

    // ----- the caret after the block, and after a place word (owner's decision 2026-11-14) -----

    [Theory]
    [InlineData("title \"T\"\n‸\npart p { clef treble }\n")]
    [InlineData("part p { }\nscore {\n  staff p\n  ```‸\n}\n")]
    public void TakingTheBlock_LeavesTheCaretAfterMd_WhereThePlacesOpen(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var taken = new LilySharpLanguageServer(Stream.Null, Stream.Null)
            .CompleteText(new CompleteTextParams { Text = text, Offset = offset })
            .Items.Single(i => i.Label == Md);
        Assert.True(taken.Again);
        string after = text[..taken.Start] + taken.Insert + text[taken.End..];
        int caret = taken.Start + taken.Caret;
        Assert.EndsWith("```md ", after[..caret]);
        Assert.Equal("start end title subtitle composer poet arranger copyright box center xMargin yMargin",
            Listed(after.Insert(caret, "‸"), " "));
    }

    [Theory]
    [InlineData("```md ‸\n\n```\ntitle \"T\"\n")]
    [InlineData("```md box ‸\n\n```\ntitle \"T\"\n")]
    [InlineData("part p { }\nscore {\n  ```md ‸\n  \n  ```\n  staff p\n}\n")]
    public void APlaceWordTaken_MovesTheCaretDownOntoTheEmptyBodyLine(string marked)
    {
        var items = Ask(marked, " ")!;
        foreach (var item in items.Where(i => FencedText.IsPlaceWord(i.Label!)))
            Assert.Equal(LilySharpLanguageServer.CursorDownCommand, item.Command!.CommandIdentifier);
        // A shape word may be followed by more: the caret stays on the line.
        Assert.All(items.Where(i => i.Label is "box" or "center"), i => Assert.Null(i.Command));
        Assert.Contains(items, i => i.Label == "start");
    }

    [Fact]
    public void WithNoEmptyLineUnderIt_APlaceWordLeavesTheCaretWhereItIs()
        => Assert.All(Ask("```md ‸\nA note.\n```\ntitle \"T\"\n", " ")!.Where(i => FencedText.IsPlaceWord(i.Label!)),
            i => Assert.Null(i.Command));

    // ----- hover on the opening line's words -----

    private static (string? Text, LilySharp.Lsp.Protocol.Range? Range) Hovered(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri($"file:///fencehover{System.Threading.Interlocked.Increment(ref _documents)}.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var hover = server.Hover(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        return (hover?.Contents.Value, hover?.Range);
    }

    [Fact]
    public void HoverOnStart_SaysWhereTheTextStands()
    {
        var (text, range) = Hovered("title \"T\"\n```md st‸art box\nA note.\n```\n");
        Assert.Contains("under the title, above the first system", text);
        Assert.Contains("`end`", text);
        Assert.Equal((1, 6, 11), (range!.Start.Line, range.Start.Character, range.End.Character));
        Assert.Contains("this score's own", Hovered("part p { }\nscore {\n  ```md start‸\n  A note.\n  ```\n  staff p\n}\n").Text);
        Assert.Contains("under the last system", Hovered("```md e‸nd\nA note.\n```\n").Text);
        Assert.Contains("ruled frame", Hovered("```md start b‸ox\nA note.\n```\n").Text);
        // Nothing in the body, nor on a section's fence.
        Assert.DoesNotContain("above the first system", Hovered("```md start\nst‸art here.\n```\n").Text ?? "");
        Assert.DoesNotContain("above the first system", Hovered("section A {\n  ```md st‸art\n  x\n  ```\n}\n").Text ?? "");
    }

    [Fact]
    public void TheBacktickIsATriggerCharacter()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var triggers = server.Initialize(new InitializeParams()).Capabilities.CompletionProvider!.TriggerCharacters!;
        Assert.Contains("`", triggers);
    }
}
