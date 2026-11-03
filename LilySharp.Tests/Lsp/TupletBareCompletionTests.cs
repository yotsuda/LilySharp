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

using System.IO;
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// <c>bare</c> through the completion REQUEST (owner's request 2026-11-04): the slot after a
/// tuplet's ratio offers it, nothing else offers it, and the music popup has the whole form for
/// the writer who never stops at the slot. <c>‸</c> marks the caret.
/// </summary>
[Trait("Category", "Unit")]
public class TupletBareCompletionTests
{
    private static readonly Uri DocUri = new("file:///tuplet.lys");

    private static CompletionItem[] Items(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Position = new Position(line, character),
        })?.Items?.ToArray() ?? [];
    }

    private static string[] Labels(string marked) => [.. Items(marked).Select(i => i.Label!)];

    private const string Part = "part m { clef treble }\n";

    [Theory]
    [InlineData("section A { m { tuplet 3/2 ‸")]
    [InlineData("section A { m { tuplet 3/2 ‸ } }")]
    [InlineData("section A { m { tuplet 3/2 b‸")]
    [InlineData("section A { m { tuplet 3/2 bar‸")]
    [InlineData("section A { m { c4 tuplet 5/4 ‸")]
    [InlineData("section A { m { tuplet 3 / 2 ‸")]
    [InlineData("section A { m { voice { tuplet 3/2 ‸")]
    [InlineData("section A { m { tuplet 3/2 { tuplet 3/2 ‸")]
    [InlineData("section A { m { tuplet 3/2 ‸{ c8 d e } } }")]
    [InlineData("part m { section A { tuplet 3/2 ‸")]
    public void AfterTheRatio_BareIsTheOneWord(string marked)
    {
        Assert.Equal(["bare"], Labels(Part + marked));
    }

    [Theory]
    [InlineData("section A { m { tuplet 3/2 bare ‸")]
    [InlineData("section A { m { tuplet ‸")]
    [InlineData("section A { m { tuplet 3/‸")]
    [InlineData("section A { m { tuplet 3/2‸")]
    [InlineData("section A { m { tuplet 3/2 x‸")]
    [InlineData("section A { m { tuplet 3/2 { ‸")]
    [InlineData("section A { m { c4 3/2 ‸")]
    [InlineData("section A { m { xtuplet 3/2 ‸")]
    [InlineData("section A { m { c4 ‸")]
    [InlineData("tuplet 3/2 ‸")]
    [InlineData("section A { m { // tuplet 3/2 ‸")]
    [InlineData("section A { m { /* tuplet 3/2 ‸")]
    [InlineData("section A { m { c4@text(\"tuplet 3/2 ‸")]
    [InlineData("lyrics v { section A { tuplet 3/2 ‸")]
    public void AnywhereElse_BareIsNotOffered(string marked)
    {
        Assert.DoesNotContain("bare", Labels(Part + marked));
    }

    [Theory]
    [InlineData("section A { m { tuplet 3/2 ‸", "bare ")]
    [InlineData("section A { m { tuplet 3/2 ‸\n", "bare ")]
    [InlineData("section A { m { tuplet 3/2 ‸{ c8 d e } } }", "bare ")]
    [InlineData("section A { m { tuplet 3/2 ‸ { c8 d e } } }", "bare")]
    public void TheWordLeavesOneSpaceBeforeTheBody(string marked, string insert)
    {
        Assert.Equal(insert, Items(Part + marked).Single().InsertText);
    }

    [Theory]
    [InlineData("part m { clef treble }\nsection A { m { ‸ } }")]
    [InlineData("part d { instrument drums }\nsection A { d { ‸ } }")]
    public void TheMusicPopupHasTheWholeForm(string marked)
    {
        var item = Items(marked).Single(i => i.Label == "tuplet bare");
        string written = marked.Replace("‸", item.InsertText!.Replace("$0", "c8 d e"));
        Assert.Contains("tuplet 3/2 bare {", written);
        Assert.False(SyntaxTree.Parse(written).HasErrors, written);
    }
}