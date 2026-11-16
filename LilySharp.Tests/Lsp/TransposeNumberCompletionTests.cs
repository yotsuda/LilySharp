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
/// The top-level <c>transpose</c> through the editor's own doors (owner's request 2026-10-17 for
/// <c>shift</c>, retired into <c>transpose</c> 2026-11-16): the completion REQUEST offers the
/// word at a top-level caret and the numbers after it, the step request moves a number, and a
/// <c>shift</c> left in a file is rewritten by its quick fix. <c>‸</c> marks the caret.
/// </summary>
[Trait("Category", "Unit")]
public class TransposeNumberCompletionTests
{
    private static readonly Uri DocUri = new("file:///transpose-number.lys");

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

    private const string Rest = "\npart m { clef treble }\nsection A { m { c'4 d e f | } }\nform { A }\nscore { staff m }\n";

    [Theory]
    [InlineData("‸")]
    [InlineData("tr‸")]
    [InlineData("key c major\n‸")]
    [InlineData("time 4/4\nkey c major\nt‸")]
    public void ATopLevelCaret_IsOfferedTranspose_AndNotShift(string head)
    {
        var labels = Labels(head + Rest);
        Assert.Contains("transpose", labels);
        Assert.DoesNotContain("shift", labels);
    }

    [Fact]
    public void OnceWritten_ItIsNotOfferedAgain()
        => Assert.DoesNotContain("transpose", Labels("transpose +1\n‸" + Rest));

    [Fact]
    public void AfterTheWord_TheNumbersComeFirst_ThenThePitches()
    {
        var labels = Labels("transpose ‸" + Rest);
        Assert.Equal(["+1", "-1", "+2", "-2"], labels.Take(4));
        Assert.Contains("+11", labels);
        Assert.Contains("bes,", labels);
        Assert.DoesNotContain("part", labels);
    }

    [Fact]
    public void TheStepRequest_MovesANumber_KeepingItsSpelling()
    {
        var (server, text, offset) = Open("key c major\ntranspose ‸+5 flat" + Rest);
        var response = server.Step(new StepParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Selections = [new StepSelection { Start = offset, End = offset }],
            Direction = 1,
            Semitone = true,
        });
        Assert.False(response.Fallback);
        var edit = Assert.Single(response.Edits);
        Assert.Equal("+6", edit.NewText);
        Assert.Equal("transpose +6: key c major -> ges major", response.Message);
    }

    [Fact]
    public void AShiftLeftInAFile_IsRewrittenByItsQuickFix()
    {
        var (server, text, _) = Open("key c major\nshift -2‸" + Rest);
        int at = text.IndexOf("shift", StringComparison.Ordinal);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, at);
        var actions = server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Range = new LilySharp.Lsp.Protocol.Range(new Position(line, character), new Position(line, character)),
            Context = new CodeActionContext(),
        }) ?? [];
        var fix = Assert.Single(actions, a => a.Title == "Write 'transpose -2'");
        var edit = Assert.Single(fix.Edit!.Changes![DocUri.ToString()]);
        Assert.Equal("transpose", edit.NewText);
    }
}
