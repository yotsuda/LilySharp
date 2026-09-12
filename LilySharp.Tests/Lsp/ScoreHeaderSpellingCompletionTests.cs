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
using System.IO;
using LilySharp.Lsp.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// A score body is a render spec however long its header is —
/// <c>score main {</c>, <c>score main "out" {</c>, and with either option written out:
/// <c>score main transpose d {</c>, <c>score main pitch concert {</c> (GRAMMAR ScoreDecl:
/// <c>'score' , Identifier , [ String ] , { ScoreOption } , '{'</c>).
/// </summary>
/// <remarks>
/// ⚠️ The header-reading walk went back TWO tokens, so it covered the first two spellings
/// and nothing else: a score with ANY option was not recognized as a score, and its body
/// fell through to the MUSIC completions — measured at 98 items opening
/// <c>c d e f g a b</c> where a score offers its 17 render items. Found by sweeping the
/// block-header spellings (session 374, third finding of the same family — after the
/// chords track's own body and the lyrics <c>sings</c> clause). The spellings are asserted
/// side by side because that is what makes a reader that knows only the short ones fail.
/// </remarks>
[Trait("Category", "Unit")]
public class ScoreHeaderSpellingCompletionTests
{
    private const string Parts = """
        part melody { section A { c'4 d' e' f' | } }
        part bass { section A { c4 d e f | } }
        form main { A }

        """;

    private static string[] CompletionLabelsAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///score.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        return list?.Items.Select(i => i.Label!).ToArray() ?? System.Array.Empty<string>();
    }

    [Theory]
    [InlineData("score main {\n  ")]
    [InlineData("score main \"out\" {\n  ")]
    [InlineData("score main transpose d {\n  ")]
    [InlineData("score main pitch concert {\n  ")]
    [InlineData("score main \"out\" transpose d pitch concert {\n  ")]  // the longest header the grammar writes
    public void EveryHeaderSpelling_OpensARenderSpec_NotMusic(string header)
    {
        string text = Parts + header;
        Assert.Equal(LilySharpLanguageServer.CompletionContext.ScoreBlock,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));

        var labels = CompletionLabelsAt(text, text.Length);
        Assert.Contains("staff", labels);          // it IS the render list
        Assert.Contains("grandStaff", labels);
        foreach (var pitch in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(pitch, labels);  // and not the music list
        Assert.DoesNotContain("break", labels);
    }

    [Theory]
    [InlineData("score main { grandStaff {\n    ")]
    [InlineData("score main transpose d { grandStaff {\n    ")]
    public void AGroupUnderAnyHeader_KeepsItsOwnNarrowList(string header)
    {
        // A group's body is narrower than the score's (a chords row in here is LYS6011),
        // and that must not widen just because the score header carries an option.
        string text = Parts + header;
        Assert.Equal(LilySharpLanguageServer.CompletionContext.StaffGroupBlock,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
        Assert.Equal(new[] { "staff", "lyrics" }, CompletionLabelsAt(text, text.Length));
    }

    [Theory]
    [InlineData("part melody { section A {\n  ")]        // music, six tokens deep in the file
    [InlineData("part melody { section A { voice 1 {\n  ")]
    [InlineData("phrase lick {\n  ")]
    public void ALongerWalkDoesNotTurnOtherBracesIntoScores(string opener)
    {
        // The walk is bounded by the grammar's longest header AND stops at the first token
        // that cannot be part of one, so a brace opened by something else stays music even
        // with a `score` written earlier in the document.
        string text = Parts + "score main { staff melody }\n" + opener;
        Assert.Equal(LilySharpLanguageServer.CompletionContext.MusicBlock,
            LilySharpLanguageServer.GetCompletionContext(text, text.Length));
        Assert.Contains("c", CompletionLabelsAt(text, text.Length));
    }
}
