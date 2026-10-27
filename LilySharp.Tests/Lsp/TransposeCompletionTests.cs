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
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// <c>transpose</c> and <c>transposition</c> through the completion REQUEST (owner's request
/// 2026-10-26: "after completing transpose or transposition, open the next word's
/// completion"): the caret the item leaves — the word and a space — is offered the values.
/// <c>‸</c> marks the caret.
/// </summary>
[Trait("Category", "Unit")]
public class TransposeCompletionTests
{
    private static readonly Uri DocUri = new("file:///transpose.lys");

    private static string[] Labels(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return [.. (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Position = new Position(line, character),
        })?.Items ?? []).Select(i => i.Label!)];
    }

    private const string Rest = "\nsection A { m { c'4 d e f | } }\nform { A }\nscore { staff m }\n";

    [Theory]
    [InlineData("transpose ‸\npart m { clef treble }" + Rest)]
    [InlineData("part m { clef treble transpose ‸ }" + Rest)]
    [InlineData("part m { clef treble }" + Rest + "score other transpose ‸ { staff m }\n")]
    public void AfterTranspose_TheTargetsAreOffered(string marked)
    {
        var labels = Labels(marked);
        Assert.Contains("bes,", labels);
        Assert.Contains("d", labels);
        Assert.DoesNotContain("part", labels);
    }

    [Fact]
    public void AfterTransposition_TheMarkersAreOffered()
        => Assert.Equal(["8va", "8vb", "15ma", "15mb"], Labels("part m { clef treble_8 transposition ‸ }" + Rest));

    [Fact]
    public void AfterShift_NoPitchIsOffered()
        => Assert.Empty(Labels("shift ‸\npart m { clef treble }" + Rest));
}
