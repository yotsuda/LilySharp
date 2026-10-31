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
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The editor's side of the chord rewrites (owner's requests 2026-10-31): "Add Guitar Diagram" on a
/// <c>&lt;…&gt;</c> chord and "Convert to Chord Notes" on a chord(…) / diagram(…) item — offered as
/// commands with the document and an offset, answered by lilysharp/addChordDiagram and
/// lilysharp/shapeToNotes with the span to replace.
/// </summary>
public class ChordRewriteLspTests
{
    private static readonly System.Uri Uri = new("file:///chords.lys");

    private static string Book(string music) => $$"""
        part gt { instrument guitar }
        section A { gt { {{music}} } }
        form { A }
        score { staff gt }
        """;

    private static LilySharpLanguageServer Open(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = Uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server;
    }

    private static CodeAction[] ActionsAt(LilySharpLanguageServer server, string text, int offset)
    {
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var at = new Position(line, character);
        return server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = Uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? [];
    }

    private static string Apply(string text, ShapeToNotesResponse r)
    {
        Assert.True(r.Replacement != null, r.Error);
        return text[..r.Start] + r.Replacement + text[r.End..];
    }

    [Theory]
    [InlineData("<f a c>1@chord(F) |", "<f a c>1@diagram(xx321x)@chord(F) |", "@chord(F")]
    [InlineData("<f a c>1@chord(F) |", "<f a c>1@diagram(xx321x)@chord(F) |", "<f a")]
    [InlineData("<f a c>1 |", "<f a c>1@diagram(xx321x) |", "<f a")]
    [InlineData("f1 |", "f1@diagram(xx3xxx) |", "f1")]
    public void AddGuitarDiagram_IsOffered_AndTheRequestWritesTheMark(string music, string expected, string caret)
    {
        string text = Book(music);
        var server = Open(text);
        var action = Assert.Single(ActionsAt(server, text, text.IndexOf(caret, System.StringComparison.Ordinal)),
            a => a.Title == "Add Guitar Diagram");
        Assert.Equal("lilysharp.addChordDiagram", action.Command!.CommandIdentifier);
        int offset = System.Convert.ToInt32(action.Command.Arguments![1]);
        var response = server.AddChordDiagram(new ShapeToNotesParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = Uri },
            Offset = offset,
        });
        Assert.Equal(Book(expected), Apply(text, response));
    }

    [Fact]
    public void ConvertToChordNotes_IsOffered_AndTheRequestWritesTheNotes()
    {
        string text = Book("chord(F 133211)1 |");
        var server = Open(text);
        var action = Assert.Single(ActionsAt(server, text, text.IndexOf("chord(F", System.StringComparison.Ordinal)),
            a => a.Title.StartsWith("Convert to Chord Notes", System.StringComparison.Ordinal));
        int offset = System.Convert.ToInt32(action.Command!.Arguments![1]);
        var response = server.ShapeToNotes(new ShapeToNotesParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = Uri },
            Offset = offset,
        });
        Assert.Contains(@"<f,\6 c\5 f'\4 a'\3 c'\2 f''\1>1@chord(F) |", Apply(text, response));
    }

    [Fact]
    public void ConvertToChordSymbol_IsOffered_AndTheRequestWritesTheItem()
    {
        string text = Book(@"<f,\6 c\5 f'\4 a'\3 c'\2 f''\1>1@chord(F) |");
        var server = Open(text);
        var action = Assert.Single(ActionsAt(server, text, text.IndexOf("<f", System.StringComparison.Ordinal)),
            a => a.Title.StartsWith("Convert to Chord Symbol", System.StringComparison.Ordinal));
        Assert.Equal("lilysharp.convertNotesToChord", action.Command!.CommandIdentifier);
        int offset = System.Convert.ToInt32(action.Command.Arguments![1]);
        var response = server.NotesToChord(new ShapeToNotesParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = Uri },
            Offset = offset,
        });
        Assert.Equal(Book("chord(F 133211)1 |"), Apply(text, response));
    }

    [Fact]
    public void AddGuitarDiagram_IsNotOffered_OnANoteOfAPartWithNoFrets()
    {
        string text = """
            part fl { instrument flute }
            section A { fl { f''1 | } }
            form { A }
            score { staff fl }
            """;
        var server = Open(text);
        Assert.DoesNotContain(ActionsAt(server, text, text.IndexOf("f''1", System.StringComparison.Ordinal)),
            a => a.Title == "Add Guitar Diagram");
    }
}