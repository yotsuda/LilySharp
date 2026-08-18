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
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The tempo feel words, <c>swing</c> and <c>shuffle</c>, coloured from the tree because no
/// line of regex can colour them.
/// </summary>
/// <remarks>
/// <para>
/// They lex as identifiers and are deliberately never reserved — <c>TempoValue.IsFeelWord</c>
/// says why: a part and a marking may still be called that. So <c>tempo</c> was coloured and
/// the one word that changes what it MEANS was plain, which is the shape the user reported
/// about a <c>fonts { }</c> block and which the part header closed on 2026-08-19.
/// </para>
/// <para>
/// ⚠️ Three TextMate rules were tried on 2026-08-18 and each failed differently: a bare
/// alternation paints <c>part swing { … }</c>; a rule spanning the whole value run swallows the
/// colours of the marking and the numbers inside it; a begin/end to end of line eats the music
/// in <c>section A { tempo 120  m { c'4 } }</c>. The missing information is position, and the
/// semantic tokens have the tree. This file is the net for that decision.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TempoFeelWordTokenTests
{
    private const int Keyword = 0;

    /// <summary>The spans coloured as keywords, as the text they cover.</summary>
    private static string[] KeywordSpans(string source) =>
        [.. LilySharpLanguageServer
            .CollectSemanticTokens(SyntaxTree.Parse(source).GetRoot(), source)
            .Where(t => t.TokenType == Keyword)
            .Select(t => Span(source, t))];

    /// <remarks>The server reports (line, character); this walks back to an offset so the
    /// assertions can be written as the WORD that was coloured rather than a coordinate.</remarks>
    private static string Span(string source, LilySharpLanguageServer.SemanticToken token)
    {
        int line = 0, offset = 0;
        while (line < token.Line)
        {
            offset = source.IndexOf('\n', offset) + 1;
            line++;
        }
        return source.Substring(offset + token.Character, token.Length);
    }

    [Theory]
    [InlineData("swing")]
    [InlineData("shuffle")]
    public void AFeelWord_IsColoured(string feel)
    {
        Assert.Contains(feel, KeywordSpans(Book($"tempo 120 {feel}")));
    }

    [Theory]
    [InlineData("swing")]
    [InlineData("shuffle")]
    public void AFeelWordWithASubdivision_IsColoured(string feel)
    {
        // The number after the feel word is its subdivision; it keeps its own colour and the
        // word keeps this one. A rule that spanned the run would have taken both.
        string[] spans = KeywordSpans(Book($"tempo {Quoted("Lively")} 4 = 116 {feel} 16"));
        Assert.Contains(feel, spans);
        Assert.DoesNotContain("116", spans);
        Assert.DoesNotContain("16", spans);
        Assert.DoesNotContain("Lively", spans);
    }

    [Fact]
    public void ABareFeelWord_IsColoured()
    {
        // `tempo swing` is the feel word, never a marking spelled that way — TempoValue reads it
        // that way (the feel-word arm precedes the marking arm) and this follows the same rule.
        Assert.Contains("swing", KeywordSpans(Book("tempo swing")));
        Assert.Equal(8, SyntaxTree.Parse(Book("tempo swing")).GetRoot()
            .DescendantNodes().OfType<TempoDeclarationSyntax>().First().Value.SwingSubdivision);
    }

    [Fact]
    public void APartCalledSwing_IsNotColoured()
    {
        // ★ The claim that makes a bare alternation wrong, measured rather than recalled: the
        // word is the writer's everywhere else, and here it names the part AND the section body.
        string book = "part swing { clef treble }" + Newline
                    + "section A { swing { c'1 } }" + Newline
                    + "form main { A }" + Newline
                    + "score main { staff swing }" + Newline;
        Assert.False(SyntaxTree.Parse(book).HasErrors);
        Assert.DoesNotContain("swing", KeywordSpans(book));
    }

    [Fact]
    public void AMarkingCalledSwing_IsStillTheMarkingsColour()
    {
        // A quoted marking is a string, not an identifier, so it is left alone whatever it says.
        string[] spans = KeywordSpans(Book($"tempo {Quoted("swing")} 4 = 120"));
        Assert.DoesNotContain("swing", spans);
        Assert.DoesNotContain(Quoted("swing"), spans);
    }

    [Fact]
    public void TheCheckCanFail()
    {
        // Green for free says nothing: this net was written after the collector was corrected.
        // A word that is NOT a feel word, in the same position, must stay plain — otherwise the
        // assertions above would pass on a branch that coloured every tempo identifier.
        Assert.DoesNotContain("Andante", KeywordSpans(Book("tempo Andante 4 = 96")));

        // And `tempo` itself is coloured either way, so its presence in the list proves nothing
        // about the feel word beside it.
        Assert.Contains("tempo", KeywordSpans(Book("tempo 120 swing")));
        Assert.Contains("tempo", KeywordSpans(Book("tempo Andante 4 = 96")));
    }

    private static string Newline => Environment.NewLine;

    private static string Quoted(string s) => '"' + s + '"';

    private static string Book(string directive) =>
        "part m { clef treble }" + Newline
        + directive + Newline
        + "section A { m { c'1 } }" + Newline
        + "form main { A }" + Newline
        + "score main { staff m }" + Newline;
}
