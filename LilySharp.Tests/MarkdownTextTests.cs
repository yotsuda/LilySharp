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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A text written as Markdown in a fence (owner's idea and decisions 2026-11-12,
/// docs/markdown-text-design.md): how the file reads it. What the page does with it is not here.
/// </summary>
[Trait("Category", "Unit")]
public class MarkdownTextTests
{
    private const string F = "```";

    private static string Book(string top = "", string inSection = "", string inScore = "") => $$"""
        title "Morning Song"
        {{top}}
        part m { clef treble }
        section A {
          {{inSection}}
          m { c'4 d e f | }
        }
        form { A }
        score { {{inScore}} staff m }
        """;

    private static SyntaxTree Parsed(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Equal(source, tree.GetRoot().ToFullString());
        return tree;
    }

    private static MarkdownTextSyntax[] Texts(SyntaxTree tree)
        => [.. tree.GetRoot().DescendantNodes<MarkdownTextSyntax>()];

    [Fact]
    public void AFenceIsOneToken_AndNothingInsideItIsLilySharp()
    {
        // A brace, a comment's slashes, a quote that never closes, a bar line, an at sign.
        string source = Book(top: F + "md\nA } and a // and a \" and | and @x.\n" + F);
        var tree = Parsed(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var text = Assert.Single(Texts(tree));
        Assert.Equal(SyntaxKind.FencedText, text.Fence.Kind);
        Assert.Equal("A } and a // and a \" and | and @x.\n", text.Value.Body);
        Assert.True(text.Value.IsMarkdown);
        Assert.True(text.Value.Closed);
        Assert.False(text.IsEnd);
        // The rest of the file is read as it was.
        Assert.Single(tree.GetRoot().DescendantNodes<SectionDeclarationSyntax>());
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ItStandsAtTheTopLevel_InASection_AndInAScore()
    {
        string source = Book(
            top: $"{F}md\nunder the title\n{F}\n{F}md end\nat the end\n{F}",
            inSection: $"{F}md 2\n  above B\n  {F}",
            inScore: $"{F}markdown\nthis score's\n{F}\n");
        var tree = Parsed(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
        var texts = Texts(tree);
        Assert.Equal(4, texts.Length);
        Assert.IsType<CompilationUnitSyntax>(texts[0].Parent);
        Assert.False(texts[0].IsEnd);
        Assert.True(texts[1].IsEnd);
        Assert.IsType<SectionDeclarationSyntax>(texts[2].Parent);
        Assert.Equal(["md", "2"], texts[2].Value.Words.Select(w => w.Text));
        Assert.IsType<RenderDeclarationSyntax>(texts[3].Parent);
        Assert.True(texts[3].Value.IsMarkdown);
    }

    [Fact]
    public void TheClosingFencesIndentation_IsTheTextsMargin()
    {
        // Indented in a section: the closing fence stands two columns in, and two are taken
        // off each line - a line further in keeps what is its own, a line less far in keeps none.
        var value = FencedText.Read("```md\n    one\n      two\n  \n   three\n    ```");
        Assert.Equal("one\n  two\n\nthree\n", value.Body);
        // A fence at the margin takes nothing off.
        Assert.Equal("  kept\n", FencedText.Read("```md\n  kept\n```").Body);
        // Carriage returns are not the text's.
        Assert.Equal("a\nb\n", FencedText.Read("```md\r\na\r\nb\r\n```").Body);
        // An empty fence: this score prints none.
        Assert.Equal("", FencedText.Read("```md\n```").Body);
    }

    [Fact]
    public void APlaceInTheText_GoesBackToItsPlaceInTheFile()
    {
        string token = "```md\n    one\n    two\n    ```";
        var value = FencedText.Read(token);
        Assert.Equal("one\ntwo\n", value.Body);
        int two = value.Body.IndexOf("two", System.StringComparison.Ordinal);
        Assert.Equal(token.IndexOf("two", System.StringComparison.Ordinal), value.TokenOffsetOf(two));
        Assert.Equal(token.IndexOf("ne", System.StringComparison.Ordinal), value.TokenOffsetOf(1));
    }

    [Fact]
    public void FourBackticks_HoldATextThatWritesThree()
    {
        string source = Book(top: "````md\nbefore\n```lys\nc4 d\n```\nafter\n````");
        var tree = Parsed(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        Assert.Equal("before\n```lys\nc4 d\n```\nafter\n", Assert.Single(Texts(tree)).Value.Body);
    }

    [Fact]
    public void AFenceNeverClosed_IsSaidAtItsBackticks()
    {
        string source = "title \"x\"\n```md\nand then the file ends\n";
        var tree = Parsed(source);
        var d = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedFence);
        Assert.Equal(source.IndexOf("```", System.StringComparison.Ordinal), d.Span.Start);
        Assert.Equal(3, d.Span.Length);
        Assert.False(Assert.Single(Texts(tree)).Value.Closed);
    }

    [Fact]
    public void FewerThanThreeBackticks_AreNotAFence()
    {
        var tree = SyntaxTree.Parse(Book(top: "``md\ntext\n``"));
        Assert.Empty(Texts(tree));
        Assert.True(tree.HasErrors);
    }

    [Theory]
    [InlineData("```\ntext\n```", "", "", "open it with ```md")]                    // says nothing
    [InlineData("```text\ntext\n```", "", "", "open it with ```md")]                // another language
    [InlineData("```md 2\ntext\n```", "", "", "'2' means nothing here")]            // a printing, at the top level
    [InlineData("```md end end\ntext\n```", "", "", "a second place")]
    [InlineData("", "```md end\ntext\n```", "", "'end' is not a printing")]         // end, in a section
    [InlineData("", "```md 0\ntext\n```", "", "'0' is not a printing")]
    [InlineData("", "```md 3-2\ntext\n```", "", "'3-2' is not a printing")]
    [InlineData("", "```md 2 all\ntext\n```", "", "a second place")]
    [InlineData("", "", "```md all\ntext\n```\n", "'all' means nothing here")]      // a printing, in a score
    public void AnOpeningLineLilySharpDoesNotRead_IsAnError(string top, string inSection, string inScore, string says)
    {
        var tree = Parsed(Book(top, inSection, inScore));
        var d = Assert.Single(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.FencedTextWord);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(says, d.Message);
    }

    [Theory]
    [InlineData("2", new[] { 2 })]
    [InlineData("1,3", new[] { 1, 3 })]
    [InlineData("2-4", new[] { 2, 3, 4 })]
    [InlineData("1,3-4", new[] { 1, 3, 4 })]
    public void APrintingIsWrittenAsAVerseIs(string word, int[] printings)
        => Assert.Equal(printings, MarkdownTexts.PrintingsOf(word));

    [Theory]
    [InlineData("all")]
    [InlineData("0")]
    [InlineData("2-")]
    [InlineData(",2")]
    [InlineData("3-2")]
    [InlineData("x")]
    public void WhatIsNotAPrinting(string word) => Assert.Null(MarkdownTexts.PrintingsOf(word));

    [Fact]
    public void ASectionInsideAPart_TakesNoText()
    {
        string source = """
            part m {
              clef treble
              section A {
                ```md
                text
                ```
                c'4 d e f |
              }
            }
            form { A }
            score { staff m }
            """;
        var tree = Parsed(source);
        // It is music there, and a fence is none: reported and kept.
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.StrayItemToken);
        Assert.Empty(Texts(tree));
    }

    [Fact]
    public void AFenceWhereNoTextBelongs_IsReportedAndKept()
    {
        // Inside a part's music: nothing claims it, and the file's width is still whole.
        string source = Book().Replace("c'4 d e f |", "c'4 d ```md\nx\n``` e f |");
        var tree = Parsed(source);
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.StrayItemToken);
    }
}
