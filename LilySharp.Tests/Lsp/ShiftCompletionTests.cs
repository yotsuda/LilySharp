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
/// <c>shift</c> through the editor's own doors (owner's request 2026-10-17: "make shift
/// completable; support Ctrl+Shift+Up on it"): the completion REQUEST at a top-level caret
/// offers it — typed from nothing, from its first letters, under a <c>key</c> — and the step
/// request moves it. <c>‸</c> marks the caret.
/// </summary>
[Trait("Category", "Unit")]
public class ShiftCompletionTests
{
    private static readonly Uri DocUri = new("file:///shift.lys");

    private static (LilySharpLanguageServer Server, string Text, int Offset) Open(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return (server, text, offset);
    }

    private static string[] Labels(string marked)
    {
        var (server, text, offset) = Open(marked);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return [.. (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Position = new Position(line, character),
        })?.Items ?? []).Select(i => i.Label!)];
    }

    private const string Rest = "\npart m { clef treble }\nsection A { m { c4 d e f | } }\nform { A }\nscore { staff m }\n";

    [Theory]
    [InlineData("‸")]
    [InlineData("sh‸")]
    [InlineData("key c major\n‸")]
    [InlineData("key c major\nsh‸")]
    [InlineData("time 4/4\nkey c major\ns‸")]
    public void ATopLevelCaret_IsOfferedShift(string head)
        => Assert.Contains("shift", Labels(head + Rest));

    [Fact]
    public void OnceWritten_ItIsNotOfferedAgain()
        => Assert.DoesNotContain("shift", Labels("shift +1\n‸" + Rest));

    [Fact]
    public void AfterTheWord_NothingOfTheTopLevelIsOffered()
    {
        // `shift ` wants a number; the top-level keywords would be noise there.
        var labels = Labels("shift ‸" + Rest);
        Assert.DoesNotContain("part", labels);
        Assert.DoesNotContain("transpose", labels);
    }

    [Fact]
    public void TheStepRequest_MovesIt()
    {
        var (server, text, offset) = Open("key c major\nshift ‸+1" + Rest);
        var response = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Selections = [new StepSelection { Start = offset, End = offset }],
            Direction = 1,
            Semitone = true,
        });
        Assert.False(response.Fallback);
        var edit = Assert.Single(response.Edits);
        Assert.Equal("+2", edit.NewText);
        Assert.Equal("shift +2: key c major -> d major", response.Message);
    }
}
