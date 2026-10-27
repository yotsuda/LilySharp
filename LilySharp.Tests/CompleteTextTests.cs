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

namespace LilySharp.Tests;

/// <summary>
/// lilysharp/completeText — the completion of a text held outside any editor (owner's request
/// 2026-10-27: completion in the field of Edit Bars Beside the Scan): the editor's items at the
/// same place, made plain for a text field.
/// </summary>
[Trait("Category", "Unit")]
public class CompleteTextTests
{
    private const string Text = "time 4/4\npart p1 { clef treble }\nsection A {\n  p1 {\n    c'4@ d'4 e'2 |\n  }\n}\nform { A }\nscore { staff p1 }\n";

    [Fact]
    public void ItOffersWhatTheEditorOffers_AtTheSamePlace()
    {
        int offset = Text.IndexOf("@ d'", StringComparison.Ordinal) + 1;
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///complete-text.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = Text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(Text, offset);
        var editor = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position { Line = line, Character = character },
            Context = new CompletionContext { TriggerKind = 2, TriggerCharacter = "@" },
        });
        var field = server.CompleteText(new CompleteTextParams { Text = Text, Offset = offset, Trigger = "@" });
        Assert.NotNull(editor);
        Assert.NotEmpty(field.Items);
        Assert.Equal(editor!.Items.Select(i => i.Label), field.Items.Select(i => i.Label));
        // Each replaces from no later than the caret to no earlier than it.
        Assert.All(field.Items, i => Assert.True(i.Start <= offset && i.End >= offset));
        Assert.Contains(field.Items, i => i.Label.Contains("fermata"));
    }

    [Fact]
    public void AnItemWithNoRange_ReplacesTheWordBeforeTheCaret()
    {
        string text = Text.Replace("c'4@ d'4", "c'4@ferm d'4");
        int offset = text.IndexOf("ferm", StringComparison.Ordinal) + 4;
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var items = server.CompleteText(new CompleteTextParams { Text = text, Offset = offset }).Items;
        Assert.All(items.Where(i => i.Start != i.End || i.Start != offset), i => Assert.True(i.End == offset));
    }

    [Theory]
    [InlineData("transpose $1", "transpose ", 10)]
    [InlineData("@fingering(${1:1})$0", "@fingering(1)", 11)]
    [InlineData("clef ${1|treble,bass|}", "clef treble", 5)]
    [InlineData("a \\$ b$0", "a $ b", 5)]
    [InlineData("plain", "plain", 5)]
    public void ASnippet_IsWrittenPlain_WithTheCaretAtItsFirstStop(string snippet, string text, int caret)
    {
        Assert.Equal((text, caret), LilySharpLanguageServer.PlainSnippet(snippet));
    }
}
